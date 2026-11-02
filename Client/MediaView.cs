using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
namespace FriendsChat;

// A picture or video shown in the chat. Data is set to null when memory is reclaimed; the card then says it is gone.
sealed class MediaItem(string mime,byte[] data,string from){
 public readonly string Mime=mime,From=from;public byte[]? Data=data;public readonly long Size=data.Length;
}

static class Pictures {
 const int MaxPixels=50_000_000;
 // Decodes with GDI+ and refuses absurd dimensions before anything is drawn (a tiny file can claim a huge picture).
 static Image Load(byte[] data,out MemoryStream stream){stream=new MemoryStream(data,false);var img=Image.FromStream(stream,false,true);
  if((long)img.Width*img.Height>MaxPixels){img.Dispose();stream.Dispose();throw new IOException("That picture is too large to show.");}return img;}
 public static Image Open(byte[] data,out MemoryStream stream)=>Load(data,out stream);
 static bool IsAnimated(byte[] data){try{using var img=Load(data,out var s);using(s)return img.GetFrameCount(FrameDimension.Time)>1;}catch(Exception e) when(e is ArgumentException or IOException or OutOfMemoryException or ExternalException){return false;}}
 // Ready to send: turned upright, at most 1920 px on the long side, re-saved as JPEG (PNG if it has transparency).
 // Re-saving drops hidden details such as GPS location. Animated GIFs up to 5 MB are sent unchanged so they keep moving.
 public static (byte[] Data,string Mime) Prepare(byte[] original,string? mime){
  if(mime=="image/gif"&&original.Length<=Media.MaxImage&&IsAnimated(original))return(original,"image/gif");
  using var src=Load(original,out var stream);using(stream){
   Upright(src);
   // Many opaque screenshots are stored in a format that could hold transparency; only real see-through pixels keep PNG.
   bool clear=Image.IsAlphaPixelFormat(src.PixelFormat)&&src is Bitmap original32&&HasTransparency(original32);
   for(int side=1920;side>=300;side=side*3/4){using var scaled=Scale(src,side,clear);var bytes=clear?Png(scaled):Jpeg(scaled,85);if(bytes.Length<=Media.MaxImage)return(bytes,clear?"image/png":"image/jpeg");}}
  throw new IOException("That picture is too large to send.");}
 static bool HasTransparency(Bitmap bmp){var data=bmp.LockBits(new Rectangle(0,0,bmp.Width,bmp.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
  try{var row=new byte[bmp.Width*4];for(int y=0;y<bmp.Height;y++){Marshal.Copy(data.Scan0+y*data.Stride,row,0,row.Length);for(int i=3;i<row.Length;i+=4)if(row[i]<255)return true;}return false;}
  finally{bmp.UnlockBits(data);}}
 public static (byte[] Data,string Mime) Prepare(Image image)=>Prepare(Png(image),"image/png");
 static void Upright(Image img){const int orientation=0x0112;if(!img.PropertyIdList.Contains(orientation))return;
  var flip=img.GetPropertyItem(orientation)?.Value?[0] switch{3=>RotateFlipType.Rotate180FlipNone,6=>RotateFlipType.Rotate90FlipNone,8=>RotateFlipType.Rotate270FlipNone,_=>RotateFlipType.RotateNoneFlipNone};
  if(flip!=RotateFlipType.RotateNoneFlipNone)img.RotateFlip(flip);}
 static Bitmap Scale(Image src,int maxSide,bool alpha){double f=Math.Min(1d,(double)maxSide/Math.Max(src.Width,src.Height));int w=Math.Max(1,(int)(src.Width*f)),h=Math.Max(1,(int)(src.Height*f));
  var bmp=new Bitmap(w,h,alpha?PixelFormat.Format32bppArgb:PixelFormat.Format24bppRgb);using var g=Graphics.FromImage(bmp);if(!alpha)g.Clear(Color.White);
  g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
  // Mirrored edge sampling stops the resizer from blending the picture's border with empty canvas.
  using var edges=new ImageAttributes();edges.SetWrapMode(WrapMode.TileFlipXY);g.DrawImage(src,new Rectangle(0,0,w,h),0,0,src.Width,src.Height,GraphicsUnit.Pixel,edges);return bmp;}
 static byte[] Png(Image img){using var ms=new MemoryStream();img.Save(ms,ImageFormat.Png);return ms.ToArray();}
 static byte[] Jpeg(Image img,long quality){using var ms=new MemoryStream();var codec=ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==ImageFormat.Jpeg.Guid);
  using var args=new EncoderParameters(1);args.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,quality);img.Save(ms,codec,args);return ms.ToArray();}
 // Chat thumbnail: the picture scaled to fit (the chat rounds its corners); null if GDI+ can't read it.
 public static Bitmap? Thumbnail(byte[] data,int maxW,int maxH){
  try{using var img=Load(data,out var stream);using(stream){Upright(img);double f=Math.Min(1d,Math.Min((double)maxW/img.Width,(double)maxH/img.Height));int w=Math.Max(1,(int)(img.Width*f)),h=Math.Max(1,(int)(img.Height*f));
   var bmp=new Bitmap(w,h,PixelFormat.Format32bppPArgb);using var g=Graphics.FromImage(bmp);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
   using var edges=new ImageAttributes();edges.SetWrapMode(WrapMode.TileFlipXY);g.DrawImage(img,new Rectangle(0,0,w,h),0,0,img.Width,img.Height,GraphicsUnit.Pixel,edges);return bmp;}}
  catch(Exception e) when(e is ArgumentException or IOException or OutOfMemoryException or ExternalException){return null;}}
 // Card shown in the chat for a video: dark panel, round play button, size.
 public static Bitmap VideoCard(long size,int w,int h,bool gone=false){
  var bmp=new Bitmap(w,h,PixelFormat.Format32bppPArgb);using var g=Graphics.FromImage(bmp);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
  var r=new RectangleF(0.5f,0.5f,w-1f,h-1f);using var shape=Aero.Round(r,h/28f);
  using(var b=new LinearGradientBrush(r,Aero.Steel,Aero.Rail,90f))g.FillPath(b,shape);using(var p=new Pen(Aero.Top))g.DrawPath(p,shape);
  float d=h*0.3f;var c=new RectangleF((w-d)/2,(h-d)/2-h*0.05f,d,d);
  using(var b=new LinearGradientBrush(c,gone?Aero.C(0x4E5058):Aero.AccentHover,gone?Aero.C(0x3A3F48):Aero.AccentDown,90f))g.FillEllipse(b,c);
  var tri=new[]{new PointF(c.X+d*0.40f,c.Y+d*0.29f),new PointF(c.X+d*0.40f,c.Y+d*0.71f),new PointF(c.X+d*0.74f,c.Y+d*0.5f)};g.FillPolygon(Brushes.White,tri);
  using var f=new Font("Segoe UI",Math.Max(10f,h*0.065f),FontStyle.Regular,GraphicsUnit.Pixel);using var sf=new StringFormat{Alignment=StringAlignment.Center};
  using(var b=new SolidBrush(Aero.Muted))g.DrawString(gone?"Video  •  no longer kept in memory":$"Video  •  {Media.Size(size)}  •  click to play",f,b,new RectangleF(0,h*0.80f,w,h*0.15f),sf);
  return bmp;}
}

// Built-in sticker pack: glossy badges drawn in code (on a transparent background), so only the id travels over the network.
static class StickerArt {
 static readonly Dictionary<string,(string Glyph,string Label,int Color)> look=new(){
  ["hi"]=("👋","Hi!",0x2B86D7),["lol"]=("😂","LOL",0xF2A600),["wow"]=("😮","WOW",0x8E24AA),["love"]=("❤","Love it",0xE0245E),["yes"]=("👍","Yes!",0x2E9E2E),["no"]=("👎","Nope",0x6D6D6D),
  ["gg"]=("🏆","GG",0xC99A06),["brb"]=("⏳","BRB",0x00838F),["party"]=("🎉","Party!",0xD35400),["sad"]=("😢","Aww",0x3D7BAD),["coffee"]=("☕","Coffee?",0x795548),["orb"]=("","3DFx!",0x0A3478)};
 static readonly Dictionary<(string,int),Bitmap> cache=[];
 public static Bitmap Draw(string id,int size){if(cache.TryGetValue((id,size),out var hit))return hit;var bmp=Render(id,size);cache[(id,size)]=bmp;return bmp;}
 static Bitmap Render(string id,int s){
  var (glyph,label,rgb)=look.TryGetValue(id,out var l)?l:("?","?",0x6D6D6D);var color=Aero.C(rgb);
  var bmp=new Bitmap(s,s,PixelFormat.Format32bppPArgb);using var g=Graphics.FromImage(bmp);g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
  var c=new RectangleF(s*0.12f,s*0.03f,s*0.76f,s*0.76f);
  if(id=="orb")Aero.Orb(g,c);
  else{
   using(var sh=new GraphicsPath()){sh.AddEllipse(c.X+s*0.01f,c.Y+s*0.02f,c.Width,c.Height);using var b=new SolidBrush(Aero.C(0x000000,35));g.FillPath(b,sh);}
   using var body=new GraphicsPath();body.AddEllipse(c);
   using(var b=new PathGradientBrush(body){CenterPoint=new PointF(c.X+c.Width/2,c.Y+c.Height*0.78f),CenterColor=ControlPaint.Light(color,0.9f),SurroundColors=[ControlPaint.Dark(color,0.2f)]})g.FillPath(b,body);
   using(var path=new GraphicsPath()){using var family=new FontFamily("Segoe UI Emoji");path.AddString(glyph,family,0,s*0.36f,PointF.Empty,StringFormat.GenericTypographic);var bounds=path.GetBounds();
    using var m=new Matrix();m.Translate(c.X+c.Width/2-bounds.X-bounds.Width/2,c.Y+c.Height/2-bounds.Y-bounds.Height/2);path.Transform(m);
    using(var shadow=(GraphicsPath)path.Clone()){using var mm=new Matrix();mm.Translate(s*0.012f,s*0.015f);shadow.Transform(mm);using var b=new SolidBrush(Aero.C(0x000000,70));g.FillPath(b,shadow);}
    g.FillPath(Brushes.White,path);}
   var cap=new RectangleF(c.X+c.Width*0.15f,c.Y+c.Height*0.04f,c.Width*0.7f,c.Height*0.46f);using(var cp=new GraphicsPath()){cp.AddEllipse(cap);using var b=new LinearGradientBrush(new RectangleF(cap.X,cap.Y-1,cap.Width,cap.Height+2),Aero.C(0xFFFFFF,170),Aero.C(0xFFFFFF,8),90f);g.FillPath(b,cp);}
   using(var p=new Pen(ControlPaint.Dark(color,0.35f),Math.Max(1f,s/70f)))g.DrawEllipse(p,c);}
  // Label pill across the bottom.
  using var font=new Font("Segoe UI",s*0.12f,FontStyle.Bold,GraphicsUnit.Pixel);var size=g.MeasureString(label,font);
  var pill=new RectangleF((s-size.Width)/2-s*0.06f,s*0.70f,size.Width+s*0.12f,s*0.22f);
  using(var sh=Aero.Round(new RectangleF(pill.X+1,pill.Y+2,pill.Width,pill.Height),pill.Height/2))using(var b=new SolidBrush(Aero.C(0x000000,40)))g.FillPath(b,sh);
  using(var pp=Aero.Round(pill,pill.Height/2)){Aero.Gloss(g,pp,pill,Color.White,Aero.C(0xF4F7FB),Aero.C(0xE6ECF3),Aero.C(0xDCE3EC));using var p=new Pen(color,Math.Max(1f,s/80f));g.DrawPath(p,pp);}
  using(var sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})using(var b=new SolidBrush(ControlPaint.Dark(color,0.1f)))g.DrawString(label,font,b,pill,sf);
  return bmp;}
}

// Base viewer window: dark page, a bar at the bottom with details and a Save button. Esc closes it.
class MediaWindow:Form {
 protected readonly byte[] Data;readonly string mime;readonly string from;
 protected readonly Panel Bar=new(){Dock=DockStyle.Bottom};readonly Label info=new(){AutoSize=true,ForeColor=Aero.Muted,BackColor=Aero.Side};
 public MediaWindow(MediaItem item,string kind){
  Data=item.Data??[];mime=item.Mime;from=item.From;Motion.FadeIn(this,200);Text=$"{kind} from {item.From} — 3DFx Vista";Font=Aero.Body;BackColor=Aero.Rail;KeyPreview=true;StartPosition=FormStartPosition.CenterParent;
  try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch(ArgumentException){}
  KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape)Close();};
  Bar.Height=LogicalToDeviceUnits(56);Bar.BackColor=Aero.Side;
  var save=new FlatButton{Text="Save…",Kind=ButtonKind.Primary,Anchor=AnchorStyles.Right|AnchorStyles.Top,Size=new Size(LogicalToDeviceUnits(100),LogicalToDeviceUnits(36))};save.Click+=(_,_)=>Save();
  Bar.Controls.Add(save);Bar.Controls.Add(info);info.Text=$"From {from}  •  {Media.Size(Data.Length)}";
  Bar.Layout+=(_,_)=>{save.Location=new Point(Bar.Width-save.Width-LogicalToDeviceUnits(14),(Bar.Height-save.Height)/2);info.Location=new Point(LogicalToDeviceUnits(14),(Bar.Height-info.Height)/2);};
  Controls.Add(Bar);}
 protected void SetInfo(string text)=>info.Text=text;
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Aero.DarkTitleBar(Handle);}
 // Saving is the only way media reaches the disk, and only where the user chooses.
 void Save(){var ext=mime switch{"image/jpeg"=>".jpg","image/png"=>".png","image/gif"=>".gif","video/mp4"=>".mp4","video/webm"=>".webm",_=>""};
  using var d=new SaveFileDialog{FileName=$"3DFx {from} {DateTime.Now:yyyy-MM-dd HHmm}{ext}",Filter=$"{ext.TrimStart('.').ToUpperInvariant()} file|*{ext}",AddExtension=true,OverwritePrompt=true};
  if(d.ShowDialog(this)==DialogResult.OK)try{File.WriteAllBytes(d.FileName,Data);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){MessageBox.Show(this,"Could not save: "+e.Message,"3DFx Vista",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
 protected Size Fit(int w,int h){var area=Screen.FromControl(this).WorkingArea;double f=Math.Min(1d,Math.Min(area.Width*0.8/w,(area.Height*0.8-Bar.Height)/h));return new Size(Math.Max(LogicalToDeviceUnits(420),(int)(w*f)),Math.Max(LogicalToDeviceUnits(300),(int)(h*f)+Bar.Height));}
}

// Full-size picture; animated GIFs play.
sealed class ImageViewer:MediaWindow {
 readonly MemoryStream? stream;readonly Image? image;
 public ImageViewer(MediaItem item):base(item,"Picture"){
  try{image=Pictures.Open(Data,out var s);stream=s;}catch(Exception e) when(e is ArgumentException or IOException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException){}
  var box=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Image=image,BackColor=BackColor};Controls.Add(box);box.BringToFront();
  ClientSize=image==null?new Size(LogicalToDeviceUnits(420),LogicalToDeviceUnits(300)):Fit(image.Width,image.Height);
  if(image!=null)SetInfo($"From {item.From}  •  {image.Width}×{image.Height}  •  {Media.Size(Data.Length)}");
  FormClosed+=(_,_)=>{image?.Dispose();stream?.Dispose();};}
}

// Video played inside the app by WebView2 (Microsoft Edge's engine), straight from memory: scripts off, no navigation,
// no downloads, private mode. Decoding untrusted video happens in Edge's sandboxed processes, not in 3DFx itself.
sealed class VideoViewer:MediaWindow {
 const string Origin="https://media.3dfx.example/";
 readonly string mime;
 public VideoViewer(MediaItem item):base(item,"Video"){
  mime=item.Mime;ClientSize=Fit(LogicalToDeviceUnits(960),LogicalToDeviceUnits(540));
  var web=new WebView2{Dock=DockStyle.Fill,DefaultBackgroundColor=BackColor};Controls.Add(web);web.BringToFront();
  Load+=async(_,_)=>await Start(web);}
 async Task Start(WebView2 web){
  try{
   var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"3DFx","WebView2"));
   var options=env.CreateCoreWebView2ControllerOptions();options.IsInPrivateModeEnabled=true;
   await web.EnsureCoreWebView2Async(env,options);
   var core=web.CoreWebView2;var s=core.Settings;
   s.IsScriptEnabled=false;s.AreDevToolsEnabled=false;s.AreDefaultContextMenusEnabled=false;s.IsStatusBarEnabled=false;s.AreBrowserAcceleratorKeysEnabled=false;s.IsWebMessageEnabled=false;s.IsZoomControlEnabled=false;s.IsGeneralAutofillEnabled=false;s.IsPasswordAutosaveEnabled=false;
   core.NewWindowRequested+=(_,e)=>e.Handled=true;core.DownloadStarting+=(_,e)=>e.Cancel=true;
   core.NavigationStarting+=(_,e)=>{if(!e.Uri.StartsWith(Origin,StringComparison.Ordinal))e.Cancel=true;};
   core.AddWebResourceRequestedFilter(Origin+"*",CoreWebView2WebResourceContext.All);
   core.WebResourceRequested+=(_,e)=>e.Response=Serve(core.Environment,e.Request);
   core.Navigate(Origin+"player.html");}
  catch(WebView2RuntimeNotFoundException){Missing(web,"Playing videos needs the Microsoft Edge WebView2 Runtime (built into Windows 11). Use Save… to watch it in another player.");}
  catch(Exception e) when(e is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException){Missing(web,"This video could not be played here. Use Save… to watch it in another player.");}}
 void Missing(Control web,string text){Controls.Remove(web);web.Dispose();var l=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=Aero.Text,Text=text,Padding=new Padding(LogicalToDeviceUnits(30))};Controls.Add(l);l.BringToFront();}
 // A tiny page with only a <video> tag, plus the video itself with byte-range support so seeking works.
 CoreWebView2WebResourceResponse Serve(CoreWebView2Environment env,CoreWebView2WebResourceRequest request){
  var path=request.Uri.Length>Origin.Length?request.Uri[Origin.Length..]:"";
  if(path=="player.html"){
   const string html="<!doctype html><meta charset=utf-8><style>html,body{margin:0;height:100%;background:#1e1f22}body{display:flex;align-items:center;justify-content:center}video{max-width:100%;max-height:100%}</style><video src=\"video\" controls autoplay controlslist=\"nodownload noremoteplayback\" disablepictureinpicture></video>";
   return env.CreateWebResourceResponse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html)),200,"OK","Content-Type: text/html; charset=utf-8\r\nContent-Security-Policy: default-src 'none'; media-src "+Origin+"; style-src 'unsafe-inline'");}
  if(path!="video")return env.CreateWebResourceResponse(null,404,"Not Found","");
  long length=Data.Length,start=0,end=length-1;
  var range=request.Headers.Contains("Range")?request.Headers.GetHeader("Range"):null;
  if(range!=null&&range.StartsWith("bytes=")){var parts=range[6..].Split('-');if(long.TryParse(parts[0],out var a))start=Math.Clamp(a,0,length-1);if(parts.Length>1&&long.TryParse(parts[1],out var b))end=Math.Clamp(b,start,length-1);
   return env.CreateWebResourceResponse(new MemoryStream(Data,(int)start,(int)(end-start+1),false),206,"Partial Content",$"Content-Type: {mime}\r\nAccept-Ranges: bytes\r\nContent-Range: bytes {start}-{end}/{length}\r\nContent-Length: {end-start+1}");}
  return env.CreateWebResourceResponse(new MemoryStream(Data,false),200,"OK",$"Content-Type: {mime}\r\nAccept-Ranges: bytes\r\nContent-Length: {length}");}
}
