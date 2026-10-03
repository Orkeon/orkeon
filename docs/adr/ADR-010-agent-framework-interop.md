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

## Amendment — 2026-10-01 (GAP-25): one scope per turn

`AddOrkeonAgentFramework()` registered a **singleton** `ICrewAgentFactory` that captured the
host's **scoped** `ICrewOrchestrationService`: every `CrewAgent` of a host shared one
orchestrator, and its scoped repositories, for the life of the process, and a host validating
scopes (the default in Development) refused to resolve the factory. The orchestrator stays
scoped — its repositories are scoped by design — and the factory now holds the host's
`IServiceScopeFactory` only.

- `ICrewAgentFactory.Create(loadCrew, name, description?)` replaces `Create(crewId, …)` and
  `Create(crew)`. Every turn of the agent it returns opens a scope, calls `loadCrew` with the
  scope's provider — it registers the crew there (adds its agents, tasks and crew to the
  scope's repositories, or loads it through the scope's `ICrewFactory`) and returns its id —
  kicks that crew off through the scope's orchestrator, and disposes the scope. A crew
  registered once in a root-resolved repository was the old contract; a scope never sees it.
- The same form is public as `new CrewAgent(IServiceScopeFactory, loadCrew, name, description?)`;
  its agent id is `orkeon-crew-<name>` and its `CrewId` is `null` (a fresh crew every turn).
  `new CrewAgent(orchestrator, crewId|crew)` stays, for a caller that owns the orchestrator and
  its scope.

## Amendment — 2026-10-03 (GAP-34): the MAF agent answers

**What was false.** `WithAgentFrameworkAgent` set `Agent.FunctionCallingLlm`, a field nothing read:
an agent built with it ran its tasks on the host's default model (or its profile), with its Orkeon
tools, and the MAF agent heard nothing — neither its instructions, nor its tools, nor its memory
served. The interop test checked the field; the example exercised the tool form only. The
consequence "an Orkeon crew can … be answered by a MAF agent" was never run end to end, and the
sentence of decision 2, "nothing in Domain, Application or Infrastructure changed for it", no longer
holds:

- **Domain.** `Agent.FunctionCallingLlm` is `Agent.Llm` (`AgentCreateOptions.Llm`,
  the `llm` parameter of `Agent.Create`): the provider the agent's turns run on,
  CrewAI's `llm` given as an object. An agent runs on its own provider or on a host profile, never
  both — `Agent.Create` and `AgentBuilder.Build()` refuse both. `LlmProviderCapabilities.RunsOwnTools`,
  declared by `AIAgentLlmProvider`: an agent whose own provider runs its own tools is refused Orkeon
  tools and delegation when it is created, and `AddTool` and `UpdateConfiguration` keep the rule.
- **Application.** `ILlmProfileRegistry.ForProvider(provider)` builds the client of an agent's own
  provider, once per instance, metered. The orchestrator runs on it the agent's turns, their
  correction round and its ballot, after the profile a task's `llm_override` names and before the
  agent's profile. A task that falls on a provider running its own tools — the agent's own, a
  profile's, the default — with tools to hold (its own, `human_input`) fails before any call, naming
  the tools and the two remedies.
- **Infrastructure.** `LlmProfileRegistry.ForProvider` is a new entrance of the metered path.
  `ManagerLlmResolver` runs a hierarchical manager agent's own provider (`provider:<name>`), after
  `Crew.ManagerLlm`. `MeteredLlmProvider` counts a call once: an outer call during which a meter nearer
  the model counted reports nothing, so a MAF agent built over Orkeon's own model is counted under the
  real provider and model, and one on a client Orkeon does not meter under `agent-framework:<name>`.

**The session.** One per provider, as decision 2 chose, so a MAF agent's memory spans the Orkeon
agent's turns and tasks — but fed once: a call that extends the conversation the session holds (the
previous call's messages, then the provider's answer) sends only what is new, any other call — a new
task — sends everything, and calls run one at a time. The tasks of one MAF agent therefore run one
after another, even in a parallel wave; an earlier task's output reaches the model by the session and
by the prompt's previous outputs, and the session grows with each task (a MAF `ChatReducer` bounds it).

**What the bridge does not send, it says.** The options of a call's configuration (model, temperature,
response format, thinking…) never reach a MAF agent; each one declared is a structured warning, once
per crew run and option. Streaming is unchanged: a streamed turn receives the MAF answer as one
fragment.

**Verified.** End to end in the interop suite (the MAF agent receives the composed prompt, the default
is not called, one usage event under the agent; counted once over Orkeon's model; tools refused at
build, at `AddTool` and at run), and by a third section of `examples/interop/agent-framework/`, run on
the echo provider; the campaign on a local model is the owner's.
