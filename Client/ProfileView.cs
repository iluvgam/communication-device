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

// Small tag after a name: "AI" for an AI member (Groki), so nobody mistakes him for a person, and "GUEST" for people without an account.
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

// Groki's logo, modern: a black hole drawn with a few bold, clean shapes. A near-black rounded square, a soft violet bloom,
// a thick ring of light around the hole that sweeps through violet, magenta, pink and amber (brightest on the left, the
// side of the disk that comes towards you), a slim tilted disk passing in front, and the pure black hole in the middle.
// No stars, no gloss. Drawn in code so it stays sharp at any size; cached per size.
// Full-bleed (no rounded corners) when it's used as his picture, because pictures get their own corners.
static class GrokiLogo {
 static readonly Dictionary<(int,bool),Bitmap> cache=[];
 public static Bitmap Draw(int size,bool rounded=true){if(!cache.TryGetValue((size,rounded),out var bmp))cache[(size,rounded)]=bmp=Render(size,rounded);return bmp;}
 public static Bitmap Picture=>Draw(192,false);
 // The ring's colours going round from the top, clockwise; it ends where it started.
 static readonly (float At,int Rgb)[] Sweep=[(0f,0x7C5CFF),(0.22f,0xD946EF),(0.5f,0xFF4D8D),(0.74f,0xFFB347),(0.88f,0xFFE3A3),(1f,0x7C5CFF)];
 static Color SweepAt(float t,int alpha){
  for(int i=1;i<Sweep.Length;i++)if(t<=Sweep[i].At){var (a,ca)=Sweep[i-1];var (b,cb)=Sweep[i];float k=(t-a)/(b-a);
   int Mix(int sh)=>(int)Math.Round(((ca>>sh)&255)*(1-k)+((cb>>sh)&255)*k);return Color.FromArgb(alpha,Mix(16),Mix(8),Mix(0));}
  return Aero.C(Sweep[^1].Rgb,alpha);}
 static Bitmap Render(int s,bool rounded){
  var bmp=new Bitmap(s,s,PixelFormat.Format32bppPArgb);using var g=Graphics.FromImage(bmp);g.Clear(Color.Transparent);
  g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
  var r=new RectangleF(0,0,s,s);using var shape=rounded?Aero.Round(r,s*0.225f):new GraphicsPath();if(!rounded)shape.AddRectangle(r);
  // Background: a quiet diagonal from deep indigo to almost black.
  using(var b=new LinearGradientBrush(r,Aero.C(0x17123A),Aero.C(0x05040C),45f))g.FillPath(b,shape);
  var keep=g.Save();g.SetClip(shape);
  float cx=s*0.5f,cy=s*0.5f,hole=s*0.2f,ring=s*0.255f;
  // A soft violet bloom behind everything.
  using(var bloom=new GraphicsPath()){bloom.AddEllipse(cx-s*0.46f,cy-s*0.46f,s*0.92f,s*0.92f);
   using var b=new PathGradientBrush(bloom){CenterColor=Aero.C(0x8B5CF6,95),SurroundColors=[Aero.C(0x8B5CF6,0)]};g.FillPath(b,bloom);}
  // The disk: a slim tilted ellipse, white-hot in the middle and fading out at both ends. Its far half is drawn first,
  // so the ring and the hole cover it; its near half is drawn last, in front of both.
  var disk=new RectangleF(cx-s*0.45f,cy-s*0.07f,s*0.9f,s*0.14f);
  void Disk(){foreach(var (w,a) in new[]{(0.07f,70),(0.036f,255)}){
    using var b=new LinearGradientBrush(RectangleF.Inflate(disk,1,s*0.1f),Color.Black,Color.Black,0f){InterpolationColors=new ColorBlend{
     Colors=[Aero.C(0xFFB347,0),Aero.C(0xFFB347,a),Aero.C(0xFFF7EA,a),Aero.C(0xFF4D8D,a),Aero.C(0xFF4D8D,0)],Positions=[0f,0.2f,0.45f,0.8f,1f]}};
    using var p=new Pen(b,s*w);g.DrawEllipse(p,disk);}}
  var tilt=g.Save();g.TranslateTransform(cx,cy);g.RotateTransform(-16f);g.TranslateTransform(-cx,-cy);
  g.SetClip(new RectangleF(0,0,s,cy),CombineMode.Intersect);Disk();g.Restore(tilt);
  // The ring's glow: one smooth halo, brightest along the ring and fading both ways.
  using(var halo=new GraphicsPath()){float o=ring+s*0.16f;halo.AddEllipse(cx-o,cy-o,o*2,o*2);float at=s*0.16f/o;
   using var b=new PathGradientBrush(halo){CenterPoint=new PointF(cx,cy),InterpolationColors=new ColorBlend{
    Colors=[Aero.C(0xE879F9,0),Aero.C(0xE879F9,120),Aero.C(0xE879F9,0),Aero.C(0xE879F9,0)],Positions=[0f,at,Math.Min(0.95f,at+0.25f),1f]}};g.FillPath(b,halo);}
  // The ring of light: short arcs, each its own colour, so the colour sweeps smoothly around (GDI+ has no conic gradient).
  using(var p=new Pen(Color.Black,s*0.07f)){const int n=180;
   for(int i=0;i<n;i++){p.Color=SweepAt((i+0.5f)/n,255);g.DrawArc(p,cx-ring,cy-ring,ring*2,ring*2,-90f+i*360f/n,360f/n+1.2f);}}
  // The hole: pure black, with a hairline of light right at its edge.
  g.FillEllipse(Brushes.Black,cx-hole,cy-hole,hole*2,hole*2);
  using(var p=new Pen(Aero.C(0xFFFFFF,110),Math.Max(1f,s*0.006f)))g.DrawEllipse(p,cx-hole,cy-hole,hole*2,hole*2);
  // The near half of the disk crosses in front of the hole.
  tilt=g.Save();g.TranslateTransform(cx,cy);g.RotateTransform(-16f);g.TranslateTransform(-cx,-cy);
  g.SetClip(new RectangleF(0,cy,s,s),CombineMode.Intersect);Disk();g.Restore(tilt);
  g.Restore(keep);
  if(rounded)using(var p=new Pen(Aero.C(0xFFFFFF,28),Math.Max(1f,s/128f))){using var rim=Aero.Round(RectangleF.Inflate(r,-p.Width/2,-p.Width/2),s*0.22f);g.DrawPath(p,rim);}
  return bmp;}
}
