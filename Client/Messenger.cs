using System.Drawing.Drawing2D;
using System.Media;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace FriendsChat;

// One DM as kept on this PC. Type: text, sticker (Text = sticker id) or picture (Text = mime, Data = base64 bytes),
// or a change to an earlier one (Reply = its id): edit (Text = new text), delete, or react (Text = emoji; again takes it back).
// Reply on a message is the id it answers.
sealed record DmEntry(string Id,bool Out,string Type,string Text,long Time,string? Data=null,string? Reply=null){
 public bool IsChange=>Type is "edit" or "delete" or "react";
 public DateTime Local=>DateTimeOffset.FromUnixTimeMilliseconds(Time).LocalDateTime;
}

// DM history lives on this PC only: one file per conversation, each line encrypted with Windows (DPAPI) for this Windows user.
static class DirectHistory {
 static readonly byte[] purpose=Encoding.UTF8.GetBytes("3DFx Vista DM history");
 // Tests point this at a scratch folder.
 internal static string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"3DFx","History");
 static string File(string relay,string me,string other)=>Path.Combine(Root,RelayClient.ValidateUrl(relay).Host,me.ToLowerInvariant(),other.ToLowerInvariant()+".dat");
 public static List<DmEntry> Load(string relay,string me,string other){var list=new List<DmEntry>();
  try{foreach(var line in System.IO.File.ReadLines(File(relay,me,other))){
    try{if(JsonSerializer.Deserialize<DmEntry>(ProtectedData.Unprotect(Convert.FromBase64String(line),purpose,DataProtectionScope.CurrentUser)) is {} e)list.Add(e);}
    catch(Exception x) when(x is FormatException or CryptographicException or JsonException){}}}
  catch(Exception x) when(x is IOException or UnauthorizedAccessException){}
  return list;}
 public static void Append(string relay,string me,string other,DmEntry e){var path=File(relay,me,other);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  System.IO.File.AppendAllText(path,Convert.ToBase64String(ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(e),purpose,DataProtectionScope.CurrentUser))+"\r\n");}
 public static void Delete(string relay,string me,string other){try{System.IO.File.Delete(File(relay,me,other));}catch(Exception x) when(x is IOException or UnauthorizedAccessException){}}
}

// The signed-in side of the app: keeps the DM connection open (reconnecting on its own), the friends list and unread counts.
// It never creates rooms or sends anything by itself; it only listens, and confirms each received DM once it is saved here.
sealed class Messenger(Form owner,Func<bool> soundOn) {
 public event Action? Changed;
 public event Action<string,bool>? Notice;
 public event Action<string>? SignedOut;
 // DM traffic for the conversations on screen, by friend's name: new or sent messages, failures, typing and receipts.
 public event Action<string,DmEntry>? Message;
 public event Action<string,string>? Failed;
 public event Action<string>? TypingFrom;
 public event Action<string,string>? DeliveredTo,SeenBy;
 // The app says which conversation is on screen in the active window; messages there don't count as unread.
 public Func<string,bool> IsViewing=_=>false;
 public List<ContactInfo> Contacts{get;private set;}=[];
 public bool Online{get;private set;}
 // The relay passes on replies, edits, deletes and reactions (0.14 and later).
 public bool Edits=>preview||client?.Features.Contains("edits")==true;
 // The relay passes feedback to AI members (thumbs up / down, the experience survey).
 public bool FeedbackOn=>preview||client?.Features.Contains("feedback")==true;
 public async Task<bool> SendFeedback(string bot,string type,string target,string where,int rating=0,string comment=""){if(preview)return true;var c=client;if(c==null)return false;
  try{await c.SendFeedback(bot,type,target,where,rating,comment);return true;}catch(Exception x) when(x is WebSocketException or OperationCanceledException or ObjectDisposedException){return false;}}
 public bool Running=>run!=null;
 public string Me{get;private set;}="";
 // Tests connect to a local ws:// relay through this; the app always uses the relay's HTTPS address.
 internal Uri? Endpoint{get;set;}
 string relay="",token="";DirectClient? client;CancellationTokenSource? run;bool preview;
 readonly Dictionary<string,int> unread=new(StringComparer.OrdinalIgnoreCase);
 readonly Dictionary<string,HashSet<string>> seen=new(StringComparer.OrdinalIgnoreCase);
 readonly Dictionary<string,string> outbox=[];
 readonly Dictionary<string,(Packet Head,MemoryStream Data)> arriving=[];
 readonly Dictionary<string,List<DmEntry>> previewHistory=new(StringComparer.OrdinalIgnoreCase);
 // Your own profile as the relay last confirmed it, and contacts' pictures by picture id (fetched once, kept in memory).
 public string MyPresence{get;private set;}="online";public string MyMood{get;private set;}="";string myAvatar="";
 readonly Dictionary<string,Image> avatars=[];readonly HashSet<string> asked=[];DateTime lastTypingSent;
 // Seen receipts: when off you neither send nor show them.
 public Func<bool> ReceiptsOn=()=>true;

 public int Unread(string name)=>unread.GetValueOrDefault(name);
 public ContactInfo? Find(string name)=>Contacts.FirstOrDefault(c=>c.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
 public void Start(string relayUrl,string me,string signIn){Stop();relay=relayUrl;Me=me;token=signIn;run=new();_=Loop(run.Token);}
 public void Stop(){run?.Cancel();run=null;var c=client;client=null;if(c!=null)_=c.Close();Online=false;Contacts=[];unread.Clear();seen.Clear();arriving.Clear();outbox.Clear();Changed?.Invoke();}

 async Task Loop(CancellationToken stop){int delay=3;
  while(!stop.IsCancellationRequested){var c=new DirectClient();
   try{await c.Connect(Endpoint??DirectClient.UrlFor(relay),token);if(stop.IsCancellationRequested)break;client=c;Online=true;delay=3;Changed?.Invoke();
    while(await c.Read() is {} p){if(stop.IsCancellationRequested)break;Handle(c,p);}}
   catch(IOException e) when(IsSignOut(e.Message)){if(!stop.IsCancellationRequested)SignedOut?.Invoke(e.Message);return;}
   catch(Exception e) when(e is IOException or WebSocketException or OperationCanceledException or ObjectDisposedException or JsonException){}
   finally{if(client==c)client=null;c.Dispose();if(!stop.IsCancellationRequested){Online=false;Changed?.Invoke();}}
   try{await Task.Delay(TimeSpan.FromSeconds(delay),stop);}catch(OperationCanceledException){return;}delay=Math.Min(60,delay*2);}}
 static bool IsSignOut(string text)=>text.Contains("Sign in again")||text.Contains("account was deleted");

 void Handle(DirectClient c,Packet p){
  switch(p.Kind){
   case "contacts":
    var before=Contacts;Contacts=ContactInfo.Parse(p.Text);
    foreach(var n in Contacts.Where(x=>x.State=="incoming"&&!before.Any(b=>b.Name==x.Name&&b.State=="incoming"))){Notice?.Invoke($"{n.Name} sent you a friend request.",false);Aero.Flash(owner);}
    foreach(var x in Contacts)Want(x.Name,x.Avatar);
    Changed?.Invoke();break;
   case "me":MyPresence=p.Type;MyMood=p.Text;myAvatar=p.Id;Want(Me,myAvatar);Changed?.Invoke();break;
   case "avatar":
    if(p.Id.Length>0&&!avatars.ContainsKey(p.Id)){try{if(Avatars.Decode(Convert.FromBase64String(p.Data)) is {} img)avatars[p.Id]=img;}catch(FormatException){}}
    Changed?.Invoke();break;
   case "dm-typing":TypingFrom?.Invoke(p.Name);break;
   case "dm-delivered":DeliveredTo?.Invoke(p.Name,p.Id);break;
   case "dm-seen":if(ReceiptsOn())SeenBy?.Invoke(p.Name,p.Id);break;
   case "dm" when p.Type=="picture":if(Media.ValidId(p.Id)&&p.Total>0&&p.Total<=Media.MaxImage)arriving[p.Id]=(p,new MemoryStream());break;
   case "dm":if(Media.ValidId(p.Id)&&p.Type is "text" or "sticker" or "edit" or "delete" or "react")Arrived(c,p,new DmEntry(p.Id,p.Room=="out",p.Type,p.Text,p.Time,Reply:p.Reply is {Length:>0} r?r:null));break;
   case "dm-chunk":
    if(!arriving.TryGetValue(p.Id,out var part))break;byte[] bytes;try{bytes=Convert.FromBase64String(p.Data);}catch(FormatException){arriving.Remove(p.Id);break;}
    if(part.Data.Length+bytes.Length>part.Head.Total)arriving.Remove(p.Id);else part.Data.Write(bytes);break;
   case "dm-end":
    if(arriving.Remove(p.Id,out var done)&&done.Data.Length==done.Head.Total)Arrived(c,done.Head,new DmEntry(p.Id,done.Head.Room=="out",done.Head.Type,done.Head.Text,done.Head.Time,Convert.ToBase64String(done.Data.ToArray()),done.Head.Reply is {Length:>0} r2?r2:null));break;
   case "dm-sent":outbox.Remove(p.Id);break;
   case "error":
    if(IsSignOut(p.Text)){SignedOut?.Invoke(p.Text);break;}
    if(outbox.Remove(p.Id,out var to)&&Failed!=null)Failed(to,p.Text);else Notice?.Invoke(p.Text,true);break;
  }}
 // Saved here first, then confirmed to the relay (which then deletes its copy). Duplicates (e.g. after a reconnect) are only confirmed again.
 void Arrived(DirectClient c,Packet head,DmEntry e){var other=head.Name;
  if(!Seen(other).Contains(e.Id)){
   try{DirectHistory.Append(relay,Me,other,e);}catch(Exception x) when(x is IOException or UnauthorizedAccessException or CryptographicException){Notice?.Invoke("Could not save a DM on this PC; it will arrive again later.",true);return;}
   Seen(other).Add(e.Id);
   Message?.Invoke(other,e);
   if(!e.Out&&!e.IsChange&&!IsViewing(other)){unread[other]=Unread(other)+1;Changed?.Invoke();Aero.Flash(owner);if(soundOn())SystemSounds.Asterisk.Play();}}
  _=c.Ack(e.Id).ContinueWith(t=>_=t.Exception,TaskContinuationOptions.OnlyOnFaulted);}
 HashSet<string> Seen(string other){if(!seen.TryGetValue(other,out var ids))seen[other]=ids=History(other).Select(x=>x.Id).ToHashSet();return ids;}
 public List<DmEntry> History(string other)=>preview?previewHistory.GetValueOrDefault(other)??[]:DirectHistory.Load(relay,Me,other);
 public void ClearHistory(string other){if(!preview)DirectHistory.Delete(relay,Me,other);seen.Remove(other);}

 public void MarkRead(string name){if(unread.Remove(name))Changed?.Invoke();}
 // Pictures: asked for once per picture id; null means "draw the initial".
 void Want(string name,string id){if(id.Length>0&&!avatars.ContainsKey(id)&&asked.Add(id)&&client is {} c)_=c.GetAvatar(name).ContinueWith(t=>_=t.Exception,TaskContinuationOptions.OnlyOnFaulted);}
 public Image? Avatar(string name){var id=name.Equals(Me,StringComparison.OrdinalIgnoreCase)?myAvatar:Find(name)?.Avatar??"";return id.Length>0&&avatars.TryGetValue(id,out var img)?img:null;}
 public async Task SetProfile(string presence,string mood){if(client is {} c)try{await c.SetProfile(presence,mood);}catch(Exception e) when(e is WebSocketException or OperationCanceledException or ObjectDisposedException){Notice?.Invoke("Not connected to the relay right now.",true);}}
 public async Task SetAvatar(byte[]? picture){if(client is {} c)try{await c.SetAvatar(picture);}catch(Exception e) when(e is WebSocketException or OperationCanceledException or ObjectDisposedException){Notice?.Invoke("Not connected to the relay right now.",true);}}
 public void SendTyping(string to){if(client is not {} c||(DateTime.UtcNow-lastTypingSent).TotalSeconds<3)return;lastTypingSent=DateTime.UtcNow;_=c.Typing(to).ContinueWith(t=>_=t.Exception,TaskContinuationOptions.OnlyOnFaulted);}
 public void SendSeen(string from,string lastId){if(ReceiptsOn()&&client is {} c)_=c.Seen(from,lastId).ContinueWith(t=>_=t.Exception,TaskContinuationOptions.OnlyOnFaulted);}
 public async Task Contact(string action,string name){var c=client;if(c==null){Notice?.Invoke("Not connected to the relay yet.",true);return;}
  try{await c.Contact(action,name);}catch(Exception e) when(e is WebSocketException or OperationCanceledException or ObjectDisposedException){Notice?.Invoke("Not connected to the relay right now.",true);}}

 // Sending: shown and saved here straight away; the relay confirms with dm-sent or explains with an error.
 // reply: the message answered, or for edit/delete/react the one being changed.
 public async Task<bool> Send(string to,string type,string text,byte[]? picture=null,string? mime=null,IProgress<int>? progress=null,string reply=""){
  var c=client;if(c==null)return false;
  try{var id=type switch{"sticker"=>await c.SendSticker(to,text,reply),"picture"=>await c.SendPicture(to,picture!,mime!,progress),"text"=>await c.SendText(to,text,reply),_=>await c.SendChange(to,type,reply,text)};
   outbox[id]=to;var e=new DmEntry(id,true,type,type=="picture"?mime!:text,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),picture!=null?Convert.ToBase64String(picture):null,reply.Length>0?reply:null);
   Seen(to).Add(id);try{DirectHistory.Append(relay,Me,to,e);}catch(Exception x) when(x is IOException or UnauthorizedAccessException or CryptographicException){}
   Message?.Invoke(to,e);return true;}
  catch(Exception x) when(x is WebSocketException or OperationCanceledException or ObjectDisposedException){return false;}}

 // --preview: sample friends and one sample conversation, all in memory.
 public void Preview(string me){preview=true;Me=me;Online=true;
  Contacts=[new("Jordan","contact",false,"offline"),new("Riley","incoming",false),new("Sam","contact",true,"online","Counting down to Friday 🎉","sam"),new("Morgan","contact",true,"busy","In a meeting until 3","morgan"),new("Casey","outgoing",false),new("groki","contact",true,"online","bored af, someone talk to me",Bot:true)];unread["Jordan"]=1;
  MyMood="Building 3DFx 1.0";myAvatar="alex";avatars["alex"]=SamplePicture(0x2B86D7,0x9FD8FF);avatars["sam"]=SamplePicture(0xD35400,0xFFD28A);avatars["morgan"]=SamplePicture(0x2E7D32,0xB9F0A0);
  long t=DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeMilliseconds();
  previewTyping=true;previewHistory["Sam"]=[new("a1",false,"text","Are you coming to friday-friends tonight?",t),new("a2",true,"text","Yes! Around 8.",t+60_000,Reply:"a1"),new("a3",false,"sticker","party",t+120_000),new("a4",true,"text","See you there 👋",t+180_000),new("a5",false,"react","❤️",t+200_000,Reply:"a4")];
  Changed?.Invoke();}
 internal bool previewTyping;
 // Simple drawn "photos" for --preview.
 static Image SamplePicture(int dark,int light){var bmp=new Bitmap(Avatars.Size,Avatars.Size);using var g=Graphics.FromImage(bmp);g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var b=new LinearGradientBrush(new Rectangle(0,0,96,96),Aero.C(light),Aero.C(dark),90f))g.FillRectangle(b,0,0,96,96);
  using(var face=new SolidBrush(Aero.C(0xFFFFFF,200))){g.FillEllipse(face,30,18,36,36);g.FillEllipse(face,14,58,68,60);}return bmp;}
}
