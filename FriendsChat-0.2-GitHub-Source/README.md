# Friends Chat 0.2

Visible Windows x64 local group chat with TLS, verified invitations, optional local room discovery, and memory-only message history.

## Download and join

Get **FriendsChat-0.2-Windows-x64.zip** from Releases. Extract it and open FriendsChat.exe. Use this version on both laptops.

Host: choose the hotspot Wi-Fi adapter, click Host Chat, then Copy invitation. Share the invitation privately through a trusted channel.

Friend: paste it, click Use invitation, confirm it came directly from the host, then Join Chat. No separate address/code/fingerprint copying is needed.

Find local chats helps locate room addresses. Discovery is unverified and does not share room codes or replace host verification. Check connection tests TCP reachability. [Read the testing guide](docs/TESTING.md) for verification, Norton and hotspot limitations.

## Build

Requires Windows and .NET 10 SDK:

```powershell
./build.ps1
```

This runs the focused discovery/invitation/local-host tests and creates artifacts/FriendsChat-0.2-Windows-x64.zip. To additionally require full TLS integration tests:

```powershell
./build.ps1 -ProtocolTests
```

Those TLS tests currently fail on the development machine because Norton Web/Mail Shield replaces the host certificate. This is correctly rejected by fingerprint pinning. No security setting is changed. The included test report covers the seven focused tests; actual cross-laptop discovery and messaging must still be validated.

If scripts are restricted, use your approved development process; the corresponding commands are in build.ps1. Do not bypass device restrictions.

## Upload a release

Upload this folder's contents as the repository source. Create a GitHub release tagged v0.2.0-test and attach the separately supplied FriendsChat-0.2-Windows-x64.zip. Use RELEASE-NOTES.md for the release text. Send your friend the release link; Code > Download ZIP provides source, not the runnable app.

## Security scope

TLS 1.2/1.3 links are pinned to the host fingerprint in a trusted invitation. The host reads and forwards all messages; no end-to-end participant encryption. Room names and discovery responses are unverified. Invitations include a private room code: do not post them publicly. The server uses TCP 45831; optional local discovery uses UDP 45832. No firewall/antivirus changes, startup entries, hidden services or automatic installation. The executable is unsigned. Messages are not deliberately saved; OS paging/crash dumps are outside that guarantee. See the guide for details.
