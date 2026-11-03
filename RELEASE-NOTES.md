Branding update: Friends Chat is now **3DFx**. The relay protocol is unchanged and works with the existing deployed 0.3 relay.

# 3DFx Vista 0.16 — AM, the new AI

- **AM (Allied Master-computer)** replaces Groki. Add **alliedmastercomputer** as a friend to DM him, or type **@alliedmastercomputer** in a room; he shows as "Allied Mastercomputer (AI)" and calls himself AM. He has a simulated body, 40 emotions, an ego, opinions he forms himself, a model of each person he talks to, a subconscious that keeps notes, and he sleeps and dreams. He reads each message before he answers (10–20 s), sometimes sends a few messages in a row, and makes the odd typo (with a *correction).
- **Private per chat**: what you tell him in one DM or room stays in that chat; nothing from one chat reaches him in another.
- **Safety**: every line he sends passes his guard (no claiming to be human, no moving people elsewhere, no asking for personal details, no secrets, no contempt, and in 3DFx no links and no @pings), and "are you an AI?" always gets the honest answer.
- **Fixed: 3DFx piling up in the tray.** Opening 3DFx again (shortcut, Start menu, Start with Windows) now brings the running one to the front instead of starting another copy. Several copies were each signed in as you, which is what made the rest of these happen:
- **Fixed: DMs repeating.** Every copy saved each DM into the same history, so it showed several times in a row. History now keeps each message once (old repeats disappear too).
- **Fixed: notification spam.** One sound, flash and balloon for a burst of messages ("+3 more"), not one each; a friend whose connection flickers pops up as online at most once every 15 minutes.
- **Better DM connection.** After a drop it reconnects within 1–2 s (it used to wait up to a minute), and straight away when the network comes back.
- Relay 0.17: upload Relay/Program.cs and Relay/Bots.cs again (AI names are now "alliedmastercomputer,groki,bryan"; if Render has a **Bots** setting, set it to that).

# 3DFx Vista 0.15 — Bryan is now Groki

- **Bryan is now Groki**: same AI, same memories, new name and a **black hole** logo (his picture everywhere, his icon and shortcuts). Type **@groki** in a room to bring him in, and add **groki** as a contact to DM him. "bryan" stays reserved so nobody can take his old name.
- His voice is now the official 4B model (Qwen3.5-4B fine-tuned on real chats), writing 2 drafts per reply that his mind picks from; about 3–6 s per reply on a laptop.
- Relay 0.16: upload Relay/Program.cs and Relay/Bots.cs again (default AI names are now "groki,bryan"; if Render has a **Bots** setting, set it to `groki,bryan`).

# 3DFx Vista 0.14 — new look

- **Reply** to a message (it shows a "replying to" line with the original; click it to jump there), **edit** and **delete** your own messages (shown as "(edited)"; deleted for everyone), and **react** with emoji (click a reaction to add yours or take it back). Hover a message for the buttons, or right-click it. Up in an empty box edits your last message; Esc cancels. Works in rooms and DMs.
- **Feedback for Bryan**, like ChatGPT: hover one of Bryan's messages for **thumbs up / thumbs down** (a thumbs down asks what was wrong, optionally). After you've seen about a dozen of his messages, 3DFx asks once: **"How has your experience with Bryan been so far?"**. Bryan learns from every vote right away: a thumbs down makes that reply (and similar ones) less likely, a thumbs up more likely. Signed-in people only. Relay 0.15: upload Relay/Direct.cs, Relay/Program.cs and Shared/Protocol.cs again; restart Bryan with the new service.py, bryan.py and feedback.py.
- **Room history**: people who join a room late (or rejoin after a drop) see its last 100 messages and stickers. The relay keeps them in memory only, and forgets them when the room ends.

- Full redesign: a chat-app layout (rooms rail, sidebar, chat, member list) with a Steam look: steel-blue panels, capitals navigation bar across the top, blue and green gradient buttons, square avatars framed in their status colour.
- Multi-line messages (Shift+Enter; the box grows), a "Jump to present" bar when you scroll up and new messages arrive, and Alt+Up / Alt+Down to switch rooms (Ctrl+K joins one).
- **Smooth, Apple-style motion**: buttons and rows fade on hover, toggles and the room icons spring into place, scrolling glides, new messages rise in, pop-ups slide in, windows and menus fade in, and an iMessage-style typing bubble. Follows Windows' "Animation effects" setting.
- **Bryan has a logo**, shown as his picture everywhere (his service also uploads it, so older apps see it too).
- **Close to the system tray** (stay online with the window closed; right-click the tray icon to quit) and **Start with Windows** (opens quietly in the tray).
- **Reopen my rooms**: rejoins the rooms you had open when 3DFx starts (asked once; room passwords kept encrypted for your Windows account).
- **Search** the open conversation with Ctrl+F (or the magnifier): matches are highlighted; Enter / Shift+Enter step through them.
- **Text size** for messages and the message box (Settings > Appearance).
- **Windows notifications** for DMs, @mentions and nudges while 3DFx is in the background; click one to open the conversation (Settings > Notifications to turn off).
- **Clickable links** and **text selection** in messages: drag to select, double-click a message, Ctrl+A / Ctrl+C. @names are highlighted.
- A red **NEW** line where your unread messages start.
- **Profile cards**: click a name or picture for their picture, status and status line, with Message / Add friend / Mention.
- Friends grouped Online / Offline, names in status colours, and a "Sam is now online" pop-up in the corner.
- Be in **several rooms at once**; each has its own icon with unread and mention badges. Messages that mention you are highlighted.
- New message view: avatars, messages grouped per person, date lines, inline pictures, video cards and stickers, hover times.
- New **log-in screen** (log in, register, reset password, or continue as a guest) and a **Settings** window (account, profile, notifications, privacy, connection, about).
- **Friends** page (Online / All / Pending / Blocked / Add Friend); DMs now open in the main window instead of separate windows.
- Short pop-up notices replace the status bar. Dark title bar, dark menus and dialogs.
- Relay 0.14: upload **Relay/Program.cs**, **Relay/Direct.cs** and **Shared/Protocol.cs** to GitHub (Render rebuilds; open rooms end during the redeploy, accounts and DMs stay). The held-DM table gains a `reply` column by itself. Until then, the app works as before and simply hides replies, edits, reactions and history. Older apps keep working with the new relay; they just don't see the changes.

# 3DFx Vista 0.11 — Bryan, the AI member

- **@bryan** in any room brings in Bryan, a humanised AI (MindAI + MindAI_LLM). About 4 seconds to his answer, "Bryan (AI) is typing…" first.
- Add **bryan** as a contact to DM him: he reads, shows Seen, types, and sometimes leaves you on Seen.
- Always labelled as AI. His memories are kept per room and per DM conversation, on the PC that runs him.
- Relay: new Relay/Bots.cs; upload Relay/Program.cs, Relay/Accounts.cs, Relay/Direct.cs, Relay/Bots.cs, Relay/Relay.csproj, Shared/Protocol.cs, and set the BotKey environment variable on Render.

# 3DFx Vista 0.10 — 1.0 phase 3: profiles, receipts, installer

- **Your card** at the top of Contacts: picture, status (Online / Away / Busy / Appear offline) and a status line. Away switches on by itself after 10 idle minutes.
- Contacts show pictures, coloured status lights and status lines (contacts only).
- DMs: **"Sam is typing…"**, **Delivered** and **Seen**. "Show Seen" can be turned off (then you don't see others' either).
- **Setup.exe**: installs for your Windows user (no admin), Start menu shortcut, uninstaller. Installed copies check GitHub Releases at start and offer **update & restart**.
- Relay: upload Relay/Direct.cs, Relay/Program.cs and Shared/Protocol.cs (plus the phase 1–2 files and the /var/data disk if not done yet).

# 3DFx Vista 0.9 — 1.0 phase 2: direct messages

- **Contacts** gadget: add people by username; they accept first. Green dot = online. Requests, unread counts, remove and block.
- **DM windows** (double-click a contact), MSN style: text, smileys, stickers, pictures up to 5 MB.
- Offline? DMs wait on the relay until their app picks them up, then the relay deletes them (or after 30 days).
- DM history stays on your PC, encrypted for your Windows user. Right-click a DM window to clear it.
- The relay can read DMs (end-to-end encryption is planned as a later phase).
- Relay: upload Relay/Program.cs, Relay/Accounts.cs, Relay/Direct.cs (new), Relay/Relay.csproj and Shared/Protocol.cs, and add the 1 GB Render disk at /var/data (from phase 1).

# 3DFx Vista 0.8 — 1.0 phase 1: accounts

- **Create account / Sign in** on the Connect card. Your username is your verified name in every room.
- Guests can still join with the room password; they show as "Name (guest)".
- **Recovery key** at sign-up: save it to reset a forgotten password. It works once and is then replaced.
- **Account…**: change password (signs out your other devices), make a new recovery key, delete the account.
- **Keep me signed in** (encrypted for your Windows user; the password itself is never saved).
- Guessing protection: after 5 wrong passwords, each try must wait longer.
- Relay: upload Relay/Program.cs, Relay/Accounts.cs, Relay/Relay.csproj and Shared/Protocol.cs, and add a 1 GB Render disk at /var/data. Without the disk, accounts stay off and everything else works.

# 3DFx Vista 0.7

- **Pictures**: 📎 → Picture…, paste with Ctrl+V, or drag a file onto the chat. Shrunk to 1920 px and re-saved before sending (this also removes hidden GPS location). Animated GIFs keep moving.
- **Videos** up to 25 MB (MP4 or WebM). Click the card to play it inside 3DFx.
- **Stickers**: 12 glossy built-in stickers in the ☺ menu.
- Click any picture or video to open it full size; **Save…** keeps a copy. Nothing else is written to disk.
- Needs the 0.7 relay: upload **both** Relay/Program.cs and Shared/Protocol.cs, then redeploy. With an older relay, pictures, videos and stickers stay off.

# 3DFx Vista 0.6

- **Nudge**: shakes everyone's window, MSN Messenger style. One per 15 seconds per person.
- **☺ smiley picker** next to the message box.
- **"Sam is typing…"** under the message box.
- **Unread count**: a red number on the taskbar button and in the window title.
- **Startup sound**: Windows' own startup chime (can be turned off).
- Nudge and typing need the 0.6 relay (redeploy Relay/Program.cs). With an older relay they stay off; everything else works.

# 3DFx Vista 0.5

- New name: **3DFx Vista**.
- New Windows Vista look: glossy blue header with a glass orb, shiny buttons, rounded cards, Segoe UI, and a dark "In room" gadget on the right like the Vista Sidebar.
- Wrong fields get a red border and a warning balloon.
- Names in the chat are coloured; join/leave notices are grey.
- Disconnect and closing the window now tell the room straight away (before, others saw it ~10 seconds later).
- Works with the deployed 0.4 relay; no redeploy needed.

# 3DFx 0.4

- The room stays open while anyone is in it, even after the creator leaves.
- "In room" shows who is there.
- "Remember relay and name" saves those (never the password).
- New messages flash the taskbar button; optional sound for chat messages.
- After a lost connection, Rejoin goes back to the same room and keeps the conversation on screen.
- Needs the 0.4 relay for the first two; redeploy on Render.

# 3DFx 0.3.2

- A wrong field is marked on its own (relay, name, room or password). The password is no longer cleared by a room-name mistake.
- Spaces in room names become hyphens. The relay address can be pasted without https:// or with /chat.
- Lost connection keeps your messages, draft and password, and says why (creator left, too many messages, or connection lost). It never rejoins by itself.
- Relay: full rooms reject cleanly, joins and leaves are announced, fast typing drops extra messages instead of closing the room, dead connections are detected within about 40 seconds.
- Relay changes need a redeploy; the 0.3.2 app also works with the existing 0.3 relay.

# 3DFx 0.3.1

Room names and passwords replace long invitation codes. Connections now go outward through a shared HTTPS relay. No local incoming port, local IP entry, discovery or self-signed chat certificate is needed.

Deploy the included relay first, then enter its HTTPS address on both laptops. Creator: choose a room name and password, Create room. Friend: same details, Join room.

The relay can read messages. Memory-only rooms end when the creator disconnects or the relay restarts. Norton and other protections remain enabled; outbound HTTPS/WebSockets may still be inspected or blocked by policy.

Download 3DFx-0.3.1-Windows-x64.zip below. Read START-HERE.txt. A working hosted relay is required; the ZIP does not provide a running public server. Twelve local protocol tests passed; public deployment and two-laptop testing remain necessary.


