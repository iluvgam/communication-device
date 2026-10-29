using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace FriendsChat;
// Id/Seq/Total/Data are only used by media packets; older relays and clients ignore unknown fields.
public record Packet(string Kind,string Room="",string Password="",string Name="",string Text="",string Id="",int Seq=0,long Total=0,string Data="");
// Images and videos travel as media-start, numbered media-chunk packets (base64, under the 16 KiB packet limit), then media-end.
// The relay checks and forwards each piece to the others; it never stores media.
public static class Media {
 public const int ChunkBytes=9216;
 public const long MaxImage=5L*1024*1024,MaxVideo=25L*1024*1024;
 public static readonly string[] ImageTypes=["image/jpeg","image/png","image/gif"],VideoTypes=["video/mp4","video/webm"];
 public static bool IsImage(string? mime)=>mime!=null&&ImageTypes.Contains(mime);
 public static bool IsVideo(string? mime)=>mime!=null&&VideoTypes.Contains(mime);
 public static long Limit(string? mime)=>IsImage(mime)?MaxImage:IsVideo(mime)?MaxVideo:0;
 public static bool ValidId(string? id)=>id!=null&&id.Length==16&&id.All(c=>c is >='0' and <='9' or >='a' and <='f');
 public static string NewId()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
 // By file extension; null means "not a supported image or video".
 public static string? MimeFor(string path)=>Path.GetExtension(path).ToLowerInvariant() switch{".jpg" or ".jpeg"=>"image/jpeg",".png"=>"image/png",".gif"=>"image/gif",".bmp"=>"image/bmp",".mp4" or ".m4v"=>"video/mp4",".webm"=>"video/webm",_=>null};
 public static string Size(long bytes)=>bytes>=1024*1024?$"{bytes/1024d/1024:0.0} MB":$"{Math.Max(1,bytes/1024)} KB";
}
// Built-in stickers travel as just their id; each app draws them itself.
public static class Stickers {
 public static readonly string[] Ids=["hi","lol","wow","love","yes","no","gg","brb","party","sad","coffee","orb"];
}
public enum Field{None,Relay,Name,Room,Password}
public static class Protocol {
 public const string RoomRule="Room name: 3–32 letters, numbers or hyphens.",NameRule="Display name: 1–32 characters.",PasswordRule="Password: 10–128 characters.";
 public static bool ValidRoom(string? s)=>s!=null&&s.Length>=3&&s.Length<=32&&s.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-');
 public static bool ValidName(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=32&&!s.Any(char.IsControl);
 public static bool ValidPassword(string? s)=>s!=null&&s.Length>=10&&s.Length<=128&&!s.Any(char.IsControl);
 // Spaces in a typed room name become hyphens: "friday friends" -> "friday-friends".
 public static string NormalizeRoom(string s)=>Regex.Replace(s.Trim(),@"\s+","-");
 // First invalid field in form order, with a short message for that field only.
 public static (Field Field,string Message) Check(string relay,string name,string room,string password){
  try{RelayClient.ValidateUrl(relay);}catch(IOException e){return (Field.Relay,e.Message);}
  if(!ValidName(name.Trim()))return (Field.Name,NameRule);
  if(!ValidRoom(NormalizeRoom(room)))return (Field.Room,RoomRule);
  if(!ValidPassword(password))return (Field.Password,PasswordRule);
  return (Field.None,"");
 }
 // "Sam is typing…", "Sam and Jo are typing…", "Several people are typing…".
 public static string DescribeTyping(IReadOnlyList<string> names)=>names.Count switch{0=>"",1=>$"{names[0]} is typing…",2=>$"{names[0]} and {names[1]} are typing…",_=>"Several people are typing…"};
 public static async Task<Packet?> Read(WebSocket socket,CancellationToken ct){
  using var data=new MemoryStream();byte[] buffer=new byte[4096];
  while(true){var r=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),ct);if(r.MessageType==WebSocketMessageType.Close)return null;if(r.MessageType!=WebSocketMessageType.Text||data.Length+r.Count>16384)throw new IOException("Invalid message size or type.");data.Write(buffer,0,r.Count);if(r.EndOfMessage)break;}
  return JsonSerializer.Deserialize<Packet>(data.ToArray())??throw new IOException("Invalid message.");
 }
 public static Task Write(WebSocket socket,Packet p,CancellationToken ct)=>socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(p)),WebSocketMessageType.Text,true,ct);
}
// Saved only when the user ticks "Remember". There is deliberately no password field.
public record Settings(string Relay="",string Name="",bool Sound=true,bool StartupSound=true){
 public static readonly Settings Empty=new();
 public static string DefaultPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"3DFx","settings.json");
 public static Settings Load(string path){try{var s=JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));return s==null||s.Relay==null||s.Name==null||s.Relay.Length>300||s.Name.Length>32?Empty:s;}catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){return Empty;}}
 public void Save(string path){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,JsonSerializer.Serialize(this));}
}
public sealed class RelayClient:IDisposable {
 // Pings every 15 s; a missing reply within 15 s ends the connection so a dropped network is noticed.
 readonly ClientWebSocket socket=new(){Options={KeepAliveInterval=TimeSpan.FromSeconds(15),KeepAliveTimeout=TimeSpan.FromSeconds(15)}};readonly SemaphoreSlim send=new(1);readonly CancellationTokenSource stop=new();
 // Accepts "name.onrender.com", a trailing "/", "/chat" or "/health". Still HTTPS only, no user info, query or fragment.
 public static Uri ValidateUrl(string value){var v=value.Trim();if(v.Length>0&&!v.Contains("://"))v="https://"+v;
  if(!Uri.TryCreate(v,UriKind.Absolute,out var u)||u.Scheme!="https"||!u.Host.Contains('.')||!string.IsNullOrEmpty(u.UserInfo)||!string.IsNullOrEmpty(u.Query)||!string.IsNullOrEmpty(u.Fragment)||u.AbsolutePath.TrimEnd('/') is not (""or"/chat"or"/health"))throw new IOException("Relay address: use https://your-relay.onrender.com");
  return new UriBuilder(u){Scheme="wss",Path="/chat"}.Uri;}
 public async Task Connect(string url,bool create,string room,string password,string name){
  var (field,problem)=Protocol.Check(url,name,room,password);if(field!=Field.None)throw new IOException(problem);
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(90));
  await socket.ConnectAsync(ValidateUrl(url),timeout.Token);
  await Protocol.Write(socket,new Packet(create?"create":"join",Protocol.NormalizeRoom(room).ToLowerInvariant(),password,name.Trim()),timeout.Token);
  var reply=await Protocol.Read(socket,timeout.Token);if(reply?.Kind!="ready")throw new IOException(reply?.Text??"Relay disconnected.");
  Features=reply.Text.Split(' ',StringSplitOptions.RemoveEmptyEntries);
 }
 // What the relay supports, from its "ready" reply (e.g. "nudge typing"). A 0.4 relay sends none; never send it packets it would reject.
 public string[] Features{get;private set;}=[];
 public Task<Packet?> Read()=>Protocol.Read(socket,stop.Token);
 public async Task Signal(string kind){if(!Features.Contains(kind))return;await Write(new Packet(kind));}
 public async Task SendSticker(string id){if(Features.Contains("stickers"))await Write(new Packet("sticker",Text:id));}
 // Sends one image or video in pieces. Each piece takes the send lock on its own, so chat messages can slip in between.
 public async Task SendMedia(byte[] data,string mime,IProgress<int>? progress=null){
  if(!Features.Contains("media"))throw new IOException("This relay does not support pictures and videos yet.");
  if(data.Length==0||data.Length>Media.Limit(mime))throw new IOException(Media.IsVideo(mime)?"Videos can be up to 25 MB.":"Pictures can be up to 5 MB.");
  var id=Media.NewId();await Write(new Packet("media-start",Text:mime,Id:id,Total:data.Length));
  for(int seq=0,offset=0;offset<data.Length;seq++,offset+=Media.ChunkBytes){
   await Write(new Packet("media-chunk",Id:id,Seq:seq,Data:Convert.ToBase64String(data,offset,Math.Min(Media.ChunkBytes,data.Length-offset))));
   progress?.Report((int)(100L*Math.Min(data.Length,offset+Media.ChunkBytes)/data.Length));}
  await Write(new Packet("media-end",Id:id));}
 async Task Write(Packet p){await send.WaitAsync(stop.Token);try{await Protocol.Write(socket,p,stop.Token);}finally{send.Release();}}
 public async Task Send(string text){if(string.IsNullOrWhiteSpace(text)||text.Length>2000)throw new IOException("Message must be 1–2,000 characters.");await send.WaitAsync(stop.Token);try{await Protocol.Write(socket,new Packet("send",Text:text),stop.Token);}finally{send.Release();}}
 // Says goodbye with a WebSocket close so the relay sees the leave at once; a bare abort takes ~10 s to pass through Render's proxy.
 public async Task Close(){try{using var t=new CancellationTokenSource(TimeSpan.FromSeconds(1.5));await send.WaitAsync(t.Token).ConfigureAwait(false);try{if(socket.State==WebSocketState.Open)await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,null,t.Token).ConfigureAwait(false);}finally{send.Release();}
  // Wait (up to the 1.5 s) for the relay's side to end, as seen by the receive loop; aborting right away lets the proxy drop the close.
  while(socket.State==WebSocketState.CloseSent)await Task.Delay(50,t.Token).ConfigureAwait(false);}catch(Exception e) when(e is WebSocketException or OperationCanceledException or ObjectDisposedException){}finally{Dispose();}}
 public void Dispose(){stop.Cancel();socket.Abort();socket.Dispose();}
}
