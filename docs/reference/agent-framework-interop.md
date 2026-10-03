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
| MAF agent → Orkeon agent's model (`WithAgentFrameworkAgent`) | the Orkeon agent's prompts, role-mapped, each message once on the MAF session | the MAF answer text and its usage, metered once | the Orkeon agent's `LlmConfig` (each option declared is a warning); no Orkeon tool — refused; MAF's own tools stay usable on its side |
| MAF agent → Orkeon tool (`WithAgentFrameworkTool`) | the `request` string | `answer` and `agent` | the MAF agent's tools, session and memory |

## An Orkeon crew as a MAF agent — `CrewAgent`

`CrewAgent : AIAgent` wraps an Orkeon crew. Every `RunAsync` is **one crew kickoff**
(`ICrewOrchestrationService.KickoffAsync`):

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
- **Identity** — the agent id is `orkeon-crew-<name>` for an agent built by the factory, and
  `orkeon-crew-<crewId>` (also the default name) for one built over an orchestrator.
  `AgentRunOptions` are not read.
- **Scope** — an agent built by the factory runs **every turn in a scope of its own**: the
  orchestrator and the crew, agent and task repositories are scoped services, so two turns, or two
  agents, never share them, and the factory resolves on a host that validates scopes.

```csharp
services.AddOrkeonAgentFramework();   // registers ICrewAgentFactory

var factory = host.Services.GetRequiredService<ICrewAgentFactory>();
AIAgent crewAgent = factory.Create(
    async (services, ct) =>           // the turn's scope: register the crew there, return its id
    {
        await services.GetRequiredService<IAgentRepository>().AddAsync(summariser, ct);
        await services.GetRequiredService<ITaskRepository>().AddAsync(summarise, ct);
        await services.GetRequiredService<ICrewRepository>().AddAsync(crew, ct);
        return crew.Id;
    },
    "orkeon-summariser",
    "Summarises text in two sentences");

var answer = await crewAgent.RunAsync("Summarise last week's incidents");
```

The loader may as well load a crew file through the scope's `ICrewFactory`
(`CreateFromFileAsync`) and return the id of what it loaded. `AddOrkeonAgentFramework()` registers
only the `ICrewAgentFactory` singleton (`TryAdd`), over the host's `IServiceScopeFactory`;
`new CrewAgent(scopeFactory, loadCrew, name, description?)` is the same without the factory, and
`new CrewAgent(orchestrator, crewId, name?, description?)` / `new CrewAgent(orchestrator, crew)` run
a registered crew through an orchestrator **the caller owns**, with the scope it lives in. The
result drops into any MAF workflow, orchestration or `AsAIFunction()` chain.

## A MAF agent inside an Orkeon crew

### As an agent's model — `WithAgentFrameworkAgent`

```csharp
var reviewer = new AgentBuilder()
    .Role("Reviewer").Goal("Find the biggest risk of a change")
    .WithAgentFrameworkAgent(mafAgent, logger)   // logger optional: it hears the options not sent
    .Build();
```

`WithAgentFrameworkAgent(agent, logger?)` is `WithLlm(new AIAgentLlmProvider(agent, logger))`: the MAF
agent becomes the Orkeon agent's own provider (`Agent.Llm`). The Orkeon agent keeps its role, goal and
tasks; what answers is the MAF agent.

- **What runs on it** — the turns of the agent's tasks and their output-correction round, its work as
  a hierarchical manager (it assigns and reviews), its ballot in a consensual vote. Another agent can
  still delegate to it (`delegate_work_to_coworker`). The MAF agent receives the prompt Orkeon composed — the
  system message (role, goal, backstory, guardrails, response template) and the user message (the
  task, its expected output, its plan, the variables, previous outputs, recalled memories, knowledge),
  screened by the Guardian first — and adds its own instructions and context providers to it.
- **The order** — the profile a task's `llm_override` names wins for that task, `default` included;
  otherwise the agent runs on its own provider. An agent runs on its own provider or on a host
  profile, never both: `Build()` and `Agent.Create` refuse the provider together with
  `WithLlmConfig(LlmConfig.OnProfile(name))`.
  A crew's `WithManagerLlm` still wins over its manager agent's own provider.
- **No Orkeon tool** — a MAF agent calls the tools it carries, never Orkeon's: `AIAgentLlmProvider`
  declares `LlmProviderCapabilities.RunsOwnTools`. Such an agent is refused `WithTool`, `WithTools` and
  `AllowDelegation` when it is built (`BuilderValidationException`), and `AddTool` later; a task that
  falls on a provider that runs its own tools — the agent's own, a profile's or the host's default —
  with tools to hold (its own `tools:`, `human_input`, the `delegate_work_to_coworker` and
  `ask_question_to_coworker` of an agent that allows delegation, as a YAML agent does unless it writes
  `allowDelegation: false`) fails
  before any call. The message names the tools and the two remedies: give the tool to the MAF agent, or
  give the MAF agent to an Orkeon agent as a tool (`WithAgentFrameworkTool`, below) — and, for the
  delegation tools, switch delegation off. One Orkeon agent cannot hold the same MAF agent both ways.
- **One session, each message once, one call at a time** — the provider keeps one MAF session for its
  lifetime, created on first use, so a MAF agent with memory or context providers sees one continuous
  conversation. The agent loop sends the whole conversation on every turn; a call that extends the
  conversation the session holds — the previous call's messages, then the answer the provider gave
  them — sends the session only what is new, and any other call — a new task — sends all its messages,
  the session keeping the earlier task. Calls run one at a time: the tasks of one MAF agent run one
  after another, even in a parallel wave or alongside an `asyncExecution` task. The session grows with
  every task, and an earlier task's output reaches the model twice — by the session and by the prompt's
  previous outputs; a `ChatReducer` on the MAF side bounds it.
- **Counted once** — the run builds a client over the provider once per instance and meters it as the
  agent's work (`operation: agent`, `manager` for a manager's calls). A MAF agent built over Orkeon's own
  metered model is counted once, by that model's meter, under the real provider and model: the meter
  nearest the model counts. A MAF agent on a client Orkeon does not meter is counted under
  `agent-framework:<name>`, with the usage its response carries (estimated when it carries none).
- **Streaming** — a streamed turn (`--stream`, `KickoffStreamingAsync`) receives the MAF answer as one
  fragment, counted once.
- **Options** — the bridge sends the MAF agent messages only: the options of a call's `LlmConfig`
  (model, temperature, max tokens, top-p, response format, thinking, grammar…) never reach it. Each one
  a call declares is a structured warning — `Option '<name>' was declared but agent-framework:<name>
  does not support it — it was not sent` (event 110, the HTTP providers' own) — once per crew run
  and per option, through the logger handed to `WithAgentFrameworkAgent`, else the `ILoggerFactory` the MAF
  agent exposes (`GetService`), else nowhere. A structured output expected from a MAF agent has the
  output validation and its correction round as its only guard.
- It maps the roles `system`, `assistant` and `tool`, and anything else to `user`, reports `Name` =
  `agent-framework:<name or id>`, and relays the MAF usage as the response's token counts.

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

- It does not translate tools across the boundary: an Orkeon agent's tools stay Orkeon's — refused on
  an agent a MAF agent answers for —, a MAF agent's stay MAF's.
- It does not stream tokens: `CrewAgent` yields a crew's final answer, and an agent a MAF agent
  answers for receives that answer as one fragment.
- It sends no option to a MAF agent: each one declared is a warning, never a silent drop.
- It registers nothing in the shipped runners: `orkeon`, `orkeon-host` and the REPL do not
  reference the package. An embedding host adds it — the example does so through
  `RunnerHost.Build`'s `configureServices` hook ([hosting](hosting.md)).

---

> **See also**: [ADR-010](../adr/ADR-010-agent-framework-interop.md) ·
> [Publication matrix](publication-matrix.md) · [Hosting](hosting.md) ·
> [Back to index](../INDEX.md)
