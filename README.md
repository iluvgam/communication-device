# Friends Chat 0.3

Windows group chat using room names and passwords. No invitation strings, IP copying, local discovery, incoming connections on laptops, or self-signed chat certificates.

## Run the app

Download and extract **FriendsChat-0.3-Windows-x64.zip** from Releases. Open FriendsChat.exe on both laptops. Use the same trusted relay HTTPS address, room name and password. One person clicks **Create room**, the friend clicks **Join room**. Passwords must be 10–128 characters; use a unique phrase shared privately. Room names are 3–32 ASCII letters/numbers/hyphens and are case-insensitive. Passwords are case-sensitive. Keep the creator connected; leaving closes the room.

**A deployed relay is required. This source package does not include a running public relay or account.** A GitHub Release distributes the app; GitHub Pages cannot run this relay. The laptops need internet access; they no longer need the same hotspot. A relay address is shared once, not a room invitation code.

## Deploy the relay on Render

1. Upload this source folder's CONTENTS to a GitHub repository root: Dockerfile, render.yaml, Relay, Shared, Client, Tests and this README should appear there. Do not upload build folders.
2. Sign in to Render at https://dashboard.render.com/ and choose **New > Web Service**. Connect the GitHub repository. Choose the Docker runtime, root directory blank, Dockerfile path ./Dockerfile. Select the service plan you want after reviewing its terms/cost. A free-plan blueprint is also included in render.yaml; no plan is purchased or service deployed by this package.
3. Deploy. Wait for the service to be live. Open the provided https://...onrender.com address; it should say the relay is running.
4. Enter that SAME HTTPS address in FriendsChat.exe on both laptops. Use the root address without /chat. The app uses secure WebSockets internally.

Render terminates public HTTPS and forwards requests to the container. The container listens on port 10000. Use exactly one running instance: state is in memory and is not shared between instances. Free-tier services may sleep; the client waits up to 90 seconds for connection. Restarts, redeploys, or service shutdowns erase all rooms. Do not expect durable sessions or high availability.

Official hosting references: https://render.com/docs/web-services and https://render.com/docs/websocket

## Security and Norton

The app uses Windows/.NET's normal certificate validation against the trusted HTTPS relay hostname. It does not override certificate validation, install roots, change Norton, change firewall settings or disable protections. Participants verify the relay address through a trusted channel and their normal HTTPS trust chain; the room password controls admission. A valid certificate for a different or malicious relay is not proof you chose the correct service: check the address.

This avoids the previous local inbound firewall and self-signed certificate design. Norton or organizational policy may still block or inspect HTTPS/WebSockets; compatibility is not guaranteed. Follow the device owner's approved process if blocked. Do not disable protection. Software trusted by Windows to inspect HTTPS can read that traffic.

The relay operator can read messages and room passwords during authentication. This is transport encryption, not end-to-end encryption. No identities/accounts or moderation system are provided; display names can be duplicated. A wrong room/password receives a generic rejection. Passwords are stored only as salted PBKDF2-SHA256 verifiers (600,000 iterations) in relay memory; active request objects may temporarily hold plaintext. Use a unique room password, never an account password.

There is no message database, message-body logging, or history replay. Clients keep bounded visible history in memory and clear it on disconnect. Relay memory stores active rooms and sockets. Hosting platforms may retain connection/access logs or crash dumps; operating-system paging, screenshots and clipboard sharing are outside the app's guarantee. The app does not persist the relay address, room name or password to disk.

Prototype limits: 16 participants per room, approximately 64 rooms, 128 concurrent connections, two concurrent password checks, 20 WebSocket connection attempts per remote IP per minute, 30 messages per participant per ten seconds, 2,000 characters per message, 16 KiB framed packet limit. Behind a proxy, multiple users may share a rate-limit bucket. Public hosting is not private access control: anyone knowing the relay URL can attempt room creation, subject to limits. Use a private small-group instance; this has not had a production security audit.

## Build

Requires Windows x64 and .NET 10 SDK for the client:

    dotnet publish Client/FriendsChat.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o artifacts/app

The relay uses the included Dockerfile or:

    dotnet run --project Relay/Relay.csproj --urls http://127.0.0.1:45903

That last command is for LOCAL TESTS ONLY. Never use plaintext HTTP for participants or expose that test listener publicly. For public use deploy behind trusted HTTPS as above.

In a second terminal, from the same source folder:

    dotnet run --project Tests/Tests.csproj -c Release

Twelve loopback protocol tests passed: create, duplicate rejection, incorrect password, unknown room, short password, case-insensitive join, Unicode/authenticated sender names, bidirectional messages, oversize rejection, creator disconnect, recreation, and enforcing HTTPS URL input. They use unencrypted loopback WebSockets and synthetic passwords to test relay behavior. Public TLS deployment, Docker execution and two-device behavior have not been tested here. Test those after deployment before relying on it. The client and relay compiled successfully.

## Publish the download

Create a GitHub release tagged v0.3.0-test and attach FriendsChat-0.3-Windows-x64.zip. Uploading source alone does not start the relay. Send friends the release link plus the deployed relay address. Share the room name and unique password privately.
