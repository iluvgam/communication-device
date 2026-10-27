using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
namespace FriendsChat;

public record Invitation(string Address, string Code, string Fingerprint) {
 public string Encode()=>"friendschat:v2:"+Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this));
 public static Invitation Parse(string value){
  try {
   const string prefix="friendschat:v2:";
   if(value.Length>2048||!value.Trim().StartsWith(prefix))throw new FormatException();
   var i=JsonSerializer.Deserialize<Invitation>(Convert.FromBase64String(value.Trim()[prefix.Length..])) ?? throw new FormatException();
   if(i.Address==null||i.Address.Count(c=>c=='.')!=3||!IPAddress.TryParse(i.Address,out var ip)||ip.AddressFamily!=AddressFamily.InterNetwork||IPAddress.IsLoopback(ip)||i.Code==null||i.Code.Length!=32||!i.Code.All(Uri.IsHexDigit)||i.Fingerprint==null||i.Fingerprint.Length!=64||!i.Fingerprint.All(Uri.IsHexDigit))throw new FormatException();
   return i;
  }catch(Exception e) when(e is FormatException or JsonException or ArgumentException){throw new FormatException("This is not a valid Friends Chat invitation. Copy the entire invitation from the host.");}
 }
}
public record Nearby(string Name,string Address,string Fingerprint){public override string ToString()=>$"{Name} — {Address} (unverified)";}
public record DiscoveryReply(string App,string Nonce,string Name,string Fingerprint);
public sealed class DiscoveryHost:IDisposable {
 public const int Port=45832;
 readonly UdpClient udp; readonly CancellationTokenSource stop=new();
 public DiscoveryHost(string name,string fingerprint){udp=new UdpClient(new IPEndPoint(IPAddress.Any,Port));_=Respond(name,fingerprint);}
 async Task Respond(string name,string fingerprint){try{while(!stop.IsCancellationRequested){var r=await udp.ReceiveAsync(stop.Token);if(r.Buffer.Length>100)continue;var query=Encoding.ASCII.GetString(r.Buffer);if(!query.StartsWith("FRIENDSCHAT_FIND_V2 "))continue;var nonce=query["FRIENDSCHAT_FIND_V2 ".Length..];if(nonce.Length!=32||!nonce.All(Uri.IsHexDigit))continue;byte[] b=JsonSerializer.SerializeToUtf8Bytes(new DiscoveryReply("FriendsChat2",nonce,name,fingerprint));await udp.SendAsync(b,r.RemoteEndPoint,stop.Token);}}catch(Exception e) when(e is OperationCanceledException or ObjectDisposedException or SocketException){}}
 public void Dispose(){stop.Cancel();udp.Dispose();}
}
public static class LocalDiscovery {
 public static List<(string Label,IPAddress Address,IPAddress Broadcast)> Interfaces(){
  var found=new List<(string,IPAddress,IPAddress)>();
  foreach(var n in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback))
   foreach(var a in n.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork)){
    var ip=a.Address.GetAddressBytes();var mask=a.IPv4Mask.GetAddressBytes();var broadcast=new IPAddress(ip.Select((v,i)=>(byte)(v|~mask[i])).ToArray());found.Add(($"{n.Name}: {a.Address}",a.Address,broadcast));
   }
  return found;
 }
 public static async Task<List<Nearby>> Find(IEnumerable<IPAddress>? destinations=null){
  using var udp=new UdpClient(new IPEndPoint(IPAddress.Any,0)){EnableBroadcast=true};using var timer=new CancellationTokenSource(TimeSpan.FromSeconds(3));
  string nonce=Guid.NewGuid().ToString("N");byte[] query=Encoding.ASCII.GetBytes("FRIENDSCHAT_FIND_V2 "+nonce);
  foreach(var target in (destinations??Interfaces().Select(i=>i.Broadcast).Append(IPAddress.Broadcast)).Distinct())try{await udp.SendAsync(query,new IPEndPoint(target,DiscoveryHost.Port),timer.Token);}catch(SocketException){}
  var found=new Dictionary<string,Nearby>();
  try {while(found.Count<32){var r=await udp.ReceiveAsync(timer.Token);if(r.Buffer.Length>1024)continue;try{var p=JsonSerializer.Deserialize<DiscoveryReply>(r.Buffer);if(p?.App=="FriendsChat2"&&p.Nonce==nonce&&Room.ValidName(p.Name)&&p.Fingerprint?.Length==64&&p.Fingerprint.All(Uri.IsHexDigit))found[r.RemoteEndPoint.Address.ToString()]=new Nearby(p.Name,r.RemoteEndPoint.Address.ToString(),p.Fingerprint);}catch(JsonException){}}}catch(OperationCanceledException){}catch(SocketException){}
  return found.Values.ToList();
 }
 public static async Task<bool> Check(string address){using var tcp=new TcpClient();using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(4));try{if(address.Count(c=>c=='.')!=3||!IPAddress.TryParse(address,out var ip)||ip.AddressFamily!=AddressFamily.InterNetwork)return false;await tcp.ConnectAsync(ip,Room.Port,ct.Token);return true;}catch(Exception e) when(e is SocketException or OperationCanceledException){return false;}}
}



