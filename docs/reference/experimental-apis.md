> 🇫🇷 [Version française](../fr/reference/experimental-apis.md)

# Experimental APIs

Some Orkeon surfaces are shipped for early feedback but are **not yet covered by the
API stability commitment** (see the [stability and versioning policy](./stability.md)).
They are marked with `[Experimental]`
(`System.Diagnostics.CodeAnalysis.ExperimentalAttribute`): referencing them produces a
**compiler error** with one of the diagnostic IDs below, which you must suppress
explicitly to opt in — that suppression is your acknowledgement that the surface may
change or disappear in any release, minor versions included.

```xml
<!-- opt in, per project -->
<PropertyGroup>
  <NoWarn>$(NoWarn);ORKEXP002</NoWarn>
</PropertyGroup>
```

or, per call site:

```csharp
#pragma warning disable ORKEXP002
var budget = AgentExecutionBudget.Default;
#pragma warning restore ORKEXP002
```

## Diagnostic IDs

| ID | Surface | Why it is experimental |
|---|---|---|
| `ORKEXP001` | **A2A (agent-to-agent protocol)** — `IA2AClient`, `IA2AServer`, `IA2AAgentDiscovery`, `IA2ATaskRouter`, `IA2ATaskStore`, `IAgentRegistrationStore`, the `A2A*` implementations, options and wire types, `StateStoreA2ATaskStore`, and `A2AExtensions` (`AddOrkeonA2A`, `AddOrkeonA2ATaskPersistence`) | The implementation predates the A2A v1.0 specification; it has task persistence and a settled position on certificate revocation, and the binding alignment toward v1.0 (HTTP+JSON first) is still to come — see the [conformance matrix](./a2a-conformance.md). |
| `ORKEXP002` | **Autonomous orchestration** — `AgentExecutionBudget` and its budget types, `IAgentChannel`/`InMemoryAgentChannel` and its request/response records, `AutonomousProcessStrategy`, `SpawnAgentTool` and its request/response records, and the method `CrewBudget.ToExecutionBudget()` (the type `CrewBudget` itself is stable) | The youngest orchestration mode: budget dimensions, spawn semantics and A2A channel contracts may still move with field feedback. |
| `ORKEXP003` | **Corrective RAG** — `CorrectiveRagPipeline` (+ `CorrectiveRagPipelineDependencies`, `RagGraphState`), `IRetrievalEvaluator`, `IGroundednessChecker`, `IWebDocumentRetriever`, the heuristic and LLM evaluator/checker implementations, and `CorrectiveRagExtensions` (`AddOrkeonCorrectiveRag`) | The CRAG loop contracts (verdicts, re-loop bounds, web-fallback policy) are calibrated against a young evaluation corpus. |
| `ORKEXP004` | **MCP integration** — `McpClient`, `McpServer`, `McpToolProvider`/`McpToolAdapter`, the transports (`IMcpTransport`, `StdioMcpTransport`, `StreamableHttpMcpTransport`), options, JSON-RPC and protocol types, and `McpServiceExtensions` (`AddOrkeonMcp`, `AddOrkeonMcpServer`) | Dual-era (the modern `2026-07-28` revision plus the legacy initialize revisions), but parts of the modern surface are still unimplemented (multi-round-trip requests) and the wire types may still move. |
| `ORKEXP005` | **Per-model capabilities** — `IModelCapabilityRegistry`, `ModelCapabilityRegistry`, `ModelCapabilities` and `ModelCapabilityOverride` (audio input and output, vision, thinking support, reasoning efforts, by model id prefix, or `<provider>/<model>` for a gateway through `TryGet(model, provider)`), read by `ContentConverter`, every HTTP provider and the profile registry; the host overrides it with `Llm:ModelCapabilities:<id>` or `Llm:ModelCapabilities:<provider>/<model>` | The built-in table is a snapshot of the vendors' families and the effort lists change with each release; the shape of an entry (output audio, per-model limits beyond efforts) may still grow. |
| `ORKEXP006` | **Host HTTP security** — `HttpRequestAuthenticator`, `HttpAuthorization` and `HttpSecurityOptions`, the authentication of an HTTP listener extracted from the A2A server for the host API | New, and shaped by the one consumer so far (`orkeon-host`): the verdict type and the options may change when a second listener uses them. |
| `ORKEXP007` | **Run history ports** — `IRunEventSink`, `IRunHistoryReader`, `RunEvent`, `RunEventScope`, `RunQuery`, `RunSummary`, `RunStatus`, `RunHistoryStore`, `RunHistoryOptions`, `RunHistoryRetention`, `RunEventWire`, `RunEventSinkExtensions`, `SqliteRunEventSink`, `RedactingRunEventSink`, and the liveness types `RunLiveness`, `RunProcessIdentity`, `IRunProcessProbe` and `SystemRunProcessProbe`, and the property `ScriptingHostPorts.RunEvents` (the type `ScriptingHostPorts` itself is stable) | The ports are new: the event shape, the query and the summary may move once the host API reads the history over HTTP. |

Everything not marked `[Experimental]` and listed in a package's `PublicAPI.Shipped.txt`
is covered by the stability commitment: a change there is a **breaking change** and is
treated as such by the versioning policy — save a member whose signature carries a type of
Jint, TreeSitter.DotNet or Terminal.Gui, which follows that library's major version
([third-party clause](./stability.md#types-of-a-third-party-library)).

The attribute sits on the types, so the diagnostic fires where your code **names** one of
them. Registering the stable entry points does not: `AddOrkeonInfrastructure(configuration)`
wires MCP and `AddOrkeonRag(configuration)` wires the corrective graph without your code
referencing an experimental type — only calling `AddOrkeonMcp`, `AddOrkeonA2A`,
`AddOrkeonCorrectiveRag` or resolving/implementing one of the types above needs the opt-in.

The `ProcessType.Autonomous` **value** itself is not experimental — selecting the mode
from YAML keeps working; the attribute covers the .NET types you would reference from
code.

Inside this repository the IDs are suppressed centrally: all seven in `src/` and `tests/`,
`ORKEXP001`–`ORKEXP005` in `examples/` (each folder's `Directory.Build.props`) — the framework wires, and its
tests and examples exercise, its own experimental surfaces by design.
