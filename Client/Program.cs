using System.Net.WebSockets;
namespace FriendsChat;
static class Program {
 [STAThread] static void Main(){ApplicationConfiguration.Initialize();Application.Run(new ChatForm());}
}
public sealed class ChatForm:Form {
 readonly TextBox relay=new(){PlaceholderText="https://your-relay.onrender.com"};
 readonly TextBox name=new(){Text=Environment.UserName,MaxLength=32};
 readonly TextBox room=new(){PlaceholderText="e.g. friday-friends",MaxLength=32};
 readonly TextBox password=new(){UseSystemPasswordChar=true,MaxLength=128,PlaceholderText="Your room password (at least 10 characters)"};
 readonly TextBox history=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.White,Dock=DockStyle.Fill};
 readonly TextBox message=new(){MaxLength=2000,PlaceholderText="Write a message…",Dock=DockStyle.Fill};
 readonly Label status=new(){Text="Ready",AutoSize=true,ForeColor=Color.Navy};
 readonly Button create=new(){Text="Create room",AutoSize=true};readonly Button join=new(){Text="Join room",AutoSize=true};readonly Button leave=new(){Text="Disconnect",AutoSize=true,Enabled=false};readonly Button send=new(){Text="Send",Dock=DockStyle.Fill,Enabled=false};
 RelayClient? client;bool closing;
 public ChatForm(){
  Text="Friends Chat 0.3";Size=new Size(900,760);MinimumSize=new Size(650,600);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(243,246,250);
  var main=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=2,RowCount=10};main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125));main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));for(int i=0;i<10;i++)main.RowStyles.Add(new RowStyle(i==7?SizeType.Percent:SizeType.AutoSize,i==7?100:0));
  void Row(int n,string label,Control c){main.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left},0,n);c.Dock=DockStyle.Fill;c.Margin=new Padding(3,5,3,5);main.Controls.Add(c,1,n);}
  Row(0,"Relay address",relay);Row(1,"Display name",name);Row(2,"Room name",room);Row(3,"Password",password);
  var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};buttons.Controls.AddRange([create,join,leave]);Row(4,"",buttons);
  var showPassword=new CheckBox{Text="Show password",AutoSize=true};showPassword.CheckedChanged+=(_,_)=>password.UseSystemPasswordChar=!showPassword.Checked;Row(5,"",showPassword);
  Row(6,"Connection",status);Row(7,"Messages",history);
  var composer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,AutoSize=true};composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,80));composer.Controls.Add(message,0,0);composer.Controls.Add(send,1,0);Row(8,"Message",composer);
  Row(9,"",new Label{Text="Use the same trusted HTTPS relay, room name and password. The relay can read messages. No messages are saved by this app.",AutoSize=true,MaximumSize=new Size(640,0)});Controls.Add(main);
  create.Click+=async(_,_)=>await Connect(true);join.Click+=async(_,_)=>await Connect(false);leave.Click+=(_,_)=>Disconnect();send.Click+=async(_,_)=>await Send();message.KeyDown+=async(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;await Send();}};FormClosing+=(_,_)=>{closing=true;Disconnect();};
 }
 async Task Connect(bool creating){if(client!=null)return;var current=new RelayClient();client=current;create.Enabled=join.Enabled=false;leave.Enabled=true;status.Text="Connecting… (a sleeping relay can take a minute)";
  try{await current.Connect(relay.Text,creating,room.Text.Trim(),password.Text,name.Text.Trim());if(client!=current||closing){current.Dispose();return;}status.Text=creating?"Room created • encrypted HTTPS connection":"Joined • encrypted HTTPS connection";history.Clear();send.Enabled=true;relay.Enabled=name.Enabled=room.Enabled=password.Enabled=false;_=Receive(current);}
  catch(Exception e){if(client==current&&!closing){Disconnect();status.Text="Connection failed";MessageBox.Show(this,Friendly(e),"Friends Chat",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
 }
 static string Friendly(Exception e)=>e switch{OperationCanceledException=>"Connection timed out. Check the relay address and internet access.",WebSocketException=>"Could not connect to the HTTPS relay. Check its address, deployment status and device network policy. Keep certificate verification enabled.",_=>e.Message};
 async Task Receive(RelayClient current){try{while(true){var p=await current.Read();if(p==null)break;if(p.Kind=="message"){history.AppendText($"[{DateTime.Now:HH:mm}] {p.Name}: {p.Text.Replace("\r"," ").Replace("\n"," ")}\r\n");if(history.TextLength>200000)history.Text=history.Text[^100000..];}else if(p.Kind=="error")status.Text=p.Text;}}catch{}finally{if(!closing&&client==current){Disconnect();status.Text="Disconnected — room creator left, relay restarted, or connection lost";}}}
 async Task Send(){var current=client;if(current==null||string.IsNullOrWhiteSpace(message.Text))return;try{var text=message.Text;await current.Send(text);if(client==current)message.Clear();}catch{if(!closing&&client==current)Disconnect();}}
 void Disconnect(){var old=client;client=null;old?.Dispose();if(closing)return;create.Enabled=join.Enabled=true;leave.Enabled=send.Enabled=false;relay.Enabled=name.Enabled=room.Enabled=password.Enabled=true;status.Text="Disconnected";history.Clear();message.Clear();password.Clear();}
}
