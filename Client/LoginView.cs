using System.Drawing.Drawing2D;
namespace FriendsChat;

// First screen, over the whole window: log in, register or reset your password on a relay, or continue as a guest (rooms only).
// Problems mark only the field they are about, in its caption; the password is never cleared by another field's mistake.
sealed class LoginView:Control {
 enum Mode{SignIn,Register,Recover}
 Mode mode=Mode.SignIn;
 public event Action<AccountReply,bool,string>? SignedIn;
 public event Action<string,string>? Guest;
 sealed class Field {
  public readonly string Caption;public readonly InputBox Frame;public readonly Label Label;public TextBox Box=>Frame.Box;
  public Field(string caption,InputBox frame,Control scale){Caption=caption;Frame=frame;Label=new Label{AutoSize=true,Font=Aero.Caps,Margin=new Padding(0,scale.LogicalToDeviceUnits(16),0,scale.LogicalToDeviceUnits(8))};Reset();Box.TextChanged+=(_,_)=>Reset();}
  public void Reset(){Label.Text=Caption.ToUpperInvariant();Label.ForeColor=Aero.Accent;Frame.Problem=false;}
 }
 readonly Field relay,user,pass,again,key,guest;
 readonly Toggle keep=new(){Checked=true};
 readonly Panel card=new(){BackColor=Aero.Bg};
 readonly TableLayoutPanel layout=new(){ColumnCount=2,AutoSize=true,BackColor=Aero.Bg,Margin=Padding.Empty,Padding=Padding.Empty};
 readonly TableLayoutPanel form=new(){ColumnCount=1,AutoSize=true,BackColor=Aero.Bg,Margin=Padding.Empty},side=new(){ColumnCount=1,AutoSize=true,BackColor=Aero.Bg};
 readonly Label error=new(){AutoSize=true,ForeColor=Aero.Red,Font=Aero.Small,Visible=false};
 int S(int px)=>LogicalToDeviceUnits(px);
 int W=>S(400);
 float rise;
 protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(!Visible)return;rise=1;PerformLayout();Motion.Run(card,card,620,t=>{rise=(float)(1-t);PerformLayout();},Motion.Spring);}
 public LoginView(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Aero.Rail;
  relay=new("Relay address",new InputBox(placeholder:"https://your-relay.onrender.com",maxLength:300),this);user=new("Username",new InputBox(maxLength:20),this);
  pass=new("Password",new InputBox(secret:true),this);again=new("Confirm password",new InputBox(secret:true),this);
  key=new("Recovery key",new InputBox(placeholder:"XXXXX-XXXXX-XXXXX-XXXXX",maxLength:40),this);guest=new("Guest name",new InputBox(maxLength:32),this);
  foreach(var f in new[]{relay,user,pass,again,key,guest})f.Box.TextChanged+=(_,_)=>error.Visible=false;
  side.Padding=new Padding(S(32),0,0,0);side.Margin=new Padding(S(32),0,0,0);side.Paint+=(_,e)=>{using var p=new Pen(Aero.Line);e.Graphics.DrawLine(p,0,S(8),0,side.Height-S(8));};
  error.MaximumSize=new Size(W,0);layout.Controls.Add(form,0,0);layout.Controls.Add(side,1,0);card.Controls.Add(layout);Controls.Add(card);
  card.Padding=new Padding(S(32));layout.Location=new Point(S(32),S(32));Build();}
 public void Prefill(string relayUrl,string username,string guestName){relay.Box.Text=relayUrl;if(username.Length>0)user.Box.Text=username;guest.Box.Text=guestName;mode=Mode.SignIn;Build();}
 public void ShowProblem(string text){error.Text=text;error.Visible=true;}
 public void FocusFirst(){(relay.Box.TextLength==0?relay:user.Box.TextLength==0?user:pass).Box.Focus();}

 void Build(){form.SuspendLayout();side.SuspendLayout();form.Controls.Clear();side.Controls.Clear();error.Visible=false;
  Label Line(string t,Font f,Color c,int top,int bottom,int width)=>new(){Text=t,Font=f,ForeColor=c,AutoSize=true,MaximumSize=new Size(width,0),MinimumSize=new Size(width,0),TextAlign=ContentAlignment.TopCenter,Margin=new Padding(0,top,0,bottom)};
  void Add(Field f,TableLayoutPanel to,int width){f.Reset();to.Controls.Add(f.Label);f.Frame.Width=width;f.Frame.Margin=Padding.Empty;to.Controls.Add(f.Frame);}
  LinkLabel Link(string t,Action a,int top=8){var l=new LinkLabel{Text=t,AutoSize=true,LinkColor=Aero.Link,ActiveLinkColor=Aero.Link,LinkBehavior=LinkBehavior.HoverUnderline,Font=Aero.Small,Margin=new Padding(0,S(top),0,0),BackColor=Aero.Bg};l.LinkClicked+=(_,_)=>a();return l;}
  FlatButton Button(string t,ButtonKind kind,Func<Task> click,int width,int top=20){var b=new FlatButton{Text=t,Kind=kind,Size=new Size(width,S(44)),Margin=new Padding(0,S(top),0,0),Surface=Aero.Bg};b.Click+=async(_,_)=>await click();return b;}
  Control Footer(string question,string link,Mode to){var row=new FlowLayoutPanel{AutoSize=true,BackColor=Aero.Bg,Margin=new Padding(0,S(10),0,0),WrapContents=false};
   row.Controls.Add(new Label{Text=question,AutoSize=true,ForeColor=Aero.Muted,Font=Aero.Small,Margin=new Padding(0,S(8),S(4),0)});row.Controls.Add(Link(link,()=>Switch(to)));return row;}
  Control KeepRow(){var row=new TableLayoutPanel{ColumnCount=2,AutoSize=true,BackColor=Aero.Bg,Margin=new Padding(0,S(16),0,0)};row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,W-S(44)));row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
   var l=new Label{Text="Keep me signed in on this PC",AutoSize=true,ForeColor=Aero.Text,Margin=new Padding(0,S(3),0,0),Cursor=Cursors.Hand};l.Click+=(_,_)=>keep.Checked=!keep.Checked;keep.Margin=Padding.Empty;row.Controls.Add(l,0,0);row.Controls.Add(keep,1,0);return row;}
  switch(mode){
   case Mode.SignIn:
    form.Controls.Add(Line("Welcome to 3DFx Vista",Aero.Title,Aero.Bright,0,S(6),W));form.Controls.Add(Line("Log in to chat with your friends.",Aero.Body,Aero.Muted,0,S(4),W));
    Add(relay,form,W);Add(user,form,W);Add(pass,form,W);form.Controls.Add(Link("Forgot your password?",()=>Switch(Mode.Recover)));form.Controls.Add(KeepRow());
    form.Controls.Add(error);form.Controls.Add(Button("Log In",ButtonKind.Primary,SignIn,W));form.Controls.Add(Footer("Need an account?","Register",Mode.Register));
    int sw=S(240);var orb=new Panel{Size=new Size(sw,S(120)),BackColor=Aero.Bg,Margin=new Padding(0,S(24),0,0)};orb.Paint+=(_,e)=>{e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;Aero.Orb(e.Graphics,new RectangleF((sw-S(110))/2f,S(4),S(110),S(110)));};
    side.Controls.Add(orb);side.Controls.Add(Line("Just visiting?",Aero.Subtitle,Aero.Bright,S(16),S(6),sw));side.Controls.Add(Line("Join rooms as a guest. Friends and DMs need an account.",Aero.Small,Aero.Muted,0,0,sw));
    Add(guest,side,sw);side.Controls.Add(Button("Continue as guest",ButtonKind.Secondary,()=>{AsGuest();return Task.CompletedTask;},sw,16));break;
   case Mode.Register:
    form.Controls.Add(Line("Create an account",Aero.Title,Aero.Bright,0,S(6),W));form.Controls.Add(Line("Your username is how friends see you. It can't be changed later.",Aero.Body,Aero.Muted,0,S(4),W));
    Add(relay,form,W);Add(user,form,W);user.Box.PlaceholderText="3–20 letters, numbers, - or _";Add(pass,form,W);pass.Box.PlaceholderText="At least 10 characters";Add(again,form,W);form.Controls.Add(KeepRow());
    form.Controls.Add(error);form.Controls.Add(Button("Continue",ButtonKind.Primary,Register,W));form.Controls.Add(Footer("Already have an account?","Log in",Mode.SignIn));break;
   default:
    form.Controls.Add(Line("Reset your password",Aero.Title,Aero.Bright,0,S(6),W));form.Controls.Add(Line("Enter the recovery key you saved when you created the account. Every device will be signed out.",Aero.Body,Aero.Muted,0,S(4),W));
    Add(relay,form,W);Add(user,form,W);Add(key,form,W);Add(pass,form,W);pass.Box.PlaceholderText="New password, at least 10 characters";Add(again,form,W);form.Controls.Add(KeepRow());
    form.Controls.Add(error);form.Controls.Add(Button("Reset password",ButtonKind.Primary,Recover,W));form.Controls.Add(Footer("Remembered it?","Back to log in",Mode.SignIn));break;}
  if(mode==Mode.SignIn)user.Box.PlaceholderText="";if(mode!=Mode.Register&&mode!=Mode.Recover)pass.Box.PlaceholderText="";
  side.Visible=mode==Mode.SignIn;form.ResumeLayout();side.ResumeLayout();PerformLayout();Invalidate();}
 void Switch(Mode to){mode=to;Build();FocusFirst();}
 protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);var size=layout.GetPreferredSize(Size.Empty);card.Size=new Size(size.Width+S(64),size.Height+S(64));
  card.Location=new Point(Math.Max(S(16),(Width-card.Width)/2),Math.Max(S(64),(Height-card.Height)/2)+(int)(rise*S(36)));
  using var path=Aero.Round(new RectangleF(0,0,card.Width,card.Height),S(8));card.Region=new Region(path);}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var b=new LinearGradientBrush(ClientRectangle.IsEmpty?new Rectangle(0,0,1,1):ClientRectangle,Aero.Top,Aero.Steel,35f))g.FillRectangle(b,ClientRectangle);
  // Soft glowing rings behind the card, a nod to the 3DFx orb.
  for(int i=0;i<4;i++){float d=Math.Max(Width,Height)*(0.35f+i*0.22f);using var p=new Pen(Aero.C(0x66C0F4,22-i*4),S(28-i*5));g.DrawEllipse(p,Width*0.82f-d/2,Height*0.12f-d/2,d,d);}
  Aero.Orb(g,new RectangleF(S(24),S(18),S(32),S(32)));TextRenderer.DrawText(g,"3DFx Vista",Aero.Subtitle,new Point(S(64),S(22)),Aero.Bright,TextFormatFlags.NoPrefix);}

 void Mark(Field f,string text){var colon=text.IndexOf(':');var detail=colon>0&&colon<20?text[(colon+1)..].Trim():text;
  f.Label.Text=$"{f.Caption.ToUpperInvariant()} — {detail}";f.Label.ForeColor=Aero.Red;f.Frame.Problem=true;f.Box.Focus();f.Box.SelectAll();}
 bool CheckRelay(){try{RelayClient.ValidateUrl(relay.Box.Text);return true;}catch(IOException e){Mark(relay,e.Message);return false;}}
 bool CheckUser(){if(Accounts.ValidUsername(user.Box.Text.Trim()))return true;Mark(user,Accounts.UsernameRule);return false;}
 bool CheckNew(){if(!Protocol.ValidPassword(pass.Box.Text)){Mark(pass,Protocol.PasswordRule);return false;}if(pass.Box.Text!=again.Box.Text){Mark(again,"The two passwords are different.");return false;}return true;}
 // Runs one account request; the relay's answer shows above the button (or on the relay field when it can't be reached).
 async Task<AccountReply?> Call(Func<AccountClient,Task<AccountReply>> request){error.Visible=false;card.Enabled=false;UseWaitCursor=true;
  try{using var c=AccountClient.ForRelay(relay.Box.Text.Trim());return await request(c);}
  catch(IOException e){if(e is not AccountException&&(e.Message.Contains("reach")||e.Message.Contains("relay")))Mark(relay,e.Message);else ShowProblem(e.Message);return null;}
  catch(Exception e) when(e is HttpRequestException or TaskCanceledException){Mark(relay,"Could not reach the relay. Check its address and your internet.");return null;}
  finally{card.Enabled=true;UseWaitCursor=false;}}
 void Done(AccountReply r){if(r.RecoveryKey.Length>0&&FindForm() is {} owner)RecoveryKeyDialog.Show(owner,r.Username,r.RecoveryKey,mode==Mode.Recover);
  pass.Box.Clear();again.Box.Clear();key.Box.Clear();mode=Mode.SignIn;Build();SignedIn?.Invoke(r,keep.Checked,relay.Box.Text.Trim());}
 async Task SignIn(){if(!CheckRelay()||!CheckUser())return;if(pass.Box.TextLength==0){Mark(pass,"Enter your password.");return;}
  var r=await Call(c=>c.SignIn(user.Box.Text.Trim(),pass.Box.Text));if(r!=null)Done(r);else if(error.Visible){pass.Box.SelectAll();pass.Box.Focus();}}
 async Task Register(){if(!CheckRelay()||!CheckUser()||!CheckNew())return;var r=await Call(c=>c.SignUp(user.Box.Text.Trim(),pass.Box.Text));if(r!=null)Done(r);}
 async Task Recover(){if(!CheckRelay()||!CheckUser())return;if(!Accounts.ValidRecoveryKey(key.Box.Text)){Mark(key,"Recovery key: 20 letters and numbers, like ABCDE-FGHJK-MNPQR-STVWX.");return;}if(!CheckNew())return;
  var r=await Call(c=>c.Recover(user.Box.Text.Trim(),key.Box.Text,pass.Box.Text));if(r!=null)Done(r);}
 void AsGuest(){if(!CheckRelay())return;var n=guest.Box.Text.Trim();if(!Protocol.ValidName(n)){Mark(guest,Protocol.NameRule);return;}Guest?.Invoke(n,relay.Box.Text.Trim());}
 // Enter submits the visible form; in the guest name box it continues as a guest.
 protected override bool ProcessCmdKey(ref Message msg,Keys keyData){if(keyData==Keys.Enter&&Visible){if(guest.Box.Focused)AsGuest();else _=mode switch{Mode.Register=>Register(),Mode.Recover=>Recover(),_=>SignIn()};return true;}return base.ProcessCmdKey(ref msg,keyData);}
}
