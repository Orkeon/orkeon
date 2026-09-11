> 🇫🇷 [Version française](../fr/adr/ADR-010-agent-framework-interop.md)

> **See also**: [ADR-002](./ADR-002-tool-abstractions-shared-kernel.md) · [ADR-005](./ADR-005-famille-tools-heterogene.md) · [Back to the index](../INDEX.md)

# ADR-010 — Interoperability with Microsoft Agent Framework is a separate package, in both directions

**Status**: Accepted · **Date**: 2026-09-11
· **Scope**: `src/interop/Orkeon.Interop.AgentFramework`, `src/packaging/Orkeon.Interop.AgentFramework`, the NuGet lineup

## Context

Microsoft Agent Framework (MAF, `Microsoft.Agents.AI`) has been GA since April 2026 and its
declarative YAML workflows since July. Its `AIAgent` abstraction is what a growing share of
.NET developers already have in their projects. The question was not whether to compete with
it on the promise the README used to open with — "describe your agents in YAML" — but how a
developer who has MAF code can try Orkeon **without giving anything up**, and how an Orkeon
crew can be one more agent in a MAF workflow.

Orkeon already carried two `IChatClient` adapters
(`LlmProviderToChatClientAdapter`, `ChatClientToLlmProviderAdapter`), so the model layer was
bridged. The agent layer was not: nothing turned a crew into an `AIAgent`, nothing let an
`AIAgent` answer for an Orkeon agent or be called by one.

## Decision

1. **One package, `Orkeon.Interop.AgentFramework`, depending on `Orkeon` and on
   `Microsoft.Agents.AI.Abstractions` only.** It is *not* folded into the `Orkeon` umbrella:
   the MAF dependency is a consumer's choice, and a consumer who never uses MAF must not
   restore it. The package follows the PUB-25 wrapper pattern (`src/packaging/`) and joins
   the NuGet.org lineup as its eighth id.
2. **Both directions, three types, no new abstraction on the Orkeon side.**
   - `CrewAgent : AIAgent` — an Orkeon crew as a MAF agent. `RunAsync` is one kickoff:
     the conversation becomes the crew's initial context, the final output the assistant
     message, token telemetry the `Usage`. Sessions hold nothing (a crew has no
     conversation state of its own between kickoffs) and serialise to an empty bag.
   - `AIAgentLlmProvider : ILlmProvider` — a MAF agent as the *model* of an Orkeon agent
     (`AgentBuilder.WithAgentFrameworkAgent`). The Orkeon agent keeps role, goal and tasks;
     the MAF agent answers, on one MAF session for the provider's lifetime.
   - `AIAgentTool : ToolBase<…>` — a MAF agent as a *tool* of an Orkeon agent
     (`AgentBuilder.WithAgentFrameworkTool`), the mirror of MAF's `AsAIFunction()`.
   The library builds on ports Orkeon already exposes (`ICrewOrchestrationService`,
   `ILlmProvider`, `ToolBase`); nothing in Domain, Application or Infrastructure changed
   for it — with one exception below, which was a bug.
3. **The initial context of a kickoff reaches the agents.** `CrewInput.InitialContext` was
   mapped into the domain input and read by nothing: `--initial-context`, Studio's field and
   every programmatic `CrewInput.Empty("…")` reached no prompt. The orchestrator now exposes
   it as the `initial_context` prompt variable (a caller-supplied variable of that name
   wins). `CrewAgent` relies on it; so does every user who ever passed `--initial-context`.

## Consequences

- A MAF workflow can call an Orkeon crew like any other agent; an Orkeon crew can delegate
  to, or be answered by, a MAF agent. Verified end to end on a local model
  (`examples/interop/agent-framework/`, both directions, `llama3.2:1b` via Ollama).
- The interop tracks `Microsoft.Agents.AI.Abstractions` 1.x. A breaking change on the MAF
  side is absorbed in this package alone; the umbrella never sees it.
- `Orkeon.Interop.AgentFramework` is part of the scope freeze's *interoperability* exception
  (CONTRIBUTING): interop lowers the cost of trying Orkeon, it does not widen its surface.
- Streaming from a crew is the final answer as one update: a crew has no token stream to
  forward. A streaming caller works; it does not see intermediate agent output.
