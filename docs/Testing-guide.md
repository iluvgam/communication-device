# Friends Chat — Windows prototype

Open `release/FriendsChat.exe` on each laptop. This is a self-contained Windows x64 app (about 111 MiB); a separate .NET installation is not needed. No installer, elevated app privileges, startup entry, service, tray-only mode, or firewall modification is included. Closing the window ends the process and its chat session. The bundled .NET runtime may extract normal runtime files to the user's temporary directory. The executable is unsigned; if Windows or an administrator blocks it, use the device's approved software-review process rather than bypassing restrictions.

## Test with two laptops

1. Enable Personal Hotspot / Allow Others to Join on the iPhone. Connect BOTH Windows laptops to that same Wi-Fi hotspot using its password. Apple setup: https://support.apple.com/en-us/111785
2. Copy the executable to a stable folder on each laptop, then open it normally. Choose a display name on each laptop.
3. On your laptop click **Host Chat**. Wait for **Hosting**. Keep this window open and the laptop awake. It listens on TCP port **45831** on all IPv4 interfaces while hosting.
4. Identify your hotspot Wi-Fi adapter's IPv4 address in Windows Settings > Network & internet > Wi-Fi > your connected network > Properties. The app shows local address candidates in Host details; other adapters and 127.0.0.1 may appear too. Use the Wi-Fi adapter address, not 127.0.0.1, the iPhone's address/default gateway, or a public internet IP. Do not assume a fixed 172.20.x.x address.
5. At the other laptop enter that address in **Host IPv4**, your host's **room code**, and the complete **SHA-256 fingerprint** from Host details. The details box is selectable: select/copy text or scroll if necessary. The code is 32 hexadecimal characters; the fingerprint is 64. These are separate values.
6. Verify all 64 fingerprint characters directly against the host laptop's screen (or obtain them through a separately trusted channel). Do this BEFORE clicking **Join Chat**. Do not trust a fingerprint supplied by an unknown network service. The app checks the actual TLS certificate against this value and refuses mismatches before sending the room code or name. No certificate installation is needed.
7. Click **Join Chat**. Expect **Connected • TLS encrypted • host fingerprint verified**. Send a message from each laptop and confirm both histories show it with the correct name. Enter or Send submits a message.
8. As negative tests, disconnect the joining laptop and change one fingerprint character: joining must fail. Restore the fingerprint and change a room-code character: joining must fail. Restore both and rejoin.
9. Click **Disconnect / Stop** on the host. The other laptop should report disconnected. Histories are cleared on disconnect. Starting another host room generates a NEW code and certificate; repeat verification. New arrivals see only new messages, not earlier history.

## Encryption and prototype limits

TLS 1.2 or 1.3 encrypts each laptop-to-host connection. A fresh self-signed RSA certificate is created for each room; the fingerprint is SHA-256 of its DER certificate bytes. An explicitly supplied, matching fingerprint is the trust anchor, replacing public certificate-authority/name validation for this local prototype. The random 128-bit room code controls admission after host verification. Keep it private. Host certificate keys use Windows' normal temporary key handling; no trusted-root certificate is added.

The host decrypts and forwards messages: this is NOT end-to-end encryption between participants. Anyone with the code can join; display names are self-chosen and can be duplicated. There are no accounts, identity verification, moderation controls, file transfers, background reconnect, cloud relay, or durable history. Anyone with local access to a participating device may see its messages. Messages are kept in application memory, never deliberately logged or saved; Windows paging/crash reporting and manual screenshots are outside that guarantee. Visible history is bounded and older text is trimmed. Maximum message length is 2,000 characters; at most 16 simultaneous connections including pending handshakes and the host's own client. Treat this as a small trusted-friends prototype, not an audited production messenger.

## If joining times out

A shared internet connection does not prove that the laptops can reach each other. Apple documents internet sharing, but does not promise peer-to-peer reachability for every hotspot/device configuration. Client isolation or other network filtering may prevent this local server design from working; that needs an actual two-laptop test. If that happens, try another authorized Wi-Fi/router network that permits local client traffic. There is no relay or restriction bypass in this app.

Windows Firewall may block inbound traffic to the HOST. The app does not change its settings. If the device owner/administrator approves, they can allow this specific executable through Windows Firewall for the applicable trusted network profile. A hotspot may be classified Public, so a Private-only exception may not apply. Do not turn off the firewall or broadly enable access on all public networks. Prefer an app-specific exception; an administrator can further restrict TCP 45831 to the friend's local IP/subnet. Remove any temporary exception when testing ends. Policy-managed devices may require an administrator to approve the app or rule. Microsoft's guidance: https://support.microsoft.com/en-us/windows/security/firewall/risks-of-allowing-apps-through-windows-firewall

Check both laptops are still on the same hotspot, verify the host address again after reconnecting, confirm Hosting is shown, and check the host has not slept. An address-in-use error means port 45831 is already occupied (possibly another copy of this prototype). Stop the other host window before retrying. A fingerprint error means stop and compare against the current host screen; a previous room's fingerprint will not work.

Optional read-only connectivity check on the joining laptop, in PowerShell:

    Test-NetConnection -ComputerName <host-IPv4> -Port 45831

Use the actual IP instead of the placeholder. A failed TCP test can indicate firewall, wrong address, stopped host, or hotspot filtering; it does not identify which. Ping alone is not conclusive.

## Build and validation

Built with .NET SDK 10.0.103, C# and Windows Forms on Windows x64. Build succeeded without warnings. Eight automated protocol checks passed: two TLS clients, bidirectional Unicode messaging, wrong fingerprint, wrong code, missing fingerprint, disconnect/rejoin, host stop, and fresh room credentials. The visible packaged app was exercised for Host Chat, Send/history, and Disconnect/Stop. The final layout enlarges host details. Actual two-device Wi-Fi/hotspot behavior has NOT been tested here. Neither firewall nor other security settings were changed.

Source is included in `src`. From this package directory with .NET 10 SDK installed:

    dotnet build src/FriendsChat.csproj -c Release
    dotnet publish src/FriendsChat.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o release

Run protocol tests from the repository root (the test report writes to `artifacts/test-results.txt`):

    dotnet run --project src/tests/Tests.csproj -c Release

Transport implementation reference: https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0

