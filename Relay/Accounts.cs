using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
namespace FriendsChat;

// Accounts on the relay's disk (one SQLite file): username, salted PBKDF2 password hash, recovery-key hash and
// hashes of sign-in tokens. Messages, rooms and media are never stored.
sealed class AccountStore {
 public const int SessionDays=30;
 readonly string connection;
 public sealed record Account(long Id,string Username,byte[] Salt,byte[] Hash,byte[] Recovery);
 public AccountStore(string path){
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
  connection=new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadWriteCreate}.ToString();
  using var db=Open();Run(db,"""
   PRAGMA journal_mode=WAL;
   CREATE TABLE IF NOT EXISTS accounts(id INTEGER PRIMARY KEY,username TEXT NOT NULL UNIQUE COLLATE NOCASE,salt BLOB NOT NULL,hash BLOB NOT NULL,recovery BLOB NOT NULL,created INTEGER NOT NULL);
   CREATE TABLE IF NOT EXISTS sessions(token BLOB PRIMARY KEY,account INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,expires INTEGER NOT NULL);
   CREATE INDEX IF NOT EXISTS sessions_account ON sessions(account);
   """);}
 internal SqliteConnection Open(){var db=new SqliteConnection(connection);db.Open();Run(db,"PRAGMA foreign_keys=ON;");return db;}
 internal static SqliteCommand Command(SqliteConnection db,string sql,(string Name,object Value)[] args){var c=db.CreateCommand();c.CommandText=sql;foreach(var (n,v) in args)c.Parameters.AddWithValue(n,v);return c;}
 internal static void Run(SqliteConnection db,string sql,params (string,object)[] args){using var c=Command(db,sql,args);c.ExecuteNonQuery();}
 void Exec(string sql,params (string,object)[] args){using var db=Open();Run(db,sql,args);}
 static long Now=>DateTimeOffset.UtcNow.ToUnixTimeSeconds();
 public Account? Find(string username){using var db=Open();using var c=Command(db,"SELECT id,username,salt,hash,recovery FROM accounts WHERE username=$u",[("$u",username)]);using var r=c.ExecuteReader();
  return r.Read()?new(r.GetInt64(0),r.GetString(1),(byte[])r[2],(byte[])r[3],(byte[])r[4]):null;}
 // Null when the username is already taken (usernames are unique regardless of case).
 public long? Create(string username,byte[] salt,byte[] hash,byte[] recovery){using var db=Open();
  try{using var c=Command(db,"INSERT INTO accounts(username,salt,hash,recovery,created) VALUES($u,$s,$h,$r,$t) RETURNING id",[("$u",username),("$s",salt),("$h",hash),("$r",recovery),("$t",Now)]);return (long)c.ExecuteScalar()!;}
  catch(SqliteException e) when(e.SqliteErrorCode==19){return null;}}
 public void SetPassword(long id,byte[] salt,byte[] hash)=>Exec("UPDATE accounts SET salt=$s,hash=$h WHERE id=$id",("$s",salt),("$h",hash),("$id",id));
 public void SetRecovery(long id,byte[] recovery)=>Exec("UPDATE accounts SET recovery=$r WHERE id=$id",("$r",recovery),("$id",id));
 public void Delete(long id)=>Exec("DELETE FROM accounts WHERE id=$id",("$id",id));
 // Sign-in tokens are 32 random bytes; only their SHA-256 is stored, so a copy of the database cannot be used to sign in.
 static byte[] TokenHash(string token)=>SHA256.HashData(Encoding.UTF8.GetBytes(token));
 public string NewSession(long account){var token=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
  Exec("DELETE FROM sessions WHERE expires<$now",("$now",Now));
  Exec("INSERT INTO sessions(token,account,expires) VALUES($t,$a,$e)",("$t",TokenHash(token)),("$a",account),("$e",Now+SessionDays*86400L));
  // At most 10 signed-in devices per account; the oldest is signed out first.
  Exec("DELETE FROM sessions WHERE account=$a AND token NOT IN (SELECT token FROM sessions WHERE account=$a ORDER BY expires DESC LIMIT 10)",("$a",account));
  return token;}
 public (long Id,string Username)? Session(string token){if(token.Length is <20 or >100)return null;
  using var db=Open();using var c=Command(db,"SELECT a.id,a.username FROM sessions s JOIN accounts a ON a.id=s.account WHERE s.token=$t AND s.expires>$now",[("$t",TokenHash(token)),("$now",Now)]);using var r=c.ExecuteReader();
  return r.Read()?(r.GetInt64(0),r.GetString(1)):null;}
 public void EndSession(string token)=>Exec("DELETE FROM sessions WHERE token=$t",("$t",TokenHash(token)));
 public void EndAllSessions(long account)=>Exec("DELETE FROM sessions WHERE account=$a",("$a",account));
}

// HTTPS JSON endpoints under /account. Passwords use the same PBKDF2 cost and the same two check slots as room passwords.
static class AccountApi {
 sealed record Body(string? Username,string? Password,string? NewPassword,string? RecoveryKey);
 // Wrong guesses per username (existing or not): from the 5th, each try must wait 30 s, doubling up to 15 minutes. Memory only.
 static readonly ConcurrentDictionary<string,(int Fails,DateTime Until)> tries=new(StringComparer.OrdinalIgnoreCase);
 static readonly byte[] dummySalt=RandomNumberGenerator.GetBytes(16),dummyHash=new byte[32];
 static byte[] Hash(string password,byte[] salt)=>Rfc2898DeriveBytes.Pbkdf2(password,salt,600000,HashAlgorithmName.SHA256,32);
 static byte[] RecoveryHash(string key)=>SHA256.HashData(Encoding.UTF8.GetBytes(Accounts.NormalizeRecoveryKey(key)));
 static IResult Error(int status,string text)=>Results.Json(new AccountReply(Error:text),statusCode:status);
 static readonly IResult expired=Error(401,"Your sign-in has expired. Sign in again.");
 static IResult? Waiting(string username){
  if(!tries.TryGetValue(username,out var t)||t.Until<=DateTime.UtcNow)return null;var wait=t.Until-DateTime.UtcNow;
  return Error(429,wait.TotalSeconds<60?$"Too many tries. Wait {Math.Ceiling(wait.TotalSeconds)} seconds.":$"Too many tries. Wait {Math.Ceiling(wait.TotalMinutes)} minutes.");}
 static void Failed(string username){if(tries.Count>10000)tries.Clear();
  tries.AddOrUpdate(username,_=>(1,DateTime.MinValue),(_,t)=>{int f=t.Fails+1;return(f,f>=5?DateTime.UtcNow.AddSeconds(Math.Min(900,30*Math.Pow(2,f-5))):DateTime.MinValue);});}
 static void Succeeded(string username)=>tries.TryRemove(username,out _);
 // Hashes even for unknown usernames, so timing does not reveal which usernames exist.
 static async Task<bool> PasswordMatches(AccountStore.Account? account,string? password,SemaphoreSlim slots,CancellationToken ct){
  if(!Protocol.ValidPassword(password))return false;
  await slots.WaitAsync(ct);try{var hash=Hash(password!,account?.Salt??dummySalt);return CryptographicOperations.FixedTimeEquals(hash,account?.Hash??dummyHash)&&account!=null;}finally{slots.Release();}}
 static async Task<byte[]> NewHash(string password,byte[] salt,SemaphoreSlim slots,CancellationToken ct){await slots.WaitAsync(ct);try{return Hash(password,salt);}finally{slots.Release();}}
 static (long Id,string Username)? Who(HttpContext http,AccountStore store,out string token){var h=http.Request.Headers.Authorization.ToString();token=h.StartsWith("Bearer ")?h[7..].Trim():"";return token.Length>0?store.Session(token):null;}

 public static void Map(WebApplication app,AccountStore store,SemaphoreSlim slots,DirectHub direct,Bots bots){
  var api=app.MapGroup("/account").RequireRateLimiting("accounts");
  api.MapPost("/signup",async(Body b,CancellationToken ct)=>{
   if(!Accounts.ValidUsername(b.Username))return Error(400,Accounts.UsernameRule);
   if(bots.IsBot(b.Username))return Error(409,"That username is taken. Choose another.");
   if(!Protocol.ValidPassword(b.Password))return Error(400,Protocol.PasswordRule);
   var salt=RandomNumberGenerator.GetBytes(16);var hash=await NewHash(b.Password!,salt,slots,ct);var key=Accounts.NewRecoveryKey();
   var id=store.Create(b.Username!,salt,hash,RecoveryHash(key));
   return id==null?Error(409,"That username is taken. Choose another."):Results.Ok(new AccountReply(b.Username!,store.NewSession(id.Value),key));});
  api.MapPost("/signin",async(Body b,CancellationToken ct)=>{var u=b.Username??"";if(Waiting(u) is {} wait)return wait;
   var account=Accounts.ValidUsername(u)?store.Find(u):null;
   if(!await PasswordMatches(account,b.Password,slots,ct)){Failed(u);return Error(401,"Username or password is incorrect.");}
   Succeeded(u);return Results.Ok(new AccountReply(account!.Username,store.NewSession(account.Id)));});
  // A recovery key works once: it sets a new password, signs out every device and is replaced by a new key.
  api.MapPost("/recover",async(Body b,CancellationToken ct)=>{var u=b.Username??"";if(Waiting(u) is {} wait)return wait;
   if(!Protocol.ValidPassword(b.NewPassword))return Error(400,Protocol.PasswordRule);
   var account=Accounts.ValidUsername(u)?store.Find(u):null;
   if(account==null||!CryptographicOperations.FixedTimeEquals(account.Recovery,RecoveryHash(b.RecoveryKey??""))){Failed(u);return Error(401,"Username or recovery key is incorrect.");}
   Succeeded(u);var salt=RandomNumberGenerator.GetBytes(16);store.SetPassword(account.Id,salt,await NewHash(b.NewPassword!,salt,slots,ct));
   var key=Accounts.NewRecoveryKey();store.SetRecovery(account.Id,RecoveryHash(key));store.EndAllSessions(account.Id);_=direct.SessionsEnded(account.Id);
   return Results.Ok(new AccountReply(account.Username,store.NewSession(account.Id),key));});
  // The operator's bot service signs in here with the BotKey; the account is created on first use with an unusable
  // password, so the BotKey is the only way in.
  api.MapPost("/bot",(Body b)=>{
   if(!bots.KeyMatches(b.Password)||!bots.IsBot(b.Username))return Error(401,"Not allowed.");
   var id=store.Find(b.Username!)?.Id??store.Create(b.Username!.ToLowerInvariant(),RandomNumberGenerator.GetBytes(16),RandomNumberGenerator.GetBytes(32),RandomNumberGenerator.GetBytes(32));
   return id is {} found?Results.Ok(new AccountReply(store.Find(b.Username!)!.Username,store.NewSession(found))):Error(409,"Could not create the bot account.");});
  api.MapPost("/me",(HttpContext http)=>Who(http,store,out _) is {} who?Results.Ok(new AccountReply(who.Username)):expired);
  api.MapPost("/signout",(HttpContext http)=>{if(Who(http,store,out var token) is not null){store.EndSession(token);_=direct.TokenEnded(token);}return Results.Ok(new AccountReply());});
  // Changing the password signs out every other device and returns a fresh sign-in for this one.
  api.MapPost("/password",async(HttpContext http,Body b,CancellationToken ct)=>{if(Who(http,store,out _) is not {} who)return expired;if(Waiting(who.Username) is {} wait)return wait;
   if(!Protocol.ValidPassword(b.NewPassword))return Error(400,Protocol.PasswordRule);
   var account=store.Find(who.Username);if(!await PasswordMatches(account,b.Password,slots,ct)){Failed(who.Username);return Error(401,"Current password is incorrect.");}
   Succeeded(who.Username);var salt=RandomNumberGenerator.GetBytes(16);store.SetPassword(account!.Id,salt,await NewHash(b.NewPassword!,salt,slots,ct));store.EndAllSessions(account.Id);_=direct.SessionsEnded(account.Id);
   return Results.Ok(new AccountReply(account.Username,store.NewSession(account.Id)));});
  api.MapPost("/recovery-key",async(HttpContext http,Body b,CancellationToken ct)=>{if(Who(http,store,out _) is not {} who)return expired;if(Waiting(who.Username) is {} wait)return wait;
   var account=store.Find(who.Username);if(!await PasswordMatches(account,b.Password,slots,ct)){Failed(who.Username);return Error(401,"Current password is incorrect.");}
   Succeeded(who.Username);var key=Accounts.NewRecoveryKey();store.SetRecovery(account!.Id,RecoveryHash(key));return Results.Ok(new AccountReply(account.Username,RecoveryKey:key));});
  api.MapPost("/delete",async(HttpContext http,Body b,CancellationToken ct)=>{if(Who(http,store,out _) is not {} who)return expired;if(Waiting(who.Username) is {} wait)return wait;
   var account=store.Find(who.Username);if(!await PasswordMatches(account,b.Password,slots,ct)){Failed(who.Username);return Error(401,"Current password is incorrect.");}
   Succeeded(who.Username);var watchers=direct.Watchers(account!.Id);store.Delete(account.Id);_=direct.Deleted(account.Id,watchers);return Results.Ok(new AccountReply());});
 }
}
