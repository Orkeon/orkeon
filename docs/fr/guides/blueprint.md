> 🇬🇧 [English version](../../guides/blueprint.md)

> **Voir aussi** : [Guide ProcessTypes](../orchestration/process-types.md) · [Retour à l'index](../INDEX.md)

# Blueprint — Ajout d'un type d'orchestration dans Orkeon

Ce document sert de guide reproductible pour implémenter et documenter un nouveau type d'orchestration. Il s'appuie sur le précédent de la FSM (voir [Orchestration FSM](../orchestration/fsm.md)) et couvre les 8 étapes nécessaires, de la couche Domain jusqu'à la documentation finale.

---

## Prérequis

Avant de commencer, identifier :

- **Nom du type** : ex. `StateGraph`, `AgenticSwarm`, `PipelineDAG`
- **Numéro de doc** : créer un fichier dans `docs/orchestration/` (ex. `docs/orchestration/nouveau-type.md`)
- **Numéro d'exemple** : prochain index libre dans `examples/` (ex. `104`)
- **Scope initial** : où le nouveau type s'applique en premier (Task, Crew, Flow)

---

## Étape 1 — Domain : Framework générique

**Dossier** : `src/core/Orkeon.Domain/Common/<NomType>/`

Créer les fichiers suivants (adapter les noms de classes) :

| Fichier | Rôle | Modèle de référence |
|---------|------|---------------------|
| `I<NomType>.cs` | Interfaces lecture seule + mutation dans un seul fichier | `IStateMachine.cs` (contient `IStateMachine` + `IMutableStateMachine`) |
| `<NomType>.cs` | Moteur principal thread-safe | `StateMachine.cs` |
| `<NomType>Builder.cs` | API fluent pour déclarer le graphe/config | `StateMachineBuilder.cs` |
| `<NomType>Policy.cs` | Record immutable de configuration avec presets + `CircuitBreakerStatus` | `CircuitBreakerPolicy.cs` |
| `<NomType>Result.cs` | Record immutable du résultat d'opération | `TransitionResult.cs` |
| `<NomType>Exceptions.cs` | Exceptions spécifiques au type | `StateMachineExceptions.cs` |
| `TransitionDefinition.cs` | Classe `internal sealed` pour une transition unitaire (from, trigger, to, guard, action) | `TransitionDefinition.cs` |

> **Note** : Les deux interfaces (lecture seule et mutation) sont dans un seul fichier par convention.
> La classe `TransitionDefinition` est `internal` et exposée aux tests via `[assembly: InternalsVisibleTo]`.

### Checklist technique

- [ ] Thread-safety : `lock` ou `SemaphoreSlim` selon le besoin async
- [ ] Presets statiques : `Strict`, `Default`, `Permissive` (immutables)
- [ ] Événements : au minimum `OnTransition` et `OnCircuitBroken` (ou équivalent)
- [ ] `CircuitBreakerStatus` ou équivalent avec snapshot complet pour observabilité
- [ ] Pas de dépendance externe (pure Domain)
- [ ] `[assembly: InternalsVisibleTo("Orkeon.Domain.Tests")]` si classes internal

### Patron de code — Moteur

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

### Patron de code — Builder fluent

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

## Étape 2 — Domain : Spécialisation

**Dossier** : `src/core/Orkeon.Domain/<Scope>/` (ex. `Task/`, `Crew/`, `Flow/`)

| Fichier | Rôle |
|---------|------|
| `<Scope><NomType>State.cs` | Les DEUX enums (états + événements) dans un seul fichier | 
| `<Scope><NomType>Machine.cs` | Factory statique `Create()` + `CreateBuilder()` + `GuardContext` record |

> **Convention FSM** : Dans l'implémentation existante, `TaskExecutionState.cs` contient à la fois
> l'enum `TaskExecutionState` et l'enum `TaskExecutionEvent`. Regrouper les deux dans un seul
> fichier quand ils sont toujours utilisés ensemble.

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

### Guards anti-hallucination (obligatoires)

Chaque type d'orchestration DOIT implémenter au minimum :

| Guard | But | Exemple FSM |
|-------|-----|-------------|
| Budget d'opérations | Limiter les appels coûteux | `ToolCallCount < MaxToolCallsPerRound` |
| Outil enregistré | Bloquer les outils hallucination | `IsToolRegistered == true` |
| Limite de retries | Éviter les boucles infinies | `RetryCount < MaxRetries` |

---

## Étape 3 — Domain : Configuration DTO

**Fichier** : `src/core/Orkeon.Domain/Configuration/<NomType>Config.cs`

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

**Modifier** `CrewConfiguration.cs` (qui contient aussi `TaskConfiguration`) :

```csharp
// Dans le record CrewConfiguration (même fichier) :
public <NomType>Config? <NomType> { get; init; }

// Dans le record TaskConfiguration (même fichier CrewConfiguration.cs) :
public <NomType>Config? <NomType> { get; init; }
```

> **Note** : `TaskConfiguration` est défini dans le même fichier que `CrewConfiguration`
> (`src/core/Orkeon.Domain/Configuration/CrewConfiguration.cs`). Il n'y a pas de fichier
> `TaskConfiguration.cs` séparé.

---

## Étape 4 — Infrastructure : Bridge YAML → Domain

**Fichier** : `src/core/Orkeon.Infrastructure/Configuration/<NomType>PolicyFactory.cs`

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
    public static <Scope>GuardContext CreateGuardContext(
        <NomType>Config? crewDefault,
        <NomType>Config? taskOverride)
    {
        var effective = taskOverride ?? crewDefault;
        return new <Scope>GuardContext
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

### Hiérarchie de résolution (invariante pour tous les types)

```
1. Task-level config       (priorité haute — champs non-null écrasent)
2. Crew-level config       (défaut — champs non-null écrasent le preset)
3. Preset nommé            (si spécifié dans Preset — base de valeurs)
4. <NomType>Policy.Strict  (fallback si rien n'est configuré)
```

---

## Étape 5 — Infrastructure : Parsing YAML

**Fichiers modifiés** : `src/core/Orkeon.Infrastructure/Configuration/Yaml/YamlConfigModels.cs`
(les modèles) et `Configuration/Yaml/YamlCrewMapper.cs` (le mapping) — le loader
(`YamlCrewDefinitionLoader.cs`) ne fait que désérialiser et déléguer.

### 5.1 Ajouter la classe YAML

Pas d'attributs `[YamlMember]` — les modèles existants n'en portent **aucun** : la
résolution des clés est par convention (`CamelCaseNamingConvention` plus le repli
snake_case du `CamelOrSnakeCaseTypeInspector` dans `YamlDotNetSerializer`). Suivez la
forme des modèles livrés — publics, non-sealed, dans le `YamlConfigModels.cs` partagé :

```csharp
public class <NomType>YamlConfig
{
    public string? Preset { get; set; }         // accessible en preset:
    public int? MaxTransitions { get; set; }    // accessible en maxTransitions: ou max_transitions:
    // ... propriétés simples, camelCase/snake_case tous deux acceptés par convention
}
```

### 5.2 Ajouter la propriété sur les TROIS modèles YAML

Le `YamlCrewDefinitionLoader` utilise des modèles différents pour le chargement single-file et multi-file :

```csharp
// Dans CrewYamlConfig (chargement single-file config.yaml) :
public <NomType>YamlConfig? <NomType> { get; set; }   // clé : <nomType> par convention

// Dans CrewSettingsYamlConfig (chargement multi-file : crew.yaml) :
public <NomType>YamlConfig? <NomType> { get; set; }

// Dans TaskYamlConfig (utilisé dans les deux modes) :
public <NomType>YamlConfig? <NomType> { get; set; }
```

> **Attention** : Il y a TROIS modèles YAML à modifier, pas deux. `CrewSettingsYamlConfig` est
> utilisé uniquement en mode multi-file (dossier avec `crew.yaml` + `agents.yaml` + `tasks.yaml`).

### 5.3 Ajouter le mapping

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

### 5.4 Câbler dans les méthodes de chargement

Ajouter `<NomType> = Map<NomType>(...)` :

- dans `YamlCrewMapper.MapTasks()` — pour le niveau task ;
- les valeurs de niveau crew transitent par `CrewMappingSettings`, que le loader
  remplit dans `LoadFromStringAsync()` (single-file) et
  `LoadFromDirectoryCoreAsync()` (multi-file) avant de déléguer au mapper.

---

## Étape 6 — Tests

**Dossier** : `tests/core/Orkeon.Domain.Tests/Common/<NomType>/`

### 6.1 Tests du framework générique

| Fichier | Tests minimum |
|---------|--------------|
| `<NomType>Tests.cs` | État initial correct, opération valide, cycle complet, opération invalide throw, TryFire false, terminal bloqué, CanFire, événement OnTransition |
| `<NomType>GuardTests.cs` | Guard true autorisé, guard false bloqué, priorité guards multiples, action exécutée |
| `<NomType>CircuitBreakerTests.cs` | Max transitions trip, détection cycles, mode dégradé, OnCircuitBroken, reset, histogramme |

### 6.2 Tests de la spécialisation

| Fichier | Tests minimum |
|---------|--------------|
| `<Scope><NomType>MachineTests.cs` | Happy path complet, chaque guard individuellement (budget, outil, retries, validation), cancel depuis tout état, circuit breaker en boucle |

### 6.3 Tests infrastructure (optionnel mais recommandé)

| Fichier | Tests minimum |
|---------|--------------|
| `<NomType>PolicyFactoryTests.cs` | Résolution preset seul, crew override, task override écrase crew, fallback Strict |

### Commande de test

```bash
dotnet test tests/core/Orkeon.Domain.Tests/ --filter "FullyQualifiedName~<NomType>"
```

---

## Étape 7 — Exemple

**Dossier** : `examples/06-engineering-devops/<NumExemple>-<nom-court>/`

### 7.1 Copier un exemple existant

Partir de l'exemple 102 ou 103 comme base, puis :

1. Dupliquer le `config.yaml`
2. Ajouter/remplacer le bloc de configuration du nouveau type
3. Adapter les tâches et agents si nécessaire

### 7.2 Structure du config.yaml

```yaml
# === Bloc de configuration du nouveau type au niveau crew ===
<nomType>:                          # ex: stateGraph, agenticSwarm
  preset: "strict"
  useDegradedMode: true
  maxRetries: 3
  # ... champs spécifiques au type

agents:
  mon_agent:              # mapping indexé par id d'agent — jamais une séquence
    role: "..."
    # ...

tasks:
  task_1:                 # l'id EST la clé (TaskYamlConfig n'a pas de champ id:)
    description: "..."
    # === Override au niveau task ===
    <nomType>:
      maxTransitions: 200
      stateTimeoutSeconds: 600
      # ...
```

### 7.3 README de l'exemple

Structure du README :

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

## Étape 8 — Documentation

**Fichier** : `docs/orchestration/<nom-type>.md`

### 8.1 Structure obligatoire du document

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

### 8.2 Mettre à jour les fichiers existants

1. **`docs/INDEX.md`** :
   - Ajouter une ligne dans la section Orchestration
   - Ajouter le fichier dans le parcours "Découverte"

2. **`docs/architecture/yaml-schema.md`** :
   - Ajouter le schéma YAML du nouveau bloc de configuration
   - Documenter les presets et valeurs par défaut

3. **`docs/orchestration/process-types.md`** :
   - Ajouter le nouveau type dans la matrice de comparaison
   - Ajouter une section dédiée avec pros/cons/cas d'usage
   - Mettre à jour l'arbre de décision

---

## Critères de stabilité (checklist finale)

Avant de considérer l'implémentation terminée, vérifier :

| Critère | Mécanisme attendu | Vérifié |
|---------|-------------------|---------|
| Boucles récursives | Circuit breaker (4 mécanismes minimum) + guards types | [ ] |
| Hallucinations outils | Guard `IsToolRegistered` ou équivalent | [ ] |
| Observabilité | Événements + snapshot/status avec histogramme | [ ] |
| Interruptibilité | Cancel depuis tout état non-terminal | [ ] |
| Déterminisme multi-modèles | Limites configurables par task (Flash ≠ Opus) | [ ] |
| Config YAML 2 niveaux | Crew default + task override | [ ] |
| Mode dégradé | Transition auto vers état safe au lieu d'exception | [ ] |
| Tests | 30+ tests couvrant framework + spécialisation | [ ] |
| Documentation | Doc complète + index + features mis à jour | [ ] |
| Exemple | config.yaml fonctionnel avec README | [ ] |
