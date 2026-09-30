> 🇫🇷 [Version française](../fr/orchestration/flows.md)

> **See also**: [ProcessTypes comparison guide](./process-types.md) · [Graph orchestration](./graph.md) · [Back to index](../INDEX.md)

# Flows (FlowEngine)

## Overview

A **flow** is a list of typed steps — run a crew, call an LLM, call a tool, branch on a condition, ask a human, wait — executed by the `FlowEngine` over a shared key/value state (`FlowState`). Where a crew hands **tasks** to **agents** under one of the six [ProcessTypes](./process-types.md), a flow chains heterogeneous **steps**, one of which may be a whole crew.

| | Crew (`ProcessType`) | Flow (`FlowType`) |
|---|---|---|
| Unit of work | A task executed by an agent | A step (`crew`, `llm`, `tool`, `conditional`, `human_input`, `delay`) |
| Shared context | Previous task outputs | `FlowState` keys, merged after each step |
| Modes | Sequential, Hierarchical, Parallel, Consensual, Graph, Autonomous | Sequential, Parallel, Conditional, Loop (Crew and Custom run as Sequential) |
| Defined in | Crew YAML, Fluent Builder, `.ork.ts` | Flow YAML (`YamlFlowDefinitionLoader`) or `FlowDefinitionBuilder` in C# |
| Run by | `orkeon run`, `orkeon-host`, Studio, `ICrewOrchestrationService` | **C# only** — `IFlowEngine` |

> **What runs today.** The flow engine is a C# API. **No CLI, host or scripting entry point runs a flow**: `orkeon run` treats every YAML file as a crew definition (a flow file fails crew validation), and the scripting DSL has no flow binding. The examples that mention `FlowEngine` in their comments ([26](https://github.com/orkeon/orkeon/blob/main/examples/02-science-research/26-knowledge-graph/), [29](https://github.com/orkeon/orkeon/blob/main/examples/02-science-research/29-hypothesis-generation/), [59](https://github.com/orkeon/orkeon/blob/main/examples/05-education/59-nonlinear-learning-path/), [61](https://github.com/orkeon/orkeon/blob/main/examples/05-education/61-gamified-learning/), [76](https://github.com/orkeon/orkeon/blob/main/examples/07-creative-media/76-narrative-studio/), [83](https://github.com/orkeon/orkeon/blob/main/examples/07-creative-media/83-interactive-fiction/), [90](https://github.com/orkeon/orkeon/blob/main/examples/08-iot-smart-systems/90-energy-management/), [100](https://github.com/orkeon/orkeon/blob/main/examples/09-experimental/100-civilization-simulator/)) are `process: "sequential"` crews: they run under `orkeon run` as crews, and the flow part is a planned feature (marked `TODO` in their `config.yaml`). No shipped example contains a flow definition.

## Architecture

### Domain layer — `Orkeon.Domain.Flows`

| Type | Role |
|------|------|
| `IFlow` | An executable flow: `Id`, `Name`, `State`, `ExecuteAsync()` → `FlowResult` (`Success`, `Output`, `Error`, `OutputState`) |
| `IFlowDefinition` | The declarative structure: `Name`, `Description`, `Type`, `Steps`, `Configuration`, `Validate(out errors)` |
| `IFlowStep` | One executable step: `Name`, `Description`, `ExecuteAsync(FlowState)` → `FlowStepResult` (`Success`, `Output`, `Error`, `UpdatedContext`, `NextStep`) |
| `FlowStep` | A step definition: `Id`, `Name`, `Type`, `Parameters` (`FlowStepParameters`), `Dependencies`, `Timeout`, `CanRetry`, `MaxRetries` (3) |
| `FlowType` | `Sequential`, `Parallel`, `Conditional`, `Loop`, `Crew`, `Custom` |
| `FlowConfiguration` | `Name`, `Type`, `Settings` (free key/value), `Timeout`, `MaxRetries` (3), `EnableLogging`, `EnableMetrics` |
| `FlowState` | Immutable key/value state (`Get<T>`, `Set`, `Merge`, `SetVariable` → `variables.<key>`, `SetSharedState` → `shared.<key>`, snapshots) |
| `FlowExecutionResult` | One entry of the engine's history (`FlowId`, `Success`, `Output`, `Error`, `Duration`, `OutputState`, `StartedAt`, `CompletedAt`) |

The same namespace also declares attribute-based flow markers (`FlowAttribute`, `FlowStepAttribute`, `StartAttribute`, `ListenAttribute`, `RouterAttribute`, `FlowBeforeAttribute`, `FlowAfterAttribute`, `FlowValidatorAttribute`, `FlowErrorHandlerAttribute`) and event types (`FlowStepStartedEventArgs`, `FlowStepCompletedEventArgs`, `EventDrivenFlowContext`). **Nothing consumes them yet**: a class decorated with them is not discovered or executed.

### Application layer

| Type | Role |
|------|------|
| `IFlowEngine` (`Orkeon.Application.Interfaces.Ports`) | `ExecuteFlowAsync(IFlow)`, `ExecuteStepAsync(IFlowStep, context)`, `GetRegisteredFlows()`, `GetExecutionHistory(limit)`, `ValidateFlow(definition)` → `FlowValidationResult`, `GetFlowMetrics(flowName)` → `FlowMetrics` |
| `IFlowStepExecutor` (`Orkeon.Application.Interfaces`) | `ResolveStep(FlowStep)` → the executable `IFlowStep` for a step type |
| `FlowDefinitionBuilder` / `FlowStepBuilder` (`Orkeon.Application.Flow`) | Fluent construction of an `IFlowDefinition` |

### Infrastructure layer — `Orkeon.Infrastructure.Flows`

| Type | Role |
|------|------|
| `FlowEngine` | The `IFlowEngine` implementation; also `RegisterFlow(definition)` and `ExecuteDefinitionAsync(definition, initialState)` (class members, not on the interface) |
| `DefinitionBasedFlow` | The `IFlow` that executes an `IFlowDefinition` according to its `FlowType`, with per-step retry and timeout |
| `FlowStepExecutor` | The `IFlowStepExecutor` mapping the six step types to their classes |
| `InMemoryFlowDefinition` | Mutable `IFlowDefinition` (what the YAML loader produces), with structural validation |
| `YamlFlowDefinitionLoader` | Parses a flow YAML file (VFS path) or string into an `IFlowDefinition` |
| `FlowStepBase<TInput, TOutput>` (`Flows.Base`) | Typed base class for steps: the `FlowState` is deserialized into `TInput`, validated, executed, and `TOutput` is serialized (snake_case keys) into the updated state |
| `Steps/*FlowStep` | The six built-in steps |
| `Visualization/*` | `FlowGraphSerializer`, `FlowExecutionTracker` |

## Registration

`AddOrkeonInfrastructure()` calls both extensions; call them yourself only in a host that does not use it.

| Extension | Registers |
|-----------|-----------|
| `AddOrkeonFlows()` | `IFlowStepExecutor` → `FlowStepExecutor`, `IFlowEngine` → `FlowEngine`, `YamlFlowDefinitionLoader` (singletons, `TryAdd`) |
| `AddOrkeonFlowVisualization()` (`FlowVisualizationExtensions`) | `FlowExecutionTracker` (singleton). `FlowGraphSerializer` is static and needs no registration |

What the steps resolve at run time: `crew` needs `ICrewOrchestrationService`, `llm` needs an `IChatClient` (registered by the runner hosts, not by `AddOrkeonInfrastructure()` alone), `tool` needs `IToolRegistry`, `human_input` needs `IHumanInputProvider` (default: `AutoApproveHumanInputProvider`, which answers without asking anyone). They are resolved from the root service provider the engine was built with.

## Flow types

`DefinitionBasedFlow` runs the definition's steps according to `FlowType`:

| Type | Execution |
|------|-----------|
| `Sequential` (default; also `Crew`, `Custom` and any unknown value) | Steps in declaration order. A step result carrying `NextStep` (a step name or id) jumps to that step — backwards too, with no iteration bound |
| `Parallel` | Steps grouped by their `dependencies` (topological sort); each group runs concurrently, groups run in order. A dependency cycle fails the flow |
| `Conditional` | Steps in order; a step whose `condition` parameter names a state key is skipped when that key is false, empty or absent. When a step returns `NextStep`, that step is executed right after it — and again in its own turn, unless its `condition` skips it |
| `Loop` | All the steps, repeatedly, until the state key `_exit_loop` is `true` or `"true"`, at most `settings.max_iterations` times (default: the flow's `MaxRetries`, 3). The current iteration is in `iteration_count` |

In every mode, the first step that fails (after its retries) fails the flow; the flow's `Output` is the last step's output and `OutputState` the final state. Cancellation returns a failed `FlowResult` ("Flow execution was cancelled.").

**Retries and timeouts** (per step): a failed step is retried up to `MaxRetries` times (3 by default, 0 when `CanRetry` is false) with exponential backoff (1 s, 2 s, 4 s…); `Timeout` cancels one attempt ("Step … timed out." when the last attempt times out). The flow-level `Timeout` is carried by `FlowConfiguration` but not enforced by `DefinitionBasedFlow`.

**State merge**: after each step, the state receives `<step name>.output` (the step's `Output` object) and the keys of its `UpdatedContext`. For the typed steps the useful values are the flat keys of their output (`crew_output`, `llm_response`, `tool_output`, `human_input`, …) — the `.output` entry holds the output record, whose text form is its type name. Two steps of the same type write the same flat key: the later one overwrites it.

## The six step types

`FlowStepExecutor.ResolveStep` maps the step's `type` (case-insensitive) to a class; any other value throws `InvalidOperationException` ("Unknown step type …"). Parameters come from the step's `parameters`; where a parameter is absent, the typed steps read the same key from the flow state.

| `type` | Class | Parameters | Writes to the state |
|--------|-------|------------|---------------------|
| `crew` | `CrewFlowStep` | `crew_config` (required) | `crew_output`, `crew_duration` (seconds) |
| `llm` | `LlmFlowStep` | `prompt_template` (required), `system_prompt` | `llm_response` |
| `tool` | `ToolFlowStep` | `tool_name` (required), `input` | `tool_output` |
| `conditional` | `ConditionalFlowStep` | `condition_key` (required), `true_step`, `false_step` | `condition_result` (+ `NextStep`) |
| `human_input` | `HumanInputFlowStep` | `prompt` (default: "Please provide input for step '…':") | `human_input` |
| `delay` | `DelayFlowStep` | `delay_ms`, else `delay_seconds` (default 1 s) | `message` |

Details per step:

- **`crew`** — runs `ICrewOrchestrationService.KickoffAsync` on the crew whose id `crew_config` holds, **as a GUID** (`crew.Id.ToGuid()`); any other value (a crew name, the ULID string) is replaced by a random id, and the kickoff fails with "crew not found". The crew must already exist in the repository the root provider sees (the in-memory repositories are scoped). The value of `crew_config` is also passed as the crew's initial context. The step does not check `CrewOutput.Succeeded`: a failed crew yields a successful step whose `crew_output` is the failure message.
- **`llm`** — replaces each `{key}` of `prompt_template` (dotted keys allowed, e.g. `{variables.topic}`) with the state value of that key, unresolved placeholders stay as they are; sends the prompt (and the optional system prompt) to `IChatClient`; the call is metered under the `flow` operation.
- **`tool`** — resolves `tool_name` in `IToolRegistry` and calls it with `input` (a string, default empty); a missing tool or a failed tool result fails the step.
- **`conditional`** — reads the state value of `condition_key`: `null` → false, a bool as is, a string is true unless empty or `"false"`, a number is true unless 0, anything else is true; returns `true_step` or `false_step` as `NextStep`.
- **`human_input`** — asks `IHumanInputProvider.GetInputAsync` with a text-input context and stores the answer.
- **`delay`** — waits, cancellably.

### Custom steps

Implement `IFlowStep`, or derive `FlowStepBase<TInput, TOutput>` and override `ExecuteTypedAsync` (and `ValidateTypedRequest`) — the state-to-input and output-to-state conversions are done for you, and any exception becomes a failed step. `FlowStepExecutor` only knows the six built-in types, and `FlowEngine.ExecuteDefinitionAsync` builds a `FlowStepExecutor` itself; to run a custom type, implement `IFlowStepExecutor` (delegating the built-in types to `FlowStepExecutor`), wrap the definition in `new DefinitionBasedFlow(definition, yourExecutor, logger)` and pass it to `IFlowEngine.ExecuteFlowAsync`. A single step can also be run alone with `IFlowEngine.ExecuteStepAsync(step, context)`.

## Flow YAML

`YamlFlowDefinitionLoader.LoadFromFileAsync(virtualPath)` reads the file through the VFS; `LoadFromString(yaml)` parses a string. The root is flat:

```yaml
name: research_pipeline            # required (validation)
description: "Research and summarize"
type: sequential                   # sequential (default) | parallel | conditional | loop | crew | custom
settings:                          # free map, copied into FlowConfiguration.Settings
  max_retries: 2                   # FlowConfiguration.MaxRetries (loop: default iteration cap)
  max_iterations: 5                # loop flows: iteration cap
  timeout_seconds: 300             # FlowConfiguration.Timeout (carried, not enforced)
steps:                             # a SEQUENCE, in execution order
  - name: research                 # unique; referenced by dependencies, true_step, false_step
    type: crew
    parameters:
      crew_config: "3f2c9d1e-0b7a-4c55-9e0f-2a6b1c8d4e77"   # the crew id as a GUID
    timeout_seconds: 600           # per attempt
    max_retries: 1                 # per step (default 3)
  - name: summarize
    type: llm
    dependencies: [research]       # step names — used by parallel flows
    parameters:
      prompt_template: "Summarize for a manager: {crew_output}"
      system_prompt: "You write concise executive summaries."
```

- Unknown `type` values fall back to `sequential`; an unknown step `type` is only detected when the step is resolved (or as a warning by `IFlowEngine.ValidateFlow`).
- `dependencies` name other steps; a name that matches no step is dropped silently.
- Write the keys in snake_case as above: `settings` and `parameters` are plain maps whose keys the engine looks up verbatim.
- The loader does not validate: call `definition.Validate(out var errors)` or `IFlowEngine.ValidateFlow(definition)` — a name, at least one step, a name and a type per step, unique ids, known dependencies, no dependency cycle (the engine adds a warning per unknown step type).

## Running a flow

```csharp
using Orkeon.Application.Interfaces.Ports;   // IFlowEngine
using Orkeon.Infrastructure.Flows;           // FlowEngine, YamlFlowDefinitionLoader
using Orkeon.Domain.Flows.ValueObjects;      // FlowState

var loader = serviceProvider.GetRequiredService<YamlFlowDefinitionLoader>();
var definition = await loader.LoadFromFileAsync("/workspace/flows/research.yaml");

var engine = serviceProvider.GetRequiredService<IFlowEngine>();
var validation = engine.ValidateFlow(definition);
if (!validation.IsValid)
    throw new InvalidOperationException(string.Join("; ", validation.Errors));

// ExecuteDefinitionAsync lives on the FlowEngine class (not on IFlowEngine)
var result = await ((FlowEngine)engine).ExecuteDefinitionAsync(
    definition,
    FlowState.Empty.Set("variables.topic", "EU battery market"));

Console.WriteLine(result.Success ? result.Output : result.Error);
Console.WriteLine(result.OutputState.Get<string>("llm_response"));
```

The same definition in C#:

```csharp
using Orkeon.Application.Flow;

var definition = new FlowDefinitionBuilder()
    .WithName("research_pipeline")
    .AsSequential()                       // AsParallel(), AsConditional(), AsLoop()
    .WithMaxRetries(2)
    .AddCrewStep("research", crew.Id.ToGuid().ToString())
    .AddLlmStep("summarize", "Summarize for a manager: {crew_output}",
        step => step.DependsOn("research").WithParameter("system_prompt", "You write concise executive summaries."))
    .Build();                             // throws when the name or the steps are missing
```

`AddStep(name, type, configure)` adds any step type; `FlowStepBuilder` offers `DependsOn(...)`, `WithTimeout(...)`, `WithMaxRetries(...)`, `WithParameter(key, value)`.

## Observability

- **Execution history**: every `ExecuteFlowAsync` appends a `FlowExecutionResult` to an in-memory history (`GetExecutionHistory(limit)`, most recent first). It is unbounded and lost at restart.
- **Metrics**: `GetFlowMetrics(flowName)` returns totals, success rate, average duration, last execution and per-step counts. The filter compares its argument with the recorded **flow id**, not the flow name; the step counts only cover steps run through `ExecuteStepAsync`.
- **Registry**: `FlowEngine.RegisterFlow(definition)` stores a definition by name, `GetRegisteredFlows()` lists them; nothing executes a registered flow by name.
- **Logs**: flow start/end with duration, per-step retries and timeouts (`LoggerMessage`).
- **Flows are not crew runs**: they emit no `ICrewExecutionHook` events and no `orkeon run --events` stream — only the crews started by `crew` steps do.

### Visualization

- `FlowGraphSerializer.Serialize(definition)` → `FlowGraph` (`FlowName`, `Nodes` = steps with id, name, type, `Edges` = dependencies); `FlowGraphSerializer.ExportToMermaid(graph)` → a Mermaid `graph TD` (conditional steps as diamonds, crew steps as subroutines).
- `FlowExecutionTracker` holds per-flow step states (`StartTracking`, `UpdateStepState`, `CompleteTracking`, `GetExecutionState` → `FlowExecutionState`; `StepState`: `Pending`, `Running`, `Completed`, `Failed`, `Skipped`). **The engine does not feed it**: your code calls it around the steps it wants to show.

```csharp
var mermaid = FlowGraphSerializer.ExportToMermaid(FlowGraphSerializer.Serialize(definition));
```

## Limitations

- No runner, host, Studio or scripting surface runs a flow (see [Overview](#overview)); the flow examples are sequential crews with a `TODO`.
- `crew` steps need a GUID crew id and a crew visible to the root provider, and report a failed crew as a successful step.
- A `Sequential` flow whose `conditional` step jumps backwards has no iteration bound; bound cycles with a `Loop` flow.
- The flow-level `Timeout` is not enforced; history and metrics are in memory; `GetFlowMetrics` filters by id.
- The attribute-based markers and event types of `Orkeon.Domain.Flows` are not consumed.
- The YAML loader has no test against a real YAML file (its test uses a stub serializer).

## Tests

| Test file | Coverage |
|-----------|----------|
| `tests/core/Orkeon.Infrastructure.Tests/Flows/FlowEngine/FlowEngineTests.cs` (58) | The four flow types, retry backoff and timeouts, topological sort, the six steps, `FlowStepExecutor`, engine history/validation/metrics, `InMemoryFlowDefinition` validation, template resolution, `AddOrkeonFlows` registration, the YAML loader mapping |
| `tests/core/Orkeon.Infrastructure.Tests/Flows/Steps/CrewFlowStepTests.cs` (5) | Crew step |
| `tests/core/Orkeon.Infrastructure.Tests/Flows/Base/FlowStepBase/FlowStepBaseTests.cs` (10) | The typed step pipeline |
| `tests/core/Orkeon.Infrastructure.Tests/Flows/Visualization/*` (19) | `FlowGraphSerializer`, `FlowExecutionTracker` |
| `tests/core/Orkeon.Application.Tests/Services/FlowDefinitionBuilderTests.cs` (19) | The fluent builder |
| `tests/core/Orkeon.Domain.Tests/Flows/*`, `ValueObjects/Flow*Tests.cs` | Flow types, attributes, events, state, parameters |

```bash
dotnet test tests/core/Orkeon.Infrastructure.Tests/Orkeon.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Flows"
```
