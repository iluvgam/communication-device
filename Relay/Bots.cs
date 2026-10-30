using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace FriendsChat;

// AI members (Bryan). Their account names are reserved: nobody can sign up as them, and only the operator's bot
// service can sign in as them, with the relay's BotKey. Typing @name in a room summons the bot: the relay gives it a
// one-time pass for that room (60 s), so it can join without the room password. Bots always show as "Name (AI)".
sealed class Bots {
 public const string Tag=" (AI)";
 readonly HashSet<string> names;readonly byte[]? key;
 readonly ConcurrentDictionary<string,(string Room,string Bot,DateTime Until)> passes=new();
 readonly ConcurrentDictionary<string,DateTime> lastSummon=new();
 public Bots(IEnumerable<string> names,string? key){this.names=new(names.Select(n=>n.Trim()).Where(n=>n.Length>0),StringComparer.OrdinalIgnoreCase);this.key=key is {Length:>=16}?Encoding.UTF8.GetBytes(key):null;}
 // Off unless a BotKey of at least 16 characters is configured. Names stay reserved either way.
 public bool On=>key!=null&&names.Count>0;
 public bool IsBot(string? username)=>username!=null&&names.Contains(username);
 public bool KeyMatches(string? given)=>key!=null&&given!=null&&CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(given)),SHA256.HashData(key));
 public static string Display(string username)=>char.ToUpperInvariant(username[0])+username[1..]+Tag;
 // "@bryan" anywhere in a message (case doesn't matter).
 public string? Mentioned(string text){if(!On)return null;foreach(var n in names)if(Regex.IsMatch(text,$@"(^|\W)@{Regex.Escape(n)}\b",RegexOptions.IgnoreCase))return n;return null;}
 // At most one summons per bot per room every 10 s.
 public bool Recently(string room,string bot){var k=room+"/"+bot.ToLowerInvariant();var now=DateTime.UtcNow;
  if(lastSummon.TryGetValue(k,out var at)&&(now-at).TotalSeconds<10)return true;lastSummon[k]=now;if(lastSummon.Count>5000)lastSummon.Clear();return false;}
 public string NewPass(string room,string bot){var pass=Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
  foreach(var old in passes.Where(p=>p.Value.Until<DateTime.UtcNow).Select(p=>p.Key).ToList())passes.TryRemove(old,out _);
  passes[pass]=(room,bot.ToLowerInvariant(),DateTime.UtcNow.AddSeconds(60));return pass;}
 // A pass works once, only for its room, only for its bot, only within 60 s.
 public bool UsePass(string? pass,string room,string bot)=>pass!=null&&passes.TryRemove(pass,out var p)&&p.Room==room&&p.Bot==bot.ToLowerInvariant()&&p.Until>DateTime.UtcNow;
}
