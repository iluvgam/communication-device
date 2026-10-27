using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace FriendsChat;
public record Packet(string Kind, string Name = "", string Text = "", string Code = "");
public sealed class Wire : IDisposable {
 readonly TcpClient tcp; readonly SslStream ssl; readonly SemaphoreSlim gate = new(1); 
 public Wire(TcpClient t, SslStream s) { tcp=t; ssl=s; }
 public async Task Send(Packet p) {
  byte[] b=JsonSerializer.SerializeToUtf8Bytes(p); if(b.Length>32768) throw new IOException("Message too large.");
  await gate.WaitAsync(); try { await ssl.WriteAsync(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(b.Length))); await ssl.WriteAsync(b); } finally {gate.Release();}
 }
 public async Task<Packet> Read(CancellationToken ct) {
  byte[] h=new byte[4]; await ssl.ReadExactlyAsync(h,ct); int n=IPAddress.NetworkToHostOrder(BitConverter.ToInt32(h));
  if(n<1 || n>32768) throw new IOException("Invalid packet size.");
  byte[] b=new byte[n]; await ssl.ReadExactlyAsync(b,ct); return JsonSerializer.Deserialize<Packet>(b) ?? throw new IOException("Invalid packet.");
 }
 public void Dispose(){ssl.Dispose();tcp.Dispose();}
}
public sealed class Room : IDisposable {
 public const int Port=45831;
 readonly X509Certificate2 cert; readonly TcpListener listener; readonly CancellationTokenSource stop=new();
 readonly ConcurrentDictionary<Wire,string> peers=new(); readonly SemaphoreSlim broadcast=new(1);
 int pending;
 public string Code {get;}=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
 public string Fingerprint => Convert.ToHexString(SHA256.HashData(cert.RawData));
 public int BoundPort => ((IPEndPoint)listener.LocalEndpoint).Port;
 public Room(int port=Port) {
  using var rsa=RSA.Create(2048); var req=new CertificateRequest("CN=FriendsChat",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
  using var tmp=req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddDays(1));
  cert=X509CertificateLoader.LoadPkcs12(tmp.Export(X509ContentType.Pfx),null,X509KeyStorageFlags.DefaultKeySet);
  listener=new TcpListener(IPAddress.Any,port); listener.Start(16); _=Accept();
 }
 async Task Accept(){try {while(!stop.IsCancellationRequested){var t=await listener.AcceptTcpClientAsync(stop.Token); if(Interlocked.Increment(ref pending)>16){Interlocked.Decrement(ref pending);t.Dispose();continue;} _=Serve(t);}}catch(OperationCanceledException){}catch(ObjectDisposedException){} }
 async Task Serve(TcpClient t){Wire? w=null; try {
  var s=new SslStream(t.GetStream(),false); w=new Wire(t,s);
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
  await s.AuthenticateAsServerAsync(new SslServerAuthenticationOptions{ServerCertificate=cert,EnabledSslProtocols=SslProtocols.Tls12|SslProtocols.Tls13},timeout.Token);
  var p=await w.Read(timeout.Token);
  if(p.Kind!="hello" || p.Code.Length!=Code.Length || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(p.Code),Encoding.UTF8.GetBytes(Code)) || !ValidName(p.Name)) {await w.Send(new Packet("error",Text:"Room code or display name was rejected."));return;}
  peers[w]=p.Name; await w.Send(new Packet("ready"));
  await Broadcast(new Packet("message","Room",p.Name+" joined."));
  while(!stop.IsCancellationRequested){p=await w.Read(stop.Token); if(p.Kind!="send" || string.IsNullOrWhiteSpace(p.Text) || p.Text.Length>2000) throw new IOException("Invalid message."); await Broadcast(new Packet("message",peers[w],p.Text));}
 }catch(Exception e) when(e is IOException or AuthenticationException or OperationCanceledException or SocketException or ObjectDisposedException or JsonException){}
 finally {if(w!=null){peers.TryRemove(w,out _);w.Dispose();} t.Dispose();Interlocked.Decrement(ref pending);}}
 public static bool ValidName(string n)=>!string.IsNullOrWhiteSpace(n)&&n.Length<=32&&!n.Any(char.IsControl);
 async Task Broadcast(Packet p){await broadcast.WaitAsync();try{foreach(var w in peers.Keys){try{await w.Send(p).WaitAsync(TimeSpan.FromSeconds(5));}catch{peers.TryRemove(w,out _);w.Dispose();}}}finally{broadcast.Release();}}
 public void Dispose(){stop.Cancel();listener.Stop();foreach(var w in peers.Keys)w.Dispose();cert.Dispose();}
}
public sealed class Client : IDisposable {
 readonly Wire wire; readonly CancellationTokenSource stop=new();
 Client(Wire w){wire=w;}
 public static async Task<Client> Connect(string ip,int port,string name,string code,string fingerprint){
  if(!Room.ValidName(name))throw new IOException("Enter a display name (1–32 characters).");
  fingerprint=fingerprint.Replace(" ","").Replace(":","").Trim().ToUpperInvariant();
  if(fingerprint.Length!=64 || !fingerprint.All(Uri.IsHexDigit))throw new IOException("Copy the host's full 64-character fingerprint first.");
  var t=new TcpClient(); Wire? w=null;
  try {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));await t.ConnectAsync(IPAddress.Parse(ip),port,timeout.Token);
   var s=new SslStream(t.GetStream(),false,(_,cert,_,_)=>{ return cert!=null && Convert.ToHexString(SHA256.HashData(cert.GetRawCertData()))==fingerprint;});
   w=new Wire(t,s);await s.AuthenticateAsClientAsync(new SslClientAuthenticationOptions{TargetHost="FriendsChat",EnabledSslProtocols=SslProtocols.Tls12|SslProtocols.Tls13},timeout.Token);
   await w.Send(new Packet("hello",name,Code:code.Trim().ToUpperInvariant())); var p=await w.Read(timeout.Token);if(p.Kind!="ready")throw new IOException(p.Text.Length>0?p.Text:"Host rejected connection.");return new Client(w);
  }catch{w?.Dispose();t.Dispose();throw;}
 }
 public Task Send(string text)=>wire.Send(new Packet("send",Text:text));
 public Task<Packet> Read()=>wire.Read(stop.Token);
 public void Dispose(){stop.Cancel();wire.Dispose();}
}



