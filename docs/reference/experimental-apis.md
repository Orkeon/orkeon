> 🇫🇷 [Version française](../fr/reference/experimental-apis.md)

# Experimental APIs

Some Orkeon surfaces are shipped for early feedback but are **not yet covered by the
API stability commitment** (see [CONTRIBUTING — Versioning and API stability](../../CONTRIBUTING.md#versioning-and-api-stability)).
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
| `ORKEXP001` | **A2A (agent-to-agent protocol)** — `IA2AClient`, `IA2AServer`, `IA2AAgentDiscovery`, `IA2ATaskRouter`, `IA2ATaskStore`, `IAgentRegistrationStore`, the `A2A*` implementations, options and wire types, `StateStoreA2ATaskStore`, and `A2AExtensions` (`AddOrkeonA2A`, `AddOrkeonA2ATaskPersistence`) | The implementation predates the A2A v1.0 specification; the conformance pass (PUB-08) added task persistence and settled certificate revocation, and the binding alignment toward v1.0 (HTTP+JSON first) is still to come — see the [conformance matrix](./a2a-conformance.md). |
| `ORKEXP002` | **Autonomous orchestration** — `AgentExecutionBudget` and its budget types, `IAgentChannel`/`InMemoryAgentChannel` and its request/response records, `AutonomousProcessStrategy`, `SpawnAgentTool` and its request/response records | The youngest orchestration mode: budget dimensions, spawn semantics and A2A channel contracts may still move with field feedback. |
| `ORKEXP003` | **Corrective RAG** — `CorrectiveRagPipeline` (+ `CorrectiveRagPipelineDependencies`, `RagGraphState`), `IRetrievalEvaluator`, `IGroundednessChecker`, `IWebDocumentRetriever`, the heuristic and LLM evaluator/checker implementations, and `CorrectiveRagExtensions` (`AddOrkeonCorrectiveRag`) | The CRAG loop contracts (verdicts, re-loop bounds, web-fallback policy) are calibrated against a young evaluation corpus. |
| `ORKEXP004` | **MCP integration** — `McpClient`, `McpServer`, `McpToolProvider`/`McpToolAdapter`, the transports (`IMcpTransport`, `StdioMcpTransport`, `SseMcpTransport`), options, JSON-RPC and protocol types, and `McpServiceExtensions` (`AddOrkeonMcp`) | Dual-era since PUB-07 (modern `2026-07-28` + legacy initialize revisions), but parts of the modern surface are still unimplemented (`subscriptions/listen`, multi-round-trip requests, OAuth) and the wire types may still move. |

Everything not marked `[Experimental]` and listed in a package's `PublicAPI.Shipped.txt`
is covered by the stability commitment: a change there is a **breaking change** and is
treated as such by the versioning policy.

The attribute sits on the types, so the diagnostic fires where your code **names** one of
them. Registering the stable entry points does not: `AddOrkeonInfrastructure(configuration)`
wires MCP and `AddOrkeonRag(configuration)` wires the corrective graph without your code
referencing an experimental type — only calling `AddOrkeonMcp`, `AddOrkeonA2A`,
`AddOrkeonCorrectiveRag` or resolving/implementing one of the types above needs the opt-in.

The `ProcessType.Autonomous` **value** itself is not experimental — selecting the mode
from YAML keeps working; the attribute covers the .NET types you would reference from
code.

Inside this repository (`src/`, `tests/`, `examples/`) the four IDs are suppressed
centrally, in each of the three `Directory.Build.props`: the framework wires — and its tests and examples exercise — its own
experimental surfaces by design.
