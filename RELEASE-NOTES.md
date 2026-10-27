# Friends Chat 0.3

Room names and passwords replace long invitation codes. Connections now go outward through a shared HTTPS relay. No local incoming port, local IP entry, discovery or self-signed chat certificate is needed.

Deploy the included relay first, then enter its HTTPS address on both laptops. Creator: choose a room name and password, Create room. Friend: same details, Join room.

The relay can read messages. Memory-only rooms end when the creator disconnects or the relay restarts. Norton and other protections remain enabled; outbound HTTPS/WebSockets may still be inspected or blocked by policy.

Download FriendsChat-0.3-Windows-x64.zip below. Read START-HERE.txt. A working hosted relay is required; the ZIP does not provide a running public server. Twelve local protocol tests passed; public deployment and two-laptop testing remain necessary.
