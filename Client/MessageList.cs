using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.RegularExpressions;
namespace FriendsChat;

enum ChatKind{Text,Notice,Nudge,Problem,Sticker,Picture,Video,Welcome}
// One entry in a conversation. Pictures keep a small thumbnail; the full bytes stay in Attachment until memory is reclaimed.
sealed class ChatItem(ChatKind kind,string who,string text,DateTime at){
 public readonly ChatKind Kind=kind;public readonly string Who=who;public readonly DateTime At=at;
 // Text changes when its author edits it.
 public string Text{get;internal set;}=text;
 public MediaItem? Attachment;public Bitmap? Thumb;public string Title="";
 // Id: the relay's id (rooms) or the sender's (DMs); "" for notices and older relays. Reply: the id this answers.
 public string Id="",Reply="";public bool Edited;public List<Reaction> Reactions=[];
 // Layout, filled in by MessageList (device pixels, in list coordinates).
 internal long Born;internal int Y,Height,MeasuredFor=-1,BodyHeight,Lead;internal bool Continued,Divider,CardGone,IsNew;internal Rectangle Body;
 // Lead: room for the "replying to" line above the name. Pills: the reaction buttons under the message (list coordinates).
 internal List<(Rectangle R,Reaction What)>? Pills;
 // Text messages are laid out word by word (for links, @mentions and selection); positions are relative to Body.
 internal List<Word>? Words;
 public bool IsMessage=>Kind is ChatKind.Text or ChatKind.Sticker or ChatKind.Picture or ChatKind.Video;
}

// One laid-out word. Href is set for links; Sep is what follows it when copied (" ", a line break, or nothing inside a long word).
// Note marks the faint "(edited)" after an edited message; it is not part of the text.
readonly record struct Word(Rectangle R,string Text,string? Href,bool Mention,string Sep,bool Note=false);

// Names carry their tag on the wire: "Groki (AI)" and "Jordan (guest)". The app shows the name plus a badge.
static class Names {
 public static (string Name,string Tag) Split(string who)=>who.EndsWith(" (AI)",StringComparison.Ordinal)?(who[..^5],"AI"):who.EndsWith(Accounts.GuestSuffix,StringComparison.Ordinal)?(who[..^Accounts.GuestSuffix.Length],"GUEST"):(who,"");
}

// A conversation's history: avatar and coloured name on the first message of a group (same person within 7 minutes),
// date lines between days, inline pictures, video cards and stickers. Click a picture or video to open it, a link to open it in
// your browser, a name or picture for that person's card. Drag across text to select it (Ctrl+C copies); double-click selects a message.
// Media stays in memory only; past 300 MB the oldest is let go.
sealed class MessageList:Control {
 readonly List<ChatItem> items=[];int scroll,total,hover=-1,dragFrom=-1,dragScroll,unseen;bool stick=true,suspended,dragged;Rectangle jump;
 // Selection, from anchor to focus (item index, word index); hoverLink is the link under the mouse.
 (int I,int W) anchor,focus;bool selecting,hasSelection;Point pressAt;string? hoverLink;
 public event Action<string,Point>? ProfileClicked;
 // Replies, edits, deletes and reactions: the conversation says what it allows; your own messages (Who == Me) can be edited and deleted.
 // React with emoji null means "choose one" (a picker at that screen point).
 public event Action<ChatItem>? ReplyClicked,EditClicked,DeleteClicked;
 public event Action<ChatItem,string?,Point>? ReactClicked;
 public Func<bool> CanReply=()=>false,CanReact=()=>false,CanEdit=()=>false;
 // Feedback on Groki's replies (thumbs up / down): which messages can be rated, how you rated one ("up", "down" or null).
 public Func<ChatItem,bool> CanRate=_=>false;public Func<ChatItem,string?> RatingOf=_=>null;
 public event Action<ChatItem,bool>? Rated;public event Action? SurveyClicked;
 // Each message as it arrives (not while loading history in one go).
 public event Action<ChatItem>? Added;
 bool Own(ChatItem it)=>Me.Length>0&&it.Who==Me&&it.Id.Length>0;
 readonly ToolTip tip=DarkTip.Create();string tipText="";
 // Search: matching text messages (oldest first) and which one is shown; words containing the text are highlighted.
 string query="";List<int> matches=[];int matchAt=-1;
 public void Search(string text){query=text.Trim();matches=Matches();matchAt=matches.Count-1;ShowMatch();}
 List<int> Matches()=>query.Length==0?[]:items.Select((it,i)=>(it,i)).Where(x=>x.it.Kind==ChatKind.Text&&x.it.Text.Contains(query,StringComparison.OrdinalIgnoreCase)).Select(x=>x.i).ToList();
 // +1 = older, -1 = newer (wrapping around).
 public void StepSearch(int dir){if(matches.Count==0)return;matchAt=((matchAt-dir)%matches.Count+matches.Count)%matches.Count;ShowMatch();}
 public string SearchStatus=>query.Length==0?"":matches.Count==0?"No results":$"{matches.Count-matchAt} of {matches.Count}";
 public void EndSearch(){query="";matches.Clear();matchAt=-1;Invalidate();}
 void ShowMatch(){if(matchAt>=0&&matches[matchAt]<items.Count){var it=items[matches[matchAt]];SmoothTo(it.Y-ClientSize.Height/3,320);}Invalidate();}
 bool IsCurrentMatch(ChatItem it)=>matchAt>=0&&matchAt<matches.Count&&matches[matchAt]<items.Count&&items[matches[matchAt]]==it;
 // Text size changed (Settings > Appearance): measure everything again.
 public void Restyle(){Font=Aero.Chat;lineH=0;underline=null;foreach(var it in items)it.MeasuredFor=-1;bool atEnd=stick;Relayout();if(atEnd)scroll=MaxScroll;Invalidate();}
 readonly object scrollKey=new(),revealKey=new();int scrollTarget;long revealed;
 const int AppearMs=340,RevealMs=260;
 // Glides the view to a scroll position (wheel, jump to present, following new messages).
 void SmoothTo(int target,double ms=220){target=Math.Clamp(target,0,MaxScroll);scrollTarget=target;int from=scroll;stick=target>=MaxScroll;if(stick)unseen=0;
  Motion.Run(scrollKey,this,ms,t=>{scroll=Math.Clamp((int)Math.Round(from+(target-from)*t),0,MaxScroll);Invalidate();},Motion.Out);}
 // Fades the whole conversation in (when you switch to it).
 public void Reveal(){revealed=Environment.TickCount64;Motion.Run(revealKey,this,RevealMs,_=>Invalidate());}
 public Func<string,Image?> AvatarFor=_=>null;
 // Messages that mention @Me get a yellow edge.
 public string Me="";
 public event Action<string>? Problem;
 public Action<ContextMenuStrip,ChatItem?>? ExtendMenu;
 const long MaxHeld=300L*1024*1024;const int MaxItems=2000;long heldBytes;
 const TextFormatFlags Wrap=TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.TextBoxControl;
 int S(int px)=>LogicalToDeviceUnits(px);
 int Gutter=>S(72);int TextWidth=>Math.Max(S(120),ClientSize.Width-S(72)-S(28));
 public MessageList(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);BackColor=Aero.Bg;Font=Aero.Chat;}
 public IReadOnlyList<ChatItem> Items=>items;

 public void BeginUpdate()=>suspended=true;
 public void EndUpdate(){suspended=false;Relayout();ScrollToEnd();}
 public ChatItem Add(ChatItem item){items.Add(item);
  if(items.Count>MaxItems){int drop=items.Count-MaxItems+200;foreach(var old in items.Take(drop)){Forget(old);old.Thumb?.Dispose();}items.RemoveRange(0,drop);hasSelection=false;}
  if(item.Attachment!=null)Remember(item);
  if(!suspended){bool atEnd=stick;item.Born=Environment.TickCount64;Relayout();if(atEnd)SmoothTo(MaxScroll,260);else{if(item.IsMessage)unseen++;Invalidate();}Motion.Run(item,this,AppearMs,_=>Invalidate());Added?.Invoke(item);}
  return item;}
 public void Say(string who,string text,DateTime? at=null,string id="",string reply="",bool edited=false)=>Add(new(ChatKind.Text,who,text,at??DateTime.Now){Id=id,Reply=reply,Edited=edited});
 public void Notice(string text,DateTime? at=null)=>Add(new(ChatKind.Notice,"",text,at??DateTime.Now));
 public void Highlight(string text,DateTime? at=null)=>Add(new(ChatKind.Nudge,"",text,at??DateTime.Now));
 public void Fail(string text)=>Add(new(ChatKind.Problem,"",text,DateTime.Now));
 public void ShowSticker(string from,string sticker,DateTime? at=null,string id="",string reply="")=>Add(new(ChatKind.Sticker,from,sticker,at??DateTime.Now){Id=id,Reply=reply});
 public void ShowMedia(string from,string mime,byte[] data,DateTime? at=null,string id="",string reply=""){
  if(Media.IsVideo(mime)){Add(new(ChatKind.Video,from,mime,at??DateTime.Now){Attachment=new MediaItem(mime,data,from),Id=id,Reply=reply});return;}
  var thumb=Pictures.Thumbnail(data,S(400),S(300));if(thumb==null){Say(from,"(sent a picture that can't be shown)",at,id,reply);return;}
  Add(new(ChatKind.Picture,from,mime,at??DateTime.Now){Attachment=new MediaItem(mime,data,from),Thumb=thumb,Id=id,Reply=reply});}
 // Changes by message id. "by" (DMs) only lets a message's own author change it; rooms leave that check to the relay.
 public ChatItem? Find(string id)=>id.Length==0?null:items.FindLast(x=>x.Id==id);
 readonly HashSet<string> deleted=[];
 public bool Edit(string id,string text,string? by=null){if(Find(id) is not {Kind:ChatKind.Text} it||by!=null&&it.Who!=by)return false;it.Text=text;it.Edited=true;Changed(it);return true;}
 public bool Remove(string id,string? by=null){var it=Find(id);if(it==null||by!=null&&it.Who!=by)return false;
  Forget(it);it.Thumb?.Dispose();items.Remove(it);deleted.Add(id);hasSelection=false;hover=-1;if(query.Length>0)matches=Matches();Changed(null);return true;}
 public void SetReactions(string id,List<Reaction> list){if(Find(id) is {} it){it.Reactions=list;Changed(it);}}
 // DMs: each side applies the same toggles in the same order.
 public void ToggleReaction(string id,string emoji,string who){if(Find(id) is not {} it)return;var r=it.Reactions.FirstOrDefault(x=>x.E==emoji);
  if(r!=null&&r.N.Remove(who)){if(r.N.Count==0)it.Reactions.Remove(r);}
  else if(r!=null||it.Reactions.Count<Reaction.MaxKinds){if(r==null)it.Reactions.Add(r=new(emoji,[]));r.N.Add(who);}
  Changed(it);}
 void Changed(ChatItem? it){if(it!=null)it.MeasuredFor=-1;if(suspended)return;bool atEnd=stick;Relayout();if(atEnd)scroll=MaxScroll;Invalidate();}
 // "Replying to" line: who and the start of what they said.
 public string Snippet(ChatItem it)=>it.Kind switch{ChatKind.Sticker=>"Sticker",ChatKind.Picture=>"Picture",ChatKind.Video=>"Video",_=>it.Text.ReplaceLineEndings(" ")};
 // Jumps to a message and flashes it (clicking a reply's quote).
 readonly object flashKey=new();ChatItem? flash;long flashAt;
 public void JumpTo(ChatItem it){SmoothTo(it.Y-ClientSize.Height/3,320);flash=it;flashAt=Environment.TickCount64;Motion.Run(flashKey,this,1600,_=>Invalidate());}
 // --preview only: shows the hover toolbar on a message.
 internal void PreviewHover(ChatItem it){hover=items.IndexOf(it);Invalidate();}
 // The big "Welcome to #room" / DM opener at the top. avatarOf starting with # draws a # instead of a picture.
 public void Welcome(string avatarOf,string title,string text)=>Add(new(ChatKind.Welcome,avatarOf,text,DateTime.Now){Title=title});
 public void Clear(){foreach(var it in items){Forget(it);it.Thumb?.Dispose();}items.Clear();deleted.Clear();scroll=0;stick=true;hover=-1;hasSelection=false;Relayout();Invalidate();}
 // The red "NEW" line above the first of the last `count` messages (the ones that arrived while you were away). Scrolls to it.
 public void MarkNew(int count){ClearNew();if(count<=0)return;int seen=0;
  for(int i=items.Count-1;i>=0;i--)if(items[i].IsMessage||items[i].Kind==ChatKind.Nudge){if(++seen==count){items[i].IsNew=true;Relayout();scroll=Math.Clamp(items[i].Y-S(80),0,MaxScroll);stick=scroll>=MaxScroll;Invalidate();return;}}}
 public void ClearNew(){bool had=false;foreach(var it in items)if(it.IsNew){it.IsNew=false;had=true;}if(had){Relayout();Invalidate();}}
 void Remember(ChatItem item){heldBytes+=item.Attachment!.Size;foreach(var old in items){if(heldBytes<=MaxHeld)break;if(old!=item)Forget(old);}}
 void Forget(ChatItem it){if(it.Attachment?.Data==null)return;it.Attachment.Data=null;heldBytes-=it.Attachment.Size;}

 // Layout: positions every entry; measured text is cached per width.
 void Relayout(){if(suspended)return;int w=TextWidth,y=S(12);ChatItem? prev=null;
  foreach(var it in items){
   it.Divider=prev!=null&&prev.At.Date!=it.At.Date&&it.Kind!=ChatKind.Welcome;
   it.Continued=!it.Divider&&!it.IsNew&&it.Reply.Length==0&&prev!=null&&it.IsMessage&&prev.IsMessage&&prev.Who==it.Who&&(it.At-prev.At).TotalMinutes<7&&it.At>=prev.At;
   if(it.Divider)y+=S(40);else if(it.IsNew)y+=S(28);else if(prev!=null&&!it.Continued)y+=S(16);
   if(it.MeasuredFor!=w){it.MeasuredFor=w;if(it.Kind==ChatKind.Text){it.Words=LayoutWords(it.Text,w,out var h,it.Edited);it.BodyHeight=h;}else it.BodyHeight=Measure(it,w);}
   bool head=it.IsMessage&&!it.Continued;it.Lead=head&&it.Reply.Length>0?S(22):0;int bodyTop=y+it.Lead+S(2)+(head?S(22):0);
   int bw=it.Kind switch{ChatKind.Picture=>it.Thumb!.Width,ChatKind.Video=>Math.Min(w,S(400)),ChatKind.Sticker=>S(160),_=>w};
   it.Y=y;
   if(it.Kind==ChatKind.Welcome){it.Body=new Rectangle(S(16),y,w,it.BodyHeight);it.Height=it.BodyHeight;}
   else{it.Body=new Rectangle(Gutter+(it.Kind==ChatKind.Text?padX:0),bodyTop,bw,it.BodyHeight);int bottom=bodyTop-y+it.BodyHeight;
    it.Pills=it.Reactions.Count>0?LayoutPills(it.Reactions,bodyTop+it.BodyHeight+S(6),w):null;if(it.Pills!=null)bottom=it.Pills[^1].R.Bottom-y;
    it.Height=Math.Max(head?it.Lead+S(46):0,bottom+S(3));}
   y+=it.Height;prev=it;}
  total=y+S(20);scroll=Math.Clamp(scroll,0,MaxScroll);}
 // Reaction pills: emoji and count, wrapping onto more rows if needed.
 static readonly Font emojiFont=new("Segoe UI Emoji",10.5f);
 List<(Rectangle,Reaction)> LayoutPills(List<Reaction> list,int top,int w){var pills=new List<(Rectangle,Reaction)>();int x=Gutter,y=top,h=S(26);
  foreach(var r in list){int pw=TextRenderer.MeasureText(r.E,emojiFont,Size.Empty,Exact).Width+TextRenderer.MeasureText(r.N.Count.ToString(),Aero.SmallBold,Size.Empty,Exact).Width+S(26);
   if(x>Gutter&&x+pw>Gutter+w){x=Gutter;y+=h+S(4);}pills.Add((new Rectangle(x,y,pw,h),r));x+=pw+S(4);}
  return pills;}
 int Measure(ChatItem it,int w)=>it.Kind switch{
  ChatKind.Sticker=>S(160),ChatKind.Picture=>it.Thumb!.Height,ChatKind.Video=>Math.Min(w,S(400))*9/16,ChatKind.Welcome=>S(170),
  ChatKind.Nudge=>TextRenderer.MeasureText(it.Text,Aero.ChatBold,new Size(w,int.MaxValue),Wrap).Height,
  _=>TextRenderer.MeasureText(it.Text.Length>0?it.Text:" ",Aero.Chat,new Size(w,int.MaxValue),Wrap).Height};

 // Word layout: wraps at spaces, keeps line breaks, splits words too long for a line. Links and @names are marked.
 static readonly Regex Tokens=new(@"\n|[^\s]+",RegexOptions.Compiled),Url=new(@"^(https?://|www\.)[^\s<>""]+$",RegexOptions.IgnoreCase|RegexOptions.Compiled);
 const TextFormatFlags Exact=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine;
 int lineH,spaceW,padX;
 static int TextW(string s)=>TextRenderer.MeasureText(s,Aero.Chat,Size.Empty,Exact).Width;
 List<Word> LayoutWords(string text,int maxW,out int height,bool edited=false){
  if(lineH==0){lineH=TextRenderer.MeasureText("Ag",Aero.Chat,Size.Empty,Exact).Height;spaceW=TextW("a a")-TextW("aa");padX=(TextRenderer.MeasureText("a",Aero.Chat).Width-TextW("a"))/2;}
  int step=lineH+S(3),x=0,y=0;var list=new List<Word>();
  foreach(Match m in Tokens.Matches(text.Replace("\r",""))){
   if(m.Value=="\n"){if(list.Count>0)list[^1]=list[^1] with{Sep="\n"};x=0;y+=step;continue;}
   var token=m.Value;var trimmed=token.TrimEnd('.',',',')','!','?',';',':');
   string? href=Url.IsMatch(trimmed)?(trimmed.StartsWith("www.",StringComparison.OrdinalIgnoreCase)?"https://"+trimmed:trimmed):null;bool mention=token.Length>1&&token[0]=='@';
   while(token.Length>0){int w=TextW(token);if(x>0&&x+w>maxW){x=0;y+=step;}
    var piece=token;if(w>maxW){int lo=1,hi=token.Length;while(lo<hi){int mid=(lo+hi+1)/2;if(TextW(token[..mid])<=maxW)lo=mid;else hi=mid-1;}piece=token[..lo];w=TextW(piece);}
    token=token[piece.Length..];list.Add(new Word(new Rectangle(x,y,w,lineH),piece,href,mention,token.Length>0?"":" "));
    if(token.Length>0){x=0;y+=step;}else x+=w+spaceW;}}
  if(list.Count>0&&list[^1].Sep==" ")list[^1]=list[^1] with{Sep=""};
  if(edited){int ew=TextW("(edited)");if(x>0&&x+ew>maxW){x=0;y+=step;}list.Add(new Word(new Rectangle(x,y,ew,lineH),"(edited)",null,false,"",true));}
  height=Math.Max(lineH,(list.Count>0?list.Max(v=>v.R.Bottom):0));if(text.EndsWith('\n'))height+=step;return list;}

 // Selection helpers.
 static int Compare((int I,int W) a,(int I,int W) b)=>a.I!=b.I?a.I.CompareTo(b.I):a.W.CompareTo(b.W);
 bool Selected(int i,int w){if(!hasSelection)return false;var (lo,hi)=Compare(anchor,focus)<=0?(anchor,focus):(focus,anchor);return Compare((i,w),lo)>=0&&Compare((i,w),hi)<=0;}
 public bool HasSelection=>hasSelection;
 public string SelectedText(){if(!hasSelection)return "";var (lo,hi)=Compare(anchor,focus)<=0?(anchor,focus):(focus,anchor);var sb=new StringBuilder();
  for(int i=lo.I;i<=hi.I&&i<items.Count;i++){var words=items[i].Words;if(words==null||words.Count==0)continue;if(sb.Length>0)sb.Append("\r\n");
   int from=i==lo.I?lo.W:0,to=i==hi.I?Math.Min(hi.W,words.Count-1):words.Count-1;
   for(int k=from;k<=to;k++){if(words[k].Note)continue;sb.Append(words[k].Text);if(k<to&&!words[k+1].Note)sb.Append(words[k].Sep=="\n"?"\r\n":words[k].Sep);}}
  return sb.ToString();}
 public void CopySelection(){var text=SelectedText();if(text.Length>0)Clipboard.SetText(text);}
 // The word under a point (list coordinates); with nearest, the closest word of the text message under it.
 (int I,int W)? WordAt(Point p,bool nearest){int i=HitIndex(p.Y);if(i<0||items[i].Words is not {Count:>0} words)return null;var it=items[i];
  var local=new Point(p.X-it.Body.X,p.Y-it.Body.Y);
  for(int k=0;k<words.Count;k++){var r=words[k].R;r.Inflate(spaceW/2+1,S(2));if(r.Contains(local))return (i,k);}
  if(!nearest)return null;
  int best=0;long bestD=long.MaxValue;for(int k=0;k<words.Count;k++){var r=words[k].R;long dy=local.Y<r.Top?r.Top-local.Y:local.Y>r.Bottom?local.Y-r.Bottom:0,dx=local.X<r.Left?r.Left-local.X:local.X>r.Right?local.X-r.Right:0;long d=dy*10000+dx;if(d<bestD){bestD=d;best=k;}}
  return (i,best);}
 // Name and picture at the top of a message group.
 bool OnProfile(ChatItem it,Point p){if(!it.IsMessage||it.Continued)return false;var pic=new Rectangle(S(16),it.Y+it.Lead+S(2),S(40),S(40));
  var name=Names.Split(it.Who).Name;var n=new Rectangle(Gutter,it.Y+it.Lead,TextRenderer.MeasureText(name,Aero.ChatBold,Size.Empty,TextFormatFlags.NoPrefix).Width,S(22));return pic.Contains(p)||n.Contains(p);}
 // The hover toolbar (top right of the message under the mouse): react, reply, edit, delete. Client coordinates.
 internal List<(Rectangle R,string Act)> Toolbar(){var list=new List<(Rectangle,string)>();if(hover<0||hover>=items.Count)return list;var it=items[hover];if(!it.IsMessage||it.Id.Length==0)return list;
  var acts=new List<string>();if(CanRate(it)){acts.Add("up");acts.Add("down");}if(CanReact())acts.Add("react");if(CanReply())acts.Add("reply");if(CanEdit()&&Own(it)){if(it.Kind==ChatKind.Text)acts.Add("edit");acts.Add("delete");}
  int bw=S(32),h=S(30),x=ClientSize.Width-S(28)-bw*acts.Count,y=Math.Max(S(2),it.Y-scroll+it.Lead-S(12));
  foreach(var a in acts){list.Add((new Rectangle(x,y,bw,h),a));x+=bw;}return list;}
 static string Tip(string act)=>act switch{"up"=>"Good response","down"=>"Bad response","react"=>"Add reaction","reply"=>"Reply","edit"=>"Edit","delete"=>"Delete",_=>""};
 (ChatItem It,Reaction R,Rectangle At)? PillAt(Point p){int i=HitIndex(p.Y);if(i<0||items[i].Pills is not {} pills)return null;foreach(var (r,what) in pills)if(r.Contains(p))return (items[i],what,r);return null;}
 Rectangle ReplyQuote(ChatItem it)=>it.Lead>0?new Rectangle(Gutter,it.Y,Math.Max(0,ClientSize.Width-Gutter-S(28)),it.Lead):Rectangle.Empty;
 void Act(string act,ChatItem it,Point screen){switch(act){case "up" or "down":Rated?.Invoke(it,act=="up");break;case "react":ReactClicked?.Invoke(it,null,screen);break;case "reply":ReplyClicked?.Invoke(it);break;case "edit":EditClicked?.Invoke(it);break;case "delete":DeleteClicked?.Invoke(it);break;}}
 void SetTip(string text){if(text==tipText)return;tipText=text;tip.SetToolTip(this,text.Length>0?text:null);}
 static void OpenLink(string href){if(!href.StartsWith("http://",StringComparison.OrdinalIgnoreCase)&&!href.StartsWith("https://",StringComparison.OrdinalIgnoreCase))return;
  try{Process.Start(new ProcessStartInfo(href){UseShellExecute=true});}catch(Exception e) when(e is System.ComponentModel.Win32Exception or InvalidOperationException){}}
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.Control&&e.KeyCode==Keys.C&&hasSelection){CopySelection();e.Handled=true;}
  else if(e.Control&&e.KeyCode==Keys.A){SelectAll();e.Handled=true;}}
 internal void SelectAll(){var texts=items.Select((it,i)=>(it,i)).Where(x=>x.it.Words is {Count:>0}).ToList();if(texts.Count>0){anchor=(texts[0].i,0);focus=(texts[^1].i,texts[^1].it.Words!.Count-1);hasSelection=true;Invalidate();}}
 protected override void OnMouseDoubleClick(MouseEventArgs e){base.OnMouseDoubleClick(e);if(e.Button!=MouseButtons.Left||WordAt(new Point(e.X,e.Y+scroll),false) is not {} w)return;
  anchor=(w.I,0);focus=(w.I,items[w.I].Words!.Count-1);hasSelection=true;selecting=false;Invalidate();}

 int MaxScroll=>Math.Max(0,total-ClientSize.Height);
 public void ScrollToEnd(){scroll=MaxScroll;stick=true;unseen=0;Invalidate();}
 void Scrolled(){stick=scroll>=MaxScroll;if(stick)unseen=0;Invalidate();}
 protected override void OnResize(EventArgs e){base.OnResize(e);bool atEnd=stick;Relayout();if(atEnd)scroll=MaxScroll;Invalidate();}
 protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);int from=Motion.Busy(scrollKey)?scrollTarget:scroll;
  SmoothTo(from-e.Delta*S(48)/120*Math.Max(1,SystemInformation.MouseWheelScrollLines)/3);}
 Rectangle Track=>new(ClientSize.Width-S(12),S(4),S(8),ClientSize.Height-S(8));
 Rectangle Thumb(){if(total<=ClientSize.Height)return Rectangle.Empty;var t=Track;int h=Math.Max(S(32),(int)((long)t.Height*ClientSize.Height/total));
  int y=t.Y+(int)((long)(t.Height-h)*scroll/Math.Max(1,MaxScroll));return new(t.X,y,t.Width,h);}
 int HitIndex(int y){int lo=0,hi=items.Count-1;while(lo<=hi){int mid=(lo+hi)/2;var it=items[mid];if(y<it.Y)hi=mid-1;else if(y>=it.Y+it.Height)lo=mid+1;else return mid;}return -1;}
 ChatItem? At(Point p){int i=HitIndex(p.Y+scroll);return i>=0?items[i]:null;}
 static bool HasMedia(ChatItem it)=>it.Kind is ChatKind.Picture or ChatKind.Video;
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);dragged=false;if(e.Button!=MouseButtons.Left)return;var th=Thumb();
  if(!jump.IsEmpty&&jump.Contains(e.Location)){dragged=true;SmoothTo(MaxScroll,420);return;}
  foreach(var (r,act) in Toolbar())if(r.Contains(e.Location)){dragged=true;Act(act,items[hover],PointToScreen(new Point(r.Left,r.Bottom)));return;}
  var lp=new Point(e.X,e.Y+scroll);
  if(PillAt(lp) is {} pill){dragged=true;if(CanReact())ReactClicked?.Invoke(pill.It,pill.R.E,PointToScreen(new Point(pill.At.X,pill.At.Bottom-scroll)));return;}
  if(At(e.Location) is {Lead:>0} quoted&&ReplyQuote(quoted).Contains(lp)){dragged=true;if(Find(quoted.Reply) is {} original)JumpTo(original);return;}
  if(th.Contains(e.Location)){dragFrom=e.Y;dragScroll=scroll;}
  else if(!th.IsEmpty&&Track.Contains(e.Location)){scroll=Math.Clamp(scroll+(e.Y<th.Y?-1:1)*ClientSize.Height*9/10,0,MaxScroll);Scrolled();}
  else{pressAt=e.Location;if(hasSelection){hasSelection=false;Invalidate();}
   if(WordAt(new Point(e.X,e.Y+scroll),false) is {} w){anchor=focus=w;selecting=true;Focus();}}}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);
  if(dragFrom>=0){dragged=true;var th=Thumb();scroll=Math.Clamp(dragScroll+(int)((long)(e.Y-dragFrom)*MaxScroll/Math.Max(1,Track.Height-th.Height)),0,MaxScroll);Scrolled();return;}
  if(selecting&&e.Button==MouseButtons.Left){if(Math.Abs(e.X-pressAt.X)+Math.Abs(e.Y-pressAt.Y)>S(4))dragged=true;
   if(e.Y<0)scroll=Math.Max(0,scroll-S(20));else if(e.Y>ClientSize.Height)scroll=Math.Min(MaxScroll,scroll+S(20));
   if(dragged&&WordAt(new Point(e.X,Math.Clamp(e.Y,0,ClientSize.Height-1)+scroll),true) is {} w){focus=w;hasSelection=true;Invalidate();}return;}
  var bar=Toolbar();var onBar=bar.FirstOrDefault(b=>b.R.Contains(e.Location));
  if(onBar.Act==null){int i=HitIndex(e.Y+scroll);if(i!=hover){hover=i;Invalidate();}}else Invalidate(onBar.R);
  var it=hover>=0&&hover<items.Count?items[hover]:null;var p=new Point(e.X,e.Y+scroll);var word=onBar.Act==null&&it?.Words!=null?WordAt(p,false):null;
  var link=word is {} wd?items[wd.I].Words![wd.W].Href:null;if(link!=hoverLink){hoverLink=link;Invalidate();}
  var pill=onBar.Act==null?PillAt(p):null;bool quote=it is {Lead:>0}&&ReplyQuote(it).Contains(p);
  SetTip(onBar.Act!=null?Tip(onBar.Act):pill is {} pl?string.Join(", ",pl.R.N.Select(n=>Names.Split(n).Name))+" reacted with "+pl.R.E:"");
  Cursor=onBar.Act!=null||pill!=null||quote||!jump.IsEmpty&&jump.Contains(e.Location)||link!=null||it!=null&&(HasMedia(it)&&it.Body.Contains(p)||OnProfile(it,p))?Cursors.Hand:word!=null?Cursors.IBeam:Cursors.Default;}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);SetTip("");if(hover>=0||hoverLink!=null){hover=-1;hoverLink=null;Invalidate();}}
 protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);bool wasDrag=dragFrom>=0;dragFrom=-1;selecting=false;if(wasDrag||dragged)return;var it=At(e.Location);var p=new Point(e.X,e.Y+scroll);
  var word=WordAt(p,false);var link=word is {} wd?items[wd.I].Words![wd.W].Href:null;
  if(e.Button==MouseButtons.Left&&link!=null)OpenLink(link);
  else if(e.Button==MouseButtons.Left&&it!=null&&OnProfile(it,p))ProfileClicked?.Invoke(it.Who,PointToScreen(new Point(Gutter,it.Y-scroll+S(24))));
  else if(e.Button==MouseButtons.Left&&it!=null&&HasMedia(it)&&it.Body.Contains(p))Open(it);
  else if(e.Button==MouseButtons.Right){var m=DarkMenu.Create();
   if(hasSelection)m.Item("Copy",CopySelection);
   if(link!=null){m.Item("Open link",()=>OpenLink(link));m.Item("Copy link",()=>Clipboard.SetText(link));}
   if(it is {Kind:not (ChatKind.Welcome or ChatKind.Picture or ChatKind.Video or ChatKind.Sticker)})m.Item(hasSelection?"Copy whole message":"Copy text",()=>Clipboard.SetText(it.Text));
   if(it is {IsMessage:true,Id.Length:>0}&&CanRate(it)){m.Item("Good response",()=>Rated?.Invoke(it,true));m.Item("Bad response",()=>Rated?.Invoke(it,false));m.Item("Rate your experience with Groki…",()=>SurveyClicked?.Invoke());}
   if(it is {IsMessage:true,Id.Length:>0}){var at=Cursor.Position;
    if(CanReact())m.Item("Add reaction",()=>ReactClicked?.Invoke(it,null,at));
    if(CanReply())m.Item("Reply",()=>ReplyClicked?.Invoke(it));
    if(CanEdit()&&Own(it)){if(it.Kind==ChatKind.Text)m.Item("Edit message",()=>EditClicked?.Invoke(it));m.Item("Delete message",()=>DeleteClicked?.Invoke(it),danger:true);}}
   if(it is {IsMessage:true})m.Item("View profile",()=>ProfileClicked?.Invoke(it.Who,Cursor.Position));
   if(it!=null&&HasMedia(it))m.Item(it.Kind==ChatKind.Video?"Play video":"Open picture",()=>Open(it));
   ExtendMenu?.Invoke(m,it);if(m.Items.Count>0)m.Show(this,e.Location);}}
 void Open(ChatItem it){if(it.Attachment?.Data==null){Problem?.Invoke("That picture or video is no longer kept in memory.");return;}
  Form viewer=Media.IsVideo(it.Attachment.Mime)?new VideoViewer(it.Attachment):new ImageViewer(it.Attachment);viewer.Show(FindForm());}

 static string Stamp(DateTime at)=>at.Date==DateTime.Today?$"Today at {at:HH:mm}":at.Date==DateTime.Today.AddDays(-1)?$"Yesterday at {at:HH:mm}":$"{at:dd/MM/yyyy HH:mm}";
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(BackColor);long now=Environment.TickCount64;
  double reveal=Math.Min(1,(now-revealed)/(double)RevealMs);if(reveal<1)g.TranslateTransform(0,(float)((1-Motion.Out(reveal))*S(10)));
  for(int i=0;i<items.Count;i++){var it=items[i];int top=it.Y-scroll;if(top+it.Height<-S(48))continue;if(top-S(48)>ClientSize.Height)break;
   // A new message rises a little and fades in (a veil of the background colour that clears).
   double age=it.Born>0?(now-it.Born)/(double)AppearMs:1;
   if(age<1){double t=Motion.Out(age);var st=g.Save();g.TranslateTransform(0,(float)((1-t)*S(14)));Draw(g,it,top,i==hover);g.Restore(st);
    using var veil=new SolidBrush(Color.FromArgb((int)((1-t)*255),BackColor));g.FillRectangle(veil,0,top-S(4),ClientSize.Width,it.Height+S(20));}
   else Draw(g,it,top,i==hover);}
  if(reveal<1){g.ResetTransform();using var veil=new SolidBrush(Color.FromArgb((int)((1-Motion.Out(reveal))*255),BackColor));g.FillRectangle(veil,ClientRectangle);}
  var th=Thumb();if(!th.IsEmpty){g.SmoothingMode=SmoothingMode.AntiAlias;var t=Track;
   using(var p=Aero.Round(t,t.Width/2f))using(var b=new SolidBrush(Aero.Side))g.FillPath(b,p);using(var p=Aero.Round(th,th.Width/2f))using(var b=new SolidBrush(Aero.Steel))g.FillPath(b,p);}
  // While scrolled up: a bar at the bottom to jump back, with the count of messages that arrived meanwhile.
  jump=Rectangle.Empty;if(!stick&&(unseen>0||scroll<MaxScroll-ClientSize.Height)){g.SmoothingMode=SmoothingMode.AntiAlias;
   jump=new Rectangle(S(16),ClientSize.Height-S(34),Math.Max(0,ClientSize.Width-S(44)),S(28));
   using(var p=Aero.Round(jump,S(4)))using(var b=new LinearGradientBrush(new Rectangle(jump.X,jump.Y-1,jump.Width,jump.Height+2),unseen>0?Aero.AccentHover:Aero.Steel,unseen>0?Aero.AccentDown:Aero.Side,90f))g.FillPath(b,p);
   const TextFormatFlags one=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter;
   TextRenderer.DrawText(g,unseen>0?$"{unseen} new message{(unseen==1?"":"s")}":"You're viewing older messages",Aero.SmallBold,new Rectangle(jump.X+S(12),jump.Y,jump.Width/2,jump.Height),Color.White,one);
   TextRenderer.DrawText(g,"Jump to present  "+Aero.IconDown,Aero.SmallBold,new Rectangle(jump.X,jump.Y,jump.Width-S(12),jump.Height),Color.White,one|TextFormatFlags.Right);}
  DrawToolbar(g);}
 void DrawToolbar(Graphics g){var bar=Toolbar();if(bar.Count==0)return;g.SmoothingMode=SmoothingMode.AntiAlias;var all=Rectangle.Union(bar[0].R,bar[^1].R);
  using(var path=Aero.Round(all,S(6))){using(var b=new SolidBrush(Aero.Side))g.FillPath(b,path);using var p=new Pen(Aero.Line);g.DrawPath(p,path);}
  var mouse=PointToClient(Cursor.Position);
  foreach(var (r,act) in bar){bool hot=r.Contains(mouse);if(hot){var inner=Rectangle.Inflate(r,-S(3),-S(3));using var path=Aero.Round(inner,S(4));using var b=new SolidBrush(Aero.SideHover);g.FillPath(b,path);}
   var glyph=act switch{"up"=>Aero.IconLike,"down"=>Aero.IconDislike,"react"=>Aero.IconEmoji,"reply"=>Aero.IconReply,"edit"=>Aero.IconEdit,_=>Aero.IconDelete};
   var rated=act is "up" or "down"&&RatingOf(items[hover])==act;
   TextRenderer.DrawText(g,glyph,Aero.Icons(12f),r,rated?(act=="up"?Aero.Accent:Aero.Red):act=="delete"&&hot?Aero.Red:hot?Aero.Bright:Aero.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);}}
 void Draw(Graphics g,ChatItem it,int top,bool hot){int w=ClientSize.Width;var body=it.Body;body.Offset(0,-scroll);
  if(it.Divider){int ly=top-S(20);using(var p=new Pen(Aero.Line))g.DrawLine(p,S(16),ly,w-S(16),ly);var label=it.At.ToString("MMMM d, yyyy");var sz=TextRenderer.MeasureText(label,Aero.SmallBold);
   var r=new Rectangle((w-sz.Width)/2-S(4),ly-sz.Height/2,sz.Width+S(8),sz.Height);using(var b=new SolidBrush(BackColor))g.FillRectangle(b,r);
   TextRenderer.DrawText(g,label,Aero.SmallBold,r,Aero.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);}
  if(it.IsNew){int ly=it.Divider?top-S(20):top-S(14);g.SmoothingMode=SmoothingMode.None;if(!it.Divider)using(var p=new Pen(Aero.Red,Math.Max(1,S(1))))g.DrawLine(p,S(16),ly,w-S(70),ly);g.SmoothingMode=SmoothingMode.AntiAlias;
   var pill=new Rectangle(w-S(60),ly-S(8),S(40),S(16));using(var path=Aero.Round(pill,S(3)))using(var b=new SolidBrush(Aero.Red))g.FillPath(b,path);
   TextRenderer.DrawText(g,"NEW",Aero.Caps,pill,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);}
  if(it.Kind==ChatKind.Welcome){DrawWelcome(g,it,top);return;}
  bool mention=it.Kind==ChatKind.Text&&Me.Length>0&&it.Who!=Me&&it.Text.Contains("@"+Names.Split(Me).Name,StringComparison.OrdinalIgnoreCase);
  var row=new Rectangle(0,top,w-S(16),it.Height);
  if(mention){using(var b=new SolidBrush(Aero.C(0xF0B232,hot?40:26)))g.FillRectangle(b,row);using(var b=new SolidBrush(Aero.Yellow))g.FillRectangle(b,0,top,S(2),it.Height);}
  else if(hot)using(var b=new SolidBrush(Aero.Hover))g.FillRectangle(b,row);
  if(IsCurrentMatch(it)){using(var b=new SolidBrush(Aero.C(0xE9B44C,30)))g.FillRectangle(b,row);using(var b=new SolidBrush(Aero.Yellow))g.FillRectangle(b,0,top,S(3),it.Height);}
  if(it==flash){double age=(Environment.TickCount64-flashAt)/1600.0;if(age<1){using var b=new SolidBrush(Aero.C(0x1A9FFF,(int)(70*(1-Motion.InOut(age)))));g.FillRectangle(b,row);}}
  if(it.Lead>0)DrawQuote(g,it,top);
  int lead=it.Lead;top+=lead;
  const TextFormatFlags one=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine;
  switch(it.Kind){
   case ChatKind.Notice or ChatKind.Nudge or ChatKind.Problem:
    var (glyph,color)=it.Kind switch{ChatKind.Nudge=>(Aero.IconBolt,Aero.Yellow),ChatKind.Problem=>("",Aero.Red),
     _=>it.Text.EndsWith("joined.")||it.Text.StartsWith("Rejoined")?("",Aero.Green):it.Text.EndsWith("left.")||it.Text.StartsWith("Disconnected")?("",Aero.Red):("",Aero.Muted)};
    TextRenderer.DrawText(g,glyph,Aero.Icons(11f),new Rectangle(S(16),body.Y,S(40),S(20)),color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|one);
    TextRenderer.DrawText(g,it.Text,it.Kind==ChatKind.Nudge?Aero.ChatBold:Aero.Chat,body,it.Kind switch{ChatKind.Nudge=>Aero.Bright,ChatKind.Problem=>Aero.Red,_=>Aero.Muted},Wrap);
    if(hot)TextRenderer.DrawText(g,$"{it.At:HH:mm}",Aero.Small,new Rectangle(w-S(80),body.Y,S(60),S(20)),Aero.Faint,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|one);return;}
  if(!it.Continued){var (name,tag)=Names.Split(it.Who);Avatars.Draw(g,new RectangleF(S(16),top+S(2),S(40),S(40)),name,AvatarFor(it.Who));
   var nameSize=TextRenderer.MeasureText(name,Aero.ChatBold,Size.Empty,TextFormatFlags.NoPrefix);int x=Gutter;TextRenderer.DrawText(g,name,Aero.ChatBold,new Point(x,top+S(1)),Aero.NameColor(it.Who),one);
   x+=nameSize.Width;if(tag.Length>0)x+=Badge.Draw(g,tag,x,top+S(1)+nameSize.Height/2,this)+S(4);
   TextRenderer.DrawText(g,Stamp(it.At),Aero.Small,new Rectangle(x,top+S(1),S(200),nameSize.Height),Aero.Muted,TextFormatFlags.VerticalCenter|one);}
  else if(hot)TextRenderer.DrawText(g,$"{it.At:HH:mm}",Aero.Small,new Rectangle(0,body.Y,Gutter-S(10),S(22)),Aero.Faint,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|one);
  switch(it.Kind){
   case ChatKind.Text:DrawWords(g,it,body);break;
   case ChatKind.Sticker:g.DrawImage(StickerArt.Draw(it.Text,S(160)),body.X,body.Y,S(160),S(160));break;
   case ChatKind.Picture:{g.SmoothingMode=SmoothingMode.AntiAlias;using var clip=Aero.Round(body,S(8));var st=g.Save();g.SetClip(clip);g.DrawImage(it.Thumb!,body);g.Restore(st);
    if(it.Attachment?.Data==null)using(var veil=new SolidBrush(Aero.C(0x000000,120)))g.FillPath(veil,clip);break;}
   case ChatKind.Video:{bool gone=it.Attachment?.Data==null;if(it.Thumb==null||it.Thumb.Width!=body.Width||it.Thumb.Height!=body.Height||gone!=it.CardGone){it.Thumb?.Dispose();it.Thumb=Pictures.VideoCard(it.Attachment?.Size??0,body.Width,body.Height,gone);it.CardGone=gone;}
    g.DrawImage(it.Thumb,body);break;}}
  if(it.Pills!=null)DrawPills(g,it);}
 void DrawPills(Graphics g,ChatItem it){g.SmoothingMode=SmoothingMode.AntiAlias;var mouse=PointToClient(Cursor.Position);
  foreach(var (rect,what) in it.Pills!){var r=rect;r.Offset(0,-scroll);bool mine=what.N.Contains(Me),hot=r.Contains(mouse);
   using(var path=Aero.Round(r,S(8))){using(var b=new SolidBrush(mine?Aero.C(0x1A9FFF,hot?80:50):hot?Aero.SideHover:Aero.Side))g.FillPath(b,path);
    using var p=new Pen(mine?Aero.Accent:hot?Aero.Steel:Aero.Line);g.DrawPath(p,path);}
   var ew=TextRenderer.MeasureText(what.E,emojiFont,Size.Empty,Exact).Width;
   TextRenderer.DrawText(g,what.E,emojiFont,new Rectangle(r.X+S(9),r.Y,ew,r.Height),Aero.Bright,Exact|TextFormatFlags.VerticalCenter);
   TextRenderer.DrawText(g,what.N.Count.ToString(),Aero.SmallBold,new Rectangle(r.X+S(9)+ew+S(6),r.Y,r.Width,r.Height),mine?Aero.Bright:Aero.Text,Exact|TextFormatFlags.VerticalCenter);}}
 // Discord-style "replying to" line: a curved spine from the avatar, the original's author and the start of their message.
 void DrawQuote(Graphics g,ChatItem it,int top){int mid=top+S(11),x=Gutter;g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var p=new Pen(Aero.Steel,Math.Max(1,S(2))))using(var spine=new GraphicsPath()){spine.AddLine(S(36),top+S(24),S(36),mid+S(6));spine.AddArc(S(36),mid,S(12),S(12),180,90);spine.AddLine(S(42),mid,x-S(6),mid);g.DrawPath(p,spine);}
  const TextFormatFlags one=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter;
  var row=new Rectangle(x,top,Math.Max(0,ClientSize.Width-x-S(28)),S(22));
  if(Find(it.Reply) is not {} original){TextRenderer.DrawText(g,deleted.Contains(it.Reply)?"Original message was deleted":"Original message isn't loaded",Aero.Small,row,Aero.Faint,one);return;}
  var (name,_)=Names.Split(original.Who);Avatars.Draw(g,new RectangleF(x,mid-S(8),S(16),S(16)),name,AvatarFor(original.Who));x+=S(22);
  var nw=TextRenderer.MeasureText(name,Aero.SmallBold,Size.Empty,TextFormatFlags.NoPrefix).Width;
  TextRenderer.DrawText(g,name,Aero.SmallBold,new Rectangle(x,top,nw,S(22)),Aero.NameColor(original.Who),one);x+=nw+S(4);
  bool hot=ReplyQuote(it).Contains(PointToClient(Cursor.Position) with{Y=PointToClient(Cursor.Position).Y+scroll});
  TextRenderer.DrawText(g,Snippet(original),Aero.Small,new Rectangle(x,top,Math.Max(0,row.Right-x),S(22)),hot?Aero.Bright:Aero.Muted,one);}
 static Font? underline;
 void DrawWords(Graphics g,ChatItem it,Rectangle body){if(it.Words==null)return;int index=items.IndexOf(it);g.SmoothingMode=SmoothingMode.AntiAlias;
  for(int k=0;k<it.Words.Count;k++){var word=it.Words[k];var r=word.R;r.Offset(body.X,body.Y);if(r.Bottom<0||r.Top>ClientSize.Height)continue;
   if(Selected(index,k)){var sel=r;sel.Width+=word.Sep==" "?spaceW:0;using var b=new SolidBrush(Aero.C(0x1A9FFF,110));g.FillRectangle(b,sel);}
   else if(query.Length>0&&!word.Note&&word.Text.Contains(query,StringComparison.OrdinalIgnoreCase)){var pill=Rectangle.Inflate(r,S(2),1);using var path=Aero.Round(pill,S(3));using var b=new SolidBrush(Aero.C(0xE9B44C,IsCurrentMatch(it)?190:90));g.FillPath(b,path);}
   else if(word.Mention){var pill=Rectangle.Inflate(r,S(2),1);using var path=Aero.Round(pill,S(3));using var b=new SolidBrush(Aero.C(0x1A9FFF,55));g.FillPath(b,path);}
   var font=word.Href!=null&&word.Href==hoverLink?underline??=new Font(Aero.Chat,FontStyle.Underline):Aero.Chat;
   TextRenderer.DrawText(g,word.Text,font,r.Location,word.Note?Aero.Faint:word.Href!=null?Aero.Link:word.Mention?Aero.Bright:Aero.Text,Exact);}}
 void DrawWelcome(Graphics g,ChatItem it,int top){var c=new RectangleF(S(16),top+S(8),S(68),S(68));g.SmoothingMode=SmoothingMode.AntiAlias;
  if(it.Who.StartsWith('#')){using(var b=new SolidBrush(Aero.Selected))g.FillEllipse(b,c);TextRenderer.DrawText(g,"#",Aero.Big,Rectangle.Round(c),Aero.Bright,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);}
  else Avatars.Draw(g,c,Names.Split(it.Who).Name,AvatarFor(it.Who));
  TextRenderer.DrawText(g,it.Title,Aero.Big,new Point(S(14),top+S(86)),Aero.Bright,TextFormatFlags.NoPrefix);
  TextRenderer.DrawText(g,it.Text,Aero.Body,new Rectangle(S(16),top+S(128),ClientSize.Width-S(40),S(40)),Aero.Muted,Wrap);}
}
