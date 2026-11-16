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
| Incoming connection | Plain TCP |
| Upstream connection | Plain TCP |

Click **Start relay**, then configure your client to connect to `127.0.0.1:9000`. For a client on another machine, bind to an appropriate local LAN IP (or `0.0.0.0`) and connect to the relay machine's LAN address. Any required firewall configuration is separate.

## TLS inspection without an existing certificate

1. Expand **Incoming TLS certificate** and enter the hostname or IP your client will use to connect to the relay. For local tests, use `localhost`; its generated SANs also include `127.0.0.1` and `::1`.
2. Click **Create test certificate**. This selects incoming TLS (or preserves Auto mode) and creates a self-signed RSA-2048 server certificate valid for 30 days.
3. Click **Export public test certificate…** and configure your test client to trust that certificate explicitly. Use its matching hostname/IP to connect. The export contains no private key. Clients with certificate pinning need a corresponding test configuration.
4. Set **Upstream connection** to **TLS** if the real server always expects TLS from connection start, or **Match incoming** to mirror each client's plain/TLS transport. For an IP connection with a DNS-named certificate, set **TLS server name / SNI override** to the certificate's DNS name, or let Match incoming reuse the client's SNI.
5. Upstream hostname, certificate trust and validity-date checks are all enabled by default and have independent toggles. **RemoteCertificateNameMismatch** means the checked name does not match the certificate. Keep **Upstream host / IP** as the connection address and set the SNI override to a DNS name or IP in the server certificate's Subject Alternative Name. The error details show the checked name and server subject/SAN. The relay's own test certificate is for incoming connections. For a private/self-signed upstream, select its trusted root (or explicitly trusted self-signed server certificate) under **Upstream TLS options**, or disable certificate trust verification for this relay. With a custom CA selected, system roots are not used. Revocation is not checked. See the verification toggles below.
6. Start the relay and connect your client to its listen IP/port. Captures contain decrypted application bytes in both directions.

Alternatively, browse to an existing PFX/P12 with a server certificate and private key, then enter its password. On Windows, TLS certificates use temporary user private-key containers because Schannel cannot use memory-only ephemeral keys. The app does not set `PersistKeySet`; .NET manages container cleanup when the associated native handles are released. Cleanup may be deferred by TLS credential caching or interrupted by forced termination. Test certificates are not retained for reuse after the tab closes, and no certificates are installed in a trusted store.

## Capture behavior and lifecycle

The workspace fits its traffic list and selected-entry inspector into the available window height. They appear side by side when space permits and stack within the available height on narrower windows. Hex, Text and Details tabs have their own scrolling viewers. Connection settings remain above the traffic area in a collapsible panel with independent scrolling; short windows and starting the relay collapse it to give traffic more space. Reopen **Connection details** when you need to edit configuration. The **Test cert…** toolbar action exports the public generated certificate even while the relay is running.

Clicking **Start relay** with missing or invalid required settings opens the connection panel and shows field-specific tooltip prompts. Plain TCP needs a listen IP/port and an upstream host/port. Incoming TLS also needs a generated test certificate or a PFX/P12; Auto can generate its certificate using the test certificate name.

Each accepted client gets a separate upstream connection and connection ID. **Auto scroll** starts checked and follows new capture entries; uncheck it to inspect older traffic without the list moving to the newest entry. Select an entry to view exact received bytes as hex or a text preview. On the **Text** tab, choose UTF-8 (default), ASCII, Latin-1 (ISO-8859-1), Windows-1252, OEM CP437, UTF-16 LE or UTF-16 BE. Changing the encoding immediately updates the selected preview and **Copy text**. TCP reads can split or combine application messages; chunks and their timestamps describe read observations, not packet boundaries or exact ordering between the two independent directions. Text previews replace invalid sequences, including characters split across chunks. Hex and exported hex preserve every captured byte.

The relay records bytes when received, before forwarding. If a write fails, the event log reports the failure; a capture entry alone does not prove the destination received those bytes. Both directions run concurrently. Plain TCP half-close is forwarded so a server can respond after a client finishes sending.

Enable **Telnet preview** on the **Text** tab to remove Telnet commands from the displayed text and unescape `IAC IAC` to a literal `FF` data byte before applying the selected encoding. Expand **Telnet commands** to see WILL/WONT/DO/DONT with option names, standalone commands, and SB/SE subnegotiation payloads as hex. Command parsing follows [RFC 854](https://www.rfc-editor.org/rfc/rfc854.txt) and [RFC 855](https://www.rfc-editor.org/rfc/rfc855.txt), with common option names from the [IANA registry](https://www.iana.org/assignments/telnet-options/).

The preview reconstructs command state from earlier retained chunks for the selected connection and direction, so split commands do not leak into its text. Incomplete or malformed commands are identified. Evicted or cleared history can leave the initial command state incomplete. Each entry still has its own text decoding; split multibyte characters can be replaced. Subnegotiation payload previews are capped at 64 bytes and command output at 256 commands per entry, with notices when shortened. **Copy text** copies the filtered text when Telnet preview is enabled. Text controls, including CR/LF, NUL and terminal escape sequences, are preserved; this is a passive protocol preview, with no terminal screen emulation, option negotiation, or automatic character-set switching. Forwarded bytes, Hex and exported capture bytes stay exact.

The capture ring retains at most 1,024 entries and 4 MiB of data. Old entries are evicted, with an eviction count; forwarding continues. **Clear capture** clears retained rows without interrupting connections, and byte totals remain cumulative for the current run. **Export capture…** writes a snapshot of retained events as JSON Lines, including direction, connection ID, UTC timestamp, byte count and exact hex. Its header includes byte totals and eviction count. Starting a new run resets the capture.

At most 16 clients can connect concurrently; extra clients are closed with an event. A shared 30-second setup deadline covers first-byte detection (when enabled), the incoming TLS handshake, upstream connection and upstream TLS handshake. **Stop relay** closes the listener and all connections while retaining captured rows. Closing the tab also requests immediate socket shutdown and releases certificates when connection tasks finish. A listener runs while its tab is open, even when another tab is selected. Configuration controls are locked while it runs.

## Automatic incoming TLS detection

Set **Incoming connection** to **Auto · first byte 0x16 = TLS** to accept both TLS and plain clients on one listening port. The relay peeks at the first byte without consuming it. `0x16` selects TLS termination with the configured incoming certificate; any other byte selects plain TCP. If no certificate is configured, starting Auto mode creates a self-signed certificate for the **Test certificate DNS name or IP** (default `localhost`). Export its public certificate using **Test cert…** and configure your client to trust it.

This is a first-byte heuristic, not full protocol detection. Plain protocols whose first byte is `0x16` need explicit Plain TCP mode. Auto waits for the client to send first and closes silent clients after the setup deadline; use Plain TCP for server-first protocols. Empty connections close without contacting upstream. A failed TLS handshake is reported and closed, with no fallback to plain TCP. **Upstream connection** can be fixed to Plain TCP/TLS or set to Match incoming.

## Match upstream to the client

Use **Incoming connection → Auto** and **Upstream connection → Match incoming** to mirror plain/TLS per connection. Plain clients get a plain upstream stream; TLS clients get a separately negotiated upstream TLS session. Match incoming reuses the client's SNI name when present. The upstream name is chosen in this order: explicit **TLS server name / SNI override**, client SNI in Match mode, configured upstream host. The destination host/port stays fixed; the client name does not redirect the connection. TLS versions, cipher suites, certificates and client authentication settings are not copied from the client; each leg negotiates independently, with no upstream protocol probing or TLS-to-plain retry.

## Verify upstream hostname

Three independent upstream checkboxes default to checked:

| Option | When unchecked |
| --- | --- |
| Verify upstream hostname | Permits a DNS/IP name mismatch; SNI is unchanged. |
| Verify upstream certificate trust | Permits unknown-CA/self-signed or partial certificate chains. |
| Verify upstream validity dates | Ignores expired/not-yet-valid chain certificates and date-nesting errors. |

Each toggle relaxes only its named check. Disabling trust does not disable date or hostname checks; disabling dates does not trust an unknown issuer. These settings also apply when a custom CA is selected. Other checks, such as certificate signatures and usage, still apply; an absent or malformed certificate is not accepted. The TLS connection event records all three verification settings. These options do not alter the incoming client's validation of the relay certificate and do not change machine-wide trust settings.

TLS 1.2 and 1.3 are supported as permitted by the OS. Legacy SSL/TLS 1.0/1.1, STARTTLS upgrades, UDP, HTTP CONNECT, mutual TLS and traffic editing are not implemented. HTTP/HTTPS data can pass through the raw relay, but no HTTP parsing, ALPN negotiation or protocol-aware editing is performed.
