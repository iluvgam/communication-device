using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace FriendsChat;

// Contacts and DMs waiting for delivery, in the accounts database. A held DM is deleted as soon as one of the
// recipient's apps confirms it (dm-ack), and in any case after 30 days. Nothing else about DMs is kept.
sealed class DirectStore {
 public const int MaxPending=500;public const long MaxPendingBytes=100L*1024*1024;const long KeepMs=30L*86400*1000;
 readonly AccountStore accounts;
 public sealed record Held(long Row,string Sender,string MsgId,string Type,string Text,long Created,string Reply);
 public DirectStore(AccountStore accounts){this.accounts=accounts;using var db=Open();AccountStore.Run(db,"""
  CREATE TABLE IF NOT EXISTS contacts(owner INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,other INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,state TEXT NOT NULL,PRIMARY KEY(owner,other));
  CREATE TABLE IF NOT EXISTS dms(id INTEGER PRIMARY KEY,sender INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,recipient INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,msgid TEXT NOT NULL,type TEXT NOT NULL,text TEXT NOT NULL,blob BLOB,created INTEGER NOT NULL);
  CREATE INDEX IF NOT EXISTS dms_recipient ON dms(recipient,id);
  CREATE INDEX IF NOT EXISTS contacts_other ON contacts(other);
  CREATE TABLE IF NOT EXISTS profiles(account INTEGER PRIMARY KEY REFERENCES accounts(id) ON DELETE CASCADE,presence TEXT NOT NULL DEFAULT 'online',mood TEXT NOT NULL DEFAULT '',avatar BLOB,avatar_id TEXT NOT NULL DEFAULT '');
  """);
  // 0.14: the id a DM answers or changes (replies, edits, deletes, reactions). Added to databases made by older relays.
  using var columns=AccountStore.Command(db,"SELECT COUNT(*) FROM pragma_table_info('dms') WHERE name='reply'",[]);
  if(Convert.ToInt64(columns.ExecuteScalar())==0)AccountStore.Run(db,"ALTER TABLE dms ADD COLUMN reply TEXT NOT NULL DEFAULT ''");}
 SqliteConnection Open()=>accounts.Open();
 static long Now=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
 void Exec(string sql,params (string,object)[] args){using var db=Open();AccountStore.Run(db,sql,args);}
 // Each side has its own row: contact, incoming, outgoing or blocked.
 public string? State(long owner,long other){using var db=Open();using var c=AccountStore.Command(db,"SELECT state FROM contacts WHERE owner=$o AND other=$t",[("$o",owner),("$t",other)]);return c.ExecuteScalar() as string;}
 public void Set(long owner,long other,string? state){
  if(state==null)Exec("DELETE FROM contacts WHERE owner=$o AND other=$t",("$o",owner),("$t",other));
  else Exec("INSERT INTO contacts(owner,other,state) VALUES($o,$t,$s) ON CONFLICT(owner,other) DO UPDATE SET state=$s",("$o",owner),("$t",other),("$s",state));}
 public List<(long Id,string Name,string State,string Presence,string Mood,string Avatar)> Contacts(long owner){var list=new List<(long,string,string,string,string,string)>();using var db=Open();
  using var c=AccountStore.Command(db,"SELECT a.id,a.username,c.state,COALESCE(p.presence,'online'),COALESCE(p.mood,''),COALESCE(p.avatar_id,'') FROM contacts c JOIN accounts a ON a.id=c.other LEFT JOIN profiles p ON p.account=a.id WHERE c.owner=$o ORDER BY a.username COLLATE NOCASE",[("$o",owner)]);using var r=c.ExecuteReader();
  while(r.Read())list.Add((r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5)));return list;}
 // Profile: chosen presence, status line and picture. Kept until changed (or the account is deleted).
 public (string Presence,string Mood,string Avatar) Profile(long account){using var db=Open();using var c=AccountStore.Command(db,"SELECT presence,mood,avatar_id FROM profiles WHERE account=$a",[("$a",account)]);using var r=c.ExecuteReader();
  return r.Read()?(r.GetString(0),r.GetString(1),r.GetString(2)):("online","","");}
 public void SetProfile(long account,string presence,string mood)=>Exec("INSERT INTO profiles(account,presence,mood) VALUES($a,$p,$m) ON CONFLICT(account) DO UPDATE SET presence=$p,mood=$m",("$a",account),("$p",presence),("$m",mood));
 public string SetAvatar(long account,byte[]? picture){var id=picture==null?"":Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(picture))[..12].ToLowerInvariant();
  Exec("INSERT INTO profiles(account,avatar,avatar_id) VALUES($a,$b,$i) ON CONFLICT(account) DO UPDATE SET avatar=$b,avatar_id=$i",("$a",account),("$b",(object?)picture??DBNull.Value),("$i",id));return id;}
 public (string Id,byte[] Data)? Avatar(long account){using var db=Open();using var c=AccountStore.Command(db,"SELECT avatar_id,avatar FROM profiles WHERE account=$a AND avatar IS NOT NULL",[("$a",account)]);using var r=c.ExecuteReader();
  return r.Read()?(r.GetString(0),(byte[])r[1]):null;}
 // Everyone whose list mentions this account (to refresh their lists when it is deleted).
 public List<long> Watchers(long account){var list=new List<long>();using var db=Open();using var c=AccountStore.Command(db,"SELECT owner FROM contacts WHERE other=$a",[("$a",account)]);using var r=c.ExecuteReader();while(r.Read())list.Add(r.GetInt64(0));return list;}
 // Returns the time stamp, or null when the recipient already has too much waiting.
 public long? Hold(long sender,long recipient,string msgid,string type,string text,byte[]? blob,string reply=""){using var db=Open();
  using(var c=AccountStore.Command(db,"SELECT COUNT(*),COALESCE(SUM(LENGTH(blob)),0) FROM dms WHERE recipient=$r",[("$r",recipient)])){using var r=c.ExecuteReader();r.Read();if(r.GetInt64(0)>=MaxPending||r.GetInt64(1)+(blob?.Length??0)>MaxPendingBytes)return null;}
  var now=Now;AccountStore.Run(db,"INSERT INTO dms(sender,recipient,msgid,type,text,blob,created,reply) VALUES($s,$r,$m,$t,$x,$b,$c,$y)",("$s",sender),("$r",recipient),("$m",msgid),("$t",type),("$x",text),("$b",(object?)blob??DBNull.Value),("$c",now),("$y",reply));return now;}
 public List<Held> Pending(long recipient){var list=new List<Held>();using var db=Open();
  using var c=AccountStore.Command(db,"SELECT d.id,a.username,d.msgid,d.type,d.text,d.created,d.reply FROM dms d JOIN accounts a ON a.id=d.sender WHERE d.recipient=$r ORDER BY d.id",[("$r",recipient)]);using var r=c.ExecuteReader();
  while(r.Read())list.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetInt64(5),r.GetString(6)));return list;}
 public byte[]? Blob(long row){using var db=Open();using var c=AccountStore.Command(db,"SELECT blob FROM dms WHERE id=$i",[("$i",row)]);return c.ExecuteScalar() as byte[];}
 // Returns who sent it, so they can be told it was delivered.
 public long? Ack(long recipient,string msgid){using var db=Open();using var c=AccountStore.Command(db,"DELETE FROM dms WHERE recipient=$r AND msgid=$m RETURNING sender",[("$r",recipient),("$m",msgid)]);return c.ExecuteScalar() is long sender?sender:null;}
 public void Expire()=>Exec("DELETE FROM dms WHERE created<$cut",("$cut",Now-KeepMs));
 public void DropBetween(long sender,long recipient)=>Exec("DELETE FROM dms WHERE sender=$s AND recipient=$r",("$s",sender),("$r",recipient));
}

// The live side of DMs: who is online (any number of devices per account), contact changes, delivery and confirmations.
sealed class DirectHub(AccountStore accounts,DirectStore store,Bots bots) {
 // Edits: the app said (hello "edits") it understands replies, edits, deletes and reactions. Older apps never get the changes.
 sealed class Device(Peer peer,long account,string name,string token,bool edits){public readonly Peer Peer=peer;public readonly long Account=account;public readonly string Name=name,Token=token;public readonly bool Edits=edits;}
 static bool IsChange(string type)=>type is "edit" or "delete" or "react";
 sealed class Upload{public required string Id,Mime;public long Total;public int Seq;public (long Id,string Name)? To;public string? Problem;public readonly MemoryStream Data=new();}
 readonly object gate=new();readonly Dictionary<long,List<Device>> online=[];int connections;
 Device[] DevicesOf(long account){lock(gate)return online.TryGetValue(account,out var l)?[..l]:[];}
 bool IsOnline(long account){lock(gate)return online.ContainsKey(account);}
 bool Add(Device d){lock(gate){if(!online.TryGetValue(d.Account,out var l))online[d.Account]=l=[];l.Add(d);return l.Count==1;}}
 bool Remove(Device d){lock(gate){if(!online.TryGetValue(d.Account,out var l)||!l.Remove(d)||l.Count>0)return false;online.Remove(d.Account);return true;}}
 static async Task SendTo(Device d,Packet p){try{await d.Peer.Send(p);}catch{d.Peer.Abort();}}
 async Task Push(long account,Packet p,Device? except=null){foreach(var d in DevicesOf(account))if(d!=except)await SendTo(d,p);}
 // Presence, status line and picture are shown only to accepted contacts, never to pending requests or blocks.
 // "Appear offline" (invisible) looks exactly like offline.
 Packet ContactsPacket(long account)=>new("contacts",Text:JsonSerializer.Serialize(store.Contacts(account).Select(c=>{
  if(c.State!="contact")return new ContactInfo(c.Name,c.State,false,Bot:bots.IsBot(c.Name));
  var presence=!IsOnline(c.Id)||c.Presence=="invisible"?"offline":c.Presence;return new ContactInfo(c.Name,c.State,presence!="offline",presence,c.Mood,c.Avatar,bots.IsBot(c.Name));}).ToList()));
 // For summons: delivers to all of an account's DM connections; false if it has none (offline).
 public async Task<bool> PushToUser(string username,Packet p){if(accounts.Find(username) is not {} a)return false;var devices=DevicesOf(a.Id);foreach(var d in devices)await SendTo(d,p);return devices.Length>0;}
 Packet MePacket(long account){var p=store.Profile(account);return new Packet("me",Type:p.Presence,Text:p.Mood,Id:p.Avatar);}
 Task PushContacts(long account)=>Push(account,ContactsPacket(account));
 async Task PushPresence(long account){foreach(var c in store.Contacts(account))if(c.State=="contact")await PushContacts(c.Id);}

 // Hooks from the account endpoints: signing out, password changes and deletion end the matching DM connections.
 public async Task TokenEnded(string token){Device[] all;lock(gate)all=online.Values.SelectMany(l=>l).Where(d=>d.Token==token).ToArray();foreach(var d in all)await d.Peer.Close("Your sign-in has expired. Sign in again.");}
 public async Task SessionsEnded(long account){foreach(var d in DevicesOf(account))await d.Peer.Close("Your sign-in has expired. Sign in again.");}
 public List<long> Watchers(long account)=>store.Watchers(account);
 public async Task Deleted(long account,List<long> watchers){foreach(var d in DevicesOf(account))await d.Peer.Close("This account was deleted.");foreach(var w in watchers)await PushContacts(w);}

 public async Task Handle(HttpContext context){
  if(!context.WebSockets.IsWebSocketRequest){context.Response.StatusCode=400;return;}
  if(Interlocked.Increment(ref connections)>256){Interlocked.Decrement(ref connections);context.Response.StatusCode=503;return;}
  Device? me=null;Peer? peer=null;
  try{
   using var socket=await context.WebSockets.AcceptWebSocketAsync();peer=new Peer(socket);
   using var helloTimeout=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);helloTimeout.CancelAfter(TimeSpan.FromSeconds(30));
   var hello=await Protocol.Read(socket,helloTimeout.Token);
   if(hello?.Kind!="hello"||hello.Token is not {Length:>0}||accounts.Session(hello.Token) is not {} who){await peer.Close("Your sign-in has expired. Sign in again.");await Peer.Drain(socket);return;}
   me=new Device(peer,who.Id,who.Username,hello.Token,(hello.Text??"").Split(' ').Contains("edits"));peer.Name=who.Username;bool first=Add(me);
   await peer.Send(new Packet("ready",Text:"direct edits"));await peer.Send(MePacket(me.Account));await peer.Send(ContactsPacket(me.Account));
   if(first)await PushPresence(me.Account);
   store.Expire();await DeliverHeld(me);
   await Loop(me,socket,context.RequestAborted);
  }catch(Exception e) when(e is IOException or WebSocketException or OperationCanceledException or JsonException or ObjectDisposedException){}
  finally{try{if(me!=null&&Remove(me))await PushPresence(me.Account);}finally{peer?.Abort();Interlocked.Decrement(ref connections);}}
 }

 // An older app can't show changes, so they are dropped for it rather than waiting forever.
 async Task DeliverHeld(Device me){foreach(var h in store.Pending(me.Account)){
  if(IsChange(h.Type)&&!me.Edits){store.Ack(me.Account,h.MsgId);continue;}
  byte[]? blob=h.Type=="picture"?store.Blob(h.Row):null;if(h.Type=="picture"&&blob==null)continue;
  await SendDm(me.Peer,h.Sender,"in",h.MsgId,h.Type,h.Text,blob,h.Created,h.Reply);}}
 // A DM as packets: one "dm" (for pictures followed by dm-chunk pieces and dm-end). Room says "in" or "out" (sent from your other device).
 static async Task SendDm(Peer to,string other,string direction,string id,string type,string text,byte[]? blob,long time,string reply=""){
  await to.Send(new Packet("dm",Room:direction,Name:other,Text:text,Id:id,Type:type,Total:blob?.Length??0,Time:time,Reply:reply));if(blob==null)return;
  for(int seq=0,o=0;o<blob.Length;seq++,o+=Media.ChunkBytes)await to.Send(new Packet("dm-chunk",Id:id,Seq:seq,Data:Convert.ToBase64String(blob,o,Math.Min(Media.ChunkBytes,blob.Length-o))));
  await to.Send(new Packet("dm-end",Id:id));}
 // Only mutual contacts can message each other.
 (long Id,string Name)? Recipient(Device me,string? name){var a=Accounts.ValidUsername(name)?accounts.Find(name!):null;
  return a!=null&&a.Id!=me.Account&&store.State(me.Account,a.Id)=="contact"&&store.State(a.Id,me.Account)=="contact"?(a.Id,a.Username):null;}
 // Held first, so nothing is lost if delivery fails; the recipient's apps confirm with dm-ack, which deletes it.
 async Task Deliver(Device me,(long Id,string Name) to,string id,string type,string text,byte[]? blob,string reply=""){
  if(store.Hold(me.Account,to.Id,id,type,text,blob,reply) is not {} time){await me.Peer.Send(new Packet("error",Id:id,Text:$"{to.Name}'s inbox is full. Try again later."));return;}
  await me.Peer.Send(new Packet("dm-sent",Id:id,Time:time));bool change=IsChange(type);
  foreach(var d in DevicesOf(to.Id))if(d.Edits||!change)try{await SendDm(d.Peer,me.Name,"in",id,type,text,blob,time,reply);}catch{d.Peer.Abort();}
  foreach(var d in DevicesOf(me.Account))if(d!=me&&(d.Edits||!change))try{await SendDm(d.Peer,to.Name,"out",id,type,text,blob,time,reply);}catch{d.Peer.Abort();}}

 async Task Loop(Device me,WebSocket socket,CancellationToken ct){
  var window=DateTime.UtcNow;int packets=0,messages=0,contactChanges=0,profileChanges=0;var mediaWindow=DateTime.UtcNow;long mediaBudget=0;var lastTyping=DateTime.MinValue;var avatarBytes=new byte[Profiles.MaxAvatar];
  Upload? picture=null;var chunk=new byte[Media.ChunkBytes];
  Task Fail(string? id,string text)=>me.Peer.Send(new Packet("error",Id:id??"",Text:text));
  while(socket.State==WebSocketState.Open){
   var p=await Protocol.Read(socket,ct);if(p==null)break;
   if((DateTime.UtcNow-window).TotalSeconds>=10){window=DateTime.UtcNow;packets=messages=0;}
   // Picture pieces are bounded by the announced size (at most 5 MB) rather than counted.
   if(p.Kind=="dm-chunk"){
    if(picture==null||p.Id!=picture.Id||p.Seq!=picture.Seq||p.Data is not {Length:<=12288}||!Convert.TryFromBase64String(p.Data,chunk,out int n)||n==0||picture.Data.Length+n>picture.Total)throw new IOException("Invalid picture.");
    picture.Data.Write(chunk,0,n);picture.Seq++;continue;}
   if(++packets>300){await me.Peer.Close("Too many messages. Disconnected.");break;}
   switch(p.Kind){
    case "dm-ack":if(Media.ValidId(p.Id)&&store.Ack(me.Account,p.Id) is {} sender)await Push(sender,new Packet("dm-delivered",Id:p.Id,Name:me.Name));break;
    // Typing (at most every 2 s) and Seen go live to a mutual contact's devices; nothing is stored.
    case "dm-typing":if((DateTime.UtcNow-lastTyping).TotalSeconds>=2&&Recipient(me,p.Name) is {} typingTo){lastTyping=DateTime.UtcNow;await Push(typingTo.Id,new Packet("dm-typing",Name:me.Name));}break;
    case "dm-seen":if(Media.ValidId(p.Id)&&Recipient(me,p.Name) is {} reader)await Push(reader.Id,new Packet("dm-seen",Name:me.Name,Id:p.Id));break;
    case "profile":
     if(++profileChanges>60){await Fail(null,"Too many profile changes. Try again later.");break;}
     if(!Profiles.Presences.Contains(p.Type)||!Profiles.ValidMood(p.Text)){await Fail(null,$"Status line: up to {Profiles.MaxMood} characters.");break;}
     store.SetProfile(me.Account,p.Type,p.Text);await Push(me.Account,MePacket(me.Account));await PushPresence(me.Account);break;
    case "avatar":
     if(++profileChanges>60){await Fail(null,"Too many profile changes. Try again later.");break;}
     byte[]? newAvatar=null;if(p.Data is {Length:>0}){if(p.Data.Length>12288||!Convert.TryFromBase64String(p.Data,avatarBytes,out int size)||!Profiles.ValidAvatar(avatarBytes[..size])){await Fail(null,"Pictures must be a small JPEG or PNG.");break;}newAvatar=avatarBytes[..size];}
     store.SetAvatar(me.Account,newAvatar);await Push(me.Account,MePacket(me.Account));await PushPresence(me.Account);break;
    case "avatar-get":
     var whose=p.Name!=null&&p.Name.Equals(me.Name,StringComparison.OrdinalIgnoreCase)?(me.Account,me.Name):Recipient(me,p.Name);
     if(whose is not {} owner){await Fail(null,"You can only see your contacts' pictures.");break;}
     if(store.Avatar(owner.Id) is {} avatar)await me.Peer.Send(new Packet("avatar",Name:owner.Name,Id:avatar.Id,Data:Convert.ToBase64String(avatar.Data)));
     else await me.Peer.Send(new Packet("avatar",Name:owner.Name));break;
    case "contact-add" or "contact-accept" or "contact-decline" or "contact-remove" or "contact-block" or "contact-unblock":
     if(++contactChanges>60){await Fail(null,"Too many contact changes. Try again later.");break;}
     await ContactChange(me,p.Kind["contact-".Length..],p.Name);break;
    case "dm":
     if(!Media.ValidId(p.Id))throw new IOException("Invalid message.");
     if(++messages>30){await Fail(p.Id,"Slow down. Message not sent.");break;}
     // Reply: the id answered (optional for text and stickers), or the id an edit, delete or reaction is for.
     var reply=p.Reply??"";bool text=!string.IsNullOrWhiteSpace(p.Text)&&p.Text.Length<=2000;
     if(!(p.Type switch{"text"=>text&&(reply==""||Media.ValidId(reply)),"sticker"=>Stickers.Ids.Contains(p.Text)&&(reply==""||Media.ValidId(reply)),
       "edit"=>text&&Media.ValidId(reply),"delete"=>Media.ValidId(reply),"react"=>Reaction.Valid(p.Text)&&Media.ValidId(reply),_=>false})){await Fail(p.Id,"That message can't be sent.");break;}
     if(Recipient(me,p.Name) is not {} to){await Fail(p.Id,"You can only message your contacts.");break;}
     await Deliver(me,to,p.Id,p.Type,p.Type=="delete"?"":p.Text,null,reply);break;
    case "dm-picture":
     if((DateTime.UtcNow-mediaWindow).TotalMinutes>=10){mediaWindow=DateTime.UtcNow;mediaBudget=0;}
     if(picture!=null||!Media.ValidId(p.Id)||!Media.IsImage(p.Text)||p.Total<=0||p.Total>Media.MaxImage)throw new IOException("Invalid picture.");
     // A refused picture is still read to the end (and dropped) so the sender's connection stays in step.
     var target=Recipient(me,p.Name);var problem=target==null?"You can only message your contacts.":mediaBudget+p.Total>50L*1024*1024?"Too many pictures. Try again in a few minutes.":null;
     if(problem==null)mediaBudget+=p.Total;picture=new Upload{Id=p.Id,Mime=p.Text,Total=p.Total,To=target,Problem=problem};break;
    case "dm-end":
     if(picture==null||p.Id!=picture.Id||picture.Data.Length!=picture.Total)throw new IOException("Invalid picture.");
     var done=picture;picture=null;
     if(done.Problem!=null||done.To is not {} dest){await Fail(done.Id,done.Problem??"You can only message your contacts.");break;}
     await Deliver(me,dest,done.Id,"picture",done.Mime,done.Data.ToArray());break;
    default:throw new IOException("Invalid message.");
   }}}

 // Contact list changes. "No one with that username can be added" covers unknown names and people who blocked you alike.
 async Task ContactChange(Device me,string action,string? name){
  var other=Accounts.ValidUsername(name)?accounts.Find(name!):null;string? error=null;
  if(other==null||other.Id==me.Account)error=action=="add"?"No one with that username can be added.":"That person isn't in your contacts.";
  else{var mine=store.State(me.Account,other.Id);var theirs=store.State(other.Id,me.Account);long a=me.Account,b=other.Id;
   switch(action){
    case "add":
     if(mine=="contact")error=$"{other.Username} is already a contact.";
     else if(mine=="blocked")error=$"Unblock {other.Username} first.";
     else if(theirs=="blocked")error="No one with that username can be added.";
     else if(mine=="incoming"){store.Set(a,b,"contact");store.Set(b,a,"contact");}
     else{store.Set(a,b,"outgoing");store.Set(b,a,"incoming");}
     break;
    case "accept":if(mine!="incoming")error="There's no request from them.";else{store.Set(a,b,"contact");store.Set(b,a,"contact");}break;
    case "decline":if(mine!="incoming")error="There's no request from them.";else{store.Set(a,b,null);store.Set(b,a,null);}break;
    case "remove":if(mine is not ("contact" or "outgoing"))error="That person isn't in your contacts.";else{store.Set(a,b,null);if(theirs!="blocked")store.Set(b,a,null);}break;
    case "block":store.Set(a,b,"blocked");if(theirs!="blocked")store.Set(b,a,null);store.DropBetween(b,a);break;
    case "unblock":if(mine!="blocked")error="That person isn't blocked.";else store.Set(a,b,null);break;
   }}
  if(error!=null){await me.Peer.Send(new Packet("error",Text:error));return;}
  await PushContacts(me.Account);await PushContacts(other!.Id);}
}
