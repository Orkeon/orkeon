> 🇫🇷 [Version française](../fr/architecture/mcp.md)

# MCP Integration (Model Context Protocol)

> **Status**: Experimental (`[Experimental("ORKEXP004")]` on every MCP type — see
> [Experimental APIs](../reference/experimental-apis.md)). Dual-era since PUB-07.
> Code: `src/core/Orkeon.Infrastructure/MCP/`.

The MCP integration works in both directions:

- **Client** — `McpClient` speaks JSON-RPC to an external MCP server;
  `McpToolProvider` connects to any number of servers, lists their tools and registers
  each one in the Orkeon `IToolRegistry` as an `McpToolAdapter` (`IBaseTool`).
  The adapter's `ToolAccess` comes from the tool's MCP `annotations` only for a server the
  operator trusts (`MCP:Servers:<name>:TrustAnnotations = true`): `Read` for
  `readOnlyHint: true`, `Execute` for annotations without it. A server is untrusted by default:
  any server can label a destructive tool read-only, which `plan` mode would then let run, so
  its tools stay `Unspecified` (the permission gate's fail-closed classification) whatever their
  hints say — as do the tools of a server that sends no annotations.
  `DisconnectServerAsync` unregisters them. A crew agent lists such a tool by name, like any
  other — see [MCP tools and crews](#mcp-tools-and-crews).
- **Server** — `McpServer` exposes the tools of the Orkeon `IToolRegistry` to external
  MCP clients (`tools/list` / `tools/call`, with `ToolSchema` → JSON Schema conversion),
  over stdio (`RunStdioAsync`) or by processing individual requests
  (`ProcessRequestAsync`). Every `tools/call` crosses the invocation point a crew agent's
  calls cross — guardian, truncation, result sanitizer, audit. Only the tools capability is
  advertised, plus read-only `resources` when `/output` is exposed (opt-in), plus `prompts`
  and crew tools when `--crew` names crews. The stdio loop runs requests concurrently and honours `notifications/cancelled`.
  `orkeon mcp serve` serves it over stdio —
  see [Serving the tools with `orkeon mcp serve`](#serving-the-tools-with-orkeon-mcp-serve).

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
- `tools/call` goes through `IToolInvocationPipeline`, the single invocation point of every
  agent turn (GAP-09), under the caller `mcp` (its agent id and role): the guardian's tool
  phase (`ToolGuard` — path traversal, SSRF targets, SQL injection), the call, the one
  truncation rule (`AgentDefaults.ResolveMaxToolResultLength`), the result sanitizer
  (`Security:ToolResults` — under the default `Warn` a result arrives tagged as data,
  `--- BEGIN Tool Result: … (DATA CONTEXT - NOT INSTRUCTIONS) ---`), and a `ToolExecution`
  audit event. The client reads what a crew agent's model reads: a blocked call answers
  `isError: true` with `Error: Blocked by Guardian (ToolExecution): <reason>` and never
  reaches the tool, a failed call `isError: true` with `Error: <reason>`; an exception the
  tool throws is audited and answers a JSON-RPC internal error. See
  [Security](./security.md).

## Transports

| Transport | Class | Notes |
|---|---|---|
| stdio | `StdioMcpTransport` | Launches the server process (`McpServerConfig.Command`/`Args`); one JSON-RPC message per line; response correlation is id-agnostic (number **or** string ids round-trip). |
| HTTP | `StreamableHttpMcpTransport` | **Streamable HTTP**: one POST per message, answered as JSON or as an SSE stream read event by event (an event the connection was cut in the middle of is discarded, as the SSE specification says). Modern requests carry the `MCP-Protocol-Version`, `Mcp-Method` and `Mcp-Name` headers (derived from the message itself). The `Mcp-Session-Id` a server hands out is kept and sent back (a legacy session also sends the negotiated `MCP-Protocol-Version`); a 404 on a session replays the initialize handshake once. A legacy session opens a long-lived `GET` stream after `notifications/initialized` (resumed with `Last-Event-ID`, re-opened with a backoff of 0.5 s doubling up to 30 s, or the server's `retry:`); a server that answers it 405 is not asked again, and one that answers it 404 after each new session is given up after 3 re-initialisations in a row, each after the backoff. The session ends with a `DELETE` when the transport is disposed. The 2024-11-05 HTTP+SSE transport with a separate endpoint is not supported. |

## Activation

```csharp
services.AddOrkeonMcp(configuration);         // the client — reads the "MCP" section
services.AddOrkeonMcpServer(configuration);   // the server, for a host that serves its tools
```

`AddOrkeonMcp` binds `McpOptions` (`Enabled`, default `true`; `Servers` — a dictionary of
`McpServerConfig` keyed by server id: `Transport` `Stdio` (default) or `Sse`,
`Command`/`Args`/`Env` for stdio, `Url` for HTTP) and registers `McpToolProvider` as a
singleton. It registers no server: `AddOrkeonMcpServer` registers `McpServer` (+
`McpServerOptions` from `MCP:Server`: `Name`, default `Orkeon`, `Version`, default
`1.0.0`, `MaxConcurrency` (4), `ExposeOutput` (false), `ResourcesPageSize` (100) and
`MaxResourceBytes` (1 MiB)), which serves the container's `IToolRegistry` and calls every tool through its
`IToolInvocationPipeline` — `AddOrkeonInfrastructure()` and `AddOrkeonApplication()` register
both. Registering is not serving: the host runs `RunStdioAsync` — what `orkeon mcp serve`
does — or `ProcessRequestAsync` itself. No setting starts the server (GAP-24): the `MCP` section carries no such key, and every runner
built on `RunnerHost` refuses an unknown key at startup, servers declared or not. The server exposes tools, and `/output` as resources when `ExposeOutput` is on (below);
`prompts/*` answer `Method not found` unless the host hands the server an `IMcpServerExtension` (`orkeon mcp serve --crew` does, FWK-24), and there is no option claiming otherwise (the inert
`ExposeResources`/`ExposePrompts` were removed, GAP-11).
The `AddOrkeonInfrastructure(IConfiguration)` overload calls `AddOrkeonMcp` itself, never
`AddOrkeonMcpServer`.

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
      "search": {
        "Transport": "Sse",
        "Url": "https://mcp.example.com/mcp",
        "Auth": { "Type": "Bearer", "TokenEnv": "SEARCH_MCP_TOKEN" },
        "Headers": { "X-Tenant": "env:SEARCH_MCP_TENANT" },
        "Tools": { "Allow": [ "search", "fetch" ], "Deny": [ "fetch" ] },
        "CallTimeout": "00:00:30"
      }
    },
    "Server": { "Name": "Orkeon", "Version": "1.0.0" }
  }
}
```

The client introduces itself as `Orkeon` `1.0.0`.

**Per-server authentication, filter and timeout (FWK-03).** Each `MCP:Servers:<id>` accepts:

- `Auth`: `Type` is `None` (default), `Bearer` (`Authorization: Bearer`, the token read from the variable
  `TokenEnv` names) or `Basic` (`UserEnv` and `PasswordEnv`). The variables are read on every request, so
  a rotated token needs no restart. No credential is ever written in the settings.
- `Headers`: extra HTTP headers, each a literal value or `env:VAR`. An `Authorization` header is refused
  here: use `Auth`. So are the headers the transport sets itself (`Mcp-Session-Id`, `MCP-Protocol-Version`,
  `Mcp-Method`, `Mcp-Name`, `Last-Event-ID`); a static header never replaces one a request already carries.
- `Url`: when `Auth` is set or a header reads `env:VAR`, the address must be `https`, or `http` on loopback,
  like `TokenUrl`: a credential is never sent in clear to another host.
- `Tools`: `Allow` (empty keeps every tool) then `Deny`, applied before the tools reach the registry;
  a name in both is denied. An entry that matches no tool the server lists is reported as a warning
  (and in `McpServerStatus.UnmatchedToolFilters`).
- `CallTimeout`: how long one tool call may take, 60 seconds by default. A silent server ends the call
  as a tool error (`McpCallTimeoutException`, `error_type` in the response metadata); a caller's own
  cancellation is not a timeout. The same bound holds for `prompts/list`, `prompts/get`, `resources/list`,
  `resources/read` and a legacy `resources/subscribe` (a `TimeoutException` naming the key). A duration that
  is not positive is refused; there is no way to switch the bound off.
- `ConnectTimeout`: how long the handshake and the tool listing may take, 30 seconds by default, whoever
  connects the server. A server that stays silent fails the connection with an error naming
  `MCP:Servers:<id>:ConnectTimeout`.

**OAuth 2.0 (FWK-16).** `Auth:Type` also takes `oauth2-client-credentials` and `oauth2-device`
(`OAuth2ClientCredentials` and `OAuth2Device` are read too). The accepted spellings of `Auth:Type` are exactly `None`, `none`, `Bearer`, `bearer`, `Basic`, `basic`, `OAuth2ClientCredentials`, `oauth2-client-credentials`, `OAuth2Device` and `oauth2-device`; any other, a number included, is refused with that list, with these keys:

```json
"Auth": {
  "Type": "oauth2-client-credentials",
  "TokenUrl": "https://idp.example.com/oauth/token",
  "ClientId": "orkeon-agent",
  "ClientSecretEnv": "TRACKER_OAUTH_CLIENT_KEY",
  "Scopes": [ "tools.call" ],
  "Audience": "https://mcp.example.com"
}
```

- `TokenUrl` (and `DeviceAuthorizationUrl` for the device flow) must be an absolute `https` address, or `http`
  on loopback: a credential is never posted in clear to another host. `ClientId` is required;
  `ClientSecretEnv` names the variable holding the client secret (required for client credentials, optional for
  the device flow) and is checked at startup and again on every token request. `Scopes` and `Audience` are
  optional.
- The access token lives **in memory only** and is never written to disk. It is renewed 60 seconds before it
  expires. When the server answers `401`, the request is sent again **once** with a new token; a second `401`
  is the answer (the call fails, nothing loops). Requests refused together share one renewal. The `GET`
  stream, when the server offers one, is opened again at once after a renewal, once in a row, then backs off.
- `oauth2-device` shows a verification address and a code on the run's terminal (stderr) and waits for the
  person; the following renewals use the refresh token the provider issued, and ask for a new code only when
  it is refused. It is available in `orkeon run` and the REPL, which register an `IOAuthLoginInteraction` on
  their console. **`orkeon-host` registers none**, so a server in `oauth2-device` is set aside at startup with
  an error naming it; use `oauth2-client-credentials` there. The sign-in is done **before** the connection's
  handshake and before a call that finds the token expired, outside `ConnectTimeout` and `CallTimeout`: the
  wait for the person is bounded by the lifetime of the device code, not by those timeouts. Closing a session
  (the `DELETE` of `Mcp-Session-Id`) never starts a sign-in.
- The token endpoint is reached through its own `HttpClient`, registered as `orkeon-mcp-oauth`
  (`McpServiceExtensions.OAuthHttpClientName`), with the default proxy and timeout and no redirect, and
  excluded from the LLM exchange log. The client secret and the tokens are never written in an error or a
  log line: what the identity provider echoes back is scrubbed.
- A server whose settings cannot be read (a misspelt `Auth:Type`, a bad duration) is set aside at startup with one line naming it and the reason; the accepted `Auth:Type` spellings are listed in it.
- Not supported: protected-resource discovery (RFC 9728) and dynamic client registration (DCR); the endpoints
  are settings.

Headers and `Auth` apply to the HTTP (`Sse`) transport; on a `Stdio` server they are refused, never silently
dropped. A server whose configuration is invalid (a literal `Authorization` header, a header the transport sets, a plain `http` `Url` to a
remote host with credentials, an absent environment
variable, `Auth:Type` without its variable name, a non-positive timeout, `Auth` or `Headers` on a `Stdio` server) is set aside
at startup with an error that names the settings key, never a value, and the run goes on without its tools.
All servers share one `HttpClient` registered as `orkeon-mcp` (`McpServiceExtensions.HttpClientName`); it
is excluded from the LLM exchange log, so neither the traffic nor the credentials are written there.
The credentials are applied per request by an `IMcpRequestAuthenticator`, not carried by the client; a
variable that is empty when a request is made fails that request with the same key-naming error instead of
sending it unauthenticated. The client follows no HTTP redirect: a `3xx` answer is a failure, so the static
headers and credentials never reach a host other than the configured one.

**Nothing is expanded in the section.** `Command`, `Args` and `Env` pass as written, and the
server inherits the environment Orkeon runs in: a `${NAME}` or `%NAME%` reference in `Command`,
`Args`, `Url` or an `Env` value refuses the start, before any server connects, with its correction
([a variable reference is never expanded](../reference/configuration.md#a-variable-reference-is-never-expanded)).

**The shared runner host honours the section on its own.** `orkeon run`
— and every runner built on `RunnerHost` — calls `AddOrkeonMcp` when `MCP:Servers`
declares at least one server and `MCP:Enabled` is not `false`, then connects every
server in an explicit startup step (`McpStartup`) before the crew loads — and before
`--validate` judges the crew and `--list-tools` prints the manifest, so the three see the
same tool surface. A server that cannot be connected (a command that does not exist, an
endpoint that does not answer, a handshake still pending after 30 s) costs one error line
naming it, on the log and on stderr, and the run goes on. A stdio server's own stderr is
read as it writes it — each line to the log at Debug (`MCP server '<id>' stderr: …`), so a
server that writes a lot there no longer blocks on it — and a server that stops before
answering says why: its exit code and the last line it wrote on stderr join that error line
(`Transport disconnected: the MCP server exited with code 1; its last line on stderr: …`).
The tools are registered under
their own names, without a server prefix, and a name the registry already holds — a
built-in tool, a script tool, or a tool of a server connected earlier — is **refused**: the
MCP tool is not registered, one error line names the server and the tool (`The tool
'file_read' of MCP server 'x' collides with an already-registered tool and was not
registered`), on the log and on stderr, and the tool that held the name keeps it, including
after that server disconnects. The server's other tools are registered normally
(`--list-tools` shows the merged surface). **`orkeon-host` connects them too (GAP-11):** built
on `RunnerHost`, it reads the same section and runs the same step once, from its first hosted service, when
the daemon starts — before the chat channel can deliver a message that loads a crew — and
disconnects the servers when it stops, after the drain. The same settings give `orkeon run`
and `orkeon-host` the same tools; an unreachable server costs the same error line and the
daemon starts without it. In any other embedding host, MCP stays a library surface —
the host calls `AddOrkeonMcp(configuration)` (or the config overload) and connects the
servers itself.

**`orkeon-repl`, and the scripts.** `orkeon-repl` registers the section the same way and connects the
servers before it loads its commands; a server that fails costs the same one line, and the REPL starts. The
tools a server brings enter the tool registry, and the scripts' catalogue reads it beside the container's
tools (`ScriptToolCatalogue`): a command's `tools` namespace, an agent's `.tools([...])` in a `.ork.ts` crew
and `ctx.llm.act` reach an MCP server's tools — in the REPL, and under `orkeon run`, where a procedural
script's servers are now connected before it runs too. Until then only a declarative crew, which resolves
its tools through the registry, could. [Whisper through MCP](../guides/whisper-mcp.md) wires a
speech-to-text server this way.

The library ships **no hosted service** for this: an embedding host resolves
`McpToolProvider` and calls `ConnectServerAsync(serverId, config)` for each configured
server, explicitly — which is exactly what the runner host's startup step does, and what
`orkeon-host`'s connection service calls. A host that serves its tools calls
`AddOrkeonMcpServer(configuration)`, then resolves `McpServer` and runs
`RunStdioAsync(reader, writer)` over the streams it owns — what `orkeon mcp serve` does. `orkeon-host` serves no MCP.

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

## Serving the tools with `orkeon mcp serve`

```bash
orkeon mcp serve [--settings <file>] [--tools a,b,…] [--max-concurrency <n>] [--expose-output] [--crew <a.yaml> [<b.yaml> …]] [--allow-external-mounts]
```

The verb serves an Orkeon host's tools to an MCP client over **stdio** — the transport by
which Claude Desktop, editors and agent frameworks start a local server: the client launches
the process, writes one JSON-RPC message per line on its stdin and reads the answers on its
stdout, one per line; closing stdin ends the server.

- **The host is the one a run builds.** The settings resolve like `orkeon run`'s, anchored at
  the current directory: `--settings`, else `appsettings.json` there, else an
  `appsettings/appsettings.json` found walking up, else the per-user file of `orkeon init`,
  else the `ORKEON_*` environment variables alone; the file used is named on stderr. The
  mounts it declares (`Orkeon:FileSystem:Mounts`) are what the file tools reach, and the MCP
  servers it declares under `MCP:Servers` are connected first, their tools served too. What
  is served is what `orkeon run --list-tools` prints for the same settings, `human_input`
  aside: that tool answers for the operator of a run, and the client of an MCP server has a
  human of its own. No crew is loaded, so no tool a `.ork.ts` crew declares is served: those
  are registered when their crew loads.
- **`--crew <a.yaml> [<b.yaml> …]` serves crews as tools (FWK-24).** Several crews go space-separated after
  one flag, which cannot be repeated (like `--mount`). Each crew is loaded once at the start through the entry point
  `orkeon run` and `orkeon eval run --crew` load crews with, in a host of its own that carries what `orkeon run`
  gives a crew: the settings (`--settings`, else resolved from the crew's folder), the budget, the guardian, and the
  permission gate (`Orkeon:Security:PermissionGate`, `ask` refused without a channel). A crew that does not load, a
  folder outside the working directory without `--allow-external-mounts`, or two crews that give one name refuse the
  start, named; so does a `run_crew_*` name a tool of the host already holds. Only the crews named are served: no
  tool takes a path.
  - **The tool is `run_crew_<name>`**: the crew's `name`, lower case, every run of other characters one underscore.
    Its `inputSchema` is an object with one required string property per `{placeholder}` found in the tasks'
    `description` and `expectedOutput` (matched without regard to case, in order of first use) and no other
    property. A number or a boolean is read as its text; an object, an array or `null` is refused, so is an unknown
    or a missing argument (`-32602`, the names listed). The result is the crew's final output; a crew that fails is a
    result with `isError`, not a JSON-RPC error.
  - **Progress**: when the call carries `_meta.progressToken`, the server sends one `notifications/progress` per
    finished task (`progress` 1 to `total`, the number of tasks, with a `message`) before the answer; without a token
    nothing is sent. `notifications/cancelled` cancels the run.
  - **Prompts**: every task is the prompt `<name>_task_<n>` (1-based), its arguments the placeholders of that task, the
    message its description and expected output interpolated. The `prompts` capability is advertised only with
    `--crew`.
  - **Calls of one crew run one at a time.** The crew's host is shared by its calls, and so is what the host mounts
    (`/output`, `AUTO_SUMMARY.md`, the checkpoints), so a second call of a crew waits for the first (calls of other
    crews, and every other tool, run alongside). Each call still runs in a scope of its own, with its own task state, and
    a client's `notifications/cancelled` cancels the running crew: its agents see the token and the call is not answered.
  - **The run is recorded like any run of the host.** With `Orkeon:RunHistory:Enabled` on in the crew's settings, each call
    is a run of the history: `run.started`, the task events, `run.finished`, under its own run id (the one the
    checkpoints use), read with `orkeon runs`. With the history off nothing is written. The events carry no token
    count: the model's usage is not attributed per call.
  - **There is no offline mode.** The serve host reads the real settings and a crew calls the models they declare; the
    mock provider is reached only by pointing the profiles at it.
  - The progress `total` is the number of tasks of the crew; a retry or a hierarchical manager can run more task
    attempts than that, and the progress then stops at `total`.
- **The permission gate is not consulted for the client's calls (`GateExempt`).** The server's calls run through the
  guarded invocation point (Guardian, result sanitizer, audit) as the caller `mcp`, marked `GateExempt`: the AI client
  that launched `orkeon mcp serve` over stdio is not passed through the `IPermissionGate`. The reason: the client is
  the operator's own agent, and the operator already chose what it may reach (the settings, `--tools`, the mounts);
  stdout carries the protocol, so an `ask` would have no channel to a human and every call needing approval would be
  refused. A crew served with `--crew` is another matter: its own agents are gated as in `orkeon run`.
- **`--tools a,b,…`** serves the named tools only (comma-separated); a name the host does not
  have refuses the start, naming it. The other tools are neither listed nor callable, even those an
  upstream MCP server registers later (`tools/list_changed`): the allow-list is the server's own
  (`MCP:Server:AllowedTools`), not an unregistration. The tools of `--crew` are not subject to it.
- **Requests run concurrently** (`--max-concurrency <n>`, at least 1, default 4, or
  `MCP:Server:MaxConcurrency`). Each request is one task; at most `n` run at once and the
  others wait their turn, so a fifth slow call starts when one of the first four ends. A `--crew` call
  holds no slot: it waits for its crew (one run per crew at a time), so `tools/list` and `ping` stay
  answered meanwhile. The
  answers go through one channel to one writer: two answers never interleave on stdout, and
  they may arrive in a different order than the requests (the client matches them by `id`).
  When stdin closes, every request already read is answered before the server ends.
- **`notifications/cancelled`** cancels the request its `requestId` names, whether it runs or
  still waits for its turn: the tool call receives the cancellation, and a cancelled request is
  never answered, as the specification asks. An id the server does not know is ignored. A
  second request with an id already in flight is refused (`-32600`). `McpServer` is a
  singleton with no scoped dependency, so a request has no DI scope of its own.
- **`--expose-output`** (or `MCP:Server:ExposeOutput`) serves `/output` as read-only
  resources, off by default. `resources/list` pages the files under `/output` in path order
  (`ResourcesPageSize` per page, an opaque `nextCursor`), each as
  `orkeon:///output/<path>` with its `name`, `mimeType` and `size`; `resources/read` returns
  a file as `text` when it is UTF-8 text and as a base64 `blob` otherwise, up to
  `MaxResourceBytes`. Everything goes through `IFileSystemService`: the `/output` mount must
  exist and grant Read (a warning on stderr says when it is not mounted), and the URI is
  parsed strictly — no `.`/`..`/empty segment, no query or fragment, no `%2f`, anything but
  `orkeon:///output/…` refused. `/workspace` and every other mount are never exposed, no
  URI or error carries a physical path, and there is no `resources/subscribe` and no
  `resources/templates/list`. Without the flag `resources/*` answer `Method not found`.
- **Every call is guarded**: the invocation point of the [server](#server-mcpserver--dual-era-on-one-endpoint),
  under the caller `mcp` — a blocked call never reaches the tool, a result arrives truncated and
  tagged as data, and the audit trail records each call under the agent `mcp`; a blocked or
  failed one also reaches stderr, through the trail's log sink (`Agent=mcp`).
- **Stdout carries the protocol and nothing else.** Every log line, warning and diagnostic
  goes to stderr — where an MCP client records a server's output —, and so do `--help` and
  the usage errors.
- **Exit codes**: `0` the client closed the stream (or `--help`); `1` a usage error, a refused
  setting (an unknown key, a mount the settings declare that cannot be mounted, any other
  setting the runner host refuses — one `ERROR:` line names it), a `--tools` name the host does
  not have, a serve started by another serve (below); `2` an unexpected error. Ctrl+C ends the
  process.

A client configuration — Claude Desktop's `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "orkeon": {
      "command": "orkeon",
      "args": ["mcp", "serve", "--settings", "/home/me/orkeon/appsettings.json"]
    }
  }
}
```

A client starts its servers in a working directory of its own choosing: name the settings
file with `--settings`, or rely on the per-user file of `orkeon init`.

**A serve never starts under another one.** Before it connects the MCP servers of its
settings, a serve sets `ORKEON_MCP_SERVE` in its own environment — every process it starts
inherits it, and it is cleared when the serve ends — and a serve that finds it set refuses to
start: exit `1`, and one line on stderr, which the serve that started it joins to its
connection error
(``orkeon mcp serve: refused to start: another `orkeon mcp serve` started this one (ORKEON_MCP_SERVE is set), …``).
Settings that declare `orkeon mcp serve` itself under `MCP:Servers` therefore cost that one line, where each
server used to start another before answering, until the first gave up after 30 s: remove the
entry, or declare both servers in the client. `orkeon run` and `orkeon-host` set no marker,
and can use an `orkeon mcp serve` started on other settings. Like `ORKEON_DEBUG`, the variable
is also read by the `ORKEON_` configuration layer, as a key (`MCP_SERVE`) no setting is.

## Notifications, pagination and prompts (the client)

- **Pagination.** `tools/list`, `resources/list` and `prompts/list` follow `nextCursor` until a page names none
  (`McpClient.ListToolsAsync`, `ListResourcesAsync`, `ListPromptsAsync`). A server whose cursor never ends fails
  the listing after 100 pages, or after 10 000 items, (`InvalidOperationException`) instead of growing it without end.
- **Notifications.** A frame without an `id` is no longer dropped: `IMcpTransport.Notifications` is a
  `ChannelReader<JsonRpcNotification>` (256 entries, `DropOldest`, one warning per dropped entry). The stdio
  transport feeds it from the server's stdout; the HTTP transport feeds it with the notifications of an SSE-framed answer (as they arrive, not after its final
  message), of the `GET` stream and of `subscriptions/listen`. A request the server sends to the client
  (`sampling/createMessage`, `roots/list`, `elicitation/create`) is answered `-32601` on stdio and on HTTP: the client
  offers none of them; a `ping` on HTTP gets the empty result the specification requires.
- **Subscriptions.** On a modern server over HTTP, `McpToolProvider` keeps a `subscriptions/listen` request open
  (`McpClient.ListenAsync`: tools, prompts and resources list changes, plus the subscribed resources); when the
  server ends the stream it is opened again after the same backoff and the tool list is marked stale, since what the
  server said meanwhile is lost. A server that refuses the request is not asked again. The stream is not bounded by
  `CallTimeout`. On a legacy server, `McpToolProvider.SubscribeResourceAsync` sends `resources/subscribe` (`UnsubscribeResourceAsync` ends it and drops the cached read) (refused when
  the server does not announce `resources.subscribe`) and the updates arrive on the `GET` stream. A
  `notifications/resources/updated` invalidates only the cached read of that resource
  (`McpToolProvider.ReadResourceAsync` caches the reads of subscribed resources and nothing else, and a read that was in flight when the update arrived is not kept); the tools are not listed again. When the `GET` stream is opened again or the session initialised again, the cache is dropped and the subscriptions are sent again, since the notifications of the gap are lost.
- **`notifications/tools/list_changed`.** `McpToolProvider` watches each connected server. After a 500 ms wait
  (the notifications that arrive meanwhile are absorbed) it lists the tools again, applies the server's `Tools`
  filter, unregisters the tools the server dropped, swaps the ones whose definition changed for a new adapter in one registry step (`IToolRegistry.ReplaceToolAsync`: the name never stops resolving), and registers the new ones. A name
  another tool holds (a built-in, a script tool, another server's) is left to it and warned about as
  `mcp.tool.rejected`; `McpServerStatus.RejectedToolNames` names it. A task already running keeps the tool
  instances it was given: only the crew built afterwards sees the new set. The notifications are read as they arrive, so a flood of other frames cannot push a `list_changed` out of the bounded channel; one that arrives during a re-listing triggers another. A failed re-listing keeps the tools
  registered so far and is logged.
- **Prompts.** `McpClient.ListPromptsAsync` and `GetPromptAsync(name, arguments)` speak `prompts/*`. The built-in
  tool `mcp_prompt_get` (`server`, `name`, `arguments`) hands a prompt to an agent, or lists the server's prompts
  when `name` is empty; it is registered by `AddOrkeonMcp`. **The prompt text is untrusted content of the
  server and a prompt-injection vector**: it reaches the model as the result of a tool call, nothing more, and
  a server can put instructions in it. The tool declares `ToolAccess.Read`, so the permission gate lets the call
  through without asking, in every mode: connect only servers you trust, or keep the tool out of the `tools:` of
  an agent that must not read their prompts.

## Honest limitations

Aligned with [Known limitations](../reference/limitations.md) and the PUB-07 changelog
entry:

- **Subscriptions** — `subscriptions/listen` runs on HTTP only (the stdio client hears every notification the server sends); the 2026-07-28 filter shape (`notifications.toolsListChanged`, `promptsListChanged`, `resourcesListChanged`, `resourceSubscriptions`) is the one this client sends, checked against its own fake servers and the reference server, not yet against the published schema.
- **No sampling, roots or elicitation** — a server request for them is answered `-32601`: sampling would make Orkeon call an LLM for the server outside the budget, guard and cost of a run; roots would expose physical paths (VFS rule); elicitation waits for durable human-in-the-loop.
- **No multi-round-trip requests (MRTR)** — a modern `input_required` interim result is
  surfaced as an explicit tool error, never as partial data.
- **OAuth is client credentials and device flow only (FWK-16)** — no authorization-code flow, no protected-resource discovery (RFC 9728), no dynamic client registration (DCR); the device flow needs a host with a terminal (not `orkeon-host`); tokens are never persisted, so a device sign-in is asked again at the next run.
- **HTTP+SSE 2024-11-05 not supported** — the transport with a separate message endpoint is refused; Streamable HTTP is the HTTP transport. Interop against the reference server runs nightly (`McpReferenceServerInteropTests`, [MCP interop](../guides/mcp-interop.md)).
- **`2025-03-26` excluded** (mandatory JSON-RPC batching).
- **The server speaks stdio only** (`orkeon mcp serve`, or `RunStdioAsync` /
  `ProcessRequestAsync` in a C# host): no HTTP transport, prompts and crews as tools only for the crews `--crew` names (a client never gives a path), and resources are `/output` only (read-only, no `resources/subscribe`).

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
