using System.Security.Cryptography;
namespace FriendsChat;
// Short check against a deployed relay, through RelayClient with normal HTTPS certificate checks:
//   dotnet run --project Tests/Tests.csproj -c Release -- https://your-relay.onrender.com
// Uses about 6 connections (under the 20/minute limit) and a random throwaway room and password.
static class LiveSmoke {
 static async Task<Packet> Next(RelayClient c,Func<Packet,bool> match){using var t=new CancellationTokenSource(TimeSpan.FromSeconds(20));while(true){var p=await c.Read().WaitAsync(t.Token)??throw new Exception("Relay closed the connection");if(match(p))return p;}}
 public static async Task<int> Run(string url){
  var results=new List<string>();int failed=0;
  async Task Test(string name,Func<Task> body){try{await body();results.Add("PASS: "+name);}catch(Exception e){failed++;results.Add("FAIL: "+name+" — "+e.Message);}Console.WriteLine(results[^1]);}
  void Expect(bool ok,string why){if(!ok)throw new Exception(why);}
  string room="smoke-"+Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(),password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
  RelayClient a=new(),b=new(),c=new();string suffix="";string G(string n)=>n+suffix;
  await Test("Health reports 0.11 with accounts on",async()=>{using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(90)};var body=await http.GetStringAsync(url.TrimEnd('/')+"/health");Expect(body.Contains("\"0.11\"")&&body.Contains("\"accounts\":\"on\""),"Got "+body);});
  await Test("Create room over HTTPS",async()=>{await a.Connect(url,true,room,password,"Smoke-A");if(a.Features.Contains("accounts"))suffix=Accounts.GuestSuffix;var m=await Next(a,p=>p.Kind=="members");Expect(m.Text==G("Smoke-A"),"Members: "+m.Text);});
  await Test("Relay offers nudge, typing, media, stickers, accounts, DMs, profiles and receipts",()=>{Expect(new[]{"nudge","typing","media","stickers","accounts","direct","profiles","receipts"}.All(a.Features.Contains),"Features: "+string.Join(" ",a.Features));return Task.CompletedTask;});
  await Test("Wrong password rejected",async()=>{using var w=new RelayClient();try{await w.Connect(url,false,room,"not the right password","Smoke-W");throw new Exception("Accepted");}catch(IOException e){Expect(e.Message=="Room name or password is incorrect.","Got "+e.Message);}});
  await Test("Join and member list",async()=>{await b.Connect(url,false,room,password,"Smoke-B");var m=await Next(a,p=>p.Kind=="members");Expect(m.Text==G("Smoke-A")+"\n"+G("Smoke-B"),"Members: "+m.Text);});
  await Test("Message delivered with sender name",async()=>{await b.Send("hello from B");var m=await Next(a,p=>p.Kind=="message"&&p.Name==G("Smoke-B"));Expect(m.Text=="hello from B","Got "+m.Text);});
  await Test("Nudge delivered, second one limited",async()=>{await b.Signal("nudge");var n=await Next(a,p=>p.Kind=="nudge");Expect(n.Name==G("Smoke-B"),"From "+n.Name);await b.Signal("nudge");var e=await Next(b,p=>p.Kind is "error" or "nudge");Expect(e.Kind=="error","Second nudge not limited");});
  await Test("Typing delivered",async()=>{await b.Signal("typing");var t=await Next(a,p=>p.Kind=="typing");Expect(t.Name==G("Smoke-B"),"From "+t.Name);});
  await Test("Picture delivered intact",async()=>{var data=RandomNumberGenerator.GetBytes(30000);await b.SendMedia(data,"image/png");var start=await Next(a,p=>p.Kind=="media-start");using var got=new MemoryStream();
   while(true){var p=await Next(a,p=>p.Kind is "media-chunk" or "media-end" or "media-cancel");if(p.Kind=="media-chunk")got.Write(Convert.FromBase64String(p.Data));else{Expect(p.Kind=="media-end","Cancelled");break;}}
   Expect(start.Name==G("Smoke-B")&&got.ToArray().SequenceEqual(data),"Bytes differ");});
  await Test("Sticker delivered",async()=>{await b.SendSticker("party");var s=await Next(a,p=>p.Kind=="sticker");Expect(s.Text=="party"&&s.Name==G("Smoke-B"),"Got "+s.Text+" from "+s.Name);});
  await Test("Room stays open after creator leaves",async()=>{await a.Close();await Next(b,p=>p.Text==G("Smoke-A")+" left.");var m=await Next(b,p=>p.Kind=="members");Expect(m.Text==G("Smoke-B"),"Members: "+m.Text);await c.Connect(url,false,room,password,"Smoke-C");await b.Send("still open");await Next(c,p=>p.Text=="still open");});
  // A throwaway account, deleted again at the end, so the live relay keeps no test accounts.
  await Test("Account: create, join a room under its name, delete",async()=>{using var acct=AccountClient.ForRelay(url);var user="smoke_"+Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();const string pw="smoke test password";
   var r=await acct.SignUp(user,pw);Expect(Accounts.ValidRecoveryKey(r.RecoveryKey),"Recovery key "+r.RecoveryKey);
   try{using var d=new RelayClient();await d.Connect(url,false,room,password,"Spoofed",r.Token);var joined=await Next(b,p=>p.Text.EndsWith(" joined.")&&!p.Text.StartsWith("Smoke-C"));Expect(joined.Text==user+" joined.","Joined as "+joined.Text);await d.Close();}
   finally{await acct.Delete(r.Token,pw);}
   try{await acct.SignIn(user,pw);throw new Exception("Deleted account still signs in");}catch(AccountException e){Expect(e.Status==401,"Status "+e.Status);}});
  // Two throwaway accounts become contacts, exchange a DM (live and held), then delete themselves.
  await Test("DMs: contact request, live DM, held DM, confirm",async()=>{using var acct=AccountClient.ForRelay(url);const string pw="smoke test password";string N()=>"smoke_"+Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
   var (u1,u2)=(N(),N());var t1=(await acct.SignUp(u1,pw)).Token;var t2=(await acct.SignUp(u2,pw)).Token;
   try{var dm=DirectClient.UrlFor(url);var x=new DirectClient();await x.Connect(dm,t1);var y=new DirectClient();await y.Connect(dm,t2);
    async Task<Packet> On(DirectClient c,Func<Packet,bool> m){using var t=new CancellationTokenSource(TimeSpan.FromSeconds(20));while(true){var p=await c.Read().WaitAsync(t.Token)??throw new Exception("closed");if(m(p))return p;}}
    await x.Contact("add",u2);await On(y,p=>p.Kind=="contacts"&&p.Text.Contains("incoming"));await y.Contact("accept",u1);await On(x,p=>p.Kind=="contacts"&&p.Text.Contains("\"contact\""));
    var id=await x.SendText(u2,"live hello");var got=await On(y,p=>p.Kind=="dm");Expect(got.Id==id&&got.Text=="live hello"&&got.Name==u1,"Live DM "+got);await y.Ack(id);
    await y.Close();var held=await x.SendText(u2,"held hello");await On(x,p=>p.Kind=="dm-sent"&&p.Id==held);
    var y2=new DirectClient();await y2.Connect(dm,t2);var later=await On(y2,p=>p.Kind=="dm");Expect(later.Id==held,"Held DM "+later);await y2.Ack(held);var delivered=await On(x,p=>p.Kind=="dm-delivered"&&p.Id==held);Expect(delivered.Name==u2,"Delivered "+delivered);
    await x.SetProfile("busy","smoke test");var seenBusy=await On(y2,p=>p.Kind=="contacts"&&p.Text.Contains("\"busy\""));await y2.Close();x.Dispose();}
   finally{await acct.Delete(t1,pw);await acct.Delete(t2,pw);}});
  await Test("Room ends when the last person leaves",async()=>{await b.Close();await c.Close();await Task.Delay(1000);using var d=new RelayClient();try{await d.Connect(url,false,room,password,"Smoke-D");throw new Exception("Room still open 1 s after everyone left");}catch(IOException e){Expect(e.Message=="Room name or password is incorrect.","Got "+e.Message);}});
  Console.WriteLine(failed==0?$"All {results.Count} live checks passed.":$"{failed} of {results.Count} live checks FAILED.");
  return failed==0?0:1;
 }
}
