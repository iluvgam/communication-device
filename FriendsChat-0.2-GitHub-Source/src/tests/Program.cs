using FriendsChat;
using System.Security.Authentication;
var results=new List<string>();
void Pass(string s){results.Add("PASS: "+s);Console.WriteLine(results[^1]);}
using var room=new Room(0);
using var a=await Client.Connect("127.0.0.1",room.BoundPort,"Alice",room.Code,room.Fingerprint);
await a.Read().WaitAsync(TimeSpan.FromSeconds(3));
using var b=await Client.Connect("127.0.0.1",room.BoundPort,"Bob",room.Code,room.Fingerprint);
await a.Read().WaitAsync(TimeSpan.FromSeconds(3));await b.Read().WaitAsync(TimeSpan.FromSeconds(3));
await a.Send("Hello Bob 👋");var am=await a.Read();var bm=await b.Read();if(am.Text!=bm.Text||bm.Name!="Alice"||bm.Text!="Hello Bob 👋")throw new Exception("Broadcast failed");Pass("Two TLS clients receive authenticated sender and Unicode message");
await b.Send("Hello Alice");if((await a.Read()).Name!="Bob"||(await b.Read()).Text!="Hello Alice")throw new Exception("Reply failed");Pass("Bidirectional messaging");
try{using var bad=await Client.Connect("127.0.0.1",room.BoundPort,"Eve",room.Code,new string('0',64));throw new Exception("Accepted bad fingerprint");}catch(AuthenticationException){Pass("Wrong host fingerprint rejected before room authentication");}
try{using var bad=await Client.Connect("127.0.0.1",room.BoundPort,"Eve",new string('0',32),room.Fingerprint);throw new Exception("Accepted bad code");}catch(IOException){Pass("Wrong room code rejected");}
try{using var bad=await Client.Connect("127.0.0.1",room.BoundPort,"Eve",room.Code,"");throw new Exception("Accepted missing fingerprint");}catch(IOException){Pass("Missing fingerprint rejected");}
a.Dispose();using var c=await Client.Connect("127.0.0.1",room.BoundPort,"Carol",room.Code,room.Fingerprint);await c.Read();await b.Read();await c.Send("Still here");if((await b.Read()).Text!="Still here")throw new Exception("Reconnect failed");Pass("Remaining room works after participant disconnect and new join");
var oldFingerprint=room.Fingerprint;room.Dispose();try{await b.Read().WaitAsync(TimeSpan.FromSeconds(3));throw new Exception("Room did not close");}catch(IOException){Pass("Stopping host closes participant connection");}
using var next=new Room(0);if(next.Code==room.Code||next.Fingerprint==oldFingerprint)throw new Exception("Reused credentials");Pass("New room rotates code and certificate");
var invitation=new Invitation("172.20.10.7",next.Code,next.Fingerprint);
if(Invitation.Parse(invitation.Encode())!=invitation)throw new Exception("Invitation round trip failed");Pass("Single invitation round trip preserves address, code and fingerprint");
foreach(var badInvite in new[]{"junk", "friendschat:v2:!!!!", new Invitation("127.0.0.1",next.Code,next.Fingerprint).Encode(),new Invitation("45831",next.Code,next.Fingerprint).Encode(),new Invitation("172.20.10.7","bad",next.Fingerprint).Encode()}){try{Invitation.Parse(badInvite);throw new Exception("Invalid invitation accepted");}catch(FormatException){}}
Pass("Malformed invitations and loopback addresses rejected");
using(var discoverHost=new DiscoveryHost("Local test",next.Fingerprint)){
 var found=await LocalDiscovery.Find(new[]{System.Net.IPAddress.Loopback});
 if(found.Count!=1||found[0].Name!="Local test"||found[0].Fingerprint!=next.Fingerprint)throw new Exception("Discovery failed");Pass("UDP discovery finds hosting room via loopback without sharing room code");
}
if(await LocalDiscovery.Check("45831"))throw new Exception("Invalid IP reachable");Pass("Reachability check rejects invalid IPv4 input");
Directory.CreateDirectory("artifacts"); File.WriteAllLines("artifacts/test-results.txt",results);


