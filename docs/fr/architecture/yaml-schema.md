> 🇬🇧 [English version](../../architecture/yaml-schema.md)

# Référence YAML — Schéma complet

Ce document est la **source unique de vérité** pour le schéma YAML d'Orkeon. Les documents spécialisés (FSM, Graph, Autonomous) renvoient ici pour le schéma.

## Schéma de configuration complet

La structure YAML suit ce schéma :

```yaml
# Schéma complet CrewYamlConfig
name: string              # Identifiant de la crew
goal: string              # Objectif (requis)
process: string           # "sequential" | "hierarchical" | "parallel" | "consensual" | "graph" | "autonomous"
verbose: bool             # default: false
memory: bool              # default: false
memoryProvider: string    # "InMemory" | "Redis" | "Sqlite" | "ChromaDb" | "Pinecone" | "LanceDb"
planning: bool            # default: false
managerAgent: string      # Requis si process = "hierarchical"

rag:                      # Configuration RAG au niveau crew (optionnel)
  provider: string        # Store mémoire/vectoriel des collections ("InMemory" | "Redis" | "Sqlite" | "ChromaDb" | "Pinecone" | "LanceDb")
  collections:
    <nom_collection>:
      sources: [string]   # Sources d'ingestion (globs de fichiers ou répertoires), résolues au kickoff
      chunking:
        strategy: string  # default: "recursive"
        max_tokens: int   # default: 512 — tokens par chunk
        overlap: int      # default: 64 — recouvrement de tokens entre chunks
  defaults:
    profile: string       # Profil de requête par défaut des attachements sans profil propre

agents:
  <agent_id>:             # Clé = identifiant unique de l'agent
    role: string          # Rôle de l'agent (requis)
    goal: string          # Objectif personnel de l'agent (requis)
    backstory: string     # Contexte et expertise (multi-ligne recommandé)
    tools: [string]       # Noms d'outils enregistrés dans IToolRegistry
    allowDelegation: bool # default: true — permet la délégation à d'autres agents
    maxIter: int          # default: 20 — itérations maximales avant timeout
    maxRpm: int           # default: 10 — requêtes par minute (rate limiting)
    verbose: bool         # default: false — logs détaillés pour cet agent
    llm:
      model: string       # Modèle LLM ("gpt-4", "claude-3-opus", etc.)
      temperature: float  # Créativité (0.0-1.0)
      maxTokens: int      # Limite de tokens en sortie
    knowledge:            # Collections de connaissance (RAG) attachées à l'agent (optionnel)
      - string            # Forme courte : nom de collection avec options par défaut
      - collection: string       # Forme longue (clé requise)
        top_k: int               # default: 5 — chunks retenus par requête
        min_score: float         # Score de pertinence minimal dans [0, 1]
        profile: string          # Profil de requête ("fast" | "balanced" | "quality", libre)
        max_context_tokens: int  # Plafond de tokens de contexte injectés

tasks:
  <task_id>:              # Clé = identifiant unique de la tâche
    description: string   # Description détaillée de la tâche (requis)
    expectedOutput: string # Format/contenu attendu en résultat (requis)
    agent: string         # ID de l'agent assigné à la tâche
    dependencies: [string] # IDs des tâches prérequises (garantit l'ordre)
    asyncExecution: bool  # default: false — exécution asynchrone
    humanInput: bool      # default: false — demande intervention humaine
    context: {key: value} # Données additionnelles de contexte
    circuitBreaker:       # Configuration FSM / circuit breaker (optionnel)
      preset: string      # "strict" | "permissive" | "default"
      maxTransitions: int # Transitions max avant trip
      stateTimeoutSeconds: int  # Timeout par état (secondes)
      maxStateVisits: int       # Visites max d'un même état (cycles)
      maxTotalDurationSeconds: int # Durée totale max (secondes)
      useDegradedMode: bool     # true = Degraded, false = exception
      maxRetries: int           # Retries après échec
      maxToolCallsPerRound: int # Tool calls max par round
      maxValidationRetries: int # Boucles validation max
```

Le bloc `circuitBreaker` est également utilisable au niveau racine du YAML (défaut pour toutes les tâches).

## Configuration Knowledge & RAG

Deux blocs complémentaires (RAG-03/C4). Le bloc crew `rag:` déclare les **collections**
(provider, sources d'ingestion, chunking, défauts globaux). Le bloc agent `knowledge:`
**attache** des collections à un agent avec ses options de récupération. À l'assemblage du
contexte d'exécution, les collections attachées sont interrogées avec l'entrée de la tâche et
les résultats injectés dans les prompts de l'agent (le câblage prompt arrive dans un lot
ultérieur ; le parsing est pleinement fonctionnel dès aujourd'hui — aucune ingestion n'est
déclenchée au chargement).

Forme courte — attacher des collections avec les options par défaut :

```yaml
rag:
  collections:
    produits:
      sources: ["./data/catalogue/**/*.pdf", "./data/faq.md"]

agents:
  support:
    role: "Agent support client"
    goal: "Répondre aux questions produits"
    knowledge: [produits, procedures]
```

Forme longue — options de récupération par collection (mixable avec la forme courte dans la
même liste) :

```yaml
rag:
  provider: Sqlite
  collections:
    procedures:
      sources: ["./docs/procedures/"]
      chunking: { strategy: recursive, max_tokens: 512, overlap: 64 }
  defaults: { profile: balanced }

agents:
  expert:
    role: "Expert métier"
    goal: "Fournir des réponses sourcées"
    knowledge:
      - collection: procedures
        profile: quality
        top_k: 8
        min_score: 0.35
        max_context_tokens: 1500
```

Les clés acceptent snake_case (canonique) et camelCase. Une entrée `knowledge` malformée
(`collection` manquante, `top_k` non numérique, …) est ignorée ou dégradée avec un warning —
elle ne fait jamais planter le loader. L'équivalent fluent est
`AgentBuilder.WithKnowledge("produits")` /
`WithKnowledge("produits", opts => { opts.TopK = 8; opts.Profile = "quality"; })` (cumulable).

## Configuration Graph

Quand `process: "graph"` est utilisé, un bloc `graphConfig` supplémentaire configure le moteur de graphe d'état :

```yaml
graphConfig:
  maxRetryCycles: int           # default: 2 — cycles de retry pour tâches échouées
  circuitBreakerPreset: string  # "strict" | "permissive" | "default"
  maxTransitions: int           # Surcharge le preset
  maxStateVisits: int           # Détection de cycles (surcharge le preset)
  maxTotalDurationSeconds: int  # Durée totale en secondes (surcharge le preset)
```

## Configuration Circuit Breaker

Le bloc `circuitBreaker` avec tous les paramètres disponibles :

```yaml
circuitBreaker:
  preset: string                # "strict" | "permissive" | "default"
  maxTransitions: int           # Transitions max avant trip
  stateTimeoutSeconds: int      # Timeout par état (secondes)
  maxStateVisits: int           # Visites max d'un même état (cycles)
  maxTotalDurationSeconds: int  # Durée totale max (secondes)
  useDegradedMode: bool         # true = Degraded, false = exception
  maxRetries: int               # Retries après échec
  maxToolCallsPerRound: int     # Tool calls max par round
  maxValidationRetries: int     # Boucles validation max
```

## Configuration Autonomous Budget

Quand `process: "autonomous"` est utilisé, un bloc `autonomousBudget` optionnel configure le budget d'exécution multi-dimensions :

```yaml
autonomousBudget:              # ← optionnel, défauts si absent
  maxToolCalls: int            # Nombre max d'appels outils (default: 15)
  maxDelegationDepth: int      # Profondeur max de delegation recursive (default: 2)
  maxWallTime: string          # Temps reel maximum au format HH:MM:SS (default: "00:05:00")
  maxTokensConsumed: int       # Tokens totaux - prompt + completion (default: 16000)
  maxSpawnedAgents: int        # Nombre max de sous-agents crees (default: 3)
  preset: string               # "strict" | "default" | "permissive" (surcharge les valeurs ci-dessus)
```

**Presets prédéfinis** :

- **strict** : MaxToolCalls=8, MaxDelegationDepth=1, MaxWallTime=2min, MaxTokens=8000, MaxSpawns=1
- **default** : MaxToolCalls=15, MaxDelegationDepth=2, MaxWallTime=5min, MaxTokens=16000, MaxSpawns=3
- **permissive** : MaxToolCalls=50, MaxDelegationDepth=4, MaxWallTime=15min, MaxTokens=64000, MaxSpawns=10

> **Note** : le parsing YAML de `autonomousBudget` n'est pas encore implémenté dans le loader. En l'absence de ce bloc, `AgentExecutionBudget.Default` est utilisé. Les presets sont fonctionnels via l'API C# (`AgentExecutionBudget.Strict`, `.Default`, `.Permissive`).

## Modèles YAML

Les modèles YAML incluent :
- `CrewYamlConfig` (définition complète d'une crew)
- `AgentYamlConfig` (rôle, objectif, backstory, outils, limites)
- `TaskYamlConfig` (description, résultat attendu, dépendances, circuit breaker)
- `LlmYamlConfig` (modèle, température, max tokens)
- `CircuitBreakerYamlConfig` (preset, seuils, guards)
- `GraphYamlConfig` (maxRetryCycles, circuitBreakerPreset, surcharges)
- `AutonomousBudgetYamlConfig` (presets, limites multi-dimensions)
- `RagYamlConfig` (provider, collections + sources/chunking, défauts) → `RagCrewConfig`
- `AgentYamlConfig.Knowledge` (entrées forme courte/longue) → `KnowledgeAttachment`

Des configurations prédéfinies sont disponibles via `OrkeonConfig` : `Default`, `Development` (debug activé, InMemory), `Production` (debug désactivé, Redis).

---

> **Voir aussi** : [YAML et Builders](../getting-started/yaml-and-builders.md) · [Orchestration FSM](../orchestration/fsm.md) · [Orchestration Graph](../orchestration/graph.md) · [Orchestration Autonome](../orchestration/autonomous.md) · [Retour à l'index](../INDEX.md)
