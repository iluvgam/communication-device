# Friends Chat 0.2 — easier local joining

## Start here

Both friends should replace the previous executable with FriendsChat.exe from this ZIP. Open it normally on permitted Windows x64 devices. No installation or separate .NET download is needed.

HOST:
1. Connect to the shared hotspot. Enter a display name.
2. Select the Wi-Fi adapter in the list. Each entry shows an adapter name and its IPv4 address.
3. Click Host Chat. Keep this window open and the laptop awake.
4. Click Copy invitation. Send the single copied line privately to your friend using a trusted channel, or transfer it directly in person. It includes the address, room code and certificate fingerprint. Treat it like a room password.

FRIEND:
1. Enter a display name.
2. Paste the entire invitation into the invitation box and click Use invitation.
3. Confirm the invitation came directly from the actual host through a trusted channel. Tick the verification box, then click Join Chat.
4. Send a message each way.

No separate IP/code/fingerprint copying is needed. Invitations expire when the host stops the room or its IP changes. Copy a fresh invitation after either event. Old versions do not understand invitations; use 0.2 on both laptops.

## Find local chats

Click Find local chats. It sends a small UDP discovery query to local IPv4 broadcasts and waits about three seconds. Hosts answer while their visible room is running. Select a result to choose its address. Discovery shows the host's self-chosen name and address, not its private room code.

Discovery replies are UNVERIFIED and can be spoofed. They do not authorize a connection. Get a trusted invitation before joining, or compare the full SHA-256 fingerprint directly against Show room details on the host and use manual fields. A discovered certificate fingerprint is never automatically trusted. Receiving an invitation from an unknown party is not host verification.

Show address / manual entry reveals the IPv4, room code and full SHA-256 fields. Changing any field clears the verification confirmation. Show room details on the host displays the corresponding values for comparison.

Check connection tests TCP reachability to the entered/imported IP on port 45831. Success means a service answered TCP, not that its identity or room credentials are valid. Join Chat performs those checks. Failure suggests an incorrect IP, stopped host, firewall blocking or hotspot filtering. Discovery can work while TCP is blocked, and TCP can work while discovery is blocked. Use an invitation if discovery finds nothing.

## Norton, firewall and hotspot limitations

This update does not change firewall, antivirus or certificate settings and does not bypass policy restrictions. The host listens on TCP 45831 and UDP 45832 on its IPv4 interfaces only while hosting. Discovery is optional. Norton/firewall policies can block either port. An authorized device owner or administrator must decide any app-specific allowance. Do not disable protection. A new executable or folder may require a fresh approved app rule.

A recent local TLS test observed a certificate issued by 'Norton Web/Mail Shield Self-signed Root' instead of the host certificate. Friends Chat correctly rejected that substituted certificate. A firewall allowance alone does not resolve TLS inspection. If this occurs on your devices, ask an authorized administrator to review compatibility; the app will not accept the replacement certificate or bypass fingerprint checking. The host sends its own messages directly inside the same process, so local hosting does not need a loopback TLS connection.

Both laptops must be on the same network with local peer traffic permitted. Internet access through an iPhone hotspot does not establish that local peer traffic is allowed. If your hotspot filters local traffic, use another authorized Wi-Fi network. There is no cloud relay, external server, internet scan, router configuration, hidden service or automatic startup.

## Privacy and encryption

Laptop-to-host links use TLS 1.2/1.3 with mandatory SHA-256 certificate pinning. The random room code is sent after that check. The host decrypts and forwards messages; this is not participant-to-participant end-to-end encryption. Names are self-chosen, not authenticated identities. Maximum message length: 2,000 characters. The room supports up to 16 network connections plus its local host.

No chat messages are deliberately written to files. Visible history is bounded in memory and cleared on disconnect; new joiners see only new messages. OS paging, crash reporting and screenshots are outside this guarantee. Invitations remain on the clipboard until replaced; clear or replace the clipboard after sharing if desired. The app does not save invitations. Windows handles temporary certificate keys and runtime extraction in its normal way. The unsigned executable may require your device's approved software-review process.

## What was tested

The 0.2 build compiled successfully. Seven focused tests passed: invitation round trip, malformed/loopback invitation rejection, UDP loopback discovery, discovery shutdown, rejecting a port as an IPv4 address, direct host messaging, and host message-length enforcement. Visible hosting was exercised. Cross-laptop discovery has not been tested here. The latest full TLS messaging test was blocked by Norton certificate substitution; the earlier 0.1 messaging test report is historical, not validation of 0.2 under the current protection configuration. The build also reported that NuGet vulnerability metadata could not be fetched (NU1900); it still produced the executable. No security settings were changed.
