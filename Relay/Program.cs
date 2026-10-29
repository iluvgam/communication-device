using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using FriendsChat;
using Microsoft.AspNetCore.RateLimiting;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k=>k.Limits.MaxRequestBodySize=16384);
// Default 20 per IP per minute; local tests raise it with --ConnectionsPerMinute=500.
int perMinute=builder.Configuration.GetValue("ConnectionsPerMinute",20);
builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("connections",ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=perMinute,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));});
var app=builder.Build();var rooms=new ConcurrentDictionary<string,ChatRoom>();using var authSlots=new SemaphoreSlim(2);int connections=0;
// A ping without a pong reply within 20 s ends the connection, so dropped clients leave promptly.
app.UseRateLimiter();app.UseWebSockets(new WebSocketOptions{KeepAliveInterval=TimeSpan.FromSeconds(20),KeepAliveTimeout=TimeSpan.FromSeconds(20)});
app.MapGet("/",()=>Results.Text("3DFx Vista relay 0.7 is running. Use this HTTPS address in 3DFxVista.exe. Rooms and messages are temporary."));
app.MapGet("/health",()=>Results.Ok(new{status="ok",version="0.7"}));
app.Map("/chat",async context=>{
 if(!context.WebSockets.IsWebSocketRequest){context.Response.StatusCode=400;return;}
 if(Interlocked.Increment(ref connections)>128){Interlocked.Decrement(ref connections);context.Response.StatusCode=503;return;}
 Peer? peer=null;ChatRoom? room=null;bool joined=false;string key="";string? mediaId=null;
 try{
  using var socket=await context.WebSockets.AcceptWebSocketAsync();peer=new Peer(socket);
  // Sends the reason, closes cleanly and waits briefly for the client's close reply so the reason is not lost.
  async Task Reject(string reason){await peer.Close(reason);await Peer.Drain(socket);}
  using var authTimeout=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);authTimeout.CancelAfter(TimeSpan.FromSeconds(30));
  var hello=await Protocol.Read(socket,authTimeout.Token);if(hello==null)return;
  var problem=hello.Kind is not ("create" or "join")?"Unsupported request.":!Protocol.ValidRoom(hello.Room)?Protocol.RoomRule:!Protocol.ValidName(hello.Name)?Protocol.NameRule:!Protocol.ValidPassword(hello.Password)?Protocol.PasswordRule:null;
  if(problem!=null){await Reject(problem);return;}
  key=hello.Room.ToLowerInvariant();peer.Name=hello.Name.Trim();
  string? denied=null;
  await authSlots.WaitAsync(authTimeout.Token);
  try{
   if(hello.Kind=="create"){
    var candidate=rooms.Count>=64?null:new ChatRoom(hello.Password);
    if(candidate==null)denied="Relay is full. Try later.";
    else if(!rooms.TryAdd(key,candidate))denied="That room already exists. Choose another name or use Join room.";
    else room=candidate;
   }else if(!rooms.TryGetValue(key,out room)||!room.CheckPassword(hello.Password)){room=null;denied="Room name or password is incorrect.";}
  }finally{authSlots.Release();}
  // Rejected outside the password-check slots so a slow close cannot hold one.
  if(denied!=null||room==null){await Reject(denied??"Room is closed.");return;}
  // Join sends "ready" only once the peer is in the room, so a full or closing room never says ready first.
  if(!await room.Join(peer)){await Reject("Room is closed or full.");return;}
  joined=true;
  await room.Announce(peer.Name+" joined.");
  var last=DateTime.UtcNow;int count=0,packets=0;DateTime lastTyping=DateTime.MinValue,lastNudge=DateTime.MinValue;
  // One media transfer at a time per person, at most 100 MB per 10 minutes. Pieces are checked and forwarded, never stored.
  long mediaTotal=0,mediaGot=0,mediaBudget=0;int mediaSeq=0;var mediaWindow=DateTime.UtcNow;var chunk=new byte[Media.ChunkBytes];
  while(socket.State==WebSocketState.Open){
   var p=await Protocol.Read(socket,context.RequestAborted);if(p==null)break;
   if((DateTime.UtcNow-last).TotalSeconds>=10){last=DateTime.UtcNow;count=packets=0;}
   // Media pieces are bounded by the announced size instead of the packet count (a 25 MB video is ~2,800 pieces).
   if(p.Kind=="media-chunk"){
    if(mediaId==null||p.Id!=mediaId||p.Seq!=mediaSeq||p.Data is not {Length:<=12288}||!Convert.TryFromBase64String(p.Data,chunk,out int n)||n==0||mediaGot+n>mediaTotal)throw new IOException("Invalid media.");
    mediaSeq++;mediaGot+=n;await room.Broadcast(new Packet("media-chunk",Name:peer.Name,Id:mediaId,Seq:p.Seq,Data:p.Data),except:peer);continue;}
   if(p.Kind=="media-end"){
    if(mediaId==null||p.Id!=mediaId||mediaGot!=mediaTotal)throw new IOException("Invalid media.");
    await room.Broadcast(new Packet("media-end",Name:peer.Name,Id:mediaId),except:peer);mediaId=null;continue;}
   // Any packet kind counts here, so typing/nudge floods cannot bypass the message limits.
   if(++packets>200){await Reject("Too many messages. Disconnected.");break;}
   // Typing: forwarded to the others at most once per 2 s per person; extras are dropped quietly.
   if(p.Kind=="typing"){if((DateTime.UtcNow-lastTyping).TotalSeconds>=2){lastTyping=DateTime.UtcNow;await room.Broadcast(new Packet("typing",Name:peer.Name),except:peer);}continue;}
   // Nudge: shakes the others' windows; one per 15 s per person.
   if(p.Kind=="nudge"){if((DateTime.UtcNow-lastNudge).TotalSeconds<15){await peer.Send(new Packet("error",Text:"Wait a moment before nudging again."));continue;}lastNudge=DateTime.UtcNow;await room.Broadcast(new Packet("nudge",Name:peer.Name),except:peer);continue;}
   if(p.Kind=="media-start"){
    if((DateTime.UtcNow-mediaWindow).TotalMinutes>=10){mediaWindow=DateTime.UtcNow;mediaBudget=0;}
    if(mediaId!=null||!Media.ValidId(p.Id)||p.Total<=0||p.Total>Media.Limit(p.Text))throw new IOException("Invalid media.");
    if(mediaBudget+p.Total>100L*1024*1024){await peer.Send(new Packet("error",Text:"Too many pictures and videos. Try again in a few minutes."));continue;}
    mediaBudget+=p.Total;mediaId=p.Id;mediaTotal=p.Total;mediaGot=0;mediaSeq=0;
    await room.Broadcast(new Packet("media-start",Name:peer.Name,Text:p.Text,Id:mediaId,Total:mediaTotal),except:peer);continue;}
   // Stickers are just an id from the built-in list; everyone (sender included) gets them like a chat message.
   var sticker=p.Kind=="sticker";
   if(sticker?!Stickers.Ids.Contains(p.Text):p.Kind!="send"||string.IsNullOrWhiteSpace(p.Text)||p.Text.Length>2000)throw new IOException("Invalid message.");
   // Over 30 per 10 s: drop the message but stay connected (a creator pasting lines must not close the room). Over 90: disconnect.
   if(++count>90){await Reject("Too many messages. Disconnected.");break;}
   if(count>30){await peer.Send(new Packet("error",Text:"Slow down. Message not sent."));continue;}
   await room.Broadcast(new Packet(sticker?"sticker":"message",Name:peer.Name,Text:p.Text));
  }
 }catch(Exception e) when(e is IOException or WebSocketException or OperationCanceledException or System.Text.Json.JsonException or ObjectDisposedException){}
 finally{
  try{
   // The room lives while anyone is in it, creator or not; the last one out removes it.
   // A transfer cut short (disconnect or bad piece) is cancelled for the others before the leave notice.
   if(room!=null&&joined&&mediaId!=null){room.Remove(peer!);await room.Broadcast(new Packet("media-cancel",Name:peer!.Name,Id:mediaId));}
   if(room!=null){if(room.Remove(peer!))rooms.TryRemove(new KeyValuePair<string,ChatRoom>(key,room));else if(joined)await room.Announce(peer!.Name+" left.");}
  }finally{peer?.Abort();Interlocked.Decrement(ref connections);}
 }
}).RequireRateLimiting("connections");
await app.RunAsync();

sealed class Peer(WebSocket socket){
 readonly SemaphoreSlim gate=new(1);public string Name="";
 public async Task Send(Packet p){await gate.WaitAsync();try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));await Protocol.Write(socket,p,timeout.Token);}finally{gate.Release();}}
 // Tells the client why, then starts a normal WebSocket close. Falls back to abort.
 public async Task Close(string reason){try{await Send(new Packet("error",Text:reason));await gate.WaitAsync();try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,null,timeout.Token);}finally{gate.Release();}}catch{Abort();}}
 public static async Task Drain(WebSocket socket){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));try{while(await Protocol.Read(socket,timeout.Token)!=null){}}catch{}}
 public void Abort(){try{socket.Abort();}catch(ObjectDisposedException){}}
}
sealed class ChatRoom{
 readonly byte[] salt=RandomNumberGenerator.GetBytes(16);readonly byte[] hash;readonly object sync=new();readonly List<Peer> peers=[];readonly SemaphoreSlim broadcast=new(1);bool closed;
 public ChatRoom(string password){hash=Derive(password);}
 byte[] Derive(string p)=>Rfc2898DeriveBytes.Pbkdf2(p,salt,600000,HashAlgorithmName.SHA256,32);
 public bool CheckPassword(string p){var candidate=Derive(p);try{return CryptographicOperations.FixedTimeEquals(hash,candidate);}finally{CryptographicOperations.ZeroMemory(candidate);}}
 // Holds the broadcast lock so no room message can reach the peer before "ready".
 public async Task<bool> Join(Peer p){await broadcast.WaitAsync();try{lock(sync){if(closed||peers.Count>=16||peers.Contains(p))return false;peers.Add(p);}try{await p.Send(new Packet("ready",Text:Features));}catch{Remove(p);throw;}return true;}finally{broadcast.Release();}}
 // Returns true when this left the room empty; an empty room is closed for good so a late joiner cannot revive it.
 public bool Remove(Peer p){lock(sync){peers.Remove(p);if(peers.Count==0)closed=true;return closed;}}
 // Room notice followed by the current member list (names in join order, one per line).
 public async Task Announce(string text){await Broadcast(new Packet("message",Name:"Room",Text:text));string names;lock(sync)names=string.Join('\n',peers.Select(x=>x.Name));await Broadcast(new Packet("members",Text:names));}
 // Optional extras this relay supports, sent in "ready" so clients only use what is there (0.4 clients ignore it).
 public const string Features="nudge typing media stickers";
 public async Task Broadcast(Packet p,Peer? except=null){await broadcast.WaitAsync();try{Peer[] list;lock(sync)list=peers.ToArray();foreach(var peer in list){if(peer==except)continue;try{await peer.Send(p);}catch{Remove(peer);peer.Abort();}}}finally{broadcast.Release();}}
}
