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
   mémoire `Orkeon:FileSystem:Mounts:<i>` (précédence maximale), placée **par racine
   virtuelle** : un `--mount` sur une racine que le tableau déclaré (couches 1 + 2) tient
   déjà est écrit à l'index de la première entrée et **remplace toutes les entrées déclarées
   de cette racine** pour le run ; un `--mount` sur une racine neuve est ajouté après le plus
   haut index déclaré. Le montage `/crew` (ou `/script`) du runner et les `InternalMounts`
   sont toujours ajoutés. Une entrée déclarée peut porter un **identifiant** — l'ULID de
   26 caractères devant un `|`, `01J9Z3K4M5N6P7Q8R9S0T1V2W3|C:\data:/output:rw` (VFS-90) —
   et plusieurs entrées peuvent déclarer une même racine si chacune en porte un :
   `--mount-id <ulid>`, sinon le bloc `mounts:` de la crew, sélectionne l'entrée que le run
   garde, et toute autre entrée de cette racine est **retirée** — sa clé est écrite à `null`
   à son propre index, son chemin de base n'est pas mis en liste blanche et son dossier
   n'est pas sondé.

Le même préfixe `ORKEON_` alimente aussi `EnvironmentSecretProvider` (résolution de
secrets, p. ex. `OPENAI_API_KEY` → `ORKEON_OPENAI_API_KEY` ; la clé Tavily de l'outil
`web_search` est `ORKEON_TAVILY_API_KEY`). Le second maillon de cette chaîne est la
section `Secrets` du fichier (`Secrets:TAVILY_API_KEY`), la variable d'environnement
l'emportant quand les deux existent.

**Ce que cela signifie en pratique.** Le fichier est la base durable et partagée ; tout
ce qui se pose dessus est un calque éphémère qui vit et meurt avec un processus. La
vérification `llm-config` d'`orkeon doctor` compose exactement comme un runner (même
chaîne de résolution, même surcouche `ORKEON_`) : son verdict répond à la question
*qu'utiliserait un run lancé depuis ce shell, sans calque propre au lancement ?* Les
réglages nommés d'Orkeon Studio empruntent la couche 2 : le réglage élu par défaut est
recopié dans la section `Llm` du fichier (un `orkeon run` manuel en terminal suit donc
la même élection — c'est ce que `llm-config` reflète), tandis qu'une équipe qui a élu
un autre réglage le reçoit en variables `ORKEON_Llm__*` sur son seul lancement —
`llm-config` ne peut pas les voir, car elles n'existent nulle part tant que ce
lancement n'a pas démarré. Aucun fichier n'est jamais généré : la composition est en
mémoire.

## Provider LLM (section `Llm`)

La section `Llm` est lue par `RunnerHost.RegisterLlmProvider` et transformée en
`ILlmProvider` via `ILlmProviderFactory`. **Le provider est inféré automatiquement**, dans
l'ordre : motifs d'hôte du `BaseUrl` (p. ex. `deepseek.com` → DeepSeek, `api.x.ai` → Grok,
`/engines/` → Docker Model Runner/compatible OpenAI), puis motifs du nom de modèle, puis
forme de la clé API ; défaut `openai`. Clés : `Model`, `BaseUrl`, `ApiKey` (préférer
`ORKEON_Llm__ApiKey` — la variable où vit conventionnellement la clé de chaque fournisseur, et
les trois noms qu'on confond avec elle, sont dans le
[comparatif des fournisseurs](llm-providers-comparison.md#clés-dapi--la-variable-par-fournisseur)), `Temperature`, `MaxTokens`, `TimeoutSeconds`, `MaxRetries`, et
`Thinking:{Enabled,Effort}` pour les providers à raisonnement. `TimeoutSeconds` vaut 30 s par
défaut, trop court pour un modèle qui réfléchit avant de répondre (Kimi K2.6, DeepSeek V4 et GLM
le font par défaut) : mettez 600 s, ou coupez la réflexion avec `Thinking:Enabled = false`. Un
appel qui atteint le délai est réessayé une fois, puis fait échouer sa tâche avec un message qui
nomme le réglage — il n'est jamais rapporté comme une réponse vide (LLM-11). `MaxTokens` est un
**épinglage** : absent, la requête porte le **maximum de sortie documenté** du modèle,
lu dans le catalogue `LlmModelOutputLimits` (128K sur `gpt-5.6-sol` et la génération Claude 5,
384K sur `deepseek-flash`, 131 072 sur les familles GLM-5 et Qwen 3.7/3.8, 65 536 sur Gemini 3.x
Flash — voir la [table des plafonds](llm-providers-comparison.md#plafonds-de-sortie--le-maximum-documenté-par-modèle-llm-10)),
aucun plafond quand le fournisseur n'en documente pas (Mistral, un Ollama local), et **4096
seulement pour un modèle inconnu du catalogue** — la valeur que le moteur envoyait pour tous
les modèles, qu'un modèle raisonneur dépense à réfléchir avant d'écrire un mot et qui revient
vide (une réponse finale vide fait échouer la tâche au lieu de passer pour une tâche
accomplie). Épinglez-le quand le modèle n'est pas au catalogue ou pour un plafond plus serré ;
un profil de modèle Studio l'épingle en `ORKEON_Llm__MaxTokens`, et l'éditeur de profil dit ce
qu'un champ vide signifie pour le modèle choisi. Un plafond du catalogue que le point d'accès
refuse est rejoué une fois sans le champ, avec un avertissement qui nomme le modèle. Sans
section `Llm`, le
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
| `MCP`, `MCP:Server` | Connexions client MCP + serveur MCP optionnel | `RunnerHost` (`orkeon run`) dès que `MCP:Servers` déclare au moins un serveur et que `MCP:Enabled` n'est pas `false` — les serveurs sont connectés avant le chargement de la crew (STUDIO-21) ; les hôtes bibliothèque appellent `AddOrkeonMcp(configuration)` ou la surcharge `AddOrkeonInfrastructure(configuration)` — voir [Intégration MCP](../architecture/mcp.md) |
| `Secrets:<NOM>` | Second maillon de la chaîne de secrets après `ORKEON_<NOM>` (`ConfigurationSecretProvider`), p. ex. `Secrets:TAVILY_API_KEY` pour `web_search` | `AddOrkeonInfrastructure()` |
| `Evaluation` | Services d'évaluation | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement) |
| `RaggableTree` | Indexation de codebase (embedding, exclusions) | **opt-out dans les hôtes runner** : `RunnerHost` l'enregistre par défaut, `RaggableTree:Enabled = false` le désactive ; les consommateurs bibliothèque appellent `AddRaggableTree(options)` explicitement |
| `Resilience` | Réglages retry/circuit-breaker/timeout (`ResilienceOptions`) | lié par `AddOrkeonInfrastructure()` |
| `Memory:Provider`, `Memory:ConnectionString` | Sélection du provider mémoire via `MemoryProviderFactory` (absent → in-memory) | `AddOrkeonInfrastructure()` |
| `ToolRateLimiting`, `TokenBudget` | Rate limits et budgets de tokens par outil | opt-in `AddOrkeonToolRateLimiting(configuration)` (voir [sous-systèmes opt-in](./opt-in-subsystems.md)) |
| `Security:Audit`, `Security:Prompt`, `Security:ToolResults`, `Security:Url`, `Security:Vault` | Sinks d'audit, durcissement de prompt, filtrage des résultats d'outils, validation d'URL, coffre à secrets | Infrastructure (les sections gouvernent le comportement) |
| `Llm:AvailableModels` | La liste de modèles qu'une commande REPL scriptée `/model` peut proposer | `AddOrkeonSessionTools(configuration)` |
| `BRAVE_API_KEY` | Aussi lu comme **clé de configuration** (pas seulement une variable d'env) pour gater l'outil Brave | `RunnerHost` |
| `Plugins` | Découverte du répertoire de plugins | opt-in `AddOrkeonPlugins(fileSystem, configuration)` |

## Sections `Orkeon:*`

### Cœur, orchestration, persistance

| Section | Configure | Consommateur | Opt-in |
|---|---|---|---|
| `Orkeon:CrewFactory:StrictTools` | Échec du chargement de crew sur outil inconnu (défaut runners `true`) | `RunnerHost` → `CrewFactoryOptions` | — |
| `Orkeon:ExecutionState:Persistence` | Persistance durable des états d'exécution (`Enabled`, `DeleteFromStoreOnArchive`) | `ScopedCrewExecutionStateManager` | `AddCrewExecutionStatePersistence(configuration)` — appelé automatiquement par `AddOrkeonInfrastructure(configuration)` quand la section existe ; requiert un `IStateStore` |
| `Orkeon:Checkpointing:*` | State store Postgres (`ConnectionString`, `SchemaName`, `AutoMigrate`, `MaxHistoryPerSession`) | `CheckpointingExtensions` | `AddOrkeonPostgresCheckpointing(configuration)` |
| `Orkeon:Consensus` | Options de vote du mode consensuel | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement) |
| `Orkeon:CostTracking`, `Orkeon:TokenCounter` | Suivi des coûts LLM et comptage de tokens | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement) |
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
| `Orkeon:Encryption` | Chiffrement de la mémoire au repos | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement). La rotation de clés reste opt-in : `AddOrkeonKeyRotation()` |

### RAG (`Orkeon:Rag`)

Détails et sémantique : [Pipeline RAG](../architecture/rag-pipeline.md). Tout ce qui suit
requiert l'opt-in `AddOrkeonRag(configuration)` (`Orkeon.Rag.DependencyInjection`).

| Section | Configure |
|---|---|
| `Orkeon:Rag:Profile` | Preset de profil `fast` (défaut) / `balanced` / `quality` / `adaptive` / `corrective` ; toute clé `Orkeon:Rag` surcharge le preset clé par clé |
| `Orkeon:Rag:Provider`, `Orkeon:Rag:ConnectionString` | Provider dédié du document store RAG (`RagStoreOptions`) ; défaut : l'`IMemoryProvider` ambiant |
| `Orkeon:Rag:Collection` | Nom de collection par défaut |
| `Orkeon:Rag:Retrieval` (`TopK`, `CandidateK`, `MinScore`) | Bornes de l'étape de retrieval |
| `Orkeon:Rag:Rerank` (`Kind`, `TopN`) | Choix et profondeur du reranker |
| `Orkeon:Rag:Context` (`MaxTokens`, `Ordering`) | Assemblage du contexte (ordre `edges` anti-Lost-in-the-Middle) |
| `Orkeon:Rag:Groundedness`, `Orkeon:Rag:Generation` (`Enabled`, `SystemPrompt`) | Hook de groundedness et étape de génération citée |
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
| `Orkeon:Guardian` | Pipeline de sûreté de contenu | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement) |
| `Orkeon:Auth:AzureAD`, `Orkeon:Auth:OIDC` | Providers d'authentification | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement) |
| `Orkeon:CodeSandbox` (+ `:Docker`) | Sandbox de l'interpréteur de code sécurisé | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement) |
| `Orkeon:Sandbox` | Montage sandbox du système de fichiers (`/sandbox`, Internal) | `AddOrkeonFileSystem(...)` | — |
| `Orkeon:FileSystem` (`Mounts`) | Montages VFS (voir [Conformité VFS](../architecture/vfs-compliance.md)). Une entrée peut porter un **identifiant** — `<ulid>|<physique>:<virtuel>:<droits>` (VFS-90) : ce par quoi le bloc `mounts:` d'une crew, le sidecar d'équipe de Studio et `--mount-id` la désignent ; Studio en écrit un à chaque enregistrement. Un `--mount` CLI sur la même racine virtuelle **remplace toutes les entrées de cette racine** pour ce run ; sur une racine neuve il est ajouté (jamais fusionné, jamais perdu). Une racine déclarée deux fois n'est légitime que si chacune de ses entrées porte un identifiant — `--mount-id`, ou le `mounts:` de la crew, en sélectionne alors une et les autres sont retirées pour le run ; si rien n'en sélectionne une, le run est refusé avant tout host (`'/output' is declared twice in <settings> (<idA>: <dossierA>, <idB>: <dossierB>) and nothing selects one. Pass --mount-id <id>, list '<id>|/output' under mounts: in the crew, or pass --mount <folder>:/output:rw to replace them all.`), de même qu'une racine déclarée deux fois avec une entrée sans identifiant (`… and '<entrée>' has no id. Give every entry an id …`) ou un identifiant porté par deux entrées. Le chemin de base de chaque entrée déclarée est **mis en liste blanche pour `PathValidator`** sans `--allow-external-mounts` — un dossier déclaré est l'intention du propriétaire de la machine, il reste donc accessible même hors du répertoire de travail du processus ; une entrée retirée ne l'est pas | `AddOrkeonFileSystem(...)` | — |
| `Orkeon:FileSystem` (`InternalMounts`) | Même grammaire que `Mounts`, enregistrés en `MountVisibility.Internal` : résolubles par le VFS, **absents de `list_mounts`, de la table de montages du prompt agent et des messages de refus d'accès**. C'est là qu'un hôte met ce que le VFS doit atteindre et qu'aucun agent n'a à adresser — le répertoire `--llm-log` y vit ([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)) | `AddOrkeonFileSystem(...)` | — |
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
