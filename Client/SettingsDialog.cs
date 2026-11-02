using System.Drawing.Drawing2D;
namespace FriendsChat;

// Settings: pages on the left (account, profile, notifications, privacy, connection, about), the chosen page on the right. Esc closes.
sealed class SettingsDialog:Form {
 readonly Shell shell;readonly SideList nav=new(Aero.Side){Dock=DockStyle.Left};
 readonly ScrollPanel page=new(){Dock=DockStyle.Fill,BackColor=Aero.Bg};
 readonly FlowLayoutPanel body=new(){FlowDirection=FlowDirection.TopDown,AutoSize=true,WrapContents=false,BackColor=Aero.Bg};
 string current;
 int S(int px)=>LogicalToDeviceUnits(px);
 int W=>S(640);
 public SettingsDialog(Shell shell,string start="My Account"){this.shell=shell;current=start;
  Text="Settings — 3DFx Vista";Font=Aero.Body;BackColor=Aero.Bg;ForeColor=Aero.Text;StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;MinimizeBox=false;KeyPreview=true;
  Size=new Size(S(1060),S(720));Motion.FadeIn(this,200);MinimumSize=new Size(S(860),S(520));try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch(ArgumentException){}
  nav.Width=S(232);body.Location=new Point(S(40),S(56));page.Controls.Add(body);page.Content=body;
  var close=new FlatButton{Kind=ButtonKind.Icon,Glyph=Aero.IconClose,GlyphSize=12f,Size=new Size(S(36),S(36)),Anchor=AnchorStyles.Top|AnchorStyles.Right,Surface=Aero.Bg};close.Click+=(_,_)=>Close();DarkTip.Create().SetToolTip(close,"Close (Esc)");
  page.Controls.Add(close);page.Resize+=(_,_)=>close.Location=new Point(page.ClientSize.Width-S(64),S(52));
  Controls.Add(page);Controls.Add(nav);page.BringToFront();
  KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape)Close();};
  shell.Hub.Changed+=Reopen;FormClosed+=(_,_)=>shell.Hub.Changed-=Reopen;
  Open(start);}
 void Reopen(){if(current=="Profile"&&!body.ContainsFocus)Open(current);}
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Aero.DarkTitleBar(Handle);}
 void Nav(){bool signedIn=shell.Token!=null;var rows=new List<SideRow>();
  void Item(string name,bool show=true,Color? color=null){if(show)rows.Add(new SideRow{Text=name,Selected=current==name,Color=color,Click=()=>Open(name)});}
  rows.Add(new SideRow{Heading=true,Text="User settings"});Item("My Account");Item("Profile",signedIn);
  rows.Add(new SideRow{Heading=true,Text="App settings"});Item("Appearance");Item("Notifications");Item("Windows Settings");Item("Privacy & Safety");Item("Connection");
  rows.Add(new SideRow{Heading=true,Text="3DFx"});Item("About");
  rows.Add(new SideRow{Heading=true,Text=""});
  if(signedIn)rows.Add(new SideRow{Text="Log Out",Glyph=Aero.IconLeave,Color=Aero.Red,Click=LogOut});else rows.Add(new SideRow{Text="Log In",Glyph=Aero.IconPerson,Click=()=>{Close();shell.ShowLogin();}});
  nav.SetRows(rows);}
 void LogOut(){if(MessageBox.Show(this,"Log out of 3DFx Vista? You'll leave any rooms you're in.","Log out",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;Close();shell.LogOut();}

 // Page building blocks.
 void Add(Control c)=>body.Controls.Add(c);
 void Title(string t)=>Add(new Label{Text=t,Font=Aero.Title,ForeColor=Aero.Bright,AutoSize=true,Margin=new Padding(0,0,0,S(20))});
 void Caps(string t,int top=20)=>Add(new Label{Text=t.ToUpperInvariant(),Font=Aero.Caps,ForeColor=Aero.Muted,AutoSize=true,Margin=new Padding(0,S(top),0,S(8))});
 void Para(string t)=>Add(new Label{Text=t,ForeColor=Aero.Muted,AutoSize=true,MaximumSize=new Size(W,0),Margin=new Padding(0,0,0,S(4))});
 void Divider()=>Add(new Panel{Height=1,Width=W,BackColor=Aero.Line,Margin=new Padding(0,S(28),0,S(8))});
 InputBox Input(bool secret=false,string placeholder="",int max=128){var b=new InputBox(secret,placeholder,max){Width=W,Margin=Padding.Empty};Add(b);return b;}
 FlatButton Button(string t,ButtonKind kind,Func<Task> click,Control? into=null){var b=new FlatButton{Text=t,Kind=kind,Margin=new Padding(0,S(14),S(10),0),Surface=Aero.Bg};b.Size=b.GetPreferredSize(Size.Empty);b.Click+=async(_,_)=>await click();(into??body).Controls.Add(b);return b;}
 Label Status(){var l=new Label{AutoSize=true,Font=Aero.Small,MaximumSize=new Size(W,0),Margin=new Padding(0,S(8),0,0),Text=""};Add(l);return l;}
 static void Say(Label l,string text,bool ok){l.Text=text;l.ForeColor=ok?Aero.Green:Aero.Red;}
 FlowLayoutPanel Row(){var r=new FlowLayoutPanel{AutoSize=true,WrapContents=false,BackColor=Aero.Bg,Margin=Padding.Empty};Add(r);return r;}
 void Switch(string title,string text,bool on,Action<bool> changed){var row=new TableLayoutPanel{ColumnCount=2,AutoSize=true,BackColor=Aero.Bg,Margin=new Padding(0,S(4),0,0)};
  row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,W-S(56)));row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
  var words=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,AutoSize=true,WrapContents=false,BackColor=Aero.Bg,Margin=Padding.Empty};
  words.Controls.Add(new Label{Text=title,ForeColor=Aero.Bright,Font=Aero.Bold,AutoSize=true,Margin=new Padding(0,0,0,S(4))});words.Controls.Add(new Label{Text=text,ForeColor=Aero.Muted,Font=Aero.Small,AutoSize=true,MaximumSize=new Size(W-S(72),0),Margin=Padding.Empty});
  var t=new Toggle{Checked=on,Margin=new Padding(S(8),0,0,0)};t.CheckedChanged+=(_,_)=>changed(t.Checked);row.Controls.Add(words,0,0);row.Controls.Add(t,1,0);Add(row);Divider();}
 // Banner card with your picture and name, as friends see it.
 void ProfileCard(bool withEdit){var hub=shell.Hub;var card=new Panel{Size=new Size(W,S(176)),BackColor=Aero.Bg,Margin=new Padding(0,0,0,S(8))};
  card.Paint+=(_,e)=>{var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Aero.Round(new RectangleF(0,0,card.Width,card.Height),S(8)))using(var b=new SolidBrush(Aero.Rail))g.FillPath(b,path);
   using(var shape=Aero.Round(new RectangleF(0,0,card.Width,card.Height),S(8))){var st=g.Save();g.SetClip(shape);using(var b=new LinearGradientBrush(new Rectangle(0,0,card.Width,S(80)),Aero.Steel,Aero.Accent,0f))g.FillRectangle(b,0,0,card.Width,S(80));g.Restore(st);}
   var pic=new RectangleF(S(16),S(40),S(84),S(84));using(var b=new SolidBrush(Aero.Rail))g.FillEllipse(b,RectangleF.Inflate(pic,S(6),S(6)));
   Avatars.DrawWithPresence(g,pic,shell.Account,hub.Avatar(shell.Account),hub.Online?hub.MyPresence:"offline",Aero.Rail);
   TextRenderer.DrawText(g,shell.Account,Aero.Title,new Point(S(116),S(92)),Aero.Bright,TextFormatFlags.NoPrefix);
   TextRenderer.DrawText(g,hub.MyMood.Length>0?hub.MyMood:$"on {shell.RelayHost}",Aero.Small,new Rectangle(S(118),S(128),card.Width-S(140),S(20)),Aero.Muted,TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);};
  if(withEdit){var edit=new FlatButton{Text="Edit profile",Kind=ButtonKind.Primary,Surface=Aero.Rail};edit.Size=edit.GetPreferredSize(Size.Empty);edit.Location=new Point(W-edit.Width-S(16),S(92));edit.Click+=(_,_)=>Open("Profile");card.Controls.Add(edit);}
  Add(card);}

 void Open(string name){current=name;Nav();body.SuspendLayout();foreach(Control c in body.Controls.Cast<Control>().ToList())c.Dispose();body.Controls.Clear();
  switch(name){
   case "My Account":Account();break;case "Profile":Profile();break;case "Notifications":Notifications();break;
   case "Privacy & Safety":Privacy();break;case "Appearance":Appearance();break;case "Windows Settings":WindowsSettings();break;case "Connection":Connection();break;default:About();break;}
  body.ResumeLayout();page.ScrollTo(0);}
 void Account(){Title("My Account");
  if(shell.Token==null){Para($"You're using 3DFx as a guest named {shell.GuestName}. Log in or register to add friends, send direct messages and have a profile.");
   Button("Log in or register",ButtonKind.Primary,()=>{Close();shell.ShowLogin();return Task.CompletedTask;});return;}
  ProfileCard(true);
  Caps("Password and authentication",28);Para("Enter your current password for any of these.");
  Caps("Current password",12);var current=Input(true);Caps("New password",12);var fresh=Input(true,"At least 10 characters");Caps("Confirm new password",12);var again=Input(true);
  var status=Status();
  bool HasCurrent(){if(Protocol.ValidPassword(current.Box.Text))return true;current.Problem=true;Say(status,"Enter your current password first.",false);current.Box.Focus();return false;}
  async Task<AccountReply?> Call(Func<AccountClient,string,Task<AccountReply>> request){UseWaitCursor=true;body.Enabled=false;
   try{using var c=AccountClient.ForRelay(shell.Relay);return await request(c,shell.Token!);}catch(IOException e){Say(status,e.Message,false);return null;}finally{UseWaitCursor=false;body.Enabled=true;}}
  Button("Change password",ButtonKind.Primary,async()=>{if(!HasCurrent())return;
   if(!Protocol.ValidPassword(fresh.Box.Text)){fresh.Problem=true;Say(status,Protocol.PasswordRule,false);fresh.Box.Focus();return;}
   if(fresh.Box.Text!=again.Box.Text){again.Problem=true;Say(status,"The two new passwords are different.",false);again.Box.Focus();return;}
   var r=await Call((c,t)=>c.ChangePassword(t,current.Box.Text,fresh.Box.Text));
   if(r!=null){shell.TokenChanged(r.Token);current.Box.Clear();fresh.Box.Clear();again.Box.Clear();Say(status,"Password changed. Your other devices have been signed out.",true);}});
  Divider();Caps("Recovery key",4);Para("Lost your recovery key? Make a new one; the old one stops working. Needs your current password above.");
  Button("Make a new recovery key",ButtonKind.Secondary,async()=>{if(!HasCurrent())return;var r=await Call((c,t)=>c.NewRecoveryKey(t,current.Box.Text));if(r!=null){current.Box.Clear();RecoveryKeyDialog.Show(this,shell.Account,r.RecoveryKey,true);Say(status,"New recovery key made.",true);}});
  Divider();Caps("Account removal",4);Para("Removes your account from the relay and frees your username. This can't be undone. Needs your current password above.");
  Button("Delete account",ButtonKind.Danger,async()=>{if(!HasCurrent())return;
   if(MessageBox.Show(this,$"Delete the account \"{shell.Account}\" for good?","Delete account",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
   var r=await Call((c,t)=>c.Delete(t,current.Box.Text));if(r!=null){Close();shell.AccountDeleted();}});}
 void Profile(){var hub=shell.Hub;Title("Profile");ProfileCard(false);
  Caps("Avatar",24);var row=Row();Button("Change avatar",ButtonKind.Primary,shell.ChoosePicture,row);Button("Remove avatar",ButtonKind.Ghost,()=>hub.SetAvatar(null),row);
  Divider();Caps("Custom status",4);Para("Your friends see this under your name. Leave it empty to clear it.");
  var mood=Input(false,"e.g. Playing games 🎮",Profiles.MaxMood);mood.Box.Text=hub.MyMood;mood.Margin=new Padding(0,S(8),0,0);var status=Status();
  Button("Save status",ButtonKind.Primary,async()=>{var text=mood.Box.Text.Trim();if(!Profiles.ValidMood(text)){mood.Problem=true;Say(status,$"Up to {Profiles.MaxMood} characters.",false);return;}
   await hub.SetProfile(hub.MyPresence,text);Say(status,"Status saved.",true);});
  Divider();Caps("Online status",4);Para("\"Appear offline\" hides you from friends while you can still chat.");var choices=Row();
  foreach(var p in Profiles.Presences){var choice=p;Button(Profiles.Label(p),hub.MyPresence==p?ButtonKind.Primary:ButtonKind.Secondary,async()=>{shell.ManualPresence();await hub.SetProfile(choice,hub.MyMood);},choices);}}
 void Appearance(){Title("Appearance");Caps("Chat text size",4);Para("The size of messages and the message box.");var row=Row();
  foreach(var (label,scale) in new[]{("Small",0.9f),("Default",1f),("Large",1.15f),("Larger",1.3f)}){var s=scale;Button(label,Math.Abs(shell.TextScale-s)<0.01f?ButtonKind.Primary:ButtonKind.Secondary,()=>{shell.SetTextScale(s);Open("Appearance");return Task.CompletedTask;},row);}
  Caps("Preview",24);var sample=new Panel{Size=new Size(W,S(96)),BackColor=Aero.Bg,Margin=Padding.Empty};
  sample.Paint+=(_,e)=>{var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Aero.Round(new RectangleF(0,0,sample.Width,sample.Height),S(6)))using(var b=new SolidBrush(Aero.Side))g.FillPath(b,path);
   Avatars.Draw(g,new RectangleF(S(16),S(16),S(40),S(40)),"Sam",null);var name=TextRenderer.MeasureText("Sam",Aero.ChatBold,Size.Empty,TextFormatFlags.NoPrefix);
   TextRenderer.DrawText(g,"Sam",Aero.ChatBold,new Point(S(70),S(14)),Aero.NameColor("Sam"),TextFormatFlags.NoPrefix);TextRenderer.DrawText(g,"Today at 20:30",Aero.Small,new Point(S(74)+name.Width,S(14)+(name.Height-S(14))/2),Aero.Muted,TextFormatFlags.NoPrefix);
   TextRenderer.DrawText(g,"Are you coming to friday-friends tonight? 🎉",Aero.Chat,new Point(S(70),S(16)+name.Height),Aero.Text,TextFormatFlags.NoPrefix);};
  sample.Height=S(40)+TextRenderer.MeasureText("Ag",Aero.ChatBold).Height+TextRenderer.MeasureText("Ag",Aero.Chat).Height;Add(sample);}
 void WindowsSettings(){Title("Windows Settings");
  Switch("Close to the system tray","Closing the window keeps 3DFx running in the tray, so you stay online and get notifications. Right-click the tray icon to quit.",shell.CloseToTray,v=>{shell.CloseToTray=v;shell.SavePrefs();});
  Switch("Start with Windows","Opens 3DFx quietly in the tray when you sign in to Windows.",shell.StartWithWindows,v=>{try{shell.StartWithWindows=v;}catch(Exception e) when(e is UnauthorizedAccessException or System.Security.SecurityException or IOException){MessageBox.Show(this,"Windows didn't allow that: "+e.Message,"3DFx Vista");}});
  Switch("Reopen my rooms","When 3DFx starts, rejoin the rooms you had open. Room names and passwords are kept on this PC, encrypted for your Windows account.",shell.ReopenRooms==true,shell.SetReopenRooms);}
 void Notifications(){Title("Notifications");
  Switch("Message sounds","Play a sound when a message, nudge or DM arrives while 3DFx isn't the window in front.",shell.SoundOn,v=>{shell.SoundOn=v;shell.SaveSettings();});
  Switch("Windows notifications","Show a Windows notification for direct messages, @mentions and nudges while 3DFx isn't the window in front. Click it to open the conversation.",shell.DesktopNotifications,v=>{shell.DesktopNotifications=v;shell.SaveSettings();});
  Switch("Startup sound","Play the Windows startup sound when 3DFx opens.",shell.StartupSound,v=>{shell.StartupSound=v;shell.SaveSettings();});
  Para("The taskbar button flashes and shows a red count for unread messages while 3DFx is in the background.");}
 void Privacy(){Title("Privacy & Safety");
  Switch("Show \"Seen\" in direct messages","Friends see when you've read their DMs, and you see when they've read yours. Turn it off to do neither.",shell.Receipts,v=>{shell.Receipts=v;shell.SaveSettings();});
  Caps("What the relay can see",8);Para("The relay can read messages, pictures and videos in rooms. Nothing is saved unless you click Save on a picture or video.");
  Caps("Direct messages");Para("DMs wait on the relay until they're delivered (up to 30 days). After that they live only on this PC, encrypted for your Windows account. Right-click a conversation to clear it from this PC.");
  Caps("Groki");Para("Groki is an AI, always marked AI. He only joins a room when someone types @groki, and remembers things per room.");}
 void Connection(){Title("Connection");Caps("Relay address",4);var relay=Input();relay.Box.Text=shell.Relay;relay.Box.ReadOnly=true;
  Para(shell.Token!=null?"Your account lives on this relay. To use another relay, log out first.":"To use another relay, log in again from the start screen.");
  Divider();Switch("Remember me on this PC","Keeps the relay address and your guest name for next time. Passwords are never saved. Turning this off also forgets \"Keep me signed in\".",shell.Remember,v=>{shell.Remember=v;shell.SaveSettings();});}
 void About(){Title("About");var card=new Panel{Size=new Size(W,S(120)),BackColor=Aero.Bg};
  card.Paint+=(_,e)=>{var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;Aero.Orb(g,new RectangleF(0,S(8),S(96),S(96)));
   TextRenderer.DrawText(g,Shell.AppTitle,Aero.Title,new Point(S(116),S(24)),Aero.Bright,TextFormatFlags.NoPrefix);
   TextRenderer.DrawText(g,"Group chat for you and your friends: rooms, DMs, pictures, videos, stickers, and Groki the AI.",Aero.Body,new Rectangle(S(118),S(62),W-S(120),S(44)),Aero.Muted,TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);};
  Add(card);var status=Status();
  Button("Check for updates",ButtonKind.Primary,async()=>{Say(status,"Checking…",true);Say(status,await shell.CheckForUpdates(),true);});
  Caps("Updates",28);Para($"Installed copies update from GitHub Releases ({Updates.Repository}). Nothing downloads unless you say yes.");}
}
