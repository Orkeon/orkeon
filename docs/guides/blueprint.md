> 🇫🇷 [Version française](../fr/guides/blueprint.md)

> **See also**: [ProcessTypes guide](../orchestration/process-types.md) · [Back to index](../INDEX.md)

# Blueprint — Adding an orchestration type to Orkeon

This document serves as a reproducible guide for implementing and documenting a new orchestration type. It builds on the FSM precedent (see [FSM Orchestration](../orchestration/fsm.md)) and covers the 8 required steps, from the Domain layer through to the final documentation.

---

## Prerequisites

Before starting, identify:

- **Type name**: e.g. `StateGraph`, `AgenticSwarm`, `PipelineDAG`
- **Doc number**: create a file in `docs/orchestration/` (e.g. `docs/orchestration/nouveau-type.md`)
- **Example number**: next free index in `examples/` (e.g. `104`)
- **Initial scope**: where the new type applies first (Task, Crew, Flow)

---

## Step 1 — Domain: Generic framework

**Folder**: `src/core/Orkeon.Domain/Common/<NomType>/`

Create the following files (adapt the class names):

| File | Role | Reference model |
|---------|------|---------------------|
| `I<NomType>.cs` | Read-only + mutation interfaces in a single file | `IStateMachine.cs` (contains `IStateMachine` + `IMutableStateMachine`) |
| `<NomType>.cs` | Thread-safe main engine | `StateMachine.cs` |
| `<NomType>Builder.cs` | Fluent API to declare the graph/config | `StateMachineBuilder.cs` |
| `<NomType>Policy.cs` | Immutable configuration record with presets + `CircuitBreakerStatus` | `CircuitBreakerPolicy.cs` |
| `<NomType>Result.cs` | Immutable record of the operation result | `TransitionResult.cs` |
| `<NomType>Exceptions.cs` | Type-specific exceptions | `StateMachineExceptions.cs` |
| `TransitionDefinition.cs` | `internal sealed` class for a unit transition (from, trigger, to, guard, action) | `TransitionDefinition.cs` |

> **Note**: The two interfaces (read-only and mutation) live in a single file by convention.
> The `TransitionDefinition` class is `internal` and exposed to tests via `[assembly: InternalsVisibleTo]`.

### Technical checklist

- [ ] Thread-safety: `lock` or `SemaphoreSlim` depending on the async need
- [ ] Static presets: `Strict`, `Default`, `Permissive` (immutable)
- [ ] Events: at minimum `OnTransition` and `OnCircuitBroken` (or equivalent)
- [ ] `CircuitBreakerStatus` or equivalent with a full snapshot for observability
- [ ] No external dependency (pure Domain)
- [ ] `[assembly: InternalsVisibleTo("Orkeon.Domain.Tests")]` if there are internal classes

### Code pattern — Engine

```csharp
namespace Orkeon.Domain.Common.<NomType>;

public sealed class <NomType><TState, TEvent>
    : I<NomType><TState, TEvent>, IMutable<NomType><TState, TEvent>
    where TState : notnull
    where TEvent : notnull
{
    private readonly object _lock = new();
    // ... état interne, compteurs, histogramme

    // Points d'extension obligatoires :
    // 1. Vérification des limites AVANT chaque opération
    // 2. Hook d'action APRÈS chaque opération réussie
    // 3. Événements pour observabilité externe
    // 4. Mode dégradé (fallback state) quand UseDegradedMode = true
}
```

### Code pattern — Fluent builder

```csharp
public sealed class <NomType>Builder<TState, TEvent>
    where TState : notnull
    where TEvent : notnull
{
    public <NomType>Builder<TState, TEvent> WithInitialState(TState state) { ... }
    public <NomType>Builder<TState, TEvent> WithTerminalStates(params TState[] states) { ... }
    public <NomType>Builder<TState, TEvent> WithDegradedState(TState state) { ... }
    public <NomType>Builder<TState, TEvent> WithCircuitBreaker(<NomType>Policy policy) { ... }

    // Fonctions clé pour dictionnaires internes (utile si TState n'est pas un enum) :
    public <NomType>Builder<TState, TEvent> WithStateKey(Func<TState, string> keyFunc) { ... }
    public <NomType>Builder<TState, TEvent> WithEventKey(Func<TEvent, string> keyFunc) { ... }

    // Pattern nested builder pour les transitions/nœuds :
    public TransitionBuilder When(TState from, TEvent trigger) { ... }

    // Raccourci pour transition simple sans guard ni action :
    public <NomType>Builder<TState, TEvent> AddTransition(TState from, TEvent trigger, TState to) { ... }

    public <NomType><TState, TEvent> Build() { ... }

    public sealed class TransitionBuilder
    {
        public TransitionBuilder TransitionTo(TState to) { ... }
        public TransitionBuilder WithGuard(Func<bool> guard, string? desc = null) { ... }
        public TransitionBuilder WithGuard<TContext>(Func<TContext, bool> guard, string? desc = null) { ... }
        public TransitionBuilder WithAction(Action action) { ... }
        public TransitionBuilder WithAction(Action<TState, TState> action) { ... }
        public <NomType>Builder<TState, TEvent> Done() { ... }
    }
}
```

---

## Step 2 — Domain: Specialization

**Folder**: `src/core/Orkeon.Domain/<Scope>/` (e.g. `Task/`, `Crew/`, `Flow/`)

| File | Role |
|---------|------|
| `<Scope><NomType>State.cs` | BOTH enums (states + events) in a single file | 
| `<Scope><NomType>Machine.cs` | Static factory `Create()` + `CreateBuilder()` + `GuardContext` record |

> **FSM convention**: In the existing implementation, `TaskExecutionState.cs` contains both
> the `TaskExecutionState` enum and the `TaskExecutionEvent` enum. Group both in a single
> file when they are always used together.

### Guard context type

```csharp
public sealed record <Scope>GuardContext
{
    // Compteurs de boucle (adapter selon le type d'orchestration)
    public int RetryCount { get; init; }
    public int MaxRetries { get; init; } = 3;

    // Compteurs spécifiques au type
    // Ex FSM : ToolCallCount, ValidationAttempts
    // Ex LangGraph : NodeVisitCount, EdgeTraversalCount
    // Ex Agentique : SubAgentSpawnCount, DelegationDepth

    // Predicats derives
    public bool CanRetry => RetryCount < MaxRetries;
    // ...
}
```

### Anti-hallucination guards (mandatory)

Every orchestration type MUST implement at minimum:

| Guard | Purpose | FSM example |
|-------|-----|-------------|
| Operation budget | Limit expensive calls | `ToolCallCount < MaxToolCallsPerRound` |
| Registered tool | Block hallucinated tools | `IsToolRegistered == true` |
| Retry limit | Avoid infinite loops | `RetryCount < MaxRetries` |

---

## Step 3 — Domain: Configuration DTO

**File**: `src/core/Orkeon.Domain/Configuration/<NomType>Config.cs`

```csharp
namespace Orkeon.Domain.Configuration;

/// <summary>
/// DTO immutable pour la configuration <NomType> depuis YAML.
/// Tous les champs sont nullable pour permettre l'héritage crew → task.
/// </summary>
public sealed record <NomType>Config
{
    public string? Preset { get; init; }

    // Champs du circuit breaker (communs à tous les types)
    public int? MaxTransitions { get; init; }
    public int? StateTimeoutSeconds { get; init; }
    public int? MaxStateVisits { get; init; }
    public int? MaxTotalDurationSeconds { get; init; }
    public bool? UseDegradedMode { get; init; }

    // Champs spécifiques au type (guards)
    public int? MaxRetries { get; init; }
    // ... adapter selon le type
}
```

**Modify** `CrewConfiguration.cs` (which also contains `TaskConfiguration`):

```csharp
// Dans le record CrewConfiguration (même fichier) :
public <NomType>Config? <NomType> { get; init; }

// Dans le record TaskConfiguration (même fichier CrewConfiguration.cs) :
public <NomType>Config? <NomType> { get; init; }
```

> **Note**: `TaskConfiguration` is defined in the same file as `CrewConfiguration`
> (`src/core/Orkeon.Domain/Configuration/CrewConfiguration.cs`). There is no separate
> `TaskConfiguration.cs` file.

---

## Step 4 — Infrastructure: YAML → Domain bridge

**File**: `src/core/Orkeon.Infrastructure/Configuration/<NomType>PolicyFactory.cs`

```csharp
namespace Orkeon.Infrastructure.Configuration;

public static class <NomType>PolicyFactory
{
    /// <summary>
    /// Résolution hiérarchique : task override → crew default → preset → Strict fallback.
    /// </summary>
    public static <NomType>Policy Resolve(
        <NomType>Config? crewDefault,
        <NomType>Config? taskOverride)
    {
        // 1. Déterminer le preset de base
        // 2. Appliquer les overrides crew-level
        // 3. Appliquer les overrides task-level (écrase crew)
        // 4. Convertir int seconds → TimeSpan
    }

    /// <summary>
    /// Crée une FSM spécialisée (retour type concret, pas générique).
    /// Le nom de la méthode doit refléter le scope : CreateTaskFsm, CreateCrewFsm, etc.
    /// </summary>
    public static StateMachine<TaskExecutionState, TaskExecutionEvent> Create<Scope>Fsm(
        <NomType>Config? crewDefault,
        <NomType>Config? taskOverride)
    {
        var policy = Resolve(crewDefault, taskOverride);
        return <Scope><NomType>Machine.Create(policy);
    }

    /// <summary>
    /// Construit le guard context avec fusion hiérarchique des limites.
    /// Le task override prend les champs non-null, sinon crew default, sinon valeur par défaut du record.
    /// </summary>
    public static <Scope><NomType>Machine.<Scope>GuardContext CreateGuardContext(
        <NomType>Config? crewDefault,
        <NomType>Config? taskOverride)
    {
        var effective = taskOverride ?? crewDefault;
        return new <Scope><NomType>Machine.<Scope>GuardContext
        {
            MaxRetries = effective?.MaxRetries ?? 3,
            // ... autres champs avec fallback par défaut
            IsToolRegistered = true, // Concern runtime, pas config
        };
    }
}

// --- Exemple réel (CircuitBreakerPolicyFactory) ---
// La méthode `CreateTaskFsm` retourne un type concret, pas générique.
// La méthode `ApplyOverrides` est private et gère la conversion int seconds → TimeSpan.
```

### Resolution hierarchy (invariant across all types)

```
1. Task-level config       (priorité haute — champs non-null écrasent)
2. Crew-level config       (défaut — champs non-null écrasent le preset)
3. Preset nommé            (si spécifié dans Preset — base de valeurs)
4. <NomType>Policy.Strict  (fallback si rien n'est configuré)
```

---

## Step 5 — Infrastructure: YAML parsing

**Modified file**: `src/core/Orkeon.Infrastructure/Configuration/YamlCrewDefinitionLoader.cs`

### 5.1 Add the YAML class

```csharp
private sealed class <NomType>YamlConfig
{
    [YamlMember(Alias = "preset")]
    public string? Preset { get; set; }

    [YamlMember(Alias = "maxTransitions")]
    public int? MaxTransitions { get; set; }

    // ... tous les champs avec [YamlMember(Alias = "camelCase")]
}
```

### 5.2 Add the property on the THREE YAML models

The `YamlCrewDefinitionLoader` uses different models for single-file and multi-file loading:

```csharp
// Dans CrewYamlConfig (chargement single-file config.yaml) :
[YamlMember(Alias = "<nomType>")]     // ex: "stateGraph", "agenticSwarm"
public <NomType>YamlConfig? <NomType> { get; set; }

// Dans CrewSettingsYamlConfig (chargement multi-file : crew.yaml) :
[YamlMember(Alias = "<nomType>")]
public <NomType>YamlConfig? <NomType> { get; set; }

// Dans TaskYamlConfig (utilisé dans les deux modes) :
[YamlMember(Alias = "<nomType>")]
public <NomType>YamlConfig? <NomType> { get; set; }
```

> **Warning**: There are THREE YAML models to modify, not two. `CrewSettingsYamlConfig` is
> used only in multi-file mode (folder with `crew.yaml` + `agents.yaml` + `tasks.yaml`).

### 5.3 Add the mapping

```csharp
private static <NomType>Config? Map<NomType>(<NomType>YamlConfig? yaml)
{
    if (yaml is null) return null;
    return new <NomType>Config
    {
        Preset = yaml.Preset,
        MaxTransitions = yaml.MaxTransitions,
        // ...
    };
}
```

### 5.4 Wire into the loading methods

Add `<NomType> = Map<NomType>(...)` in:

- `MapTasks()` — for the task level
- `LoadFromStringAsync()` — for the crew level (single-file)
- `LoadFromDirectoryCoreAsync()` — for the crew level (multi-file)

---

## Step 6 — Tests

**Folder**: `tests/core/Orkeon.Domain.Tests/Common/<NomType>/`

### 6.1 Generic framework tests

| File | Minimum tests |
|---------|--------------|
| `<NomType>Tests.cs` | Correct initial state, valid operation, full cycle, invalid operation throws, TryFire false, terminal blocked, CanFire, OnTransition event |
| `<NomType>GuardTests.cs` | Guard true allowed, guard false blocked, multiple guards priority, action executed |
| `<NomType>CircuitBreakerTests.cs` | Max transitions trip, cycle detection, degraded mode, OnCircuitBroken, reset, histogram |

### 6.2 Specialization tests

| File | Minimum tests |
|---------|--------------|
| `<Scope><NomType>MachineTests.cs` | Full happy path, each guard individually (budget, tool, retries, validation), cancel from any state, circuit breaker in a loop |

### 6.3 Infrastructure tests (optional but recommended)

| File | Minimum tests |
|---------|--------------|
| `<NomType>PolicyFactoryTests.cs` | Preset-only resolution, crew override, task override overrides crew, Strict fallback |

### Test command

```bash
dotnet test tests/core/Orkeon.Domain.Tests/ --filter "FullyQualifiedName~<NomType>"
```

---

## Step 7 — Example

**Folder**: `examples/06-engineering-devops/<NumExemple>-<nom-court>/`

### 7.1 Copy an existing example

Start from example 102 or 103 as the base, then:

1. Duplicate the `config.yaml`
2. Add/replace the configuration block for the new type
3. Adapt tasks and agents if necessary

### 7.2 config.yaml structure

```yaml
# === Bloc de configuration du nouveau type au niveau crew ===
<nomType>:                          # ex: stateGraph, agenticSwarm
  preset: "strict"
  useDegradedMode: true
  maxRetries: 3
  # ... champs spécifiques au type

agents:
  - role: "..."
    # ...

tasks:
  - id: task_1
    description: "..."
    # === Override au niveau task ===
    <nomType>:
      maxTransitions: 200
      stateTimeoutSeconds: 600
      # ...
```

### 7.3 Example README

README structure:

```markdown
# Exemple <Num> — <Titre>

## Objectif
Quoi et pourquoi.

## Diagramme
ASCII ou Mermaid du graphe/workflow.

## Configuration YAML
Bloc annoté avec les valeurs clés.

## Diff avec l'exemple <Num-1>
Ce qui change par rapport à l'exemple précédent.

## Exécution
Commande pour lancer l'exemple.
```

---

## Step 8 — Documentation

**File**: `docs/orchestration/<nom-type>.md`

### 8.1 Mandatory document structure

```markdown
# Orchestration par <NomType>

## Vue d'ensemble
Paragraphe introductif : quoi, pourquoi, où dans l'architecture.

## Architecture

### Couche Domain — Framework générique
Table : Classe | Rôle (pointer vers les fichiers)

### Couche Domain — Spécialisation <Scope>
Table : Classe | Rôle

### Couche Infrastructure — Intégration YAML
Table : Classe | Rôle

### Couche Domain — Configuration
Table : Classe | Rôle

## Graphe / Diagramme
ASCII art du graphe d'états ou du workflow.
Lister les états terminaux.

## Circuit breaker / Mécanismes de protection
Table : Mécanisme | Paramètre | Description
Expliquer les deux modes (exception vs dégradé).

## Presets
Table : Preset | Valeurs (reprendre Strict, Default, Permissive)

## Observabilité
Événements exposés, contenu du status/snapshot.

## Guards types
Table : Guard | Transition/Nœud protégé | Condition
Expliquer le guard anti-hallucination.

## Configuration YAML

### Schéma
Bloc YAML annoté avec types et descriptions.

### Hiérarchie de résolution
Diagramme ASCII : task → crew → preset → fallback.

### Modèles YAML
Table : Modèle C# | Classe YAML | Fichier

## Utilisation en code C#

### Création manuelle (Fluent Builder)
Exemple complet avec observabilité et guard context.

### Création depuis la configuration YAML
Exemple avec la PolicyFactory.

### Construction custom
Exemple avec le builder générique.

## Exemple <Num>
Référence vers l'exemple, résumé des ajouts.

## Relation avec l'existant

### StateTransitionManager
Explication de la complémentarité (lifecycle vs runtime).

### SequentialCrewOrchestrator
Explication du point d'intégration (IProcessStrategy).

### <Autres types d'orchestration existants>
Comment ce type coexiste avec les précédents.

## Tests
Table : Fichier | Couverture
Commande de test.
```

### 8.2 Update the existing files

1. **`docs/INDEX.md`**:
   - Add a line in the Orchestration section
   - Add the file to the "Découverte" reading path

2. **`docs/architecture/yaml-schema.md`**:
   - Add the YAML schema of the new configuration block
   - Document the presets and default values

3. **`docs/orchestration/process-types.md`**:
   - Add the new type to the comparison matrix
   - Add a dedicated section with pros/cons/use cases
   - Update the decision tree

---

## Stability criteria (final checklist)

Before considering the implementation complete, verify:

| Criterion | Expected mechanism | Verified |
|---------|-------------------|---------|
| Recursive loops | Circuit breaker (4 mechanisms minimum) + typed guards | [ ] |
| Tool hallucinations | `IsToolRegistered` guard or equivalent | [ ] |
| Observability | Events + snapshot/status with histogram | [ ] |
| Interruptibility | Cancel from any non-terminal state | [ ] |
| Multi-model determinism | Per-task configurable limits (Flash ≠ Opus) | [ ] |
| 2-level YAML config | Crew default + task override | [ ] |
| Degraded mode | Auto transition to a safe state instead of an exception | [ ] |
| Tests | 30+ tests covering framework + specialization | [ ] |
| Documentation | Complete doc + index + features updated | [ ] |
| Example | Working config.yaml with README | [ ] |
