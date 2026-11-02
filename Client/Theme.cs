using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
namespace FriendsChat;

// 3DFx's look: the layout of modern chat apps (rail, sidebar, chat, members) in Steam-style steel blues,
// with Steam's blue and green gradient buttons, square avatars framed in their status colour, and capitals navigation.
// Colours, fonts, Windows icon glyphs and a few drawing helpers. ("Aero" is the old name, kept so drawing code stays put.)
static class Aero {
 public static Color C(int rgb,int alpha=255)=>Color.FromArgb(alpha,(rgb>>16)&255,(rgb>>8)&255,rgb&255);
 // Surfaces, darkest to lightest.
 public static readonly Color Top=C(0x171A21),Rail=C(0x131820),Panel=C(0x10151C),Side=C(0x1B2838),Bg=C(0x1F2B3A),Input=C(0x2A3B4E),Field=C(0x101822),
  Hover=C(0x243447),SideHover=C(0x2A3F55),Selected=C(0x2F4B68),Line=C(0x2C3E52),Steel=C(0x2A475E);
 // Text, brightest to faintest.
 public static readonly Color Bright=C(0xFFFFFF),Text=C(0xC7D5E0),Muted=C(0x8F98A0),Faint=C(0x5D6C7B);
 // Signals. Accent is Steam blue; Green is the "Play" green. Online is Steam's light blue, offline grey.
 public static readonly Color Accent=C(0x1A9FFF),AccentHover=C(0x47B4FF),AccentDown=C(0x0E7FD6),Green=C(0x5BA32B),Yellow=C(0xE9B44C),Red=C(0xD94848),RedHover=C(0xC03A3A),Link=C(0x66C0F4),
  Online=C(0x57CBDE),OnlineName=C(0x6DCFF6),Offline=C(0x898989);
 // Older names still used by some drawing code.
 public static readonly Color Heading=Bright,Ink=Text,Problem=Red,Focus=Accent;
 public static readonly Font Body=new("Segoe UI",10f),Bold=new("Segoe UI Semibold",10f),Small=new("Segoe UI",8.5f),SmallBold=new("Segoe UI Semibold",8.5f),
  Italic=new("Segoe UI",9.5f,FontStyle.Italic),Title=new("Segoe UI Semibold",16f),Subtitle=new("Segoe UI Semibold",12f),Header=new("Segoe UI Semibold",11f),
  Caps=new("Segoe UI Semibold",7.75f),Big=new("Segoe UI Semibold",22f),Nav=new("Segoe UI Semibold",12.5f);
 // Message text; its size follows Settings > Appearance.
 public static Font Chat=new("Segoe UI",10.5f),ChatBold=new("Segoe UI Semibold",10.5f);
 public static void ScaleChat(float scale){Chat=new Font("Segoe UI",10.5f*scale);ChatBold=new Font("Segoe UI Semibold",10.5f*scale);}
 // Windows' own icon font: Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10 (same code points).
 public static readonly string IconFamily=FontFamily.Families.Any(f=>f.Name=="Segoe Fluent Icons")?"Segoe Fluent Icons":"Segoe MDL2 Assets";
 static readonly Dictionary<float,Font> iconFonts=[];
 public static Font Icons(float size){if(!iconFonts.TryGetValue(size,out var f))iconFonts[size]=f=new Font(IconFamily,size);return f;}
 public const string IconAdd="\uE710",IconSettings="\uE713",IconEmoji="\uE76E",IconAttach="\uE723",IconSend="\uE724",IconPeople="\uE716",IconChat="\uE8BD",
  IconClose="\uE711",IconCheck="\uE73E",IconMore="\uE712",IconSearch="\uE721",IconBolt="\uE945",IconLeave="\uE7E8",IconBlock="\uE8F8",IconPerson="\uE77B",
  IconPhoto="\uEB9F",IconVideo="\uE714",IconCopy="\uE8C8",IconRetry="\uE72C",IconDown="\uE70D",IconHash="\uE8E9",IconLink="\uE71B",IconRobot="\uE99A",
  IconReply="\uE97A",IconEdit="\uE70F",IconDelete="\uE74D",IconLike="\uE8E1",IconDislike="\uE8E0";
 // Name colours, picked per person; bright enough for the dark background.
 static readonly Color[] nameColors=[C(0x6DCFF6),C(0xA4D007),C(0xE88AE0),C(0xFF9A8A),C(0xFFB347),C(0x7FE0C8),C(0xFFE066),C(0xA9B8FF)];
 public static Color NameColor(string who){int h=0;foreach(var ch in who)h=h*31+ch;return nameColors[(h&int.MaxValue)%nameColors.Length];}
 public static GraphicsPath Round(RectangleF r,float radius){var p=new GraphicsPath();float d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
 // Vista gloss: the upper and lower halves are separate gradients with a hard step where they meet.
 public static void Gloss(Graphics g,GraphicsPath shape,RectangleF r,Color top1,Color top2,Color bottom1,Color bottom2){
  var state=g.Save();g.SetClip(shape,CombineMode.Intersect);float half=r.Height/2;
  using(var b=new LinearGradientBrush(new RectangleF(r.X,r.Y-1,r.Width,half+2),top1,top2,90f))g.FillRectangle(b,r.X,r.Y,r.Width,half+1);
  using(var b=new LinearGradientBrush(new RectangleF(r.X,r.Y+half-1,r.Width,half+2),bottom1,bottom2,90f))g.FillRectangle(b,r.X,r.Y+half,r.Width,half+1);
  g.Restore(state);}
 // Blue glass orb with "3D", used for the header and the app icon.
 public static void Orb(Graphics g,RectangleF r){
  g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAlias;
  using var body=new GraphicsPath();body.AddEllipse(r);
  using(var b=new PathGradientBrush(body){CenterPoint=new PointF(r.X+r.Width/2,r.Y+r.Height*0.75f),CenterColor=C(0x7CD8FF),SurroundColors=[C(0x0A3478)]})g.FillPath(b,body);
  if(r.Width>=24){using var f=new Font("Segoe UI",r.Width*0.32f,FontStyle.Bold,GraphicsUnit.Pixel);using var sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};var t=new RectangleF(r.X,r.Y+r.Height*0.05f,r.Width,r.Height);using var shadow=new SolidBrush(C(0x001433,140));g.DrawString("3D",f,shadow,new RectangleF(t.X+r.Width/40+0.5f,t.Y+r.Width/40+0.5f,t.Width,t.Height),sf);g.DrawString("3D",f,Brushes.White,t,sf);}
  var cap=new RectangleF(r.X+r.Width*0.15f,r.Y+r.Height*0.05f,r.Width*0.7f,r.Height*0.48f);
  using(var capPath=new GraphicsPath()){capPath.AddEllipse(cap);using var b=new LinearGradientBrush(new RectangleF(cap.X,cap.Y-1,cap.Width,cap.Height+2),C(0xFFFFFF,200),C(0xFFFFFF,12),90f);g.FillPath(b,capPath);}
  using(var pen=new Pen(C(0x081E46,170),Math.Max(1f,r.Width/36)))g.DrawEllipse(pen,r);}
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
 [StructLayout(LayoutKind.Sequential)] struct FlashInfo{public uint cbSize;public IntPtr hwnd;public uint dwFlags,uCount,dwTimeout;}
 [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FlashInfo info);
 // Flashes a window's taskbar button until it is brought forward; nothing happens if it is already the active window.
 public static void Flash(Form f){if(Form.ActiveForm==f||!f.IsHandleCreated||f.IsDisposed)return;var info=new FlashInfo{cbSize=(uint)Marshal.SizeOf<FlashInfo>(),hwnd=f.Handle,dwFlags=15};FlashWindowEx(ref info);}
 // Dark title bar (Windows 10 2004+ and 11; older versions ignore it), in the rail colour on Windows 11.
 public static void DarkTitleBar(IntPtr hwnd){int on=1,caption=ColorTranslator.ToWin32(Top),text=ColorTranslator.ToWin32(Text);
  DwmSetWindowAttribute(hwnd,20,ref on,4);DwmSetWindowAttribute(hwnd,35,ref caption,4);DwmSetWindowAttribute(hwnd,36,ref text,4);}
 public static void TintTitleBar(IntPtr hwnd)=>DarkTitleBar(hwnd);
 [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd,string? app,string? id);
 // Windows' dark scrollbars for a scrolling panel.
 public static void DarkScrollbars(Control c){void Apply()=>SetWindowTheme(c.Handle,"DarkMode_Explorer",null);if(c.IsHandleCreated)Apply();else c.HandleCreated+=(_,_)=>Apply();}
 // Steam's top-lit panel: a lighter steel blue fading into the panel colour.
 public static void Sheen(Graphics g,Rectangle r,Color bottom,int height){if(r.Width<=0||height<=0)return;var top=new Rectangle(r.X,r.Y,r.Width,Math.Min(height,r.Height));
  using(var b=new LinearGradientBrush(new Rectangle(top.X,top.Y-1,top.Width,top.Height+2),Steel,bottom,90f))g.FillRectangle(b,top);}
 // A pill with text, e.g. an unread count.
 public static int PillWidth(string text,Font font,Control scale){int h=scale.LogicalToDeviceUnits(16);return Math.Max(h,TextRenderer.MeasureText(text,font).Width+scale.LogicalToDeviceUnits(4));}
 public static int Pill(Graphics g,string text,Font font,int x,int centerY,Color back,Color fore,Control scale){
  int h=scale.LogicalToDeviceUnits(16),w=PillWidth(text,font,scale);var r=new RectangleF(x,centerY-h/2f,w,h);
  g.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Round(r,h/2f))using(var b=new SolidBrush(back))g.FillPath(b,path);
  TextRenderer.DrawText(g,text,font,Rectangle.Round(r),fore,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding);return w;}
}

// Pop-up with smileys (Picked inserts one into the message) and the sticker pack (StickerPicked sends one).
sealed class EmojiPicker:ToolStripDropDown {
 public static readonly string[] Faces=["😊","😄","😉","😛","😮","😢","😠","😎","😍","😂","🤔","😴","👍","👋","❤️","🎉","🔥","⭐"];
 public event Action<string>? Picked,StickerPicked;
 readonly TableLayoutPanel stickers;readonly Label stickerNote;
 // Off until the relay says it supports stickers.
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool StickersEnabled{get=>stickers.Enabled;set{stickers.Enabled=value;stickerNote.Visible=!value;foreach(Control c in stickers.Controls)c.Invalidate();}}
 // withStickers off: smileys only (picking a reaction).
 public EmojiPicker(bool withStickers=true){
  BackColor=Aero.Panel;Renderer=new ToolStripProfessionalRenderer(new DarkColors());
  var page=new TableLayoutPanel{ColumnCount=1,AutoSize=true,BackColor=Aero.Panel,Padding=new Padding(6)};
  int S(int px)=>page.LogicalToDeviceUnits(px);
  Label Heading(string text)=>new(){Text=text.ToUpperInvariant(),AutoSize=true,ForeColor=Aero.Muted,Font=Aero.Caps,Margin=new Padding(S(4),S(6),0,S(4))};
  var faces=new TableLayoutPanel{ColumnCount=6,AutoSize=true,BackColor=Aero.Panel,Margin=Padding.Empty};
  var font=new Font("Segoe UI Emoji",15f);
  foreach(var face in Faces){
   var cell=new Label{Text=face,Font=font,ForeColor=Aero.Text,AutoSize=false,Size=new Size(S(40),S(38)),TextAlign=ContentAlignment.MiddleCenter,Cursor=Cursors.Hand,Margin=new Padding(1),BackColor=Aero.Panel};
   cell.MouseEnter+=(_,_)=>cell.BackColor=Aero.Selected;cell.MouseLeave+=(_,_)=>cell.BackColor=Aero.Panel;
   cell.Click+=(_,_)=>{Picked?.Invoke(face);Close();};faces.Controls.Add(cell);}
  stickers=new TableLayoutPanel{ColumnCount=4,AutoSize=true,BackColor=Aero.Panel,Margin=Padding.Empty};
  foreach(var id in FriendsChat.Stickers.Ids){
   var art=StickerArt.Draw(id,S(54));
   var cell=new PictureBox{Image=art,SizeMode=PictureBoxSizeMode.CenterImage,Size=new Size(S(60),S(60)),Cursor=Cursors.Hand,Margin=new Padding(1),BackColor=Aero.Panel};
   cell.MouseEnter+=(_,_)=>cell.BackColor=Aero.Selected;cell.MouseLeave+=(_,_)=>cell.BackColor=Aero.Panel;
   cell.Paint+=(_,e)=>{if(!stickers.Enabled)using(var veil=new SolidBrush(Aero.C(0x232428,180)))e.Graphics.FillRectangle(veil,cell.ClientRectangle);};
   cell.Click+=(_,_)=>{StickerPicked?.Invoke(id);Close();};stickers.Controls.Add(cell);}
  stickerNote=new Label{Text="This relay doesn't support stickers yet.",AutoSize=true,ForeColor=Aero.Faint,Font=Aero.Small,Margin=new Padding(S(4),0,0,S(4))};
  page.Controls.Add(Heading(withStickers?"Smileys":"React"));page.Controls.Add(faces);if(withStickers){page.Controls.Add(Heading("Stickers"));page.Controls.Add(stickerNote);page.Controls.Add(stickers);}
  StickersEnabled=false;Motion.FadeIn(this,150);
  Padding=Padding.Empty;Items.Add(new ToolStripControlHost(page){Margin=Padding.Empty,Padding=Padding.Empty,AutoSize=true});}
}

// Dark menus (right-click menus, the + menu): colours for the standard menu renderer.
sealed class DarkColors:ProfessionalColorTable {
 public override Color ToolStripDropDownBackground=>Aero.Rail;public override Color ImageMarginGradientBegin=>Aero.Rail;public override Color ImageMarginGradientMiddle=>Aero.Rail;public override Color ImageMarginGradientEnd=>Aero.Rail;
 public override Color MenuItemSelected=>Aero.Accent;public override Color MenuItemSelectedGradientBegin=>Aero.Accent;public override Color MenuItemSelectedGradientEnd=>Aero.Accent;
 public override Color MenuItemBorder=>Aero.Accent;public override Color MenuBorder=>Aero.Panel;public override Color SeparatorDark=>Aero.Line;public override Color SeparatorLight=>Aero.Rail;public override Color CheckBackground=>Aero.Accent;public override Color CheckSelectedBackground=>Aero.AccentHover;
}
static class DarkMenu {
 public static ContextMenuStrip Create(){var m=new ContextMenuStrip{Renderer=new ToolStripProfessionalRenderer(new DarkColors()),BackColor=Aero.Rail,ForeColor=Aero.Text,ShowImageMargin=false,Font=Aero.Body,Padding=new Padding(4)};
  m.ItemAdded+=(_,e)=>{if(e.Item is not {} i)return;i.ForeColor=i.Tag as string=="danger"?Aero.Red:Aero.Text;i.Padding=new Padding(4,5,4,5);};Motion.FadeIn(m);return m;}
 public static ToolStripItem Item(this ContextMenuStrip m,string text,Action click,bool danger=false){var i=new ToolStripMenuItem(text){Tag=danger?"danger":null};i.Click+=(_,_)=>click();m.Items.Add(i);return i;}
}

// Unread count on the taskbar button: a small red badge drawn over the app icon (Windows 7+ taskbar overlay).
static class Taskbar {
 [ComImport,Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface ITaskbarList3{
  void HrInit();void AddTab(IntPtr hwnd);void DeleteTab(IntPtr hwnd);void ActivateTab(IntPtr hwnd);void SetActiveAlt(IntPtr hwnd);
  void MarkFullscreenWindow(IntPtr hwnd,[MarshalAs(UnmanagedType.Bool)]bool fullscreen);
  void SetProgressValue(IntPtr hwnd,ulong completed,ulong total);void SetProgressState(IntPtr hwnd,int flags);
  void RegisterTab(IntPtr tab,IntPtr main);void UnregisterTab(IntPtr tab);void SetTabOrder(IntPtr tab,IntPtr insertBefore);void SetTabActive(IntPtr tab,IntPtr main,uint reserved);
  void ThumbBarAddButtons(IntPtr hwnd,uint count,IntPtr buttons);void ThumbBarUpdateButtons(IntPtr hwnd,uint count,IntPtr buttons);void ThumbBarSetImageList(IntPtr hwnd,IntPtr imageList);
  void SetOverlayIcon(IntPtr hwnd,IntPtr icon,[MarshalAs(UnmanagedType.LPWStr)]string? description);}
 [ComImport,Guid("56FDF344-FD6D-11d0-958A-006097C9A090"),ClassInterface(ClassInterfaceType.None)] class TaskbarList{}
 [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
 static ITaskbarList3? list;static IntPtr current;
 public static void SetBadge(IntPtr hwnd,int count){
  try{
   if(list==null){list=(ITaskbarList3)new TaskbarList();list.HrInit();}
   var icon=IntPtr.Zero;
   if(count>0){int size=SystemInformation.SmallIconSize.Width;using var bmp=new Bitmap(size,size);using(var g=Graphics.FromImage(bmp)){
     g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;var r=new RectangleF(0,0,size-1,size-1);
     using(var b=new System.Drawing.Drawing2D.LinearGradientBrush(r,Aero.C(0xFF6A5A),Aero.C(0xC0141E),90f))g.FillEllipse(b,r);using(var p=new Pen(Color.White))g.DrawEllipse(p,r);
     using var f=new Font("Segoe UI",size*(count>9?0.42f:0.58f),FontStyle.Bold,GraphicsUnit.Pixel);using var sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
     g.DrawString(count>9?"9+":count.ToString(),f,Brushes.White,new RectangleF(0,0.5f,size,size),sf);}
    icon=bmp.GetHicon();}
   list.SetOverlayIcon(hwnd,icon,count>0?$"{count} unread":null);
   if(current!=IntPtr.Zero)DestroyIcon(current);current=icon;}
  catch(Exception e) when(e is COMException or InvalidCastException){}// No taskbar (e.g. some remote sessions): the title-bar count still shows.
 }
}
