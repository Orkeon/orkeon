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
respectée quel que soit l'ordre des appels suivants :

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
| Knowledge                                 | ❌                          | ✅                                        |
| RAG (+ validation RAG)                    | ❌                          | ✅                                        |
| ChromaDB (vector store)                   | ❌                          | ✅ **uniquement si** la section `Orkeon:ChromaDb` existe |
| Pinecone (vector store)                   | ❌                          | ✅ **uniquement si** la section `Orkeon:Pinecone` existe |

> La surcharge sans `IConfiguration` ne câble **pas** les modules d'interopérabilité
> (MCP, VectorSearch, Knowledge, RAG) ni les vector stores externes. Utilisez
> `AddOrkeonInfrastructure(configuration)` dès que ces capacités sont requises.
> ChromaDB et Pinecone ne s'activent que si leur section de configuration respective
> est présente.

> **Sous-systèmes opt-in (R4.9)** : A2A, monitoring backend, MultiModal, NIST, DLP,
> rate-limiting d'outils, rotation de clés, benchmarking et hooks de kickoff ne sont
> enregistrés par **aucune** des deux surcharges — ils s'activent explicitement via
> leur extension `AddOrkeonXxx()`. Voir
> [Sous-systèmes opt-in](../reference/opt-in-subsystems.md).

## Exécuter une Crew

### Approche 1 : À partir d'un fichier YAML

```csharp
// Récupérer les services depuis l'hôte
var crewFactory = host.Services.GetRequiredService<ICrewFactory>();
var orchestrator = host.Services.GetRequiredService<ICrewOrchestrationService>();

// Créer la Crew depuis le fichier YAML
var crew = await crewFactory.CreateFromFileAsync("config/sales-report-crew.yaml");

// Préparer l'entrée
var input = new CrewInput(initialContext: "Process last week's sales data");

// Exécuter la Crew de manière synchrone
var output = await orchestrator.KickoffAsync(crew.Id, input);

// Afficher les résultats
Console.WriteLine("=== Résultat Final ===");
Console.WriteLine(output.Output);

Console.WriteLine("\n=== Résultats par Task ===");
foreach (var taskOutput in output.TaskOutputs)
{
    Console.WriteLine($"[{taskOutput.TaskId}] {taskOutput.Output[..Math.Min(100, taskOutput.Output.Length)]}...");
}

Console.WriteLine($"\nDurée totale : {output.ExecutionTime.TotalSeconds:F2}s");
Console.WriteLine($"Succès : {output.Success}");
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
await agentRepository.SaveAsync(analyst);
await agentRepository.SaveAsync(writer);
await taskRepository.SaveAsync(analyzeTask);
await taskRepository.SaveAsync(reportTask);
await crewRepository.SaveAsync(crew);

// Exécuter la Crew
var variables = CrewVariables.Empty
    .Set("start_date", "2024-10-01")
    .Set("end_date", "2024-12-31");

var input = new CrewInput(
    initialContext: "Generate sales report for Q4 2024",
    variables: variables);

var output = await orchestrator.KickoffAsync(crew.Id, input);

Console.WriteLine(output.Output);
```

### Types d'entrée/sortie

L'exécution d'une Crew utilise les types suivants :

**CrewInput** (`Orkeon.Domain.Crew`) :
```csharp
public sealed record CrewInput
{
    public string InitialContext { get; init; }       // Contexte initial textuel
    public CrewVariables Variables { get; init; }     // Variables typées (string, int, bool, double)
    public Dictionary<string, object> Parameters => Variables.ToDictionary();
    public CrewMetadata Metadata { get; init; }       // Métadonnées d'exécution
    public DateTime CreatedAt { get; init; }
}
```

`CrewVariables` est un conteneur immutable fortement typé avec des dictionnaires séparés pour `string`, `int`, `bool` et `double`. Chaque méthode `Set` retourne une nouvelle instance (pattern d'immutabilité).

**CrewOutput** (`Orkeon.Domain.Crew`) :
```csharp
public sealed record CrewOutput
{
    public string Output { get; init; }                          // Résultat final textuel
    public object? StructuredOutput { get; init; }               // Résultat structuré (JSON désérialisé)
    public IReadOnlyList<TaskOutput> TaskOutputs { get; init; }  // Résultats par tâche
    public bool Success { get; init; }                           // Succès global
    public string? Error { get; init; }                          // Message d'erreur si échec
    public TimeSpan ExecutionTime { get; init; }                 // Durée d'exécution
    public DateTime CompletedAt { get; init; }                   // Horodatage de fin
    public CrewMetadata Metadata { get; init; }                  // Métadonnées
}
```

**TaskOutput** (`Orkeon.Domain.Task.ValueObjects`) :
```csharp
public sealed record TaskOutput
{
    public TaskId? TaskId { get; init; }
    public string RawOutput { get; init; }           // Sortie brute
    public string? FormattedOutput { get; init; }    // Sortie formatée (optionnel)
    public string Output { get; init; }              // Alias de RawOutput
    public bool Success { get; init; }
    public TimeSpan ExecutionTime { get; init; }
    public object? StructuredOutput { get; init; }
}
```

## Modes d'exécution alternatifs

Orkeon supporte plusieurs modes d'exécution via `ICrewOrchestrationService` :

**Mode itératif (par lot)** :
```csharp
// Exécuter la Crew pour chaque élément d'une collection
var inputs = new List<CrewInput>
{
    new(initialContext: "Process sales data for region 1"),
    new(initialContext: "Process sales data for region 2"),
    new(initialContext: "Process sales data for region 3")
};

var batchOutput = await orchestrator.KickoffForEachAsync(crew.Id, inputs);

// batchOutput est de type BatchOutput contenant les résultats individuels
```

**Mode streaming** :
```csharp
// Exécuter la Crew et recevoir les événements en temps réel via IAsyncEnumerable
await foreach (var executionEvent in orchestrator.KickoffStreamingAsync(crew.Id, input))
{
    switch (executionEvent)
    {
        case TaskStartedEvent taskStarted:
            Console.WriteLine($"Tâche démarrée : {taskStarted.TaskId}");
            break;
        case ToolUsedEvent toolUsed:
            Console.WriteLine($"Outil utilisé : {toolUsed.ToolName}");
            break;
        case TaskCompletedEvent taskCompleted:
            Console.WriteLine($"Tâche complétée : {taskCompleted.Content[..50]}...");
            break;
    }
}
```

**Mode asynchrone non-bloquant (fire-and-forget)** :
```csharp
// Démarrer l'exécution sans attendre la complétion
var executionId = await orchestrator.KickoffAsyncNoWait(crew.Id, input);

Console.WriteLine($"Exécution démarrée avec ID : {executionId}");

// Vérifier le statut ultérieurement
var status = await orchestrator.GetExecutionStatusAsync(executionId);
Console.WriteLine($"Statut : {status.State}");
```
