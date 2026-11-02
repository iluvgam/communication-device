using System.ComponentModel;
using System.Drawing.Drawing2D;
namespace FriendsChat;

enum ButtonKind{Primary,Secondary,Danger,Success,Ghost,Link,Icon}
// Flat rounded button. Icon buttons show a Windows icon glyph (Glyph) and only change colour on hover.
sealed class FlatButton:Button {
 ButtonKind kind=ButtonKind.Primary;bool hover,down,active;readonly Glide hot,press;
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public ButtonKind Kind{get=>kind;set{kind=value;Invalidate();}}
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public string Glyph{get;set;}="";
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public float GlyphSize{get;set;}=13f;
 // Background drawn behind the rounded shape (defaults to the parent's colour).
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Color? Surface{get;set;}
 // Icon buttons that switch something on (e.g. the member list) stay bright while on.
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Active{get=>active;set{active=value;Invalidate();}}
 int S(int px)=>LogicalToDeviceUnits(px);
 public FlatButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Font=Aero.Bold;Cursor=Cursors.Hand;Size=new Size(S(96),S(38));hot=new(this);press=new(this);}
 public override Size GetPreferredSize(Size proposed){if(Kind==ButtonKind.Icon)return new Size(S(32),S(32));var t=TextRenderer.MeasureText(Text,Font);return new Size(Math.Max(S(Kind is ButtonKind.Ghost or ButtonKind.Link?40:96),t.Width+S(32)),S(38));}
 protected override void OnMouseEnter(EventArgs e){hover=true;hot.To(1,140);base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){hover=down=false;hot.To(0,260);press.To(0,200);base.OnMouseLeave(e);}
 protected override void OnMouseDown(MouseEventArgs e){down=e.Button==MouseButtons.Left;if(down)press.To(1,90);base.OnMouseDown(e);}
 protected override void OnMouseUp(MouseEventArgs e){down=false;press.To(0,320,Motion.Spring);base.OnMouseUp(e);}
 // Resting, hover and pressed colours; the paint blends between them as the mouse comes and goes.
 (Color Normal,Color Hover,Color Down) Shades()=>Kind switch{
  ButtonKind.Primary=>(Aero.Accent,Aero.AccentHover,Aero.AccentDown),
  ButtonKind.Secondary=>(Aero.C(0x3D5A75),Aero.C(0x4A7092),Aero.C(0x324B62)),
  ButtonKind.Danger=>(Aero.Red,Aero.RedHover,Aero.C(0xA12D2F)),
  ButtonKind.Success=>(Aero.Green,Aero.C(0x6EBE34),Aero.C(0x4C8A24)),
  _=>(Color.Empty,Color.Empty,Color.Empty)};
 (Color Back,Color Fore) Colors(){var (n,h,d)=Shades();var back=n==Color.Empty?n:Motion.Mix(Motion.Mix(n,h,hot.Value),d,press.Value);
  return (back,Kind switch{ButtonKind.Ghost=>Aero.Bright,ButtonKind.Link=>Aero.Link,ButtonKind.Icon=>Motion.Mix(Aero.Muted,Aero.Bright,active?1:hot.Value),_=>Color.White});}
 static Color Lift(Color c,float f)=>Color.FromArgb(c.R+(int)((255-c.R)*f),c.G+(int)((255-c.G)*f),c.B+(int)((255-c.B)*f));
 static Color Mix(Color a,Color b)=>Color.FromArgb((a.R+b.R)/2,(a.G+b.G)/2,(a.B+b.B)/2);
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;var under=Surface??Parent?.BackColor??Aero.Bg;g.Clear(under);g.SmoothingMode=SmoothingMode.AntiAlias;
  var (back,fore)=Colors();if(!Enabled){if(back!=Color.Empty)back=Mix(back,under);fore=Mix(fore,under);}
  // Steam-style gradient: lighter at the top (blue buttons run sky blue to deep blue, green ones like "Play").
  float inset=press.Value*S(1)*1.5f;
  if(back!=Color.Empty)using(var path=Aero.Round(new RectangleF(inset,inset,Width-inset*2,Height-inset*2),S(3))){var top=Kind switch{ButtonKind.Primary=>Lift(back,0.35f),ButtonKind.Success=>Lift(back,0.3f),_=>Lift(back,0.12f)};
   var bottom=Kind==ButtonKind.Primary?Color.FromArgb(back.R*6/10,back.G*6/10,back.B*9/10):Kind==ButtonKind.Success?Color.FromArgb(back.R*7/10,back.G*7/10,back.B*6/10):back;
   using var b=new LinearGradientBrush(new RectangleF(0,-1,Width,Height+2),top,bottom,90f);g.FillPath(b,path);}
  const TextFormatFlags centre=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis;
  if(Glyph.Length>0&&Text.Length==0){TextRenderer.DrawText(g,Glyph,Aero.Icons(GlyphSize),ClientRectangle,fore,centre);return;}
  if(Glyph.Length>0){var icon=Aero.Icons(GlyphSize);int iw=TextRenderer.MeasureText(Glyph,icon).Width,tw=TextRenderer.MeasureText(Text,Font).Width,x=(Width-iw-tw)/2;
   TextRenderer.DrawText(g,Glyph,icon,new Rectangle(x,0,iw,Height),fore,centre);TextRenderer.DrawText(g,Text,Font,new Rectangle(x+iw,0,tw,Height),fore,centre);return;}
  var font=hot.Value>0.5f&&Kind is ButtonKind.Ghost or ButtonKind.Link?Underlined(Font):Font;TextRenderer.DrawText(g,Text,font,ClientRectangle,fore,centre);}
 static readonly Dictionary<Font,Font> underlined=[];
 static Font Underlined(Font f){if(!underlined.TryGetValue(f,out var u))underlined[f]=u=new Font(f,f.Style|FontStyle.Underline);return u;}
 protected override void OnPaintBackground(PaintEventArgs e){}
}

// Dark text field (no border; red when it has a problem). Box is the real TextBox inside.
sealed class InputBox:Control {
 public readonly TextBox Box;bool problem;
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Problem{get=>problem;set{if(problem==value)return;problem=value;Invalidate();}}
 int S(int px)=>LogicalToDeviceUnits(px);
 public InputBox(bool secret=false,string placeholder="",int maxLength=128,Color? back=null){
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  BackColor=back??Aero.Field;Box=new TextBox{BorderStyle=BorderStyle.None,BackColor=BackColor,ForeColor=Aero.Text,Font=Aero.Body,UseSystemPasswordChar=secret,PlaceholderText=placeholder,MaxLength=maxLength};
  Controls.Add(Box);Height=S(40);Cursor=Cursors.IBeam;Box.TextChanged+=(_,_)=>Problem=false;Box.GotFocus+=(_,_)=>Invalidate();Box.LostFocus+=(_,_)=>Invalidate();}
 protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);Box.SetBounds(S(10),(Height-Box.Height)/2,Math.Max(0,Width-S(20)),Box.Height);}
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Box.Focus();}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(Parent?.BackColor??Aero.Bg);g.SmoothingMode=SmoothingMode.AntiAlias;
  using var path=Aero.Round(new RectangleF(0.5f,0.5f,Width-1f,Height-1f),S(3));using(var b=new SolidBrush(BackColor))g.FillPath(b,path);
  if(!problem&&Box.Focused)using(var p=new Pen(Aero.Accent,S(1)))g.DrawPath(p,path);
  if(problem)using(var p=new Pen(Aero.Red,S(1)))g.DrawPath(p,path);}
}

// On/off switch, as in settings pages.
sealed class Toggle:CheckBox {
 int S(int px)=>LogicalToDeviceUnits(px);
 readonly Glide knob;
 public Toggle(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);AutoSize=false;Size=new Size(S(40),S(24));Cursor=Cursors.Hand;Text="";knob=new(this);}
 protected override void OnCheckedChanged(EventArgs e){base.OnCheckedChanged(e);if(IsHandleCreated&&Visible)knob.To(Checked?1:0,300,Motion.Spring);else knob.Set(Checked?1:0);}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(Parent?.BackColor??Aero.Bg);g.SmoothingMode=SmoothingMode.AntiAlias;
  var track=new RectangleF(0,0,Width,Height);float k=Math.Clamp(knob.Value,0,1.08f);using(var path=Aero.Round(track,Height/2f))using(var b=new SolidBrush(Motion.Mix(Aero.C(0x3D4450),Aero.Accent,Math.Min(1,k))))g.FillPath(b,path);
  float d=Height-S(6);var dot=new RectangleF(S(3)+(Width-d-S(6))*k,S(3),d,d);g.FillEllipse(Brushes.White,dot);
  if(Focused&&ShowFocusCues)using(var p=new Pen(Aero.Link,S(2))){using var ring=Aero.Round(new RectangleF(1,1,Width-2,Height-2),(Height-2)/2f);g.DrawPath(p,ring);}}
}

// A short message at the top of the chat area that fades away after a few seconds (replaces the old status bar).
sealed class Toast:Control {
 readonly System.Windows.Forms.Timer hide=new(){Interval=5000};bool problem;string? who;Image? picture;
 // Space kept clear on the right (e.g. the member list).
 public int RightInset;
 int S(int px)=>LogicalToDeviceUnits(px);
 public Toast(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Visible=false;Cursor=Cursors.Hand;Font=Aero.Body;
  hide.Tick+=(_,_)=>{hide.Stop();Away();};Click+=(_,_)=>{hide.Stop();Away();};}
 int home;
 void Away(){if(Parent==null||!Visible)return;int from=Left,to=Parent.Width;Motion.Run(this,this,240,t=>Left=(int)Motion.Lerp(from,to,t),Motion.InOut,()=>Visible=false);}
 // Like Steam's pop-ups: bottom right, above the message box. With a name it shows that person's picture.
 public void Show(string text,bool isProblem,string? name=null,Image? face=null){if(Parent==null)return;Text=text;problem=isProblem;who=name;picture=face;
  int left=who!=null?S(66):S(24),width=Math.Min(S(340),Math.Max(S(200),Parent.Width-S(40)));var size=TextRenderer.MeasureText(text,Font,new Size(width-left-S(12),0),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
  Size=new Size(width,Math.Max(who!=null?S(64):0,size.Height+S(24)));home=Math.Max(S(8),Parent.Width-RightInset-Width-S(20));int start=Visible?Left:Parent.Width;Location=new Point(start,Parent.Height-Height-S(96));Visible=true;BringToFront();
  Motion.Run(this,this,480,t=>Left=(int)Motion.Lerp(start,home,t),Motion.Spring);hide.Stop();hide.Interval=isProblem?7000:5000;hide.Start();Invalidate();}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(Parent?.BackColor??Aero.Bg);g.SmoothingMode=SmoothingMode.AntiAlias;
  var r=new RectangleF(0,0,Width,Height);using(var path=Aero.Round(r,S(4)))using(var b=new LinearGradientBrush(new RectangleF(0,-1,Width,Height+2),Aero.Steel,Aero.Top,90f))g.FillPath(b,path);
  using(var b=new SolidBrush(problem?Aero.Red:who!=null?Aero.Online:Aero.Accent))g.FillRectangle(b,0,S(4),S(3),Height-S(8));
  int left=S(24);if(who!=null){Avatars.DrawWithPresence(g,new RectangleF(S(16),(Height-S(38))/2f,S(38),S(38)),who,picture,"online",Aero.Top);left=S(66);}
  TextRenderer.DrawText(g,Text,Font,new Rectangle(left,S(12),Width-left-S(12),Height-S(20)),Aero.Bright,TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter);}
}

// Dark dialog: centred heading and text, fields with capitals captions, buttons on a darker band at the bottom.
// Field problems show in that field's caption ("USERNAME — 3–20 letters…") with a red edge; other problems show above the buttons.
class Modal:Form {
 protected readonly TableLayoutPanel Body;readonly FlowLayoutPanel band;readonly Label error;
 readonly Dictionary<TextBox,(Label Caption,string Text,InputBox Frame)> fields=[];
 protected int S(int px)=>LogicalToDeviceUnits(px);
 protected int Width400=>S(408);
 public Modal(string title,string heading,string? text=null){
  Text=title;Font=Aero.Body;BackColor=Aero.Bg;ForeColor=Aero.Text;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;KeyPreview=true;
  try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch(ArgumentException){}Motion.FadeIn(this,180);
  var root=new TableLayoutPanel{ColumnCount=1,AutoSize=true,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Aero.Bg};
  Body=new TableLayoutPanel{ColumnCount=1,AutoSize=true,Padding=new Padding(S(16),S(20),S(16),S(16)),Margin=Padding.Empty,BackColor=Aero.Bg};
  Label Centred(string t,Font f,Color c,int below)=>new(){Text=t,AutoSize=true,Font=f,ForeColor=c,MaximumSize=new Size(Width400,0),MinimumSize=new Size(Width400,0),TextAlign=ContentAlignment.TopCenter,Margin=new Padding(0,0,0,below)};
  Body.Controls.Add(Centred(heading,Aero.Title,Aero.Bright,S(8)));
  if(text!=null)Body.Controls.Add(Centred(text,Aero.Body,Aero.Muted,S(10)));
  error=new Label{AutoSize=true,ForeColor=Aero.Red,Font=Aero.Small,MaximumSize=new Size(Width400,0),Visible=false,Margin=new Padding(0,S(10),0,0)};
  band=new FlowLayoutPanel{FlowDirection=FlowDirection.RightToLeft,AutoSize=true,Dock=DockStyle.Fill,BackColor=Aero.Side,Padding=new Padding(S(16)),Margin=Padding.Empty,MinimumSize=new Size(Width400+S(32),0)};
  root.Controls.Add(Body);root.Controls.Add(band);Controls.Add(root);}
 protected override void OnLoad(EventArgs e){Body.Controls.Add(error);base.OnLoad(e);}
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Aero.DarkTitleBar(Handle);}
 // Buttons are added right to left: Cancel first, then the main action.
 protected FlatButton AddButton(string text,bool accent=false,bool danger=false){var b=new FlatButton{Text=text,Kind=danger?ButtonKind.Danger:accent?ButtonKind.Primary:ButtonKind.Secondary,Margin=new Padding(S(8),0,0,0)};b.Size=b.GetPreferredSize(Size.Empty);band.Controls.Add(b);return b;}
 protected FlatButton AddCancel(string text="Cancel"){var b=new FlatButton{Text=text,Kind=ButtonKind.Ghost,Margin=new Padding(S(8),0,0,0),DialogResult=DialogResult.Cancel};b.Size=b.GetPreferredSize(Size.Empty);band.Controls.Add(b);CancelButton=b;return b;}
 protected Label Caption(string text)=>new(){Text=text.ToUpperInvariant(),AutoSize=true,ForeColor=Aero.Muted,Font=Aero.Caps,Margin=new Padding(0,S(10),0,S(6))};
 protected TextBox AddField(string caption,bool secret=false,string? placeholder=null,int maxLength=128){
  var label=Caption(caption);Body.Controls.Add(label);var frame=new InputBox(secret,placeholder??"",maxLength){Width=Width400,Margin=Padding.Empty};Body.Controls.Add(frame);
  fields[frame.Box]=(label,caption,frame);frame.Box.TextChanged+=(_,_)=>{error.Visible=false;Unmark(frame.Box);};return frame.Box;}
 protected Toggle AddSwitch(string text,bool on){var row=new TableLayoutPanel{ColumnCount=2,AutoSize=true,Width=Width400,Margin=new Padding(0,S(12),0,0),BackColor=Aero.Bg};row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Width400-S(48)));row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
  var t=new Toggle{Checked=on,Margin=Padding.Empty};var l=new Label{Text=text,AutoSize=true,ForeColor=Aero.Text,Margin=new Padding(0,S(3),0,0),Cursor=Cursors.Hand};l.Click+=(_,_)=>t.Checked=!t.Checked;
  row.Controls.Add(l,0,0);row.Controls.Add(t,1,0);Body.Controls.Add(row);return t;}
 protected Control AddLink(string text,Action click){var l=new LinkLabel{Text=text,AutoSize=true,LinkColor=Aero.Link,ActiveLinkColor=Aero.Link,LinkBehavior=LinkBehavior.HoverUnderline,Font=Aero.Small,Margin=new Padding(0,S(6),0,0)};l.LinkClicked+=(_,_)=>click();Body.Controls.Add(l);return l;}
 // With a field: only that field is marked, and its caption says what is wrong. Without: the message shows above the buttons.
 protected void ShowError(string? text,TextBox? field=null){
  if(text==null){error.Visible=false;foreach(var box in fields.Keys)Unmark(box);return;}
  if(field!=null&&fields.TryGetValue(field,out var f)){var colon=text.IndexOf(':');var detail=colon>0&&colon<20?text[(colon+1)..].Trim():text;
   f.Caption.Text=$"{f.Text.ToUpperInvariant()} — {detail}";f.Caption.ForeColor=Aero.Red;f.Frame.Problem=true;field.Focus();field.SelectAll();return;}
  error.Text=text;error.Visible=true;}
 void Unmark(TextBox box){if(!fields.TryGetValue(box,out var f))return;f.Caption.Text=f.Text.ToUpperInvariant();f.Caption.ForeColor=Aero.Muted;f.Frame.Problem=false;}
 protected void Busy(bool on){UseWaitCursor=on;Body.Enabled=band.Enabled=!on;}
 // Runs one account request with the busy cursor; relay errors appear above the buttons.
 protected async Task<AccountReply?> Call(Func<AccountClient,Task<AccountReply>> request,string relay){
  Busy(true);ShowError(null);
  try{using var client=AccountClient.ForRelay(relay);return await request(client);}
  catch(IOException e){ShowError(e.Message);return null;}
  finally{if(!IsDisposed)Busy(false);}}
}

// A page that scrolls with the mouse wheel and shows a thin dark scrollbar instead of Windows' light one. Content is placed at Top.
sealed class ScrollPanel:Panel {
 Control? content;int offset,dragFrom=-1,dragOffset;
 int S(int px)=>LogicalToDeviceUnits(px);
 public ScrollPanel(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
 public int Top0=56;
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Control? Content{get=>content;set{content=value;if(value!=null)value.SizeChanged+=(_,_)=>ScrollTo(offset);}}
 int Max=>Math.Max(0,(content?.Height??0)+S(Top0)+S(48)-ClientSize.Height);
 public void ScrollTo(int y){offset=Math.Clamp(y,0,Max);if(content!=null)content.Top=S(Top0)-offset;Invalidate();}
 protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);ScrollTo(offset-e.Delta*S(60)/120);}
 protected override void OnResize(EventArgs e){base.OnResize(e);ScrollTo(offset);}
 Rectangle Track=>new(ClientSize.Width-S(12),S(4),S(8),ClientSize.Height-S(8));
 Rectangle Thumb(){if(Max==0)return Rectangle.Empty;var t=Track;int h=Math.Max(S(32),(int)((long)t.Height*ClientSize.Height/(ClientSize.Height+Max)));return new(t.X,t.Y+(int)((long)(t.Height-h)*offset/Max),t.Width,h);}
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(Thumb().Contains(e.Location)){dragFrom=e.Y;dragOffset=offset;}}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragFrom<0)return;var t=Track;ScrollTo(dragOffset+(int)((long)(e.Y-dragFrom)*Max/Math.Max(1,t.Height-Thumb().Height)));}
 protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragFrom=-1;}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(BackColor);var th=Thumb();if(th.IsEmpty)return;g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var p=Aero.Round(Track,S(4)))using(var b=new SolidBrush(Aero.Side))g.FillPath(b,p);using(var p=Aero.Round(th,S(4)))using(var b=new SolidBrush(Aero.Steel))g.FillPath(b,p);}
}
