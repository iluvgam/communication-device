using System.Drawing.Drawing2D;
namespace FriendsChat;

// Home with no DM open: your friends under tabs (Online, All, Pending, Blocked) and Add Friend.
// Each row has round action buttons: message / more for friends, accept / decline for requests, cancel, unblock.
sealed class FriendsView:Control {
 static readonly string[] Tabs=["Online","All","Pending","Blocked","Add Friend"];
 readonly Messenger hub;string tab="Online";int hover=-1,hoverAction=-1,scroll,hoverTab=-1;
 List<ContactInfo> shown=[];string sent="";
 public event Action<string>? OpenDm;
 readonly InputBox addBox=new(placeholder:"Enter a 3DFx username",maxLength:20);
 readonly FlatButton addSend=new(){Text="Send Friend Request",Kind=ButtonKind.Primary,Enabled=false};
 int S(int px)=>LogicalToDeviceUnits(px);
 public FriendsView(Messenger hub){this.hub=hub;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Aero.Bg;
  addBox.Height=S(48);addSend.Size=addSend.GetPreferredSize(Size.Empty);addSend.Height=S(36);addSend.Surface=Aero.Field;Controls.Add(addSend);Controls.Add(addBox);addSend.BringToFront();
  addBox.Box.TextChanged+=(_,_)=>{addSend.Enabled=addBox.Box.TextLength>0;if(sent.Length>0){sent="";Invalidate();}};
  addBox.Box.KeyDown+=async(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;await Add();}};addSend.Click+=async(_,_)=>await Add();
  Reload();}
 long revealed;readonly object revealKey=new();
 public void Reveal(){revealed=Environment.TickCount64;Motion.Run(revealKey,this,240,_=>Invalidate());}
 public void ShowTab(string name){tab=name;scroll=0;Reload();Reveal();if(tab=="Add Friend")addBox.Box.Focus();}
 public void Reload(){var all=hub.Contacts;
  shown=(tab switch{"Online"=>all.Where(c=>c.State=="contact"&&c.Online),"All"=>all.Where(c=>c.State=="contact"),"Pending"=>all.Where(c=>c.State is "incoming" or "outgoing"),"Blocked"=>all.Where(c=>c.State=="blocked"),_=>[]})
   .OrderBy(c=>c.State=="incoming"?0:1).ThenBy(c=>c.Name,StringComparer.OrdinalIgnoreCase).ToList();
  addBox.Visible=addSend.Visible=tab=="Add Friend";hover=hoverAction=-1;PerformLayout();Invalidate();}
 async Task Add(){var name=addBox.Box.Text.Trim();if(name.Length==0)return;
  if(!Accounts.ValidUsername(name)){addBox.Problem=true;sent="!"+Accounts.UsernameRule;Invalidate();return;}
  if(!hub.Online){sent="!Not connected to the relay yet. Try again in a moment.";Invalidate();return;}
  await hub.Contact("add",name);addBox.Box.Clear();sent=$"Friend request sent to {name}. They'll show under Pending until they accept.";Invalidate();}

 int Content=>S(48);int ListTop=>Content+S(56);int RowH=>S(62);
 Rectangle TabRect(int i){int x=S(150);for(int k=0;k<i;k++)x+=TabWidth(k)+S(16);return new(x,S(12),TabWidth(i),S(26));}
 int TabWidth(int i)=>TextRenderer.MeasureText(Tabs[i],Aero.Bold).Width+S(16)+(Tabs[i]=="Pending"&&hub.Contacts.Any(c=>c.State=="incoming")?S(22):0);
 Rectangle Row(int i)=>new(S(20),ListTop+i*RowH-scroll,Math.Max(0,Width-S(40)),RowH);
 string[] Actions(ContactInfo c)=>c.State switch{"contact"=>["message","more"],"incoming"=>["accept","decline"],"outgoing"=>["cancel"],_=>["unblock"]};
 Rectangle ActionRect(Rectangle row,int k,int count)=>new(row.Right-S(10)-(count-k)*S(46)+S(10),row.Y+(row.Height-S(36))/2,S(36),S(36));
 protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);int w=Math.Min(Math.Max(S(200),Width-S(60)),S(760));addBox.SetBounds(S(30),Content+S(104),w,S(52));
  addSend.Location=new Point(addBox.Right-addSend.Width-S(8),addBox.Top+(addBox.Height-addSend.Height)/2);addBox.Box.Width=Math.Max(0,addSend.Left-addBox.Left-S(20));}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int tabHit=-1;for(int i=0;i<Tabs.Length;i++)if(TabRect(i).Contains(e.Location))tabHit=i;
  int row=-1,action=-1;if(tab!="Add Friend"&&e.Y>=ListTop)for(int i=0;i<shown.Count;i++){var r=Row(i);if(!r.Contains(e.Location))continue;row=i;var acts=Actions(shown[i]);for(int k=0;k<acts.Length;k++)if(ActionRect(r,k,acts.Length).Contains(e.Location))action=k;}
  if(row!=hover||action!=hoverAction||tabHit!=hoverTab){hover=row;hoverAction=action;hoverTab=tabHit;Invalidate();}
  Cursor=tabHit>=0||action>=0||(row>=0&&shown[row].State=="contact")?Cursors.Hand:Cursors.Default;}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=hoverAction=hoverTab=-1;Invalidate();}
 protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);scroll=Math.Clamp(scroll-e.Delta*S(40)/120,0,Math.Max(0,shown.Count*RowH-(Height-ListTop)+S(20)));Invalidate();}
 protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(e.Button!=MouseButtons.Left)return;
  for(int i=0;i<Tabs.Length;i++)if(TabRect(i).Contains(e.Location)){ShowTab(Tabs[i]);return;}
  if(hover<0||hover>=shown.Count)return;var c=shown[hover];
  if(hoverAction<0){if(c.State=="contact")OpenDm?.Invoke(c.Name);return;}
  switch(Actions(c)[hoverAction]){
   case "message":OpenDm?.Invoke(c.Name);break;
   case "more":var m=DarkMenu.Create();m.Item("Message",()=>OpenDm?.Invoke(c.Name));m.Item("Remove friend",()=>Confirm($"Remove {c.Name} from your friends?","remove",c.Name),danger:true);m.Item("Block",()=>Confirm($"Block {c.Name}? They won't be able to message you or send requests.","block",c.Name),danger:true);m.Show(this,e.Location);break;
   case "accept":_=hub.Contact("accept",c.Name);break;
   case "decline":_=hub.Contact("decline",c.Name);break;
   case "cancel":_=hub.Contact("remove",c.Name);break;
   case "unblock":_=hub.Contact("unblock",c.Name);break;}}
 void Confirm(string question,string action,string name){if(MessageBox.Show(FindForm(),question,"3DFx Vista",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes)_=hub.Contact(action,name);}

 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(BackColor);Aero.Sheen(g,new Rectangle(0,0,Width,S(160)),BackColor,S(160));g.SmoothingMode=SmoothingMode.AntiAlias;
  const TextFormatFlags one=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter;
  const TextFormatFlags centre=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix;
  // Top bar: Friends, divider, tabs.
  TextRenderer.DrawText(g,Aero.IconPeople,Aero.Icons(15f),new Rectangle(S(16),0,S(24),S(48)),Aero.Faint,centre);
  TextRenderer.DrawText(g,"Friends",Aero.Header,new Rectangle(S(48),0,S(90),S(48)),Aero.Bright,one);
  using(var b=new SolidBrush(Aero.Line))g.FillRectangle(b,S(132),S(14),S(1),S(20));
  int incoming=hub.Contacts.Count(c=>c.State=="incoming");
  for(int i=0;i<Tabs.Length;i++){var r=TabRect(i);bool sel=tab==Tabs[i],hot=hoverTab==i,add=Tabs[i]=="Add Friend";
   var back=add?(sel?Color.Empty:Aero.Green):sel?Aero.Selected:hot?Aero.SideHover:Color.Empty;
   if(back!=Color.Empty)using(var path=Aero.Round(r,S(4)))using(var b=new SolidBrush(back))g.FillPath(b,path);
   bool count=Tabs[i]=="Pending"&&incoming>0;TextRenderer.DrawText(g,Tabs[i],Aero.Bold,count?r with{Width=r.Width-S(22)}:r,add?(sel?Aero.Green:Color.White):sel||hot?Aero.Bright:Aero.Muted,centre);
   if(count)Aero.Pill(g,incoming.ToString(),Aero.SmallBold,r.Right-S(26),r.Y+r.Height/2,Aero.Red,Color.White,this);}
  using(var p=new Pen(Aero.Rail))g.DrawLine(p,0,S(47),Width,S(47));
  if(tab=="Add Friend"){
   TextRenderer.DrawText(g,"ADD FRIEND",Aero.Header,new Point(S(28),Content+S(24)),Aero.Bright,TextFormatFlags.NoPrefix);
   TextRenderer.DrawText(g,"You can add friends with their 3DFx username. They need to accept before you can message each other.",Aero.Body,new Rectangle(S(30),Content+S(54),Width-S(60),S(40)),Aero.Muted,TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
   if(sent.Length>0){bool bad=sent[0]=='!';TextRenderer.DrawText(g,bad?sent[1..]:sent,Aero.Small,new Rectangle(S(30),addBox.Bottom+S(8),Width-S(60),S(22)),bad?Aero.Red:Aero.Green,one);}
   using(var p=new Pen(Aero.Line))g.DrawLine(p,S(20),addBox.Bottom+S(44),Width-S(20),addBox.Bottom+S(44));
   TextRenderer.DrawText(g,hub.Online?"Friends see your status and status line, and can send you DMs.":"Connecting to the relay…",Aero.Small,new Rectangle(S(30),addBox.Bottom+S(56),Width-S(60),S(22)),Aero.Faint,one);return;}
  var heading=tab switch{"Online"=>"Online","All"=>"All friends","Pending"=>"Pending",_=>"Blocked"};
  TextRenderer.DrawText(g,$"{heading.ToUpperInvariant()} — {shown.Count}",Aero.Caps,new Rectangle(S(30),Content+S(20),Width-S(60),S(24)),Aero.Muted,one);
  if(!hub.Online)TextRenderer.DrawText(g,"Connecting to the relay…",Aero.Small,new Rectangle(0,Content+S(20),Width-S(30),S(24)),Aero.Faint,TextFormatFlags.Right|one);
  if(shown.Count==0){var empty=tab switch{"Online"=>"Nobody's online right now. Your friends show up here when they are.","All"=>"No friends yet. Use Add Friend with someone's 3DFx username.","Pending"=>"No pending friend requests.",_=>"You haven't blocked anyone."};
   TextRenderer.DrawText(g,empty,Aero.Body,new Rectangle(S(30),Content+S(140),Width-S(60),S(60)),Aero.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);return;}
  var st=g.Save();g.SetClip(new Rectangle(0,ListTop-S(8),Width,Height));
  for(int i=0;i<shown.Count;i++){var c=shown[i];var r=Row(i);if(r.Bottom<ListTop-S(8)||r.Top>Height)continue;bool hot=i==hover;var back=hot?Aero.SideHover:BackColor;
   if(hot){using var path=Aero.Round(r,S(8));using var b=new SolidBrush(back);g.FillPath(b,path);}
   else if(i==0||hover!=i-1)using(var p=new Pen(Aero.Line))g.DrawLine(p,r.X+S(10),r.Y,r.Right-S(10),r.Y);
   int pic=S(32);Avatars.DrawWithPresence(g,new RectangleF(r.X+S(10),r.Y+(r.Height-pic)/2f,pic,pic),c.Name,c.Bot?AMLogo.Picture:hub.Avatar(c.Name),c.State=="contact"?(c.Online?c.Presence:"offline"):null,back);
   int x=r.X+S(54);var nameW=TextRenderer.MeasureText(c.Name,Aero.Bold,Size.Empty,TextFormatFlags.NoPrefix).Width;TextRenderer.DrawText(g,c.Name,Aero.Bold,new Rectangle(x,r.Y+S(10),nameW,S(22)),c.State=="contact"?Avatars.PresenceName(c.Online?c.Presence:"offline"):Aero.Bright,one);
   if(c.Bot)Badge.Draw(g,"AI",x+nameW+S(4),r.Y+S(21),this);
   var sub=c.State switch{"incoming"=>"Incoming friend request","outgoing"=>"Outgoing friend request","blocked"=>"Blocked",_=>c.Mood.Length>0?c.Mood:c.Online?Profiles.Label(c.Presence):"Offline"};
   var acts=Actions(c);TextRenderer.DrawText(g,sub,Aero.Small,new Rectangle(x,r.Y+S(32),Math.Max(0,ActionRect(r,0,acts.Length).X-x-S(10)),S(20)),Aero.Muted,one);
   for(int k=0;k<acts.Length;k++){var a=ActionRect(r,k,acts.Length);bool on=hot&&hoverAction==k;using(var b=new SolidBrush(hot?Aero.Rail:Aero.Side))g.FillEllipse(b,a);
    var (glyph,tint)=acts[k] switch{"message"=>(Aero.IconChat,Aero.Bright),"more"=>(Aero.IconMore,Aero.Bright),"accept"=>(Aero.IconCheck,Aero.Green),"unblock"=>(Aero.IconBlock,Aero.Red),_=>(Aero.IconClose,Aero.Red)};
    TextRenderer.DrawText(g,glyph,Aero.Icons(12f),a,on?tint:Aero.Muted,centre);}}
  g.Restore(st);
  double t=Math.Min(1,(Environment.TickCount64-revealed)/240.0);if(t<1)using(var veil=new SolidBrush(Color.FromArgb((int)((1-Motion.Out(t))*255),BackColor)))g.FillRectangle(veil,0,S(48),Width,Height);}
}
