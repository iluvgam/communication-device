using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
namespace FriendsChat;
static class Program {
 [STAThread] static void Main(){ApplicationConfiguration.Initialize();Application.Run(new ChatForm());}
}
public sealed class ChatForm:Form {
 readonly TextBox name=new(){Text=Environment.UserName,MaxLength=32};
 readonly TextBox ip=new(){PlaceholderText="Example: 172.20.10.7"};
 readonly TextBox code=new(){PlaceholderText="Filled by invitation",MaxLength=32};
 readonly TextBox fingerprint=new(){PlaceholderText="Full SHA-256 fingerprint",MaxLength=100};
 readonly TextBox invite=new(){PlaceholderText="Paste the host's invitation here",MaxLength=2048};
 readonly ComboBox adapters=new(){DropDownStyle=ComboBoxStyle.DropDownList};
 readonly TextBox history=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.White};
 readonly TextBox message=new(){MaxLength=2000,PlaceholderText="Write a message…",Dock=DockStyle.Fill};
 readonly Label status=new(){Text="Ready — host a room or find a friend",AutoSize=true,ForeColor=Color.Navy};
 readonly Label discoveryStatus=new(){Text="Discovery is local and unverified.",AutoSize=true};
 readonly Label hostInfo=new(){Text="Choose your hotspot Wi-Fi adapter above.",AutoSize=true};
 readonly CheckBox verified=new(){Text="I got this invitation directly from the host through a trusted channel, or compared the full fingerprint.",AutoSize=true};
 readonly ListBox nearby=new(){Height=65};
 readonly Button host=new(){Text="Host Chat",AutoSize=true}; readonly Button join=new(){Text="Join Chat",AutoSize=true};
 readonly Button find=new(){Text="Find local chats",AutoSize=true};readonly Button check=new(){Text="Check connection",AutoSize=true};
 readonly Button copy=new(){Text="Copy invitation",AutoSize=true,Enabled=false};
 readonly Button leave=new(){Text="Disconnect / Stop",AutoSize=true,Enabled=false}; readonly Button send=new(){Text="Send",Enabled=false,Dock=DockStyle.Fill};
 Room? room; Client? client; DiscoveryHost? discovery; bool busy,closing; string hostName="";
 readonly List<(string Label,IPAddress Address,IPAddress Broadcast)> interfaces=LocalDiscovery.Interfaces();
 public ChatForm(){
  Text="Friends Chat 0.2 • local rooms";MinimumSize=new Size(940,700);Size=new Size(1100,850);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(242,245,250);
  var main=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=2,RowCount=2};main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,420));main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));main.RowStyles.Add(new RowStyle(SizeType.AutoSize));main.RowStyles.Add(new RowStyle(SizeType.Percent,100));
  status.Padding=new Padding(0,0,0,12);main.Controls.Add(status,0,0);main.SetColumnSpan(status,2);
  var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(0,0,12,0)};
  var setup=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(0,0,8,12)};setup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scroll.Controls.Add(setup);main.Controls.Add(scroll,0,1);
  void Add(Control c){c.Dock=DockStyle.Top;c.Margin=new Padding(0,3,0,5);setup.Controls.Add(c);}
  void Label(string s,bool bold=false){Add(new Label{Text=s,AutoSize=true,Font=new Font(Font,bold?FontStyle.Bold:FontStyle.Regular)});}
  FlowLayoutPanel Buttons(params Control[] cs){var f=new FlowLayoutPanel{AutoSize=true,WrapContents=true};f.Controls.AddRange(cs);return f;}
  Label("YOUR NAME",true);Add(name);Label("HOST A ROOM",true);Add(adapters);Add(Buttons(host,copy,leave));Add(hostInfo);
  Label("JOIN A FRIEND",true);Add(Buttons(find,check));Add(nearby);Add(discoveryStatus);
  Label("Have an invitation? Paste it here:");Add(invite);var roomDetails=new Button{Text="Show room details",AutoSize=true};Add(roomDetails);roomDetails.Click+=(_,_)=>{if(room!=null)Show($"Host IPv4: {SelectedAddress()}\nPort: {Room.Port}\nRoom code: {room.Code}\nSHA-256 fingerprint:\n{room.Fingerprint}");else Show("Start Host Chat to see room details.");};var apply=new Button{Text="Use invitation",AutoSize=true};Add(apply);
  verified.MaximumSize=new Size(370,0);Add(verified);Add(join);
  var advanced=new CheckBox{Text="Show address / manual entry",AutoSize=true};Add(advanced);
  var manual=new TableLayoutPanel{AutoSize=true,ColumnCount=1,Visible=false};manual.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  foreach(var c in new Control[]{new Label{Text="Host IPv4 address",AutoSize=true},ip,new Label{Text="Room code",AutoSize=true},code,new Label{Text="Host fingerprint (SHA-256)",AutoSize=true},fingerprint}){c.Dock=DockStyle.Top;manual.Controls.Add(c);}Add(manual);advanced.CheckedChanged+=(_,_)=>manual.Visible=advanced.Checked;
  Label("Discovery only finds addresses. It does not prove host identity or share the room code. TLS protects messages; the host can read them.");
  var chat=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Padding=new Padding(12,0,0,0)};chat.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));chat.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,80));chat.RowStyles.Add(new RowStyle(SizeType.AutoSize));chat.RowStyles.Add(new RowStyle(SizeType.Percent,100));chat.RowStyles.Add(new RowStyle(SizeType.AutoSize));
  chat.Controls.Add(new Label{Text="ROOM CONVERSATION",Font=new Font(Font,FontStyle.Bold),AutoSize=true},0,0);history.Dock=DockStyle.Fill;chat.Controls.Add(history,0,1);chat.SetColumnSpan(history,2);chat.Controls.Add(message,0,2);chat.Controls.Add(send,1,2);main.Controls.Add(chat,1,1);Controls.Add(main);
  foreach(var i in interfaces)adapters.Items.Add(i.Label);if(adapters.Items.Count>0)adapters.SelectedIndex=0;
  host.Click+=async(_,_)=>await Start(true);join.Click+=async(_,_)=>await Start(false);leave.Click+=(_,_)=>Disconnect();send.Click+=async(_,_)=>await Send();
  find.Click+=async(_,_)=>await Find();check.Click+=async(_,_)=>await Check();copy.Click+=(_,_)=>CopyInvite();apply.Click+=(_,_)=>ApplyInvite();
  nearby.SelectedIndexChanged+=(_,_)=>{if(nearby.SelectedItem is Nearby n){ip.Text=n.Address;verified.Checked=false;discoveryStatus.Text="Address selected. Get an invitation from the host to verify it.";}};
  ip.TextChanged+=(_,_)=>verified.Checked=false;code.TextChanged+=(_,_)=>verified.Checked=false;fingerprint.TextChanged+=(_,_)=>verified.Checked=false;
  message.KeyDown+=async(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;await Send();}};
  adapters.SelectedIndexChanged+=(_,_)=>UpdateHostInfo();FormClosing+=(_,_)=>{closing=true;Disconnect();};
 }
 void UpdateHostInfo(){hostInfo.Text=room==null?"Choose your hotspot Wi-Fi adapter above.":$"Hosting on {SelectedAddress()} • TCP {Room.Port}\nDiscovery: {(discovery==null?"unavailable — invitation still works":"active on UDP 45832")}\nCopy an invitation and send it privately to your friend.";}
 string SelectedAddress()=>adapters.SelectedIndex>=0?interfaces[adapters.SelectedIndex].Address.ToString():"";
 void CopyInvite(){if(room==null)return;try{if(string.IsNullOrEmpty(SelectedAddress()))throw new IOException("No local IPv4 adapter is available.");Clipboard.SetText(new Invitation(SelectedAddress(),room.Code,room.Fingerprint).Encode());status.Text="Invitation copied — send it privately through a trusted channel";}catch(Exception e){Show(e.Message);}}
 void ApplyInvite(){try{var i=Invitation.Parse(invite.Text);ip.Text=i.Address;code.Text=i.Code;fingerprint.Text=i.Fingerprint;verified.Checked=false;status.Text="Invitation loaded. Confirm its trusted source below, then Join Chat.";}catch(Exception e){Show(e.Message);}}
 async Task Find(){find.Enabled=false;nearby.Items.Clear();discoveryStatus.Text="Looking for local rooms for 3 seconds…";try{var rooms=await LocalDiscovery.Find();if(closing)return;foreach(var r in rooms)nearby.Items.Add(r);discoveryStatus.Text=rooms.Count==0?"No rooms found. Try an invitation; UDP discovery may be blocked.":$"{rooms.Count} room(s) found — select one, then use a trusted invitation.";}catch(Exception e){if(!closing)discoveryStatus.Text="Discovery unavailable: "+e.Message;}finally{if(!closing)find.Enabled=true;}}
 async Task Check(){check.Enabled=false;try{bool reachable=await LocalDiscovery.Check(ip.Text.Trim());if(!closing)status.Text=reachable?"TCP port reachable. Join Chat will verify TLS identity and room code.":"Cannot reach host TCP 45831. Check IP, hosting, Norton/firewall, or hotspot filtering.";}finally{if(!closing)check.Enabled=true;}}
 async Task Start(bool hosting){if(busy||client!=null||room!=null)return;if(!hosting&&!verified.Checked){Show("First confirm the invitation came directly from the host through a trusted channel, or compare the full fingerprint with the host.");return;}busy=true;host.Enabled=join.Enabled=false;status.Text="Connecting securely…";
  try{
   if(!Room.ValidName(name.Text.Trim()))throw new IOException("Enter a display name (1–32 characters).");
   Client? connected=null;
   if(hosting){room=new Room();hostName=name.Text.Trim();room.Message+=p=>{if(!closing&&IsHandleCreated)BeginInvoke((Action)(()=>{if(room!=null&&!closing)Append(p);}));};try{discovery=new DiscoveryHost(name.Text.Trim(),room.Fingerprint);}catch(SocketException){discovery=null;}UpdateHostInfo();}
   else connected=await Client.Connect(ip.Text.Trim(),Room.Port,name.Text.Trim(),code.Text,fingerprint.Text);
   if(closing){connected?.Dispose();return;}client=connected;status.Text=hosting?"Hosting • TLS encrypted • copy an invitation for your friend":"Connected • TLS encrypted • host fingerprint verified";leave.Enabled=send.Enabled=true;copy.Enabled=hosting;name.Enabled=false;history.Clear();if(connected!=null)_=Receive(connected);
  }catch(Exception e){if(!closing){Disconnect();Show(Friendly(e));}}finally{busy=false;if(!closing&&client==null)host.Enabled=join.Enabled=true;}
 }
 static string Friendly(Exception e)=>e switch{
  OperationCanceledException=>"Connection timed out. Check the host IP, that Hosting is active, and Norton/firewall or hotspot filtering. Try Check connection.",
  AuthenticationException=>"Host certificate verification failed. Get a fresh trusted invitation. Security software such as Norton Web/Mail Shield may be replacing the certificate; an authorized administrator must review compatibility. Do not bypass verification.",
  SocketException s when s.SocketErrorCode==SocketError.ConnectionRefused=>"The host refused TCP 45831. Check that Host Chat is running at this IP and that the host firewall permits this app.",
  SocketException=>"The host network could not be reached. Check that both laptops use the same hotspot and the correct Wi-Fi IPv4 address.",
  FormatException=>"Enter an IPv4 address such as 172.20.10.7, or use an invitation. 45831 is the port, not the address.",
  _=>e.Message};
 void Show(string s)=>MessageBox.Show(this,s,"Friends Chat",MessageBoxButtons.OK,MessageBoxIcon.Information);
 async Task Receive(Client current){try{while(true){var p=await current.Read();if(p.Kind=="message")Append(p);}}catch{if(!closing&&client==current){Disconnect();status.Text="Disconnected — host stopped or connection lost";}}}
 void Append(Packet p){history.AppendText($"[{DateTime.Now:HH:mm}] {p.Name}: {p.Text.Replace("\r"," ").Replace("\n"," ")}\r\n");if(history.TextLength>200000)history.Text=history.Text[^100000..];}
 async Task Send(){var c=client;if((c==null&&room==null)||string.IsNullOrWhiteSpace(message.Text))return;try{string text=message.Text;message.Clear();if(room!=null)await room.Post(hostName,text);else if(c!=null)await c.Send(text);}catch{if(!closing&&client==c)Disconnect();}}
 void Disconnect(){var c=client;client=null;c?.Dispose();discovery?.Dispose();discovery=null;room?.Dispose();room=null;if(closing)return;send.Enabled=leave.Enabled=copy.Enabled=false;host.Enabled=join.Enabled=name.Enabled=true;status.Text="Disconnected";UpdateHostInfo();history.Clear();message.Clear();invite.Clear();verified.Checked=false;}
}


