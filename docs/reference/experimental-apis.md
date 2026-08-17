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
| `ORKEXP001` | **A2A (agent-to-agent protocol)** — `IA2AClient`, `IA2AServer`, `IA2AAgentDiscovery`, `IA2ATaskRouter`, `IAgentRegistrationStore`, the `A2A*` implementations and `AddOrkeonA2A` | The implementation predates the A2A v1.0.1 specification; the conformance pass (PUB-08) will reshape parts of it (task persistence, certificate revocation). |
| `ORKEXP002` | **Autonomous orchestration** — `AgentExecutionBudget` and its budget types, `IAgentChannel`/`InMemoryAgentChannel`, `AutonomousProcessStrategy`, `SpawnAgentTool` | The youngest orchestration mode: budget dimensions, spawn semantics and A2A channel contracts may still move with field feedback. |
| `ORKEXP003` | **Corrective RAG** — `CorrectiveRagPipeline`, `IRetrievalEvaluator`, `IGroundednessChecker` and the `Corrective` evaluator/checker implementations | The CRAG loop contracts (verdicts, re-loop bounds, web-fallback policy) are calibrated against a young evaluation corpus. |
| `ORKEXP004` | **MCP integration** — `McpClient`, `McpServer`, transports, options and protocol types | Pinned to protocol version `2024-11-05`; the upgrade to the current MCP specification (PUB-07) will change the wire surface. |

Everything not marked `[Experimental]` and listed in a package's `PublicAPI.Shipped.txt`
is covered by the stability commitment: a change there is a **breaking change** and is
treated as such by the versioning policy.

The `ProcessType.Autonomous` **value** itself is not experimental — selecting the mode
from YAML keeps working; the attribute covers the .NET types you would reference from
code.

Inside this repository (`src/`, `tests/`, `examples/`) the four IDs are suppressed
centrally: the framework wires — and its tests and examples exercise — its own
experimental surfaces by design.
