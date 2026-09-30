> 🇬🇧 [English version](../../orchestration/flows.md)

> **Voir aussi** : [Guide comparatif ProcessTypes](./process-types.md) · [Orchestration Graph](./graph.md) · [Retour à l'index](../INDEX.md)

# Flows (FlowEngine)

## Vue d'ensemble

Un **flow** est une liste d'étapes typées — exécuter une crew, appeler un LLM, appeler un outil, brancher sur une condition, interroger un humain, attendre — exécutées par le `FlowEngine` sur un état clé/valeur partagé (`FlowState`). Là où une crew confie des **tâches** à des **agents** selon l'un des six [ProcessTypes](./process-types.md), un flow enchaîne des **étapes** hétérogènes, dont l'une peut être une crew entière.

| | Crew (`ProcessType`) | Flow (`FlowType`) |
|---|---|---|
| Unité de travail | Une tâche exécutée par un agent | Une étape (`crew`, `llm`, `tool`, `conditional`, `human_input`, `delay`) |
| Contexte partagé | Sorties des tâches précédentes | Clés du `FlowState`, fusionnées après chaque étape |
| Modes | Sequential, Hierarchical, Parallel, Consensual, Graph, Autonomous | Sequential, Parallel, Conditional, Loop (Crew et Custom s'exécutent en Sequential) |
| Défini dans | YAML de crew, Fluent Builder, `.ork.ts` | YAML de flow (`YamlFlowDefinitionLoader`) ou `FlowDefinitionBuilder` en C# |
| Exécuté par | `orkeon run`, `orkeon-host`, Studio, `ICrewOrchestrationService` | **C# uniquement** — `IFlowEngine` |

> **Ce qui s'exécute aujourd'hui.** Le moteur de flows est une API C#. **Aucun point d'entrée CLI, hôte ou scripting n'exécute un flow** : `orkeon run` traite tout fichier YAML comme une définition de crew (un fichier de flow échoue à la validation de crew), et le DSL de scripting n'a pas de binding de flow. Les exemples qui mentionnent `FlowEngine` dans leurs commentaires ([26](https://github.com/orkeon/orkeon/blob/main/examples/02-science-research/26-knowledge-graph/), [29](https://github.com/orkeon/orkeon/blob/main/examples/02-science-research/29-hypothesis-generation/), [59](https://github.com/orkeon/orkeon/blob/main/examples/05-education/59-nonlinear-learning-path/), [61](https://github.com/orkeon/orkeon/blob/main/examples/05-education/61-gamified-learning/), [76](https://github.com/orkeon/orkeon/blob/main/examples/07-creative-media/76-narrative-studio/), [83](https://github.com/orkeon/orkeon/blob/main/examples/07-creative-media/83-interactive-fiction/), [90](https://github.com/orkeon/orkeon/blob/main/examples/08-iot-smart-systems/90-energy-management/), [100](https://github.com/orkeon/orkeon/blob/main/examples/09-experimental/100-civilization-simulator/)) sont des crews en `process: "sequential"` : elles s'exécutent sous `orkeon run` comme des crews, et la partie flow est une fonctionnalité planifiée (marquée `TODO` dans leur `config.yaml`). Aucun exemple livré ne contient de définition de flow.

## Architecture

### Couche Domain — `Orkeon.Domain.Flows`

| Type | Rôle |
|------|------|
| `IFlow` | Un flow exécutable : `Id`, `Name`, `State`, `ExecuteAsync()` → `FlowResult` (`Success`, `Output`, `Error`, `OutputState`) |
| `IFlowDefinition` | La structure déclarative : `Name`, `Description`, `Type`, `Steps`, `Configuration`, `Validate(out errors)` |
| `IFlowStep` | Une étape exécutable : `Name`, `Description`, `ExecuteAsync(FlowState)` → `FlowStepResult` (`Success`, `Output`, `Error`, `UpdatedContext`, `NextStep`) |
| `FlowStep` | Une définition d'étape : `Id`, `Name`, `Type`, `Parameters` (`FlowStepParameters`), `Dependencies`, `Timeout`, `CanRetry`, `MaxRetries` (3) |
| `FlowType` | `Sequential`, `Parallel`, `Conditional`, `Loop`, `Crew`, `Custom` |
| `FlowConfiguration` | `Name`, `Type`, `Settings` (clé/valeur libre), `Timeout`, `MaxRetries` (3), `EnableLogging`, `EnableMetrics` |
| `FlowState` | État clé/valeur immuable (`Get<T>`, `Set`, `Merge`, `SetVariable` → `variables.<key>`, `SetSharedState` → `shared.<key>`, instantanés) |
| `FlowExecutionResult` | Une entrée de l'historique du moteur (`FlowId`, `Success`, `Output`, `Error`, `Duration`, `OutputState`, `StartedAt`, `CompletedAt`) |

Le même namespace déclare aussi des marqueurs de flow par attributs (`FlowAttribute`, `FlowStepAttribute`, `StartAttribute`, `ListenAttribute`, `RouterAttribute`, `FlowBeforeAttribute`, `FlowAfterAttribute`, `FlowValidatorAttribute`, `FlowErrorHandlerAttribute`) et des types d'événements (`FlowStepStartedEventArgs`, `FlowStepCompletedEventArgs`, `EventDrivenFlowContext`). **Rien ne les consomme encore** : une classe qui les porte n'est ni découverte ni exécutée.

### Couche Application

| Type | Rôle |
|------|------|
| `IFlowEngine` (`Orkeon.Application.Interfaces.Ports`) | `ExecuteFlowAsync(IFlow)`, `ExecuteStepAsync(IFlowStep, context)`, `GetRegisteredFlows()`, `GetExecutionHistory(limit)`, `ValidateFlow(definition)` → `FlowValidationResult`, `GetFlowMetrics(flowName)` → `FlowMetrics` |
| `IFlowStepExecutor` (`Orkeon.Application.Interfaces`) | `ResolveStep(FlowStep)` → l'`IFlowStep` exécutable d'un type d'étape |
| `FlowDefinitionBuilder` / `FlowStepBuilder` (`Orkeon.Application.Flow`) | Construction fluent d'un `IFlowDefinition` |

### Couche Infrastructure — `Orkeon.Infrastructure.Flows`

| Type | Rôle |
|------|------|
| `FlowEngine` | L'implémentation d'`IFlowEngine` ; aussi `RegisterFlow(definition)` et `ExecuteDefinitionAsync(definition, initialState)` (membres de la classe, absents de l'interface) |
| `DefinitionBasedFlow` | L'`IFlow` qui exécute un `IFlowDefinition` selon son `FlowType`, avec retry et timeout par étape |
| `FlowStepExecutor` | L'`IFlowStepExecutor` qui associe les six types d'étapes à leurs classes |
| `InMemoryFlowDefinition` | `IFlowDefinition` mutable (ce que produit le loader YAML), avec validation structurelle |
| `YamlFlowDefinitionLoader` | Parse un fichier YAML de flow (chemin VFS) ou une chaîne en `IFlowDefinition` |
| `FlowStepBase<TInput, TOutput>` (`Flows.Base`) | Classe de base typée des étapes : le `FlowState` est désérialisé en `TInput`, validé, exécuté, et `TOutput` est sérialisé (clés snake_case) dans l'état mis à jour |
| `Steps/*FlowStep` | Les six étapes intégrées |
| `Visualization/*` | `FlowGraphSerializer`, `FlowExecutionTracker` |

## Enregistrement

`AddOrkeonInfrastructure()` appelle les deux extensions ; ne les appelez vous-même que dans un hôte qui ne l'utilise pas.

| Extension | Enregistre |
|-----------|-----------|
| `AddOrkeonFlows()` | `IFlowStepExecutor` → `FlowStepExecutor`, `IFlowEngine` → `FlowEngine`, `YamlFlowDefinitionLoader` (singletons, `TryAdd`) |
| `AddOrkeonFlowVisualization()` (`FlowVisualizationExtensions`) | `FlowExecutionTracker` (singleton). `FlowGraphSerializer` est statique et n'a pas besoin d'enregistrement |

Ce que les étapes résolvent à l'exécution : `crew` a besoin d'`ICrewOrchestrationService`, `llm` d'un `IChatClient` (enregistré par les hôtes runner, pas par `AddOrkeonInfrastructure()` seul), `tool` d'`IToolRegistry`, `human_input` d'`IHumanInputProvider` (défaut : `AutoApproveHumanInputProvider`, qui répond sans interroger personne). Ils sont résolus depuis le fournisseur de services racine avec lequel le moteur a été construit.

## Types de flow

`DefinitionBasedFlow` exécute les étapes de la définition selon le `FlowType` :

| Type | Exécution |
|------|-----------|
| `Sequential` (défaut ; aussi `Crew`, `Custom` et toute valeur inconnue) | Étapes dans l'ordre de déclaration. Un résultat d'étape portant `NextStep` (un nom ou un id d'étape) saute vers cette étape — y compris en arrière, sans borne d'itérations |
| `Parallel` | Étapes groupées selon leurs `dependencies` (tri topologique) ; chaque groupe s'exécute en concurrence, les groupes dans l'ordre. Un cycle de dépendances fait échouer le flow |
| `Conditional` | Étapes dans l'ordre ; une étape dont le paramètre `condition` nomme une clé d'état est sautée quand cette clé est fausse, vide ou absente. Quand une étape renvoie `NextStep`, cette étape est exécutée juste après — et à nouveau à son propre tour, sauf si sa `condition` la fait sauter |
| `Loop` | Toutes les étapes, en boucle, jusqu'à ce que la clé d'état `_exit_loop` vaille `true` ou `"true"`, au plus `settings.max_iterations` fois (défaut : le `MaxRetries` du flow, 3). L'itération courante est dans `iteration_count` |

Dans tous les modes, la première étape qui échoue (après ses retries) fait échouer le flow ; l'`Output` du flow est la sortie de la dernière étape et `OutputState` l'état final. L'annulation renvoie un `FlowResult` en échec (« Flow execution was cancelled. »).

**Retries et timeouts** (par étape) : une étape échouée est relancée jusqu'à `MaxRetries` fois (3 par défaut, 0 quand `CanRetry` est faux) avec un backoff exponentiel (1 s, 2 s, 4 s…) ; `Timeout` annule une tentative (« Step … timed out. » quand la dernière tentative expire). Le `Timeout` au niveau du flow est porté par `FlowConfiguration` mais n'est pas appliqué par `DefinitionBasedFlow`.

**Fusion de l'état** : après chaque étape, l'état reçoit `<step name>.output` (l'objet `Output` de l'étape) et les clés de son `UpdatedContext`. Pour les étapes typées, les valeurs utiles sont les clés à plat de leur sortie (`crew_output`, `llm_response`, `tool_output`, `human_input`, …) — l'entrée `.output` contient l'objet de sortie, dont la forme texte est son nom de type. Deux étapes du même type écrivent la même clé à plat : la plus récente l'écrase.

## Les six types d'étapes

`FlowStepExecutor.ResolveStep` associe le `type` de l'étape (insensible à la casse) à une classe ; toute autre valeur lève `InvalidOperationException` (« Unknown step type … »). Les paramètres viennent des `parameters` de l'étape ; quand un paramètre est absent, les étapes typées lisent la même clé dans l'état du flow.

| `type` | Classe | Paramètres | Écrit dans l'état |
|--------|-------|------------|---------------------|
| `crew` | `CrewFlowStep` | `crew_config` (requis) | `crew_output`, `crew_duration` (secondes) |
| `llm` | `LlmFlowStep` | `prompt_template` (requis), `system_prompt` | `llm_response` |
| `tool` | `ToolFlowStep` | `tool_name` (requis), `input` | `tool_output` |
| `conditional` | `ConditionalFlowStep` | `condition_key` (requis), `true_step`, `false_step` | `condition_result` (+ `NextStep`) |
| `human_input` | `HumanInputFlowStep` | `prompt` (défaut : « Please provide input for step '…': ») | `human_input` |
| `delay` | `DelayFlowStep` | `delay_ms`, sinon `delay_seconds` (défaut 1 s) | `message` |

Détails par étape :

- **`crew`** — exécute `ICrewOrchestrationService.KickoffAsync` sur la crew dont `crew_config` contient l'id, **sous forme de GUID** (`crew.Id.ToGuid()`) ; toute autre valeur (un nom de crew, la chaîne ULID) est remplacée par un id aléatoire, et le kickoff échoue avec « crew not found ». La crew doit déjà exister dans le repository que voit le fournisseur racine (les repositories en mémoire sont scoped). La valeur de `crew_config` est aussi transmise comme contexte initial de la crew. L'étape ne vérifie pas `CrewOutput.Succeeded` : une crew échouée donne une étape réussie dont `crew_output` est le message d'échec.
- **`llm`** — remplace chaque `{key}` de `prompt_template` (clés pointées permises, par ex. `{variables.topic}`) par la valeur d'état de cette clé, les placeholders non résolus restant tels quels ; envoie le prompt (et le system prompt optionnel) à `IChatClient` ; l'appel est comptabilisé sous l'opération `flow`.
- **`tool`** — résout `tool_name` dans `IToolRegistry` et l'appelle avec `input` (une chaîne, vide par défaut) ; un outil introuvable ou un résultat d'outil en échec fait échouer l'étape.
- **`conditional`** — lit la valeur d'état de `condition_key` : `null` → faux, un booléen tel quel, une chaîne est vraie sauf vide ou `"false"`, un nombre est vrai sauf 0, toute autre valeur est vraie ; renvoie `true_step` ou `false_step` comme `NextStep`.
- **`human_input`** — interroge `IHumanInputProvider.GetInputAsync` avec un contexte de saisie texte et stocke la réponse.
- **`delay`** — attend, de façon annulable.

### Étapes personnalisées

Implémentez `IFlowStep`, ou dérivez `FlowStepBase<TInput, TOutput>` et surchargez `ExecuteTypedAsync` (et `ValidateTypedRequest`) — les conversions état → entrée et sortie → état sont faites pour vous, et toute exception devient une étape en échec. `FlowStepExecutor` ne connaît que les six types intégrés, et `FlowEngine.ExecuteDefinitionAsync` construit lui-même un `FlowStepExecutor` ; pour exécuter un type personnalisé, implémentez `IFlowStepExecutor` (en déléguant les types intégrés à `FlowStepExecutor`), enveloppez la définition dans `new DefinitionBasedFlow(definition, yourExecutor, logger)` et passez-la à `IFlowEngine.ExecuteFlowAsync`. Une étape isolée peut aussi s'exécuter seule avec `IFlowEngine.ExecuteStepAsync(step, context)`.

## YAML de flow

`YamlFlowDefinitionLoader.LoadFromFileAsync(virtualPath)` lit le fichier via le VFS ; `LoadFromString(yaml)` parse une chaîne. La racine est plate :

```yaml
name: research_pipeline            # requis (validation)
description: "Rechercher et résumer"
type: sequential                   # sequential (défaut) | parallel | conditional | loop | crew | custom
settings:                          # map libre, copiée dans FlowConfiguration.Settings
  max_retries: 2                   # FlowConfiguration.MaxRetries (loop : plafond d'itérations par défaut)
  max_iterations: 5                # flows loop : plafond d'itérations
  timeout_seconds: 300             # FlowConfiguration.Timeout (porté, non appliqué)
steps:                             # une SÉQUENCE, dans l'ordre d'exécution
  - name: research                 # unique ; référencé par dependencies, true_step, false_step
    type: crew
    parameters:
      crew_config: "3f2c9d1e-0b7a-4c55-9e0f-2a6b1c8d4e77"   # l'id de la crew sous forme de GUID
    timeout_seconds: 600           # par tentative
    max_retries: 1                 # par étape (défaut 3)
  - name: summarize
    type: llm
    dependencies: [research]       # noms d'étapes — utilisés par les flows parallel
    parameters:
      prompt_template: "Résume pour un manager : {crew_output}"
      system_prompt: "Tu rédiges des synthèses exécutives concises."
```

- Une valeur de `type` inconnue retombe sur `sequential` ; un `type` d'étape inconnu n'est détecté qu'à la résolution de l'étape (ou comme avertissement par `IFlowEngine.ValidateFlow`).
- `dependencies` nomme d'autres étapes ; un nom qui ne correspond à aucune étape est ignoré silencieusement.
- Écrivez les clés en snake_case comme ci-dessus : `settings` et `parameters` sont des maps simples dont le moteur cherche les clés telles quelles.
- Le loader ne valide pas : appelez `definition.Validate(out var errors)` ou `IFlowEngine.ValidateFlow(definition)` — un nom, au moins une étape, un nom et un type par étape, des ids uniques, des dépendances connues, pas de cycle de dépendances (le moteur ajoute un avertissement par type d'étape inconnu).

## Exécuter un flow

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

// ExecuteDefinitionAsync appartient à la classe FlowEngine (pas à IFlowEngine)
var result = await ((FlowEngine)engine).ExecuteDefinitionAsync(
    definition,
    FlowState.Empty.Set("variables.topic", "EU battery market"));

Console.WriteLine(result.Success ? result.Output : result.Error);
Console.WriteLine(result.OutputState.Get<string>("llm_response"));
```

La même définition en C# :

```csharp
using Orkeon.Application.Flow;

var definition = new FlowDefinitionBuilder()
    .WithName("research_pipeline")
    .AsSequential()                       // AsParallel(), AsConditional(), AsLoop()
    .WithMaxRetries(2)
    .AddCrewStep("research", crew.Id.ToGuid().ToString())
    .AddLlmStep("summarize", "Résume pour un manager : {crew_output}",
        step => step.DependsOn("research").WithParameter("system_prompt", "Tu rédiges des synthèses exécutives concises."))
    .Build();                             // lève si le nom ou les étapes manquent
```

`AddStep(name, type, configure)` ajoute n'importe quel type d'étape ; `FlowStepBuilder` offre `DependsOn(...)`, `WithTimeout(...)`, `WithMaxRetries(...)`, `WithParameter(key, value)`.

## Observabilité

- **Historique d'exécution** : chaque `ExecuteFlowAsync` ajoute un `FlowExecutionResult` à un historique en mémoire (`GetExecutionHistory(limit)`, du plus récent au plus ancien). Il est non borné et perdu au redémarrage.
- **Métriques** : `GetFlowMetrics(flowName)` renvoie les totaux, le taux de succès, la durée moyenne, la dernière exécution et les comptes par étape. Le filtre compare son argument à l'**id** de flow enregistré, pas au nom du flow ; les comptes par étape ne couvrent que les étapes exécutées via `ExecuteStepAsync`.
- **Registre** : `FlowEngine.RegisterFlow(definition)` stocke une définition par nom, `GetRegisteredFlows()` les liste ; rien n'exécute un flow enregistré par son nom.
- **Logs** : début/fin de flow avec durée, retries et timeouts par étape (`LoggerMessage`).
- **Les flows ne sont pas des runs de crew** : ils n'émettent ni événement `ICrewExecutionHook` ni flux `orkeon run --events` — seules les crews lancées par des étapes `crew` le font.

### Visualisation

- `FlowGraphSerializer.Serialize(definition)` → `FlowGraph` (`FlowName`, `Nodes` = étapes avec id, nom, type, `Edges` = dépendances) ; `FlowGraphSerializer.ExportToMermaid(graph)` → un `graph TD` Mermaid (étapes conditionnelles en losanges, étapes crew en sous-routines).
- `FlowExecutionTracker` conserve les états d'étapes par flow (`StartTracking`, `UpdateStepState`, `CompleteTracking`, `GetExecutionState` → `FlowExecutionState` ; `StepState` : `Pending`, `Running`, `Completed`, `Failed`, `Skipped`). **Le moteur ne l'alimente pas** : votre code l'appelle autour des étapes qu'il veut montrer.

```csharp
var mermaid = FlowGraphSerializer.ExportToMermaid(FlowGraphSerializer.Serialize(definition));
```

## Limitations

- Aucune surface runner, hôte, Studio ou scripting n'exécute un flow (voir la [Vue d'ensemble](#vue-densemble)) ; les exemples de flows sont des crews séquentielles avec un `TODO`.
- Les étapes `crew` exigent un id de crew en GUID et une crew visible du fournisseur racine, et rapportent une crew échouée comme une étape réussie.
- Un flow `Sequential` dont une étape `conditional` saute en arrière n'a pas de borne d'itérations ; bornez les cycles avec un flow `Loop`.
- Le `Timeout` au niveau du flow n'est pas appliqué ; historique et métriques sont en mémoire ; `GetFlowMetrics` filtre par id.
- Les marqueurs par attributs et les types d'événements d'`Orkeon.Domain.Flows` ne sont pas consommés.
- Le loader YAML n'a pas de test sur un vrai fichier YAML (son test utilise un sérialiseur factice).

## Tests

| Fichier de test | Couverture |
|-----------|----------|
| `tests/core/Orkeon.Infrastructure.Tests/Flows/FlowEngine/FlowEngineTests.cs` (58) | Les quatre types de flow, backoff des retries et timeouts, tri topologique, les six étapes, `FlowStepExecutor`, historique/validation/métriques du moteur, validation d'`InMemoryFlowDefinition`, résolution des templates, enregistrement `AddOrkeonFlows`, mapping du loader YAML |
| `tests/core/Orkeon.Infrastructure.Tests/Flows/Steps/CrewFlowStepTests.cs` (5) | Étape crew |
| `tests/core/Orkeon.Infrastructure.Tests/Flows/Base/FlowStepBase/FlowStepBaseTests.cs` (10) | Le pipeline d'étape typé |
| `tests/core/Orkeon.Infrastructure.Tests/Flows/Visualization/*` (19) | `FlowGraphSerializer`, `FlowExecutionTracker` |
| `tests/core/Orkeon.Application.Tests/Services/FlowDefinitionBuilderTests.cs` (19) | Le builder fluent |
| `tests/core/Orkeon.Domain.Tests/Flows/*`, `ValueObjects/Flow*Tests.cs` | Types de flow, attributs, événements, état, paramètres |

```bash
dotnet test tests/core/Orkeon.Infrastructure.Tests/Orkeon.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Flows"
```
