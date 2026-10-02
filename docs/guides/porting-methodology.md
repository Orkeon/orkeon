> 🇫🇷 [Version française](../fr/guides/porting-methodology.md)

> **See also**: [Full example](./porting-example.md) · [Tool inventory](../tools/inventory.md) · [Back to index](../INDEX.md)

# Porting guide — 5-step methodology

This guide details the methodology for porting an existing .NET application to an Orkeon agent-team architecture.

## Overview

Porting consists of decomposing the responsibilities of a monolithic or modular application into specialized agent roles, equipped with tools, and orchestrated by one or more Crews.

```
Source application → Responsibility analysis → Mapping to agents
→ Tool identification → Task definition → Porting plan
```

## Choosing the approach: YAML-first or Code-first

Orkeon supports two approaches for defining a crew. The choice impacts the porting workflow.

| Criterion | YAML-first | Code-first (Fluent Builder) |
|---------|-----------|---------------------------|
| Modification without recompilation | Yes — editing the YAML is enough | No — recompilation needed |
| Type safety | Runtime validation (CrewFactory) | Compile-time validation |
| Iterative prompt engineering | Fast — edit descriptions/backstories | Slower |
| Custom tools with dependencies | Requires separate DI registration | Direct instantiation possible |
| Sharing configurations | Portable YAML file | C# code to integrate |
| Reference examples | 100+ YAML examples in `examples/` (see the generated `examples/INDEX.md`) | Builders documented in the docs |

**Recommendation**: favor the **YAML-first** approach for porting. YAML lets you iterate quickly on the prompts (descriptions, backstories) without touching the code. Custom tools remain in C# and are registered via DI.

### YAML-first workflow

```
1. Write config.yaml (agents, tasks, process type — see the YAML schema)
2. Identify the missing tools
3. Code the custom tools (ToolBase<TReq, TRes>)
4. Register via DI (AddSingleton<IBaseTool, MyTool>()) — the default tool
   registry of AddOrkeonInfrastructure() reads it, so YAML names resolve
5. Load and run — the YAML path is a virtual path, read through the VFS:
   var crew = await crewFactory.CreateFromFileAsync("/crews/config.yaml");
   var output = await orchestrator.KickoffAsync(crew.Id, input);
6. Iterate on the prompts in the YAML
```

While the crew only uses built-in tools, no host code is needed at all: `orkeon run config.yaml --validate` loads it with strict tool resolution (no LLM call), and `orkeon run config.yaml` runs it — the runner reads the `Llm` section, mounts the folders and registers the DI-backed tool registry itself (see the [CLI reference](../reference/cli.md#orkeon-run)). The host of section 5.5 becomes necessary once custom C# tools enter the port. The YAML keys are listed in the [YAML schema](../architecture/yaml-schema.md).

## Step 1 — Analyze the source application's responsibilities

### 1.1 Inventory the components

List all the application's services, controllers, handlers and modules. For each component, note its main responsibility, its incoming and outgoing dependencies, and the type of data it handles.

### 1.2 Identify the data flows

Trace the journey of the data through the application: where it comes from (files, API, database), which transformations it undergoes, where it ends up (storage, API, UI).

### 1.3 Classify the responsibilities

Categorize each responsibility according to this grid:

| Category | Description | Examples |
|-----------|-------------|----------|
| **Collection** | Acquiring data from external sources | API calls, file reads, web scraping, DB queries |
| **Analysis** | Processing, transforming, enriching data | Parsing, computations, aggregations, pattern detection |
| **Decision** | Business logic, rules, conditional routing | Validation, scoring, classification, prioritization |
| **Production** | Generating output artifacts | Reports, emails, files, API responses |
| **Coordination** | Orchestrating sub-processes | Workflow management, sequencing, parallelization |

### 1.4 Identify human interactions

Spot the points where the application requires human intervention (validation, input, approval). These points will become tasks with `HumanInput = true` in Orkeon (`humanInput: true` in YAML). The flag hands the agent the `human_input` tool for that task, so the host must register one: `orkeon run` surfaces the question on its run event bus, and a hand-built host calls `AddOrkeonHumanInput<TProvider>()` with its own `IHumanInputProvider` (the parameterless `AddOrkeonHumanInput()` auto-approves). Without a registered `human_input` tool the flag is a no-op.

## Step 2 — Map the responsibilities to agent roles

### 2.1 Decomposition principles

A good Orkeon agent follows these principles:

- **Single responsibility**: each agent has a clear, specialized role (defined by `AgentRole`)
- **Measurable objective**: the goal (`AgentGoal`) describes a concrete, verifiable result
- **Autonomy**: the agent must be able to accomplish its tasks with its tools without depending on another agent for every operation
- **Appropriate granularity**: neither too broad (a "does everything" agent) nor too narrow (an agent that performs a single trivial operation)

### 2.2 Mapping template

For each responsibility identified in Step 1, fill in this template:

```
Source responsibility  : [description]
→ Agent role          : [AgentRole — concise specialist name]
→ Agent goal          : [AgentGoal — expected result]
→ Backstory           : [AgentBackstory — context and expertise]
→ Required tools      : [list of tools]
→ Constraints         : [MaxIterations, MaxRpm, AllowDelegation]
```

### 2.3 Common mapping patterns

| Source pattern | Orkeon mapping |
|---------------|----------------|
| Service that reads and transforms data | "Data Analyst" agent with file/DB tools |
| Service that calls external APIs | "API Integrator" agent with `HttpApiTool` |
| Service that generates reports | "Report Writer" agent with `FileWriteTool` |
| Controller that orchestrates a workflow | Crew with `ProcessType.Sequential` |
| Validation / review service | "Quality Reviewer" agent with JSON output validation |
| Scheduler / batch processor | Crew with `KickoffForEachAsync` (batch) |
| Service that routes work to specialists at runtime | `Hierarchical` crew with `LlmBasedManager` |
| Service with explicit branching or retry loops | `Graph` crew (`StateGraph<TState>`, conditional edges, controlled cycles) |
| Open-ended job whose steps are not known in advance | `Autonomous` crew (a manager LLM assigns each task; a failed task goes to a peer agent within a crew-wide delegation depth; extra agents only through a host-registered `SpawnAgentTool`; all bounded by `AgentExecutionBudget`) |

### 2.4 When to create an agent vs. a tool

| Create an **agent** when... | Create a **tool** when... |
|----------------------------|---------------------------|
| The responsibility requires reasoning, analysis or creativity | The operation is deterministic and mechanical |
| The result varies with the context and the data | The operation always follows the same algorithm |
| Several reflection steps are needed | It is an atomic operation (input → output) |
| Interacting with an LLM adds value | An LLM would add nothing over an algorithm |

## Step 3 — Identify the required tools

### 3.1 Inventory the tool needs

For each agent defined in Step 2, list the concrete operations it must perform. For each operation, check whether an existing tool covers it (see [Tool inventory](../tools/inventory.md)).

### 3.2 Decision matrix: reuse vs. create

| Criterion | Reuse the existing | Create a new tool |
|---------|----------------------|----------------------|
| The operation is covered by an Orkeon tool | Yes | — |
| The existing operation is almost suitable but lacks a parameter | Consider a contribution/extension | — |
| The operation requires a call to a specific business API | — | Yes — inherit from `HttpToolBase<>` |
| The operation handles an unsupported file format | — | Yes — inherit from `FileToolBase<>` |
| The operation is a pure business computation | — | Yes — inherit from `ToolBase<>` |

### 3.3 Existing tools by common need

The YAML name is what a crew lists under an agent's `tools:`.

| Need | Existing tool | YAML name | Package / registration |
|--------|---------------|-----------|---------|
| Read a text/JSON/XML file | `FileReadTool` | `file_read` | `Orkeon.Tools.FileSystem` — `AddOrkeonFileSystemTools()` |
| Write a file | `FileWriteTool` | `file_write` | `Orkeon.Tools.FileSystem` — `AddOrkeonFileSystemTools()` |
| List a directory | `DirectoryReadTool` | `directory_read` | `Orkeon.Tools.FileSystem` — `AddOrkeonFileSystemTools()` |
| Search within files | `DirectorySearchTool` | `directory_search` | `Orkeon.Tools.FileSystem` — `AddOrkeonFileSystemTools()` |
| Read a CSV | `CsvReaderTool` | `csv_reader` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| Read a PDF | `PdfReaderTool` | `pdf_reader` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| Read a DOCX | `DocxReadTool` | `docx_reader` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| Read / write an Excel (.xlsx) | `XlsxReadTool` / `XlsxWriteTool` | `xlsx_reader` / `xlsx_writer` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| Manipulate JSON | `JsonTool` | `json_tool` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| SQL query | `RelationalDatabaseTool` | `relational_database_query` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| MongoDB query | `MongoDbTool` | `mongodb_query` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| Web search | `WebSearchTool` / `BraveSearchTool` | `web_search` / `brave_search` | `Orkeon.Tools.Web` — opt-in `AddOrkeonWebSearchTool()` / `AddOrkeonBraveSearchTool(apiKey)` |
| Web scraping | `WebScrapeTool` | `web_scrape` | `Orkeon.Tools.Web` — `AddOrkeonWebTools()` |
| REST API call | `HttpApiTool` | `http_api` | `Orkeon.Tools.Web` — `AddOrkeonWebTools()` |
| Run a shell command | `ShellCommandTool` | `shell_command` | `Orkeon.Tools.Code` — `AddOrkeonCodeTools()` |
| Execute C# code | `SecureCodeInterpreterTool` | `code_interpreter` | `Orkeon.Infrastructure` — registered as a concrete type only: add `services.AddSingleton<IBaseTool>(sp => sp.GetRequiredService<SecureCodeInterpreterTool>())` for a YAML crew to resolve it by name |
| Read, draft or send e-mail | the 13 `email_*` tools | `email_read`, `email_draft`, `email_send`, … | `Orkeon.Tools.Email` — `AddOrkeonEmailTools(configuration)` (see [E-mail tools](./email.md)) |
| Ask a colleague | `AskQuestionTool` | `ask_question_to_coworker` | `Orkeon.Infrastructure` — given per agent when `AllowDelegation` is on |
| Delegate a task | `DelegateWorkTool` | `delegate_work_to_coworker` | `Orkeon.Infrastructure` — given per agent when `AllowDelegation` is on |
| Semantic search | `SearchTool` | `semantic_search` | `Orkeon.Infrastructure` — opt-in `AddSemanticSearchTool()` (`Orkeon.Hosting`; `orkeon run` calls it) |
| RAG over documents | `RagSearchTool` | `rag_search` | `Orkeon.Tools.Rag` (opt-in: `AddOrkeonRag` + `AddOrkeonRagTools`) |

## Step 4 — Define the tasks and the orchestration flow

### 4.1 Decompose into tasks

Each expected output of the crew becomes a `CrewTask`. A task is defined by its `TaskDescription` (what the agent must do) and its `ExpectedOutput` (format and content of the expected result).

### 4.2 Choose the ProcessType

| Situation | Recommended ProcessType |
|-----------|----------------------|
| The steps must chain, each output feeding the next input | `Sequential` |
| A manager must dynamically route tasks to the most competent agents | `Hierarchical` |
| Several independent tasks can run simultaneously | `Parallel` |
| The agents must vote and reach a consensus | `Consensual` |
| The flow branches on conditions or loops until a check passes | `Graph` |
| A manager LLM routes each task, a failed task gets a second chance with a peer, all within a budget | `Autonomous` |

In YAML the mode is the crew's `process:` key (`sequential`, `hierarchical`, `parallel`, `consensual`, `graph`, `autonomous`); with the builder, `.Sequential()`, `.Hierarchical()`, `.Parallel()`, `.Consensual()` or `.Process(ProcessType.Graph)` / `.Process(ProcessType.Autonomous)`. See the [Graph](../orchestration/graph.md) and [Autonomous](../orchestration/autonomous.md) orchestration guides for the last two.

### 4.3 Define the dependencies

Use `CrewTaskBuilder.DependsOn()` (YAML: `dependencies:`) to express prerequisites between tasks. In `Sequential` mode the declaration order is enough, and declared dependencies reorder it when they disagree. In `Parallel` mode the dependencies split the tasks into waves: the tasks whose prerequisites are met run together, and the next wave starts when they are done, with their outputs in context.

### 4.4 Configure the execution options

For each task, decide on: the priority (`TaskPriority`), human intervention (`HumanInput`, see 1.4), the output validation schema (`OutputJson`), and the file the framework writes from the result (the YAML `deliverable:` block — `path`, `format`, optional schema). `AsyncExecution` (`asyncExecution:`) is recorded but honoured by no mode yet: use `process: parallel` for concurrency.

## Step 5 — Produce the porting plan

### 5.1 Plan template

| Source component | Responsibility | Target agent | Role | Required tool(s) | Status | Effort |
|-----------------|----------------|-------------|------|-----------------|--------|--------|
| `OrderService` | Order validation | Order Validator | "Order Validation Specialist" | `RelationalDatabaseTool`, custom tool `ValidateOrderTool` | To create (custom tool) | M |
| `PricingEngine` | Price computation | Pricing Analyst | "Pricing Specialist" | `CsvReaderTool`, `JsonTool` | Ready (existing tools) | S |
| ... | ... | ... | ... | ... | ... | ... |

### 5.2 Effort legend

| Code | Meaning | Estimated duration |
|------|--------------|---------------|
| **XS** | Direct mapping to an existing tool, no code | < 1h |
| **S** | Simple agent with existing tools | 1-4h |
| **M** | Agent + 1 simple custom tool | 0.5-1 day |
| **L** | Agent + complex custom tool or external integration | 1-3 days |
| **XL** | Significant refactoring, multiple custom tools | > 3 days |

### 5.3 Status columns

| Status | Meaning |
|--------|--------------|
| Ready | All tools exist, configuration only |
| To create (tool) | One or more custom tools must be developed |
| To create (agent) | The agent requires specific backstory/prompt engineering |
| Blocked | Unresolved external dependency |

### 5.4 Plan validation checklist

Before starting the implementation, verify that:

- Every responsibility of the source application is covered by at least one agent
- Every agent has at least one assigned task
- All dependencies between tasks are explicit
- Missing tools are identified with an estimated effort
- The `ProcessType` is justified by the nature of the workflow
- Human intervention points are identified (`HumanInput = true`)
- Memory is enabled if the crew should remember its earlier runs (`EnableMemory(true)`, `memory: true`) — not for the context between the tasks of one run, which the previous outputs already carry into each prompt
- The rate-limiting constraints (`MaxRpm`) are compatible with the external APIs used

### 5.5 Minimal DI setup for the port

A port that brings its own C# tools runs in its own host. Every such host needs this dependency-injection setup:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.DependencyInjection;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Hosting;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Tools.Code.DependencyInjection;
using Orkeon.Tools.Data.DependencyInjection;
using Orkeon.Tools.FileSystem.DependencyInjection;
using Orkeon.Tools.Web.DependencyInjection;

var host = Host.CreateDefaultBuilder(args)
    // Same environment overlay as the runners: ORKEON_Llm__ApiKey → Llm:ApiKey
    .ConfigureAppConfiguration(config => config.AddEnvironmentVariables("ORKEON_"))
    .ConfigureServices((context, services) =>
    {
        // Required — Application and Infrastructure layers
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure(context.Configuration);

        // Required — the virtual file system every tool and the YAML loader go through
        // (mounts declared under Orkeon:FileSystem:Mounts, e.g. "/srv/app/crews:/crews:ro")
        services.AddOrkeonFileSystem(context.Configuration);

        // Tool suites — add only the ones you need
        services.AddOrkeonFileSystemTools();   // If agents read/write files
        services.AddOrkeonDataTools();         // If agents handle CSV, PDF, JSON, SQL, MongoDB
        services.AddOrkeonWebTools();          // If agents scrape pages or call HTTP APIs
        services.AddOrkeonCodeTools();         // If agents execute shell commands

        // Custom tools identified in Step 3
        services.AddSingleton<IBaseTool, MyCustomTool1>();
        services.AddSingleton<IBaseTool, MyCustomTool2>();

        // YAML tool names resolve against these IBaseTool registrations: the default
        // ToolRegistry of AddOrkeonInfrastructure() reads them — nothing more to register.

        // LLM — a hand-built host reads no Llm section by itself: build the provider here.
        // Use the provider class of your vendor (OpenAIProvider, AnthropicLlmProvider, ...).
        var llm = LlmConfig.Create(
            context.Configuration["Llm:Model"] ?? "gpt-5.6-sol",
            context.Configuration["Llm:ApiKey"]);
        services.AddOrkeonLlmProvider(
            sp => new OpenAIProvider(llm,
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<OpenAIProvider>>()),
            llm);
    })
    .Build();
```

`AddOrkeonLlmProvider` comes **after** `AddOrkeonInfrastructure`: it registers the provider on the three surfaces the runtime consumes (`ILlmProvider`, `IBasicLlmProvider`, `IChatClient`), metered, and the last registration wins. A mount whose folder lies outside the working directory must also be listed under `PathSecurity:AdditionalAllowedDirectories`. `orkeon run` does all of the above from the settings file (see [Configuration](../reference/configuration.md)).

### 5.6 Execution pattern

```csharp
// ICrewFactory is scoped: resolve it from a scope
using var scope = host.Services.CreateScope();
var services = scope.ServiceProvider;

// Load the crew from the YAML (a virtual path, under a declared mount)
var crewFactory = services.GetRequiredService<ICrewFactory>();
var crew = await crewFactory.CreateFromFileAsync("/crews/config.yaml");

// Prepare the input with execution variables (task templates {date}, {environment})
var input = CrewInput.WithStringVariables(
    "Initial context for the execution",
    new Dictionary<string, string>
    {
        ["date"] = DateTime.Today.ToString("yyyy-MM-dd"),
        ["environment"] = "production"
    });

// Run and retrieve the results — KickoffAsync never throws: a failure comes back
// as Succeeded = false with its reason in Error
var orchestrator = services.GetRequiredService<ICrewOrchestrationService>();
var output = await orchestrator.KickoffAsync(crew.Id, input);

// Use the results
if (output.Succeeded)
{
    // Success — process the final result
    Console.WriteLine(output.FinalOutput);
}
else
{
    // Failure — the reason, then the failed tasks
    Console.Error.WriteLine($"Crew failed: {output.Error}");
    foreach (var failed in output.TaskOutputs.Where(t => !t.Success))
        Console.Error.WriteLine($"Task {failed.TaskId} failed: {failed.RawOutput}");
}
```
