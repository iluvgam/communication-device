using System.Net.WebSockets;
using System.Text.Json;
namespace FriendsChat;
public record Packet(string Kind,string Room="",string Password="",string Name="",string Text="");
public static class Protocol {
 public static bool ValidRoom(string? s)=>s!=null&&s.Length>=3&&s.Length<=32&&s.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-');
 public static bool ValidName(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=32&&!s.Any(char.IsControl);
 public static bool ValidPassword(string? s)=>s!=null&&s.Length>=10&&s.Length<=128&&!s.Any(char.IsControl);
 public static async Task<Packet?> Read(WebSocket socket,CancellationToken ct){
  using var data=new MemoryStream();byte[] buffer=new byte[4096];
  while(true){var r=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),ct);if(r.MessageType==WebSocketMessageType.Close)return null;if(r.MessageType!=WebSocketMessageType.Text||data.Length+r.Count>16384)throw new IOException("Invalid message size or type.");data.Write(buffer,0,r.Count);if(r.EndOfMessage)break;}
  return JsonSerializer.Deserialize<Packet>(data.ToArray())??throw new IOException("Invalid message.");
 }
 public static Task Write(WebSocket socket,Packet p,CancellationToken ct)=>socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(p)),WebSocketMessageType.Text,true,ct);
}
public sealed class RelayClient:IDisposable {
 readonly ClientWebSocket socket=new();readonly SemaphoreSlim send=new(1);readonly CancellationTokenSource stop=new();
 public static Uri ValidateUrl(string value){if(!Uri.TryCreate(value.Trim(),UriKind.Absolute,out var u)||u.Scheme!="https"||!string.IsNullOrEmpty(u.UserInfo)||!string.IsNullOrEmpty(u.Query)||!string.IsNullOrEmpty(u.Fragment)||u.AbsolutePath!="/")throw new IOException("Enter the relay's HTTPS address, such as https://your-relay.example.com");return new UriBuilder(u){Scheme="wss",Path="/chat"}.Uri;}
 public async Task Connect(string url,bool create,string room,string password,string name){
  if(!Protocol.ValidRoom(room))throw new IOException("Room name: 3–32 letters, numbers or hyphens.");if(!Protocol.ValidPassword(password))throw new IOException("Use a password of 10–128 characters.");if(!Protocol.ValidName(name))throw new IOException("Enter a display name (1–32 characters).");
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(90));
  await socket.ConnectAsync(ValidateUrl(url),timeout.Token);
  await Protocol.Write(socket,new Packet(create?"create":"join",room.ToLowerInvariant(),password,name),timeout.Token);
  var reply=await Protocol.Read(socket,timeout.Token);if(reply?.Kind!="ready")throw new IOException(reply?.Text??"Relay disconnected.");
 }
 public Task<Packet?> Read()=>Protocol.Read(socket,stop.Token);
 public async Task Send(string text){if(string.IsNullOrWhiteSpace(text)||text.Length>2000)throw new IOException("Message must be 1–2,000 characters.");await send.WaitAsync(stop.Token);try{await Protocol.Write(socket,new Packet("send",Text:text),stop.Token);}finally{send.Release();}}
 public void Dispose(){stop.Cancel();socket.Abort();socket.Dispose();}
}
