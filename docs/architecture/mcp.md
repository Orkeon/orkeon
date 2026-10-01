> 🇫🇷 [Version française](../fr/architecture/mcp.md)

# MCP Integration (Model Context Protocol)

> **Status**: Experimental (`[Experimental("ORKEXP004")]` on every MCP type — see
> [Experimental APIs](../reference/experimental-apis.md)). Dual-era since PUB-07.
> Code: `src/core/Orkeon.Infrastructure/MCP/`.

The MCP integration works in both directions:

- **Client** — `McpClient` speaks JSON-RPC to an external MCP server;
  `McpToolProvider` connects to any number of servers, lists their tools and registers
  each one in the Orkeon `IToolRegistry` as an `McpToolAdapter` (`IBaseTool`).
  `DisconnectServerAsync` unregisters them. **A crew agent cannot use them yet** — see
  [MCP tools and crews](#mcp-tools-and-crews).
- **Server** — `McpServer` exposes the tools of the Orkeon `IToolRegistry` to external
  MCP clients (`tools/list` / `tools/call`, with `ToolSchema` → JSON Schema conversion),
  over stdio (`RunStdioAsync`) or by processing individual requests
  (`ProcessRequestAsync`). Only the tools capability is advertised — no resources, no
  prompts, no crews — and no shipped command serves it: there is no `orkeon mcp serve`,
  an embedding host starts it itself.

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

`AddOrkeonMcp` binds `McpOptions` (`Enabled`, default `true`; `EnableServer`, default
`false`; `Servers` — a dictionary of `McpServerConfig` keyed by server id: `Transport`
`Stdio` (default) or `Sse`, `Command`/`Args`/`Env` for stdio, `Url` for HTTP) and registers
`McpToolProvider` as a singleton; `McpServer` (+ `McpServerOptions` from `MCP:Server`:
`Name`, default `Orkeon`, and `Version`, default `1.0.0`) is registered only when
`MCP:EnableServer = true`. The server exposes tools only — Orkeon has no resource or prompt
model to serve, so `resources/*` and `prompts/*` answer `Method not found`, and there is no
option claiming otherwise (the inert `ExposeResources`/`ExposePrompts` were removed, GAP-11).
The `AddOrkeonInfrastructure(IConfiguration)` overload calls `AddOrkeonMcp` itself.

```json
{
  "MCP": {
    "Enabled": true,
    "Servers": {
      "filesystem": {
        "Transport": "Stdio",
        "Command": "npx",
        "Args": [ "-y", "@modelcontextprotocol/server-filesystem", "/srv/data" ],
        "Env": { "NODE_ENV": "production" }
      },
      "search": { "Transport": "Sse", "Url": "https://mcp.example.com/mcp" }
    },
    "EnableServer": false,
    "Server": { "Name": "Orkeon", "Version": "1.0.0" }
  }
}
```

The client introduces itself as `Orkeon` `1.0.0`. The HTTP transport sends no
`Authorization` header, so a server behind authentication is out of reach (see the
limitations below).

**The shared runner host honours the section on its own (STUDIO-21).** `orkeon run`
— and every runner built on `RunnerHost` — calls `AddOrkeonMcp` when `MCP:Servers`
declares at least one server and `MCP:Enabled` is not `false`, then connects every
server in an explicit startup step (`McpStartup`) before the crew loads — and before
`--validate` judges the crew and `--list-tools` prints the manifest, so the three see the
same tool surface. A server that cannot be connected (a command that does not exist, an
endpoint that does not answer, a handshake still pending after 30 s) costs one error line
naming it, on the log and on stderr, and the run goes on. The tools are registered under
their own names, without a server prefix, and a name the registry already holds — a
built-in tool, a script tool, or a tool of a server connected earlier — is **refused**: the
MCP tool is not registered, one error line names the server and the tool (`The tool
'file_read' of MCP server 'x' collides with an already-registered tool and was not
registered`), on the log and on stderr, and the tool that held the name keeps it, including
after that server disconnects. The server's other tools are registered normally
(`--list-tools` shows the merged surface). Orkeon Studio writes the section from its
«Settings › MCP» tab. **`orkeon-host` connects them too (GAP-11):** built on `RunnerHost`,
it reads the same section and runs the same step once, from its first hosted service, when
the daemon starts — before the chat channel can deliver a message that loads a crew — and
disconnects the servers when it stops, after the drain. The same settings give `orkeon run`
and `orkeon-host` the same tools; an unreachable server costs the same error line and the
daemon starts without it. The REPL calls the parameterless `AddOrkeonInfrastructure()` and
registers no MCP at all; there, and in any embedding host, MCP stays a library surface —
the host calls `AddOrkeonMcp(configuration)` (or the config overload) and connects the
servers itself.

The library ships **no hosted service** for this: an embedding host resolves
`McpToolProvider` and calls `ConnectServerAsync(serverId, config)` for each configured
server (and `McpServer.RunStdioAsync()` to serve), explicitly — which is exactly what the
runner host's startup step does, and what `orkeon-host`'s connection service calls.

### MCP tools and crews

A connected MCP tool is a tool like any other: a crew agent lists it by name, in YAML or in
a `.ork.ts` script, and receives it.

```yaml
agents:
  analyst:
    tools: [search_issues]   # a tool exposed by an MCP server declared in MCP:Servers
```

`CrewFactory` attaches every tool the registry resolves; there is no narrower interface
than `IBaseTool` to implement. The servers are connected before the crew loads, so under
`StrictTools` — the runners' default — a crew that names a tool of a server that could not
be connected fails at load with the ordinary `unknown tool(s)` line. `--list-tools` and
`--validate` see the MCP tools, `McpServer` re-exposes whatever the registry holds, and C#
code can resolve one with `IToolRegistry.GetToolByNameAsync(name)`. The observed-run
decorator of `--events jsonl` wraps the tools registered in DI; MCP tools reach the
registry at run time and are not wrapped.

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
