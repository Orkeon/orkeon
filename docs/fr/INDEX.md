> 🇬🇧 [English version](../INDEX.md)

# Documentation Orkeon

## Structure

La documentation est organisée en 6 sections thématiques.

### Démarrage

| Fichier | Description |
|---------|-------------|
| [Trois façons d'exécuter Orkeon](./getting-started/three-ways-to-run-orkeon.md) | Page centrale : depuis les sources vs binaire de release vs conteneur — prérequis, commandes, canaux d'installation par OS et tableau comparatif |
| [Exécuter votre premier exemple (depuis les sources)](./getting-started/run-your-first-example.md) | Première exécution de bout en bout : prérequis, matrice de profils LLM, la commande complète, chaque flag du runner, résolution des settings, dépannage |
| [Vue d'ensemble](./getting-started/overview.md) | Architecture, concepts fondamentaux (Agent, Task, Tool, Crew), YAML vs Fluent Builder |
| [Bootstrap et exécution](./getting-started/bootstrap.md) | Injection de dépendances, exécution d'une Crew, modes batch/streaming/fire-and-forget |
| [YAML, Builders et CrewFactory](./getting-started/yaml-and-builders.md) | Fluent Builders, schéma YAML, pipeline CrewFactory, modes de chargement |

### Architecture

| Fichier | Description |
|---------|-------------|
| [Fournisseurs LLM](./architecture/llm-providers.md) | 12 providers (OpenAI, Anthropic, Azure, Groq, Ollama, etc.), adaptateurs, factory, kit de campagnes sur API réelle |
| [Système de mémoire](./architecture/memory-system.md) | 5 types de mémoire, 6 providers (InMemory, Redis, SQLite, ChromaDB, Pinecone, LanceDB), mémoire cognitive |
| [Événements, CQRS et observabilité](./architecture/domain-events.md) | 41 domain events, pipeline CQRS, callbacks à 2 niveaux |
| [EventHub et cycle de vie des crews](./architecture/event-hub-and-crew-lifecycle.md) | Spécification de référence du messaging inter-agents et inter-crews (EventHub) et de la mise en sommeil/réveil des crews — ports Application, adapters InMemory + SQLite |
| [Sécurité, résilience et plugins](./architecture/security.md) | 7 couches de sécurité, politiques Polly, checkpointing, système de plugins |
| [Conformité VFS](./architecture/vfs-compliance.md) | Principe VFS-only (tout I/O via `IFileSystemService`) : analyseur Roslyn `Orkeon.Compliance.Vfs`, 5 diagnostics, périmètres exemptés, critères de sortie de la migration |
| [Système de plugins](./architecture/plugins.md) | Contrat `IOrkeonPlugin`, découverte VFS, isolation `AssemblyLoadContext`, activation opt-in `AddOrkeonPlugins`, ⚠️ frontière de confiance |
| [DSL de scripting](./architecture/scripting.md) | DSL à syntaxe TypeScript (`.ork.ts`) : transpilation esbuild, exécution sandboxée Jint, toute la surface Orkeon (agents, crews, tools, FSM, graphes, événements) via builders fluides |
| [Commandes CLI TypeScript](./architecture/cli-ts-commands.md) | Commandes REPL interactives en `*.cmd.ts` (`defineCommand`) chargées au démarrage sans recompilation .NET, avec dispatch de travail vers les agents |
| [Coding agent TypeScript](./architecture/coding-agent-ts.md) | Agent de codage agentique construit sur la pile scriptée : plan de contrôle `*.cmd.ts` vs moteur `crew.ork.ts`, outils C# `ToolBase` |
| [Référence YAML](./architecture/yaml-schema.md) | **Source unique** du schéma YAML complet (crew, agents, tasks, circuitBreaker, graphConfig, autonomousBudget) |
| [RaggableTree — graphe sémantique](./architecture/raggable-tree.md) | Pipeline 6 phases, 15 tools, 5 langages, réindexation incrémentale, watcher, injection de contexte |
| [Pipeline RAG](./architecture/rag-pipeline.md) | Le sous-système `src/rag/` : ingestion, pipeline à 7 étages (transform → retrieve → fuse/MMR → rerank → assemble → generate → groundedness), graphe correctif CRAG, repli web, 5 profils, évaluation mesurée |
| [ADR — RaggableTree](./architecture/raggable-tree-adr.md) | Décision graphe stratifié à 6 niveaux via Tree-sitter, alternatives rejetées, conséquences |
| [Décisions d'architecture (ADR)](./adr/) | ADR-002 (shared kernel Tools.Abstractions), ADR-003 (shared kernels Analysis), ADR-004 (jumeaux de nommage scripting — remplacé par l'ADR-007), ADR-005 (famille Tools.* hétérogène), ADR-006 (sous-système RAG `src/rag/`), ADR-007 (D3 : renommage `Orkeon.Cli.Commands.Scripting`) |

### Orchestration

| Fichier | Description |
|---------|-------------|
| [Guide comparatif ProcessTypes](./orchestration/process-types.md) | Les 6 stratégies côte à côte : matrice, arbre de décision, pros/cons, coûts |
| [FSM — Machine à états](./orchestration/fsm.md) | Orchestration intra-tâche, circuit breaker à 4 mécanismes, presets, guards |
| [Graph — Graphe d'états](./orchestration/graph.md) | Orchestration inter-tâches LangGraph-style, edges conditionnels, cycles contrôlés, retry |
| [Autonomous — Auto-organisation](./orchestration/autonomous.md) | Budget multi-dimensions, délégation récursive, spawn dynamique, communication A2A |

### Outils

| Fichier | Description |
|---------|-------------|
| [Inventaire des outils](./tools/inventory.md) | 36+ outils par catégorie, résolution YAML, enregistrement DI, gaps identifiés |
| [Créer un nouvel outil](./tools/new-tool-pattern.md) | Pipeline typé, attributs FieldSchema/ReturnSchema, pattern composition, enregistrement |

### Guides

| Fichier | Description |
|---------|-------------|
| [Méthodologie de portage](./guides/porting-methodology.md) | 5 étapes pour migrer une application, YAML-first vs Code-first, estimation effort |
| [Exemple de portage](./guides/porting-example.md) | Pipeline e-commerce complet : analyse, mapping agents, YAML, bootstrap C# |
| [Blueprint nouvelle orchestration](./guides/blueprint.md) | Template 8 étapes pour ajouter un nouveau ProcessType au framework |
| [Contenu multi-modal (vision)](./guides/multimodal.md) | Vision réelle (R3.9) : `MultiModalContent` → `LlmMessage` → payloads Anthropic (blocs image) / OpenAI (`image_url`), chargeur VFS, activation opt-in |
| [Format de réponse LLM](./guides/llm-response-format.md) | Sortie JSON forcée à la frontière provider (`response_format: json_object`), cascade d'override à 5 niveaux (crew → agent → task → script → appel), premier provider câblé : DeepSeek |
| [Quality Gate SonarQube](./guides/quality-gate.md) | Gate « Orkeon Transitional » bloquant (R5.4) : seuils transitoires, trajectoire de durcissement, provisionnement automatique par les scripts |
| [Modèles locaux](./guides/local-models.md) | Tout exécuter sur sa machine : Docker Model Runner (pull/configure/inspect, contextes 128K), Ollama, variante d'image `local-llm` embarquée, changement de modèle, dépannage |

### Référence

| Fichier | Description |
|---------|-------------|
| [Catalogue des exemples](./reference/examples-catalog.md) | Carte éditoriale d'`examples/` (9 catégories métier + vitrines RAG/RaggableTree/scripting) ; l'`examples/INDEX.md` généré est l'inventaire faisant foi |
| [Limites et contraintes](./reference/limitations.md) | Contraintes connues de la version courante |
| [APIs expérimentales](./reference/experimental-apis.md) | Surfaces `[Experimental]` (A2A, Autonomous, RAG correctif, MCP), IDs de diagnostic `ORKEXP001–004`, comment s'inscrire |
| [Politique de données des exemples](./reference/example-data-policy.md) | Pourquoi les exemples livrent de la config et pas des datasets, comment monter vos entrées (`/data:ro`, `/output:rw`), règles contributeurs pour les fixtures |
| [Hosting & bootstrap des runners](./reference/hosting.md) | `Orkeon.Hosting` : `RunnerHost.Build`, ordre de câblage `ConfigureRunnerServices` (LLM d'abord, suites d'outils, VFS, `ServiceProviderToolRegistry`), flux `RunnerExecution`, pattern hôte web |
| [Gabarit de README d'exemple](./templates/example-readme.md) | Gabarit pour `examples/**/README.md` : Ce qu'il fait / Prérequis / Données requises / L'exécuter / Sortie attendue / Durée & coût |
| [Sous-systèmes opt-in](./reference/opt-in-subsystems.md) | A2A, monitoring, NIST, DLP, rate-limiting d'outils, rotation de clés, benchmarking, multi-modal, hooks de kickoff, sous-système RAG — activation explicite `AddOrkeonXxx()` (hors DI par défaut) |
| [Comparatif des fournisseurs LLM](./arkeon/llm-providers-comparatif.md) | Matrice de capacités par provider (streaming SSE, tool calling natif, grammaire GBNF, `response_format`, thinking, métriques, résilience), dérivée du code source |

---

## Parcours de lecture recommandés

### "Je veux comprendre le framework"

```
overview → yaml-and-builders → process-types → fsm → graph → autonomous → inventory → new-tool-pattern
```

Commencer par la vue d'ensemble pour assimiler Agent, Task, Crew, Tool. Puis explorer la configuration YAML et les builders. Le guide comparatif des ProcessTypes donne une vision d'ensemble des 6 stratégies, les docs FSM/Graph/Autonomous approfondissent les modes avancés. L'inventaire des outils montre les capacités natives. Terminer par le pattern de création d'outil pour comprendre l'extensibilité.

### "J'ai une application à migrer"

```
overview → bootstrap → inventory → porting-methodology → porting-example → new-tool-pattern
```

S'imprégner de l'architecture et du setup DI. Identifier les outils disponibles. Appliquer la méthodologie avec l'exemple concret. Revenir au pattern outil si des outils custom sont nécessaires.

### "Je veux étendre le framework"

```
overview → new-tool-pattern → yaml-and-builders → blueprint → inventory
```

Comprendre l'architecture, puis maîtriser le pipeline typé et le pattern de composition. Le blueprint guide la création de nouveaux modes d'orchestration. L'inventaire sert de référence pour positionner les contributions.

---

## Migration depuis l'ancienne documentation

L'ancienne documentation (dossier `docs/arkeon/`) a été réorganisée comme suit :

| Ancien fichier | Nouveau(x) fichier(s) |
|----------------|----------------------|
| `01_OVERVIEW.md` | `getting-started/overview.md` + `getting-started/bootstrap.md` |
| `02_FEATURES.md` | Éclaté en 8 fichiers thématiques (voir ci-dessus) |
| `03_TOOLS_INVENTORY.md` | `tools/inventory.md` |
| `04_NEW_TOOL_PATTERN.md` | `tools/new-tool-pattern.md` |
| `05_PORTING_METHODOLOGY.md` | `guides/porting-methodology.md` |
| `06_PORTING_EXAMPLE.md` | `guides/porting-example.md` |
| `07_FSM_ORCHESTRATION.md` | `orchestration/fsm.md` |
| `08_GRAPH_ORCHESTRATION.md` | `orchestration/graph.md` |
| `09_AUTONOMOUS_ORCHESTRATION.md` | `orchestration/autonomous.md` |
| `10_PROCESS_TYPES.md` | `orchestration/process-types.md` |
| `BLUEPRINT_NEW_ORCHESTRATION.md` | `guides/blueprint.md` |
