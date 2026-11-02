using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using FriendsChat;
// Loopback relay tests. Start the relay first:
//   dotnet run --project Relay/Relay.csproj --urls http://127.0.0.1:45903 --ConnectionsPerMinute=500 --AccountsPerMinute=500 --AccountsDb=%TEMP%\3dfx-test-accounts.db --BotKey=test-bot-key-0123456789
// With accounts on, people without a sign-in show as "Name (guest)"; G() adds that suffix.
// With an https address argument, run only the short live check instead (Live.cs).
if(args is [var liveUrl])return await LiveSmoke.Run(liveUrl);
var results=new List<string>();int failed=0;
using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(300));
const string secret="Correct horse test room";
async Task Test(string name,Func<Task> body){try{await body();results.Add("PASS: "+name);}catch(Exception e){failed++;results.Add("FAIL: "+name+" — "+e.Message);}Console.WriteLine(results[^1]);}
void Expect(bool ok,string why){if(!ok)throw new Exception(why);}
string G(string name)=>name+Accounts.GuestSuffix;
async Task<ClientWebSocket> Open(){var s=new ClientWebSocket();await s.ConnectAsync(new Uri("ws://127.0.0.1:45903/chat"),deadline.Token);return s;}
async Task<ClientWebSocket> Connect(Packet p){var s=await Open();await Protocol.Write(s,p,deadline.Token);return s;}
// Skips "members" roster packets unless asked for them.
async Task<Packet?> TryRead(ClientWebSocket s,int seconds=10,bool members=false){using var t=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);t.CancelAfter(TimeSpan.FromSeconds(seconds));try{while(true){var p=await Protocol.Read(s,t.Token);if(p?.Kind!="members"||members)return p;}}catch(WebSocketException){return null;}}
async Task<Packet> Read(ClientWebSocket s)=>await TryRead(s)??throw new Exception("Unexpected close");
// Reads until the relay closes the socket; returns every packet received.
async Task<List<Packet>> ReadToClose(ClientWebSocket s){var all=new List<Packet>();while(await TryRead(s) is {} p)all.Add(p);return all;}
async Task<Packet> ReadMembers(ClientWebSocket s){while(true){var p=await TryRead(s,members:true)??throw new Exception("Unexpected close");if(p.Kind=="members")return p;}}
async Task<Packet> ReadUntil(ClientWebSocket s,Func<Packet,bool> match){while(true){var p=await Read(s);if(match(p))return p;}}
// Creates or joins and consumes "ready" plus the own "joined" notice.
async Task<ClientWebSocket> Enter(string kind,string room,string name,string password=secret){var s=await Connect(new Packet(kind,room,password,name));var r=await Read(s);Expect(r.Kind=="ready",$"{name}: expected ready, got {r.Kind} {r.Text}");await ReadUntil(s,p=>p.Text==G(name)+" joined.");return s;}
async Task SendRaw(ClientWebSocket s,byte[] data,WebSocketMessageType type)=>await s.SendAsync(data,type,true,deadline.Token);

// --- Original behavior ---
ClientWebSocket a=null!,b=null!;
await Test("Create password-protected room",async()=>{a=await Enter("create","test-room","Alice");});
await Test("Duplicate room rejected",async()=>{using var d=await Connect(new Packet("create","test-room",secret,"Other"));Expect((await Read(d)).Kind=="error","Duplicate room accepted");});
await Test("Wrong password rejected",async()=>{using var w=await Connect(new Packet("join","test-room","Wrong password here","Eve"));Expect((await Read(w)).Kind=="error","Wrong password accepted");});
await Test("Unknown room rejected",async()=>{using var u=await Connect(new Packet("join","absent-room",secret,"Eve"));Expect((await Read(u)).Kind=="error","Missing room accepted");});
await Test("Short password rejected",async()=>{using var s=await Connect(new Packet("create","short-room","tiny","Eve"));Expect((await Read(s)).Kind=="error","Short password accepted");});
await Test("Join room case-insensitively with correct password",async()=>{b=await Enter("join","TEST-ROOM","Bob");await ReadUntil(a,p=>p.Text==G("Bob")+" joined.");});
await Test("Unicode broadcast and server-assigned sender name",async()=>{await Protocol.Write(a,new Packet("send",Name:"Spoof",Text:"Hello Bob 👋"),deadline.Token);var am=await Read(a);var bm=await Read(b);Expect(am.Text=="Hello Bob 👋"&&bm.Text==am.Text&&bm.Name==G("Alice"),"Broadcast or identity failure");});
await Test("Bidirectional messaging",async()=>{await Protocol.Write(b,new Packet("send",Text:"Reply"),deadline.Token);Expect((await Read(a)).Name==G("Bob")&&(await Read(b)).Text=="Reply","Reply failed");});
await Test("Oversized chat message rejected",async()=>{using var o=await Enter("join","test-room","Large");await Protocol.Write(o,new Packet("send",Text:new string('x',2001)),deadline.Token);Expect(!(await ReadToClose(o)).Any(p=>p.Kind=="message"&&p.Name==G("Large")),"Oversized message broadcast");});
await Test("Leaving participant is announced",async()=>{await ReadUntil(b,p=>p.Text==G("Large")+" left.");await ReadUntil(a,p=>p.Text==G("Large")+" left.");});
await Test("Room stays open when the creator leaves",async()=>{a.Abort();await ReadUntil(b,p=>p.Text==G("Alice")+" left.");using var c=await Enter("join","test-room","Carol");await Protocol.Write(b,new Packet("send",Text:"still open"),deadline.Token);await ReadUntil(c,p=>p.Text=="still open");});
await Test("Empty room is removed and can be recreated",async()=>{b.Abort();await Task.Delay(300);using var r=await Enter("create","test-room","Dave","A new password today");});
await Test("Member list sent on join and leave",async()=>{using var o=await Enter("create","who-room","Owner");var m=await ReadMembers(o);Expect(m.Text==G("Owner"),"Got "+m.Text);using(var g=await Enter("join","who-room","Guest")){m=await ReadMembers(o);Expect(m.Text==G("Owner")+"\n"+G("Guest"),"Got "+m.Text);}m=await ReadMembers(o);Expect(m.Text==G("Owner"),"After leave got "+m.Text);});

// --- Nudge and typing (0.5 relay) ---
await Test("Relay announces its features",async()=>{using var s=await Connect(new Packet("create","feat-room",secret,"Feat"));var r=await Read(s);var features=r.Text.Split(' ');Expect(r.Kind=="ready"&&features.Contains("nudge")&&features.Contains("typing"),"Ready text: "+r.Text);});
await Test("Nudge reaches others, not the sender, and is rate-limited",async()=>{using var o=await Enter("create","nudge-room","Owner");using var g=await Enter("join","nudge-room","Guest");
 await Protocol.Write(o,new Packet("nudge"),deadline.Token);var n=await ReadUntil(g,p=>p.Kind=="nudge");Expect(n.Name==G("Owner"),"Name "+n.Name);
 await Protocol.Write(o,new Packet("nudge"),deadline.Token);var e=await ReadUntil(o,p=>p.Kind is "error" or "nudge");Expect(e.Kind=="error","Second nudge was not limited, or was echoed to the sender");
 await Protocol.Write(o,new Packet("send",Text:"after nudge"),deadline.Token);await ReadUntil(g,p=>p.Text=="after nudge");});
await Test("Typing reaches others once per burst and floods do not disconnect",async()=>{using var o=await Enter("create","type-room","Owner");using var g=await Enter("join","type-room","Guest");
 for(int i=0;i<50;i++)await Protocol.Write(o,new Packet("typing"),deadline.Token);await Protocol.Write(o,new Packet("send",Text:"done typing"),deadline.Token);
 var seen=new List<Packet>();while(true){var p=await Read(g);if(p.Text=="done typing")break;seen.Add(p);}
 Expect(seen.Count(p=>p.Kind=="typing")==1&&seen.First(p=>p.Kind=="typing").Name==G("Owner"),"Typing packets seen: "+seen.Count(p=>p.Kind=="typing"));
 var mine=await ReadUntil(o,p=>p.Kind=="typing"||p.Text=="done typing");Expect(mine.Kind!="typing","Typing echoed to sender");});
await Test("Packet flood disconnects only the flooder",async()=>{using var o=await Enter("create","flood-room","Owner");using var f=await Enter("join","flood-room","Flood");
 try{for(int i=0;i<260;i++)await Protocol.Write(f,new Packet("typing"),deadline.Token);}catch(WebSocketException){}
 await ReadUntil(o,p=>p.Text==G("Flood")+" left.");await Protocol.Write(o,new Packet("send",Text:"still here"),deadline.Token);await ReadUntil(o,p=>p.Text=="still here");});

// --- Media and stickers (0.7 relay) ---
async Task SendMediaRaw(ClientWebSocket s,string id,string mime,byte[] data,long? claimed=null){await Protocol.Write(s,new Packet("media-start",Text:mime,Id:id,Total:claimed??data.Length),deadline.Token);for(int seq=0,o=0;o<data.Length;seq++,o+=Media.ChunkBytes)await Protocol.Write(s,new Packet("media-chunk",Id:id,Seq:seq,Data:Convert.ToBase64String(data,o,Math.Min(Media.ChunkBytes,data.Length-o))),deadline.Token);await Protocol.Write(s,new Packet("media-end",Id:id),deadline.Token);}
async Task<byte[]> ReceiveMedia(ClientWebSocket s,string id){var start=await ReadUntil(s,p=>p.Kind=="media-start");Expect(start.Id==id,"Start id");using var buf=new MemoryStream();while(true){var p=await Read(s);if(p.Kind=="media-chunk"&&p.Id==id)buf.Write(Convert.FromBase64String(p.Data));else if(p.Kind=="media-end"&&p.Id==id)return buf.ToArray();else if(p.Kind=="media-cancel")throw new Exception("Cancelled");}}
byte[] Sample(int n){var b=new byte[n];new Random(n).NextBytes(b);return b;}
await Test("Relay announces media and stickers",async()=>{using var s=await Connect(new Packet("create","media-feat",secret,"Feat"));var r=await Read(s);var f=r.Text.Split(' ');Expect(f.Contains("media")&&f.Contains("stickers"),"Ready text: "+r.Text);});
await Test("Picture reaches others intact, not echoed to the sender",async()=>{using var o=await Enter("create","pic-room","Owner");using var g=await Enter("join","pic-room","Guest");var data=Sample(20000);var id=Media.NewId();
 await SendMediaRaw(o,id,"image/png",data);var got=await ReceiveMedia(g,id);Expect(got.SequenceEqual(data),"Bytes differ");
 await Protocol.Write(o,new Packet("send",Text:"after picture"),deadline.Token);var next=await ReadUntil(o,p=>p.Kind.StartsWith("media")||p.Text=="after picture");Expect(next.Text=="after picture","Media echoed to sender");});
await Test("Sender name comes from the relay",async()=>{using var o=await Enter("create","pic-name","Owner");using var g=await Enter("join","pic-name","Guest");var id=Media.NewId();await Protocol.Write(o,new Packet("media-start",Name:"Spoof",Text:"image/jpeg",Id:id,Total:10),deadline.Token);var s=await ReadUntil(g,p=>p.Kind=="media-start");Expect(s.Name==G("Owner"),"Name "+s.Name);});
await Test("Chat messages can pass between media pieces",async()=>{using var o=await Enter("create","mix-room","Owner");using var g=await Enter("join","mix-room","Guest");var data=Sample(Media.ChunkBytes*2);var id=Media.NewId();
 await Protocol.Write(o,new Packet("media-start",Text:"video/mp4",Id:id,Total:data.Length),deadline.Token);await Protocol.Write(o,new Packet("media-chunk",Id:id,Seq:0,Data:Convert.ToBase64String(data,0,Media.ChunkBytes)),deadline.Token);
 await Protocol.Write(o,new Packet("send",Text:"in between"),deadline.Token);await Protocol.Write(o,new Packet("media-chunk",Id:id,Seq:1,Data:Convert.ToBase64String(data,Media.ChunkBytes,Media.ChunkBytes)),deadline.Token);await Protocol.Write(o,new Packet("media-end",Id:id),deadline.Token);
 var kinds=new List<string>();while(kinds.LastOrDefault()!="media-end"){var p=await Read(g);if(p.Kind!="message"||p.Name!="Room")kinds.Add(p.Kind=="message"?p.Text:p.Kind);}
 Expect(string.Join(",",kinds)=="media-start,media-chunk,in between,media-chunk,media-end","Order: "+string.Join(",",kinds));});
await Test("Oversized picture rejected",async()=>{using var o=await Enter("create","big-room","Owner");using var b=await Enter("join","big-room","Big");await Protocol.Write(b,new Packet("media-start",Text:"image/jpeg",Id:Media.NewId(),Total:Media.MaxImage+1),deadline.Token);await ReadToClose(b);await ReadUntil(o,p=>p.Text==G("Big")+" left.");});
await Test("Video over 25 MB rejected",async()=>{using var o=await Enter("create","bigv-room","Owner");using var b=await Enter("join","bigv-room","Big");await Protocol.Write(b,new Packet("media-start",Text:"video/mp4",Id:Media.NewId(),Total:Media.MaxVideo+1),deadline.Token);await ReadToClose(b);await ReadUntil(o,p=>p.Text==G("Big")+" left.");});
await Test("Unsupported file type rejected",async()=>{using var o=await Enter("create","type-media","Owner");using var b=await Enter("join","type-media","Bad");await Protocol.Write(b,new Packet("media-start",Text:"text/html",Id:Media.NewId(),Total:100),deadline.Token);await ReadToClose(b);await ReadUntil(o,p=>p.Text==G("Bad")+" left.");});
await Test("More bytes than announced cancels the transfer",async()=>{using var o=await Enter("create","lie-room","Owner");using var b=await Enter("join","lie-room","Liar");var id=Media.NewId();
 try{await SendMediaRaw(b,id,"image/png",Sample(Media.ChunkBytes*2),claimed:Media.ChunkBytes);}catch(WebSocketException){}await ReadToClose(b);var c=await ReadUntil(o,p=>p.Kind=="media-cancel"||p.Text==G("Liar")+" left.");Expect(c.Kind=="media-cancel"&&c.Id==id,"No cancel before leave");});
await Test("Leaving mid-transfer cancels it for others",async()=>{using var o=await Enter("create","quit-room","Owner");var q=await Enter("join","quit-room","Quitter");var id=Media.NewId();
 await Protocol.Write(q,new Packet("media-start",Text:"video/mp4",Id:id,Total:Media.ChunkBytes*3),deadline.Token);await ReadUntil(o,p=>p.Kind=="media-start");q.Abort();var c=await ReadUntil(o,p=>p.Kind=="media-cancel"||p.Text==G("Quitter")+" left.");Expect(c.Kind=="media-cancel","Left without cancel");});
await Test("Sticker reaches everyone with the sender's name",async()=>{using var o=await Enter("create","stick-room","Owner");using var g=await Enter("join","stick-room","Guest");await Protocol.Write(o,new Packet("sticker",Text:"party"),deadline.Token);
 var a1=await ReadUntil(o,p=>p.Kind=="sticker");var a2=await ReadUntil(g,p=>p.Kind=="sticker");Expect(a1.Text=="party"&&a2.Text=="party"&&a2.Name==G("Owner"),"Sticker "+a2.Text+" from "+a2.Name);});
await Test("Unknown sticker rejected",async()=>{using var o=await Enter("create","stick-bad","Owner");using var b=await Enter("join","stick-bad","Bad");await Protocol.Write(b,new Packet("sticker",Text:"<script>"),deadline.Token);await ReadToClose(b);await ReadUntil(o,p=>p.Text==G("Bad")+" left.");});
await Test("Media pieces fit in a packet even with a long Unicode name",()=>{var p=new Packet("media-chunk",Name:new string('é',32),Id:Media.NewId(),Seq:int.MaxValue,Data:Convert.ToBase64String(new byte[Media.ChunkBytes]));Expect(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(p).Length<16384,"Too big");return Task.CompletedTask;});
await Test("Media helpers",()=>{Expect(Media.MimeFor("a.JPG")=="image/jpeg"&&Media.MimeFor("b.webm")=="video/webm"&&Media.MimeFor("c.exe")==null,"MimeFor");Expect(Media.Limit("image/png")==Media.MaxImage&&Media.Limit("video/mp4")==Media.MaxVideo&&Media.Limit("text/html")==0,"Limit");Expect(Media.ValidId(Media.NewId())&&!Media.ValidId("../etc")&&!Media.ValidId(null),"ValidId");return Task.CompletedTask;});

// --- Accounts (0.8 relay) ---
var acct=new AccountClient(new Uri("http://127.0.0.1:45903/"));
string U(string prefix)=>prefix+Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
async Task<AccountException> Fails(Func<Task> action){try{await action();}catch(AccountException e){return e;}throw new Exception("Expected the relay to refuse");}
const string pw1="first password 1",pw2="second password 2";
await Test("Relay announces accounts",async()=>{using var s=await Connect(new Packet("create",U("feat"),secret,"Feat"));var r=await Read(s);Expect(r.Text.Split(' ').Contains("accounts"),"Ready text: "+r.Text);});
await Test("Creating an account gives a session and a recovery key",async()=>{var u=U("alice");var r=await acct.SignUp(u,pw1);Expect(r.Username==u&&r.Token.Length>=40&&Accounts.ValidRecoveryKey(r.RecoveryKey)&&r.RecoveryKey.Length==23,"Reply "+r);var me=await acct.Me(r.Token);Expect(me.Username==u,"Me "+me.Username);});
await Test("Usernames are unique regardless of case",async()=>{var u=U("bob");await acct.SignUp(u,pw1);var e=await Fails(()=>acct.SignUp(u.ToUpperInvariant(),pw2));Expect(e.Status==409&&e.Message.Contains("taken"),e.Status+" "+e.Message);});
await Test("Account rules name the bad field",async()=>{var e1=await Fails(()=>acct.SignUp("x",pw1));var e2=await Fails(()=>acct.SignUp(U("carl"),"short"));var e3=await Fails(()=>acct.SignUp("bad name!",pw1));
 Expect(e1.Message.StartsWith("Username")&&e2.Message.StartsWith("Password")&&e3.Message.StartsWith("Username"),$"{e1.Message} | {e2.Message} | {e3.Message}");});
await Test("Sign in works; wrong password and unknown user get the same answer",async()=>{var u=U("dan");await acct.SignUp(u,pw1);var r=await acct.SignIn(u.ToUpperInvariant(),pw1);Expect(r.Username==u&&r.Token.Length>=40,"Sign in "+r);
 var wrong=await Fails(()=>acct.SignIn(u,"wrong password!"));var unknown=await Fails(()=>acct.SignIn(U("nobody"),pw1));Expect(wrong.Status==401&&unknown.Status==401&&wrong.Message==unknown.Message,wrong.Message+" | "+unknown.Message);});
await Test("Repeated wrong passwords make sign-in wait",async()=>{var u=U("dora");await acct.SignUp(u,pw1);for(int i=0;i<5;i++)await Fails(()=>acct.SignIn(u,"wrong password!"));var e=await Fails(()=>acct.SignIn(u,pw1));Expect(e.Status==429&&e.Message.Contains("Wait"),e.Status+" "+e.Message);});
await Test("A signed-in name in rooms comes from the account",async()=>{var u=U("erin");var r=await acct.SignUp(u,pw1);using var s=await Connect(new Packet("create",U("acct-room"),secret,"Spoofed",Token:r.Token));
 Expect((await Read(s)).Kind=="ready","Not ready");var joined=await ReadUntil(s,p=>p.Text.EndsWith(" joined."));var members=await ReadMembers(s);Expect(joined.Text==u+" joined."&&members.Text==u,joined.Text+" / "+members.Text);
 await Protocol.Write(s,new Packet("send",Text:"hi"),deadline.Token);var m=await ReadUntil(s,p=>p.Text=="hi");Expect(m.Name==u,"Sender "+m.Name);});
await Test("Guests are marked as guests",async()=>{using var s=await Enter("create",U("guest-room"),"Gus");var members=await ReadMembers(s);Expect(members.Text==G("Gus"),"Members "+members.Text);});
await Test("A fake or expired sign-in is refused, not turned into a guest",async()=>{using var s=await Connect(new Packet("create",U("fake-room"),secret,"Mallory",Token:"bm90LWEtcmVhbC10b2tlbi1hdC1hbGwtcmVhbGx5LW5vdA"));var p=await Read(s);Expect(p.Kind=="error"&&p.Text.Contains("Sign in again"),p.Kind+": "+p.Text);});
await Test("Sign out ends the session",async()=>{var r=await acct.SignUp(U("fay"),pw1);await acct.SignOut(r.Token);var e=await Fails(()=>acct.Me(r.Token));Expect(e.Status==401,"Status "+e.Status);});
await Test("Changing the password ends the other sessions",async()=>{var u=U("gail");var r=await acct.SignUp(u,pw1);var other=await acct.SignIn(u,pw1);var changed=await acct.ChangePassword(r.Token,pw1,pw2);
 await Fails(()=>acct.Me(other.Token));await Fails(()=>acct.Me(r.Token));Expect((await acct.Me(changed.Token)).Username==u,"New session");await Fails(()=>acct.SignIn(u,pw1));await acct.SignIn(u,pw2);
 var e=await Fails(()=>acct.ChangePassword(changed.Token,"wrong password!",pw1));Expect(e.Status==401,"Wrong current password accepted");});
await Test("The recovery key resets the password once",async()=>{var u=U("hank");var r=await acct.SignUp(u,pw1);var rec=await acct.Recover(u,r.RecoveryKey,pw2);
 Expect(Accounts.ValidRecoveryKey(rec.RecoveryKey)&&rec.RecoveryKey!=r.RecoveryKey&&rec.Token.Length>=40,"Reply "+rec);await Fails(()=>acct.Me(r.Token));await acct.SignIn(u,pw2);
 var e=await Fails(()=>acct.Recover(u,r.RecoveryKey,pw1));Expect(e.Status==401,"Old key reused: "+e.Status);});
await Test("The recovery key ignores dashes, spaces and case",async()=>{var u=U("ivy");var r=await acct.SignUp(u,pw1);await acct.Recover(u," "+r.RecoveryKey.Replace("-"," ").ToLowerInvariant()+" ",pw2);await acct.SignIn(u,pw2);});
await Test("Making a new recovery key retires the old one",async()=>{var u=U("jon");var r=await acct.SignUp(u,pw1);var fresh=await acct.NewRecoveryKey(r.Token,pw1);await Fails(()=>acct.Recover(u,r.RecoveryKey,pw2));await acct.Recover(u,fresh.RecoveryKey,pw2);});
await Test("Deleting an account needs the password and frees the username",async()=>{var u=U("kim");var r=await acct.SignUp(u,pw1);await Fails(()=>acct.Delete(r.Token,"wrong password!"));await acct.Delete(r.Token,pw1);
 await Fails(()=>acct.Me(r.Token));await Fails(()=>acct.SignIn(u,pw1));await acct.SignUp(u,pw2);});
await Test("Account helpers",()=>{Expect(Accounts.ValidUsername("Alice_99")&&Accounts.ValidUsername("a-b")&&!Accounts.ValidUsername("ab")&&!Accounts.ValidUsername("has space")&&!Accounts.ValidUsername(new string('a',21))&&!Accounts.ValidUsername("Ålice"),"Usernames");
 Expect(Accounts.NormalizeRecoveryKey("abcde-fghjk-mnpqr-stvwo")=="ABCDEFGHJKMNPQRSTVW0"&&Accounts.ValidRecoveryKey(Accounts.NewRecoveryKey())&&!Accounts.ValidRecoveryKey("too-short"),"Recovery keys");return Task.CompletedTask;});

// --- Direct messages (0.9 relay) ---
var dmUrl=new Uri("ws://127.0.0.1:45903/dm");
async Task<(DirectClient C,string User,string Token)> Online(string prefix){var u=U(prefix);var r=await acct.SignUp(u,pw1);var c=new DirectClient();await c.Connect(dmUrl,r.Token);return (c,u,r.Token);}
async Task<DirectClient> Device(string token){var c=new DirectClient();await c.Connect(dmUrl,token);return c;}
// A read that outlives a timeout is kept and reused, so no packet is swallowed by an abandoned read.
var pendingReads=new Dictionary<DirectClient,Task<Packet?>>();
async Task<Packet?> DmTry(DirectClient c,Func<Packet,bool> match,int seconds=10){var until=DateTime.UtcNow.AddSeconds(seconds);
 try{while(true){if(!pendingReads.TryGetValue(c,out var read))pendingReads[c]=read=c.Read();
  var left=until-DateTime.UtcNow;if(left<=TimeSpan.Zero||await Task.WhenAny(read,Task.Delay(left,deadline.Token))!=read)return null;
  pendingReads.Remove(c);var p=await read;if(p==null)return null;if(match(p))return p;}}
 catch(WebSocketException){pendingReads.Remove(c);return null;}}
async Task<Packet> DmNext(DirectClient c,Func<Packet,bool> match)=>await DmTry(c,match)??throw new Exception("Expected packet did not arrive");
async Task<List<ContactInfo>> ContactsUntil(DirectClient c,Func<List<ContactInfo>,bool> ok){while(true){var l=ContactInfo.Parse((await DmNext(c,p=>p.Kind=="contacts")).Text);if(ok(l))return l;}}
async Task Befriend(DirectClient a,string ua,DirectClient b,string ub){await a.Contact("add",ub);await ContactsUntil(b,l=>l.Any(x=>x.Name==ua&&x.State=="incoming"));await b.Contact("accept",ua);
 await ContactsUntil(a,l=>l.Any(x=>x.Name==ub&&x.State=="contact"));await ContactsUntil(b,l=>l.Any(x=>x.Name==ua&&x.State=="contact"));}
await Test("Relay announces direct messages",async()=>{using var s=await Connect(new Packet("create",U("dm-feat"),secret,"Feat"));var r=await Read(s);Expect(r.Text.Split(' ').Contains("direct"),"Ready text: "+r.Text);});
await Test("The DM connection needs a valid sign-in",async()=>{var dm=new ClientWebSocket();await dm.ConnectAsync(dmUrl,deadline.Token);await Protocol.Write(dm,new Packet("hello",Token:"bm90LWEtcmVhbC10b2tlbi1hdC1hbGwtcmVhbGx5LW5vdA"),deadline.Token);
 var p=await TryRead(dm);Expect(p?.Kind=="error"&&p.Text.Contains("Sign in again"),"Got "+p?.Kind+" "+p?.Text);dm.Dispose();});
await Test("Contact request, accept, and online status",async()=>{var (a,ua,_)=await Online("amy");var (b,ub,_)=await Online("ben");
 await a.Contact("add",ub);var la=await ContactsUntil(a,l=>l.Any(x=>x.Name==ub));Expect(la.Single(x=>x.Name==ub) is {State:"outgoing",Online:false},"A sees "+la.Single(x=>x.Name==ub));
 var lb=await ContactsUntil(b,l=>l.Any(x=>x.Name==ua));Expect(lb.Single(x=>x.Name==ua).State=="incoming","B sees "+lb.Single(x=>x.Name==ua));
 await b.Contact("accept",ua);la=await ContactsUntil(a,l=>l.Any(x=>x.Name==ub&&x.State=="contact"));Expect(la.Single(x=>x.Name==ub).Online,"B not shown online");a.Dispose();b.Dispose();});
await Test("Asking each other makes you contacts",async()=>{var (a,ua,_)=await Online("cal");var (b,ub,_)=await Online("dee");await a.Contact("add",ub);await ContactsUntil(b,l=>l.Any(x=>x.Name==ua));await b.Contact("add",ua);
 await ContactsUntil(a,l=>l.Any(x=>x.Name==ub&&x.State=="contact"));await ContactsUntil(b,l=>l.Any(x=>x.Name==ua&&x.State=="contact"));a.Dispose();b.Dispose();});
await Test("Unknown usernames and yourself can't be added",async()=>{var (a,ua,_)=await Online("eli");await a.Contact("add",U("nobody"));var e1=await DmNext(a,p=>p.Kind=="error");await a.Contact("add",ua);var e2=await DmNext(a,p=>p.Kind=="error");Expect(e1.Text.Length>0&&e2.Text.Length>0,"No errors");a.Dispose();});
await Test("Only contacts can be messaged",async()=>{var (a,_,_)=await Online("fin");var (b,ub,_)=await Online("gus");var id=await a.SendText(ub,"hi");var e=await DmNext(a,p=>p.Kind is "error" or "dm-sent");Expect(e.Kind=="error"&&e.Id==id,"Got "+e.Kind);
 Expect(await DmTry(b,p=>p.Kind=="dm",1)==null,"Stranger's DM delivered");a.Dispose();b.Dispose();});
await Test("A DM arrives live, is confirmed, and shows on the sender's other device",async()=>{var (a,ua,ta)=await Online("hal");var (b,ub,_)=await Online("ida");await Befriend(a,ua,b,ub);var a2=await Device(ta);
 var id=await a.SendText(ub,"hello there");var got=await DmNext(b,p=>p.Kind=="dm");Expect(got is {Name:var n,Text:"hello there",Room:"in",Type:"text"}&&n==ua&&got.Id==id&&got.Time>0,"B got "+got);
 var sent=await DmNext(a,p=>p.Kind is "dm-sent" or "error");Expect(sent.Kind=="dm-sent"&&sent.Id==id,"Sender got "+sent.Kind);
 var echo=await DmNext(a2,p=>p.Kind=="dm");Expect(echo.Room=="out"&&echo.Name==ub&&echo.Text=="hello there","Other device got "+echo);a.Dispose();a2.Dispose();b.Dispose();});
await Test("A DM to an offline contact is held until their app confirms it",async()=>{var (a,ua,_)=await Online("jan");var (b,ub,tb)=await Online("kai");await Befriend(a,ua,b,ub);await b.Close();
 await ContactsUntil(a,l=>l.Single(x=>x.Name==ub).Online==false);var id=await a.SendText(ub,"while you were away");await DmNext(a,p=>p.Kind=="dm-sent");
 var b2=await Device(tb);var first=await DmNext(b2,p=>p.Kind=="dm");Expect(first.Id==id&&first.Text=="while you were away","Held DM "+first);await b2.Close();
 var b3=await Device(tb);var again=await DmNext(b3,p=>p.Kind=="dm");Expect(again.Id==id,"Unconfirmed DM not resent");await b3.Ack(id);await Task.Delay(300);await b3.Close();
 var b4=await Device(tb);Expect(await DmTry(b4,p=>p.Kind=="dm",2)==null,"Confirmed DM sent again");a.Dispose();b4.Dispose();});
await Test("Stickers work in DMs; unknown ones are refused",async()=>{var (a,ua,_)=await Online("lou");var (b,ub,_)=await Online("max");await Befriend(a,ua,b,ub);
 await a.SendSticker(ub,"party");var st=await DmNext(b,p=>p.Kind=="dm");Expect(st.Type=="sticker"&&st.Text=="party","Got "+st);
 var bad=await a.SendSticker(ub,"<script>");var e=await DmNext(a,p=>p.Kind=="error"&&p.Id==bad);Expect(e.Text.Length>0,"No error");a.Dispose();b.Dispose();});
await Test("A picture DM is held and delivered intact",async()=>{var (a,ua,_)=await Online("ned");var (b,ub,tb)=await Online("ola");await Befriend(a,ua,b,ub);await b.Close();
 var data=Sample(30000);var id=await a.SendPicture(ub,data,"image/png");await DmNext(a,p=>p.Kind=="dm-sent"&&p.Id==id);
 var b2=await Device(tb);var head=await DmNext(b2,p=>p.Kind=="dm");Expect(head is {Type:"picture",Text:"image/png",Total:30000}&&head.Id==id,"Head "+head);
 using var buf=new MemoryStream();while(true){var p=await DmNext(b2,x=>x.Id==id);if(p.Kind=="dm-chunk")buf.Write(Convert.FromBase64String(p.Data));else if(p.Kind=="dm-end")break;}
 Expect(buf.ToArray().SequenceEqual(data),"Bytes differ");await b2.Ack(id);a.Dispose();b2.Dispose();});
await Test("Blocking stops messages and requests",async()=>{var (a,ua,_)=await Online("pia");var (b,ub,_)=await Online("quin");await Befriend(a,ua,b,ub);
 await b.Contact("block",ua);await ContactsUntil(a,l=>!l.Any(x=>x.Name==ub));var lb=await ContactsUntil(b,l=>l.Any(x=>x.Name==ua&&x.State=="blocked"));
 var id=await a.SendText(ub,"hello?");Expect((await DmNext(a,p=>p.Kind is "error" or "dm-sent")).Kind=="error","Blocked DM accepted");
 await a.Contact("add",ub);await DmNext(a,p=>p.Kind=="error");Expect(await DmTry(b,p=>p.Kind=="contacts"&&ContactInfo.Parse(p.Text).Any(x=>x.Name==ua&&x.State=="incoming"),1)==null,"Request reached blocker");
 await b.Contact("unblock",ua);await ContactsUntil(b,l=>!l.Any(x=>x.Name==ua));a.Dispose();b.Dispose();});
await Test("Removing a contact ends messaging",async()=>{var (a,ua,_)=await Online("ray");var (b,ub,_)=await Online("sol");await Befriend(a,ua,b,ub);await a.Contact("remove",ub);
 await ContactsUntil(b,l=>!l.Any(x=>x.Name==ua));await a.SendText(ub,"still there?");Expect((await DmNext(a,p=>p.Kind is "error" or "dm-sent")).Kind=="error","DM after removal accepted");a.Dispose();b.Dispose();});
await Test("Deleting an account removes it from contacts",async()=>{var (a,ua,_)=await Online("tia");var (b,ub,tb)=await Online("uma");await Befriend(a,ua,b,ub);await acct.Delete(tb,pw1);
 await ContactsUntil(a,l=>!l.Any(x=>x.Name==ub));Expect(await DmTry(b,_=>false,3)==null,"Deleted account's DM connection stayed open");a.Dispose();b.Dispose();});
await Test("Changing the password closes the old DM connections",async()=>{var (a,_,ta)=await Online("val");var changed=await acct.ChangePassword(ta,pw1,pw2);Expect(await DmTry(a,_=>false,5)==null,"Old connection still open");
 var again=await Device(changed.Token);again.Dispose();a.Dispose();});
await Test("Over-long DMs are refused",async()=>{var (a,ua,_)=await Online("wes");var (b,ub,_)=await Online("xia");await Befriend(a,ua,b,ub);
 var raw=new ClientWebSocket();await raw.ConnectAsync(dmUrl,deadline.Token);var tok=(await acct.SignIn(ua,pw1)).Token;await Protocol.Write(raw,new Packet("hello",Token:tok),deadline.Token);await TryRead(raw);
 await Protocol.Write(raw,new Packet("dm",Name:ub,Text:new string('x',2001),Id:Media.NewId(),Type:"text"),deadline.Token);
 Expect(await DmTry(b,p=>p.Kind=="dm",2)==null,"Over-long DM delivered");raw.Dispose();a.Dispose();b.Dispose();});

// --- Profiles and receipts (0.10 relay) ---
byte[] Jpeg(int n){var b=Sample(n);b[0]=0xFF;b[1]=0xD8;return b;}
await Test("Relay announces profiles and receipts",async()=>{using var s=await Connect(new Packet("create",U("p-feat"),secret,"Feat"));var r=await Read(s);var f=r.Text.Split(' ');Expect(f.Contains("profiles")&&f.Contains("receipts"),"Ready text: "+r.Text);});
await Test("Status and status line reach contacts",async()=>{var (a,ua,_)=await Online("ann");var (b,ub,_)=await Online("bea");await Befriend(a,ua,b,ub);await a.SetProfile("busy","In a meeting");
 var l=await ContactsUntil(b,l=>l.Any(x=>x.Name==ua&&x.Presence=="busy"));var them=l.Single(x=>x.Name==ua);Expect(them.Mood=="In a meeting"&&them.Online,"B sees "+them);a.Dispose();b.Dispose();});
await Test("Appear offline looks offline to contacts but stays connected",async()=>{var (a,ua,_)=await Online("cat");var (b,ub,_)=await Online("dee");await Befriend(a,ua,b,ub);await a.SetProfile("invisible","");
 var l=await ContactsUntil(b,l=>l.Any(x=>x.Name==ua&&!x.Online));Expect(l.Single(x=>x.Name==ua).Presence=="offline","B sees "+l.Single(x=>x.Name==ua));
 var id=await b.SendText(ua,"are you there?");var got=await DmNext(a,p=>p.Kind=="dm");Expect(got.Id==id,"Invisible user missed a live DM");a.Dispose();b.Dispose();});
await Test("Your other devices and a reconnect keep your profile",async()=>{var (a,_,ta)=await Online("eli");var a2=await Device(ta);await a.SetProfile("away","brb");
 var me=await DmNext(a2,p=>p.Kind=="me"&&p.Type=="away");Expect(me.Text=="brb","Other device got "+me);a2.Dispose();await a.Close();
 var a3=await Device(ta);var again=await DmNext(a3,p=>p.Kind=="me");Expect(again.Type=="away"&&again.Text=="brb","After reconnect "+again);a3.Dispose();});
await Test("Pictures reach contacts only",async()=>{var (a,ua,_)=await Online("fay");var (b,ub,_)=await Online("gus");var (c,_,_)=await Online("hal");await Befriend(a,ua,b,ub);var pic=Jpeg(4000);await a.SetAvatar(pic);
 var l=await ContactsUntil(b,l=>l.Single(x=>x.Name==ua).Avatar.Length>0);await b.GetAvatar(ua);var got=await DmNext(b,p=>p.Kind=="avatar");
 Expect(got.Name==ua&&got.Id==l.Single(x=>x.Name==ua).Avatar&&Convert.FromBase64String(got.Data).SequenceEqual(pic),"Picture mismatch");
 await c.GetAvatar(ua);var e=await DmNext(c,p=>p.Kind is "avatar" or "error");Expect(e.Kind=="error","A stranger got the picture");
 await a.SetAvatar(null);await ContactsUntil(b,l=>l.Single(x=>x.Name==ua).Avatar.Length==0);a.Dispose();b.Dispose();c.Dispose();});
await Test("Bad pictures, unknown statuses and long status lines are refused",async()=>{var (a,_,_)=await Online("ida");var junk=Sample(500);junk[0]=0x00;
 await a.SetAvatar(junk);await DmNext(a,p=>p.Kind=="error");await a.SetAvatar(Jpeg(Profiles.MaxAvatar+1));await DmNext(a,p=>p.Kind=="error");
 await a.SetProfile("online",new string('x',Profiles.MaxMood+1));await DmNext(a,p=>p.Kind=="error");await a.SetProfile("dancing","");await DmNext(a,p=>p.Kind=="error");a.Dispose();});
await Test("Typing reaches contacts only",async()=>{var (a,ua,_)=await Online("jay");var (b,ub,_)=await Online("kim");var (c,uc,_)=await Online("lee");await Befriend(a,ua,b,ub);
 await a.Typing(ub);var t=await DmNext(b,p=>p.Kind=="dm-typing");Expect(t.Name==ua,"From "+t.Name);await Task.Delay(2100);await a.Typing(uc);Expect(await DmTry(c,p=>p.Kind=="dm-typing",1)==null,"Stranger saw typing");a.Dispose();b.Dispose();c.Dispose();});
await Test("Delivered is reported when the recipient's app confirms",async()=>{var (a,ua,_)=await Online("mo");var (b,ub,_)=await Online("nia");await Befriend(a,ua,b,ub);
 var id=await a.SendText(ub,"hi");await DmNext(b,p=>p.Kind=="dm");await b.Ack(id);var d=await DmNext(a,p=>p.Kind=="dm-delivered");Expect(d.Id==id&&d.Name==ub,"Got "+d);a.Dispose();b.Dispose();});
await Test("Seen is passed to the sender, not to strangers",async()=>{var (a,ua,_)=await Online("oz");var (b,ub,_)=await Online("pam");var (c,uc,_)=await Online("quo");await Befriend(a,ua,b,ub);
 var id=await a.SendText(ub,"read me");await DmNext(b,p=>p.Kind=="dm");await b.Seen(ua,id);var seen=await DmNext(a,p=>p.Kind=="dm-seen");Expect(seen.Id==id&&seen.Name==ub,"Got "+seen);
 await b.Seen(uc,Media.NewId());Expect(await DmTry(c,p=>p.Kind=="dm-seen",1)==null,"Stranger got Seen");a.Dispose();b.Dispose();c.Dispose();});

await Test("DM replies, edits, deletes and reactions reach the other side and your other devices",async()=>{var (a,ua,ta)=await Online("rex");var (b,ub,_)=await Online("sia");await Befriend(a,ua,b,ub);Expect(a.Features.Contains("edits"),"Ready: "+string.Join(' ',a.Features));
 var a2=await Device(ta);var first=await a.SendText(ub,"hey");await DmNext(b,p=>p.Kind=="dm"&&p.Id==first);
 var reply=await b.SendText(ua,"hi back",first);var got=await DmNext(a,p=>p.Kind=="dm"&&p.Id==reply);Expect(got.Reply==first&&got.Type=="text","Reply "+got);
 var edit=await a.SendChange(ub,"edit",first,"hey there");var e=await DmNext(b,p=>p.Kind=="dm"&&p.Id==edit);Expect(e.Type=="edit"&&e.Reply==first&&e.Text=="hey there","Edit "+e);
 var e2=await DmNext(a2,p=>p.Kind=="dm"&&p.Id==edit);Expect(e2.Room=="out"&&e2.Type=="edit","Other device "+e2);
 var react=await b.SendChange(ua,"react",first,"❤️");var r=await DmNext(a,p=>p.Kind=="dm"&&p.Id==react);Expect(r.Type=="react"&&r.Text=="❤️"&&r.Reply==first,"React "+r);
 var del=await a.SendChange(ub,"delete",first);var d=await DmNext(b,p=>p.Kind=="dm"&&p.Id==del);Expect(d.Type=="delete"&&d.Reply==first,"Delete "+d);a.Dispose();a2.Dispose();b.Dispose();});
await Test("Bad DM changes are refused",async()=>{var (a,ua,_)=await Online("tia");var (b,ub,_)=await Online("uma");await Befriend(a,ua,b,ub);
 foreach(var (type,target,text) in new[]{("react",Media.NewId(),"lol"),("edit",Media.NewId(),""),("edit","not-an-id","x"),("delete","","")}){var id=await a.SendChange(ub,type,target,text);var p=await DmNext(a,p=>p.Id==id&&p.Kind is "error" or "dm-sent");Expect(p.Kind=="error",$"{type} '{text}' accepted");}a.Dispose();b.Dispose();});
await Test("Held changes reach new apps; older apps get only messages",async()=>{var (a,ua,_)=await Online("vic");var (b,ub,tb)=await Online("wes");await Befriend(a,ua,b,ub);b.Dispose();await Task.Delay(300);
 var m=await a.SendText(ub,"while away");var ed=await a.SendChange(ub,"edit",m,"while you were away");
 var old=new ClientWebSocket();await old.ConnectAsync(dmUrl,deadline.Token);await Protocol.Write(old,new Packet("hello",Token:tb),deadline.Token);var oldGot=new List<Packet>();
 try{while(await TryRead(old,2) is {} p){oldGot.Add(p);if(p.Kind=="dm")await Protocol.Write(old,new Packet("dm-ack",Id:p.Id),deadline.Token);}}catch(OperationCanceledException) when(!deadline.IsCancellationRequested){}
 Expect(oldGot.Any(p=>p.Kind=="dm"&&p.Id==m)&&!oldGot.Any(p=>p.Kind=="dm"&&p.Type=="edit"),"Old app got "+string.Join(",",oldGot.Where(p=>p.Kind=="dm").Select(p=>p.Type)));old.Abort();
 var m2=await a.SendText(ub,"again");var ed2=await a.SendChange(ub,"edit",m2,"again!");var fresh=await Device(tb);var e=await DmNext(fresh,p=>p.Kind=="dm"&&p.Id==ed2);Expect(e.Type=="edit"&&e.Text=="again!","Fresh "+e);a.Dispose();fresh.Dispose();});

// --- Groki, the AI member (0.11 relay, started with a BotKey) ---
const string botKey="test-bot-key-0123456789";
async Task<(DirectClient C,string Token)> GrokiOnline(){var r=await acct.BotSignIn("groki",botKey);var c=new DirectClient();await c.Connect(dmUrl,r.Token);return (c,r.Token);}
async Task<ClientWebSocket> Summoned(string room,string token,string pass){var s=await Open();await Protocol.Write(s,new Packet("summoned",Room:room,Token:token,Data:pass),deadline.Token);return s;}
await Test("Relay announces bots",async()=>{using var s=await Connect(new Packet("create",U("bot-feat"),secret,"Feat"));var r=await Read(s);Expect(r.Text.Split(' ').Contains("bots"),"Ready text: "+r.Text);});
await Test("Bryan, his old name, stays reserved",async()=>{var e=await Fails(()=>acct.SignUp("bryan",pw1));Expect(e.Status==409,"Status "+e.Status);});
await Test("Nobody can sign up as Groki",async()=>{var e=await Fails(()=>acct.SignUp("Groki",pw1));Expect(e.Status==409,"Status "+e.Status);});
await Test("Groki's sign-in needs the bot key, and only works for bot names",async()=>{var e1=await Fails(()=>acct.BotSignIn("groki","wrong key wrong key"));var e2=await Fails(()=>acct.BotSignIn(U("someone"),botKey));
 Expect(e1.Status==401&&e2.Status==401,$"{e1.Status} {e2.Status}");var a=await acct.BotSignIn("groki",botKey);var b=await acct.BotSignIn("groki",botKey);Expect(a.Username=="groki"&&b.Username=="groki"&&a.Token!=b.Token,"Sign-ins "+a+" "+b);
 var e3=await Fails(()=>acct.SignIn("groki","any password at all"));Expect(e3.Status==401,"Password sign-in worked for Groki");});
await Test("@groki while Groki is offline tells only the caller",async()=>{var room=U("off-room");using var o=await Enter("create",room,"Owner");await Protocol.Write(o,new Packet("send",Text:"hey @groki you there"),deadline.Token);
 var note=await ReadUntil(o,p=>p.Name=="Room"&&p.Text.Contains("online"));Expect(note.Text=="Groki (AI) isn't online right now.","Got "+note.Text);});
await Test("@groki summons him: one-time pass, no room password, shown as Groki (AI)",async()=>{var (bot,bt)=await GrokiOnline();var room=U("summon-room");using var o=await Enter("create",room,"Owner");
 await Protocol.Write(o,new Packet("send",Text:"yo @Groki what do you think"),deadline.Token);var call=await DmNext(bot,p=>p.Kind=="summon");
 Expect(call.Room==room&&call.Text=="yo @Groki what do you think"&&call.Name==G("Owner")&&call.Token.Length==32,"Summon "+call);
 using var s=await Summoned(room,bt,call.Token);var r=await Read(s);Expect(r.Kind=="ready","Bot not admitted: "+r.Text);
 await ReadUntil(o,p=>p.Text=="Groki (AI) joined.");await Protocol.Write(s,new Packet("send",Text:"hey whats up"),deadline.Token);var m=await ReadUntil(o,p=>p.Text=="hey whats up");Expect(m.Name=="Groki (AI)","Name "+m.Name);
 await Protocol.Write(s,new Packet("typing"),deadline.Token);var t=await ReadUntil(o,p=>p.Kind=="typing");Expect(t.Name=="Groki (AI)","Typing from "+t.Name);
 await Protocol.Write(o,new Packet("send",Text:"@groki again"),deadline.Token);Expect(await DmTry(bot,p=>p.Kind=="summon",1)==null,"Summoned while already in the room");
 using var reuse=await Summoned(room,bt,call.Token);var again=await Read(reuse);Expect(again.Kind=="error","A pass worked twice");bot.Dispose();});
await Test("A pass only works for its room, and only for Groki",async()=>{var (bot,bt)=await GrokiOnline();var room=U("pass-room");var other=U("other-room");using var o=await Enter("create",room,"Owner");using var o2=await Enter("create",other,"Owner2");
 await Protocol.Write(o,new Packet("send",Text:"@groki"),deadline.Token);var call=await DmNext(bot,p=>p.Kind=="summon");
 using(var wrongRoom=await Summoned(other,bt,call.Token)){Expect((await Read(wrongRoom)).Kind=="error","Pass worked in another room");}
 await Task.Delay(10500);await Protocol.Write(o,new Packet("send",Text:"@groki"),deadline.Token);var call2=await DmNext(bot,p=>p.Kind=="summon");var human=(await acct.SignUp(U("mallory"),pw1)).Token;
 using(var impostor=await Summoned(room,human,call2.Token)){Expect((await Read(impostor)).Kind=="error","A normal account used Groki's pass");}bot.Dispose();});
await Test("Groki is marked as AI in contact lists",async()=>{var (bot,_)=await GrokiOnline();var (a,ua,_)=await Online("amy");await a.Contact("add","groki");await ContactsUntil(bot,l=>l.Any(x=>x.Name==ua&&x.State=="incoming"));
 await bot.Contact("accept",ua);var l=await ContactsUntil(a,l=>l.Any(x=>x.Name=="groki"&&x.State=="contact"));Expect(l.Single(x=>x.Name=="groki").Bot&&!l.Single(x=>x.Name=="groki").Name.Contains("AI"),"Not marked");a.Dispose();bot.Dispose();});

// --- Replies, edits, deletes, reactions and room history (0.14 relay) ---
async Task<Packet> Said(ClientWebSocket s,string text)=>await ReadUntil(s,p=>p.Kind=="message"&&p.Text==text);
async Task Say(ClientWebSocket s,string text,string reply="")=>await Protocol.Write(s,new Packet("send",Text:text,Reply:reply),deadline.Token);
async Task<List<Packet>> HistoryOf(ClientWebSocket s){var all=new List<Packet>();while(true){var p=await Read(s);if(p.Kind=="message"&&p.Name=="Room")return all;all.Add(p);}}
async Task<ClientWebSocket> EnterWithHistory(string room,string name,List<Packet> history,string? token=null){var s=await Connect(new Packet("join",room,secret,name,Token:token??""));var r=await Read(s);Expect(r.Kind=="ready","Not ready: "+r.Text);history.AddRange(await HistoryOf(s));return s;}
await Test("Relay announces replies, edits, reactions and history",async()=>{using var s=await Connect(new Packet("create",U("ext-feat"),secret,"Feat"));var f=(await Read(s)).Text.Split(' ');
 Expect(new[]{"replies","edits","reactions","history"}.All(f.Contains),"Ready text: "+string.Join(' ',f));});
await Test("Messages carry an id and time; a reply keeps the id it answers",async()=>{var room=U("reply-room");using var o=await Enter("create",room,"Owner");using var g=await Enter("join",room,"Guest");
 await Say(o,"first");var first=await Said(g,"first");Expect(Media.ValidId(first.Id)&&Math.Abs(first.Time-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())<10000,"Id/time "+first);
 await Say(g,"answer",first.Id);var answer=await Said(o,"answer");Expect(answer.Reply==first.Id&&answer.Id!=first.Id,"Reply "+answer);
 await Say(g,"to nowhere",Media.NewId());var lost=await Said(o,"to nowhere");Expect(lost.Reply=="","Unknown reply id kept: "+lost.Reply);
 await Protocol.Write(o,new Packet("sticker",Text:"gg",Reply:answer.Id),deadline.Token);var st=await ReadUntil(g,p=>p.Kind=="sticker");Expect(st.Reply==answer.Id&&Media.ValidId(st.Id),"Sticker "+st);});
await Test("Late joiners get the room's recent history, then the join notice",async()=>{var room=U("hist-room");using var o=await Enter("create",room,"Owner");
 await Say(o,"one");var one=await Said(o,"one");await Protocol.Write(o,new Packet("sticker",Text:"party"),deadline.Token);await ReadUntil(o,p=>p.Kind=="sticker");await Say(o,"three",one.Id);await Said(o,"three");
 var h=new List<Packet>();using var late=await EnterWithHistory(room,"Late",h);var texts=h.Where(p=>p.Kind=="history").Select(p=>p.Text).ToList();
 Expect(string.Join(",",texts)=="one,party,three","History: "+string.Join(",",texts));Expect(h.Where(p=>p.Kind=="history").All(p=>p.Name==G("Owner")&&Media.ValidId(p.Id)&&p.Time>0),"Names/ids");
 Expect(h.Single(p=>p.Text=="party").Type=="sticker"&&h.Single(p=>p.Text=="three").Reply==one.Id&&h.Single(p=>p.Text=="one").Type=="text","Types/replies");
 Expect(!h.Any(p=>p.Kind=="message"),"History came as live messages");});
await Test("Only the author can edit; everyone sees it and history keeps the new text",async()=>{var room=U("edit-room");using var o=await Enter("create",room,"Owner");using var g=await Enter("join",room,"Guest");
 await Say(o,"typo hre");var m=await Said(g,"typo hre");
 await Protocol.Write(g,new Packet("edit",Id:m.Id,Text:"hacked"),deadline.Token);var refused=await ReadUntil(g,p=>p.Kind is "error" or "edit");Expect(refused.Kind=="error","Someone else edited it");
 await Protocol.Write(o,new Packet("edit",Id:m.Id,Text:"typo here"),deadline.Token);var e=await ReadUntil(g,p=>p.Kind=="edit");Expect(e.Id==m.Id&&e.Text=="typo here"&&e.Name==G("Owner"),"Edit "+e);await ReadUntil(o,p=>p.Kind=="edit");
 await Protocol.Write(o,new Packet("edit",Id:m.Id,Text:new string('x',2001)),deadline.Token);Expect((await ReadUntil(o,p=>p.Kind is "error" or "edit")).Kind=="error","Oversized edit accepted");
 var h=new List<Packet>();using var late=await EnterWithHistory(room,"Late",h);var entry=h.Single(p=>p.Kind=="history");Expect(entry.Text=="typo here"&&entry.Edited,"History "+entry);});
await Test("Only the author can delete; it leaves history",async()=>{var room=U("del-room");using var o=await Enter("create",room,"Owner");using var g=await Enter("join",room,"Guest");
 await Say(o,"keep");await Said(g,"keep");await Say(o,"oops");var m=await Said(g,"oops");
 await Protocol.Write(g,new Packet("delete",Id:m.Id),deadline.Token);Expect((await ReadUntil(g,p=>p.Kind is "error" or "delete")).Kind=="error","Someone else deleted it");
 await Protocol.Write(o,new Packet("delete",Id:m.Id),deadline.Token);var d=await ReadUntil(g,p=>p.Kind=="delete");Expect(d.Id==m.Id,"Delete "+d);
 await Protocol.Write(o,new Packet("edit",Id:m.Id,Text:"back"),deadline.Token);Expect((await ReadUntil(o,p=>p.Kind is "error" or "edit")).Kind=="error","Edited a deleted message");
 var h=new List<Packet>();using var late=await EnterWithHistory(room,"Late",h);Expect(string.Join(",",h.Where(p=>p.Kind=="history").Select(p=>p.Text))=="keep","History "+string.Join(",",h.Select(p=>p.Text)));});
await Test("Reactions toggle, show who reacted, and come with history",async()=>{var room=U("react-room");using var o=await Enter("create",room,"Owner");using var g=await Enter("join",room,"Guest");
 await Say(o,"pizza?");var m=await Said(g,"pizza?");
 await Protocol.Write(g,new Packet("react",Id:m.Id,Text:"👍"),deadline.Token);var r1=Reaction.Parse((await ReadUntil(o,p=>p.Kind=="reactions"&&p.Id==m.Id)).Text);Expect(r1.Count==1&&r1[0].E=="👍"&&r1[0].N.SequenceEqual([G("Guest")]),"First "+r1.Count);
 await Protocol.Write(o,new Packet("react",Id:m.Id,Text:"👍"),deadline.Token);var r2=Reaction.Parse((await ReadUntil(g,p=>p.Kind=="reactions"&&p.Id==m.Id&&Reaction.Parse(p.Text).Sum(x=>x.N.Count)==2)).Text);Expect(r2[0].N.SequenceEqual([G("Guest"),G("Owner")]),"Second");
 await Protocol.Write(g,new Packet("react",Id:m.Id,Text:"👍"),deadline.Token);var r3=Reaction.Parse((await ReadUntil(o,p=>p.Kind=="reactions"&&p.Id==m.Id&&Reaction.Parse(p.Text).Sum(x=>x.N.Count)==1)).Text);Expect(r3[0].N.SequenceEqual([G("Owner")]),"Toggle off");
 using(var bad=await Enter("join",room,"Bad")){await Protocol.Write(bad,new Packet("react",Id:m.Id,Text:"lol"),deadline.Token);Expect((await ReadUntil(bad,p=>p.Kind is "error" or "reactions")).Kind=="error","Word accepted as a reaction");
  await Protocol.Write(bad,new Packet("react",Id:Media.NewId(),Text:"❤️"),deadline.Token);Expect((await ReadUntil(bad,p=>p.Kind is "error" or "reactions")).Kind=="error","Reaction to a missing message");}
 var h=new List<Packet>();using var late=await EnterWithHistory(room,"Late",h);var hr=h.Single(p=>p.Kind=="reactions");Expect(hr.Id==m.Id&&Reaction.Parse(hr.Text)[0].N.SequenceEqual([G("Owner")]),"History reactions");});
await Test("A reaction can't be used to flood: at most 8 kinds per message",async()=>{var room=U("many-react");using var o=await Enter("create",room,"Owner");await Say(o,"x");var m=await Said(o,"x");
 string[] faces=["😀","😁","😂","🤣","😃","😄","😅","😆","😉"];foreach(var f in faces)await Protocol.Write(o,new Packet("react",Id:m.Id,Text:f),deadline.Token);
 var last=new List<Packet>();for(int i=0;i<faces.Length;i++)last.Add(await ReadUntil(o,p=>p.Kind is "reactions" or "error"));Expect(last.Count(p=>p.Kind=="error")==1&&Reaction.Parse(last.Last(p=>p.Kind=="reactions").Text).Count==8,"Got "+string.Join(",",last.Select(p=>p.Kind)));});
await Test("A guest who reconnects can't edit old messages; a signed-in person can",async()=>{var room=U("who-room");using var o=await Enter("create",room,"Owner");
 using(var g1=await Enter("join",room,"Sam")){await Say(g1,"guest words");await Said(g1,"guest words");}var gm=await Said(o,"guest words");
 using var g2=await Enter("join",room,"Sam");await Protocol.Write(g2,new Packet("edit",Id:gm.Id,Text:"changed"),deadline.Token);Expect((await ReadUntil(g2,p=>p.Kind is "error" or "edit")).Kind=="error","Another guest with the same name edited it");
 var u=U("acc");var token=(await acct.SignUp(u,pw1)).Token;var h=new List<Packet>();
 using(var a1=await EnterWithHistory(room,"x",h,token)){await Say(a1,"account words");await Said(a1,"account words");}var am=await Said(o,"account words");
 using var a2=await EnterWithHistory(room,"x",h,token);await Protocol.Write(a2,new Packet("edit",Id:am.Id,Text:"account edit"),deadline.Token);var e=await ReadUntil(o,p=>p.Kind=="edit");Expect(e.Id==am.Id&&e.Name==u,"Edit "+e);});
await Test("Pictures can be reacted to and deleted, but aren't replayed",async()=>{var room=U("pic-react");using var o=await Enter("create",room,"Owner");using var g=await Enter("join",room,"Guest");var id=Media.NewId();
 await SendMediaRaw(o,id,"image/png",Sample(3000));var start=await ReadUntil(g,p=>p.Kind=="media-start");Expect(start.Time>0,"No time on media");await ReadUntil(g,p=>p.Kind=="media-end");
 await Protocol.Write(g,new Packet("react",Id:id,Text:"🔥"),deadline.Token);var r=await ReadUntil(o,p=>p.Kind=="reactions");Expect(r.Id==id,"Reaction id");
 var h=new List<Packet>();using(var late=await EnterWithHistory(room,"Late",h))Expect(!h.Any(p=>p.Kind=="history"||p.Id==id),"Picture replayed");
 await Protocol.Write(o,new Packet("delete",Id:id),deadline.Token);Expect((await ReadUntil(g,p=>p.Kind=="delete")).Id==id,"Picture not deleted");});
await Test("History keeps the last 100 messages",async()=>{var room=U("long-room");using var o=await Enter("create",room,"Owner");
 for(int i=0;i<110;i++){await Say(o,"n"+i);await Said(o,"n"+i);if(i%25==24)await Task.Delay(10100);}
 var h=new List<Packet>();using var late=await EnterWithHistory(room,"Late",h);var texts=h.Where(p=>p.Kind=="history").Select(p=>p.Text).ToList();Expect(texts.Count==100&&texts[0]=="n10"&&texts[^1]=="n109","Got "+texts.Count+" from "+texts.FirstOrDefault());});
await Test("Reaction helpers",()=>{Expect(Reaction.Valid("👍")&&Reaction.Valid("❤️")&&Reaction.Valid("🇳🇴")&&!Reaction.Valid("")&&!Reaction.Valid("lol")&&!Reaction.Valid("👍 ")&&!Reaction.Valid(string.Concat(Enumerable.Repeat("😀",9))),"Valid");
 var list=new List<Reaction>{new("👍",[new string('é',32),"Sam"])};Expect(Reaction.Parse(Reaction.Format(list))[0].N[0]==new string('é',32)&&Reaction.Parse("not json").Count==0,"Round trip");return Task.CompletedTask;});

// --- Feedback on Groki (0.15 relay): thumbs up / down and the experience survey ---
await Test("Feedback reaches Groki, is held while he's offline, and isn't echoed",async()=>{var (bot,bt)=await GrokiOnline();var (a,ua,ta)=await Online("fay");Expect(a.Features.Contains("feedback"),"Ready: "+string.Join(' ',a.Features));
 var a2=await Device(ta);var target=Media.NewId();var id=await a.SendFeedback("groki","up",target,"dm");
 var f=await DmNext(bot,p=>p.Kind=="feedback"&&p.Id==id);Expect(f.Name==ua&&f.Type=="up"&&f.Reply==target&&f.Room=="dm"&&f.Time>0,"Got "+f);
 await DmNext(a,p=>p.Kind=="dm-sent"&&p.Id==id);Expect(await DmTry(a2,p=>p.Id==id,1)==null,"Echoed to the sender's other device");
 await bot.Ack(id);await Task.Delay(300);bot.Dispose();await Task.Delay(300);
 var s=await a.SendFeedback("groki","survey","","",4,"pretty good");var d=await a.SendFeedback("groki","down",Media.NewId(),"room:pizza-room",0,"Rude or mean");
 await DmNext(a,p=>p.Kind=="dm-sent"&&p.Id==d);var back=await Device(bt);var got=new List<Packet>{await DmNext(back,p=>p.Kind=="feedback"),await DmNext(back,p=>p.Kind=="feedback")};
 Expect(got.Any(p=>p.Id==s&&p.Type=="survey"&&p.Seq==4&&p.Text=="pretty good"&&p.Name==ua)&&got.Any(p=>p.Id==d&&p.Type=="down"&&p.Room=="room:pizza-room"&&p.Text=="Rude or mean"),"Held: "+string.Join(" | ",got));
 Expect(!got.Any(p=>p.Id==id),"A confirmed vote came back");a.Dispose();a2.Dispose();back.Dispose();});
await Test("Feedback is only for AI members, and must be well formed",async()=>{var (bot,_)=await GrokiOnline();var (a,ua,_)=await Online("gus");var (b,ub,_)=await Online("hal");await Befriend(a,ua,b,ub);
 foreach(var (to,type,target,rating,text) in new[]{(ub,"up",Media.NewId(),0,""),("groki","meh",Media.NewId(),0,""),("groki","up","",0,""),("groki","up","not-an-id",0,""),("groki","survey","",0,""),("groki","survey","",6,""),("groki","down",Media.NewId(),0,new string('x',501)),("nobody-here","up",Media.NewId(),0,"")}){
  var id=await a.SendFeedback(to,type,target,"dm",rating,text);var r=await DmNext(a,p=>p.Id==id&&p.Kind is "error" or "dm-sent");Expect(r.Kind=="error",$"{to} {type} {rating} accepted");}
 Expect(await DmTry(b,p=>p.Kind=="feedback",1)==null,"A person got feedback");a.Dispose();b.Dispose();bot.Dispose();});
await Test("Feedback helpers",()=>{Expect(FriendsChat.Feedback.Valid(new Packet("feedback",Type:"up",Reply:Media.NewId()))&&FriendsChat.Feedback.Valid(new Packet("feedback",Type:"survey",Seq:5,Text:"line one\nline two"))&&!FriendsChat.Feedback.Valid(new Packet("feedback",Type:"survey",Seq:3,Reply:Media.NewId()))&&!FriendsChat.Feedback.Valid(new Packet("feedback",Type:"down",Reply:Media.NewId(),Text:"bad\u0007")),"Valid");return Task.CompletedTask;});

// --- Field-specific rejections ---
await Test("Relay names the bad room field",async()=>{using var s=await Connect(new Packet("create","bad room",secret,"Eve"));var p=await Read(s);Expect(p.Kind=="error"&&p.Text.StartsWith("Room name"),"Got: "+p.Text);});
await Test("Relay names the bad password field",async()=>{using var s=await Connect(new Packet("create","good-room","tiny","Eve"));var p=await Read(s);Expect(p.Kind=="error"&&p.Text.StartsWith("Password"),"Got: "+p.Text);});
await Test("Relay names the bad display name field",async()=>{using var s=await Connect(new Packet("create","good-room",secret,"  "));var p=await Read(s);Expect(p.Kind=="error"&&p.Text.StartsWith("Display name"),"Got: "+p.Text);});

// --- Lifecycle ---
await Test("Full room rejects without saying ready first",async()=>{var peers=new List<ClientWebSocket>{await Enter("create","full-room","Owner")};for(int i=1;i<16;i++)peers.Add(await Enter("join","full-room","P"+i));using var extra=await Connect(new Packet("join","full-room",secret,"Extra"));var first=await Read(extra);foreach(var p in peers)p.Abort();Expect(first.Kind=="error","Got "+first.Kind+" before rejection");});
await Test("Fast typing does not disconnect the creator",async()=>{using var o=await Enter("create","fast-room","Fast");using var f=await Enter("join","fast-room","Friend");for(int i=0;i<35;i++)await Protocol.Write(o,new Packet("send",Text:"burst "+i),deadline.Token);await Task.Delay(11000);await Protocol.Write(o,new Packet("send",Text:"still here"),deadline.Token);await ReadUntil(f,p=>p.Text=="still here");});

// --- Malformed input ---
await Test("Invalid JSON closes only that connection",async()=>{using var s=await Open();await SendRaw(s,Encoding.UTF8.GetBytes("{not json"),WebSocketMessageType.Text);Expect((await ReadToClose(s)).Count==0,"Unexpected reply");});
await Test("Binary frame rejected",async()=>{using var s=await Open();await SendRaw(s,[1,2,3],WebSocketMessageType.Binary);Expect((await ReadToClose(s)).Count==0,"Unexpected reply");});
await Test("JSON null and null fields rejected",async()=>{using(var s=await Open()){await SendRaw(s,Encoding.UTF8.GetBytes("null"),WebSocketMessageType.Text);Expect((await ReadToClose(s)).All(p=>p.Kind=="error"),"Accepted null");}using(var s=await Open()){await SendRaw(s,Encoding.UTF8.GetBytes("{\"Kind\":null,\"Room\":null,\"Password\":null,\"Name\":null}"),WebSocketMessageType.Text);Expect((await ReadToClose(s)).All(p=>p.Kind=="error"),"Accepted null fields");}});
await Test("Frame over 16 KiB rejected",async()=>{using var s=await Open();await SendRaw(s,Encoding.UTF8.GetBytes(new string('x',20000)),WebSocketMessageType.Text);Expect((await ReadToClose(s)).Count==0,"Unexpected reply");});
await Test("Malformed message after joining keeps the room open",async()=>{using var o=await Enter("create","junk-room","Owner");using var j=await Enter("join","junk-room","Junk");await ReadUntil(o,p=>p.Text==G("Junk")+" joined.");await SendRaw(j,Encoding.UTF8.GetBytes("{oops"),WebSocketMessageType.Text);await ReadUntil(o,p=>p.Text==G("Junk")+" left.");using var k=await Enter("join","junk-room","Later");});
await Test("Relay still works after bad input",async()=>{using var s=await Enter("create","after-junk","Fresh");});

// --- Client-side checks (no relay needed) ---
await Test("Production client enforces HTTPS and secure WebSockets",()=>{foreach(var url in new[]{"http://example.com","https://user:pass@example.com","https://example.com?token=secret","not-a-url",""}){try{RelayClient.ValidateUrl(url);throw new Exception("Bad URL accepted: "+url);}catch(IOException){}}Expect(RelayClient.ValidateUrl("https://example.com").Scheme=="wss","Secure URL conversion failed");return Task.CompletedTask;});
await Test("Relay address accepts common pastes",()=>{foreach(var url in new[]{"communication-device.onrender.com","https://communication-device.onrender.com/"," https://communication-device.onrender.com/chat ","https://communication-device.onrender.com/health"})Expect(RelayClient.ValidateUrl(url).ToString()=="wss://communication-device.onrender.com/chat","Rejected or changed: "+url);return Task.CompletedTask;});
await Test("Form check blames the right field",()=>{const string r="https://relay.example.com";
 Expect(Protocol.Check(r,"Al","bad!","a good password").Field==Field.Room,"Room");
 Expect(Protocol.Check(r,"Al","good-room","short").Field==Field.Password,"Password");
 Expect(Protocol.Check(r,"","good-room","a good password").Field==Field.Name,"Name");
 Expect(Protocol.Check("","Al","good-room","a good password").Field==Field.Relay,"Relay");
 Expect(Protocol.Check(r,"Al","friday friends","a good password").Field==Field.None,"Space in room name should become a hyphen");
 Expect(Protocol.NormalizeRoom(" friday  friends ")=="friday-friends","Normalize");return Task.CompletedTask;});

await Test("Settings round-trip and never store the password",()=>{var path=Path.Combine(Path.GetTempPath(),"3dfx-test-"+Guid.NewGuid()+".json");try{
 new Settings("https://relay.example.com","Al",false).Save(path);var s=Settings.Load(path);Expect(s==new Settings("https://relay.example.com","Al",false),"Round trip");
 Expect(!File.ReadAllText(path).Contains("assword",StringComparison.OrdinalIgnoreCase),"Password field present");
 File.WriteAllText(path,"{broken");Expect(Settings.Load(path)==Settings.Empty,"Corrupt file not ignored");
 Expect(Settings.Load(path+".missing")==Settings.Empty,"Missing file not ignored");}finally{File.Delete(path);}return Task.CompletedTask;});

await Test("Older settings files get the startup sound on",()=>{var path=Path.Combine(Path.GetTempPath(),"3dfx-test-"+Guid.NewGuid()+".json");try{
 File.WriteAllText(path,"{\"Relay\":\"https://r.example.com\",\"Name\":\"Al\",\"Sound\":false}");var s=Settings.Load(path);Expect(s.StartupSound&&!s.Sound&&s.Name=="Al","Got "+s);
 new Settings("https://r.example.com","Al",true,false).Save(path);Expect(!Settings.Load(path).StartupSound,"StartupSound off not kept");}finally{File.Delete(path);}return Task.CompletedTask;});
await Test("Typing line wording",()=>{Expect(Protocol.DescribeTyping([])=="","none");Expect(Protocol.DescribeTyping(["Sam"])=="Sam is typing…","one");Expect(Protocol.DescribeTyping(["Sam","Jo"])=="Sam and Jo are typing…","two");Expect(Protocol.DescribeTyping(["A","B","C"])=="Several people are typing…","three");return Task.CompletedTask;});

Directory.CreateDirectory("artifacts");File.Delete("artifacts/v0.14-tests.txt");File.WriteAllLines("artifacts/v0.14-tests.txt",results);
Console.WriteLine(failed==0?$"All {results.Count} tests passed.":$"{failed} of {results.Count} tests FAILED.");
return failed==0?0:1;
