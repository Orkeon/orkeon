> 🇬🇧 [English version](../../getting-started/bootstrap.md)

# Bootstrap et exécution

> **Voir aussi** : [Vue d'ensemble](./overview.md) · [Configuration YAML](../architecture/yaml-schema.md) · [Retour à l'index](../INDEX.md)

## Bootstrap et injection de dépendances

L'intégration d'Orkeon dans une application .NET s'effectue par injection de dépendances au démarrage. Voici un exemple complet dans `Program.cs` :

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
        // FileSystem : FileRead, FileWrite, DirectoryRead, DirectorySearch, EmailParser, CountPattern
        services.AddOrkeonFileSystemTools();

        // Data : CSV, PDF, JSON, XML, DOCX, XLSX, requêtes SQL, requêtes MongoDB, etc.
        services.AddOrkeonDataTools();

        // Web : WebScrape, ScrapeElement, HttpApi, GitHub, ImageGeneration
        // (WebSearch/BraveSearch/Slack/CacheSearch ont leurs propres extensions opt-in)
        services.AddOrkeonWebTools();

        // Code : ShellCommand (SecureCodeInterpreter est câblé par le sandbox d'AddOrkeonInfrastructure)
        services.AddOrkeonCodeTools();
    })
    .Build();

// Démarrer l'application
await host.RunAsync();
```

Chaque appel `AddOrkeon*()` enregistre automatiquement :
- Les interfaces de service (ports)
- Les implémentations (adapters)
- Les dépendances transitives (factories, converters, validators)
- Les outils disponibles dans la suite

Cette approche garantit une injection de dépendances cohérente et testable conforme à l'architecture Clean Architecture.

### Surcharger un service Orkeon

`AddOrkeonInfrastructure()` enregistre ses services surchargeables via `TryAdd*`. Pour
remplacer une implémentation par défaut (par exemple `IPathValidator` ou
`IComponentSerializer`), il suffit d'**enregistrer la vôtre avant** l'appel — elle sera
respectée quel que soit l'ordre des appels suivants.

> **Réserve** : tous les services ne sont pas enregistrés en `TryAdd`.
> `AddOrkeonApplication()` enregistre `IAgentExecutionService` et `IAgentPlanner`
> inconditionnellement (`AddScoped`), et `AddOrkeonInfrastructure()` fait de même
> pour quelques services qu'il possède en propre (`ILlmProviderFactory`,
> `ICrewOrchestrationService`, …). Pour ceux-là, enregistrez votre implémentation
> **après** l'appel Orkeon — le dernier enregistrement gagne à la résolution.


```csharp
// Votre implémentation gagne car TryAdd* ne réenregistre pas un service déjà présent.
services.AddSingleton<IPathValidator, MyPathValidator>();
services.AddOrkeonInfrastructure();
```

L'enregistrement n'a **aucun effet de bord** sur l'état statique du Domain : le
sérialiseur par défaut (`ComponentBase.DefaultSerializer`) est posé de façon
**non destructive** (`TrySetDefaultSerializer`) et ne sera donc jamais écrasé si l'hôte
en a déjà configuré un.

### Table des capacités par surcharge

Deux surcharges existent et produisent des conteneurs aux capacités **différentes** :

| Capacité / module                         | `AddOrkeonInfrastructure()` | `AddOrkeonInfrastructure(IConfiguration)` |
|-------------------------------------------|:---------------------------:|:-----------------------------------------:|
| Cœur (LLM baseline, mémoire in-memory, sécurité, sérialisation, outils) | ✅ | ✅ |
| Télémétrie OpenTelemetry                  | ❌                          | ✅ (section `Telemetry`)                  |
| MCP                                       | ❌                          | ✅                                        |
| VectorSearch                              | ❌                          | ✅                                        |
| Store de connaissances (stub in-memory, avertit au premier usage) | ✅  | ✅                                        |
| Sous-système RAG                          | ❌ (opt-in : `AddOrkeonRag` + `AddOrkeonRagTools`) | ❌ (même opt-in)    |
| Persistance durable de l'état d'exécution | ❌                          | ✅ **uniquement si** `Orkeon:ExecutionState:Persistence` existe (`Enabled=true` + un `IStateStore`) |
| ChromaDB (vector store)                   | ❌                          | ✅ **uniquement si** la section `Orkeon:ChromaDb` existe |
| Pinecone (vector store)                   | ❌                          | ✅ **uniquement si** la section `Orkeon:Pinecone` existe |

> La surcharge sans `IConfiguration` ne câble **pas** les modules d'interopérabilité
> (MCP, VectorSearch) ni les vector stores externes. Utilisez
> `AddOrkeonInfrastructure(configuration)` dès que ces capacités sont requises.
> ChromaDB et Pinecone ne s'activent que si leur section de configuration respective
> est présente. Le sous-système RAG n'est câblé par **aucune** des deux surcharges —
> c'est toujours la paire explicite `AddOrkeonRag(configuration)` + `AddOrkeonRagTools()`.

> **Sous-systèmes opt-in (R4.9)** : A2A, monitoring backend, MultiModal, NIST, DLP,
> rate-limiting d'outils, rotation de clés, benchmarking, hooks de kickoff — et de
> même le **sous-système RAG** (`AddOrkeonRag` + `AddOrkeonRagTools`),
> **RaggableTree** (`AddRaggableTree`), le **système de plugins**
> (`AddOrkeonPlugins`) et la **permission gate** (`AddOrkeonPermissionGate`) — ne
> sont enregistrés par **aucune** des deux surcharges : chacun s'active
> explicitement via son extension `AddOrkeonXxx()`. MCP, lui, vient avec la
> surcharge `IConfiguration` (ou `AddOrkeonMcp(configuration)` seul), et le store
> de checkpointing **in-memory** est enregistré par les deux surcharges — seule
> sa variante durable est opt-in.
> Catalogue complet : [Sous-systèmes opt-in](../reference/opt-in-subsystems.md).

## Exécuter une Crew

### Approche 1 : À partir d'un fichier YAML

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

### Approche 2 : À partir du Fluent Builder

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

### Types d'entrée/sortie

L'exécution d'une Crew utilise les types suivants :

**CrewInput** (`Orkeon.Application.Interfaces.Services`) :
```csharp
public record CrewInput(
    string? InitialContext,                          // Contexte initial textuel
    IReadOnlyDictionary<string, object> Variables);  // Variables d'exécution

// Helpers :
//   CrewInput.Empty(initialContext)
//   CrewInput.WithStringVariables(initialContext, variables)
```

`Variables` est un simple dictionnaire en lecture seule. La couche Domain définit aussi des records `CrewInput`/`CrewOutput` plus riches (`Orkeon.Domain.Crew`, avec `CrewVariables` typé) utilisés en interne par l'exécution des Crews.

**CrewOutput** (`Orkeon.Application.Interfaces.Services`) :
```csharp
public record CrewOutput(
    string FinalOutput,                       // Résultat final textuel
    IReadOnlyList<TaskOutput> TaskOutputs,    // Résultats par tâche
    TimeSpan Duration,                        // Durée d'exécution
    TokenUsage? TokensUsed);                  // Consommation de tokens (si disponible)
```

**TaskOutput** (`Orkeon.Application.Execution`) :
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

## Modes d'exécution alternatifs

Orkeon supporte plusieurs modes d'exécution via `ICrewOrchestrationService` :

**Mode itératif (par lot)** :
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

**Mode streaming** :
```csharp
// Exécuter la Crew et recevoir les événements en temps réel via IAsyncEnumerable
await foreach (var executionEvent in orchestrator.KickoffStreamingAsync(crew.Id, input))
{
    Console.WriteLine(
        $"[{executionEvent.Timestamp:HH:mm:ss}] {executionEvent.AgentRole} — {executionEvent.TaskDescription}");
    Console.WriteLine($"  {executionEvent.Thought.Type} : {executionEvent.Thought.Content}");
}
```

> **Granularité du streaming.** La pleine granularité par appel d'outil (événements
> `AgentThought` de type `Reasoning` / `ToolSelection` / `ToolExecution` / `Conclusion`)
> requiert un `IStreamingAgentExecutionService`. `AddOrkeonInfrastructure()` en enregistre
> un (`StreamingAgentExecutionService`) **par défaut**, aucun câblage supplémentaire n'est
> donc nécessaire — il lui faut seulement un `IChatClient` / provider LLM configuré (voir
> [Fournisseurs LLM](../architecture/llm-providers.md)). Si le service est absent (setup DI
> partiel, ou orchestrateur construit à la main sans lui), `KickoffStreamingAsync`
> **dégrade en rejeu par tâche** — un événement `Conclusion` par tâche, sans détail des
> appels d'outils — et journalise un `Warning` explicite nommant l'enregistrement manquant,
> plutôt que de rétrograder en silence.

**Mode asynchrone non-bloquant (fire-and-forget)** :
```csharp
// Démarrer l'exécution sans attendre la complétion
var executionId = await orchestrator.KickoffAsyncNoWait(crew.Id, input);

Console.WriteLine($"Exécution démarrée avec ID : {executionId}");

// Vérifier le statut ultérieurement
var status = await orchestrator.GetExecutionStatusAsync(executionId);
Console.WriteLine($"Statut : {status.State}");
```
