> 🇫🇷 [Version française](../fr/guides/porting-methodology.md)

> **See also**: [Full example](./porting-example.md) · [Tool inventory](../tools/inventory.md) · [Back to index](../INDEX.md)

# Porting guide — 5-step methodology

This guide details the methodology for porting an existing .NET application to an Orkeon agent-team architecture.

## Overview

Porting consists of decomposing the responsibilities of a monolithic or modular application into specialized agent roles, equipped with tools, and orchestrated by one or more Crews.

```
Application source → Analyse des responsabilités → Mapping vers agents
→ Identification des outils → Définition des tasks → Plan de portage
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
| Reference examples | 103 YAML examples in `examples/` | Builders documented in the docs |

**Recommendation**: favor the **YAML-first** approach for porting. YAML lets you iterate quickly on the prompts (descriptions, backstories) without touching the code. Custom tools remain in C# and are registered via DI.

### YAML-first workflow

```
1. Écrire config.yaml (agents, tasks, process type)
2. Identifier les outils manquants
3. Coder les outils custom (ToolBase<TReq, TRes>)
4. Enregistrer via DI (AddSingleton<IBaseTool, MonTool>())
5. Charger et exécuter :
   var crew = await crewFactory.CreateFromFileAsync("config.yaml");
   var output = await orchestrator.KickoffAsync(crew.Id, input);
6. Itérer sur les prompts dans le YAML
```

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

Spot the points where the application requires human intervention (validation, input, approval). These points will become tasks with `HumanInput = true` in Orkeon.

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
Responsabilité source : [description]
→ Rôle agent         : [AgentRole — nom concis du spécialiste]
→ Objectif agent     : [AgentGoal — résultat attendu]
→ Backstory          : [AgentBackstory — contexte et expertise]
→ Outils nécessaires : [liste des outils]
→ Contraintes        : [MaxIterations, MaxRpm, AllowDelegation]
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
| Service with complex branching logic | `Hierarchical` crew with `LlmBasedManager` |

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

| Need | Existing tool | Package |
|--------|---------------|---------|
| Read a text/JSON/XML file | `FileReadTool` | `Orkeon.Tools.FileSystem` |
| Write a file | `FileWriteTool` | `Orkeon.Tools.FileSystem` |
| List a directory | `DirectoryReadTool` | `Orkeon.Tools.FileSystem` |
| Search within files | `DirectorySearchTool` | `Orkeon.Tools.FileSystem` |
| Read a CSV | `CsvReaderTool` | `Orkeon.Tools.Data` |
| Read a PDF | `PdfReaderTool` | `Orkeon.Tools.Data` |
| Read a DOCX | `DocxReadTool` | `Orkeon.Tools.Data` |
| Manipulate JSON | `JsonTool` | `Orkeon.Tools.Data` |
| SQL query | `RelationalDatabaseTool` | `Orkeon.Tools.Data` |
| MongoDB query | `MongoDbTool` | `Orkeon.Tools.Data` |
| Web search | `WebSearchTool` / `BraveSearchTool` | `Orkeon.Tools.Web` |
| Web scraping | `WebScrapeTool` | `Orkeon.Tools.Web` |
| REST API call | `HttpApiTool` | `Orkeon.Tools.Web` |
| Execute C# code | `SecureCodeInterpreterTool` | `Orkeon.Infrastructure` |
| Ask a colleague | `AskQuestionTool` | `Orkeon.Infrastructure` |
| Delegate a task | `DelegateWorkTool` | `Orkeon.Infrastructure` |
| Semantic search | `SearchTool` | `Orkeon.Infrastructure` |
| RAG over documents | `RagTool` | `Orkeon.Infrastructure` |

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

### 4.3 Define the dependencies

Use `CrewTaskBuilder.DependsOn()` to express prerequisites between tasks. In `Sequential` mode, the declaration order is sufficient. In `Parallel` mode, explicit dependencies control the sequencing.

### 4.4 Configure the execution options

For each task, decide on: the priority (`TaskPriority`), asynchronous execution (`AsyncExecution`), human intervention (`HumanInput`), the output validation schema (`OutputJson`), the output file (`OutputFile`).

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
- Memory is enabled if inter-task context is needed (`EnableMemory(true)`)
- The rate-limiting constraints (`MaxRpm`) are compatible with the external APIs used

### 5.5 Minimal DI setup for the port

Every port requires this dependency-injection setup:

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Requis — couches Application et Infrastructure
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();

        // Suites d'outils — ajouter uniquement celles nécessaires
        services.AddOrkeonFileSystemTools();   // Si agents lisent/écrivent des fichiers
        services.AddOrkeonDataTools();         // Si agents manipulent CSV, PDF, JSON, SQL, MongoDB
        services.AddOrkeonWebTools();          // Si agents font du web search, scraping, HTTP API
        services.AddOrkeonCodeTools();         // Si agents exécutent du shell

        // Outils custom identifiés en Étape 3
        services.AddSingleton<IBaseTool, MonOutilCustom1>();
        services.AddSingleton<IBaseTool, MonOutilCustom2>();

        // Configuration LLM (si pas de défaut)
        services.Configure<LlmConfig>(context.Configuration.GetSection("Llm"));
    })
    .Build();
```

### 5.6 Execution pattern

```csharp
// Charger la crew depuis le YAML
var crewFactory = host.Services.GetRequiredService<ICrewFactory>();
var crew = await crewFactory.CreateFromFileAsync("config.yaml");

// Préparer l'input avec des variables d'exécution
var variables = new Dictionary<string, object>
{
    ["date"] = DateTime.Today.ToString("yyyy-MM-dd"),
    ["environment"] = "production"
};

var input = new CrewInput(
    "Contexte initial pour l'exécution",
    variables);

// Exécuter et récupérer les résultats
var orchestrator = host.Services.GetRequiredService<ICrewOrchestrationService>();
var output = await orchestrator.KickoffAsync(crew.Id, input);

// Exploiter les résultats
var failedTasks = output.TaskOutputs.Where(t => !t.Success).ToList();
if (failedTasks.Count == 0)
{
    // Succès — traiter le résultat final
    Console.WriteLine(output.FinalOutput);
}
else
{
    // Échec — identifier les tasks en erreur
    foreach (var failed in failedTasks)
        Console.Error.WriteLine($"Task {failed.TaskId} failed: {failed.RawOutput}");
}
```
