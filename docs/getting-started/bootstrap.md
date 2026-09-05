> 🇫🇷 [Version française](../fr/getting-started/bootstrap.md)

# Bootstrap and Execution

> **See also**: [Overview](./overview.md) · [YAML configuration](../architecture/yaml-schema.md) · [Back to the index](../INDEX.md)

## Getting the packages

Before any of the code below compiles, add the two NuGet packages it uses. The framework
itself ships as a single package, `Orkeon`; the built-in tool suites wired below
(`AddOrkeonFileSystemTools`, `AddOrkeonDataTools`, `AddOrkeonWebTools`,
`AddOrkeonCodeTools`) live in a second one, `Orkeon.Tools`:

```bash
dotnet add package Orkeon --prerelease
dotnet add package Orkeon.Tools --prerelease
```

`--prerelease` is required while the 1.0.0 line is a release candidate; drop it once
1.0.0 is final. Both commands write the matching `<PackageReference>` entries into your
`.csproj`, so an equivalent hand-edit works just as well.

`Orkeon` carries the whole core closure (Domain, Application, Infrastructure, Hosting,
Plugins, RAG, Analysis, Scripting) — there is no separate `Orkeon.Domain` /
`Orkeon.Application` / `Orkeon.Infrastructure` package. The two opt-ins
`Orkeon.Rag.Onnx` and `Orkeon.Tools.Embeddings.Local` are added the same way when you
need them. The full lineup is the
[publication matrix](../reference/publication-matrix.md).

## Bootstrap and dependency injection

Integrating Orkeon into a .NET application is done through dependency injection at startup. Here is a complete example in `Program.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.DependencyInjection;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Tools.FileSystem.DependencyInjection;
using Orkeon.Tools.Data.DependencyInjection;
using Orkeon.Tools.Web.DependencyInjection;
using Orkeon.Tools.Code.DependencyInjection;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // 1. Application layer (CQRS, orchestration, services)
        services.AddOrkeonApplication();

        // 2. Infrastructure layer (LLM, memory, security, YAML)
        services.AddOrkeonInfrastructure();

        // 3. Tool suites
        // FileSystem: FileRead, FileWrite, DirectoryRead, DirectorySearch, EmailParser, CountPattern
        services.AddOrkeonFileSystemTools();

        // Data: CSV, PDF, JSON, XML, DOCX, XLSX, SQL queries, MongoDB queries, etc.
        services.AddOrkeonDataTools();

        // Web: WebScrape, ScrapeElement, HttpApi, GitHub, ImageGeneration
        // (WebSearch/BraveSearch/Slack/CacheSearch have their own opt-in extensions)
        services.AddOrkeonWebTools();

        // Code: ShellCommand (SecureCodeInterpreter is wired by AddOrkeonInfrastructure's sandbox)
        services.AddOrkeonCodeTools();
    })
    .Build();

// Start the application
await host.RunAsync();
```

Each `AddOrkeon*()` call automatically registers:
- The service interfaces (ports)
- The implementations (adapters)
- The transitive dependencies (factories, converters, validators)
- The tools available in the suite

This approach guarantees consistent, testable dependency injection in line with Clean Architecture.

### Overriding an Orkeon service

`AddOrkeonInfrastructure()` registers its overridable services via `TryAdd*`. To
replace a default implementation (for example `IPathValidator` or
`IComponentSerializer`), simply **register yours before** the call — it will be
honored regardless of the order of subsequent calls.

> **Caveat**: not every service is `TryAdd`-registered. `AddOrkeonApplication()`
> registers `IAgentExecutionService` and `IAgentPlanner` unconditionally
> (`AddScoped`), and `AddOrkeonInfrastructure()` does the same for a few services
> it owns outright (`ILlmProviderFactory`, `ICrewOrchestrationService`, …). For
> those, register your implementation **after** the Orkeon call — the last
> registration wins at resolution time.


```csharp
// Your implementation wins because TryAdd* does not re-register an already present service.
services.AddSingleton<IPathValidator, MyPathValidator>();
services.AddOrkeonInfrastructure();
```

Registration has **no side effect** on the Domain's static state: the default
serializer (`ComponentBase.DefaultSerializer`) is set **non-destructively**
(`TrySetDefaultSerializer`) and will therefore never be overwritten if the host
has already configured one.

### Capability table per overload

Two overloads exist and produce containers with **different** capabilities:

| Capability / module                       | `AddOrkeonInfrastructure()` | `AddOrkeonInfrastructure(IConfiguration)` |
|-------------------------------------------|:---------------------------:|:-----------------------------------------:|
| Core (baseline LLM, in-memory memory, security, serialization, tools) | ✅ | ✅ |
| OpenTelemetry telemetry                   | ❌                          | ✅ (`Telemetry` section)                  |
| MCP                                       | ❌                          | ✅                                        |
| VectorSearch                              | ❌                          | ✅                                        |
| Knowledge store (in-memory stub, warns on first use) | ✅            | ✅                                        |
| RAG subsystem                             | ❌ (opt-in: `AddOrkeonRag` + `AddOrkeonRagTools`) | ❌ (same opt-in)     |
| Durable execution-state persistence       | ❌                          | ✅ **only if** `Orkeon:ExecutionState:Persistence` exists (`Enabled=true` + an `IStateStore`) |
| ChromaDB (vector store)                   | ❌                          | ✅ **only if** the `Orkeon:ChromaDb` section exists |
| Pinecone (vector store)                   | ❌                          | ✅ **only if** the `Orkeon:Pinecone` section exists |

> The overload without `IConfiguration` does **not** wire the interoperability
> modules (MCP, VectorSearch) nor the external vector stores. Use
> `AddOrkeonInfrastructure(configuration)` as soon as these capabilities are required.
> ChromaDB and Pinecone are only activated if their respective configuration section
> is present. The RAG subsystem is wired by **neither** overload — it is always the
> explicit `AddOrkeonRag(configuration)` + `AddOrkeonRagTools()` pair.

> **Opt-in subsystems (R4.9)**: A2A, monitoring backend, MultiModal, NIST, DLP,
> tool rate-limiting, key rotation, benchmarking, kickoff hooks — and likewise
> the **RAG subsystem** (`AddOrkeonRag` + `AddOrkeonRagTools`), **RaggableTree**
> (`AddRaggableTree`), the **plugin system** (`AddOrkeonPlugins`) and the
> **permission gate** (`AddOrkeonPermissionGate`) — are registered by **neither**
> of the two overloads: each is enabled explicitly via its `AddOrkeonXxx()`
> extension. MCP, by contrast, comes with the `IConfiguration` overload (or
> standalone `AddOrkeonMcp(configuration)`), and the **in-memory** checkpointing
> store is registered by both overloads — only its durable variant is opt-in.
> Full catalog: [Opt-in subsystems](../reference/opt-in-subsystems.md).

## Running a Crew

### Approach 1: From a YAML file

```csharp
// Retrieve the services from the host
var crewFactory = host.Services.GetRequiredService<ICrewFactory>();
var orchestrator = host.Services.GetRequiredService<ICrewOrchestrationService>();

// Create the Crew from the YAML file
var crew = await crewFactory.CreateFromFileAsync("config/sales-report-crew.yaml");

// Prepare the input
var input = CrewInput.Empty("Process last week's sales data");

// Run the Crew synchronously
var output = await orchestrator.KickoffAsync(crew.Id, input);

// Print the results
Console.WriteLine("=== Final Result ===");
Console.WriteLine(output.FinalOutput);

Console.WriteLine("\n=== Results per Task ===");
foreach (var taskOutput in output.TaskOutputs)
{
    Console.WriteLine($"[{taskOutput.TaskId}] {taskOutput.Content[..Math.Min(100, taskOutput.Content.Length)]}...");
}

Console.WriteLine($"\nTotal duration: {output.Duration.TotalSeconds:F2}s");
```

### Approach 2: From the Fluent Builder

```csharp
// Retrieve the repositories and the orchestrator
var agentRepository = host.Services.GetRequiredService<IAgentRepository>();
var taskRepository = host.Services.GetRequiredService<ITaskRepository>();
var crewRepository = host.Services.GetRequiredService<ICrewRepository>();
var orchestrator = host.Services.GetRequiredService<ICrewOrchestrationService>();

// (After creating analyst, writer, analyzeTask, reportTask, crew with builders)
// Persist the entities in the repositories
await agentRepository.AddAsync(analyst);
await agentRepository.AddAsync(writer);
await taskRepository.AddAsync(analyzeTask);
await taskRepository.AddAsync(reportTask);
await crewRepository.AddAsync(crew);

// Run the Crew
var variables = new Dictionary<string, object>
{
    ["start_date"] = "2024-10-01",
    ["end_date"] = "2024-12-31"
};

var input = new CrewInput(
    "Generate sales report for Q4 2024",
    variables);

var output = await orchestrator.KickoffAsync(crew.Id, input);

Console.WriteLine(output.FinalOutput);
```

### Input/output types

Running a Crew uses the following types:

**CrewInput** (`Orkeon.Application.Interfaces.Services`):
```csharp
public record CrewInput(
    string? InitialContext,                          // Initial textual context
    IReadOnlyDictionary<string, object> Variables);  // Execution variables

// Helpers:
//   CrewInput.Empty(initialContext)
//   CrewInput.WithStringVariables(initialContext, variables)
```

`Variables` is a plain read-only dictionary. The Domain layer also defines richer `CrewInput`/`CrewOutput` records (`Orkeon.Domain.Crew`, with typed `CrewVariables`) used internally by crew execution.

**CrewOutput** (`Orkeon.Application.Interfaces.Services`):
```csharp
public record CrewOutput(
    string FinalOutput,                       // Final textual result
    IReadOnlyList<TaskOutput> TaskOutputs,    // Results per task
    TimeSpan Duration,                        // Execution duration
    TokenUsage? TokensUsed);                  // Token consumption (when available)
```

**TaskOutput** (`Orkeon.Application.Execution`):
```csharp
public record TaskOutput(
    string TaskId,
    string? AgentId,
    string Content,                                  // Task output
    DateTime CompletedAt,
    bool Success,
    TimeSpan ExecutionTime,
    IReadOnlyList<ToolUsage>? ToolsUsed = null)
{
    public string RawOutput => Content;              // Alias of Content
}
```

## Alternative execution modes

Orkeon supports several execution modes via `ICrewOrchestrationService`:

**Iterative mode (batch)**:
```csharp
// Run the Crew for each element of a collection
var inputs = new List<CrewInput>
{
    CrewInput.Empty("Process sales data for region 1"),
    CrewInput.Empty("Process sales data for region 2"),
    CrewInput.Empty("Process sales data for region 3")
};

var batchOutput = await orchestrator.KickoffForEachAsync(crew.Id, inputs);

// batchOutput is a BatchOutput containing the individual results
```

**Streaming mode**:
```csharp
// Run the Crew and receive the events in real time via IAsyncEnumerable
await foreach (var executionEvent in orchestrator.KickoffStreamingAsync(crew.Id, input))
{
    Console.WriteLine(
        $"[{executionEvent.Timestamp:HH:mm:ss}] {executionEvent.AgentRole} — {executionEvent.TaskDescription}");
    Console.WriteLine($"  {executionEvent.Thought.Type}: {executionEvent.Thought.Content}");
}
```

> **Streaming granularity.** Full tool-call granularity (`AgentThought` events of type
> `Reasoning` / `ToolSelection` / `ToolExecution` / `Conclusion`) requires an
> `IStreamingAgentExecutionService`. `AddOrkeonInfrastructure()` registers one
> (`StreamingAgentExecutionService`) **by default**, so no extra wiring is needed — it only
> needs an `IChatClient` / LLM provider to be configured (see [LLM providers](../architecture/llm-providers.md)).
> If the service is absent (a partial DI setup, or the orchestrator built by hand without it),
> `KickoffStreamingAsync` **degrades to per-task replay** — one `Conclusion` event per task,
> no tool-call detail — and logs an explicit `Warning` naming the missing registration rather
> than downgrading silently.

**Non-blocking asynchronous mode (fire-and-forget)**:
```csharp
// Start the execution without awaiting completion
var executionId = await orchestrator.KickoffAsyncNoWait(crew.Id, input);

Console.WriteLine($"Execution started with ID: {executionId}");

// Check the status later
var status = await orchestrator.GetExecutionStatusAsync(executionId);
Console.WriteLine($"Status: {status.State}");
```
