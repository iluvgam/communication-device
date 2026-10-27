using System.Net.WebSockets;
using FriendsChat;
var results=new List<string>();
void Pass(string s){results.Add("PASS: "+s);Console.WriteLine(results[^1]);}
using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(50));
async Task<ClientWebSocket> Connect(Packet p){var s=new ClientWebSocket();await s.ConnectAsync(new Uri("ws://127.0.0.1:45903/chat"),deadline.Token);await Protocol.Write(s,p,deadline.Token);return s;}
async Task<Packet> Read(ClientWebSocket s){var p=await Protocol.Read(s,deadline.Token);return p??throw new Exception("Unexpected close");}
const string secret="Correct horse test room";
using var a=await Connect(new Packet("create","test-room",secret,"Alice"));if((await Read(a)).Kind!="ready")throw new Exception("Create failed");await Read(a);Pass("Create password-protected room");
using(var duplicate=await Connect(new Packet("create","test-room",secret,"Other"))){if((await Read(duplicate)).Kind!="error")throw new Exception("Duplicate room accepted");}Pass("Duplicate room rejected");
using(var wrong=await Connect(new Packet("join","test-room","Wrong password here","Eve"))){if((await Read(wrong)).Kind!="error")throw new Exception("Wrong password accepted");}Pass("Wrong password rejected");
using(var absent=await Connect(new Packet("join","absent-room",secret,"Eve"))){if((await Read(absent)).Kind!="error")throw new Exception("Missing room accepted");}Pass("Unknown room rejected");
using(var shortPassword=await Connect(new Packet("create","short-room","tiny","Eve"))){if((await Read(shortPassword)).Kind!="error")throw new Exception("Short password accepted");}Pass("Short password rejected");
using var b=await Connect(new Packet("join","TEST-ROOM",secret,"Bob"));if((await Read(b)).Kind!="ready")throw new Exception("Join failed");await Read(a);await Read(b);Pass("Join room case-insensitively with correct password");
await Protocol.Write(a,new Packet("send",Name:"Spoof",Text:"Hello Bob 👋"),deadline.Token);var am=await Read(a);var bm=await Read(b);if(am.Text!="Hello Bob 👋"||bm.Text!=am.Text||bm.Name!="Alice")throw new Exception("Broadcast or identity failure");Pass("Unicode broadcast and server-assigned sender name");
await Protocol.Write(b,new Packet("send",Text:"Reply"),deadline.Token);if((await Read(a)).Name!="Bob"||(await Read(b)).Text!="Reply")throw new Exception("Reply failed");Pass("Bidirectional messaging");
using(var oversized=await Connect(new Packet("join","test-room",secret,"Large"))){await Read(oversized);await Read(oversized);await Read(a);await Read(b);await Protocol.Write(oversized,new Packet("send",Text:new string('x',2001)),deadline.Token);try{var p=await Protocol.Read(oversized,deadline.Token);if(p?.Kind=="message")throw new Exception("Oversized message broadcast");}catch(WebSocketException){}}Pass("Oversized chat message rejected");
a.Abort();try{var p=await Protocol.Read(b,deadline.Token);if(p!=null)throw new Exception("Room stayed open");}catch(WebSocketException){}Pass("Creator disconnect closes participants");
await Task.Delay(200);
using var recreated=await Connect(new Packet("create","test-room","A new password today","Carol"));if((await Read(recreated)).Kind!="ready")throw new Exception("Recreate failed");await Read(recreated);Pass("Closed room can be recreated without previous history");
foreach(var url in new[]{"http://example.com","https://user:pass@example.com","https://example.com?token=secret","not-a-url"}){try{RelayClient.ValidateUrl(url);throw new Exception("Bad URL accepted");}catch(IOException){}}if(RelayClient.ValidateUrl("https://example.com").Scheme!="wss")throw new Exception("Secure URL conversion failed");Pass("Production client enforces HTTPS and secure WebSockets");
Directory.CreateDirectory("artifacts");File.WriteAllLines("artifacts/v0.3-tests.txt",results);
