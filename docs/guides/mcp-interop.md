> 🇫🇷 [Version française](../fr/guides/mcp-interop.md)

# Check the MCP client against the reference server

> **See also**: [MCP integration](../architecture/mcp.md) · [Known limitations](../reference/limitations.md) · [Whisper through MCP](./whisper-mcp.md)

The MCP client is tested against fakes that speak the protocol, and against the reference server of the MCP
project, `@modelcontextprotocol/server-everything`, which exposes tools, resources, prompts and
resource subscriptions. Two ways to run it: the nightly test, and the MCP Inspector by hand.

## The nightly test

`McpReferenceServerInteropTests` (`tests/core/Orkeon.Infrastructure.Tests/MCP/`, `[Trait("Category", "Integration")]`)
starts `npx -y --prefer-offline @modelcontextprotocol/server-everything` once over **stdio** and once over **Streamable
HTTP** (`streamableHttp`, on a loopback port it picks), connects it through `McpToolProvider` and checks:

- the server's tools are registered (`echo` among them);
- the prompts are listed and one without arguments is fetched;
- the resources are listed (all pages followed) and one is read;
- a subscribed resource is updated by the server (`resources/updated`), which proves that the notifications the
  server sends on its own reach the client — on stdio through the process output, on HTTP through the `GET` stream.

`integration.yml` installs Node with `actions/setup-node` after it cleans the runner's disk (the cleanup removes the
runner's own npm). Without `npx` the test is skipped on a workstation and fails when `CI=true`. It downloads the
package: on a metered connection run it once with the cache warm (`npx -y @modelcontextprotocol/server-everything --help`
fills it), then locally:

```bash
dotnet test tests/core/Orkeon.Infrastructure.Tests/Orkeon.Infrastructure.Tests.csproj --no-build \
  --filter "FullyQualifiedName~McpReferenceServerInteropTests"
```

`tools/list_changed` has no deterministic trigger on the reference server: that path is covered by the fake servers
(`McpToolListChangedTests`, `McpSubscriptionTests`).

## By hand with the MCP Inspector

The [MCP Inspector](https://github.com/modelcontextprotocol/inspector) is the project's debugging UI. Use it to see
what a server of yours sends before pointing Orkeon at it:

1. Start your server (Streamable HTTP, for instance `http://127.0.0.1:3001/mcp`).
2. `npx @modelcontextprotocol/inspector`, open the URL it prints, choose the transport **Streamable HTTP**, enter the URL.
3. Connect, then list tools, resources and prompts. Compare with what `orkeon mcp` reports for the same `MCP:Servers`
   entry.
4. In the History pane check the headers of the POSTs: `Mcp-Session-Id` is sent back after `initialize`, a request answered
   `404` makes Orkeon initialise again.

The transport Orkeon does **not** speak is the 2024-11-05 HTTP+SSE one, where the server announces a separate message
endpoint: the Inspector offers it as "SSE"; choose Streamable HTTP.
