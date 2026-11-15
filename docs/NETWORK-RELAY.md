# TCP / TLS Relay

Open **Utilities → TCP / TLS Relay**. The client must connect directly to the relay's listening address; the relay forwards to the upstream endpoint you configure. This is a raw TCP inspection proxy and does not change routing, DNS, firewall rules or machine trust settings.

## Plain TCP

For a server at `10.0.0.20:5000`, set:

| Setting | Value |
| --- | --- |
| Listen IP | `127.0.0.1` |
| Listen port | `9000` |
| Upstream host / IP | `10.0.0.20` |
| Upstream port | `5000` |
| TLS from client / TLS to upstream | Both off |

Click **Start relay**, then configure your client to connect to `127.0.0.1:9000`. For a client on another machine, bind to an appropriate local LAN IP (or `0.0.0.0`) and connect to the relay machine's LAN address. Any required firewall configuration is separate.

## TLS inspection without an existing certificate

1. Expand **Incoming TLS certificate** and enter the hostname or IP your client will use to connect to the relay. For local tests, use `localhost`; its generated SANs also include `127.0.0.1` and `::1`.
2. Click **Create test certificate**. This enables incoming TLS and creates a self-signed RSA-2048 server certificate valid for 30 days.
3. Click **Export public test certificate…** and configure your test client to trust that certificate explicitly. Use its matching hostname/IP to connect. The export contains no private key. Clients with certificate pinning need a corresponding test configuration.
4. Enable **TLS to upstream server** if the real server expects TLS from connection start. If connecting to an IP address while the server's certificate names a DNS host, set **TLS server name / SNI** to that DNS name.
5. The upstream certificate must pass hostname and trust checks. For a private/self-signed server, select its trusted root (or explicitly trusted self-signed server certificate) under **Upstream TLS options**. This trust applies only to this relay. With a custom CA selected, system roots are not used. Revocation is not checked.
6. Start the relay and connect your client to its listen IP/port. Captures contain decrypted application bytes in both directions.

Alternatively, browse to an existing PFX/P12 with a server certificate and private key, then enter its password. On Windows, TLS certificates use temporary user private-key containers because Schannel cannot use memory-only ephemeral keys. The app does not set `PersistKeySet`; .NET manages container cleanup when the associated native handles are released. Cleanup may be deferred by TLS credential caching or interrupted by forced termination. Test certificates are not retained for reuse after the tab closes, and no certificates are installed in a trusted store.

## Capture behavior and lifecycle

Each accepted client gets a separate upstream connection and connection ID. Select an entry to view exact received bytes as hex or a UTF-8 preview. TCP reads can split or combine application messages; chunks and their timestamps describe read observations, not packet boundaries or exact ordering between the two independent directions. UTF-8 previews replace invalid sequences, including characters split across chunks. Hex and exported hex preserve every captured byte.

The relay records bytes when received, before forwarding. If a write fails, the event log reports the failure; a capture entry alone does not prove the destination received those bytes. Both directions run concurrently. Plain TCP half-close is forwarded so a server can respond after a client finishes sending.

The capture ring retains at most 1,024 entries and 4 MiB of data. Old entries are evicted, with an eviction count; forwarding continues. **Clear capture** clears retained rows without interrupting connections, and byte totals remain cumulative for the current run. **Export capture…** writes a snapshot of retained events as JSON Lines, including direction, connection ID, UTC timestamp, byte count and exact hex. Its header includes byte totals and eviction count. Starting a new run resets the capture.

At most 16 clients can connect concurrently; extra clients are closed with an event. A shared 30-second setup deadline covers the incoming TLS handshake, upstream connection and upstream TLS handshake. **Stop relay** closes the listener and all connections while retaining captured rows. Closing the tab also requests immediate socket shutdown and releases certificates when connection tasks finish. A listener runs while its tab is open, even when another tab is selected. Configuration controls are locked while it runs.

TLS 1.2 and 1.3 are supported as permitted by the OS. Legacy SSL/TLS 1.0/1.1, STARTTLS upgrades, UDP, HTTP CONNECT, mutual TLS and traffic editing are not implemented. HTTP/HTTPS data can pass through the raw relay, but no HTTP parsing, ALPN negotiation or protocol-aware editing is performed.
