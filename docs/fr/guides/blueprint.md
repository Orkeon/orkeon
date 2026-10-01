> 🇬🇧 [English version](../../guides/blueprint.md)

> **Voir aussi** : [Guide ProcessTypes](../orchestration/process-types.md) · [Retour à l'index](../INDEX.md)

# Blueprint — Ajout d'un type d'orchestration dans Orkeon

Ce document est une check-list reproductible pour ajouter un septième `ProcessType` au framework et le documenter. Il suit le chemin qu'ont pris les six modes existants — le mode Graph (un moteur Domain + un bloc YAML) et le mode Autonomous (un point d'entrée `IProcessStrategy` dédié) sont les deux précédents de référence — et couvre les 8 étapes, du value object Domain jusqu'à la documentation.

Un enregistrement oublié échoue bruyamment plutôt qu'en silence : `ProcessType.From`, le parser YAML et l'adaptateur de scripting refusent un nom inconnu, et `ProcessStrategyFactory` comme `SequentialCrewOrchestrator` lèvent `NotSupportedException` pour un mode qu'ils n'aiguillent pas.

---

## Prérequis

Avant de commencer, décidez :

- **Nom du mode** : la valeur de `ProcessType` (PascalCase, par ex. `Swarm`) et son écriture YAML/scripting (`swarm`, comparée sans tenir compte de la casse)
- **Point d'entrée** : réutiliser `IProcessStrategy.ExecuteSequentialAsync(crew, plan, …)` (Graph, Consensual) ou ajouter une méthode dédiée (Autonomous, qui a besoin d'un `AgentExecutionBudget`)
- **Configuration** : aucune, une section d'options .NET (Consensual : `Orkeon:Consensus`), ou un bloc YAML de niveau crew (Graph : `graphConfig`)
- **Moteur** : si le mode a besoin d'un moteur Domain réutilisable (`StateGraph<TState>`, `StateMachine<TState, TEvent>`) ou vit entièrement dans sa stratégie
- **Stabilité** : si la surface publique est livrée sous un diagnostic `[Experimental]` (Autonomous : `ORKEXP002`, voir les [API expérimentales](../reference/experimental-apis.md))

---

## Étape 1 — Domain : déclarer le mode

**Fichier** : `src/core/Orkeon.Domain/SharedKernel/ValueObjects/ProcessType.cs`

```csharp
/// <summary>Agents swarm over the task pool under a shared budget.</summary>
public static readonly ProcessType Swarm = new("Swarm");

private static readonly Dictionary<string, ProcessType> s_all = new(StringComparer.OrdinalIgnoreCase)
{ /* … les six entrées existantes …, */ [nameof(Swarm)] = Swarm };
```

`ProcessType.All`, `From` et `TryFrom` lisent `s_all` : une fois l'entrée ajoutée, le YAML (`YamlCrewMapper.ParseProcessType`) et l'adaptateur de scripting (`JsCrewConfigurationAdapter`) acceptent la nouvelle valeur sans autre changement, et leurs messages d'erreur la listent.

**Point d'entrée.** Quand le mode a besoin de son propre point d'entrée, ajoutez-le à `IProcessStrategy` (`src/core/Orkeon.Domain/Crew/IProcessStrategy.cs`), comme l'a été `ExecuteAutonomousAsync` ; chaque stratégie existante l'implémente alors en levant `NotSupportedException("Use <Mode>ProcessStrategy …")`.

**API publique.** Les projets core suivent leur surface publique : ajoutez chaque nouveau membre public au `PublicAPI.Unshipped.txt` du projet (`src/core/Orkeon.Domain/`, `src/core/Orkeon.Infrastructure/`, `src/core/Orkeon.Application/`), sinon le build échoue (RS0016/RS0017 sont des erreurs).

**DTO Application.** La couche Application porte son propre enum, `Orkeon.Application.Crew.DTOs.ProcessType` (`Crew/DTOs/CrewEnums.cs`), mappé par `CrewMapper.ToProcessTypeDomain` (`Common/Mapping/CrewMapper.cs`), qui lève sur une valeur qu'il ne connaît pas : ajoutez le membre et sa branche de mapping.

**Fluent Builder (optionnel).** `CrewBuilder.Process(ProcessType.Swarm)` fonctionne tel quel ; un raccourci comme `.Swarm()` n'existe que pour les modes historiques (`Sequential()`, `Hierarchical(...)`, `Parallel()`, `Consensual()`).

---

## Étape 2 — Domain : configuration et moteur (si nécessaire)

### DTO de configuration

**Fichier** : `src/core/Orkeon.Domain/Configuration/<Mode>Config.cs` — un `sealed record` immuable avec des valeurs par défaut, nullable là où une valeur surcharge un preset (`GraphConfig` sert de modèle : `MaxRetryCycles = 2`, `CircuitBreakerPreset = "strict"`, limites nullables).

Faites-le voyager jusqu'à l'exécution :

| Où | Quoi ajouter | Précédent Graph |
|-------|-------------|-----------------|
| `Configuration/CrewConfiguration.cs` | `public <Mode>Config? <Mode>Config { get; init; }` sur `CrewConfiguration` (et sur `TaskConfiguration`, même fichier, pour un bloc de niveau tâche) | `CrewConfiguration.GraphConfig` |
| `Crew/CrewCreateOptions.cs` | La même propriété | `CrewCreateOptions.GraphConfig` |
| `Crew/Crew.cs` | Une propriété en lecture seule alimentée depuis les options dans `Crew.Create` | `Crew.GraphConfig` |
| `Crew/CrewBuilder.cs` | `With<Mode>Config(...)` qui alimente les options | `WithGraphConfig(...)` |

La stratégie doit lire la config **sur l'argument crew** au moment de l'exécution, jamais la stocker sur l'instance de stratégie (scoped, partagée) — des crews concurrentes écraseraient mutuellement leurs réglages.

### Moteur

Un moteur réutilisable va dans la couche Domain, sans aucune dépendance externe (`Orkeon.Domain.Graph` pour `StateGraph<TState>`, `Orkeon.Domain.Common.StateMachine` pour la FSM). Ce que fournissent les moteurs existants, et qu'un nouveau devrait fournir aussi :

- une garantie de terminaison — circuit breaker (`CircuitBreakerPolicy`, presets `Strict` / `Default` / `Permissive`) ou budget (`AgentExecutionBudget`, mêmes trois presets) ;
- des événements d'observabilité (`OnNodeCompleted` / `OnTransition`, `OnCircuitBroken`) et un instantané d'état ;
- l'annulation coopérative (`CancellationToken` sur chaque membre asynchrone) ;
- la sûreté des threads là où le moteur est partagé (`lock`, `Interlocked`) ;
- `[assembly: InternalsVisibleTo]` uniquement pour les types que les tests doivent atteindre.

---

## Étape 3 — Infrastructure : la stratégie

**Fichier** : `src/core/Orkeon.Infrastructure/Crew/Strategies/<Mode>ProcessStrategy.cs`

```csharp
public sealed partial class SwarmProcessStrategy : IProcessStrategy
{
    public SwarmProcessStrategy(
        CrewStrategyDependencies dependencies,   // tâches, agents, service d'exécution, scope mémoire
        ILogger<SwarmProcessStrategy> logger,
        ICrewExecutionHook? hook = null,          // optionnel : l'observateur du run
        TaskAgentSelector? agentSelector = null)  // optionnel : qui exécute une tâche sans agent:
    { /* … */ }

    public Task<CrewOutput> ExecuteSequentialAsync(Crew crew, ExecutionPlan plan,
        IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken ct = default)
    { /* le mode */ }

    // Les points d'entrée que le mode ne sert pas :
    public Task<CrewOutput> ExecuteHierarchicalAsync(/* … */)
        => throw new NotSupportedException("Use HierarchicalProcessStrategy for hierarchical orchestration.");
    // … ExecuteParallelAsync, ExecuteAutonomousAsync de même
}
```

Réutilisez les briques partagées de `Crew/Strategies/` et `Crew/` — plusieurs sont `internal`, raison pour laquelle les stratégies vivent dans `Orkeon.Infrastructure` :

| Brique | Usage |
|----------------|-----|
| `CrewStrategyDependencies` | Les quatre collaborateurs que prend chaque stratégie (repositories de tâches/agents, `IAgentExecutionService`, `IMemoryScope`) |
| `CrewTaskSequencer.ResolveAsync` | Ordre des tâches : celui du plan, sinon le tri topologique stable sur `dependencies` |
| `TaskAgentSelector` | Choix de l'agent : `agent:` déclaré, sinon `OrkeonApplicationOptions.AgentSelectionStrategy` |
| `CrewHookDispatcher` | Appels à `ICrewExecutionHook` — envoyez l'événement terminal (`CrewCompletedAsync` / `CrewFailedAsync`) sur **chaque** sortie, initialisation et annulation comprises |
| `TokenUsageTally` | Télémétrie de tokens dans les métadonnées de `CrewOutput` (jamais un zéro fabriqué) |
| `AgentDelegationToolsProvider` | Outils de délégation pour les agents avec `allowDelegation: true` (Sequential, Graph) |
| `LlmUsageScope.Begin(...)` | Comptabiliser les appels LLM que le mode fait lui-même (un manager, un juge) sous la bonne opération |

Rendez compte honnêtement du résultat : `CrewOutput.CreateFailure(...)` quand le contrat propre du mode a échoué (le runner sort alors avec un code non nul), avec les sorties et l'usage de tokens produits jusque-là.

---

## Étape 4 — Infrastructure : enregistrement et aiguillage

Trois endroits, tous dans `Orkeon.Infrastructure` :

| Fichier | Changement |
|------|--------|
| `DependencyInjection/InfrastructureExtensions.cs` | `services.AddScoped<SwarmProcessStrategy>();` dans `AddOrkeonRepositoriesAndStrategies` — ou une extension publique `AddOrkeon<Mode>()` (binding d'options, stratégies de vote…) appelée depuis `AddOrkeonFeatureModules`, comme `AddOrkeonConsensus()` |
| `Crew/Strategies/ProcessStrategyFactory.cs` | `"Swarm" => _serviceProvider.GetRequiredService<SwarmProcessStrategy>(),` |
| `Orchestration/SequentialCrewOrchestrator.cs` | Un cas dans `ExecuteDomainStrategyAsync` qui appelle le point d'entrée choisi (`"Swarm" => await processStrategy.ExecuteSequentialAsync(crew, defaultPlan, stringVariables, cancellationToken)…`) |

Une section d'options suit la convention `Orkeon:*` (`services.AddOptions<SwarmOptions>().BindConfiguration("Orkeon:Swarm")`) et est listée dans la [configuration](../reference/configuration.md).

---

## Étape 5 — YAML : le bloc de configuration

`process: swarm` ne demande rien de plus (étape 1). Un bloc de niveau crew suit le chemin de `graphConfig` ; un bloc de niveau tâche suit celui du `deliverable:` de tâche — mappé dans `MapTasks`, puis appliqué à la tâche par `CrewFactory` : un bloc lu et jamais appliqué est un bug, et une crew qui écrit une clé supprimée est refusée au chargement (`RetiredCrewYamlKeys`).

**Modèles** — `src/core/Orkeon.Infrastructure/Configuration/Yaml/YamlConfigModels.cs`. Les modèles ne portent **aucun** attribut `[YamlMember]` : les clés se résolvent par convention (camelCase, avec un repli snake_case). Ajoutez une classe publique et la propriété sur les modèles qui en ont besoin :

```csharp
public class SwarmYamlConfig
{
    public int? MaxAgents { get; set; }   // maxAgents: ou max_agents:
}

// Sur CrewYamlConfig (crew mono-fichier) ET CrewSettingsYamlConfig (crew.yaml multi-fichiers) :
public SwarmYamlConfig? SwarmConfig { get; set; }   // clé : swarmConfig
// Sur TaskYamlConfig pour un bloc de niveau tâche.
```

**Mapping** — `Configuration/Yaml/YamlCrewMapper.cs` : une méthode privée `Map<Mode>Config(...)` qui renvoie le DTO Domain (ou `null`), une propriété sur `CrewMappingSettings`, et l'affectation dans `BuildConfiguration` (niveau crew) ou `MapTasks` (niveau tâche).

**Loader** — `Configuration/YamlCrewDefinitionLoader.cs` remplit `CrewMappingSettings` dans les deux chemins : depuis `CrewSettingsYamlConfig` (multi-fichiers) et depuis `CrewYamlConfig` (mono-fichier). En oublier un fait fonctionner le bloc dans une seule disposition.

**Factory** — `Configuration/CrewFactory.cs` : `if (config.SwarmConfig is not null) builder.WithSwarmConfig(config.SwarmConfig);`, à côté de `WithGraphConfig`.

**Validation** — les règles de chargement (une valeur hors bornes, une combinaison que le mode refuse) vont dans `Configuration/Yaml/CrewDefinitionValidator.cs`, pour qu'un fichier erroné échoue au chargement et non en cours de run.

---

## Étape 6 — DSL de scripting et Studio

| Fichier | Changement |
|------|--------|
| `src/scripting/Orkeon.Scripting/Builders/JsCrewBuilder.cs` | Ajouter le nom à `AllowedProcesses` (le builder refuse tout autre nom) |
| `src/scripting/Orkeon.Scripting/Typings/crew.d.ts` | L'ajouter à l'union `Process` |
| `src/scripting/Orkeon.Scripting/Adapters/JsCrewConfigurationAdapter.cs` | Rien pour le nom (`ProcessType.TryFrom`) ; mapper la config du mode quand le DSL l'expose, ou la lister dans `CollectIgnoredFeatures` |
| `src/scripting/Orkeon.Scripting.Cli/Commands/UseCases/UseCasesCommand.cs` | Le texte d'aide de `--process` de `orkeon usecases list` |
| `src/apps/Orkeon.Studio.Wpf/ViewModels/Teams/UseCaseGalleryViewModel.cs` | L'entrée du filtre de process de la galerie |
| `src/apps/Orkeon.Studio.Core/Localization/StudioStrings.cs` + `src/apps/Orkeon.Studio.Wpf/Resources/Strings*.resx` | Clé `WizardGalleryProcess<Mode>`, valeur anglaise par défaut et traductions (fr, de, es, zh-Hans) |

La forme procédurale d'un script (`await crew.run()`) ignore le process ; seule la forme déclarative (`globalThis.crew = crew`) exécute la stratégie — voir [Scripting](../architecture/scripting.md).

---

## Étape 7 — Tests

xUnit avec les assertions natives et des **doubles écrits à la main** (`Mock*` / `Fake*` / `Stub*` dans un dossier `Doubles/` — pas de bibliothèque de mocking).

| Test | Où | Précédent |
|------|-------|-----------|
| Le value object (nombre d'éléments de `All` — figé à 6 aujourd'hui —, `From`, `TryFrom`, insensibilité à la casse) | `tests/core/Orkeon.Domain.Tests/ValueObjects/ProcessTypeTests.cs` | — |
| Le moteur, s'il y en a un | `tests/core/Orkeon.Domain.Tests/<Zone>/` | `Graph/StateGraphTests.cs`, `Common/StateMachine/*` |
| La stratégie : chemin nominal, crew vide, échecs, annulation, événement terminal du hook sur chaque sortie, métadonnées de tokens, points d'entrée non supportés | `tests/core/Orkeon.Infrastructure.Tests/Strategies/<Mode>ProcessStrategy/` | `GraphProcessStrategy/GraphProcessStrategyTests.cs` |
| L'aiguillage de la factory | `Strategies/ProcessStrategyFactory/ProcessStrategyFactoryTests.cs` | — |
| L'aiguillage de l'orchestrateur vers le bon point d'entrée | `Orchestration/CovAutonomous_SequentialCrewOrchestratorTests.cs` | `KickoffAsync_Graph_DispatchesViaSequential` |
| YAML : parsing de `process:` et le bloc de config dans les deux dispositions | `Configuration/YamlCrewDefinition/YamlCrewDefinitionTests.cs` (itère sur `ProcessType.All`), `Configuration/CircuitBreakerPolicyFactoryGraphTests.cs` | — |
| Mapping Application | `tests/core/Orkeon.Application.Tests/DTOs/Mapping/CrewMapperTests.cs` (itère sur `ProcessType.All`) | — |
| Scripting | `tests/scripting/Orkeon.Scripting.Tests/` (le builder accepte le nom, l'adaptateur le mappe) | — |

```bash
dotnet test tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj --filter "FullyQualifiedName~ProcessType"
dotnet test tests/core/Orkeon.Infrastructure.Tests/Orkeon.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Swarm"
```

Le runner de tests est Microsoft.Testing.Platform : un `--filter` qui ne correspond à aucun test d'un projet est une erreur, filtrez donc chaque projet sur des noms qu'il contient.

---

## Étape 8 — Exemple et documentation

### Exemple

**Dossier** : `examples/<NN-catégorie>/<NNN-nom>/` avec un `config.yaml` (ou un `main.ork.ts`) et un `README.md` (objectif, diagramme, YAML annoté, comment l'exécuter). Partez de [102-graph-orchestration](https://github.com/orkeon/orkeon/blob/main/examples/09-experimental/102-graph-orchestration/). La racine YAML est plate — pas d'enveloppe `crew:` — et `agents:` / `tasks:` sont des mappings indexés par id :

```yaml
name: "swarm-demo"
goal: "…"
process: swarm
swarmConfig:
  maxAgents: 5

agents:
  scout:                  # mapping indexé par id d'agent — jamais une séquence
    role: "…"
    goal: "…"

tasks:
  explore:                # l'id EST la clé (TaskYamlConfig n'a pas de champ id:)
    description: "…"
    expectedOutput: "…"
```

Un nouvel exemple numéroté change le nombre d'exemples que la documentation annonce et que `scripts/check-doc-claims.py` vérifie ; listez-le dans `examples/INDEX.md`.

### Documentation

| Page | Changement |
|------|--------|
| `docs/orchestration/<mode>.md` (nouvelle) | Vue d'ensemble, architecture (classes par couche), flux d'exécution, configuration et résolution, garanties de terminaison, observabilité, usage YAML et C#, exemple, tests — les pages [Graph](../orchestration/graph.md) et [Autonomous](../orchestration/autonomous.md) servent de modèles |
| [`docs/orchestration/process-types.md`](../orchestration/process-types.md) | La liste des valeurs, le diagramme d'architecture, la colonne de la matrice, une section dédiée, l'arbre de décision, le tableau des coûts |
| [`docs/architecture/yaml-schema.md`](../architecture/yaml-schema.md) | Les valeurs de `process:` et le nouveau bloc |
| [`docs/reference/configuration.md`](../reference/configuration.md) | Une nouvelle section d'options, le cas échéant |
| `docs/INDEX.md`, `docs/toc.yml` | La nouvelle page |
| `CHANGELOG.md`, `CLAUDE.md` | La fonctionnalité, et chaque mention de « 6 modes » |

Chaque page a son miroir français sous `docs/fr/` (même chemin), mis à jour dans le même changement — une vérification de CI contrôle la parité. Les placeholders en prose vont dans des code spans (`<Mode>`), jamais nus. Puis exécutez :

```bash
python3 scripts/check-doc-claims.py
```

---

## Check-list finale

| Critère | Attendu | Vérifié |
|---------|-------------------|---------|
| Déclaré | Entrée `ProcessType`, nombre d'éléments de `All`, DTO Application + branche `CrewMapper` | [ ] |
| Aiguillé | Stratégie enregistrée, cas `ProcessStrategyFactory`, cas `SequentialCrewOrchestrator` | [ ] |
| Se termine | Circuit breaker, budget ou boucle bornée — et annulation respectée | [ ] |
| Observable | Événement terminal du hook sur chaque sortie, métadonnées de tokens, logs structurés | [ ] |
| Résultat honnête | `CreateFailure` quand le contrat du mode échoue | [ ] |
| Configurable | Config par crew lue sur la crew, les deux dispositions YAML, validation au chargement | [ ] |
| Scriptable | `AllowedProcesses`, `crew.d.ts`, `orkeon usecases`, galerie Studio | [ ] |
| Testé | Domain, stratégie, factory, orchestrateur, YAML, mapping, scripting | [ ] |
| Documenté | Page du mode + miroir FR, process-types, yaml-schema, INDEX/toc, CHANGELOG, `check-doc-claims.py` au vert | [ ] |
| Exemple | `config.yaml` fonctionnel avec README, listé dans `examples/INDEX.md` | [ ] |
