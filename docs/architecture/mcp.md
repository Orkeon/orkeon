> 🇫🇷 [Version française](../fr/architecture/mcp.md)

# MCP Integration (Model Context Protocol)

> **Status**: Experimental (`[Experimental("ORKEXP004")]` on every MCP type — see
> [Experimental APIs](../reference/experimental-apis.md)). Dual-era since PUB-07.
> Code: `src/core/Orkeon.Infrastructure/MCP/`.

The MCP integration works in both directions:

- **Client** — `McpClient` speaks JSON-RPC to an external MCP server;
  `McpToolProvider` connects to any number of servers, lists their tools and registers
  each one in the Orkeon `IToolRegistry` as an `McpToolAdapter` (`IBaseTool`), so agents
  use external MCP tools like native ones. `DisconnectServerAsync` unregisters them.
- **Server** — `McpServer` exposes the tools of the Orkeon `IToolRegistry` to external
  MCP clients (`tools/list` / `tools/call`, with `ToolSchema` → JSON Schema conversion),
  over stdio (`RunStdioAsync`) or by processing individual requests
  (`ProcessRequestAsync`).

## Version negotiation: two protocol lineages

`McpProtocol` declares what both sides speak:

- **Modern, stateless lineage** — `ModernVersion = "2026-07-28"`. No handshake: every
  request carries `_meta` (`io.modelcontextprotocol/protocolVersion`, `clientInfo`,
  `clientCapabilities`); capability discovery is the `server/discover` method.
- **Legacy initialize lineage** — `SupportedLegacyVersions = ["2025-11-25", "2025-06-18",
  "2024-11-05"]`: classic `initialize` handshake followed by the mandatory
  `notifications/initialized`. **`2025-03-26` is deliberately excluded**: it is the only
  revision that mandates JSON-RPC batching, which this implementation does not speak.

### Client (`McpClient.ConnectAsync`)

1. Probe with `server/discover` (declaring `2026-07-28` in `_meta`).
2. **Success with `supportedVersions`** → modern server; adopt the newest mutually
   supported revision. A success *without* `supportedVersions` is a legacy server
   answering an unknown method leniently → fall back to `initialize`.
3. **Error `-32022` `UnsupportedProtocolVersionError`** → still a modern server; read its
   supported list from the error data, renegotiate, retry `server/discover`.
4. **Any other error** → legacy server; run the `initialize` handshake, which really
   negotiates (the server's chosen revision is accepted if the client supports it) and
   ends with `notifications/initialized`.

On an established modern connection, every request carries per-request `_meta`; a
mid-flight `-32022` triggers **one renegotiation + retry** with a mutually supported
revision (`McpClient.SendAsync`).

### Server (`McpServer`) — dual-era on one endpoint

- Implements `server/discover` (supported versions, capabilities, `ttlMs`/`cacheScope`
  freshness hints, server identity in result `_meta`).
- A request that declares an unsupported `_meta` version is rejected with `-32022` and the
  `{ supported, requested }` payload, whatever the method.
- Legacy `initialize` echoes the requested revision when supported, otherwise answers with
  the newest legacy revision. `ping` stays served for legacy sessions only (removed from
  the modern lineage).
- `tools/list` is returned in **deterministic order** (ordinal sort by name — a 2026-07-28
  SHOULD, for stable client caches) with the modern `resultType`/`ttlMs`/`cacheScope`
  fields; legacy clients ignore these additive members.
- Notifications (no `id`, or `notifications/*`) are never answered.

## Transports

| Transport | Class | Notes |
|---|---|---|
| stdio | `StdioMcpTransport` | Launches the server process (`McpServerConfig.Command`/`Args`); one JSON-RPC message per line; response correlation is id-agnostic (number **or** string ids round-trip). |
| HTTP | `SseMcpTransport` | Streamable HTTP shape, **JSON-response mode**: one POST per message. Modern requests carry the `MCP-Protocol-Version`, `Mcp-Method` and `Mcp-Name` headers (derived from the message itself); an SSE-framed response body (`text/event-stream`) is unwrapped to its final `data:` payload. Server-initiated streams are not consumed. |

## Activation

```csharp
services.AddOrkeonMcp(configuration);   // reads the "MCP" section
```

`AddOrkeonMcp` binds `McpOptions` (`Enabled`, `EnableServer`, `Servers` — a dictionary of
`McpServerConfig`: `Transport` stdio/sse, `Command`/`Args` or `Url`) and registers
`McpToolProvider` as a singleton; `McpServer` (+ `McpServerOptions` from `MCP:Server`:
`Name`, `Version`) is registered only when `MCP:EnableServer = true`. The
`AddOrkeonInfrastructure(IConfiguration)` overload calls `AddOrkeonMcp` itself —
but **no shipped composition root uses that overload** (`orkeon run`, `orkeon-host`
and the REPL all call the parameterless one), so MCP is effectively a library-only
surface: an embedding host calls `AddOrkeonMcp(configuration)` (or the config
overload) itself.

There is **no hosted service**: the host resolves `McpToolProvider` and calls
`ConnectServerAsync(serverId, config)` for each configured server (and
`McpServer.RunStdioAsync()` to serve), explicitly.

## Honest limitations

Aligned with [Known limitations](../reference/limitations.md) and the PUB-07 changelog
entry:

- **No `subscriptions/listen`** — server-push change notifications are not consumed.
- **No multi-round-trip requests (MRTR)** — a modern `input_required` interim result is
  surfaced as an explicit tool error, never as partial data.
- **No MCP authorization (OAuth)**.
- **HTTP = JSON-response mode only** — modern headers sent, SSE bodies unwrapped, but no
  server-initiated streaming.
- **`2025-03-26` excluded** (mandatory JSON-RPC batching).
- **Interop against reference servers (MCP Inspector) has not run yet** — tracked in
  PUB-07's closure note.

## Experimental status

Every MCP type carries `[Experimental("ORKEXP004")]`: referencing them from your code is a
compiler error until you suppress the diagnostic — that suppression is your opt-in
acknowledgement that the surface (notably the wire types) may still move. See
[Experimental APIs](../reference/experimental-apis.md).

---

> **See also**: [Opt-in subsystems](../reference/opt-in-subsystems.md) ·
> [Experimental APIs](../reference/experimental-apis.md) ·
> [Known limitations](../reference/limitations.md) ·
> [Back to index](../INDEX.md)
