using Velopack;
using Velopack.Sources;
namespace FriendsChat;

// Updates come from the GitHub Releases of the 3DFx repository (Velopack: Setup.exe installs per user, updates apply on restart).
// Only copies installed with Setup.exe update themselves; a portable copy just skips the check.
static class Updates {
 public const string Repository="https://github.com/iluvgam/communication-device";
 public static async Task<(UpdateManager Manager,UpdateInfo Info)?> Check(){
  try{var manager=new UpdateManager(new GithubSource(Repository,null,false));if(!manager.IsInstalled)return null;
   var info=await manager.CheckForUpdatesAsync();return info==null?null:(manager,info);}
  // An update check must never get in the way of chatting: any failure (offline, GitHub busy, odd release) means "no update now".
  catch(Exception){return null;}}
}
