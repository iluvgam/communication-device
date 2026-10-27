using FriendsChat;
using System.Net;
var results=new List<string>();
void Pass(string s){results.Add("PASS: "+s);Console.WriteLine(results[^1]);}
using var next=new Room(0);
var invitation=new Invitation("172.20.10.7",next.Code,next.Fingerprint);
if(Invitation.Parse(invitation.Encode())!=invitation)throw new Exception("Invitation round trip failed");Pass("Invitation preserves address, code and fingerprint");
foreach(var bad in new[]{"junk","friendschat:v2:!!!!",new Invitation("127.0.0.1",next.Code,next.Fingerprint).Encode(),new Invitation("45831",next.Code,next.Fingerprint).Encode(),new Invitation("172.20.10.7","bad",next.Fingerprint).Encode()}){try{Invitation.Parse(bad);throw new Exception("Invalid invitation accepted");}catch(FormatException){}}Pass("Malformed and loopback invitations rejected");
using(var discoverHost=new DiscoveryHost("Local test",next.Fingerprint)){
 var found=await LocalDiscovery.Find(new[]{IPAddress.Loopback});
 if(found.Count!=1||found[0].Name!="Local test"||found[0].Fingerprint!=next.Fingerprint)throw new Exception("Discovery failed");Pass("UDP loopback discovery finds room and fingerprint");
}
var none=await LocalDiscovery.Find(new[]{IPAddress.Loopback});if(none.Count!=0)throw new Exception("Discovery did not stop");Pass("Discovery stops when host is disposed");
if(await LocalDiscovery.Check("45831"))throw new Exception("Port interpreted as address");Pass("Connection check rejects a port in place of IPv4");
Packet? received=null;next.Message+=p=>received=p;await next.Post("Host","Local hello");if(received?.Text!="Local hello")throw new Exception("Local hosting failed");Pass("Host sends and receives locally without network loopback");
try{await next.Post("Host",new string('x',2001));throw new Exception("Oversized message accepted");}catch(IOException){Pass("Host message length limit enforced");}
Directory.CreateDirectory("artifacts");File.WriteAllLines("artifacts/remaster-test-results.txt",results);
