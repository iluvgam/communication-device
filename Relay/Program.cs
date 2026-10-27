using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using FriendsChat;
using Microsoft.AspNetCore.RateLimiting;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k=>k.Limits.MaxRequestBodySize=16384);
builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("connections",ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=20,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));});
var app=builder.Build();var rooms=new ConcurrentDictionary<string,ChatRoom>();using var authSlots=new SemaphoreSlim(2);int connections=0;
app.UseRateLimiter();app.UseWebSockets(new WebSocketOptions{KeepAliveInterval=TimeSpan.FromSeconds(25)});
app.MapGet("/",()=>Results.Text("Friends Chat relay 0.3 is running. Use this HTTPS address in FriendsChat.exe. Rooms and messages are temporary."));
app.MapGet("/health",()=>Results.Ok(new{status="ok",version="0.3"}));
app.Map("/chat",async context=>{
 if(!context.WebSockets.IsWebSocketRequest){context.Response.StatusCode=400;return;}
 if(Interlocked.Increment(ref connections)>128){Interlocked.Decrement(ref connections);context.Response.StatusCode=503;return;}
 Peer? peer=null;ChatRoom? room=null;bool owner=false;
 try{
  using var socket=await context.WebSockets.AcceptWebSocketAsync();peer=new Peer(socket);
  using var authTimeout=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);authTimeout.CancelAfter(TimeSpan.FromSeconds(30));
  var hello=await Protocol.Read(socket,authTimeout.Token);
  if(hello==null||!Protocol.ValidRoom(hello.Room)||!Protocol.ValidName(hello.Name)||!Protocol.ValidPassword(hello.Password)||hello.Kind is not ("create" or "join")){await peer.Send(new Packet("error",Text:"Check your room name, display name and password (10–128 characters)."));return;}
  string key=hello.Room.ToLowerInvariant();peer.Name=hello.Name;
  await authSlots.WaitAsync(authTimeout.Token);
  try{
   if(hello.Kind=="create"){
    if(rooms.Count>=64){await peer.Send(new Packet("error",Text:"Relay is full. Try later."));return;}
    var candidate=new ChatRoom(hello.Password);
    if(!rooms.TryAdd(key,candidate)){await peer.Send(new Packet("error",Text:"That room already exists. Choose another name or use Join room."));return;}
    room=candidate;owner=true;
   }else{
    if(!rooms.TryGetValue(key,out room)||!room.CheckPassword(hello.Password)){await peer.Send(new Packet("error",Text:"Room name or password is incorrect."));room=null;return;}
   }
  }finally{authSlots.Release();}
  // Queue the ready response before making this socket visible to broadcasts.
  await peer.Send(new Packet("ready"));
  if(!room.Add(peer)){await peer.Send(new Packet("error",Text:"Room is closed or full."));return;}
  await room.Broadcast(new Packet("message",Name:"Room",Text:peer.Name+" joined."));
  var last=DateTime.UtcNow;int count=0;
  while(socket.State==WebSocketState.Open){
   var p=await Protocol.Read(socket,context.RequestAborted);if(p==null)break;
   if(p.Kind!="send"||string.IsNullOrWhiteSpace(p.Text)||p.Text.Length>2000)throw new IOException("Invalid message.");
   if((DateTime.UtcNow-last).TotalSeconds>=10){last=DateTime.UtcNow;count=0;}if(++count>30){await peer.Send(new Packet("error",Text:"Too many messages. Reconnect in a moment."));break;}
   await room.Broadcast(new Packet("message",Name:peer.Name,Text:p.Text));
  }
 }catch(Exception e) when(e is IOException or WebSocketException or OperationCanceledException or System.Text.Json.JsonException or ObjectDisposedException){}
 finally{
  if(room!=null&&peer!=null){room.Remove(peer);if(owner){var entry=rooms.FirstOrDefault(x=>ReferenceEquals(x.Value,room));if(entry.Key!=null)rooms.TryRemove(entry.Key,out _);room.Close();}}
  peer?.Abort();Interlocked.Decrement(ref connections);
 }
}).RequireRateLimiting("connections");
await app.RunAsync();

sealed class Peer(WebSocket socket){
 readonly SemaphoreSlim gate=new(1);public string Name="";
 public async Task Send(Packet p){await gate.WaitAsync();try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));await Protocol.Write(socket,p,timeout.Token);}finally{gate.Release();}}
 public void Abort(){try{socket.Abort();}catch(ObjectDisposedException){}}
}
sealed class ChatRoom{
 readonly byte[] salt=RandomNumberGenerator.GetBytes(16);readonly byte[] hash;readonly object sync=new();readonly HashSet<Peer> peers=[];readonly SemaphoreSlim broadcast=new(1);bool closed;
 public ChatRoom(string password){hash=Derive(password);}
 byte[] Derive(string p)=>Rfc2898DeriveBytes.Pbkdf2(p,salt,600000,HashAlgorithmName.SHA256,32);
 public bool CheckPassword(string p){var candidate=Derive(p);try{return CryptographicOperations.FixedTimeEquals(hash,candidate);}finally{CryptographicOperations.ZeroMemory(candidate);}}
 public bool Add(Peer p){lock(sync){return !closed&&peers.Count<16&&peers.Add(p);}}
 public void Remove(Peer p){lock(sync)peers.Remove(p);}
 public async Task Broadcast(Packet p){await broadcast.WaitAsync();try{Peer[] list;lock(sync)list=peers.ToArray();foreach(var peer in list){try{await peer.Send(p);}catch{Remove(peer);peer.Abort();}}}finally{broadcast.Release();}}
 public void Close(){lock(sync){closed=true;foreach(var p in peers)p.Abort();peers.Clear();}}
}
