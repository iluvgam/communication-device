using System.Media;
using System.Net.WebSockets;
namespace FriendsChat;

// What the chat area shows: a room or a DM. The main window's composer sends through it.
abstract class Conversation {
 public readonly MessageList List=new(){Dock=DockStyle.Fill};
 // Unsent text while you look at another conversation; unread counts for the badges.
 public string Draft="";public int Unread,Mentions;
 public abstract string Key{get;}
 public abstract string Title{get;}
 public abstract string Placeholder{get;}
 public abstract bool CanSend{get;}
 public abstract bool CanPictures{get;}
 public virtual bool CanVideos=>false;
 public virtual bool CanStickers=>CanSend;
 public virtual bool CanNudge=>false;
 // The line under the message box: typing, uploads, receipts.
 public abstract string Activity{get;}
 // Plain-language privacy note shown beside it.
 public abstract string Note{get;}
 // Each returns null when it worked, "" when there is nothing to say, else what went wrong (the draft is kept).
 public abstract Task<string?> SendText(string text);
 public abstract Task<string?> SendSticker(string id);
 public abstract Task<string?> SendMedia(byte[] data,string mime);
 public abstract void Typing();
 // Replying to or editing a message (shown above the message box). Stash keeps what you were typing before an edit.
 public ChatItem? Replying,Editing;public string Stash="";
 public virtual bool CanReply=>false;
 public virtual bool CanEdit=>false;
 public virtual bool CanReact=>false;
 // Like SendText: null when it worked, else what went wrong ("" for nothing to say).
 public virtual Task<string?> Edit(ChatItem it,string text)=>Task.FromResult<string?>("");
 public virtual Task<string?> Delete(ChatItem it)=>Task.FromResult<string?>("");
 public virtual Task<string?> React(ChatItem it,string emoji)=>Task.FromResult<string?>("");
 protected Conversation(){List.CanReply=()=>CanReply;List.CanEdit=()=>CanEdit;List.CanReact=()=>CanReact;}
 public event Action? Changed;protected void Raise()=>Changed?.Invoke();
}

// One room you are in. Several can be open at once; each has its own connection, history, members and typing line.
sealed class RoomSession(string relay,string room,string password,string who,string? token):Conversation {
 public readonly string Relay=relay,Room=room,Password=password,Who=who;
 RelayClient? client;bool previewOn,sending,sendingMedia,closing;DateTime lastTypingSent,nudgeReady;string upload="";
 public bool Connected=>client!=null||previewOn;
 public string[] Features=>previewOn?["nudge","typing","media","stickers","accounts","bots","replies","edits","reactions","history"]:client?.Features??[];
 public string MyName{get;private set;}="";
 public string[] Members{get;private set;}=[];
 // Why the connection ended; shown with a Rejoin button.
 public string Problem{get;private set;}="";
 // Media arriving in pieces, keyed by sender and transfer id. Who is typing, with when we last heard.
 sealed class Incoming{public required string Mime,From;public long Total;public readonly MemoryStream Data=new();public DateTime Seen=DateTime.UtcNow;}
 readonly Dictionary<string,Incoming> incoming=[];readonly Dictionary<string,DateTime> typers=[];
 // counts (a message or nudge, not a join notice), mentions you, sound.
 public event Action<RoomSession,bool,bool,SystemSound?>? Alert;
 public event Action<RoomSession>? Shake;
 public event Action<string>? Notice;
 public event Action? SignInExpired;
 public override string Key=>"room:"+Room;
 public override string Title=>Room;
 public bool HasBots=>Features.Contains("bots");
 public override string Placeholder=>!Connected?"You're disconnected. Click Rejoin to send messages.":HasBots?$"Message #{Room}   (type @groki to bring in Groki, our AI)":$"Message #{Room}";
 public override bool CanSend=>Connected;
 public override bool CanPictures=>Connected&&Features.Contains("media")&&!sendingMedia;
 public override bool CanVideos=>CanPictures;
 public override bool CanStickers=>Connected&&Features.Contains("stickers");
 public override bool CanNudge=>Connected&&Features.Contains("nudge")&&DateTime.UtcNow>=nudgeReady;
 public override string Note=>Features.Contains("history")?"The relay can read messages. It keeps the last 100 in memory for people who join later, until the room ends.":"The relay can read messages, pictures and videos. Nothing is saved unless you click Save.";
 public override bool CanReply=>Connected&&Features.Contains("replies");
 public override bool CanEdit=>Connected&&Features.Contains("edits");
 public override bool CanReact=>Connected&&Features.Contains("reactions");
 // Edits, deletes and reactions show up when the relay sends them back to everyone.
 public override Task<string?> Edit(ChatItem it,string text)=>Do(c=>c.Edit(it.Id,text));
 public override Task<string?> Delete(ChatItem it)=>Do(c=>c.Delete(it.Id));
 public override Task<string?> React(ChatItem it,string emoji)=>Do(c=>c.React(it.Id,emoji));
 async Task<string?> Do(Func<RelayClient,Task> change){var current=client;if(current==null)return "You're disconnected. Click Rejoin first.";
  try{await change(current);return null;}catch(IOException e){return e.Message;}catch(Exception e) when(e is WebSocketException or OperationCanceledException or ObjectDisposedException){return "Not sent: connection lost.";}}
 // History from the relay keeps its own time; live messages use the time they arrive here.
 static DateTime? Sent(Packet p)=>p.Time>0?DateTimeOffset.FromUnixTimeMilliseconds(p.Time).LocalDateTime:null;
 bool MentionsMe(string text)=>MyName.Length>0&&text.Contains("@"+Names.Split(MyName).Name,StringComparison.OrdinalIgnoreCase);

 // Returns null when connected, else the problem. Rejoining keeps the history and only ever joins; it never creates a room by itself.
 public async Task<string?> Connect(bool creating,bool rejoining=false){if(client!=null)return null;closing=false;
  var current=new RelayClient();client=current;Problem="";Raise();
  try{await current.Connect(Relay,creating,Room,Password,Who,token);if(client!=current||closing){current.Dispose();return null;}
   MyName=token!=null?Who:Who.Trim()+(current.Features.Contains("accounts")?Accounts.GuestSuffix:"");List.Me=MyName;
   if(rejoining)List.Notice("Rejoined.");
   else if(List.Items.Count==0)List.Welcome("#"+Room,$"Welcome to #{Room}!",creating?$"You created #{Room}. Share the name and password with friends so they can join.":$"This is the start of your time in #{Room}. Say hi!");
   Raise();_=Receive(current);return null;}
  catch(Exception e){if(client==current)client=null;current.Dispose();var text=Friendly(e);
   if(rejoining&&text=="Room name or password is incorrect.")text="Could not rejoin: the room has ended. Use + to create it again.";
   if(rejoining){Problem=text;Raise();}
   if(text.Contains("Sign in again"))SignInExpired?.Invoke();
   return text;}}
 public static string Friendly(Exception e)=>e switch{OperationCanceledException=>"Connection timed out. Check the relay address and internet access.",WebSocketException=>"Could not reach the relay. Check its address and your internet. Keep certificate checks on.",_=>e.Message};
 async Task Receive(RelayClient current){string? reason=null;
  try{while(true){var p=await current.Read();if(p==null)break;
   if(p.Kind=="message"){reason=null;bool chat=p.Name!="Room";if(!chat)List.Notice(p.Text);else{typers.Remove(p.Name);if(List.Find(p.Id)==null)List.Say(p.Name,p.Text,id:p.Id,reply:p.Reply);}Alert?.Invoke(this,chat,chat&&MentionsMe(p.Text),null);}
   // Messages from before you joined (or while you were disconnected); anything already shown is skipped. No alerts.
   else if(p.Kind=="history"&&Media.ValidId(p.Id)&&List.Find(p.Id)==null){if(p.Type=="sticker"){if(Stickers.Ids.Contains(p.Text))List.ShowSticker(p.Name,p.Text,Sent(p),p.Id,p.Reply);}else List.Say(p.Name,p.Text,Sent(p),p.Id,p.Reply,p.Edited);}
   else if(p.Kind=="edit")List.Edit(p.Id,p.Text);
   else if(p.Kind=="delete")List.Remove(p.Id);
   else if(p.Kind=="reactions")List.SetReactions(p.Id,Reaction.Parse(p.Text));
   else if(p.Kind=="nudge"){Nudged(Names.Split(p.Name).Name+" sent a nudge!");Alert?.Invoke(this,true,false,SystemSounds.Exclamation);}
   else if(p.Kind=="typing")typers[p.Name]=DateTime.UtcNow;
   else if(p.Kind=="sticker"&&Stickers.Ids.Contains(p.Text)){reason=null;typers.Remove(p.Name);if(List.Find(p.Id)==null)List.ShowSticker(p.Name,p.Text,id:p.Id,reply:p.Reply);Alert?.Invoke(this,true,false,null);}
   else if(p.Kind.StartsWith("media-"))Piece(p);
   else if(p.Kind=="members")Members=p.Text.Split('\n',StringSplitOptions.RemoveEmptyEntries);
   else if(p.Kind=="error"){if(p.Id.Length==0)reason=p.Text;Notice?.Invoke(p.Text);}
   Raise();}}
  catch{}
  // The relay's last error explains the drop; otherwise it was the network or a relay restart.
  finally{if(!closing&&client==current){var text=reason??"Connection lost (internet or relay restart). Click Rejoin to try again.";List.Notice("Disconnected: "+text);End(text);}}}
 void End(string reason){var old=client;client=null;old?.Dispose();Problem=reason;typers.Clear();incoming.Clear();Members=[];Raise();}
 // A chosen leave says goodbye so others see it at once.
 public Task Leave(){closing=true;previewOn=false;var old=client;client=null;Members=[];return old?.Close()??Task.CompletedTask;}
 // Media pieces from the relay. Anything that does not add up is dropped quietly; nothing is written to disk.
 void Piece(Packet p){var key=p.Name+"/"+p.Id;
  if(p.Kind=="media-start"){if(Media.ValidId(p.Id)&&p.Total>0&&p.Total<=Media.Limit(p.Text))incoming[key]=new Incoming{Mime=p.Text,From=p.Name,Total=p.Total};}
  else if(p.Kind=="media-chunk"&&incoming.TryGetValue(key,out var t)){
   byte[] bytes;try{bytes=Convert.FromBase64String(p.Data??"");}catch(FormatException){incoming.Remove(key);return;}
   if(t.Data.Length+bytes.Length>t.Total)incoming.Remove(key);else{t.Data.Write(bytes);t.Seen=DateTime.UtcNow;}}
  else if(p.Kind=="media-end"&&incoming.Remove(key,out var done)&&done.Data.Length==done.Total){typers.Remove(done.From);List.ShowMedia(done.From,done.Mime,done.Data.ToArray(),id:p.Id);Alert?.Invoke(this,true,false,null);}
  else if(p.Kind=="media-cancel")incoming.Remove(key);}
 public override string Activity{get{var now=DateTime.UtcNow;
  foreach(var stale in typers.Where(t=>(now-t.Value).TotalSeconds>5).Select(t=>t.Key).ToList())typers.Remove(stale);
  foreach(var stale in incoming.Where(t=>(now-t.Value.Seen).TotalSeconds>60).Select(t=>t.Key).ToList())incoming.Remove(stale);
  var parts=new List<string>();var who=Protocol.DescribeTyping(typers.Keys.Select(n=>Names.Split(n).Name).ToList());if(who.Length>0)parts.Add(who);
  foreach(var x in incoming.Values)parts.Add($"Receiving {(Media.IsVideo(x.Mime)?"video":"picture")} from {Names.Split(x.From).Name}… {100*x.Data.Length/Math.Max(1,x.Total)}%");
  if(upload.Length>0)parts.Add(upload);
  return string.Join("   •   ",parts);}}

 public override async Task<string?> SendText(string text){var current=client;if(current==null)return "You're disconnected. Click Rejoin first; your text is kept.";if(sending)return "";sending=true;
  try{await current.Send(text,Replying?.Id??"");return null;}
  catch(IOException e) when(e.Message.StartsWith("Message must")){return e.Message;}
  catch{if(!closing&&client==current){List.Notice("Disconnected: connection lost.");End("Message not sent: connection lost. Your text is kept. Click Rejoin.");}return "Message not sent: connection lost. Your text is kept. Click Rejoin.";}
  finally{sending=false;}}
 public override async Task<string?> SendSticker(string id){var current=client;if(current==null)return "";
  try{await current.SendSticker(id,Replying?.Id??"");return null;}catch(Exception e) when(e is WebSocketException or OperationCanceledException or ObjectDisposedException){return "";}}
 // Pictures arrive here already shrunk and re-saved; videos go as they are, up to 25 MB.
 public override async Task<string?> SendMedia(byte[] data,string mime){var current=client;if(current==null)return "";if(sendingMedia)return "Wait for the current upload to finish.";
  sendingMedia=true;var what=Media.IsVideo(mime)?"video":"picture";Raise();
  var progress=new Progress<int>(pc=>{upload=$"Sending {what}… {pc}%";Raise();});
  try{var id=await current.SendMedia(data,mime,progress);if(client==current)List.ShowMedia(MyName,mime,data,id:id);return null;}
  catch(IOException e){return e.Message;}
  catch(Exception e) when(e is WebSocketException or OperationCanceledException or ObjectDisposedException){return "";}
  finally{sendingMedia=false;upload="";Raise();}}
 // Nudge, MSN Messenger style: a bold line and a quick window shake. The relay allows one per 15 s per person.
 public async Task SendNudge(){var current=client;if(current==null||!CanNudge)return;nudgeReady=DateTime.UtcNow.AddSeconds(15);Raise();
  try{await current.Signal("nudge");Nudged("You sent a nudge.");}catch(Exception e) when(e is WebSocketException or OperationCanceledException or ObjectDisposedException){return;}
  await Task.Delay(15000);Raise();}
 void Nudged(string text){List.Highlight(text);Shake?.Invoke(this);}
 // Tells the others we are typing, at most every 3 s (the relay also limits it).
 public override void Typing(){var current=client;if(current==null||(DateTime.UtcNow-lastTypingSent).TotalSeconds<3)return;lastTypingSent=DateTime.UtcNow;_=current.Signal("typing").ContinueWith(t=>_=t.Exception,TaskContinuationOptions.OnlyOnFaulted);}
 // --preview only: looks connected, with sample members and someone typing. Nothing touches the network.
 internal void PreviewState(string me,string[] members,params string[] typing){previewOn=true;MyName=me;List.Me=me;Members=members;foreach(var t in typing)typers[t]=DateTime.UtcNow.AddMinutes(10);}
}

// A DM conversation with one friend, shown in the main window. History comes from this PC; receipts and typing from the Messenger.
sealed class DirectConversation:Conversation {
 readonly Messenger hub;public readonly string Contact;
 // The line under the box: "Sam is typing…" for 5 s after a signal, else Sent/Delivered/Seen for your last message, then their status.
 DateTime typingUntil;string receipt="",upload="";string? lastOut,lastIn,lastSeenSent;bool sending,sendingMedia;
 // True while this conversation is on screen in the active window (then incoming messages are marked seen).
 public Func<bool> Visible=()=>false;
 public DirectConversation(Messenger hub,string contact){this.hub=hub;Contact=contact;List.AvatarFor=hub.Avatar;List.Me=hub.Me;
  List.BeginUpdate();Opening();foreach(var e in hub.History(contact))Show(e,quiet:true);List.EndUpdate();receipt="";
  if(hub.previewTyping)typingUntil=DateTime.UtcNow.AddMinutes(5);
  List.ExtendMenu=(m,_)=>m.Item("Clear history on this PC…",ClearHistory,danger:true);}
 void Opening()=>List.Welcome(Contact,Contact,$"This is the beginning of your direct message history with {Contact}.");
 public override string Key=>"dm:"+Contact.ToLowerInvariant();
 public override string Title=>Contact;
 ContactInfo? Info=>hub.Find(Contact);
 public override bool CanSend=>Info is {State:"contact"}&&hub.Online;
 public override bool CanPictures=>CanSend&&!sendingMedia;
 public override string Placeholder=>Info is {State:"contact"}?$"Message @{Contact}":$"{Contact} is no longer in your friends";
 public override string Note=>"DMs are kept only on this PC, encrypted for your Windows account.";
 public override string Activity{get{
  if(DateTime.UtcNow<typingUntil)return $"{Contact} is typing…";
  var c=Info;var status=c is not {State:"contact"}?$"{Contact} is no longer in your friends.":!hub.Online?"Connecting to the relay…":c.Online?$"{Contact} is {Profiles.Label(c.Presence).ToLowerInvariant()}.":$"{Contact} is offline. Messages wait on the relay until they're back (up to 30 days).";
  var parts=new List<string>();if(upload.Length>0)parts.Add(upload);if(receipt.Length>0)parts.Add(receipt);parts.Add(status);return string.Join("   •   ",parts);}}
 public void Show(DmEntry e,bool quiet=false){var who=e.Out?hub.Me:Contact;var reply=e.Reply??"";
  // Changes to earlier messages: only the person who wrote a message can edit or delete it.
  if(e.Type is "edit" or "delete" or "react"){
   if(e.Type=="edit")List.Edit(reply,e.Text,who);else if(e.Type=="delete")List.Remove(reply,who);else if(Reaction.Valid(e.Text))List.ToggleReaction(reply,e.Text,who);
   if(!quiet)Raise();return;}
  if(e.Out){lastOut=e.Id;receipt="Sent";}else{lastIn=e.Id;typingUntil=DateTime.MinValue;if(!quiet&&Visible())SendSeen();}
  switch(e.Type){
   case "sticker":List.ShowSticker(who,e.Text,e.Local,e.Id,reply);break;
   case "picture":try{List.ShowMedia(who,e.Text,Convert.FromBase64String(e.Data??""),e.Local,e.Id,reply);}catch(FormatException){}break;
   default:List.Say(who,e.Text,e.Local,e.Id,reply);break;}
  if(!quiet)Raise();}
 public override bool CanReply=>CanSend&&hub.Edits;
 public override bool CanEdit=>CanSend&&hub.Edits;
 public override bool CanReact=>CanSend&&hub.Edits;
 public override async Task<string?> Edit(ChatItem it,string text)=>!CanEdit?"":text.Length>2000?"Message must be 1–2,000 characters.":await hub.Send(Contact,"edit",text,reply:it.Id)?null:"Not changed: not connected to the relay right now.";
 public override async Task<string?> Delete(ChatItem it)=>!CanEdit?"":await hub.Send(Contact,"delete","",reply:it.Id)?null:"Not deleted: not connected to the relay right now.";
 public override async Task<string?> React(ChatItem it,string emoji)=>!CanReact?"":await hub.Send(Contact,"react",emoji,reply:it.Id)?null:"Not sent: not connected to the relay right now.";
 public void Failed(string reason){List.Fail("Not sent: "+reason);receipt="";Raise();}
 public void TypingFrom(){typingUntil=DateTime.UtcNow.AddSeconds(5);Raise();}
 public void Delivered(string id){if(id==lastOut&&!receipt.StartsWith("Seen")){receipt="Delivered";Raise();}}
 public void SeenBy(string id){if(id==lastOut){receipt=$"Seen {DateTime.Now:HH:mm}";Raise();}}
 // Tells the friend you've seen their latest message (only when receipts are on, and only once per message).
 public void SendSeen(){if(lastIn!=null&&lastIn!=lastSeenSent&&CanSend){lastSeenSent=lastIn;hub.SendSeen(Contact,lastIn);}}
 public override void Typing(){if(CanSend)hub.SendTyping(Contact);}
 public override async Task<string?> SendText(string text){if(!CanSend)return "You can't message this person right now.";if(sending)return "";sending=true;
  try{return await hub.Send(Contact,"text",text,reply:Replying?.Id??"")?null:"Not sent: not connected to the relay right now. Your text is kept.";}finally{sending=false;}}
 public override async Task<string?> SendSticker(string id)=>!CanSend?"":await hub.Send(Contact,"sticker",id,reply:Replying?.Id??"")?null:"Not sent: not connected to the relay right now.";
 public override async Task<string?> SendMedia(byte[] data,string mime){if(!CanSend)return "";if(Media.IsVideo(mime))return "Videos can be sent in rooms. DMs take pictures.";
  sendingMedia=true;Raise();var progress=new Progress<int>(pc=>{upload=$"Sending picture… {pc}%";Raise();});
  try{return await hub.Send(Contact,"picture","",data,mime,progress)?null:"Not sent: not connected to the relay right now.";}
  finally{sendingMedia=false;upload="";Raise();}}
 void ClearHistory(){if(MessageBox.Show(List.FindForm(),$"Delete your conversation with {Contact} from this PC?","3DFx Vista",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
  hub.ClearHistory(Contact);List.Clear();Opening();receipt="";Raise();}
}
