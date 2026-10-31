using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace FriendsChat;
// Id/Seq/Total/Data are used by media packets, Token only by create/join and the DM hello, Type by DMs and room history.
// Id and Time also mark each room message (set by the relay); Reply is the id of the message being answered (or changed, in DMs);
// Edited marks an edited message in room history. Older relays and clients ignore unknown fields.
public record Packet(string Kind,string Room="",string Password="",string Name="",string Text="",string Id="",int Seq=0,long Total=0,string Data="",string Token="",string Type="",long Time=0,string Reply="",bool Edited=false);
// Reactions on one message: each emoji (E) with who used it (N), in the order they were first used.
public sealed record Reaction(string E,List<string> N){
 public const int MaxKinds=8,MaxTotal=48;
 // One emoji (flags and skin tones included), not words or spaces.
 public static bool Valid(string? s)=>s is {Length:>0 and <=16}&&s.All(c=>c>0x7F&&!char.IsWhiteSpace(c)&&!char.IsControl(c)&&!char.IsLetterOrDigit(c));
 public static string Format(List<Reaction> list)=>JsonSerializer.Serialize(list);
 public static List<Reaction> Parse(string? json){try{return (JsonSerializer.Deserialize<List<Reaction>>(json??"")??[]).Where(r=>r?.E!=null&&r.N!=null).ToList();}catch(JsonException){return [];}}
}
// One row of your contact list. State: contact, incoming (they asked you), outgoing (you asked them) or blocked.
// Online, Presence (online/away/busy/offline), Mood (status line) and Avatar (picture id) are only filled in for accepted contacts.
// Bot marks an AI member (Bryan), so apps can label it.
public sealed record ContactInfo(string Name,string State,bool Online,string Presence="",string Mood="",string Avatar="",bool Bot=false){
 public static List<ContactInfo> Parse(string json){try{return JsonSerializer.Deserialize<List<ContactInfo>>(json)??[];}catch(JsonException){return [];}}
}
// Accounts live on the relay: unique username, password (same rules as room passwords) and a one-time recovery key.
public static class Accounts {
 public const string UsernameRule="Username: 3–20 letters, numbers, - or _.",GuestSuffix=" (guest)";
 public static bool ValidUsername(string? s)=>s!=null&&s.Length>=3&&s.Length<=20&&s.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_');
 // Recovery keys: 20 characters from a 32-letter alphabet without look-alikes (100 random bits), shown as ABCDE-FGHJK-MNPQR-STVWX.
 const string Alphabet="0123456789ABCDEFGHJKMNPQRSTVWXYZ";
 public static string NewRecoveryKey(){var b=RandomNumberGenerator.GetBytes(20);var c=b.Select(x=>Alphabet[x&31]).ToArray();return $"{new string(c,0,5)}-{new string(c,5,5)}-{new string(c,10,5)}-{new string(c,15,5)}";}
 // Typing slips are forgiven: case, spaces and dashes are ignored, and O/I/L read as 0/1/1.
 public static string NormalizeRecoveryKey(string? key)=>new((key??"").ToUpperInvariant().Where(c=>c is not ('-' or ' ')).Select(c=>c switch{'O'=>'0','I' or 'L'=>'1',_=>c}).ToArray());
 public static bool ValidRecoveryKey(string? key){var k=NormalizeRecoveryKey(key);return k.Length==20&&k.All(Alphabet.Contains);}
}
public sealed record AccountReply(string Username="",string Token="",string RecoveryKey="",string Error="");
public sealed class AccountException(string message,int status):IOException(message){public int Status{get;}=status;}
// Profiles: chosen presence, a short status line and a small picture (JPEG or PNG, one packet).
// "invisible" (Appear offline) shows you as offline to contacts while you stay connected.
public static class Profiles {
 public const int MaxMood=80,MaxAvatar=9216;
 public static readonly string[] Presences=["online","away","busy","invisible"];
 public static bool ValidMood(string? s)=>s!=null&&s.Length<=MaxMood&&!s.Any(char.IsControl);
 public static bool ValidAvatar(byte[] b)=>b.Length is >0 and <=MaxAvatar&&(b is [0xFF,0xD8,..]||b is [0x89,0x50,0x4E,0x47,..]);
 public static string Label(string presence)=>presence switch{"online"=>"Online","away"=>"Away","busy"=>"Busy","invisible"=>"Appear offline",_=>"Offline"};
}
// The signed-in "online" connection: contacts, presence and direct messages (DMs).
// DMs to offline contacts are held on the relay until one of the recipient's apps confirms it saved them (Ack).
public sealed class DirectClient:IDisposable {
 readonly ClientWebSocket socket=new(){Options={KeepAliveInterval=TimeSpan.FromSeconds(15),KeepAliveTimeout=TimeSpan.FromSeconds(15)}};readonly SemaphoreSlim send=new(1);readonly CancellationTokenSource stop=new();
 public static Uri UrlFor(string relayUrl)=>new UriBuilder(RelayClient.ValidateUrl(relayUrl)){Path="/dm"}.Uri;
 public async Task Connect(Uri url,string token){
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(60));
  // "edits": this app understands replies, edits, deletes and reactions, so the relay may send them.
  await socket.ConnectAsync(url,timeout.Token);await Protocol.Write(socket,new Packet("hello",Token:token,Text:"edits"),timeout.Token);
  var reply=await Protocol.Read(socket,timeout.Token);if(reply?.Kind!="ready")throw new IOException(reply?.Text??"Relay disconnected.");Features=reply.Text.Split(' ',StringSplitOptions.RemoveEmptyEntries);}
 // From the relay's "ready" ("direct", plus "edits" from 0.14).
 public string[] Features{get;private set;}=[];
 public Task<Packet?> Read()=>Protocol.Read(socket,stop.Token);
 public Task Contact(string action,string username)=>Write(new Packet("contact-"+action,Name:username));
 public async Task<string> SendText(string to,string text,string reply=""){if(string.IsNullOrWhiteSpace(text)||text.Length>2000)throw new IOException("Message must be 1–2,000 characters.");var id=Media.NewId();await Write(new Packet("dm",Name:to,Text:text,Id:id,Type:"text",Reply:reply));return id;}
 public async Task<string> SendSticker(string to,string sticker,string reply=""){var id=Media.NewId();await Write(new Packet("dm",Name:to,Text:sticker,Id:id,Type:"sticker",Reply:reply));return id;}
 // A change to an earlier DM: "edit" (new text), "delete", or "react" (an emoji; sending it again takes it back). Delivered like a message.
 public async Task<string> SendChange(string to,string type,string target,string text=""){var id=Media.NewId();await Write(new Packet("dm",Name:to,Text:text,Id:id,Type:type,Reply:target));return id;}
 // Pictures only (up to 5 MB), in the same pieces as room media; each piece takes the send lock on its own.
 public async Task<string> SendPicture(string to,byte[] data,string mime,IProgress<int>? progress=null){
  if(!Media.IsImage(mime)||data.Length==0||data.Length>Media.MaxImage)throw new IOException("Pictures can be up to 5 MB.");
  var id=Media.NewId();await Write(new Packet("dm-picture",Name:to,Text:mime,Id:id,Total:data.Length));
  for(int seq=0,offset=0;offset<data.Length;seq++,offset+=Media.ChunkBytes){
   await Write(new Packet("dm-chunk",Id:id,Seq:seq,Data:Convert.ToBase64String(data,offset,Math.Min(Media.ChunkBytes,data.Length-offset))));
   progress?.Report((int)(100L*Math.Min(data.Length,offset+Media.ChunkBytes)/data.Length));}
  await Write(new Packet("dm-end",Id:id));return id;}
 public Task Ack(string id)=>Write(new Packet("dm-ack",Id:id));
 // Profile and receipts. Typing and Seen go only to accepted contacts, and only live (never stored).
 public Task SetProfile(string presence,string mood)=>Write(new Packet("profile",Type:presence,Text:mood));
 public Task SetAvatar(byte[]? picture)=>Write(new Packet("avatar",Data:picture==null?"":Convert.ToBase64String(picture)));
 public Task GetAvatar(string username)=>Write(new Packet("avatar-get",Name:username));
 public Task Typing(string to)=>Write(new Packet("dm-typing",Name:to));
 public Task Seen(string from,string lastId)=>Write(new Packet("dm-seen",Name:from,Id:lastId));
 async Task Write(Packet p){await send.WaitAsync(stop.Token);try{await Protocol.Write(socket,p,stop.Token);}finally{send.Release();}}
 public async Task Close(){try{using var t=new CancellationTokenSource(TimeSpan.FromSeconds(1.5));if(socket.State==WebSocketState.Open)await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,null,t.Token).ConfigureAwait(false);}catch(Exception e) when(e is WebSocketException or OperationCanceledException or ObjectDisposedException){}finally{Dispose();}}
 public void Dispose(){stop.Cancel();socket.Abort();socket.Dispose();}
}
// Talks to the relay's /account endpoints. Production use goes through ForRelay, which only accepts the relay's HTTPS address.
public sealed class AccountClient:IDisposable {
 readonly HttpClient http;
 internal AccountClient(Uri baseAddress){http=new HttpClient{BaseAddress=baseAddress,Timeout=TimeSpan.FromSeconds(90)};}
 public static AccountClient ForRelay(string relayUrl)=>new(new UriBuilder(RelayClient.ValidateUrl(relayUrl)){Scheme="https",Path="/"}.Uri);
 public Task<AccountReply> SignUp(string username,string password)=>Post("account/signup",new{username,password});
 public Task<AccountReply> SignIn(string username,string password)=>Post("account/signin",new{username,password});
 public Task<AccountReply> Recover(string username,string recoveryKey,string newPassword)=>Post("account/recover",new{username,recoveryKey,newPassword});
 public Task<AccountReply> Me(string token)=>Post("account/me",new{},token);
 public Task<AccountReply> SignOut(string token)=>Post("account/signout",new{},token);
 public Task<AccountReply> ChangePassword(string token,string password,string newPassword)=>Post("account/password",new{password,newPassword},token);
 public Task<AccountReply> NewRecoveryKey(string token,string password)=>Post("account/recovery-key",new{password},token);
 public Task<AccountReply> Delete(string token,string password)=>Post("account/delete",new{password},token);
 // For the operator's bot service only: signs in a reserved bot account with the relay's BotKey (creating it the first time).
 public Task<AccountReply> BotSignIn(string username,string botKey)=>Post("account/bot",new{username,password=botKey});
 async Task<AccountReply> Post(string path,object body,string? token=null){
  using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};
  if(token!=null)request.Headers.Authorization=new("Bearer",token);
  HttpResponseMessage response;
  try{response=await http.SendAsync(request);}catch(Exception e) when(e is HttpRequestException or TaskCanceledException){throw new AccountException("Could not reach the relay. Check its address and your internet.",0);}
  using(response){
   AccountReply? reply=null;try{reply=await response.Content.ReadFromJsonAsync<AccountReply>();}catch(Exception e) when(e is JsonException or NotSupportedException or InvalidOperationException){}
   if(response.IsSuccessStatusCode&&reply!=null)return reply;
   var status=(int)response.StatusCode;
   throw new AccountException(reply is {Error.Length:>0}?reply.Error:status==429?"Too many tries. Wait a minute and try again.":status==404?"This relay does not support accounts yet.":$"The relay could not do that (error {status}).",status);}}
 public void Dispose()=>http.Dispose();
}
// Images and videos travel as media-start, numbered media-chunk packets (base64, under the 16 KiB packet limit), then media-end.
// The relay checks and forwards each piece to the others; it never stores media.
public static class Media {
 public const int ChunkBytes=9216;
 public const long MaxImage=5L*1024*1024,MaxVideo=25L*1024*1024;
 public static readonly string[] ImageTypes=["image/jpeg","image/png","image/gif"],VideoTypes=["video/mp4","video/webm"];
 public static bool IsImage(string? mime)=>mime!=null&&ImageTypes.Contains(mime);
 public static bool IsVideo(string? mime)=>mime!=null&&VideoTypes.Contains(mime);
 public static long Limit(string? mime)=>IsImage(mime)?MaxImage:IsVideo(mime)?MaxVideo:0;
 public static bool ValidId(string? id)=>id!=null&&id.Length==16&&id.All(c=>c is >='0' and <='9' or >='a' and <='f');
 public static string NewId()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
 // By file extension; null means "not a supported image or video".
 public static string? MimeFor(string path)=>Path.GetExtension(path).ToLowerInvariant() switch{".jpg" or ".jpeg"=>"image/jpeg",".png"=>"image/png",".gif"=>"image/gif",".bmp"=>"image/bmp",".mp4" or ".m4v"=>"video/mp4",".webm"=>"video/webm",_=>null};
 public static string Size(long bytes)=>bytes>=1024*1024?$"{bytes/1024d/1024:0.0} MB":$"{Math.Max(1,bytes/1024)} KB";
}
// Built-in stickers travel as just their id; each app draws them itself.
public static class Stickers {
 public static readonly string[] Ids=["hi","lol","wow","love","yes","no","gg","brb","party","sad","coffee","orb"];
}
public enum Field{None,Relay,Name,Room,Password}
public static class Protocol {
 public const string RoomRule="Room name: 3–32 letters, numbers or hyphens.",NameRule="Display name: 1–32 characters.",PasswordRule="Password: 10–128 characters.";
 public static bool ValidRoom(string? s)=>s!=null&&s.Length>=3&&s.Length<=32&&s.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-');
 public static bool ValidName(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=32&&!s.Any(char.IsControl);
 public static bool ValidPassword(string? s)=>s!=null&&s.Length>=10&&s.Length<=128&&!s.Any(char.IsControl);
 // Spaces in a typed room name become hyphens: "friday friends" -> "friday-friends".
 public static string NormalizeRoom(string s)=>Regex.Replace(s.Trim(),@"\s+","-");
 // First invalid field in form order, with a short message for that field only.
 public static (Field Field,string Message) Check(string relay,string name,string room,string password){
  try{RelayClient.ValidateUrl(relay);}catch(IOException e){return (Field.Relay,e.Message);}
  if(!ValidName(name.Trim()))return (Field.Name,NameRule);
  if(!ValidRoom(NormalizeRoom(room)))return (Field.Room,RoomRule);
  if(!ValidPassword(password))return (Field.Password,PasswordRule);
  return (Field.None,"");
 }
 // "Sam is typing…", "Sam and Jo are typing…", "Several people are typing…".
 public static string DescribeTyping(IReadOnlyList<string> names)=>names.Count switch{0=>"",1=>$"{names[0]} is typing…",2=>$"{names[0]} and {names[1]} are typing…",_=>"Several people are typing…"};
 public static async Task<Packet?> Read(WebSocket socket,CancellationToken ct){
  using var data=new MemoryStream();byte[] buffer=new byte[4096];
  while(true){var r=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),ct);if(r.MessageType==WebSocketMessageType.Close)return null;if(r.MessageType!=WebSocketMessageType.Text||data.Length+r.Count>16384)throw new IOException("Invalid message size or type.");data.Write(buffer,0,r.Count);if(r.EndOfMessage)break;}
  return JsonSerializer.Deserialize<Packet>(data.ToArray())??throw new IOException("Invalid message.");
 }
 public static Task Write(WebSocket socket,Packet p,CancellationToken ct)=>socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(p)),WebSocketMessageType.Text,true,ct);
}
// Saved only when the user ticks "Remember". There is deliberately no password field.
// Account/Session: who stays signed in. Session is the sign-in token encrypted with Windows (DPAPI) for this Windows user only.
public record Settings(string Relay="",string Name="",bool Sound=true,bool StartupSound=true,string Account="",string Session="",bool ReadReceipts=true){
 public static readonly Settings Empty=new();
 public static string DefaultPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"3DFx","settings.json");
 public static Settings Load(string path){try{var s=JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));return s==null||s.Relay==null||s.Name==null||s.Account==null||s.Session==null||s.Relay.Length>300||s.Name.Length>32?Empty:s;}catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){return Empty;}}
 public void Save(string path){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,JsonSerializer.Serialize(this));}
}
public sealed class RelayClient:IDisposable {
 // Pings every 15 s; a missing reply within 15 s ends the connection so a dropped network is noticed.
 readonly ClientWebSocket socket=new(){Options={KeepAliveInterval=TimeSpan.FromSeconds(15),KeepAliveTimeout=TimeSpan.FromSeconds(15)}};readonly SemaphoreSlim send=new(1);readonly CancellationTokenSource stop=new();
 // Accepts "name.onrender.com", a trailing "/", "/chat" or "/health". Still HTTPS only, no user info, query or fragment.
 public static Uri ValidateUrl(string value){var v=value.Trim();if(v.Length>0&&!v.Contains("://"))v="https://"+v;
  if(!Uri.TryCreate(v,UriKind.Absolute,out var u)||u.Scheme!="https"||!u.Host.Contains('.')||!string.IsNullOrEmpty(u.UserInfo)||!string.IsNullOrEmpty(u.Query)||!string.IsNullOrEmpty(u.Fragment)||u.AbsolutePath.TrimEnd('/') is not (""or"/chat"or"/health"))throw new IOException("Relay address: use https://your-relay.onrender.com");
  return new UriBuilder(u){Scheme="wss",Path="/chat"}.Uri;}
 // With a session token the relay uses the account's username; without one you join as a guest.
 public async Task Connect(string url,bool create,string room,string password,string name,string? token=null){
  var (field,problem)=Protocol.Check(url,name,room,password);if(field!=Field.None)throw new IOException(problem);
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(90));
  await socket.ConnectAsync(TestEndpoint??ValidateUrl(url),timeout.Token);
  await Protocol.Write(socket,new Packet(create?"create":"join",Protocol.NormalizeRoom(room).ToLowerInvariant(),password,name.Trim(),Token:token??""),timeout.Token);
  var reply=await Protocol.Read(socket,timeout.Token);if(reply?.Kind!="ready")throw new IOException(reply?.Text??"Relay disconnected.");
  Features=reply.Text.Split(' ',StringSplitOptions.RemoveEmptyEntries);
 }
 // Tests point this at a local ws:// relay; the app always uses the relay's HTTPS address.
 internal static Uri? TestEndpoint;
 // What the relay supports, from its "ready" reply (e.g. "nudge typing"). A 0.4 relay sends none; never send it packets it would reject.
 public string[] Features{get;private set;}=[];
 public Task<Packet?> Read()=>Protocol.Read(socket,stop.Token);
 public async Task Signal(string kind){if(!Features.Contains(kind))return;await Write(new Packet(kind));}
 public async Task SendSticker(string id,string reply=""){if(Features.Contains("stickers"))await Write(new Packet("sticker",Text:id,Reply:reply));}
 // Changes to room messages (by the relay's message id). Only your own can be edited or deleted; reacting again takes it back.
 public async Task Edit(string id,string text){if(string.IsNullOrWhiteSpace(text)||text.Length>2000)throw new IOException("Message must be 1–2,000 characters.");if(Features.Contains("edits"))await Write(new Packet("edit",Id:id,Text:text));}
 public async Task Delete(string id){if(Features.Contains("edits"))await Write(new Packet("delete",Id:id));}
 public async Task React(string id,string emoji){if(Features.Contains("reactions"))await Write(new Packet("react",Id:id,Text:emoji));}
 // Sends one image or video in pieces. Each piece takes the send lock on its own, so chat messages can slip in between.
 // Returns its id (others react to it by this id).
 public async Task<string> SendMedia(byte[] data,string mime,IProgress<int>? progress=null){
  if(!Features.Contains("media"))throw new IOException("This relay does not support pictures and videos yet.");
  if(data.Length==0||data.Length>Media.Limit(mime))throw new IOException(Media.IsVideo(mime)?"Videos can be up to 25 MB.":"Pictures can be up to 5 MB.");
  var id=Media.NewId();await Write(new Packet("media-start",Text:mime,Id:id,Total:data.Length));
  for(int seq=0,offset=0;offset<data.Length;seq++,offset+=Media.ChunkBytes){
   await Write(new Packet("media-chunk",Id:id,Seq:seq,Data:Convert.ToBase64String(data,offset,Math.Min(Media.ChunkBytes,data.Length-offset))));
   progress?.Report((int)(100L*Math.Min(data.Length,offset+Media.ChunkBytes)/data.Length));}
  await Write(new Packet("media-end",Id:id));return id;}
 async Task Write(Packet p){await send.WaitAsync(stop.Token);try{await Protocol.Write(socket,p,stop.Token);}finally{send.Release();}}
 public async Task Send(string text,string reply=""){if(string.IsNullOrWhiteSpace(text)||text.Length>2000)throw new IOException("Message must be 1–2,000 characters.");await send.WaitAsync(stop.Token);try{await Protocol.Write(socket,new Packet("send",Text:text,Reply:Features.Contains("replies")?reply:""),stop.Token);}finally{send.Release();}}
 // Says goodbye with a WebSocket close so the relay sees the leave at once; a bare abort takes ~10 s to pass through Render's proxy.
 public async Task Close(){try{using var t=new CancellationTokenSource(TimeSpan.FromSeconds(1.5));await send.WaitAsync(t.Token).ConfigureAwait(false);try{if(socket.State==WebSocketState.Open)await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,null,t.Token).ConfigureAwait(false);}finally{send.Release();}
  // Wait (up to the 1.5 s) for the relay's side to end, as seen by the receive loop; aborting right away lets the proxy drop the close.
  while(socket.State==WebSocketState.CloseSent)await Task.Delay(50,t.Token).ConfigureAwait(false);}catch(Exception e) when(e is WebSocketException or OperationCanceledException or ObjectDisposedException){}finally{Dispose();}}
 public void Dispose(){stop.Cancel();socket.Abort();socket.Dispose();}
}
