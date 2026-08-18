> 🇬🇧 [English version](../../reference/configuration.md)

# Référence de configuration (`appsettings.json`)

Cette page est la carte unique des sections de configuration lues par le framework. La
colonne « Opt-in » nomme le geste d'activation lorsque la section ne prend effet qu'après
un enregistrement dédié — voir [Sous-systèmes opt-in](./opt-in-subsystems.md) pour le
détail de chacun.

## D'où viennent les réglages

Les hôtes runner (`RunnerHost.Build`, utilisé par `orkeon run` et les runners YAML)
empilent, au-dessus des sources standard de l'hôte .NET :

1. **Un `appsettings.json` résolu** — chaîne de résolution (`RunnerSettings.ResolveSettingsPath`) :
   `--settings <chemin>` explicite → `appsettings.json` à côté de la config de crew →
   `appsettings/appsettings.json` en remontant l'arborescence (`examples/appsettings/appsettings.json`
   dans ce dépôt ; `_shared/appsettings.json` est un repli déprécié) → la config globale
   par utilisateur écrite par `orkeon init`. Aucun fichier trouvé ⇒ variables
   d'environnement uniquement.
2. **Les variables d'environnement préfixées `ORKEON_`** (`AddEnvironmentVariables("ORKEON_")`
   dans `RunnerHost` et dans `orkeon doctor`). Mapping .NET standard : `__` sépare les
   niveaux — `ORKEON_Llm__ApiKey` surcharge `Llm:ApiKey`, `ORKEON_Orkeon__Rag__Profile`
   surcharge `Orkeon:Rag:Profile`. Les variables sont ajoutées **après** le fichier :
   elles gagnent.
3. **Les surcharges CLI de montage** — chaque argument `--mount` devient une entrée
   mémoire `Orkeon:FileSystem:Mounts:<i>` (précédence maximale).

Le même préfixe `ORKEON_` alimente aussi `EnvironmentSecretProvider` (résolution de
secrets, p. ex. `OPENAI_API_KEY` → `ORKEON_OPENAI_API_KEY`).

## Provider LLM (section `Llm`)

La section `Llm` est lue par `RunnerHost.RegisterLlmProvider` et transformée en
`ILlmProvider` via `ILlmProviderFactory`. **Le provider est inféré automatiquement**, dans
l'ordre : motifs d'hôte du `BaseUrl` (p. ex. `deepseek.com` → DeepSeek, `groq.com` → Groq,
`/engines/` → Docker Model Runner/compatible OpenAI), puis motifs du nom de modèle, puis
forme de la clé API ; défaut `openai`. Clés : `Model`, `BaseUrl`, `ApiKey` (préférer
`ORKEON_Llm__ApiKey`), `Temperature`, `MaxTokens`, `TimeoutSeconds`, `MaxRetries`, et
`Thinking:{Enabled,Effort}` pour les providers à raisonnement. Sans section `Llm`, le
runtime dégrade vers le provider écho et avertit une fois. Voir
[Providers LLM](../architecture/llm-providers.md) ; des gabarits vivent dans
`examples/appsettings/*.json.example`.

## Sections hors préfixe `Orkeon:`

| Section | Configure | Consommateur / opt-in |
|---|---|---|
| `Llm` | Provider LLM actif (voir ci-dessus) | `RunnerHost` |
| `RateLimiting` | Limitation des requêtes LLM (concurrence, quotas/minute, file) | pipeline LLM Infrastructure |
| `LlmLogging` | Réglage de la capture des échanges LLM (p. ex. `FullEmbeddingLog`) | `RunnerHost` + `AddLlmExchangeFileLogging` (CLI `--llm-log`) |
| `PathSecurity` | Répertoires physiques autorisés (`AdditionalAllowedDirectories`) | `AddOrkeonInfrastructure()` |
| `Telemetry` | Export OpenTelemetry | `AddOrkeonInfrastructure(configuration)` |
| `A2A`, `A2A:Security` | Serveur/client A2A, mTLS, schémas d'auth | opt-in `AddOrkeonA2A(configuration)` |
| `MCP`, `MCP:Server` | Connexions client MCP + serveur MCP optionnel | `AddOrkeonMcp(configuration)` — voir [Intégration MCP](../architecture/mcp.md) |
| `Evaluation` | Services d'évaluation | opt-in (`EvaluationServiceCollectionExtensions`) |
| `RaggableTree` | Indexation de codebase (embedding, exclusions) | flag opt-in `RunnerHost` / `AddRaggableTree` |
| `Plugins` | Découverte du répertoire de plugins | opt-in `AddOrkeonPlugins(fileSystem, configuration)` |

## Sections `Orkeon:*`

### Cœur, orchestration, persistance

| Section | Configure | Consommateur | Opt-in |
|---|---|---|---|
| `Orkeon:CrewFactory:StrictTools` | Échec du chargement de crew sur outil inconnu (défaut runners `true`) | `RunnerHost` → `CrewFactoryOptions` | — |
| `Orkeon:ExecutionState:Persistence` | Persistance durable des états d'exécution (`Enabled`, `DeleteFromStoreOnArchive`) | `ScopedCrewExecutionStateManager` | `AddCrewExecutionStatePersistence(configuration)` — appelé automatiquement par `AddOrkeonInfrastructure(configuration)` quand la section existe ; requiert un `IStateStore` |
| `Orkeon:Checkpointing:*` | State store Postgres (`ConnectionString`, `SchemaName`, `AutoMigrate`, `MaxHistoryPerSession`) | `CheckpointingExtensions` | `AddOrkeonPostgresCheckpointing(configuration)` |
| `Orkeon:Consensus` | Options de vote du mode consensuel | Infrastructure | `AddOrkeonConsensus()` |
| `Orkeon:CostTracking`, `Orkeon:TokenCounter` | Suivi des coûts LLM et comptage de tokens | Infrastructure | `AddOrkeonCostTracking()` |
| `Orkeon:Monitoring` | Backend de monitoring | Infrastructure | `AddOrkeonMonitoring(configuration)` |

### Embeddings, recherche vectorielle, mémoire

| Section | Configure | Consommateur | Opt-in |
|---|---|---|---|
| `Orkeon:Embeddings` | Sélection du provider d'embeddings (`Provider`, `Model`, `Dimension`, `BatchSize`, `EnableCache`) | `DefaultEmbeddingProviderResolver` | lié par `AddOrkeonVectorSearch(configuration)` (appelé par `AddOrkeonInfrastructure(configuration)`) |
| `Orkeon:EmbeddingCache` | Cache d'embeddings (`SlidingExpirationMinutes`, `MaxCacheSizeBytes`) | idem | idem |
| `Orkeon:VectorSearch` | Options de recherche vectorielle | idem | idem |
| `Orkeon:ChromaDb`, `Orkeon:Pinecone` | Stores vectoriels externes | `VectorStoreExtensions` | auto-enregistrés par `AddOrkeonInfrastructure(configuration)` **quand la section existe**, ou `AddOrkeonChromaDb`/`AddOrkeonPinecone` |
| `Orkeon:LanceDb` | Serveur LanceDB distant | `VectorStoreExtensions` | `AddOrkeonLanceDb(...)` uniquement (jamais auto) |
| `Orkeon:CognitiveMemory` | Couche de mémoire cognitive | Infrastructure | `AddOrkeonCognitiveMemory(configuration)` |
| `Orkeon:Encryption` | Chiffrement de la mémoire au repos | Infrastructure | `AddOrkeonEncryption()` (rotation de clés : `AddOrkeonKeyRotation()`) |

### RAG (`Orkeon:Rag`)

Détails et sémantique : [Pipeline RAG](../architecture/rag-pipeline.md). Tout ce qui suit
requiert l'opt-in `AddOrkeonRag(configuration)` (`Orkeon.Rag.DependencyInjection`).

| Section | Configure |
|---|---|
| `Orkeon:Rag:Profile` | Preset de profil `fast` (défaut) / `balanced` / `quality` / `adaptive` / `corrective` ; toute clé `Orkeon:Rag` surcharge le preset clé par clé |
| `Orkeon:Rag:Provider`, `Orkeon:Rag:ConnectionString` | Provider dédié du document store RAG (`RagStoreOptions`) ; défaut : l'`IMemoryProvider` ambiant |
| `Orkeon:Rag:Ingestion` | Pipeline d'ingestion (`RagIngestionOptions`) |
| `Orkeon:Rag:Retrieval:Hybrid`, `Orkeon:Rag:Retrieval:Mmr` | Récupération hybride BM25+RRF, MMR opt-in |
| `Orkeon:Rag:QueryTransform`, `Orkeon:Rag:QueryRouting` | Transformateurs de requête (`multi-query`/`rag-fusion`/`hyde`), routage Adaptive-RAG |
| `Orkeon:Rag:Corrective` (dont `MaxIterations`) | Bornes du graphe correctif CRAG |
| `Orkeon:Rag:Corrective:WebFallback` + `Orkeon:Rag:WebFallback` | Repli web — double opt-in, les deux `Enabled` désactivés par défaut (politique + transport SearxNG) |

### Sécurité, sandbox, outils

| Section | Configure | Consommateur | Opt-in |
|---|---|---|---|
| `Orkeon:Security:PermissionGate` | Barrière par appel d'outil (`Enabled` défaut `false`, `Interactive`) | `ModePermissionGate` | `AddOrkeonPermissionGate(configuration)` — appelé par `RunnerHost` ; sans effet tant que `Enabled = true` n'est pas posé |
| `Orkeon:Dlp` | Politiques DLP / détection PII | Infrastructure | `AddOrkeonDlp()` |
| `Orkeon:Guardian` | Pipeline de sûreté de contenu | Infrastructure | `AddOrkeonGuardian()` |
| `Orkeon:Auth:AzureAD`, `Orkeon:Auth:OIDC` | Providers d'authentification | Infrastructure | `AddOrkeonAuth()` |
| `Orkeon:CodeSandbox` (+ `:Docker`) | Sandbox de l'interpréteur de code sécurisé | Infrastructure | `AddOrkeonCodeSandbox()` |
| `Orkeon:Sandbox` | Montage sandbox du système de fichiers | `AddSandboxMount(...)` | — |
| `Orkeon:FileSystem` (`Mounts`) | Montages VFS (voir [Conformité VFS](../architecture/vfs-compliance.md)) ; surchargé par le CLI `--mount` | `AddOrkeonFileSystem(...)` | — |
| `Orkeon:Tools:Shell:AllowInterpreters` | Autorise interpréteurs/git mutant dans `ShellCommandTool` (**équivalent RCE**, avertissement émis) | `AddOrkeonCodeTools()` | config seule |
| `Orkeon:Tools:Shell:ExtraAllowedCommands` / `AllowedCommands` | Allowlist shell : additive / remplacement complet (le remplacement annule `AllowInterpreters`) | idem | config seule |

### Scripting et CLI

| Section | Configure | Consommateur |
|---|---|---|
| `Orkeon:DefaultLlmProvider` | Nom du provider par défaut de l'espace `ctx.llm` du scripting | `Orkeon.Scripting` |
| `Orkeon:Scripting:Limits` | Sandbox Jint : `MemoryLimitBytes` (défaut 100 Mo), `RecursionLimit` (64), `ExecutionTimeout` (30 s) | `Orkeon.Scripting` |
| `Orkeon:Scripting:Toolchain` | Résolution de la toolchain esbuild | `Orkeon.Scripting` |
| `Orkeon:Cli:ScriptCommands` (+ `:Limits`) | Découverte des commandes CLI TypeScript (`Enabled`, `Directories`, `FailFastOnInvalidScript`, `EsbuildTranspile`) + profil sandbox CLI plus strict | `Orkeon.Cli.Commands.Scripting` |
| `Orkeon:Cli:ScriptHost` | Répertoires de crews pour la résolution `<nom>/crew.ork.ts` | `ScriptHostFacade` |
| `Orkeon:Cli:ConsoleStreaming` | Deltas `ctx.llm.act` streamés sur la console REPL (`Enabled` défaut `false`) | `AddLlmConsoleStreaming(configuration)` |
| `Orkeon:Cli:Session:ContextWindowTokens` | Fenêtre de contexte utilisée par `token_budget` et le TUI | `TokenBudgetTool`, ConsoleApp |
| `Orkeon:Cli:Tui:SpinnerVerbs` | Verbes du spinner TUI | ConsoleApp |

### Multi-modal

| Section | Configure | Opt-in |
|---|---|---|
| `Orkeon:MultiModal` | Validation vision/contenu (`Enabled`) | `AddOrkeonMultiModal(configuration)` |

---

> **Voir aussi** : [Sous-systèmes opt-in](./opt-in-subsystems.md) ·
> [Hébergement](./hosting.md) ·
> [Pipeline RAG](../architecture/rag-pipeline.md) ·
> [Retour à l'index](../INDEX.md)
