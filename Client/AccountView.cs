using System.Security.Cryptography;
using System.Text;
namespace FriendsChat;

// "Keep me signed in": the sign-in token is encrypted with Windows (DPAPI) so only this Windows user can read it back.
static class SessionVault {
 static readonly byte[] purpose=Encoding.UTF8.GetBytes("3DFx Vista session");
 public static string Protect(string token)=>Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token),purpose,DataProtectionScope.CurrentUser));
 public static string? Unprotect(string stored){try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored),purpose,DataProtectionScope.CurrentUser));}catch(Exception e) when(e is CryptographicException or FormatException){return null;}}
}

// Room passwords for "Reopen my rooms", encrypted with Windows (DPAPI) so only this Windows user can read them back.
static class RoomVault {
 static readonly byte[] purpose=Encoding.UTF8.GetBytes("3DFx Vista rooms");
 public static string Protect(string password)=>Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password),purpose,DataProtectionScope.CurrentUser));
 public static string? Unprotect(string stored){try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored),purpose,DataProtectionScope.CurrentUser));}catch(Exception e) when(e is CryptographicException or FormatException){return null;}}
}

// Shows a new recovery key once, with Copy and Save, and asks the person to confirm they kept it.
sealed class RecoveryKeyDialog:Modal {
 RecoveryKeyDialog(string username,string key,bool replaced):base("Recovery key — 3DFx Vista","Save your recovery key",
  (replaced?"Your old recovery key no longer works. ":"")+"If you forget your password, this key lets you set a new one. It is shown only this once. Keep it somewhere safe, such as a password manager or a printed page.")
 {
  var box=new InputBox{Width=Width400,Height=S(52),Margin=new Padding(0,S(6),0,S(8))};box.Box.Text=key;box.Box.ReadOnly=true;box.Box.Font=new Font("Consolas",16f,FontStyle.Bold);box.Box.TextAlign=HorizontalAlignment.Center;box.Box.BackColor=box.BackColor;
  Body.Controls.Add(box);
  var tools=new FlowLayoutPanel{AutoSize=true,Margin=Padding.Empty,BackColor=Aero.Bg};
  var copy=new FlatButton{Text="Copy",Kind=ButtonKind.Secondary,Margin=new Padding(0,0,S(8),0)};copy.Size=copy.GetPreferredSize(Size.Empty);copy.Click+=(_,_)=>{Clipboard.SetText(key);copy.Text="Copied";};
  var save=new FlatButton{Text="Save as text file…",Kind=ButtonKind.Secondary,Margin=Padding.Empty};save.Size=save.GetPreferredSize(Size.Empty);
  save.Click+=(_,_)=>{using var f=new SaveFileDialog{FileName=$"3DFx recovery key ({username}).txt",Filter="Text file|*.txt"};
   if(f.ShowDialog(this)==DialogResult.OK)try{File.WriteAllText(f.FileName,$"3DFx Vista recovery key\r\nUsername: {username}\r\nRecovery key: {key}\r\n");save.Text="Saved";}catch(Exception e) when(e is IOException or UnauthorizedAccessException){ShowError("Could not save: "+e.Message);}};
  tools.Controls.AddRange([copy,save]);Body.Controls.Add(tools);
  var saved=AddSwitch("I have saved my recovery key",false);
  var done=AddButton("Done",accent:true);done.Enabled=false;saved.CheckedChanged+=(_,_)=>done.Enabled=saved.Checked;done.Click+=(_,_)=>Close();AcceptButton=done;
  FormClosing+=(_,e)=>{if(!saved.Checked&&e.CloseReason==CloseReason.UserClosing&&MessageBox.Show(this,"Close without saving your recovery key? You won't see it again.","3DFx Vista",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)e.Cancel=true;};}
 public static void Show(IWin32Window owner,string username,string key,bool replaced){using var d=new RecoveryKeyDialog(username,key,replaced);d.ShowDialog(owner);}
}

// "Join or create a room": room name and password. Join is the main action; Create starts a new room with that name.
// The connection itself is run by the caller (Connect returns null when it worked, else the problem); problems mark only the field they are about.
sealed class RoomDialog:Modal {
 public RoomDialog(Func<bool,string,string,Task<string?>> connect,string room=""):base("Join a room — 3DFx Vista","Join or create a room","Rooms are private: anyone with the name and password can join. A room ends when everyone has left.")
 {
  var name=AddField("Room name",placeholder:"e.g. friday-friends",maxLength:32);name.Text=room;var pass=AddField("Room password",secret:true,placeholder:"At least 10 characters");
  var show=AddSwitch("Show password",false);show.CheckedChanged+=(_,_)=>pass.UseSystemPasswordChar=!show.Checked;
  AddCancel();var create=AddButton("Create room");var join=AddButton("Join room",accent:true);AcceptButton=join;
  async Task Go(bool creating){ShowError(null);name.Text=Protocol.NormalizeRoom(name.Text);Busy(true);string? problem;
   try{problem=await connect(creating,name.Text,pass.Text);}finally{if(!IsDisposed)Busy(false);}
   if(problem==null){DialogResult=DialogResult.OK;return;}
   ShowError(problem,problem.StartsWith(Protocol.RoomRule)||problem.StartsWith("Room name or")?name:problem.StartsWith(Protocol.PasswordRule)?pass:null);}
  join.Click+=async(_,_)=>await Go(false);create.Click+=async(_,_)=>await Go(true);
  Shown+=(_,_)=>(name.Text.Length>0?pass:name).Focus();}
}
