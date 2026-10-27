# Friends Chat

A visible Windows group-chat prototype for a small group of friends on the same local network. One laptop temporarily hosts; others join using its IPv4 address, room code, and verified certificate fingerprint.

## Get the app

Open this repository's **Releases**, select the test release, and download **FriendsChat-Test-Download-Windows-x64.zip** from its assets. Extract the ZIP and open **FriendsChat.exe**. No .NET installation is required. This build targets Windows x64.

The repository's **Code > Download ZIP** contains source, not the ready-to-run application. Use the release asset to test without development tools.

## Quick test

1. Connect both laptops to the same iPhone hotspot.
2. Open FriendsChat.exe on both laptops and enter display names.
3. The host clicks **Host Chat**, then shares its Wi-Fi IPv4 address, room code, and full SHA-256 fingerprint directly with the friend.
4. The friend compares all fingerprint characters against the host's screen, fills in the three join fields, and clicks **Join Chat**.
5. Send a message in each direction. Keep the host laptop awake and its app open.
6. **Disconnect / Stop** ends the session. A new room has new credentials.

See [the testing guide](docs/Testing-guide.md) for detailed steps and firewall/hotspot troubleshooting. Same-hotspot internet access does not guarantee local laptop-to-laptop connectivity. The app does not modify firewall or security settings. An owner/administrator may need to approve an app-specific inbound exception; do not disable the firewall or bypass device restrictions.

## Security and scope

TLS 1.2/1.3 encrypts connections. A mandatory SHA-256 certificate fingerprint pins the host identity before the joining client sends its room code. Obtain that fingerprint directly from the host screen or a separately trusted channel. A random room code controls entry.

The host can read every message; this is not participant-to-participant end-to-end encryption. Names are self-chosen. Messages remain in application memory, with bounded visible history and no deliberate message logs. Histories clear on disconnect; new joiners receive only new messages. Windows paging/crash reporting is outside this guarantee.

The executable is unsigned. If device security blocks it, use the approved software-review process. No installer, startup entry, hidden service, monitoring evasion, or security-setting changes are included.

## Build from source

Requires Windows x64 and .NET 10 SDK. From the repository folder:

```powershell
./build.ps1
```

If PowerShell policy prevents scripts, follow your device's approved process or run these individual development commands instead:

```powershell
dotnet run --project src/tests/Tests.csproj -c Release
dotnet publish src/FriendsChat.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o artifacts/release
```

The script runs protocol tests and creates `artifacts/FriendsChat-Test-Download-Windows-x64.zip`. Build files are ignored by Git. The included test report records local protocol checks; actual two-laptop iPhone-hotspot testing remains necessary.

## Maintainer: publish a test download

1. Upload this folder's contents to your GitHub repository. README.md and src should appear at the repository root.
2. Open **Releases** and create a new release with a tag such as **v0.1.0-test**.
3. Attach the separately supplied **FriendsChat-Test-Download-Windows-x64.zip** as a release asset. Use RELEASE-NOTES.md for the description.
4. Publish the release and send its link to your friend. If the repository is private, your friend needs repository access.

Do not commit room codes, fingerprints from active sessions, or chat transcripts. Each host session generates its own credentials.

