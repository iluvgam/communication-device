using System.Runtime.InteropServices;
namespace FriendsChat;

// Apple-style motion: values glide to their targets with ease-out curves or a soft spring, all on one shared ~60 fps timer.
// Follows Windows' "Animation effects" setting (Settings > Accessibility > Visual effects): when it is off, everything jumps straight to the end.
static class Motion {
 [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action,uint param,ref bool value,uint ini);
 public static bool Enabled{get{bool on=true;try{SystemParametersInfo(0x1042,0,ref on,0);}catch(EntryPointNotFoundException){}return on;}}
 sealed class Anim{public required object Key;public required Control Owner;public required double Ms;public required Action<double> Step;public required Func<double,double> Ease;public Action? Done;public long Start;}
 static readonly List<Anim> running=[];
 static readonly List<(Control Owner,Func<bool> Active)> loops=[];
 static readonly System.Windows.Forms.Timer timer=new(){Interval=15};
 static Motion(){timer.Tick+=(_,_)=>Tick();}
 // Runs step(eased 0..1) for ms milliseconds; a new run with the same key replaces the old one (so a hover that reverses starts from where it is).
 public static void Run(object key,Control owner,double ms,Action<double> step,Func<double,double>? ease=null,Action? done=null){
  running.RemoveAll(a=>a.Key==key);
  if(!Enabled||ms<=0||owner.IsDisposed){step(1);done?.Invoke();return;}
  running.Add(new Anim{Key=key,Owner=owner,Ms=ms,Step=step,Ease=ease??Out,Done=done,Start=Environment.TickCount64});timer.Start();}
 // Repaints owner every frame while active() holds (for continuous motion such as the typing dots).
 public static void Loop(Control owner,Func<bool> active){if(!Enabled||loops.Any(l=>l.Owner==owner))return;loops.Add((owner,active));timer.Start();}
 public static bool Busy(object key)=>running.Any(a=>a.Key==key);
 static void Tick(){long now=Environment.TickCount64;
  foreach(var a in running.ToList()){if(a.Owner.IsDisposed){running.Remove(a);continue;}double t=Math.Min(1,(now-a.Start)/a.Ms);
   try{a.Step(t>=1?1:a.Ease(t));}catch(ObjectDisposedException){running.Remove(a);continue;}if(t>=1){running.Remove(a);a.Done?.Invoke();}}
  foreach(var l in loops.ToList()){if(l.Owner.IsDisposed||!l.Active()){loops.Remove(l);if(!l.Owner.IsDisposed)l.Owner.Invalidate();continue;}l.Owner.Invalidate();}
  if(running.Count==0&&loops.Count==0)timer.Stop();}
 // Curves: Out decelerates (most UI); Spring settles with a small overshoot, like iOS; InOut for fades.
 public static double Out(double t)=>1-Math.Pow(1-t,3);
 public static double Spring(double t)=>1-Math.Exp(-6.5*t)*Math.Cos(8.5*t);
 public static double InOut(double t)=>t<0.5?4*t*t*t:1-Math.Pow(-2*t+2,3)/2;
 public static float Lerp(float a,float b,double t)=>(float)(a+(b-a)*t);
 public static Color Mix(Color a,Color b,double t){t=Math.Clamp(t,0,1);return Color.FromArgb((int)(a.A+(b.A-a.A)*t),(int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));}
 // Fades a window in when it first appears.
 public static void FadeIn(Form f,double ms=200){if(!Enabled)return;f.Opacity=0;f.Shown+=(_,_)=>Run(f,f,ms,t=>{if(!f.IsDisposed)f.Opacity=Math.Min(1,t);},Out);}
 // Fades a pop-up menu or card in as it opens.
 public static void FadeIn(ToolStripDropDown d,double ms=130){if(!Enabled)return;d.Opening+=(_,_)=>d.Opacity=0;d.Opened+=(_,_)=>Run(d,d,ms,t=>{if(!d.IsDisposed)d.Opacity=Math.Min(1,t);},Out);}
}

// A number that glides to a new target (hover amount, knob position, pill height…), repainting its control as it moves.
sealed class Glide(Control owner,float value=0){
 public float Value{get;private set;}=value;public float Target{get;private set;}=value;
 public void To(float target,double ms=180,Func<double,double>? ease=null){if(target==Target&&Motion.Busy(this))return;if(target==Value&&!Motion.Busy(this)){Target=target;return;}
  Target=target;float from=Value;Motion.Run(this,owner,ms,t=>{Value=Motion.Lerp(from,target,t);owner.Invalidate();},ease);}
 public void Set(float value){Value=Target=value;}
}
