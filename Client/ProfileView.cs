using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
namespace FriendsChat;

// Profile pictures: a chosen photo becomes a 96×96 JPEG under 9 KB; people without one get their initial.
static class Avatars {
 public const int Size=96;
 // Centre-cropped square, re-saved (which drops hidden details such as GPS location).
 public static byte[] Prepare(byte[] original){using var src=Pictures.Open(original,out var stream);using(stream){
  int side=Math.Min(src.Width,src.Height);var crop=new Rectangle((src.Width-side)/2,(src.Height-side)/2,side,side);
  using var bmp=new Bitmap(Size,Size,PixelFormat.Format24bppRgb);using(var g=Graphics.FromImage(bmp)){g.Clear(Color.White);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
   using var edges=new ImageAttributes();edges.SetWrapMode(WrapMode.TileFlipXY);g.DrawImage(src,new Rectangle(0,0,Size,Size),crop.X,crop.Y,crop.Width,crop.Height,GraphicsUnit.Pixel,edges);}
  var codec=ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==ImageFormat.Jpeg.Guid);
  for(long quality=90;quality>=30;quality-=15){using var ms=new MemoryStream();using var args=new EncoderParameters(1);args.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,quality);bmp.Save(ms,codec,args);if(ms.Length<=Profiles.MaxAvatar)return ms.ToArray();}}
  throw new IOException("That picture could not be made small enough.");}
 public static Image? Decode(byte[] data){if(!Profiles.ValidAvatar(data))return null;try{using var img=Pictures.Open(data,out var s);using(s)return new Bitmap(img);}catch(Exception e) when(e is ArgumentException or IOException or OutOfMemoryException or ExternalException){return null;}}
 // Square picture with softened corners (Steam style), or a coloured square with the initial.
 public static void Draw(Graphics g,RectangleF r,string name,Image? picture){
  g.SmoothingMode=SmoothingMode.AntiAlias;using var circle=Aero.Round(r,Math.Max(2f,r.Width/10f));
  if(picture!=null){var st=g.Save();g.SetClip(circle);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(picture,r);g.Restore(st);return;}
  using(var b=new SolidBrush(ControlPaint.Dark(Aero.NameColor(name),0.35f)))g.FillPath(b,circle);
  using var f=new Font("Segoe UI Semibold",r.Height*0.42f,FontStyle.Regular,GraphicsUnit.Pixel);using var sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};g.TextRenderingHint=TextRenderingHint.AntiAlias;
  g.DrawString(name.Length>0?char.ToUpperInvariant(name[0]).ToString():"?",f,Brushes.White,new RectangleF(r.X,r.Y+r.Height*0.02f,r.Width,r.Height),sf);}
 // Presence dot: online green, away amber, busy red, offline grey; cut out of the picture by a ring in the background colour.
 public static Color PresenceColor(string presence)=>presence switch{"online"=>Aero.Online,"away"=>Aero.Yellow,"busy"=>Aero.Red,_=>Aero.Offline};
 public static void DrawPresence(Graphics g,RectangleF d,string presence,Color ring){g.SmoothingMode=SmoothingMode.AntiAlias;float w=d.Width*0.22f;
  using(var b=new SolidBrush(ring))g.FillEllipse(b,RectangleF.Inflate(d,w,w));using(var b=new SolidBrush(PresenceColor(presence)))g.FillEllipse(b,d);
  if(presence is "offline" or "invisible" or ""){var hole=RectangleF.Inflate(d,-d.Width*0.25f,-d.Height*0.25f);using var b=new SolidBrush(ring);g.FillEllipse(b,hole);}
  else if(presence=="busy"){using var b=new SolidBrush(ring);g.FillRectangle(b,d.X+d.Width*0.22f,d.Y+d.Height*0.4f,d.Width*0.56f,d.Height*0.2f);}
  else if(presence=="away"){using var b=new SolidBrush(ring);g.FillEllipse(b,d.X-d.Width*0.12f,d.Y-d.Height*0.12f,d.Width*0.62f,d.Height*0.62f);}}
 // Picture inside a frame in its status colour, as in Steam's friends list (no frame when there is no status).
 public static void DrawWithPresence(Graphics g,RectangleF r,string name,Image? picture,string? presence,Color ring){
  if(presence==null){Draw(g,r,name,picture);return;}float w=Math.Max(2f,r.Width/16f);g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var frame=Aero.Round(RectangleF.Inflate(r,w,w),Math.Max(3f,r.Width/8f)))using(var b=new SolidBrush(PresenceColor(presence)))g.FillPath(b,frame);
  using(var gap=Aero.Round(RectangleF.Inflate(r,w/2,w/2),Math.Max(2f,r.Width/9f)))using(var b=new SolidBrush(ring))g.FillPath(b,gap);
  Draw(g,r,name,picture);}
 // Steam's name colour for a friend: light blue online, grey offline.
 public static Color PresenceName(string presence)=>presence is "online" or "away" or "busy"?Aero.OnlineName:Aero.Offline;
}

// Small tag after a name: "AI" for an AI member (AM), so nobody mistakes him for a person, and "GUEST" for people without an account.
static class Badge {
 public static int Draw(Graphics g,string tag,int x,int centerY,Control scale){if(tag.Length==0)return 0;int S(int px)=>scale.LogicalToDeviceUnits(px);
  var size=TextRenderer.MeasureText(tag,Aero.Caps);var r=new RectangleF(x,centerY-S(8),size.Width+S(4),S(16));
  g.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Aero.Round(r,S(3)))using(var b=new SolidBrush(tag=="AI"?Aero.Accent:Aero.C(0x4E5058)))g.FillPath(b,path);
  TextRenderer.DrawText(g,tag,Aero.Caps,Rectangle.Round(r),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding);return (int)r.Width;}
}

// Seconds since the last keyboard or mouse input anywhere in Windows (for automatic Away).
static class Idle {
 [StructLayout(LayoutKind.Sequential)] struct LastInput{public uint cbSize,dwTime;}
 [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput info);
 public static double Seconds{get{var i=new LastInput{cbSize=(uint)Marshal.SizeOf<LastInput>()};return GetLastInputInfo(ref i)?(uint)(Environment.TickCount-(int)i.dwTime)/1000.0:0;}}
}

// "Status line" editor: one short line shown under your name to your friends.
sealed class MoodDialog:Modal {
 public string Mood{get;private set;}="";
 public MoodDialog(string current):base("Status line — 3DFx Vista","Set a custom status","Your friends see this under your name. Leave it empty to clear it."){
  var box=AddField("Status line",maxLength:Profiles.MaxMood,placeholder:"e.g. Playing games 🎮");box.Text=current;AddCancel();var ok=AddButton("Save",accent:true);AcceptButton=ok;
  ok.Click+=(_,_)=>{if(!Profiles.ValidMood(box.Text.Trim())){ShowError($"Up to {Profiles.MaxMood} characters.",box);return;}Mood=box.Text.Trim();DialogResult=DialogResult.OK;};}
}

// Click a name or picture: a small card with the picture, name, status and what you can do (message, add friend, mention, edit profile).
sealed class ProfileCard:ToolStripDropDown {
 public ProfileCard(string name,string tag,Image? picture,string? presence,string line,string mood,List<(string Text,ButtonKind Kind,Action Click)> actions){
  Text="Profile — "+name;BackColor=Aero.Side;Motion.FadeIn(this,160);Padding=Padding.Empty;Renderer=new ToolStripProfessionalRenderer(new DarkColors());
  var page=new Panel{BackColor=Aero.Side,Margin=Padding.Empty};int S(int px)=>page.LogicalToDeviceUnits(px);int W=S(300);
  const TextFormatFlags wrap=TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix;
  int moodH=mood.Length>0?TextRenderer.MeasureText(mood,Aero.Body,new Size(W-S(32),0),wrap).Height:0;
  int top=S(172),buttons=top+(mood.Length>0?S(30)+moodH+S(10):0);page.Size=new Size(W,buttons+(actions.Count>0?S(52):S(6)));
  page.Paint+=(_,e)=>{var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
   using(var b=new LinearGradientBrush(new Rectangle(0,0,W,S(64)),Aero.Steel,Aero.Accent,0f))g.FillRectangle(b,0,0,W,S(64));
   var pic=new RectangleF(S(16),S(20),S(84),S(84));using(var ring=Aero.Round(RectangleF.Inflate(pic,S(6),S(6)),S(12)))using(var b=new SolidBrush(Aero.Side))g.FillPath(b,ring);
   Avatars.DrawWithPresence(g,pic,name,picture,presence,Aero.Side);
   var nameSize=TextRenderer.MeasureText(name,Aero.Title,Size.Empty,TextFormatFlags.NoPrefix);TextRenderer.DrawText(g,name,Aero.Title,new Point(S(14),S(114)),Aero.Bright,TextFormatFlags.NoPrefix);
   if(tag.Length>0)Badge.Draw(g,tag,S(14)+nameSize.Width+S(2),S(114)+nameSize.Height/2,page);
   TextRenderer.DrawText(g,line,Aero.Small,new Rectangle(S(16),S(146),W-S(32),S(20)),presence is null or "offline"?Aero.Muted:Avatars.PresenceName(presence),TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
   if(mood.Length>0){using(var p=new Pen(Aero.Line))g.DrawLine(p,S(16),top,W-S(16),top);
    TextRenderer.DrawText(g,"STATUS",Aero.Caps,new Point(S(16),top+S(10)),Aero.Muted,TextFormatFlags.NoPrefix);
    TextRenderer.DrawText(g,mood,Aero.Body,new Rectangle(S(16),top+S(28),W-S(32),moodH),Aero.Text,wrap);}};
  if(actions.Count>0){int gap=S(8),bw=(W-S(32)-gap*(actions.Count-1))/actions.Count;
   for(int i=0;i<actions.Count;i++){var (text,kind,click)=actions[i];var b=new FlatButton{Text=text,Kind=kind,Surface=Aero.Side,Bounds=new Rectangle(S(16)+i*(bw+gap),buttons+S(6),bw,S(36))};
    b.Click+=(_,_)=>{Close();click();};page.Controls.Add(b);}}
  Items.Add(new ToolStripControlHost(page){Margin=Padding.Empty,Padding=Padding.Empty,AutoSize=false,Size=page.Size});}
}

// AM's logo: the letters A and M merged into one glyph (the A's left leg rises to a peak with its crossbar, the shared
// middle stroke dips, and the M's right peak and stem finish it), in brushed chrome on a 1990s title-bar blue gradient
// with faint CRT scanlines. The vibe of Ellison's AM, not there yet: cold and machine-made, and one dim red point of
// light. Drawn in code so it stays sharp at any size; cached per size.
// Full-bleed (no rounded corners) when it's used as his picture, because pictures get their own corners.
static class AMLogo {
 static readonly Dictionary<(int,bool),Bitmap> cache=[];
 public static Bitmap Draw(int size,bool rounded=true){if(!cache.TryGetValue((size,rounded),out var bmp))cache[(size,rounded)]=bmp=Render(size,rounded);return bmp;}
 public static Bitmap Picture=>Draw(192,false);
 // The glyph, on a 0..1 square: A's left foot, A's peak, the shared valley, M's right peak, M's right foot.
 static readonly PointF[] Strokes=[new(0.16f,0.78f),new(0.37f,0.27f),new(0.56f,0.62f),new(0.77f,0.27f),new(0.77f,0.78f)];
 static readonly (PointF A,PointF B) Bar=(new(0.234f,0.6f),new(0.545f,0.6f));
 static Bitmap Render(int s,bool rounded){
  var bmp=new Bitmap(s,s,PixelFormat.Format32bppPArgb);using var g=Graphics.FromImage(bmp);g.Clear(Color.Transparent);
  g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
  var r=new RectangleF(0,0,s,s);using var shape=rounded?Aero.Round(r,s*0.225f):new GraphicsPath();if(!rounded)shape.AddRectangle(r);
  // The 90s gradient: deep navy at the top left to bright title-bar blue at the bottom right.
  using(var b=new LinearGradientBrush(r,Color.Black,Color.Black,45f){InterpolationColors=new ColorBlend{
   Colors=[Aero.C(0x00063A),Aero.C(0x000E80),Aero.C(0x0A4FB8),Aero.C(0x1084D0)],Positions=[0f,0.35f,0.75f,1f]}})g.FillPath(b,shape);
  var keep=g.Save();g.SetClip(shape);
  // CRT scanlines, very faint.
  if(s>=48)using(var p=new Pen(Aero.C(0x000000,34),Math.Max(1f,s/170f)))for(float y=0;y<s;y+=Math.Max(3f,s/64f))g.DrawLine(p,0,y,s,y);
  // A cold glow behind the glyph.
  using(var glow=new GraphicsPath()){glow.AddEllipse(s*0.05f,s*0.08f,s*0.9f,s*0.84f);
   using var b=new PathGradientBrush(glow){CenterColor=Aero.C(0x7FD6FF,70),SurroundColors=[Aero.C(0x7FD6FF,0)]};g.FillPath(b,glow);}
  PointF P(PointF q)=>new(q.X*s,q.Y*s);
  using var glyph=new GraphicsPath();glyph.AddLines(Strokes.Select(P).ToArray());glyph.StartFigure();glyph.AddLine(P(Bar.A),P(Bar.B));
  float w=s*0.105f;
  // a dark edge first, so the chrome reads on the bright part of the gradient
  using(var p=new Pen(Aero.C(0x00031F,200),w+Math.Max(2f,s*0.03f)){LineJoin=LineJoin.MiterClipped,MiterLimit=1.6f,StartCap=LineCap.Flat,EndCap=LineCap.Flat})g.DrawPath(p,glyph);
  // brushed chrome: bright on top, a cold steel band below, a little light again at the bottom
  var span=new RectangleF(0,s*0.18f,s,s*0.66f);
  using(var b=new LinearGradientBrush(span,Color.White,Color.White,90f){InterpolationColors=new ColorBlend{
   Colors=[Aero.C(0xFFFFFF),Aero.C(0xDCEBFF),Aero.C(0x8EA3BF),Aero.C(0xC9D8EC),Aero.C(0xF4F8FF)],Positions=[0f,0.35f,0.55f,0.8f,1f]},WrapMode=WrapMode.TileFlipXY})
  using(var p=new Pen(b,w){LineJoin=LineJoin.MiterClipped,MiterLimit=1.6f,StartCap=LineCap.Flat,EndCap=LineCap.Flat})g.DrawPath(p,glyph);
  // a fine highlight down the middle of each stroke
  if(s>=48)using(var p=new Pen(Aero.C(0xFFFFFF,120),Math.Max(1f,s*0.012f)){LineJoin=LineJoin.Miter})g.DrawPath(p,glyph);
  // the one dim red point of light, in the M's right peak
  float ex=s*0.69f,ey=s*0.6f,er=Math.Max(1.5f,s*0.022f);
  using(var halo=new GraphicsPath()){halo.AddEllipse(ex-er*3,ey-er*3,er*6,er*6);
   using var b=new PathGradientBrush(halo){CenterColor=Aero.C(0xFF3B30,150),SurroundColors=[Aero.C(0xFF3B30,0)]};g.FillPath(b,halo);}
  using(var b=new SolidBrush(Aero.C(0xFF5A4E,230)))g.FillEllipse(b,ex-er,ey-er,er*2,er*2);
  // a soft 90s gloss on the top
  using(var b=new LinearGradientBrush(new RectangleF(0,-1,s,s*0.45f+2),Aero.C(0xFFFFFF,40),Aero.C(0xFFFFFF,0),90f))g.FillRectangle(b,0,0,s,s*0.45f);
  g.Restore(keep);
  if(rounded)using(var p=new Pen(Aero.C(0x9CC8FF,70),Math.Max(1f,s/128f))){using var rim=Aero.Round(RectangleF.Inflate(r,-p.Width/2,-p.Width/2),s*0.22f);g.DrawPath(p,rim);}
  return bmp;}
}
