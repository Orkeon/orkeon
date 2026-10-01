> 🇫🇷 [Version française](../fr/guides/blueprint.md)

> **See also**: [ProcessTypes guide](../orchestration/process-types.md) · [Back to index](../INDEX.md)

# Blueprint — Adding an orchestration type to Orkeon

This document is a reproducible checklist for adding a seventh `ProcessType` to the framework and documenting it. It follows the path the six existing modes took — the Graph mode (a Domain engine + a YAML block) and the Autonomous mode (a dedicated `IProcessStrategy` entry point) are the two reference precedents — and covers the 8 steps from the Domain value object to the documentation.

A missed registration fails loudly rather than silently: `ProcessType.From`, the YAML parser and the scripting adapter refuse an unknown name, and both `ProcessStrategyFactory` and `SequentialCrewOrchestrator` throw `NotSupportedException` for a mode they do not route.

---

## Prerequisites

Before starting, decide:

- **Mode name**: the `ProcessType` value (PascalCase, e.g. `Swarm`) and its YAML/scripting spelling (`swarm`, matched case-insensitively)
- **Entry point**: reuse `IProcessStrategy.ExecuteSequentialAsync(crew, plan, …)` (Graph, Consensual) or add a dedicated method (Autonomous, which needs an `AgentExecutionBudget`)
- **Configuration**: none, a .NET options section (Consensual: `Orkeon:Consensus`), or a crew-level YAML block (Graph: `graphConfig`)
- **Engine**: whether the mode needs a reusable Domain engine (`StateGraph<TState>`, `StateMachine<TState, TEvent>`) or lives entirely in its strategy
- **Stability**: whether the public surface ships under an `[Experimental]` diagnostic (Autonomous: `ORKEXP002`, see [experimental APIs](../reference/experimental-apis.md))

---

## Step 1 — Domain: declare the mode

**File**: `src/core/Orkeon.Domain/SharedKernel/ValueObjects/ProcessType.cs`

```csharp
/// <summary>Agents swarm over the task pool under a shared budget.</summary>
public static readonly ProcessType Swarm = new("Swarm");

private static readonly Dictionary<string, ProcessType> s_all = new(StringComparer.OrdinalIgnoreCase)
{ /* … the six existing entries …, */ [nameof(Swarm)] = Swarm };
```

`ProcessType.All`, `From` and `TryFrom` read `s_all`: once the entry is there, YAML (`YamlCrewMapper.ParseProcessType`) and the scripting adapter (`JsCrewConfigurationAdapter`) accept the new value without further change, and their error messages list it.

**Entry point.** When the mode needs its own entry point, add it to `IProcessStrategy` (`src/core/Orkeon.Domain/Crew/IProcessStrategy.cs`), as `ExecuteAutonomousAsync` was added; every existing strategy then implements it by throwing `NotSupportedException("Use <Mode>ProcessStrategy …")`.

**Public API.** The core projects track their public surface: add each new public member to the project's `PublicAPI.Unshipped.txt` (`src/core/Orkeon.Domain/`, `src/core/Orkeon.Infrastructure/`, `src/core/Orkeon.Application/`), or the build fails (RS0016/RS0017 are errors).

**Application DTO.** The Application layer carries its own enum, `Orkeon.Application.Crew.DTOs.ProcessType` (`Crew/DTOs/CrewEnums.cs`), mapped by `CrewMapper.ToProcessTypeDomain` (`Common/Mapping/CrewMapper.cs`), which throws on a value it does not know: add the member and its mapping arm.

**Fluent Builder (optional).** `CrewBuilder.Process(ProcessType.Swarm)` works as is; a shortcut such as `.Swarm()` exists only for the historical modes (`Sequential()`, `Hierarchical(...)`, `Parallel()`, `Consensual()`).

---

## Step 2 — Domain: configuration and engine (when needed)

### Configuration DTO

**File**: `src/core/Orkeon.Domain/Configuration/<Mode>Config.cs` — an immutable `sealed record` with defaults, nullable where a value overrides a preset (`GraphConfig` is the model: `MaxRetryCycles = 2`, `CircuitBreakerPreset = "strict"`, nullable limits).

Carry it to execution time:

| Where | What to add | Graph precedent |
|-------|-------------|-----------------|
| `Configuration/CrewConfiguration.cs` | `public <Mode>Config? <Mode>Config { get; init; }` on `CrewConfiguration` (and on `TaskConfiguration`, same file, for a task-level block) | `CrewConfiguration.GraphConfig` |
| `Crew/CrewCreateOptions.cs` | The same property | `CrewCreateOptions.GraphConfig` |
| `Crew/Crew.cs` | A read-only property set from the options in `Crew.Create` | `Crew.GraphConfig` |
| `Crew/CrewBuilder.cs` | `With<Mode>Config(...)` feeding the options | `WithGraphConfig(...)` |

The strategy must read the config **off the crew argument** at execution time, never store it on the (scoped, shared) strategy instance — concurrent crews would overwrite each other's settings.

### Engine

A reusable engine goes in the Domain layer, without any external dependency (`Orkeon.Domain.Graph` for `StateGraph<TState>`, `Orkeon.Domain.Common.StateMachine` for the FSM). What the existing engines provide, and a new one should too:

- a termination guarantee — circuit breaker (`CircuitBreakerPolicy`, presets `Strict` / `Default` / `Permissive`) or budget (`AgentExecutionBudget`, same three presets);
- observability events (`OnNodeCompleted` / `OnTransition`, `OnCircuitBroken`) and a status snapshot;
- cooperative cancellation (`CancellationToken` on every async member);
- thread safety where the engine is shared (`lock`, `Interlocked`);
- `[assembly: InternalsVisibleTo]` only for types the tests must reach.

---

## Step 3 — Infrastructure: the strategy

**File**: `src/core/Orkeon.Infrastructure/Crew/Strategies/<Mode>ProcessStrategy.cs`

```csharp
public sealed partial class SwarmProcessStrategy : IProcessStrategy
{
    public SwarmProcessStrategy(
        CrewStrategyDependencies dependencies,   // tasks, agents, execution service, memory scope
        ILogger<SwarmProcessStrategy> logger,
        ICrewExecutionHook? hook = null,          // optional: the run's observer
        TaskAgentSelector? agentSelector = null)  // optional: who runs a task without agent:
    { /* … */ }

    public Task<CrewOutput> ExecuteSequentialAsync(Crew crew, ExecutionPlan plan,
        IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken ct = default)
    { /* the mode */ }

    // The entry points the mode does not serve:
    public Task<CrewOutput> ExecuteHierarchicalAsync(/* … */)
        => throw new NotSupportedException("Use HierarchicalProcessStrategy for hierarchical orchestration.");
    // … ExecuteParallelAsync, ExecuteAutonomousAsync likewise
}
```

Reuse the shared building blocks of `Crew/Strategies/` and `Crew/` — several are `internal`, which is why strategies live in `Orkeon.Infrastructure`:

| Building block | Use |
|----------------|-----|
| `CrewStrategyDependencies` | The four collaborators every strategy takes (task/agent repositories, `IAgentExecutionService`, `IMemoryScope`) |
| `CrewTaskSequencer.ResolveAsync` | Task order: the plan's, else the stable topological sort on `dependencies` |
| `TaskAgentSelector` | Agent choice: declared `agent:`, else `OrkeonApplicationOptions.AgentSelectionStrategy` |
| `CrewHookDispatcher` | `ICrewExecutionHook` calls — send the terminal event (`CrewCompletedAsync` / `CrewFailedAsync`) on **every** exit, setup and cancellation included |
| `TokenUsageTally` | Token telemetry into the `CrewOutput` metadata (never a fabricated zero) |
| `AgentDelegationToolsProvider` | Delegation tools for agents with `allowDelegation: true` (Sequential, Graph) |
| `LlmUsageScope.Begin(...)` | Meter extra LLM calls the mode makes itself (a manager, a judge) under the right operation |

Report the outcome honestly: `CrewOutput.CreateFailure(...)` when the mode's own contract failed (the runner exits non-zero on it), with the outputs and the token usage produced so far.

---

## Step 4 — Infrastructure: registration and routing

Three places, all in `Orkeon.Infrastructure`:

| File | Change |
|------|--------|
| `DependencyInjection/InfrastructureExtensions.cs` | `services.AddScoped<SwarmProcessStrategy>();` in `AddOrkeonRepositoriesAndStrategies` — or a public `AddOrkeon<Mode>()` extension (options binding, voting strategies…) called from `AddOrkeonFeatureModules`, like `AddOrkeonConsensus()` |
| `Crew/Strategies/ProcessStrategyFactory.cs` | `"Swarm" => _serviceProvider.GetRequiredService<SwarmProcessStrategy>(),` |
| `Orchestration/SequentialCrewOrchestrator.cs` | A case in `ExecuteDomainStrategyAsync` calling the chosen entry point (`"Swarm" => await processStrategy.ExecuteSequentialAsync(crew, defaultPlan, stringVariables, cancellationToken)…`) |

An options section follows the `Orkeon:*` convention (`services.AddOptions<SwarmOptions>().BindConfiguration("Orkeon:Swarm")`) and is listed in [configuration](../reference/configuration.md).

---

## Step 5 — YAML: the configuration block

`process: swarm` needs nothing more (step 1). A crew-level block follows the `graphConfig` path; a task-level block follows the task `deliverable:` path — mapped in `MapTasks`, then applied to the task by `CrewFactory`: a block read and never applied is a bug, and a crew that writes a removed key is refused at load (`RetiredCrewYamlKeys`).

**Models** — `src/core/Orkeon.Infrastructure/Configuration/Yaml/YamlConfigModels.cs`. The models carry **no** `[YamlMember]` attributes: keys resolve by convention (camelCase, with a snake_case fallback). Add a public class and the property on the models that need it:

```csharp
public class SwarmYamlConfig
{
    public int? MaxAgents { get; set; }   // maxAgents: or max_agents:
}

// On CrewYamlConfig (single-file crew) AND CrewSettingsYamlConfig (multi-file crew.yaml):
public SwarmYamlConfig? SwarmConfig { get; set; }   // key: swarmConfig
// On TaskYamlConfig for a task-level block.
```

**Mapping** — `Configuration/Yaml/YamlCrewMapper.cs`: a private `Map<Mode>Config(...)` returning the Domain DTO (or `null`), a property on `CrewMappingSettings`, and the assignment in `BuildConfiguration` (crew level) or `MapTasks` (task level).

**Loader** — `Configuration/YamlCrewDefinitionLoader.cs` fills `CrewMappingSettings` in both paths: from `CrewSettingsYamlConfig` (multi-file) and from `CrewYamlConfig` (single-file). Forgetting one path makes the block work in one layout only.

**Factory** — `Configuration/CrewFactory.cs`: `if (config.SwarmConfig is not null) builder.WithSwarmConfig(config.SwarmConfig);`, next to `WithGraphConfig`.

**Validation** — load-time rules (a value out of range, a combination the mode refuses) go in `Configuration/Yaml/CrewDefinitionValidator.cs`, so a wrong file fails at load, not mid-run.

---

## Step 6 — Scripting DSL and Studio

| File | Change |
|------|--------|
| `src/scripting/Orkeon.Scripting/Builders/JsCrewBuilder.cs` | Add the name to `AllowedProcesses` (the builder refuses any other) |
| `src/scripting/Orkeon.Scripting/Typings/crew.d.ts` | Add it to the `Process` union |
| `src/scripting/Orkeon.Scripting/Adapters/JsCrewConfigurationAdapter.cs` | Nothing for the name (`ProcessType.TryFrom`); map the mode's config when the DSL exposes it, or list it in `CollectIgnoredFeatures` |
| `src/scripting/Orkeon.Scripting.Cli/Commands/UseCases/UseCasesCommand.cs` | The `--process` help text of `orkeon usecases list` |
| `src/apps/Orkeon.Studio.Wpf/ViewModels/Teams/UseCaseGalleryViewModel.cs` | The gallery's process filter entry |
| `src/apps/Orkeon.Studio.Core/Localization/StudioStrings.cs` + `src/apps/Orkeon.Studio.Wpf/Resources/Strings*.resx` | `WizardGalleryProcess<Mode>` key, English default and the translations (fr, de, es, zh-Hans) |

The procedural script shape (`await crew.run()`) ignores the process; only the declarative shape (`globalThis.crew = crew`) runs the strategy — see [Scripting](../architecture/scripting.md).

---

## Step 7 — Tests

xUnit with native assertions and **hand-written doubles** (`Mock*` / `Fake*` / `Stub*` in a `Doubles/` folder — no mocking library).

| Test | Where | Precedent |
|------|-------|-----------|
| The value object (count of `All` — pinned at 6 today —, `From`, `TryFrom`, case-insensitivity) | `tests/core/Orkeon.Domain.Tests/ValueObjects/ProcessTypeTests.cs` | — |
| The engine, if any | `tests/core/Orkeon.Domain.Tests/<Area>/` | `Graph/StateGraphTests.cs`, `Common/StateMachine/*` |
| The strategy: happy path, empty crew, failures, cancellation, hook terminal event on every exit, token metadata, unsupported entry points | `tests/core/Orkeon.Infrastructure.Tests/Strategies/<Mode>ProcessStrategy/` | `GraphProcessStrategy/GraphProcessStrategyTests.cs` |
| Factory routing | `Strategies/ProcessStrategyFactory/ProcessStrategyFactoryTests.cs` | — |
| Orchestrator dispatch to the right entry point | `Orchestration/CovAutonomous_SequentialCrewOrchestratorTests.cs` | `KickoffAsync_Graph_DispatchesViaSequential` |
| YAML: `process:` parsing and the config block in both layouts | `Configuration/YamlCrewDefinition/YamlCrewDefinitionTests.cs` (iterates `ProcessType.All`), `Configuration/CircuitBreakerPolicyFactoryGraphTests.cs` | — |
| Application mapping | `tests/core/Orkeon.Application.Tests/DTOs/Mapping/CrewMapperTests.cs` (iterates `ProcessType.All`) | — |
| Scripting | `tests/scripting/Orkeon.Scripting.Tests/` (builder accepts the name, adapter maps it) | — |

```bash
dotnet test tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj --filter "FullyQualifiedName~ProcessType"
dotnet test tests/core/Orkeon.Infrastructure.Tests/Orkeon.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Swarm"
```

The test runner is Microsoft.Testing.Platform: a `--filter` that matches no test in a project is an error, so filter each project on names it contains.

---

## Step 8 — Example and documentation

### Example

**Folder**: `examples/<NN-category>/<NNN-name>/` with a `config.yaml` (or a `main.ork.ts`) and a `README.md` (goal, diagram, annotated YAML, how to run). Start from [102-graph-orchestration](https://github.com/orkeon/orkeon/blob/main/examples/09-experimental/102-graph-orchestration/). The YAML root is flat — no `crew:` wrapper — and `agents:` / `tasks:` are mappings keyed by id:

```yaml
name: "swarm-demo"
goal: "…"
process: swarm
swarmConfig:
  maxAgents: 5

agents:
  scout:                  # mapping keyed by agent id — never a sequence
    role: "…"
    goal: "…"

tasks:
  explore:                # the id IS the key (TaskYamlConfig has no id: field)
    description: "…"
    expectedOutput: "…"
```

A new numbered example changes the example count that the docs state and `scripts/check-doc-claims.py` verifies; list it in `examples/INDEX.md`.

### Documentation

| Page | Change |
|------|--------|
| `docs/orchestration/<mode>.md` (new) | Overview, architecture (classes per layer), execution flow, configuration and resolution, termination guarantees, observability, YAML and C# usage, example, tests — the [Graph](../orchestration/graph.md) and [Autonomous](../orchestration/autonomous.md) pages are the models |
| [`docs/orchestration/process-types.md`](../orchestration/process-types.md) | The value list, the architecture diagram, the matrix column, a dedicated section, the decision tree, the cost table |
| [`docs/architecture/yaml-schema.md`](../architecture/yaml-schema.md) | The `process:` values and the new block |
| [`docs/reference/configuration.md`](../reference/configuration.md) | A new options section, if any |
| `docs/INDEX.md`, `docs/toc.yml` | The new page |
| `CHANGELOG.md`, `CLAUDE.md` | The feature, and every "6 modes" statement |

Every page has its French mirror under `docs/fr/` (same path), updated in the same change — a CI gate checks the parity. Placeholders in prose go inside code spans (`<Mode>`), never bare. Then run:

```bash
python3 scripts/check-doc-claims.py
```

---

## Final checklist

| Criterion | Expected | Verified |
|---------|-------------------|---------|
| Declared | `ProcessType` entry, `All` count, Application DTO + `CrewMapper` arm | [ ] |
| Routed | Strategy registered, `ProcessStrategyFactory` case, `SequentialCrewOrchestrator` case | [ ] |
| Terminates | Circuit breaker, budget, or a bounded loop — and cancellation honoured | [ ] |
| Observable | Hook terminal event on every exit, token metadata, structured logs | [ ] |
| Honest outcome | `CreateFailure` when the mode's contract fails | [ ] |
| Configurable | Per-crew config read off the crew, both YAML layouts, load-time validation | [ ] |
| Scriptable | `AllowedProcesses`, `crew.d.ts`, `orkeon usecases`, Studio gallery | [ ] |
| Tested | Domain, strategy, factory, orchestrator, YAML, mapping, scripting | [ ] |
| Documented | Mode page + FR mirror, process-types, yaml-schema, INDEX/toc, CHANGELOG, `check-doc-claims.py` green | [ ] |
| Example | Working `config.yaml` with README, listed in `examples/INDEX.md` | [ ] |
