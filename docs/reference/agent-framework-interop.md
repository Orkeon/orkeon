> 🇫🇷 [Version française](../fr/reference/agent-framework-interop.md)

# Microsoft Agent Framework interop

`Orkeon.Interop.AgentFramework` bridges Orkeon and **Microsoft Agent Framework** (MAF,
`Microsoft.Agents.AI`) in both directions: an Orkeon crew runs as a MAF `AIAgent`, and a MAF
`AIAgent` works inside an Orkeon crew — as an agent's model or as one of its tools. The decision
and its boundaries are recorded in [ADR-010](../adr/ADR-010-agent-framework-interop.md).

| | |
|---|---|
| Package | `Orkeon.Interop.AgentFramework` — depends on the `Orkeon` umbrella and on `Microsoft.Agents.AI.Abstractions` (see the [publication matrix](publication-matrix.md)) |
| Namespaces | `Orkeon.Interop.AgentFramework`, `Orkeon.Interop.AgentFramework.DependencyInjection` |
| Example | [`examples/interop/agent-framework`](https://github.com/orkeon/orkeon/blob/main/examples/interop/agent-framework/Program.cs) — both directions on one host and one model |

## Install and register

```bash
dotnet add package Orkeon.Interop.AgentFramework --prerelease
dotnet add package Microsoft.Agents.AI --prerelease   # only to build MAF agents yourself (ChatClientAgent, AsAIAgent)
```

The package brings `Orkeon` and `Microsoft.Agents.AI.Abstractions` with it. The bridge needs an
Orkeon host — the crew runs through its `ICrewOrchestrationService` — so it goes on top of the
usual registrations:

```csharp
services.AddOrkeonApplication();
services.AddOrkeonInfrastructure(configuration);
// … the tool suites your crews use …
services.AddOrkeonAgentFramework();   // Orkeon -> MAF only; MAF -> Orkeon needs no registration
```

## What crosses the bridge

| Direction | In | Out | Stays behind |
|---|---|---|---|
| Orkeon crew → MAF (`CrewAgent`) | the MAF conversation, as the crew's initial context | the crew's final output as one assistant message, its token usage | the crew's tools, mounts, budget, memory and orchestration — MAF sees an agent that answers |
| MAF agent → Orkeon agent's model (`WithAgentFrameworkAgent`) | the Orkeon agent's prompts, role-mapped | the MAF answer text and its usage | the Orkeon agent's tools and `LlmConfig`; MAF's own tools stay usable on its side |
| MAF agent → Orkeon tool (`WithAgentFrameworkTool`) | the `request` string | `answer` and `agent` | the MAF agent's tools, session and memory |

## An Orkeon crew as a MAF agent — `CrewAgent`

`CrewAgent : AIAgent` wraps a crew the host has registered. Every `RunAsync` is **one crew
kickoff** (`ICrewOrchestrationService.KickoffAsync`):

- **In** — the conversation becomes the crew's initial context: a single message verbatim;
  several messages as `Conversation so far:` (the earlier turns, one `role: text` line each)
  followed by `Request:` and the last message.
- **Out** — one assistant message carrying the crew's final output, authored by the agent's
  name. `FinishReason` is `Stop`, or `error` when the crew did not succeed. `Usage` carries the
  prompt, completion and total tokens when the run measured them, and is absent otherwise.
- **Streaming** — `RunStreamingAsync` works, but a crew answers when it is done: the stream is
  the final response, not a token stream.
- **Session** — holds nothing. A crew keeps no conversation state between kickoffs (memory,
  when enabled, is the crew's own business); the session serializes to an empty object and
  deserializes from anything, so callers that persist sessions keep working.
- **Identity** — the agent id is `orkeon-crew-<crewId>`, which is also the default name.
  `AgentRunOptions` are not read.

```csharp
services.AddOrkeonAgentFramework();   // registers ICrewAgentFactory

var factory = host.Services.GetRequiredService<ICrewAgentFactory>();
AIAgent crewAgent = factory.Create(crew.Id, "orkeon-summariser", "Summarises text in two sentences");
// or factory.Create(crew) — described by the crew's goal

var answer = await crewAgent.RunAsync("Summarise last week's incidents");
```

`AddOrkeonAgentFramework()` registers only the `ICrewAgentFactory` singleton (`TryAdd`), over the
host's `ICrewOrchestrationService`; the crew must already be in the orchestrator's repositories.
`new CrewAgent(orchestrator, crewId, name?, description?)` and `new CrewAgent(orchestrator, crew)`
do the same without DI. The result drops into any MAF workflow, orchestration or `AsAIFunction()`
chain.

## A MAF agent inside an Orkeon crew

### As an agent's model — `WithAgentFrameworkAgent`

```csharp
var agent = new AgentBuilder()
    .Role("Reviewer").Goal("Review the change")
    .WithAgentFrameworkAgent(mafAgent)
    .Build();
```

`WithAgentFrameworkAgent(agent)` is `WithLlm(new AIAgentLlmProvider(agent))`: the Orkeon agent
keeps its role, goal and tasks, and every prompt it sends is a run of the MAF agent.
`AIAgentLlmProvider : ILlmProvider`:

- keeps **one MAF session for its lifetime**, created on first use, so a MAF agent with memory
  or context providers sees one continuous conversation across the Orkeon agent's iterations;
- maps the roles `system`, `assistant` and `tool`, and anything else to `user`;
- ignores the `LlmConfig` it is handed (temperature, max tokens, response format) — the MAF agent
  is configured on its own side;
- reports `Name` = `agent-framework:<name or id>` and `LlmProviderCapabilities.Unknown`, and
  relays the MAF usage as the response's token counts;
- leaves **tool calling on the MAF side**: the MAF agent uses whatever tools it carries, and the
  Orkeon agent's own tools are not offered to it. An agent that needs both wraps the MAF agent as
  a tool instead.

### As a tool — `WithAgentFrameworkTool`

```csharp
var planner = new AgentBuilder()
    .Role("Planner").Goal("Write a plan and have it reviewed")
    .WithAgentFrameworkTool(reviewer, "ask_reviewer", "Ask the reviewer for the biggest risk of a plan")
    .Build();
```

`WithAgentFrameworkTool(agent, toolName?, description?)` adds an `AIAgentTool` next to the agent's
other tools — the mirror of MAF's `AsAIFunction()`:

| | |
|---|---|
| Name | `toolName`, or `agent_<slug>` of the MAF agent's name (lower-case, every other character `_`) |
| Description | `description`, or the MAF agent's own, or a generated sentence |
| Category | `Delegation` |
| Input | `request` (required, refused when empty) — sent as one user message |
| Output | `answer` (the MAF agent's text), `agent` (its name) |
| Session | one per tool instance: successive calls continue the same MAF conversation |

## What the bridge does not do

- It does not translate tools across the boundary: an Orkeon agent's tools stay Orkeon's, a MAF
  agent's stay MAF's.
- It does not stream tokens out of a crew (`CrewAgent` yields the final answer).
- It registers nothing in the shipped runners: `orkeon`, `orkeon-host` and the REPL do not
  reference the package. An embedding host adds it — the example does so through
  `RunnerHost.Build`'s `configureServices` hook ([hosting](hosting.md)).

---

> **See also**: [ADR-010](../adr/ADR-010-agent-framework-interop.md) ·
> [Publication matrix](publication-matrix.md) · [Hosting](hosting.md) ·
> [Back to index](../INDEX.md)
