using System.Drawing.Drawing2D;
using System.Media;
using System.Runtime.InteropServices;
namespace FriendsChat;
static class Program {
 // --preview fills the window with sample content for screenshots; it never connects. After it: room (default), dm, friends, guest, login,
 // settings, join, addfriend, profile, search, appearance, windows, or the path of a video to use as the sample video.
 // --tray starts hidden in the system tray (used by "Start with Windows").
 // One 3DFx per Windows user: opening it again shows the one that's running (see SingleInstance).
 [STAThread] static void Main(string[] args){Velopack.VelopackApp.Build().Run();var preview=args.Contains("--preview");
  if(!preview&&!SingleInstance.First()){if(!args.Contains("--tray"))SingleInstance.ShowOther();return;}
  ApplicationConfiguration.Initialize();var shell=new Shell(preview,args.SkipWhile(a=>a!="--preview").Skip(1).FirstOrDefault(),args.Contains("--tray"));
  if(!preview)SingleInstance.Listen(()=>{try{shell.BeginInvoke(shell.Restore);}catch(InvalidOperationException){}});
  Application.Run(shell);}
}

// Opening 3DFx again (a shortcut, the Start menu, Start with Windows) used to start another copy every time: each one
// sat in the tray, signed in as you, got every DM, saved it and notified you. Now the first copy holds a named mutex for
// this Windows user, and a second one just asks it (a named event) to come to the front, then quits.
static class SingleInstance {
 internal static string Name="3DFxVista-"+Environment.UserName;  // tests use their own
 static Mutex? mine;
 public static bool First(){var m=new Mutex(true,@"Local\"+Name,out var created);if(created){mine=m;return true;}m.Dispose();return false;}
 public static void ShowOther(){try{using var e=EventWaitHandle.OpenExisting(@"Local\"+Name+"-show");e.Set();}catch(Exception x) when(x is WaitHandleCannotBeOpenedException or UnauthorizedAccessException){}}
 public static void Listen(Action show){var e=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\"+Name+"-show");
  new Thread(()=>{while(e.WaitOne())show();}){IsBackground=true,Name="3DFx show"}.Start();}
}

// Lets one notification through per conversation in a burst: the first message shows; the rest within the quiet time are
// held back, and the next one after it says how many there were (Allow returns that count, or null to stay quiet).
sealed class NotifyGate(TimeSpan quiet) {
 readonly Dictionary<string,(DateTime Last,int Held)> seen=new(StringComparer.OrdinalIgnoreCase);
 public int? Allow(string key,DateTime now){
  if(seen.TryGetValue(key,out var s)&&now-s.Last<quiet){seen[key]=(s.Last,s.Held+1);return null;}
  var held=s.Held;seen[key]=(now,0);return held;}
}

// The main window, laid out like a modern chat app: rooms rail | sidebar (DMs or the room's menu, you at the bottom) | chat | members.
// Home shows your friends and DMs; each room you join gets an icon on the rail. Guests get rooms only.
public sealed class Shell:Form {
 internal const string AppTitle="3DFx Vista 0.16",DefaultRelay="https://communication-device-1.onrender.com";
 // Who you are: signed in when Token is set (the name comes from the account); guests have only GuestName.
 internal string Relay="",Account="",GuestName="";internal string? Token;bool keepSignedIn;
 internal bool SoundOn=true,StartupSound=true,Receipts=true,Remember=true,DesktopNotifications=true,CloseToTray=true;internal bool? ReopenRooms;internal float TextScale=1f;
 bool trayTipShown,quitting,startHidden;List<SavedRoom> savedRooms=[];
 // Windows notifications go through a tray icon; clicking one opens the conversation it is about.
 readonly NotifyIcon tray=new(){Text="3DFx Vista"};Action? openNotified;
 internal readonly Messenger Hub;
 readonly List<RoomSession> rooms=[];readonly Dictionary<string,DirectConversation> dms=new(StringComparer.OrdinalIgnoreCase);
 // What is on screen: a room, or Home (a DM, or the friends page when both are null).
 RoomSession? room;DirectConversation? dm;
 Conversation? Current=>room!=null?room:dm;
 readonly RoomRail rail=new(){Dock=DockStyle.Left};
 readonly TopBar topBar=new(){Dock=DockStyle.Top};
 // Friends seen online, for Steam-style "Sam is now online" pop-ups (filled quietly from the first friends list).
 HashSet<string> onlineSeen=new(StringComparer.OrdinalIgnoreCase);bool onlineSeeded;
 // At most one balloon per conversation per 10 s, and one "is now online" per friend per 15 minutes (connections flicker).
 readonly NotifyGate notifyGate=new(TimeSpan.FromSeconds(10)),onlineGate=new(TimeSpan.FromMinutes(15));
 readonly Panel sidebar=new(){Dock=DockStyle.Left,BackColor=Aero.Side};
 readonly SideTitle sideTitle=new(){Dock=DockStyle.Top};
 readonly SideList side=new(Aero.Side){Dock=DockStyle.Fill};
 readonly UserPanel me=new(){Dock=DockStyle.Bottom};
 readonly Panel main=new(){Dock=DockStyle.Fill,BackColor=Aero.Bg};
 readonly Panel chatView=new(){Dock=DockStyle.Fill,BackColor=Aero.Bg};
 readonly ChatHeader header=new(){Dock=DockStyle.Top};
 readonly Banner banner=new(){Dock=DockStyle.Top,Visible=false};
 readonly Panel chatBody=new(){Dock=DockStyle.Fill,BackColor=Aero.Bg};
 readonly SideList members=new(Aero.Side){Dock=DockStyle.Right};
 readonly Composer composer=new(){Dock=DockStyle.Bottom};
 readonly ActivityLine activity=new(){Dock=DockStyle.Bottom};
 readonly FriendsView friends;readonly GuestHome guestHome=new(){Dock=DockStyle.Fill};readonly LoginView login=new(){Visible=false};
 readonly Toast toast=new();readonly SearchBar search=new(){Visible=false};readonly EmojiPicker picker=new();
 readonly FlatButton membersTool,nudgeTool,inviteTool;
 readonly System.Windows.Forms.Timer tick=new(){Interval=400},idleTimer=new(){Interval=30000};
 bool showMembers=true,closing,shaking,autoAway,quietText;SoundPlayer? chime;
 int S(int px)=>LogicalToDeviceUnits(px);
 internal string RelayHost{get{try{return RelayClient.ValidateUrl(Relay).Host;}catch(IOException){return Relay;}}}

 public Shell(bool preview=false,string? previewArg=null,bool startInTray=false){startHidden=startInTray&&!preview;
  Hub=new Messenger(this,()=>SoundOn);friends=new FriendsView(Hub){Dock=DockStyle.Fill};
  Text=AppTitle;Font=Aero.Body;BackColor=Aero.Rail;StartPosition=FormStartPosition.CenterScreen;DoubleBuffered=true;KeyPreview=true;
  ClientSize=new Size(S(1240),S(760));MinimumSize=new Size(S(960),S(600));
  try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch(ArgumentException){}
  tray.Icon=Icon;tray.Visible=!preview;tray.BalloonTipClicked+=(_,_)=>{Restore();openNotified?.Invoke();};tray.MouseClick+=(_,e)=>{if(e.Button==MouseButtons.Left)Restore();};
  // Right-click the tray icon to open or quit (closing the window only hides it while "Close to the system tray" is on).
  var trayMenu=DarkMenu.Create();trayMenu.Item("Open 3DFx",Restore);trayMenu.Items.Add(new ToolStripSeparator());trayMenu.Item("Quit 3DFx",Quit,danger:true);tray.ContextMenuStrip=trayMenu;
  FormClosed+=(_,_)=>{tray.Visible=false;tray.Dispose();};
  sidebar.Width=S(240);members.Width=S(240);if(!preview&&!startHidden)Motion.FadeIn(this,260);
  sidebar.Controls.Add(side);sidebar.Controls.Add(me);sidebar.Controls.Add(sideTitle);side.BringToFront();
  // Dock order: header and banner on top, members down the right, then the activity line and composer at the bottom of the chat column.
  chatView.Controls.Add(chatBody);chatView.Controls.Add(composer);chatView.Controls.Add(activity);chatView.Controls.Add(members);chatView.Controls.Add(banner);chatView.Controls.Add(header);chatBody.BringToFront();
  main.Controls.Add(chatView);main.Controls.Add(friends);main.Controls.Add(guestHome);main.Controls.Add(toast);
  members.Sheen=true;
  Controls.Add(main);Controls.Add(sidebar);Controls.Add(rail);Controls.Add(topBar);Controls.Add(login);main.BringToFront();login.BringToFront();
  // The log-in screen sits over everything (not docked, so the rail and sidebar don't take part of it).
  login.Bounds=ClientRectangle;Resize+=(_,_)=>login.Bounds=ClientRectangle;
  membersTool=header.Tool(Aero.IconPeople,"Show or hide the member list",()=>{showMembers=!showMembers;int from=members.Visible?members.Width:0,to=showMembers?S(240):0;
   if(showMembers){members.Width=from;RefreshChat();}membersTool!.Active=showMembers;
   Motion.Run(members,members,showMembers?320:220,t=>members.Width=(int)Motion.Lerp(from,to,t),showMembers?Motion.Spring:Motion.InOut,()=>{members.Width=S(240);RefreshChat();});});
  nudgeTool=header.Tool(Aero.IconBolt,"Send a nudge",()=>{if(room!=null)_=room.SendNudge();});
  inviteTool=header.Tool(Aero.IconLink,"Copy an invite",()=>{if(room!=null)Invite(room);});
  header.Tool(Aero.IconSearch,"Search (Ctrl+F)",OpenSearch);
  // Search: the box floats at the top right of the chat; results are highlighted in the message list.
  chatBody.Controls.Add(search);chatBody.Resize+=(_,_)=>PlaceSearch();
  search.Changed+=text=>{if(Current is {} c){c.List.Search(text);search.SetStatus(c.List.SearchStatus);}};
  search.Step+=dir=>{if(Current is {} c){c.List.StepSearch(dir);search.SetStatus(c.List.SearchStatus);}};
  search.Closed+=CloseSearch;

  // Navigation: the top bar (Steam style), the rooms rail, the sidebar.
  topBar.Join.Click+=(_,_)=>OpenRoomDialog();
  topBar.Clicked+=(key,at)=>{switch(key){
   case "home":Select(null,dm??(Token!=null&&Hub.Contacts.FirstOrDefault(c=>c.State=="contact"&&Hub.Unread(c.Name)>0) is {} u?GetDm(u.Name):null));break;
   case "rooms":if(room!=null)break;if(rooms.Count>0)Select(rooms[^1],null);else OpenRoomDialog();break;
   case "friends":Select(null,null);break;
   default:ProfileMenu(at with{Y=at.Y+S(160)});break;}};
  rail.HomeClicked+=()=>Select(null,dm);rail.AddClicked+=OpenRoomDialog;
  rail.RoomClicked+=key=>{if(rooms.FirstOrDefault(r=>r.Key==key) is {} r)Select(r,null);};
  rail.RoomMenu+=(key,at)=>{if(rooms.FirstOrDefault(r=>r.Key==key) is {} r)RoomMenu(r).Show(at);};
  sideTitle.Clicked+=at=>{if(room!=null)RoomMenu(room).Show(at);else if(Token!=null){Select(null,null);friends.ShowTab("Add Friend");}};
  me.ProfileClicked+=ProfileMenu;me.SettingsClicked+=()=>OpenSettings();
  friends.OpenDm+=name=>Select(null,GetDm(name));
  guestHome.Join.Click+=(_,_)=>OpenRoomDialog();guestHome.LogIn.Click+=(_,_)=>ShowLogin();
  banner.Action.Click+=async(_,_)=>{if(room!=null)await Rejoin(room);};

  // Composer: Enter sends; Ctrl+V pastes a picture; + attaches; smileys and stickers; nudge.
  // Esc stops replying or editing; Up in an empty box edits your last message.
  composer.Box.KeyDown+=async(_,e)=>{if(e.KeyCode==Keys.Escape&&Current is {} esc&&(esc.Replying!=null||esc.Editing!=null)){e.SuppressKeyPress=true;e.Handled=true;CancelCompose();}
   else if(e.KeyCode==Keys.Up&&composer.Box.TextLength==0&&Current is {CanEdit:true} up&&up.List.Items.LastOrDefault(i=>i.Kind==ChatKind.Text&&i.Id.Length>0&&i.Who==up.List.Me) is {} last){e.SuppressKeyPress=true;StartEdit(last);}
   else if(e.KeyCode==Keys.Enter&&!e.Shift){e.SuppressKeyPress=true;await Send();}
   else if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;composer.Box.SelectedText=Environment.NewLine;}
   else if(e.Control&&e.KeyCode==Keys.V&&Current is {CanPictures:true}&&(Clipboard.ContainsImage()||Clipboard.ContainsFileDropList())){e.SuppressKeyPress=true;await PasteMedia();}};
  composer.Box.TextChanged+=(_,_)=>{if(!quietText&&composer.Box.TextLength>0)Current?.Typing();};
  composer.Send.Click+=async(_,_)=>await Send();
  composer.Nudge.Click+=async(_,_)=>{if(room!=null)await room.SendNudge();};
  composer.Emoji.Click+=(_,_)=>picker.Show(composer.Emoji,new Point(composer.Emoji.Width,0),ToolStripDropDownDirection.AboveLeft);
  picker.Picked+=face=>{composer.Box.SelectedText=face;composer.Box.Focus();};
  picker.StickerPicked+=async id=>{if(Current is not {} c)return;if(await c.SendSticker(id) is {Length:>0} problem)Toast(problem,true);else if(c.Replying!=null){c.Replying=null;if(Current==c)ShowStrip(c);}};
  composer.StripClosed+=CancelCompose;
  composer.Attach.Click+=(_,_)=>{var m=DarkMenu.Create();m.Item("Upload a picture…",()=>_=Pick(true));if(Current is {CanVideos:true})m.Item("Upload a video (up to 25 MB)…",()=>_=Pick(false));m.Show(composer.Attach,new Point(0,0),ToolStripDropDownDirection.AboveRight);};
  foreach(var target in new Control[]{chatBody,composer,composer.Box})Droppable(target);

  // Friends and DMs.
  Hub.ReceiptsOn=()=>Receipts;
  Hub.IsViewing=other=>dm!=null&&dm.Contact.Equals(other,StringComparison.OrdinalIgnoreCase)&&ActiveForm==this;
  Hub.Message+=(other,e)=>{if(dms.TryGetValue(other,out var d))d.Show(e);
   if(!e.Out&&!e.IsChange&&!Hub.IsViewing(other))Notify(other,e.Type switch{"sticker"=>"sent a sticker","picture"=>"sent a picture",_=>e.Text},()=>Select(null,GetDm(other)));};
  Hub.Failed+=(other,reason)=>{if(dms.TryGetValue(other,out var d))d.Failed(reason);else Toast("Not sent: "+reason,true);};
  Hub.TypingFrom+=other=>{if(dms.TryGetValue(other,out var d))d.TypingFrom();};
  Hub.DeliveredTo+=(other,id)=>{if(dms.TryGetValue(other,out var d))d.Delivered(id);};
  Hub.SeenBy+=(other,id)=>{if(dms.TryGetValue(other,out var d))d.SeenBy(id);};
  Hub.Changed+=()=>{RefreshAll();UpdateUnread();NoticeOnline();};
  Hub.Notice+=(text,problem)=>Toast(text,problem);
  Hub.SignedOut+=text=>SignedOutBy(text.Contains("deleted")?"This account was deleted.":"Your sign-in expired. Log in again.");
  login.SignedIn+=(reply,keep,relay)=>{Relay=relay;Token=reply.Token;Account=reply.Username;keepSignedIn=keep;if(keep)Remember=true;login.Visible=false;SaveSettings();
   Hub.Start(Relay,Account,Token);Select(null,null);Toast($"Welcome, {Account}!",false);_=ReopenSavedRooms();};
  login.Guest+=(name,relay)=>{Relay=relay;GuestName=name;Token=null;Account="";login.Visible=false;SaveSettings();Select(null,null);_=ReopenSavedRooms();};

  // Automatic Away after 10 idle minutes (only when your status is Online; back to Online on return).
  idleTimer.Tick+=async(_,_)=>{if(Token==null||!Hub.Online)return;var idle=Idle.Seconds;
   if(idle>=600&&Hub.MyPresence=="online"){autoAway=true;await Hub.SetProfile("away",Hub.MyMood);}
   else if(autoAway&&idle<60){autoAway=false;if(Hub.MyPresence=="away")await Hub.SetProfile("online",Hub.MyMood);}};idleTimer.Start();
  tick.Tick+=(_,_)=>{if(Current is {} c){activity.Set(c.Activity,c.Note);composer.Nudge.Enabled=c.CanNudge;nudgeTool.Enabled=c.CanNudge;if(c.Activity.Contains("typing"))activity.Invalidate();}};tick.Start();
  Activated+=(_,_)=>{if(Current is {} c){int fresh=c is DirectConversation dc?Hub.Unread(dc.Contact):c.Unread;if(fresh>0)c.List.MarkNew(fresh);c.Unread=c.Mentions=0;if(dm!=null){Hub.MarkRead(dm.Contact);dm.SendSeen();}}UpdateUnread();RefreshRail();};
  FormClosing+=(_,e)=>{if(CloseToTray&&tray.Visible&&!quitting&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();
    if(!trayTipShown){trayTipShown=true;SavePrefs();tray.ShowBalloonTip(6000,"3DFx is still running","It's in the system tray, so you stay online and get notifications. Right-click the icon to quit.",ToolTipIcon.None);}return;}
   closing=true;Hub.Stop();try{Task.WhenAll(rooms.Select(r=>r.Leave())).Wait(TimeSpan.FromSeconds(2));}catch(AggregateException){}};
  KeyDown+=(_,e)=>{if(login.Visible)return;if(e.Control&&e.KeyCode==Keys.K){e.SuppressKeyPress=true;OpenRoomDialog();}
   else if(e.Control&&e.KeyCode==Keys.F&&Current!=null){e.SuppressKeyPress=true;OpenSearch();}
   else if(e.Alt&&e.KeyCode is Keys.Up or Keys.Down){e.SuppressKeyPress=true;int at=room==null?-1:rooms.IndexOf(room),next=at+(e.KeyCode==Keys.Down?1:-1);
    if(next<-1)next=rooms.Count-1;if(next>=rooms.Count)next=-1;if(next<0)Select(null,dm);else Select(rooms[next],null);}};

  LoadSettings();
  if(preview){Load+=(_,_)=>Preview(previewArg);return;}
  if(startHidden){Opacity=0;Shown+=(_,_)=>BeginInvoke(()=>{Hide();Opacity=1;});}
  if(Token!=null)Select(null,null);else{Select(null,null);ShowLogin();}
  Shown+=async(_,_)=>{if(StartupSound&&!startHidden)PlayStartupChime();await CheckSavedSignIn();if(Token!=null)await ReopenSavedRooms();await OfferUpdate();};
 }
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Aero.DarkTitleBar(Handle);}

 // Settings: relay, guest name and choices are remembered; the sign-in only with "Keep me signed in" (encrypted for this Windows user).
 void LoadSettings(){var prefs=ClientPrefs.Load();DesktopNotifications=prefs.Notifications;CloseToTray=prefs.CloseToTray;ReopenRooms=prefs.ReopenRooms;trayTipShown=prefs.TrayTipShown;
  rated=prefs.Rated??[];amSeen=prefs.AMSeen;surveyDone=prefs.SurveyDone;surveyAfter=prefs.SurveyAfter;
  savedRooms=prefs.Rooms??[];SetTextScale(prefs.TextScale is >=0.8f and <=1.5f?prefs.TextScale:1f,save:false);var saved=Settings.Load(Settings.DefaultPath);if(saved==Settings.Empty)return;
  Relay=saved.Relay;GuestName=saved.Name;SoundOn=saved.Sound;StartupSound=saved.StartupSound;Receipts=saved.ReadReceipts;
  if(saved.Session.Length>0&&SessionVault.Unprotect(saved.Session) is {} kept){Token=kept;Account=saved.Account;keepSignedIn=true;}}
 internal void SavePrefs(){if(Token=="preview")return;try{new ClientPrefs(DesktopNotifications,CloseToTray,ReopenRooms,TextScale,trayTipShown,savedRooms,rated,amSeen,surveyDone,surveyAfter).Save();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}}
 internal void SaveSettings(){if(Token=="preview")return;
  SavePrefs();try{var keep=keepSignedIn&&Token!=null;if(Remember)new Settings(Relay,GuestName,SoundOn,StartupSound,keep?Account:"",keep?SessionVault.Protect(Token!):"",Receipts).Save(Settings.DefaultPath);else File.Delete(Settings.DefaultPath);}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException){Toast("Could not save settings.",true);}}
 // Someone's picture: AM (and any AI member) always shows his logo; guests have none.
 internal Image? Face(string who){var (name,tag)=Names.Split(who);if(tag=="GUEST")return null;return tag=="AI"||Hub.Find(name)?.Bot==true?AMLogo.Picture:Hub.Avatar(name);}
 internal void Toast(string text,bool problem)=>Pop(text,problem);
 void PlaceSearch()=>search.Location=new Point(Math.Max(S(8),chatBody.Width-search.Width-S(24)),S(12));
 void OpenSearch(){if(Current==null)return;PlaceSearch();search.Visible=true;search.BringToFront();search.Input.Box.Focus();search.Input.Box.SelectAll();
  if(search.Input.Box.TextLength>0){Current.List.Search(search.Input.Box.Text);search.SetStatus(Current.List.SearchStatus);}}
 void CloseSearch()=>CloseSearch(Current);
 void CloseSearch(Conversation? c){c?.List.EndSearch();search.Visible=false;search.SetStatus("");if(Current is {CanSend:true})composer.Box.Focus();}
 internal void Restore(){Show();if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;Activate();}
 void Quit(){quitting=true;Close();}
 // "Start with Windows": a per-user Run entry that opens 3DFx hidden in the tray.
 const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] internal bool StartWithWindows{get{using var k=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);return k?.GetValue("3DFx") is string;}
  set{using var k=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);if(value)k.SetValue("3DFx",$"\"{Environment.ProcessPath}\" --tray");else k.DeleteValue("3DFx",false);}}
 // A Windows notification, only while 3DFx isn't in front (and if turned on in Settings).
 void Notify(string title,string text,Action open){if(!DesktopNotifications||closing||Token=="preview"||ActiveForm==this&&WindowState!=FormWindowState.Minimized)return;
  if(notifyGate.Allow(title,DateTime.UtcNow) is not {} held)return;
  openNotified=open;var body=text.Replace("\r","").Replace("\n"," ")+(held>0?$"  (+{held} more)":"");tray.ShowBalloonTip(6000,title,body.Length>180?body[..180]+"…":body,ToolTipIcon.None);}
 void Pop(string text,bool problem,string? name=null,Image? face=null){if(closing)return;toast.RightInset=chatView.Visible&&members.Visible?members.Width:0;toast.Show(text,problem,name,face);}

 // Switching what is on screen keeps each conversation's unsent text.
 void Select(RoomSession? r,DirectConversation? d){var old=Current;if(old!=null)old.Draft=composer.Box.Text;room=r;dm=d;var now=Current;
  if(old!=now)old?.List.ClearNew();int fresh=now is DirectConversation dc?Hub.Unread(dc.Contact):now?.Unread??0;
  SetBox(now?.Draft??"");ShowStrip(now);
  if(now!=null){now.Unread=now.Mentions=0;}if(d!=null){Hub.MarkRead(d.Contact);d.SendSeen();}
  if(old!=now&&search.Visible)CloseSearch(old);
  if(now!=null&&!chatBody.Controls.Contains(now.List)){foreach(var list in chatBody.Controls.OfType<MessageList>().ToList())chatBody.Controls.Remove(list);chatBody.Controls.Add(now.List);search.BringToFront();}
  RefreshChat();chatView.PerformLayout();if(now!=null&&fresh>0)now.List.MarkNew(fresh);if(now!=null&&now!=old)now.List.Reveal();else if(now==null&&old!=null)friends.Reveal();
  RefreshAll();UpdateUnread();if(now!=null&&now.CanSend&&!login.Visible)composer.Box.Focus();}
 DirectConversation GetDm(string contact){var name=Hub.Find(contact)?.Name??contact;
  if(!dms.TryGetValue(name,out var d)){d=new DirectConversation(Hub,name);d.Visible=()=>dm==d&&ActiveForm==this;d.Changed+=()=>{if(dm==d)RefreshChat();};d.List.Problem+=t=>Toast(t,true);d.List.AvatarFor=Face;Droppable(d.List);Hook(d.List);dms[name]=d;}
  return d;}
 // Message lists: names and pictures open profile cards; typing while the list has focus goes to the message box.
 void Hook(MessageList list){list.ProfileClicked+=ShowProfile;
  list.CanRate=it=>Token!=null&&Hub.FeedbackOn&&it.Kind==ChatKind.Text&&it.Id.Length>0&&it.Who!=list.Me&&BotName(it.Who)!=null;
  list.RatingOf=it=>rated.GetValueOrDefault(it.Id);list.Rated+=(it,up)=>_=Rate(list,it,up);list.SurveyClicked+=()=>ShowSurvey(true);
  list.Added+=it=>{if(it.Kind==ChatKind.Text&&it.Who!=list.Me&&(DateTime.Now-it.At).TotalMinutes<2&&BotName(it.Who)!=null)AMSaid();};
  list.ReplyClicked+=StartReply;list.EditClicked+=StartEdit;list.DeleteClicked+=it=>_=DeleteMessage(it);
  list.ReactClicked+=(it,emoji,at)=>{if(emoji!=null){_=React(it,emoji);return;}var pick=new EmojiPicker(withStickers:false);pick.Picked+=e=>_=React(it,e);pick.Closed+=(_,_)=>BeginInvoke(pick.Dispose);pick.Show(at);};
  list.KeyPress+=(_,e)=>{if(char.IsControl(e.KeyChar)||Current is not {CanSend:true})return;composer.Box.Focus();composer.Box.SelectionStart=composer.Box.TextLength;composer.Box.SelectedText=e.KeyChar.ToString();e.Handled=true;};}
 void ShowProfile(string who,Point at){var (name,tag)=Names.Split(who);
  bool me=room!=null&&room.MyName.Length>0?who==room.MyName||(tag.Length==0&&name.Equals(Account,StringComparison.OrdinalIgnoreCase)):name.Equals(Token!=null?Account:GuestName,StringComparison.OrdinalIgnoreCase);
  var info=tag=="GUEST"||me?null:Hub.Find(name);
  string? presence=me?(Token!=null?(Hub.Online?(Hub.MyPresence=="invisible"?"offline":Hub.MyPresence):"offline"):null):info is {State:"contact"}?(info.Online?info.Presence:"offline"):null;
  var line=me?(Token!=null?$"That's you  •  {Profiles.Label(presence??"offline")}":"That's you (guest)"):tag=="AI"?"AI member: joins when someone types @alliedmastercomputer":tag=="GUEST"?"Guest, no 3DFx account":
   info?.State switch{"contact"=>$"{Profiles.Label(presence??"offline")}  •  Friend","incoming"=>"Sent you a friend request","outgoing"=>"Friend request sent","blocked"=>"Blocked",_=>"3DFx account"};
  var actions=new List<(string,ButtonKind,Action)>();
  if(me){if(Token!=null)actions.Add(("Edit profile",ButtonKind.Primary,()=>OpenSettings("Profile")));}
  else{if(info is {State:"contact"})actions.Add(("Message",ButtonKind.Primary,()=>Select(null,GetDm(info.Name))));
   else if(info is {State:"incoming"})actions.Add(("Accept",ButtonKind.Success,()=>_=Hub.Contact("accept",info.Name)));
   else if(Token!=null&&tag!="GUEST"&&info==null)actions.Add(("Add friend",ButtonKind.Success,()=>{_=Hub.Contact("add",name);Toast($"Friend request sent to {name}.",false);}));
   if(room is {Connected:true})actions.Add(("Mention",ButtonKind.Secondary,()=>Mention(name)));}
  new ProfileCard(name,tag,me?Face(Account):tag=="GUEST"?null:Face(name),presence,line,me?(Token!=null?Hub.MyMood:""):info?.Mood??"",actions).Show(at);}
 void Wire(RoomSession r){r.Changed+=()=>{if(room==r){RefreshChat();RefreshSide();}RefreshRail();};r.Alert+=RoomAlert;r.Shake+=s=>_=Shake();r.Notice+=t=>Toast(t,true);
  r.SignInExpired+=()=>SignedOutBy("Your sign-in expired. Log in again.");r.List.Problem+=t=>Toast(t,true);r.List.AvatarFor=Face;Droppable(r.List);Hook(r.List);
  r.List.ExtendMenu=(m,item)=>{if(item is {IsMessage:true} i&&i.Who!=r.MyName)m.Item("Mention",()=>Mention(Names.Split(i.Who).Name));};}

 void RefreshAll(){RefreshRail();RefreshSide();RefreshChat();RefreshTop();}
 void RefreshTop(){var tabs=new List<(string,string)>{("home","HOME"),("rooms","ROOMS")};if(Token!=null)tabs.Add(("friends","FRIENDS"));tabs.Add(("me",(Token!=null?Account:GuestName.Length>0?GuestName:"GUEST").ToUpperInvariant()));
  topBar.Set(tabs,room!=null?"rooms":dm!=null||Token==null?"home":"friends");}
 // Steam-style pop-up when a friend comes online (not for the ones already online when you sign in).
 void NoticeOnline(){if(Token==null||!Hub.Online){onlineSeen.Clear();onlineSeeded=false;return;}if(Hub.Contacts.Count==0)return;
  var now=Hub.Contacts.Where(c=>c.State=="contact"&&c.Online).Select(c=>c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
  if(onlineSeeded)foreach(var name in now.Except(onlineSeen))if(onlineGate.Allow(name,DateTime.UtcNow)!=null)Pop($"{name}\nis now online",false,name,Face(name));
  onlineSeen=now;onlineSeeded=true;}
 void RefreshRail()=>rail.Set(room==null,Token==null?0:Hub.Contacts.Sum(c=>Hub.Unread(c.Name))+Hub.Contacts.Count(c=>c.State=="incoming"),
  rooms.Select(r=>new RoomRail.Entry(r.Key,r.Room,r==room,r.Unread,r.Mentions,!r.Connected)).ToList());
 void RefreshSide(){var rows=new List<SideRow>();
  if(room==null){sideTitle.Show(Token!=null?"Find or start a conversation":"Guest mode",false);
   if(Token!=null){int requests=Hub.Contacts.Count(c=>c.State=="incoming");
    rows.Add(new SideRow{Text="Friends",Glyph=Aero.IconPeople,Selected=dm==null,Badge=requests,Bold=requests>0,Click=()=>Select(null,null)});
    // Friends grouped like Steam's friends list: online first, then offline; names in their status colour.
    var friendsList=Hub.Contacts.Where(c=>c.State=="contact").ToList();
    foreach(var online in new[]{true,false}){var group=friendsList.Where(c=>c.Online==online).OrderByDescending(c=>Hub.Unread(c.Name)).ThenBy(c=>c.Name,StringComparer.OrdinalIgnoreCase).ToList();
     if(group.Count==0&&!online)continue;
     rows.Add(new SideRow{Heading=true,Text=online?$"Online friends — {group.Count}":$"Offline — {group.Count}",Plus=online?()=>{Select(null,null);friends.ShowTab("Add Friend");}:null});
     foreach(var c in group){var name=c.Name;var presence=c.Online?c.Presence:"offline";
      rows.Add(new SideRow{Avatar=true,Text=name,Tag=c.Bot?"AI":"",Sub=c.Mood.Length>0?c.Mood:c.Online?Profiles.Label(c.Presence):"",Presence=presence,Picture=Face(name),Badge=Hub.Unread(name),Bold=Hub.Unread(name)>0,
       Color=Avatars.PresenceName(presence),Selected=dm?.Contact.Equals(name,StringComparison.OrdinalIgnoreCase)==true,Click=()=>Select(null,GetDm(name)),Menu=at=>FriendMenu(name).Show(at)});}}
    if(!Hub.Online)rows.Add(new SideRow{Text="Connecting to the relay…",Glyph=Aero.IconRetry});}
   else{rows.Add(new SideRow{Text="Home",Glyph=Aero.IconPeople,Selected=true});rows.Add(new SideRow{Text="Log in for friends and DMs",Glyph=Aero.IconPerson,Click=ShowLogin});}
   if(rooms.Count>0){rows.Add(new SideRow{Heading=true,Text="Your rooms",Plus=OpenRoomDialog});foreach(var r in rooms){var x=r;rows.Add(new SideRow{Text=r.Room,Glyph="#",Badge=r.Mentions,Bold=r.Unread>0,Click=()=>Select(x,null),Menu=at=>RoomMenu(x).Show(at)});}}}
  else{var r=room;sideTitle.Show(r.Room,true);
   rows.Add(new SideRow{Heading=true,Text="Text channels"});rows.Add(new SideRow{Text="chat",Glyph="#",Selected=true});
   rows.Add(new SideRow{Heading=true,Text="Room"});
   if(!r.Connected)rows.Add(new SideRow{Text="Rejoin",Glyph=Aero.IconRetry,Color=Aero.Green,Click=()=>_=Rejoin(r)});
   rows.Add(new SideRow{Text="Invite friends",Glyph=Aero.IconLink,Click=()=>Invite(r)});
   if(r.CanNudge)rows.Add(new SideRow{Text="Nudge everyone",Glyph=Aero.IconBolt,Click=()=>_=r.SendNudge()});
   if(r.HasBots&&r.Connected)rows.Add(new SideRow{Text="Bring in AM (AI)",Glyph=Aero.IconChat,Click=()=>Mention("alliedmastercomputer")});
   rows.Add(new SideRow{Text="Leave room",Glyph=Aero.IconLeave,Color=Aero.Red,Click=()=>LeaveRoom(r)});}
  side.SetRows(rows);
  if(Token!=null)me.Show(Account,Hub.Online?(Hub.MyMood.Length>0?Hub.MyMood:Profiles.Label(Hub.MyPresence)):"Connecting…",Hub.Online?(Hub.MyPresence=="invisible"?"offline":Hub.MyPresence):"offline",Face(Account));
  else me.Show(GuestName.Length>0?GuestName:"Guest","Guest • not signed in",null,null,"GUEST");}
 void RefreshChat(){var c=Current;chatView.Visible=c!=null;friends.Visible=c==null&&Token!=null;guestHome.Visible=c==null&&Token==null;
  if(c==null){if(Token!=null)friends.Reload();return;}
  if(room!=null){var r=room;header.Show("#",r.Room,r.Connected?$"{r.Members.Length} in room  •  {RelayHost}":"Disconnected");
   membersTool.Visible=nudgeTool.Visible=inviteTool.Visible=true;membersTool.Active=showMembers;nudgeTool.Enabled=r.CanNudge;nudgeTool.Visible=r.Features.Contains("nudge")||!r.Connected;
   banner.Visible=!r.Connected;banner.Text=r.Problem.Length>0?r.Problem:"You're disconnected from this room.";
   members.Visible=showMembers;if(showMembers)RefreshMembers(r);}
  else if(dm!=null){var info=Hub.Find(dm.Contact);header.Show("@",dm.Contact,info?.Mood??"",info is {State:"contact"}?(info.Online?info.Presence:"offline"):null,info?.Bot==true?"AI":"");
   membersTool.Visible=nudgeTool.Visible=inviteTool.Visible=false;banner.Visible=false;members.Visible=false;}
  composer.Box.PlaceholderText=c.Placeholder;composer.Box.ReadOnly=!c.CanSend;composer.Send.Enabled=composer.Emoji.Enabled=c.CanSend;composer.Attach.Enabled=c.CanPictures;
  composer.Nudge.Visible=c is RoomSession&&room!.Features.Contains("nudge");composer.Nudge.Enabled=c.CanNudge;picker.StickersEnabled=c.CanStickers;composer.PerformLayout();
  activity.Set(c.Activity,c.Note);}
 void RefreshMembers(RoomSession r){var rows=new List<SideRow>{new(){Heading=true,Text=$"In room — {r.Members.Length}"}};
  foreach(var m in r.Members.OrderBy(m=>Names.Split(m).Tag=="AI"?1:0).ThenBy(m=>m,StringComparer.OrdinalIgnoreCase)){var (name,tag)=Names.Split(m);var who=m;
   rows.Add(new SideRow{Avatar=true,Text=name,Tag=tag,Picture=Face(m),Color=m==r.MyName?Aero.Bright:Aero.NameColor(m),Sub=m==r.MyName?"you":tag=="AI"?"AI member • @alliedmastercomputer":"",
    Click=()=>ShowProfile(who,Cursor.Position),Menu=at=>MemberMenu(r,who,name,tag).Show(at)});}
  members.SetRows(rows);}
 void Mention(string name){var text=composer.Box.Text;var add="@"+name+" ";if(!text.Contains(add,StringComparison.OrdinalIgnoreCase))composer.Box.Text=(text.Length>0&&!text.EndsWith(' ')?text+" ":text)+add;composer.Box.SelectionStart=composer.Box.TextLength;composer.Box.Focus();}

 // Unread: per conversation (rooms and DMs), on the rail, in the title ("(3) 3DFx Vista") and as a red badge on the taskbar button.
 void RoomAlert(RoomSession r,bool counts,bool mention,SystemSound? tone){bool front=ActiveForm==this;
  if(mention&&r.List.Items.LastOrDefault(i=>i.Kind==ChatKind.Text) is {} said)Notify($"{Names.Split(said.Who).Name} in #{r.Room}",said.Text,()=>Select(r,null));
  else if(tone==SystemSounds.Exclamation&&r.List.Items.LastOrDefault(i=>i.Kind==ChatKind.Nudge) is {} nudge)Notify($"#{r.Room}",nudge.Text,()=>Select(r,null));
  if(!front)Aero.Flash(this);if(!counts)return;
  if(room!=r||!front){r.Unread++;if(mention)r.Mentions++;UpdateUnread();RefreshRail();if(room==null)RefreshSide();}
  if(!front&&SoundOn)(tone??SystemSounds.Asterisk).Play();}
 void UpdateUnread(){int total=rooms.Sum(r=>r.Unread)+(Token==null?0:Hub.Contacts.Sum(c=>Hub.Unread(c.Name)));Text=total>0?$"({total}) {AppTitle}":AppTitle;if(IsHandleCreated)Taskbar.SetBadge(Handle,total);}

 // Menus.
 ContextMenuStrip RoomMenu(RoomSession r){var m=DarkMenu.Create();
  m.Item("Invite friends (copy invite)",()=>Invite(r));if(r.CanNudge)m.Item("Send a nudge",()=>_=r.SendNudge());
  if(r.HasBots&&r.Connected)m.Item("Bring in AM (AI)",()=>{Select(r,null);Mention("alliedmastercomputer");});
  if(!r.Connected)m.Item("Rejoin",()=>_=Rejoin(r));
  if(r.Unread>0)m.Item("Mark as read",()=>{r.Unread=r.Mentions=0;UpdateUnread();RefreshRail();});
  m.Items.Add(new ToolStripSeparator());m.Item("Leave room",()=>LeaveRoom(r),danger:true);return m;}
 ContextMenuStrip FriendMenu(string name){var m=DarkMenu.Create();m.Item("Message",()=>Select(null,GetDm(name)));
  if(Hub.Unread(name)>0)m.Item("Mark as read",()=>Hub.MarkRead(name));m.Item("Copy username",()=>Clipboard.SetText(name));
  m.Items.Add(new ToolStripSeparator());
  m.Item("Remove friend",()=>{if(MessageBox.Show(this,$"Remove {name} from your friends?","3DFx Vista",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes)_=Hub.Contact("remove",name);},danger:true);
  m.Item("Block",()=>{if(MessageBox.Show(this,$"Block {name}? They won't be able to message you or send requests.","3DFx Vista",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes)_=Hub.Contact("block",name);},danger:true);return m;}
 ContextMenuStrip MemberMenu(RoomSession r,string who,string name,string tag){var m=DarkMenu.Create();
  if(who!=r.MyName)m.Item("Mention",()=>Mention(name));
  if(Token!=null&&tag.Length==0&&who!=r.MyName){var c=Hub.Find(name);
   if(c is {State:"contact"})m.Item("Message",()=>Select(null,GetDm(c.Name)));else if(c==null)m.Item("Add friend",()=>{_=Hub.Contact("add",name);Toast($"Friend request sent to {name}.",false);});}
  m.Item("Copy name",()=>Clipboard.SetText(name));return m;}
 void ProfileMenu(Point at){var m=DarkMenu.Create();
  if(Token!=null){m.ShowCheckMargin=true;
   foreach(var p in Profiles.Presences){var choice=p;var item=(ToolStripMenuItem)m.Item(Profiles.Label(p),async()=>{ManualPresence();await Hub.SetProfile(choice,Hub.MyMood);});item.Checked=Hub.MyPresence==p;item.Enabled=Hub.Online;}
   m.Items.Add(new ToolStripSeparator());
   m.Item("Set a custom status…",async()=>{using var d=new MoodDialog(Hub.MyMood);if(d.ShowDialog(this)==DialogResult.OK)await Hub.SetProfile(Hub.MyPresence,d.Mood);}).Enabled=Hub.Online;
   m.Item("Change avatar…",async()=>await ChoosePicture()).Enabled=Hub.Online;m.Item("Remove avatar",async()=>await Hub.SetAvatar(null)).Enabled=Hub.Online;
   m.Items.Add(new ToolStripSeparator());m.Item("Copy username",()=>Clipboard.SetText(Account));}
  else m.Item("Log in or register…",ShowLogin);
  m.Item("Settings",()=>OpenSettings());
  m.Show(at,ToolStripDropDownDirection.AboveRight);}
 internal void ManualPresence()=>autoAway=false;
 // Chat text size (messages and the message box): 90 %, 100 %, 115 % or 130 %.
 internal void SetTextScale(float scale,bool save=true){TextScale=scale;Aero.ScaleChat(scale);composer.Restyle();
  foreach(var c in rooms.Cast<Conversation>().Concat(dms.Values))c.List.Restyle();if(save)SavePrefs();}
 void Invite(RoomSession r){Clipboard.SetText($"Join me on 3DFx Vista: room \"{r.Room}\" on {RelayHost}. I'll send you the password separately.");Toast("Invite copied. Send the room password separately.",false);}
 void OpenSettings(string page="My Account"){using var d=new SettingsDialog(this,page);d.ShowDialog(this);RefreshAll();}

 // Rooms: join or create from the dialog; several at once. Problems go back to the dialog, which marks only the field they're about.
 void OpenRoomDialog(){if(login.Visible)return;using var d=new RoomDialog(ConnectRoom);if(d.ShowDialog(this)==DialogResult.OK)AskToRememberRooms();}
 // Asked once, after the first room you join: keep your rooms for next time? (Passwords are kept encrypted for this Windows user.)
 void AskToRememberRooms(){if(ReopenRooms!=null||Token=="preview")return;
  var yes=MessageBox.Show(this,"Reopen your rooms the next time you start 3DFx?\n\nRoom names and passwords are kept on this PC, encrypted for your Windows account. You can change this in Settings > Windows Settings.","3DFx Vista",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;
  SetReopenRooms(yes);}
 internal void SetReopenRooms(bool on){ReopenRooms=on;if(on)RememberRooms();else savedRooms.Clear();SavePrefs();}
 // The rooms open now (on this relay) become the ones to reopen; rooms on other relays are kept.
 void RememberRooms(){if(ReopenRooms!=true)return;savedRooms=savedRooms.Where(x=>!x.Relay.Equals(Relay,StringComparison.OrdinalIgnoreCase)).Concat(rooms.Select(r=>new SavedRoom(Relay,r.Room,RoomVault.Protect(r.Password)))).ToList();SavePrefs();}
 bool reopening;
 // Rejoins the saved rooms in the background; a room that has ended is dropped quietly (with a note).
 async Task ReopenSavedRooms(){if(ReopenRooms!=true||reopening)return;reopening=true;
  try{var who=Token!=null?Account:GuestName;
   foreach(var saved in savedRooms.Where(x=>x.Relay.Equals(Relay,StringComparison.OrdinalIgnoreCase)).ToList()){
    if(rooms.Any(r=>r.Room.Equals(saved.Room,StringComparison.OrdinalIgnoreCase))||RoomVault.Unprotect(saved.Secret) is not {} password)continue;
    var s=new RoomSession(Relay,saved.Room,password,who,Token);Wire(s);rooms.Add(s);RefreshAll();
    if(await s.Connect(false) is {} problem){
     if(problem=="Room name or password is incorrect."){rooms.Remove(s);savedRooms.Remove(saved);SavePrefs();if(room==s)Select(null,dm);Toast($"#{saved.Room} has ended, so it was removed from your rooms.",false);}
     else Toast($"Couldn't reopen #{saved.Room}: {problem}",true);}
    RefreshAll();}}
  finally{reopening=false;}}
 async Task<string?> ConnectRoom(bool creating,string name,string password){
  var who=Token!=null?Account:GuestName;var (_,problem)=Protocol.Check(Relay,who,name,password);if(problem.Length>0)return problem;
  var existing=rooms.FirstOrDefault(r=>r.Room.Equals(name,StringComparison.OrdinalIgnoreCase));
  if(existing!=null&&existing.Connected){Select(existing,null);return null;}
  var s=new RoomSession(Relay,name.ToLowerInvariant(),password,who,Token);Wire(s);var error=await s.Connect(creating);if(error!=null)return error;
  if(existing!=null){rooms[rooms.IndexOf(existing)]=s;existing.List.Clear();}else rooms.Add(s);RememberRooms();
  Select(s,null);return null;}
 async Task Rejoin(RoomSession r){banner.Action.Enabled=false;try{if(await r.Connect(false,rejoining:true) is {} problem)Toast(problem,true);}finally{banner.Action.Enabled=true;}RefreshAll();}
 void LeaveRoom(RoomSession r){if(MessageBox.Show(this,$"Leave #{r.Room}? Its messages are cleared from this window.","Leave room",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
  rooms.Remove(r);RememberRooms();if(room==r)Select(null,dm);else RefreshAll();UpdateUnread();_=r.Leave();r.List.Clear();}
 void LeaveAll(){foreach(var r in rooms.ToList()){_=r.Leave();r.List.Clear();}rooms.Clear();room=null;UpdateUnread();}

 // Account: log in / out, expiry, deletion. Logging in or out leaves your rooms, since your name in them changes.
 internal void ShowLogin(){if(rooms.Count>0&&MessageBox.Show(this,"Going to the log-in screen leaves your rooms. Continue?","3DFx Vista",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
  LeaveAll();Select(null,null);login.Prefill(Relay.Length>0?Relay:DefaultRelay,Account,GuestName.Length>0?GuestName:Environment.UserName);login.Visible=true;login.BringToFront();login.FocusFirst();}
 void ClearDms(){foreach(var d in dms.Values)d.List.Clear();dms.Clear();dm=null;}
 internal void LogOut(){var old=Token;var where=Relay;LeaveAll();Hub.Stop();ClearDms();Token=null;Account="";keepSignedIn=false;SaveSettings();ShowLogin();
  if(old!=null&&old!="preview")_=Task.Run(async()=>{try{using var c=AccountClient.ForRelay(where);await c.SignOut(old);}catch(IOException){}});}
 void SignedOutBy(string text){if(Token==null)return;LeaveAll();Hub.Stop();ClearDms();Token=null;Account="";keepSignedIn=false;SaveSettings();ShowLogin();login.ShowProblem(text);}
 internal void TokenChanged(string token){Token=token;SaveSettings();Hub.Start(Relay,Account,token);}
 internal void AccountDeleted()=>SignedOutBy("Your account was deleted.");
 // A kept sign-in is checked quietly at start-up; if the relay says it expired, you are signed out. No network: keep it for now.
 async Task CheckSavedSignIn(){if(Token==null)return;var kept=Token;
  try{using var c=AccountClient.ForRelay(Relay);await c.Me(kept);}
  catch(AccountException e) when(e.Status==401){if(Token==kept)SignedOutBy("Your sign-in expired. Log in again.");return;}
  catch(IOException){}
  if(Token==kept&&!Hub.Running)Hub.Start(Relay,Account,kept);}
 // Picture for your profile: cropped square, 96×96, re-saved small (hidden photo details are dropped).
 internal async Task ChoosePicture(){using var d=new OpenFileDialog{Title="Choose a profile picture",Filter="Pictures|*.jpg;*.jpeg;*.png;*.gif;*.bmp"};if(d.ShowDialog(this)!=DialogResult.OK)return;
  try{var bytes=await File.ReadAllBytesAsync(d.FileName);await Hub.SetAvatar(await Task.Run(()=>Avatars.Prepare(bytes)));}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or ExternalException){Toast(e is IOException?e.Message:"That file isn't a picture 3DFx can read.",true);}}

 // Feedback for AM (see AmFeedback.cs). In rooms AI members carry the "(AI)" tag; in DMs the contact list says.
 Dictionary<string,string> rated=[];int amSeen;bool surveyDone,surveyOpen;long surveyAfter;
 // "Allied Mastercomputer (AI)" in a room is the account alliedmastercomputer.
 string? BotName(string who){var (name,tag)=Names.Split(who);return tag=="AI"?name.Replace(" ","").ToLowerInvariant():Hub.Find(who) is {Bot:true} c?c.Name:null;}
 async Task Rate(MessageList list,ChatItem it,bool up){if(BotName(it.Who) is not {} bot)return;var where=rooms.FirstOrDefault(r=>r.List==list) is {} r?"room:"+r.Room:"dm";
  var note="";if(!up){using var d=new FeedbackDialog();if(d.ShowDialog(this)==DialogResult.OK)note=d.Note;}
  if(!await Hub.SendFeedback(bot,up?"up":"down",it.Id,where,0,note)){Toast("Not sent: not connected to the relay right now.",true);return;}
  rated[it.Id]=up?"up":"down";if(rated.Count>1000)foreach(var old in rated.Keys.Take(rated.Count-1000).ToList())rated.Remove(old);SavePrefs();list.Invalidate();
  Toast(up?"Thanks! AM learns from your feedback.":"Thanks. AM will try to do better.",false);}
 // Asked once you've seen a dozen of AM's messages, while 3DFx is in front; "Not now" asks again in a few days.
 void AMSaid(){amSeen++;SavePrefs();if(Token!=null&&Hub.FeedbackOn&&AMSurvey.Due(amSeen,surveyDone,surveyAfter,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))_=SurveySoon();}
 async Task SurveySoon(){await Task.Delay(2500);if(ActiveForm==this&&!surveyOpen&&!surveyDone)ShowSurvey(false);}
 void ShowSurvey(bool asked){if(surveyOpen||Token==null)return;surveyOpen=true;
  try{using var d=new SurveyDialog();if(Token=="preview"&&asked)d.Shown+=(_,_)=>d.PreviewPick(4);
   if(d.ShowDialog(this)==DialogResult.OK){_=SendSurvey(d.Rating,d.Comment);surveyDone=true;}
   else if(!asked)surveyAfter=DateTimeOffset.UtcNow.Add(AMSurvey.Wait).ToUnixTimeMilliseconds();SavePrefs();}
  finally{surveyOpen=false;}}
 async Task SendSurvey(int rating,string comment){if(await Hub.SendFeedback("alliedmastercomputer","survey","","",rating,comment))Toast("Thanks for telling us! AM learns from it.",false);else{surveyDone=false;Toast("Not sent: not connected to the relay right now.",true);}}
 // Sending. While editing, Enter saves the change instead (an unchanged text just stops editing).
 async Task Send(){var c=Current;var text=composer.Box.Text;if(c==null||string.IsNullOrWhiteSpace(text))return;
  if(c.Editing is {} editing){if(text==editing.Text){CancelCompose();return;}var change=await c.Edit(editing,text);
   if(change==null){if(c.Editing==editing){c.Editing=null;if(Current==c){SetBox(c.Stash);ShowStrip(c);}else c.Draft=c.Stash;}}else if(change.Length>0)Toast(change,true);return;}
  var problem=await c.SendText(text);if(problem==null){c.Replying=null;if(Current==c){SetBox("");ShowStrip(c);}else c.Draft="";}else if(problem.Length>0)Toast(problem,true);}
 void SetBox(string text){quietText=true;composer.Box.Text=text;composer.Box.SelectionStart=composer.Box.TextLength;quietText=false;}
 // Replies and edits (Discord style): a strip above the message box says what Enter will do.
 void ShowStrip(Conversation? c){if(c?.Editing!=null)composer.SetStrip("Editing message","","   ·   Enter to save, Esc to cancel");
  else if(c?.Replying is {} r)composer.SetStrip("Replying to ",Names.Split(r.Who).Name,"   ·   Esc to cancel");else composer.SetStrip("");}
 void StartReply(ChatItem it){if(Current is not {CanReply:true} c||!c.List.Items.Contains(it))return;if(c.Editing!=null){c.Editing=null;SetBox(c.Stash);}c.Replying=it;ShowStrip(c);composer.Box.Focus();}
 void StartEdit(ChatItem it){if(Current is not {CanEdit:true} c||it.Kind!=ChatKind.Text||it.Who!=c.List.Me||!c.List.Items.Contains(it))return;
  if(c.Editing==null)c.Stash=composer.Box.Text;c.Replying=null;c.Editing=it;SetBox(it.Text);ShowStrip(c);composer.Box.Focus();}
 void CancelCompose(){if(Current is not {} c)return;if(c.Editing!=null){c.Editing=null;SetBox(c.Stash);}c.Replying=null;ShowStrip(c);composer.Box.Focus();}
 async Task DeleteMessage(ChatItem it){if(Current is not {CanEdit:true} c)return;
  if(MessageBox.Show(this,"Delete this message for everyone? This can't be undone.","3DFx Vista",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
  if(c.Editing==it){c.Editing=null;SetBox(c.Stash);ShowStrip(c);}if(await c.Delete(it) is {Length:>0} problem)Toast(problem,true);}
 async Task React(ChatItem it,string emoji){if(Current is {CanReact:true} c&&c.List.Items.Contains(it)&&await c.React(it,emoji) is {Length:>0} problem)Toast(problem,true);}
 void Droppable(Control target){target.AllowDrop=true;
  target.DragEnter+=(_,e)=>e.Effect=Current is {CanPictures:true}&&e.Data?.GetDataPresent(DataFormats.FileDrop)==true?DragDropEffects.Copy:DragDropEffects.None;
  target.DragDrop+=async(_,e)=>{if(Current is {CanPictures:true}&&e.Data?.GetData(DataFormats.FileDrop) is string[]{Length:>0} files)await SendFile(files[0]);};}
 async Task Pick(bool picture){using var d=new OpenFileDialog{Title=picture?"Send a picture":"Send a video",Filter=picture?"Pictures|*.jpg;*.jpeg;*.png;*.gif;*.bmp":"Videos up to 25 MB (MP4, WebM)|*.mp4;*.m4v;*.webm"};
  if(d.ShowDialog(this)==DialogResult.OK)await SendFile(d.FileName);}
 async Task PasteMedia(){if(MessageBox.Show(this,"Send what you pasted to this conversation?","3DFx Vista",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
  if(Clipboard.ContainsFileDropList()){var files=Clipboard.GetFileDropList();if(files.Count>0&&files[0] is string first)await SendFile(first);return;}
  using var img=Clipboard.GetImage();if(img==null)return;
  try{var (data,mime)=Pictures.Prepare(img);await SendMedia(data,mime);}catch(Exception e) when(e is IOException or ArgumentException or OutOfMemoryException or ExternalException){Toast(e is IOException?e.Message:"That picture could not be sent.",true);}}
 // Pictures are shrunk and re-saved first (drops hidden GPS data); videos go as they are, up to 25 MB, and only to rooms.
 async Task SendFile(string path){var c=Current;if(c is not {CanPictures:true})return;
  var mime=Media.MimeFor(path);if(mime==null){Toast("Send a JPG, PNG, GIF, BMP, MP4 or WebM file.",true);return;}
  try{var size=new FileInfo(path).Length;
   if(Media.IsVideo(mime)){if(!c.CanVideos){Toast("Videos can be sent in rooms. DMs take pictures.",true);return;}if(size>Media.MaxVideo){Toast("Videos can be up to 25 MB.",true);return;}await SendMedia(await File.ReadAllBytesAsync(path),mime);}
   else{if(size>50L*1024*1024){Toast("That picture is too large to send.",true);return;}var bytes=await File.ReadAllBytesAsync(path);var (data,type)=await Task.Run(()=>Pictures.Prepare(bytes,mime));await SendMedia(data,type);}}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or ExternalException){Toast(e is IOException?e.Message:"That file isn't a picture 3DFx can read.",true);}}
 async Task SendMedia(byte[] data,string mime){if(Current is {} c&&await c.SendMedia(data,mime) is {Length:>0} problem)Toast(problem,true);}

 // Nudge shake, MSN Messenger style.
 async Task Shake(){if(shaking||WindowState!=FormWindowState.Normal)return;shaking=true;var home=Location;int d=S(10);
  try{for(int i=0;i<16;i++){int fade=16-i;Location=new Point(home.X+(i%2==0?d:-d)*fade/16,home.Y+((i/2)%2==0?d/2:-d/2)*fade/16);await Task.Delay(25);}}finally{Location=home;shaking=false;}}
 // Windows' own startup sound (falls back to Logon or Tada if a Windows edition lacks it).
 void PlayStartupChime(){var media=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Media");
  foreach(var file in new[]{"Windows Startup.wav","Windows Logon.wav","tada.wav"}){var path=Path.Combine(media,file);if(!File.Exists(path))continue;try{chime=new SoundPlayer(path);chime.Play();}catch(Exception e) when(e is IOException or InvalidOperationException or UnauthorizedAccessException){}return;}}
 // Installed copies check GitHub Releases at start-up; nothing downloads unless you say yes. Updating restarts the app.
 async Task OfferUpdate(bool manual=false){if(await Updates.Check() is not {} found){if(manual)Toast("3DFx Vista is up to date.",false);return;}var version=found.Info.TargetFullRelease.Version.ToString();
  if(MessageBox.Show(this,$"3DFx Vista {version} is available.\n\nDownload it and restart now?","3DFx Vista update",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes){Toast($"Version {version} is available. It will be offered again next time.",false);return;}
  try{Toast("Downloading the update…",false);await found.Manager.DownloadUpdatesAsync(found.Info,pc=>BeginInvoke(()=>Toast($"Downloading the update… {pc}%",false)));
   closing=true;Hub.Stop();try{Task.WhenAll(rooms.Select(r=>r.Leave())).Wait(TimeSpan.FromSeconds(2));}catch(AggregateException){}found.Manager.ApplyUpdatesAndRestart(found.Info);}
  catch(Exception e){closing=false;Toast("The update could not be installed: "+e.Message,true);}}
 internal async Task<string> CheckForUpdates(){var found=await Updates.Check();if(found==null)return "You're up to date (or this copy wasn't installed with Setup, so it doesn't update itself).";await OfferUpdate();return "";}

 // Sample content for screenshots only (--preview). No network, nothing saved.
 void Preview(string? arg){var video=arg!=null&&File.Exists(arg)?arg:null;var page=video==null&&arg is "dm" or "friends" or "addfriend" or "profile" or "search" or "reply" or "rate" or "survey" or "feedback" or "appearance" or "windows" or "guest" or "login" or "settings" or "join"?arg:"room";
  Relay=DefaultRelay;Account="Alex";GuestName="Alex";Token="preview";Hub.Preview("Alex");
  var r=new RoomSession(Relay,"friday-friends","example-password","Alex","preview");Wire(r);r.PreviewState("Alex",["Alex","Sam","Jordan (guest)","Allied Mastercomputer (AI)"],"Sam");
  var L=r.List;var t=DateTime.Now.AddMinutes(-45);L.BeginUpdate();
  L.Welcome("#friday-friends","Welcome to #friday-friends!","This is the start of your time in #friday-friends. Say hi!");
  L.Notice("Alex joined.",t);L.Notice("Sam joined.",t.AddMinutes(1));L.Say("Sam","Hey! The new look is very modern 👋",t.AddMinutes(2),"p1");L.Say("Sam","dark mode finally",t.AddMinutes(2.3));
  L.Say("Alex","Right? Rooms on the left, members on the right.",t.AddMinutes(3),"p2","p1");L.Notice("Jordan (guest) joined.",t.AddMinutes(9));L.Say("Jordan (guest)","Is that the 3DFx orb up in the corner?",t.AddMinutes(10),"p3",edited:true);
  L.Highlight("Sam sent a nudge!",t.AddMinutes(11));L.Say("Alex","😄 👍",t.AddMinutes(20));L.ShowMedia("Sam","image/jpeg",SamplePhoto(),t.AddMinutes(21),"p6");L.ShowSticker("Jordan (guest)","party",t.AddMinutes(22));
  L.Say("Alex","@alliedmastercomputer pineapple on pizza, yes or no?",t.AddMinutes(30),"p4");L.Say("Allied Mastercomputer (AI)","yeah i do, but its too sweet",t.AddMinutes(30.1),"p5","p4");
  L.Say("Sam","@Alex are you bringing snacks tonight?",t.AddMinutes(35),"p7");L.Say("Sam","also the new build is up: https://github.com/iluvgam/communication-device/releases",t.AddMinutes(35.5));L.ShowMedia("Alex","video/mp4",video!=null?File.ReadAllBytes(video):new byte[12_400_000],t.AddMinutes(40),"p9","p7");
  L.Items[^1].Reactions=[new("🔥",["Sam","Jordan (guest)","Allied Mastercomputer (AI)"])];L.Find("p1")!.Reactions=[new("👋",["Alex","Jordan (guest)"]),new("😄",["Allied Mastercomputer (AI)"])];L.Find("p5")!.Reactions=[new("😂",["Sam","Alex"])];L.EndUpdate();
  r.Draft="yes! bringing chips"+Environment.NewLine+"and that spicy dip 🌶️";rooms.Add(r);
  var g=new RoomSession(Relay,"game-night","example-password","Alex","preview");Wire(g);g.PreviewState("Alex",["Alex","Morgan"]);
  g.List.Welcome("#game-night","Welcome to #game-night!","Say hi!");g.List.Say("Morgan","gg everyone",DateTime.Now.AddMinutes(-3));g.Unread=4;g.Mentions=1;rooms.Add(g);
  switch(page){
   case "dm":Select(null,GetDm("Sam"));break;
   case "friends":Select(null,null);break;
   case "addfriend":Select(null,null);friends.ShowTab("Add Friend");break;
   case "rate":Select(r,null);rated["p5"]="up";Shown+=(_,_)=>BeginInvoke(()=>L.PreviewHover(L.Find("p5")!));break;
   case "survey":Select(r,null);Shown+=(_,_)=>BeginInvoke(()=>ShowSurvey(true));break;
   case "feedback":Select(r,null);Shown+=(_,_)=>BeginInvoke(()=>{using var d=new FeedbackDialog();d.ShowDialog(this);});break;
   case "reply":Select(r,null);StartReply(L.Find("p7")!);Shown+=(_,_)=>BeginInvoke(()=>L.PreviewHover(L.Find("p9")!));break;
   case "search":Select(r,null);Shown+=(_,_)=>BeginInvoke(()=>{OpenSearch();search.Input.Box.Text="snacks";});break;
   case "appearance":Select(r,null);Shown+=(_,_)=>BeginInvoke(()=>OpenSettings("Appearance"));break;
   case "windows":Select(r,null);Shown+=(_,_)=>BeginInvoke(()=>OpenSettings("Windows Settings"));break;
   case "profile":r.Unread=3;Select(r,null);Shown+=(_,_)=>BeginInvoke(()=>ShowProfile("Sam",PointToScreen(new Point(S(560),S(200)))));break;
   case "guest":Hub.Stop();Token=null;Account="";rooms.Clear();Select(null,null);break;
   case "login":Select(r,null);login.Prefill(DefaultRelay,"Alex","Alex");login.Visible=true;login.BringToFront();break;
   case "settings":Select(r,null);Shown+=(_,_)=>BeginInvoke(()=>OpenSettings());break;
   case "join":Select(null,null);Shown+=(_,_)=>BeginInvoke(()=>{using var d=new RoomDialog((_,_,_)=>Task.FromResult<string?>(Protocol.PasswordRule),"movie-night");d.Shown+=(_,_)=>d.AcceptButton!.PerformClick();d.ShowDialog(this);});break;
   default:r.Unread=3;Select(r,null);Shown+=(_,_)=>Pop("Morgan\nis now online",false,"Morgan",Face("Morgan"));break;}
  UpdateUnread();}
 // A drawn landscape standing in for a photo in --preview.
 static byte[] SamplePhoto(){using var bmp=new Bitmap(640,400);using(var g=Graphics.FromImage(bmp)){g.SmoothingMode=SmoothingMode.AntiAlias;
   using(var sky=new LinearGradientBrush(new Rectangle(0,0,640,400),Aero.C(0x5AA9E6),Aero.C(0xE8F4FF),90f))g.FillRectangle(sky,0,0,640,400);
   using(var sun=new SolidBrush(Aero.C(0xFFF2B0)))g.FillEllipse(sun,470,50,90,90);
   using(var far=new SolidBrush(Aero.C(0x6FBF73)))g.FillEllipse(far,-120,250,560,300);using(var near=new SolidBrush(Aero.C(0x3E9B4A)))g.FillEllipse(near,220,280,600,260);}
  using var ms=new MemoryStream();bmp.Save(ms,System.Drawing.Imaging.ImageFormat.Jpeg);return ms.ToArray();}
}

// App-only choices, kept in client.json beside settings.json (the shared Settings record is left as the relay project knows it).
// A room to reopen at start; the password is encrypted for this Windows user (RoomVault).
sealed record SavedRoom(string Relay,string Room,string Secret);
// Rated: your thumbs up / down on AM's messages (by message id). AMSeen / SurveyDone / SurveyAfter: when to ask about AM.
sealed record ClientPrefs(bool Notifications=true,bool CloseToTray=true,bool? ReopenRooms=null,float TextScale=1f,bool TrayTipShown=false,List<SavedRoom>? Rooms=null,
 Dictionary<string,string>? Rated=null,int AMSeen=0,bool SurveyDone=false,long SurveyAfter=0){
 public static string DefaultPath=>Path.Combine(Path.GetDirectoryName(Settings.DefaultPath)!,"client.json");
 public static ClientPrefs Load(){try{return System.Text.Json.JsonSerializer.Deserialize<ClientPrefs>(File.ReadAllText(DefaultPath))??new();}catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){return new();}}
 public void Save(){Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath)!);File.WriteAllText(DefaultPath,System.Text.Json.JsonSerializer.Serialize(this));}
}
