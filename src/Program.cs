using System.Net.NetworkInformation;
using System.Net.Sockets;
namespace FriendsChat;
static class Program {
 [STAThread] static void Main(){ApplicationConfiguration.Initialize();Application.Run(new ChatForm());}
}
public sealed class ChatForm:Form {
 readonly TextBox name=new(){Text=Environment.UserName,MaxLength=32};
 readonly TextBox ip=new(){PlaceholderText="Host laptop's IPv4 address"};
 readonly TextBox code=new(){PlaceholderText="Room code"};
 readonly TextBox fingerprint=new(){PlaceholderText="Full host fingerprint — compare directly with host"};
 readonly TextBox details=new(){Multiline=true,ReadOnly=true,Height=175,ScrollBars=ScrollBars.Vertical};
 readonly TextBox history=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
 readonly TextBox message=new(){MaxLength=2000,PlaceholderText="Write a message (up to 2,000 characters)"};
 readonly Label status=new(){Text="Disconnected",AutoSize=true};
 readonly Button host=new(){Text="Host Chat",AutoSize=true}; readonly Button join=new(){Text="Join Chat",AutoSize=true};
 readonly Button leave=new(){Text="Disconnect / Stop",AutoSize=true,Enabled=false}; readonly Button send=new(){Text="Send",Enabled=false};
 Room? room; Client? client; bool busy;
 public ChatForm(){
  Text="Friends Chat • local encrypted prototype";MinimumSize=new Size(750,650);Size=new Size(900,780);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=2,RowCount=12};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  void Row(int r,string label,Control c){layout.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left},0,r);c.Dock=DockStyle.Fill;layout.Controls.Add(c,1,r);}
  Row(0,"Display name",name); Row(1,"Host IPv4",ip);Row(2,"Room code",code);Row(3,"SHA-256 fingerprint",fingerprint);
  var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};buttons.Controls.AddRange([host,join,leave]);layout.Controls.Add(buttons,1,4);
  Row(5,"Connection",status);Row(6,"Host details",details);
  var note=new Label{Text="Before joining, copy the room code and fingerprint directly from the host screen or a trusted channel. The host can read all messages.",AutoSize=true,MaximumSize=new Size(650,0)};layout.Controls.Add(note,1,7);
  Row(8,"Message history",history);layout.RowStyles.Clear();for(int n=0;n<12;n++)layout.RowStyles.Add(new RowStyle(n==8?SizeType.Percent:SizeType.AutoSize,n==8?100:0));
  Row(9,"Message",message);layout.Controls.Add(send,1,10);Controls.Add(layout);
  host.Click+=async(_,_)=>await Start(true);join.Click+=async(_,_)=>await Start(false);leave.Click+=(_,_)=>Disconnect();send.Click+=async(_,_)=>await Send();
  message.KeyDown+=async(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;await Send();}};
  FormClosing+=(_,_)=>Disconnect();
 }
 async Task Start(bool hosting){if(busy||client!=null)return;busy=true;host.Enabled=join.Enabled=false;status.Text="Connecting…";
  try{
   if(!Room.ValidName(name.Text.Trim()))throw new IOException("Enter a display name (1–32 characters).");
   if(hosting){room=new Room();details.Text="Share the IPv4 address of your hotspot Wi-Fi adapter:\r\n"+string.Join(", ",NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork).Select(a=>a.Address.ToString()))+"\r\nPort: "+Room.Port+"\r\nRoom code: "+room.Code+"\r\nSHA-256: "+room.Fingerprint;
    client=await Client.Connect("127.0.0.1",Room.Port,name.Text.Trim(),room.Code,room.Fingerprint);
   }else client=await Client.Connect(ip.Text.Trim(),Room.Port,name.Text.Trim(),code.Text,fingerprint.Text);
   status.Text=hosting?"Hosting • TLS encrypted • stop to close room":"Connected • TLS encrypted • host fingerprint verified";leave.Enabled=send.Enabled=true;name.Enabled=false;history.Clear();_=Receive(client);
  }catch(Exception e){Disconnect();MessageBox.Show(this,e.Message,"Unable to connect",MessageBoxButtons.OK,MessageBoxIcon.Information);}finally{busy=false;}
 }
 async Task Receive(Client current){try{while(true){var p=await current.Read();if(p.Kind=="message"){history.AppendText($"[{DateTime.Now:HH:mm}] {p.Name}: {p.Text.Replace("\r"," ").Replace("\n"," ")}\r\n");if(history.TextLength>200000)history.Text=history.Text[^100000..];}}}catch{if(client==current){Disconnect();status.Text="Disconnected — host stopped or connection lost";}}}
 async Task Send(){var c=client;if(c==null||string.IsNullOrWhiteSpace(message.Text))return;try{string text=message.Text;message.Clear();await c.Send(text);}catch{if(client==c)Disconnect();}}
 void Disconnect(){var c=client;client=null;c?.Dispose();room?.Dispose();room=null;send.Enabled=leave.Enabled=false;host.Enabled=join.Enabled=name.Enabled=true;status.Text="Disconnected";details.Clear();history.Clear();}
}

