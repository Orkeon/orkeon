> 🇫🇷 [Version française](../fr/getting-started/bootstrap.md)

# Bootstrap and Execution

> **See also**: [Overview](./overview.md) · [YAML configuration](../architecture/yaml-schema.md) · [Back to the index](../INDEX.md)

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
        // 1. Couche Application (CQRS, orchestration, services)
        services.AddOrkeonApplication();

        // 2. Couche Infrastructure (LLM, mémoire, sécurité, YAML)
        services.AddOrkeonInfrastructure();

        // 3. Suites d'outils
        // FileSystem : FileRead, FileWrite, DirectoryRead, DirectoryCreate, etc.
        services.AddOrkeonFileSystemTools();

        // Data : CSV, PDF, JSON, DOCX, SQL queries, MongoDB queries, etc.
        services.AddOrkeonDataTools();

        // Web : WebSearch, WebScrape, HttpApi, GitHub, etc.
        services.AddOrkeonWebTools();

        // Code : ShellCommand, SecureCodeInterpreter
        services.AddOrkeonCodeTools();
    })
    .Build();

// Démarrer l'application
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
honored regardless of the order of subsequent calls:

```csharp
// Votre implémentation gagne car TryAdd* ne réenregistre pas un service déjà présent.
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
| Knowledge                                 | ❌                          | ✅                                        |
| RAG (+ RAG validation)                    | ❌                          | ✅                                        |
| ChromaDB (vector store)                   | ❌                          | ✅ **only if** the `Orkeon:ChromaDb` section exists |
| Pinecone (vector store)                   | ❌                          | ✅ **only if** the `Orkeon:Pinecone` section exists |

> The overload without `IConfiguration` does **not** wire the interoperability
> modules (MCP, VectorSearch, Knowledge, RAG) nor the external vector stores. Use
> `AddOrkeonInfrastructure(configuration)` as soon as these capabilities are required.
> ChromaDB and Pinecone are only activated if their respective configuration section
> is present.

> **Opt-in subsystems (R4.9)**: A2A, monitoring backend, MultiModal, NIST, DLP,
> tool rate-limiting, key rotation, benchmarking and kickoff hooks are registered
> by **neither** of the two overloads — they are enabled explicitly via their
> `AddOrkeonXxx()` extension. See
> [Opt-in subsystems](../reference/opt-in-subsystems.md).

## Running a Crew

### Approach 1: From a YAML file

```csharp
// Récupérer les services depuis l'hôte
var crewFactory = host.Services.GetRequiredService<ICrewFactory>();
var orchestrator = host.Services.GetRequiredService<ICrewOrchestrationService>();

// Créer la Crew depuis le fichier YAML
var crew = await crewFactory.CreateFromFileAsync("config/sales-report-crew.yaml");

// Préparer l'entrée
var input = CrewInput.Empty("Process last week's sales data");

// Exécuter la Crew de manière synchrone
var output = await orchestrator.KickoffAsync(crew.Id, input);

// Afficher les résultats
Console.WriteLine("=== Résultat Final ===");
Console.WriteLine(output.FinalOutput);

Console.WriteLine("\n=== Résultats par Task ===");
foreach (var taskOutput in output.TaskOutputs)
{
    Console.WriteLine($"[{taskOutput.TaskId}] {taskOutput.Content[..Math.Min(100, taskOutput.Content.Length)]}...");
}

Console.WriteLine($"\nDurée totale : {output.Duration.TotalSeconds:F2}s");
```

### Approach 2: From the Fluent Builder

```csharp
// Récupérer les repositories et l'orchestrator
var agentRepository = host.Services.GetRequiredService<IAgentRepository>();
var taskRepository = host.Services.GetRequiredService<ITaskRepository>();
var crewRepository = host.Services.GetRequiredService<ICrewRepository>();
var orchestrator = host.Services.GetRequiredService<ICrewOrchestrationService>();

// (Après avoir créé analyst, writer, analyzeTask, reportTask, crew avec builders)
// Persister les entités dans les repositories
await agentRepository.AddAsync(analyst);
await agentRepository.AddAsync(writer);
await taskRepository.AddAsync(analyzeTask);
await taskRepository.AddAsync(reportTask);
await crewRepository.AddAsync(crew);

// Exécuter la Crew
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
    string? InitialContext,                          // Contexte initial textuel
    IReadOnlyDictionary<string, object> Variables);  // Variables d'exécution

// Helpers :
//   CrewInput.Empty(initialContext)
//   CrewInput.WithStringVariables(initialContext, variables)
```

`Variables` is a plain read-only dictionary. The Domain layer also defines richer `CrewInput`/`CrewOutput` records (`Orkeon.Domain.Crew`, with typed `CrewVariables`) used internally by crew execution.

**CrewOutput** (`Orkeon.Application.Interfaces.Services`):
```csharp
public record CrewOutput(
    string FinalOutput,                       // Résultat final textuel
    IReadOnlyList<TaskOutput> TaskOutputs,    // Résultats par tâche
    TimeSpan Duration,                        // Durée d'exécution
    TokenUsage? TokensUsed);                  // Consommation de tokens (si disponible)
```

**TaskOutput** (`Orkeon.Application.Execution`):
```csharp
public record TaskOutput(
    string TaskId,
    string? AgentId,
    string Content,                                  // Sortie de la tâche
    DateTime CompletedAt,
    bool Success,
    TimeSpan ExecutionTime,
    IReadOnlyList<ToolUsage>? ToolsUsed = null)
{
    public string RawOutput => Content;              // Alias de Content
}
```

## Alternative execution modes

Orkeon supports several execution modes via `ICrewOrchestrationService`:

**Iterative mode (batch)**:
```csharp
// Exécuter la Crew pour chaque élément d'une collection
var inputs = new List<CrewInput>
{
    CrewInput.Empty("Process sales data for region 1"),
    CrewInput.Empty("Process sales data for region 2"),
    CrewInput.Empty("Process sales data for region 3")
};

var batchOutput = await orchestrator.KickoffForEachAsync(crew.Id, inputs);

// batchOutput est de type BatchOutput contenant les résultats individuels
```

**Streaming mode**:
```csharp
// Exécuter la Crew et recevoir les événements en temps réel via IAsyncEnumerable
await foreach (var executionEvent in orchestrator.KickoffStreamingAsync(crew.Id, input))
{
    Console.WriteLine(
        $"[{executionEvent.Timestamp:HH:mm:ss}] {executionEvent.AgentRole} — {executionEvent.TaskDescription}");
    Console.WriteLine($"  {executionEvent.Thought.Type} : {executionEvent.Thought.Content}");
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
// Démarrer l'exécution sans attendre la complétion
var executionId = await orchestrator.KickoffAsyncNoWait(crew.Id, input);

Console.WriteLine($"Exécution démarrée avec ID : {executionId}");

// Vérifier le statut ultérieurement
var status = await orchestrator.GetExecutionStatusAsync(executionId);
Console.WriteLine($"Statut : {status.State}");
```
