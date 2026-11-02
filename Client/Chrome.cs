using System.Drawing.Drawing2D;
namespace FriendsChat;

// Dark tooltips for icon buttons and the room rail.
static class DarkTip {
 public static ToolTip Create(){var t=new ToolTip{OwnerDraw=true,InitialDelay=300,ReshowDelay=100};
  t.Popup+=(_,e)=>{var s=TextRenderer.MeasureText(t.GetToolTip(e.AssociatedControl),Aero.Bold);e.ToolTipSize=new Size(s.Width+16,s.Height+12);};
  t.Draw+=(_,e)=>{e.Graphics.Clear(Aero.Rail);using(var p=new Pen(Aero.Panel))e.Graphics.DrawRectangle(p,0,0,e.Bounds.Width-1,e.Bounds.Height-1);
   TextRenderer.DrawText(e.Graphics,e.ToolTipText,Aero.Bold,e.Bounds,Aero.Bright,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);};return t;}
}

// The far-left column: Home (friends and DMs), one round icon per room, then + to join or create a room.
// The selected icon becomes a rounded square with a white pill beside it; unread rooms get a small pill, mentions a red count.
sealed class RoomRail:Control {
 public sealed record Entry(string Key,string Name,bool Selected,int Unread,int Mentions,bool Down);
 List<Entry> rooms=[];bool home=true;int homeBadge,hover=int.MinValue;
 readonly Dictionary<string,(Glide Shape,Glide Pill)> motion=[];
 (Glide Shape,Glide Pill) Moving(string key){if(!motion.TryGetValue(key,out var m))motion[key]=m=(new Glide(this),new Glide(this));return m;}
 // Eased shape (0 = rounded, 1 = squarer and lit) and pill height (logical px) for a slot.
 (float Shape,int Pill) Morph(string key,bool lit,int pill){var (s,p)=Moving(key);s.To(lit?1:0,lit?220:320,Motion.Out);p.To(pill,380,Motion.Spring);return (s.Value,(int)Math.Round(Math.Max(0,p.Value)*DeviceDpi/96.0));}
 public event Action? HomeClicked,AddClicked;public event Action<string>? RoomClicked;public event Action<string,Point>? RoomMenu;
 readonly ToolTip tip=DarkTip.Create();string tipText="";
 int S(int px)=>LogicalToDeviceUnits(px);
 public RoomRail(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Aero.Rail;Width=S(72);}
 public void Set(bool homeSelected,int badge,List<Entry> list){home=homeSelected;homeBadge=badge;rooms=list;Invalidate();}
 // Slot -1 is Home, 0..n-1 the rooms, n the + button.
 Rectangle Slot(int i)=>i<0?new(S(12),S(12),S(48),S(48)):new(S(12),S(78)+i*S(56),S(48),S(48));
 int HitSlot(Point p){for(int i=-1;i<=rooms.Count;i++)if(Slot(i).Contains(p))return i;return int.MinValue;}
 static string Initials(string room){var parts=room.Split('-',StringSplitOptions.RemoveEmptyEntries);return parts.Length==0?"?":string.Concat(parts.Take(3).Select(p=>char.ToUpperInvariant(p[0])));}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(Aero.Rail);g.SmoothingMode=SmoothingMode.AntiAlias;
  void Pill(Rectangle slot,int height){if(height<=0)return;var r=new RectangleF(-S(4),slot.Y+(slot.Height-height)/2f,S(8),height);using var p=Aero.Round(r,S(4));using var b=new SolidBrush(Color.White);g.FillPath(b,p);}
  void Count(Rectangle slot,int n){if(n<=0)return;var text=n>99?"99+":n.ToString();var size=TextRenderer.MeasureText(text,Aero.SmallBold);int h=S(16),w=Math.Max(h,size.Width+S(4));
   var r=new RectangleF(slot.Right-w+S(4),slot.Bottom-h+S(4),w,h);using(var ring=Aero.Round(RectangleF.Inflate(r,S(3),S(3)),(h+S(6))/2f))using(var b=new SolidBrush(Aero.Rail))g.FillPath(b,ring);
   using(var path=Aero.Round(r,h/2f))using(var b=new SolidBrush(Aero.Red))g.FillPath(b,path);TextRenderer.DrawText(g,text,Aero.SmallBold,Rectangle.Round(r),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);}
  // Home: the 3DFx orb.
  float Radius(float k)=>Motion.Lerp(S(14),S(8),k);
  var hs=Slot(-1);bool hot=hover==-1;var (k,pill)=Morph("home",home||hot,home?40:hot?20:0);
  using(var shape=Aero.Round(hs,Radius(k)))using(var b=new SolidBrush(Motion.Mix(Aero.Bg,Aero.Accent,k)))g.FillPath(b,shape);
  TextRenderer.DrawText(g,"\uE80F",Aero.Icons(17f),hs,Motion.Mix(Aero.Text,Color.White,k),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);Pill(hs,pill);Count(hs,homeBadge);
  using(var b=new SolidBrush(Aero.Line))g.FillRectangle(b,S(24),S(68),S(24),S(2));
  for(int i=0;i<rooms.Count;i++){var r=rooms[i];var slot=Slot(i);hot=hover==i;
   var (rk,rp)=Morph(r.Key,r.Selected||hot,r.Selected?40:hot?20:r.Unread>0?8:0);
   using(var shape=Aero.Round(slot,Radius(rk)))using(var b=new SolidBrush(Motion.Mix(Aero.Bg,r.Down?Aero.C(0x4E5058):Aero.Accent,rk)))g.FillPath(b,shape);
   TextRenderer.DrawText(g,Initials(r.Name),Aero.Bold,slot,r.Down?Aero.Faint:Aero.Bright,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
   if(r.Down){var dot=new RectangleF(slot.Right-S(12),slot.Bottom-S(12),S(12),S(12));using(var b=new SolidBrush(Aero.Rail))g.FillEllipse(b,RectangleF.Inflate(dot,S(3),S(3)));using(var b=new SolidBrush(Aero.Red))g.FillEllipse(b,dot);}
   Pill(slot,rp);Count(slot,r.Mentions);}
  var add=Slot(rooms.Count);hot=hover==rooms.Count;var (ak,_)=Morph("add",hot,0);using(var shape=Aero.Round(add,Radius(ak)))using(var b=new SolidBrush(Motion.Mix(Aero.Bg,Aero.Green,ak)))g.FillPath(b,shape);
  TextRenderer.DrawText(g,Aero.IconAdd,Aero.Icons(14f),add,Motion.Mix(Aero.Green,Color.White,ak),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int h=HitSlot(e.Location);if(h==hover)return;hover=h;Invalidate();Cursor=h==int.MinValue?Cursors.Default:Cursors.Hand;
  var text=h==-1?"Home":h>=0&&h<rooms.Count?rooms[h].Name+(rooms[h].Down?" (disconnected)":""):h==rooms.Count?"Join or create a room":"";
  if(text!=tipText){tipText=text;if(text.Length==0)tip.Hide(this);else tip.Show(text,this,Width-S(4),Slot(h).Y+S(12),2500);}}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=int.MinValue;tipText="";tip.Hide(this);Invalidate();}
 protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);int h=HitSlot(e.Location);
  if(e.Button==MouseButtons.Right){if(h>=0&&h<rooms.Count)RoomMenu?.Invoke(rooms[h].Key,PointToScreen(e.Location));return;}
  if(h==-1)HomeClicked?.Invoke();else if(h>=0&&h<rooms.Count)RoomClicked?.Invoke(rooms[h].Key);else if(h==rooms.Count)AddClicked?.Invoke();}
}

// A row in the sidebar or member list: a section heading, or an item with a picture or icon, a name, a second line and an unread count.
sealed class SideRow {
 public string Text="",Sub="",Tag="",Glyph="";public bool Heading,Avatar,Selected,Bold;public string? Presence;public Image? Picture;public int Badge;public Color? Color;public string Key="";
 public Action? Click,Plus;public Action<Point>? Menu;
}
sealed class SideList:Control {
 List<SideRow> rows=[];int scroll,hover=-1;bool hoverPlus;
 readonly Dictionary<int,Glide> fades=[];
 float Fade(int i,bool on){if(!fades.TryGetValue(i,out var f)){if(!on)return 0;fades[i]=f=new Glide(this);}f.To(on?1:0,on?120:260);return f.Value;}
 // Steam's top-lit gradient behind the first rows.
 public bool Sheen;
 int S(int px)=>LogicalToDeviceUnits(px);
 public SideList(Color back){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=back;}
 public void SetRows(List<SideRow> list){rows=list;scroll=Math.Clamp(scroll,0,MaxScroll);if(hover>=rows.Count)hover=-1;Invalidate();}
 int RowHeight(SideRow r)=>r.Heading?S(40):r.Avatar?S(44):S(36);
 int MaxScroll=>Math.Max(0,rows.Sum(RowHeight)+S(16)-ClientSize.Height);
 (int Index,Rectangle Bounds) Hit(Point p){int y=S(8)-scroll;for(int i=0;i<rows.Count;i++){int h=RowHeight(rows[i]);if(p.Y>=y&&p.Y<y+h)return(i,new Rectangle(0,y,Width,h));y+=h;}return(-1,Rectangle.Empty);}
 Rectangle PlusArea(Rectangle row)=>new(Width-S(32),row.Y+S(16),S(20),S(20));
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);var (i,b)=Hit(e.Location);bool plus=i>=0&&rows[i].Plus!=null&&PlusArea(b).Contains(e.Location);
  if(i!=hover||plus!=hoverPlus){hover=i;hoverPlus=plus;Invalidate();}Cursor=i>=0&&(rows[i].Click!=null||plus)?Cursors.Hand:Cursors.Default;}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=-1;hoverPlus=false;Invalidate();}
 protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);scroll=Math.Clamp(scroll-e.Delta*S(40)/120,0,MaxScroll);Invalidate();}
 protected override void OnResize(EventArgs e){base.OnResize(e);scroll=Math.Clamp(scroll,0,MaxScroll);}
 protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);var (i,b)=Hit(e.Location);if(i<0)return;var r=rows[i];
  if(e.Button==MouseButtons.Right){r.Menu?.Invoke(PointToScreen(e.Location));return;}
  if(r.Plus!=null&&PlusArea(b).Contains(e.Location))r.Plus();else r.Click?.Invoke();}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(BackColor);if(Sheen)Aero.Sheen(g,new Rectangle(0,-scroll,Width,S(140)),BackColor,S(140));g.SmoothingMode=SmoothingMode.AntiAlias;int y=S(8)-scroll;
  const TextFormatFlags one=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter;
  for(int i=0;i<rows.Count;i++){var r=rows[i];int h=RowHeight(r);if(y+h<0){y+=h;continue;}if(y>ClientSize.Height)break;bool hot=i==hover;
   if(r.Heading){TextRenderer.DrawText(g,r.Text.ToUpperInvariant(),Aero.Caps,new Rectangle(S(16),y+S(16),Width-S(56),S(20)),hot&&r.Click!=null?Aero.Text:Aero.Muted,one);
    if(r.Plus!=null)TextRenderer.DrawText(g,Aero.IconAdd,Aero.Icons(10f),PlusArea(new Rectangle(0,y,Width,h)),hot&&hoverPlus?Aero.Bright:Aero.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);y+=h;continue;}
   var item=new Rectangle(S(8),y+S(1),Width-S(16),h-S(2));var back=r.Selected?Aero.Selected:Motion.Mix(BackColor,Aero.SideHover,Fade(i,hot&&(r.Click!=null||r.Menu!=null)));
   if(back!=BackColor)using(var path=Aero.Round(item,S(4)))using(var b=new SolidBrush(back))g.FillPath(b,path);
   int x=item.X+S(8);
   if(r.Avatar){int pic=S(32);Avatars.DrawWithPresence(g,new RectangleF(x,item.Y+(item.Height-pic)/2f,pic,pic),r.Text,r.Picture,r.Presence,back);x+=pic+S(12);}
   else if(r.Glyph.Length>0){TextRenderer.DrawText(g,r.Glyph,r.Glyph.Length==1&&r.Glyph[0]<0x2000?Aero.Subtitle:Aero.Icons(13f),new Rectangle(x,item.Y,S(24),item.Height),r.Color??(r.Selected||hot?Aero.Text:Aero.Muted),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);x+=S(32);}
   int right=item.Right-S(8);
   if(r.Badge>0){var text=r.Badge>99?"99+":r.Badge.ToString();right-=Aero.PillWidth(text,Aero.SmallBold,this);Aero.Pill(g,text,Aero.SmallBold,right,item.Y+item.Height/2,Aero.Red,Color.White,this);right-=S(6);}
   var color=r.Color??(r.Selected||hot||r.Bold?Aero.Bright:Aero.Muted);var font=r.Bold?Aero.Bold:Aero.Body;
   var nameSize=TextRenderer.MeasureText(r.Text,font,Size.Empty,TextFormatFlags.NoPrefix);int nameW=Math.Min(nameSize.Width,Math.Max(0,right-x-(r.Tag.Length>0?S(40):0)));
   bool two=r.Sub.Length>0;var nameBox=new Rectangle(x,two?item.Y+S(3):item.Y,nameW,two?item.Height/2:item.Height);
   TextRenderer.DrawText(g,r.Text,font,nameBox,color,one);
   if(r.Tag.Length>0)Badge.Draw(g,r.Tag,x+nameW+S(4),nameBox.Y+nameBox.Height/2,this);
   if(two)TextRenderer.DrawText(g,r.Sub,Aero.Small,new Rectangle(x,item.Y+item.Height/2-S(2),Math.Max(0,right-x),item.Height/2),Aero.Muted,one);
   y+=h;}}
}

// Bottom of the sidebar: your picture with status, your name and second line (click for status and picture choices); the gear opens settings.
sealed class UserPanel:Control {
 string name="",sub="",tag="";string? presence;Image? picture;bool hot;
 public event Action<Point>? ProfileClicked;public event Action? SettingsClicked;
 readonly FlatButton gear=new(){Kind=ButtonKind.Icon,Glyph=Aero.IconSettings,GlyphSize=14f};
 int S(int px)=>LogicalToDeviceUnits(px);
 Rectangle Profile=>new(S(6),S(8),Math.Max(0,Width-S(52)),Height-S(16));
 public UserPanel(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Aero.Panel;Height=S(56);
  gear.Surface=Aero.Panel;Controls.Add(gear);gear.Click+=(_,_)=>SettingsClicked?.Invoke();DarkTip.Create().SetToolTip(gear,"User settings");}
 public void Show(string name,string sub,string? presence,Image? picture,string tag=""){this.name=name;this.sub=sub;this.presence=presence;this.picture=picture;this.tag=tag;Invalidate();}
 protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);gear.SetBounds(Width-S(44),(Height-S(32))/2,S(32),S(32));}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);bool h=Profile.Contains(e.Location);if(h!=hot){hot=h;Invalidate();Cursor=h?Cursors.Hand:Cursors.Default;}}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=false;Invalidate();}
 protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(Profile.Contains(e.Location))ProfileClicked?.Invoke(PointToScreen(new Point(Profile.Left,Profile.Top-S(4))));}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;var back=hot?Aero.SideHover:BackColor;
  if(hot)using(var path=Aero.Round(Profile,S(4)))using(var b=new SolidBrush(back))g.FillPath(b,path);
  int pic=S(32);Avatars.DrawWithPresence(g,new RectangleF(S(10),(Height-pic)/2f,pic,pic),name,picture,presence,back);
  int x=S(52),w=Math.Max(0,Profile.Right-x-S(4));const TextFormatFlags one=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis;
  var nameW=Math.Min(w-(tag.Length>0?S(48):0),TextRenderer.MeasureText(name,Aero.Bold,Size.Empty,TextFormatFlags.NoPrefix).Width);TextRenderer.DrawText(g,name,Aero.Bold,new Rectangle(x,S(10),Math.Max(0,nameW),S(20)),presence==null?Aero.Bright:Avatars.PresenceName(presence),one);
  if(tag.Length>0)Badge.Draw(g,tag,x+nameW+S(4),S(20),this);
  TextRenderer.DrawText(g,sub,Aero.Small,new Rectangle(x,S(30),w,S(18)),Aero.Muted,one);}
}

// Top of the sidebar: a "find" bar on Home, or the room's name with a menu arrow.
sealed class SideTitle:Control {
 bool room;bool hot;
 public event Action<Point>? Clicked;
 int S(int px)=>LogicalToDeviceUnits(px);
 public SideTitle(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Aero.Side;Height=S(48);Cursor=Cursors.Hand;}
 public void Show(string text,bool isRoom){Text=text;room=isRoom;Invalidate();}
 protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hot=true;Invalidate();}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=false;Invalidate();}
 protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);Clicked?.Invoke(PointToScreen(new Point(S(8),Height)));}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(BackColor);if(room&&hot)using(var b=new SolidBrush(Aero.SideHover))g.FillRectangle(b,ClientRectangle);else Aero.Sheen(g,ClientRectangle,BackColor,Height);g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var p=new Pen(Aero.Rail))g.DrawLine(p,0,Height-1,Width,Height-1);
  const TextFormatFlags one=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter;
  if(room){TextRenderer.DrawText(g,Text,Aero.Header,new Rectangle(S(16),0,Width-S(52),Height),Aero.Bright,one);
   TextRenderer.DrawText(g,Aero.IconDown,Aero.Icons(10f),new Rectangle(Width-S(36),0,S(20),Height),Aero.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);return;}
  var bar=new Rectangle(S(10),S(10),Width-S(20),S(28));using(var path=Aero.Round(bar,S(4)))using(var b=new SolidBrush(Aero.Rail))g.FillPath(b,path);
  TextRenderer.DrawText(g,Text,Aero.Small,new Rectangle(bar.X+S(8),bar.Y,bar.Width-S(16),bar.Height),hot?Aero.Text:Aero.Muted,one);}
}

// Top bar of the chat area: # room or @ friend, a divider and the topic; icon buttons (Tools) on the right.
sealed class ChatHeader:Control {
 string glyph="#",topic="",tag="";string? presence;
 public readonly FlowLayoutPanel Tools=new(){FlowDirection=FlowDirection.RightToLeft,Dock=DockStyle.Right,AutoSize=true,WrapContents=false,BackColor=Aero.Bg};
 int S(int px)=>LogicalToDeviceUnits(px);
 public ChatHeader(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Aero.Bg;Height=S(48);
  Tools.Padding=new Padding(0,S(8),S(10),0);Controls.Add(Tools);}
 public void Show(string glyph,string title,string topic,string? presence=null,string tag=""){this.glyph=glyph;Text=title;this.topic=topic;this.presence=presence;this.tag=tag;Invalidate();}
 public FlatButton Tool(string glyph,string tip,Action click){var b=new FlatButton{Kind=ButtonKind.Icon,Glyph=glyph,GlyphSize=15f,Size=new Size(S(32),S(32)),Margin=new Padding(S(6),0,0,0),Surface=Aero.Bg};b.Click+=(_,_)=>click();DarkTip.Create().SetToolTip(b,tip);Tools.Controls.Add(b);return b;}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var p=new Pen(Aero.Rail))g.DrawLine(p,0,Height-1,Width,Height-1);using(var p=new Pen(Aero.C(0x000000,40)))g.DrawLine(p,0,Height-2,Width,Height-2);
  const TextFormatFlags one=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter;
  int x=S(16);bool plain=glyph.Length==1&&glyph[0]<0x2000;var gfont=plain?Aero.Subtitle:Aero.Icons(15f);
  TextRenderer.DrawText(g,glyph,gfont,new Rectangle(x,0,S(24),Height),Aero.Faint,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);x+=S(30);
  int limit=Tools.Left-S(8);var ts=TextRenderer.MeasureText(Text,Aero.Header,Size.Empty,TextFormatFlags.NoPrefix);int tw=Math.Min(ts.Width,Math.Max(0,limit-x));TextRenderer.DrawText(g,Text,Aero.Header,new Rectangle(x,0,tw,Height),Aero.Bright,one);x+=tw;
  if(presence!=null){int d=S(10);Avatars.DrawPresence(g,new RectangleF(x+S(4),(Height-d)/2f,d,d),presence,BackColor);x+=S(18);}
  if(tag.Length>0)x+=Badge.Draw(g,tag,x+S(4),Height/2,this)+S(4);
  if(topic.Length>0&&x+S(40)<limit){using(var b=new SolidBrush(Aero.Line))g.FillRectangle(b,x+S(12),S(14),S(1),Height-S(28));x+=S(24);
   TextRenderer.DrawText(g,topic,Aero.Small,new Rectangle(x,0,Math.Max(0,limit-x),Height),Aero.Muted,one);}}
}

// The message box: a rounded bar with + (attach), the text, smileys and stickers, nudge and send.
sealed class Composer:Control {
 public readonly TextBox Box=new(){BorderStyle=BorderStyle.None,BackColor=Aero.Input,ForeColor=Aero.Text,Font=Aero.Chat,MaxLength=2000,Multiline=true,WordWrap=true,ScrollBars=ScrollBars.None};
 public readonly FlatButton Attach,Emoji,Nudge,Send;
 int S(int px)=>LogicalToDeviceUnits(px);
 // Above the box: "Replying to Sam" or "Editing message", with an ✕ (Esc does the same).
 string stripLead="",stripBold="",stripTail="";public event Action? StripClosed;
 public bool HasStrip=>stripLead.Length>0;
 public void SetStrip(string lead,string bold="",string tail=""){if(lead==stripLead&&bold==stripBold&&tail==stripTail)return;stripLead=lead;stripBold=bold;stripTail=tail;Fit();Invalidate();}
 int StripH=>HasStrip?S(30):0;
 Rectangle StripClose=>new(Width-S(16)-S(34),0,S(34),StripH);
 Rectangle Bar=>new(S(16),StripH,Math.Max(0,Width-S(32)),Math.Max(S(46),Height-StripH));
 int lineHeight;
 // Height of the text: one line, growing with Shift+Enter or wrapping up to 8 lines (then it scrolls).
 int TextHeight(int width){if(lineHeight==0)lineHeight=TextRenderer.MeasureText("Ag",Box.Font,Size.Empty,TextFormatFlags.NoPadding).Height;
  int h=Box.TextLength==0?lineHeight:TextRenderer.MeasureText(Box.Text+(Box.Text.EndsWith('\n')?" ":""),Box.Font,new Size(Math.Max(1,width),0),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl|TextFormatFlags.NoPadding).Height;
  return Math.Clamp(h,lineHeight,lineHeight*8);}
 public void Restyle(){Box.Font=Aero.Chat;lineHeight=0;Fit();PerformLayout();}
 void Fit(){if(Send==null)return;int want=Math.Max(S(46),TextHeight(Box.Width)+S(24))+StripH;Box.ScrollBars=TextHeight(Box.Width)>=lineHeight*8?ScrollBars.Vertical:ScrollBars.None;if(Height!=want)Height=want;else PerformLayout();}
 FlatButton Icon(string glyph,string tip,float size=15f){var b=new FlatButton{Kind=ButtonKind.Icon,Glyph=glyph,GlyphSize=size,Surface=Aero.Input,Size=new Size(S(36),S(36))};DarkTip.Create().SetToolTip(b,tip);Controls.Add(b);return b;}
 public Composer(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Aero.Bg;
  Attach=Icon(Aero.IconAdd,"Send a picture or video");Emoji=Icon(Aero.IconEmoji,"Smileys and stickers",16f);Nudge=Icon(Aero.IconBolt,"Send a nudge");Send=Icon(Aero.IconSend,"Send (Enter; Shift+Enter for a new line)");Controls.Add(Box);Height=S(46);Box.TextChanged+=(_,_)=>Fit();}
 protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);if(Send==null)return;var bar=Bar;int y=bar.Bottom-S(41),x=bar.X+S(6);Attach.SetBounds(x,y,S(36),S(36));
  int right=bar.Right-S(6);foreach(var b in new[]{Send,Nudge,Emoji}){if(!b.Visible)continue;right-=S(36);b.SetBounds(right,y,S(36),S(36));right-=S(2);}
  int w=Math.Max(0,right-Attach.Right-S(14)),h=TextHeight(w);Box.SetBounds(Attach.Right+S(8),bar.Y+(bar.Height-h)/2,w,h);}
 protected override void OnResize(EventArgs e){base.OnResize(e);Invalidate();}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
  if(HasStrip){var strip=new Rectangle(S(16),0,Math.Max(0,Width-S(32)),StripH+S(10));using(var sp=Aero.Round(strip,S(8)))using(var sb=new SolidBrush(Aero.Side))g.FillPath(sb,sp);
   const TextFormatFlags one=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis;int x=S(30);
   foreach(var (text,font,color) in new[]{(stripLead,Aero.Small,Aero.Muted),(stripBold,Aero.SmallBold,Aero.Bright),(stripTail,Aero.Small,Aero.Faint)}){if(text.Length==0)continue;
    int w=Math.Min(TextRenderer.MeasureText(text,font,Size.Empty,TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding).Width,Math.Max(0,StripClose.X-x));TextRenderer.DrawText(g,text,font,new Rectangle(x,0,w,StripH),color,one|TextFormatFlags.NoPadding);x+=w;}
   bool hot=StripClose.Contains(PointToClient(Cursor.Position));TextRenderer.DrawText(g,Aero.IconClose,Aero.Icons(9f),StripClose,hot?Aero.Bright:Aero.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);}
  using var path=Aero.Round(Bar,S(8));using var b=new SolidBrush(Aero.Input);g.FillPath(b,path);}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(HasStrip){Cursor=StripClose.Contains(e.Location)?Cursors.Hand:Cursors.Default;Invalidate(StripClose);}}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(HasStrip)Invalidate(StripClose);}
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(HasStrip&&StripClose.Contains(e.Location)){StripClosed?.Invoke();return;}if(Bar.Contains(e.Location))Box.Focus();}
}

// The line under the message box: who is typing / uploads / receipts on the left, the privacy note on the right when there's room.
sealed class ActivityLine:Control {
 string activity="",note="";
 int S(int px)=>LogicalToDeviceUnits(px);
 public ActivityLine(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Aero.Bg;Height=S(28);}
 public void Set(string activity,string note){if(activity==this.activity&&note==this.note)return;this.activity=activity;this.note=note;Invalidate();
  if(activity.Contains("typing"))Motion.Loop(this,()=>Visible&&this.activity.Contains("typing"));}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(BackColor);const TextFormatFlags one=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter;
  int x=S(18),aw=activity.Length>0?TextRenderer.MeasureText(activity,Aero.Small,Size.Empty,TextFormatFlags.NoPrefix).Width:0;
  if(activity.Length>0){if(activity.Contains("typing")){g.SmoothingMode=SmoothingMode.AntiAlias;var bubble=new RectangleF(x-S(2),Height/2f-S(8),S(30),S(16));
    using(var path=Aero.Round(bubble,S(8)))using(var b=new SolidBrush(Aero.Input))g.FillPath(b,path);double time=Environment.TickCount64/1000.0;
    for(int i=0;i<3;i++){double wave=Math.Max(0,Math.Sin((time*2*Math.PI/1.1)-i*0.7));float lift=(float)(wave*S(2));
     using var b=new SolidBrush(Motion.Mix(Aero.Muted,Aero.Bright,wave));g.FillEllipse(b,bubble.X+S(5)+i*S(7),bubble.Y+S(6)-lift,S(5),S(5));}x+=S(36);}
   TextRenderer.DrawText(g,activity,Aero.Small,new Rectangle(x,0,Math.Max(0,Width-x-S(16)),Height),Aero.Text,one);x+=aw+S(24);}
  var nw=TextRenderer.MeasureText(note,Aero.Small,Size.Empty,TextFormatFlags.NoPrefix).Width;if(note.Length>0&&Width-S(16)-nw>x)TextRenderer.DrawText(g,note,Aero.Small,new Rectangle(Width-S(16)-nw,0,nw,Height),Aero.Faint,one);}
}

// Red strip under the header when a room's connection ended, with Rejoin.
sealed class Banner:Control {
 public readonly FlatButton Action=new(){Text="Rejoin",Kind=ButtonKind.Secondary};
 int S(int px)=>LogicalToDeviceUnits(px);
 public Banner(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Aero.C(0xA1282C);Height=S(40);Action.Surface=BackColor;Controls.Add(Action);}
 protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);Action.SetBounds(Width-S(100),S(5),S(88),S(30));}
 protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(BackColor);
  TextRenderer.DrawText(g,Text,Aero.Bold,new Rectangle(S(16),0,Math.Max(0,Width-S(128)),Height),Color.White,TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter);}
}

// Home for guests: what they can do without an account.
sealed class GuestHome:Control {
 public readonly FlatButton Join=new(){Text="Join or create a room",Kind=ButtonKind.Primary},LogIn=new(){Text="Log in or register",Kind=ButtonKind.Secondary};
 int S(int px)=>LogicalToDeviceUnits(px);
 public GuestHome(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Aero.Bg;
  foreach(var b in new[]{Join,LogIn}){b.Size=b.GetPreferredSize(Size.Empty);b.Width=Math.Max(b.Width,S(200));b.Surface=Aero.Bg;Controls.Add(b);}}
 int Top0=>Height/2-S(150);
 protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);int w=Join.Width+LogIn.Width+S(12),x=(Width-w)/2,y=Top0+S(232);Join.Location=new Point(x,y);LogIn.Location=new Point(Join.Right+S(12),y);}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;int y=Top0;
  Aero.Orb(g,new RectangleF((Width-S(96))/2f,y,S(96),S(96)));
  TextRenderer.DrawText(g,"You're here as a guest",Aero.Title,new Rectangle(0,y+S(116),Width,S(36)),Aero.Bright,TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPrefix);
  TextRenderer.DrawText(g,"Join or create a room with the + on the left.\nLog in to add friends and send direct messages.",Aero.Body,new Rectangle(S(20),y+S(160),Width-S(40),S(60)),Aero.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);}
}

// Steam-style bar across the top: the orb and name, big capitals navigation (HOME, ROOMS, FRIENDS, your name), and Join a room on the right.
sealed class TopBar:Control {
 List<(string Key,string Text)> tabs=[];string active="";int hover=-1;
 public event Action<string,Point>? Clicked;
 public readonly FlatButton Join=new(){Text="+  JOIN A ROOM",Kind=ButtonKind.Success};
 int S(int px)=>LogicalToDeviceUnits(px);
 public TopBar(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Aero.Top;
  Join.Surface=Aero.Top;Join.Size=new Size(Join.GetPreferredSize(Size.Empty).Width,S(34));Controls.Add(Join);Height=S(58);}
 public void Set(List<(string Key,string Text)> list,string current){tabs=list;active=current;Invalidate();}
 Rectangle TabRect(int i){int x=S(170);for(int k=0;k<i;k++)x+=TextRenderer.MeasureText(tabs[k].Text,Aero.Nav,Size.Empty,TextFormatFlags.NoPrefix).Width+S(26);
  return new(x,0,TextRenderer.MeasureText(tabs[i].Text,Aero.Nav,Size.Empty,TextFormatFlags.NoPrefix).Width+S(6),Height);}
 protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);Join.Location=new Point(Width-Join.Width-S(16),(Height-Join.Height)/2);}
 int Hit(Point p){for(int i=0;i<tabs.Count;i++)if(TabRect(i).Contains(p))return i;return -1;}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int h=Hit(e.Location);if(h!=hover){hover=h;Invalidate();Cursor=h>=0?Cursors.Hand:Cursors.Default;}}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=-1;Invalidate();}
 protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);int h=Hit(e.Location);if(h>=0){var r=TabRect(h);Clicked?.Invoke(tabs[h].Key,PointToScreen(new Point(r.X,r.Bottom)));}}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
  Aero.Orb(g,new RectangleF(S(18),(Height-S(32))/2f,S(32),S(32)));
  TextRenderer.DrawText(g,"3DFx",Aero.Title,new Rectangle(S(58),0,S(100),Height),Aero.Bright,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
  for(int i=0;i<tabs.Count;i++){var r=TabRect(i);bool on=tabs[i].Key==active,hot=i==hover;
   TextRenderer.DrawText(g,tabs[i].Text,Aero.Nav,r,on?Aero.Link:hot?Aero.Bright:Aero.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
   if(on)using(var b=new SolidBrush(Aero.Accent))g.FillRectangle(b,r.X+S(3),Height-S(14),r.Width-S(6),S(3));}
  using(var p=new Pen(Aero.C(0x000000,90)))g.DrawLine(p,0,Height-1,Width,Height-1);}
}

// Ctrl+F: find text in the open conversation. Enter jumps to the next older match, Shift+Enter to a newer one, Esc closes.
sealed class SearchBar:Control {
 public readonly InputBox Input=new(placeholder:"Search this conversation",maxLength:100);
 readonly FlatButton older=new(){Kind=ButtonKind.Icon,Glyph="\uE70E",GlyphSize=11f},newer=new(){Kind=ButtonKind.Icon,Glyph=Aero.IconDown,GlyphSize=11f},close=new(){Kind=ButtonKind.Icon,Glyph=Aero.IconClose,GlyphSize=10f};
 string status="";
 public event Action<string>? Changed;public event Action<int>? Step;public event Action? Closed;
 int S(int px)=>LogicalToDeviceUnits(px);
 public SearchBar(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Aero.Side;Size=new Size(S(400),S(48));
  foreach(var b in new[]{older,newer,close}){b.Surface=Aero.Side;Controls.Add(b);}Controls.Add(Input);var tip=DarkTip.Create();tip.SetToolTip(older,"Older match (Enter)");tip.SetToolTip(newer,"Newer match (Shift+Enter)");tip.SetToolTip(close,"Close (Esc)");
  Input.Box.TextChanged+=(_,_)=>Changed?.Invoke(Input.Box.Text);older.Click+=(_,_)=>Step?.Invoke(1);newer.Click+=(_,_)=>Step?.Invoke(-1);close.Click+=(_,_)=>Closed?.Invoke();
  Input.Box.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Step?.Invoke(e.Shift?-1:1);}else if(e.KeyCode==Keys.Escape){e.SuppressKeyPress=true;Closed?.Invoke();}};}
 public void SetStatus(string text){status=text;Invalidate();}
 protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(Visible){int y=Top;Motion.Run(this,this,260,t=>Top=y-(int)((1-t)*S(10)),Motion.Spring);}}
 protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);if(close==null)return;int b=S(30),y=(Height-b)/2,x=Width-S(8)-b;
  close.SetBounds(x,y,b,b);x-=b;newer.SetBounds(x,y,b,b);x-=b;older.SetBounds(x,y,b,b);Input.SetBounds(S(8),S(6),Math.Max(0,x-S(84)),Height-S(12));}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(Parent?.BackColor??Aero.Bg);g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var path=Aero.Round(new RectangleF(0,0,Width,Height),S(6))){using(var b=new SolidBrush(Aero.Side))g.FillPath(b,path);using var p=new Pen(Aero.Steel);g.DrawPath(p,path);}
  TextRenderer.DrawText(g,status,Aero.Small,new Rectangle(Input.Right+S(6),0,S(76),Height),status=="No results"?Aero.Red:Aero.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);}
}
