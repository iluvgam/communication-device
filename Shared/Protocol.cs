using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace FriendsChat;
public record Packet(string Kind,string Room="",string Password="",string Name="",string Text="");
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
 public static async Task<Packet?> Read(WebSocket socket,CancellationToken ct){
  using var data=new MemoryStream();byte[] buffer=new byte[4096];
  while(true){var r=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),ct);if(r.MessageType==WebSocketMessageType.Close)return null;if(r.MessageType!=WebSocketMessageType.Text||data.Length+r.Count>16384)throw new IOException("Invalid message size or type.");data.Write(buffer,0,r.Count);if(r.EndOfMessage)break;}
  return JsonSerializer.Deserialize<Packet>(data.ToArray())??throw new IOException("Invalid message.");
 }
 public static Task Write(WebSocket socket,Packet p,CancellationToken ct)=>socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(p)),WebSocketMessageType.Text,true,ct);
}
// Saved only when the user ticks "Remember". There is deliberately no password field.
public record Settings(string Relay="",string Name="",bool Sound=true){
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
 }
 public Task<Packet?> Read()=>Protocol.Read(socket,stop.Token);
 public async Task Send(string text){if(string.IsNullOrWhiteSpace(text)||text.Length>2000)throw new IOException("Message must be 1–2,000 characters.");await send.WaitAsync(stop.Token);try{await Protocol.Write(socket,new Packet("send",Text:text),stop.Token);}finally{send.Release();}}
 public void Dispose(){stop.Cancel();socket.Abort();socket.Dispose();}
}
