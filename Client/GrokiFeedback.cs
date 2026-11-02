namespace FriendsChat;

// Feedback for Groki, like ChatGPT's: thumbs up / down on his replies (hover one of his messages), and now and then
// "How has your experience with Groki been so far?". Both go to Groki's service (only), which learns from them as it runs.
static class GrokiSurvey {
 // Asked once someone has seen this many of Groki's messages; "Not now" waits a few days before asking again.
 public const int After=12;public static readonly TimeSpan Wait=TimeSpan.FromDays(3);
 public static bool Due(int seen,bool done,long notBefore,long now)=>!done&&seen>=After&&now>=notBefore;
 public static readonly (string Face,string Label)[] Scale=[("😞","Terrible"),("🙁","Bad"),("😐","Okay"),("🙂","Good"),("😄","Great")];
}

// After a thumbs down: what was wrong (optional). Skip still sends the thumbs down.
sealed class FeedbackDialog:Modal {
 public string Note{get;private set;}="";
 static readonly string[] Reasons=["Didn't make sense","Off-topic","Rude or hurtful","Not true","Boring"];
 public FeedbackDialog():base("Feedback — 3DFx Vista","What was wrong with Groki's reply?","Groki learns from this. It goes to Groki only.")
 {
  var picks=Reasons.Select(r=>(r,AddSwitch(r,false))).ToList();var more=AddField("Anything else? (optional)",placeholder:"Tell Groki what a better reply would be",maxLength:300);
  AddCancel("Skip");var send=AddButton("Send",accent:true);AcceptButton=send;
  send.Click+=(_,_)=>{Note=string.Join("; ",picks.Where(p=>p.Item2.Checked).Select(p=>p.r).Append(more.Text.Trim()).Where(s=>s.Length>0));DialogResult=DialogResult.OK;};}
}

// "How has your experience with Groki been so far?": five faces and an optional comment.
sealed class SurveyDialog:Modal {
 public int Rating{get;private set;}
 public string Comment=>comment.Text.Trim();
 readonly TextBox comment;readonly List<Action> pickers=[];
 public SurveyDialog():base("Groki — 3DFx Vista","How has your experience with Groki been so far?","Your answer helps Groki get better. It goes to Groki only.")
 {
  var row=new TableLayoutPanel{ColumnCount=GrokiSurvey.Scale.Length,AutoSize=true,Margin=new Padding(0,S(4),0,0),BackColor=Aero.Bg,Anchor=AnchorStyles.None};
  var faces=new List<Label>();var font=new Font("Segoe UI Emoji",22f);
  FlatButton? send=null;
  for(int i=0;i<GrokiSurvey.Scale.Length;i++){var (face,label)=GrokiSurvey.Scale[i];int value=i+1;
   var cell=new TableLayoutPanel{ColumnCount=1,AutoSize=true,Margin=new Padding(S(4)),BackColor=Aero.Bg};
   var f=new Label{Text=face,Font=font,AutoSize=false,Size=new Size(S(68),S(56)),TextAlign=ContentAlignment.MiddleCenter,BackColor=Aero.Input,ForeColor=Aero.Bright,Cursor=Cursors.Hand,Margin=Padding.Empty};
   var l=new Label{Text=label,AutoSize=false,Size=new Size(S(68),S(22)),TextAlign=ContentAlignment.MiddleCenter,ForeColor=Aero.Muted,Font=Aero.Small,Margin=Padding.Empty};
   void Pick(){Rating=value;for(int k=0;k<faces.Count;k++)faces[k].BackColor=k==value-1?Aero.Accent:Aero.Input;if(send!=null)send.Enabled=true;}
   pickers.Add(Pick);f.Click+=(_,_)=>Pick();l.Click+=(_,_)=>Pick();f.MouseEnter+=(_,_)=>{if(Rating!=value)f.BackColor=Aero.Hover;};f.MouseLeave+=(_,_)=>{if(Rating!=value)f.BackColor=Aero.Input;};
   faces.Add(f);cell.Controls.Add(f);cell.Controls.Add(l);row.Controls.Add(cell,i,0);}
  Body.Controls.Add(row);
  comment=AddField("Anything you'd like to add? (optional)",placeholder:"What do you like, or what should Groki do better?",maxLength:500);
  AddCancel("Not now");send=AddButton("Send",accent:true);send.Enabled=false;AcceptButton=send;
  send.Click+=(_,_)=>{if(Rating>0)DialogResult=DialogResult.OK;};}
 // --preview only.
 internal void PreviewPick(int rating)=>pickers[rating-1]();
}
