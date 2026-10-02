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
forme de la clé API ; défaut `openai`. Une section sans `Model` tourne sur le modèle par défaut de
ce fournisseur (la colonne *Défaut (code)* du
[comparatif des fournisseurs](llm-providers-comparison.md#défauts-et-modèles-plus-récents--revue-des-catalogues-du-2026-09-19)),
jamais sur celui d'OpenAI posé sur un autre vendeur. Clés : `Model`, `BaseUrl`, `ApiKey` (préférer
`ORKEON_Llm__ApiKey` — la variable où vit conventionnellement la clé de chaque fournisseur, et
les trois noms qu'on confond avec elle, sont dans le
[comparatif des fournisseurs](llm-providers-comparison.md#clés-dapi--la-variable-par-fournisseur)), `Temperature`, `MaxTokens`, `TimeoutSeconds`, `MaxRetries` (défaut 10), et
`Thinking:{Enabled,Effort}` pour les providers à raisonnement, et `Grammar` (défaut `false`) :
ne le passez à `true` que lorsque `BaseUrl` désigne un serveur compatible llama.cpp (Docker
Model Runner, `llama-server`) — le seul genre de point d'accès qui honore le champ GBNF
`grammar` que produit un livrable `structured_output` ; ailleurs la grammaire est abandonnée avec
un avertissement qui nomme la clé ([comparatif des fournisseurs](llm-providers-comparison.md)).
`TimeoutSeconds` vaut 30 s par
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

### Profils nommés (`Llm:Profiles`)

Un hôte peut offrir plusieurs fournisseurs. Chaque enfant de `Llm:Profiles` est un **profil** :
un nom, et un fournisseur décrit avec exactement les clés de la section `Llm` (`BaseUrl`,
`ApiKey`, `Model`, `Temperature`, `MaxTokens`, `TimeoutSeconds`, `MaxRetries`, `Thinking`,
`Grammar`). La section `Llm` elle-même reste le profil **par défaut** — celui de tout agent qui
n'en nomme pas d'autre. Une crew choisit un profil **par son nom**, jamais par une clé ou une URL :
`llm: { profile: claude }` sur la crew, un agent ou le `llm_override` d'une tâche en YAML,
`llm.profile("claude")` en `.ork.ts` ([YAML et builders](../getting-started/yaml-and-builders.md#un-fournisseur-par-agent-profils)).

```json
{
  "Llm": {
    "BaseUrl": "https://api.deepseek.com/v1", "Model": "deepseek-v4-flash",
    "Profiles": {
      "claude": { "BaseUrl": "https://api.anthropic.com/v1", "Model": "claude-sonnet-5" },
      "local":  { "BaseUrl": "http://localhost:11434", "Model": "qwen3" }
    }
  }
}
```

Les clés restent hors des fichiers de la même façon : `ORKEON_Llm__Profiles__claude__ApiKey`. Le
fournisseur de chaque profil est construit une fois, au premier usage, compté comme celui par
défaut — les relevés `cost.updated` d'un run nomment le fournisseur de chaque appel, et la barre
d'état de Studio ventile les jetons par fournisseur. Les profils sont validés au démarrage de
l'hôte : `default` est un nom réservé (il désigne la section `Llm`, et une crew peut le nommer pour
ramener un agent au défaut), et une `BaseUrl` invalide ou une valeur qui n'est pas un nombre fait
échouer le démarrage en nommant la clé à corriger. Une crew qui nomme un profil que l'hôte ne
définit pas **échoue au chargement**, et le message liste les profils offerts. **Un modèle non
précisé est celui du profil, sur tous les chemins** : un bloc `llm:` YAML ou `.ork.ts` sans
`model`, le `.Thinking()` ou le `.MaxOutputTokens(n)` d'un agent C#, `llm.default_` sur un hôte qui
ne configure aucun modèle, le planificateur, les boucles d'agent hors client de chat et les appels
d'analyse de la mémoire cognitive laissent tous le modèle au fournisseur qu'ils atteignent, qui
envoie le modèle que son profil configure, sinon son propre défaut — jamais un modèle vide, jamais
celui d'OpenAI sur un autre vendeur. Seuls les tours des agents changent de fournisseur : le manager hiérarchique, le planificateur, le Guardian, les
pipelines RAG et les juges LLM restent sur le profil par défaut. Une section qui ne contient que
`Profiles` ne configure aucun fournisseur par défaut — le défaut est alors le provider écho, avec
l'avertissement habituel. `orkeon-host` peut restreindre les profils que ses crews peuvent nommer
(`Orkeon:Host:LlmProfiles`, plus bas).

## Sections hors préfixe `Orkeon:`

| Section | Configure | Consommateur / opt-in |
|---|---|---|
| `Llm` | Provider LLM actif — le profil par défaut (voir ci-dessus) | `RunnerHost` |
| `Llm:Profiles:<nom>` | Profils LLM nommés qu'une crew choisit par agent ou par tâche, mêmes clés que `Llm` (voir ci-dessus) | `RunnerHost`, le REPL (`AddOrkeonLlmProfiles(configuration)`) |
| `Llm:AvailableModels` | La liste de modèles qu'une commande REPL scriptée `/model` peut proposer (tableau de chaînes, ou une chaîne séparée par des virgules) | `AddOrkeonSessionTools(configuration)` |
| `Memory:Provider` | TYPE du provider mémoire de l'application (`inmemory`, `redis`, `sqlite`, `chromadb`, `pinecone`, `lancedb` ; absent → in-memory). Sa connexion est la section propre de ce provider (`Orkeon:Redis`, `Orkeon:Sqlite`, … plus bas) — voir [Système de mémoire](../architecture/memory-system.md#sélection-par-configuration) | `AddOrkeonInfrastructure()` |
| `RateLimiting` | Limitation des requêtes LLM : `GlobalRequestsPerMinute`, `ProviderRequestsPerMinute`, `AgentRequestsPerMinute`, `MaxConcurrentRequests`, `QueueLimit` | `AddOrkeonInfrastructure()` (`ILlmRateLimiter`) |
| `LlmLogging` | Réglage de la capture des échanges LLM : `FullEmbeddingLog` (défaut `true`), `LogStreamingExchanges` (`true`), `MaxBodyLengthChars` (`0` = pas de troncature) | `RunnerHost` → `AddLlmExchangeLogging(logDirectory, options)`, uniquement quand le run passe `--llm-log` |
| `PathSecurity` | Validation des chemins physiques : `DefaultWorkspaceRoot`, `AdditionalAllowedDirectories`, `AdditionalBlockedExtensions`, `ResolveSymlinks`, `MaxFileSizeBytes` | `AddOrkeonInfrastructure()` (`IPathValidator`) |
| `Telemetry` | Export OpenTelemetry : `Enabled`, `OtlpEndpoint`, `ExportToConsole`, `PrometheusEndpoint`, `MaxMemoryMB` (la variable standard `OTEL_EXPORTER_OTLP_ENDPOINT` marche aussi dans les runners) | `AddOrkeonTelemetry(configuration)` — appelé par `AddOrkeonInfrastructure(configuration)` et `RunnerHost` |
| `A2A` | Serveur/client A2A : `EnableServer`, `Port`, `Host`, `AgentName`, `AgentDescription`, `AgentVersion`, `Organization`, `ContactUrl`, `TimeoutSeconds` ; `EnableServer` enregistre aussi le service hébergé qui démarre le serveur avec l'hôte générique | opt-in `AddOrkeonA2A(configuration)` — aucun binaire livré ne l'appelle encore ; voir [Conformité A2A](./a2a-conformance.md#activation) |
| `A2A:Security` | `ClientCertificatePath`, `ClientCertificatePassword`, `TrustedCertificateAuthorities`, `TrustedClientCertificateThumbprints`, `RequireMutualTls`, `AllowedAuthSchemes` (`Bearer`, `ApiKey` — chacun exige son validateur, sinon le serveur refuse de démarrer), `ApiKeySecretNames` (noms des secrets qui portent les clés acceptées, lus via `ISecretProvider`) ; côté client `ClientAuthScheme` (`Bearer`/`ApiKey`) et `ClientCredentialSecretName` (le secret qu'`A2AClient` envoie en `Authorization`) ; validateurs bearer `A2A:Security:AzureAD` (`TenantId`, `ClientId`, `Authority`, `ValidIssuers`, `ValidAudiences`) et `A2A:Security:Oidc` (`Authority`, `ClientId`, `ValidAudiences`, `RequireHttpsMetadata`) | idem — voir [Sécurité](../architecture/security.md#tls-mutuel-a2a) |
| `MCP` | Connexions client MCP (`MCP:Servers:<id>`), l'interrupteur `MCP:Enabled` (défaut `true`), et le serveur MCP optionnel — `MCP:EnableServer` (défaut `false`) + `MCP:Server` (`Name`, `Version` ; le serveur n'expose que des outils) | `RunnerHost` (`orkeon run`, `orkeon-host`) dès que `MCP:Servers` déclare au moins un serveur et que `MCP:Enabled` n'est pas `false` — les serveurs sont connectés avant le chargement de la crew (STUDIO-21), et par `orkeon-host` une fois au démarrage, avant son premier message (GAP-11) ; les hôtes bibliothèque appellent `AddOrkeonMcp(configuration)` ou la surcharge `AddOrkeonInfrastructure(configuration)` — voir [Intégration MCP](../architecture/mcp.md) |
| `Secrets:<NOM>` | Second maillon de la chaîne de secrets après `ORKEON_<NOM>` (`ConfigurationSecretProvider`), p. ex. `Secrets:TAVILY_API_KEY` pour `web_search` | `AddOrkeonInfrastructure()` |
| `Evaluation` | `EnableLlmJudge` (défaut `false`) : l'`IEvaluationSuite` par défaut exécute aussi les juges LLM de cohérence, fluidité et ancrage sur l'`IChatClient` enregistré | `AddOrkeonInfrastructure(configuration)`, ou `AddOrkeonEvaluation(configuration)` (idempotent : un second appel n'enregistre aucun évaluateur deux fois) |
| `RaggableTree` | Indexation de codebase : `Enabled`, `Embedding` (`Provider`, `Model`, `ApiKey`, `BaseUrl`, `Dimensions`, `MaxTextChars`) — rien d'autre : toute autre clé fait échouer l'hôte au démarrage ; ce que couvre un index se règle à chaque appel `index_codebase` | **opt-out dans les hôtes runner** : `RunnerHost` l'enregistre par défaut, `RaggableTree:Enabled = false` le désactive ; les consommateurs bibliothèque appellent `AddRaggableTree(options)` explicitement |
| `ToolRateLimiting` | Rate limits par outil : `GlobalToolRequestsPerMinute`, `DefaultToolRequestsPerMinute`, `ToolSpecificLimits` | opt-in `AddOrkeonToolRateLimiting()` (lie depuis l'`IConfiguration` enregistrée ; voir [sous-systèmes opt-in](./opt-in-subsystems.md)) |
| `TokenBudget` | Budgets de tokens : `MaxTokensPerAgent`, `MaxTokensPerCrew`, `MaxCostPerCrew` | idem |
| `Security:Audit` | Sinks d'audit : `Enabled`, `MinSeverity`, `EnabledCategories`, `AuditDirectory`, `RetentionDays` | `AddOrkeonInfrastructure()` |
| `Security:Url` | Validation SSRF : `AllowedSchemes`, `BlockedPorts`, `AllowedDomains`, `BlockedDomains`, `BlockPrivateIPs`, `ResolveDNS` | `AddOrkeonInfrastructure()` (`IUrlValidator`) |
| `Security:Vault` | Chaîne de coffres à secrets : `AzureKeyVaultUri`, `UseAwsSecretsManager`, `DpapiSecretsDirectory`, `CacheTtl` | `AddOrkeonInfrastructure()` |
| `Security:Prompt` | La phase d'entrée du Guardian : `Policy` (`Block` par défaut — High/Critical fait échouer la tâche, moindre avertit ; `Warn` ; `None`), `CustomPatterns`, `EnableExfiltrationDetection` | `AddOrkeonInfrastructure()` — filtre l'invite de chaque tour d'agent ; voir [Sécurité](../architecture/security.md) |
| `Security:ToolResults` | Filtrage des résultats d'outils : `Policy` (`Warn` par défaut — balisé comme donnée et signalé ; `Block` retient High/Critical ; `None`), `TrustedTools` (ajoutés aux `email_*` par défaut). La longueur ne se règle pas ici : une seule règle, `AgentDefaults.ResolveMaxToolResultLength` | `AddOrkeonInfrastructure()` — appliqué à chaque résultat d'outil par `IToolInvocationPipeline` |
| `BRAVE_API_KEY` | Aussi lu comme **clé de configuration** (pas seulement une variable d'env) pour gater l'outil Brave | `RunnerHost` |
| `Plugins` | Découverte du répertoire de plugins : `Directory` (défaut `/plugins`), `SearchPattern` (`*.dll`), `ContinueOnError`, `SharedAssemblyPrefixes` | opt-in `AddOrkeonPlugins(fileSystem, configuration)` — voir [Plugins](../architecture/plugins.md) |

## Sections `Orkeon:*`

### Cœur, orchestration, persistance

| Section | Configure | Consommateur | Opt-in |
|---|---|---|---|
| `Orkeon:CrewFactory:StrictTools` | Échec du chargement de crew sur outil inconnu (défaut runners `true`) | `RunnerHost` → `CrewFactoryOptions` | — |
| `Orkeon:ExecutionState:Persistence` | Persistance durable des états d'exécution (`Enabled`, `DeleteFromStoreOnArchive`) | `ScopedCrewExecutionStateManager` | `AddCrewExecutionStatePersistence(configuration)` — appelé automatiquement par `AddOrkeonInfrastructure(configuration)` quand la section existe ; requiert un `IStateStore` |
| `Orkeon:Checkpointing:*` | State store Postgres (`ConnectionString`, `SchemaName` défaut `orkeon`, `AutoMigrate`, `MaxHistoryPerSession`) | `CheckpointingExtensions` | `AddOrkeonPostgresCheckpointing(configuration)` |
| `Orkeon:Consensus` | Vote du mode consensuel : `VotingOptions`, `MaxVotingRounds`, `EnableDiscussion`, `FallbackStrategy`, `RoleWeights` | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement) |
| `Orkeon:CostTracking` | Suivi des coûts LLM : `Enabled`, `DefaultCrewBudget`, `CustomPricings` | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement) |
| `Orkeon:TokenCounter` | Estimation des tokens : `CharsPerToken`, `TokensPerMessage`, `TokensPerReply`, `SpecialTokenOverhead` | Infrastructure | idem |
| `Orkeon:Monitoring` | Backend de monitoring : `MaxTraceHistory`, `MetricsRetentionMinutes`, `TraceSourcePrefix` | Infrastructure | `AddOrkeonMonitoring(configuration)` |

### Embeddings, recherche vectorielle, mémoire

| Section | Configure | Consommateur | Opt-in |
|---|---|---|---|
| `Orkeon:Embeddings` | Sélection du provider d'embeddings (`Provider`, `Model`, `Dimension`, `BatchSize`, `EnableCache`) | `DefaultEmbeddingProviderResolver` | lié par `AddOrkeonVectorSearch(configuration)` (appelé par `AddOrkeonInfrastructure(configuration)`) |
| `Orkeon:EmbeddingCache` | Cache d'embeddings (`SlidingExpirationMinutes`, `MaxCacheSizeBytes`) | idem | idem |
| `Orkeon:VectorSearch` | Options de recherche vectorielle (`DefaultMetric`, `DefaultTopK`, `DefaultMinScore`, `PreferVectorSearch`) | idem | idem |
| `Orkeon:Redis` | Provider mémoire Redis : `ConnectionString` (défaut `localhost:6379`), `KeyPrefix` (défaut `orkeon:memory:`). La connexion s'ouvre au premier usage | `MemoryProviderFactory` — tout chemin qui sélectionne `redis` : `Memory:Provider`, le `memoryProvider:` d'une crew, `Orkeon:Rag:Provider`, `AddOrkeonRedisMemory` | liée par `AddOrkeonInfrastructure()` |
| `Orkeon:Sqlite` | Provider mémoire SQLite : `ConnectionString` (défaut `Data Source=:memory:` ; une `Data Source` fichier est un chemin virtuel sur un montage inscriptible), `TableName`, `DefaultTopK`, `MinSimilarityScore` | `MemoryProviderFactory` — tout chemin qui sélectionne `sqlite` | idem |
| `Orkeon:ChromaDb` | Serveur ChromaDB : `BaseUrl`, `Tenant`, `Database`, `CollectionName`, `DefaultTopK` | `MemoryProviderFactory` — tout chemin qui sélectionne `chromadb` | idem ; `AddOrkeonChromaDb(configuration)` (appelée par `AddOrkeonInfrastructure(configuration)` quand la section existe) expose aussi le `ChromaDbMemoryProvider` partagé par sa classe |
| `Orkeon:Pinecone` | Index Pinecone : `ApiKey`, `IndexName`, `Host` (facultatif : sans lui, un appel `describe_index` résout l'hôte de l'index au premier usage), `Namespace` | `MemoryProviderFactory` — tout chemin qui sélectionne `pinecone` | idem, `AddOrkeonPinecone(configuration)` |
| `Orkeon:LanceDb` | Serveur LanceDB distant : `Endpoint`, `ApiKey`, `Database`, `TableName`, `EmbeddingDimension`, `DistanceType`, `DefaultTopK`, `MinSimilarityScore`, `VectorWeight`, `FullTextWeight`, `CreateFullTextIndexOnInit` | `MemoryProviderFactory` — tout chemin qui sélectionne `lancedb` | liée par `AddOrkeonInfrastructure()` ; `AddOrkeonLanceDb(configuration)` ajoute la classe concrète et `LanceDbMigrationService` |
| `Orkeon:CognitiveMemory` | Couche de mémoire cognitive (`EnableLlmAnalysis`, `EnableContradictionDetection`, `ContradictionCandidateCount`, `AnalysisModel`, `AnalysisTemperature`, `PruningThreshold`, `PruningMinAgeDays`, `RecencyHalfLifeHours`, `DefaultRecallOptions`) | Infrastructure | `AddOrkeonCognitiveMemory(configuration)` |
| `Orkeon:Encryption` | Chiffrement de la mémoire au repos (`Enabled`, `SecretName`, `KeySizeInBits`) | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement ; le décorateur est posé par l'hôte). La rotation de clés reste opt-in : `AddOrkeonKeyRotation()` |

### RAG (`Orkeon:Rag`)

Détails et sémantique : [Pipeline RAG](../architecture/rag-pipeline.md). Tout ce qui suit
requiert `AddOrkeonRag(configuration)` (`Orkeon.Rag.DependencyInjection`), qu'appelle tout hôte
runner (`orkeon run`, `orkeon-host`).

| Section | Configure |
|---|---|
| `Orkeon:Rag:Profile` | Preset de profil `fast` (défaut) / `balanced` / `quality` / `adaptive` / `corrective` ; toute clé `Orkeon:Rag` surcharge le preset clé par clé |
| `Orkeon:Rag:Provider` | TYPE du provider du document store RAG (`RagStoreOptions` — un alias de type de `MemoryProviderFactory`), connecté depuis la section propre de ce provider (`Orkeon:Redis`, `Orkeon:Sqlite`, …) ; défaut : l'`IMemoryProvider` ambiant |
| `Orkeon:Rag:Collection` | Collection qu'interroge `rag_search` quand l'agent n'en nomme aucune (non définie : `default`) ; `rag_eval` l'utilise pour un dataset qui ne nomme pas de collection et n'apporte pas de corpus |
| `Orkeon:Rag:Retrieval` (`TopK`, `CandidateK`, `MinScore`) | Bornes de l'étape de retrieval |
| `Orkeon:Rag:Retrieval:Hybrid` (`Enabled`, `RrfK`), `Orkeon:Rag:Retrieval:Mmr` (`Enabled`, `Lambda`) | Récupération hybride BM25+RRF, MMR opt-in |
| `Orkeon:Rag:Rerank` (`Enabled`, `Kind`, `TopN`) | Choix et profondeur du reranker |
| `Orkeon:Rag:Context` (`MaxTokens`, `Ordering`) | Assemblage du contexte (ordre `edges` anti-Lost-in-the-Middle) |
| `Orkeon:Rag:Groundedness` (`Enabled`) | Hook de groundedness |
| `Orkeon:Rag:Generation` (`SystemPrompt`, `Temperature`, `MaxOutputTokens`) | Étape de génération citée |
| `Orkeon:Rag:Ingestion` (`DefaultChunkingStrategy` défaut `recursive`, `ManifestDirectory` défaut `/output/rag/manifests`) | Pipeline d'ingestion (`RagIngestionOptions`) |
| `Orkeon:Rag:QueryTransform` (`Mode`, `VariantCount`), `Orkeon:Rag:QueryRouting` (`Classifier`) | Transformateurs de requête (`multi-query`/`rag-fusion`/`hyde`), routage Adaptive-RAG (`heuristic` ou `llm`) |
| `Orkeon:Rag:Corrective` (`MaxIterations`) | Bornes du graphe correctif CRAG |
| `Orkeon:Rag:Corrective:WebFallback` (`Enabled`, `MaxResults`) + `Orkeon:Rag:WebFallback` (`Enabled`, `Endpoint`, `ApiKeyEnvVar`, `MaxResults`, `Timeout`, `SuspiciousAction`) | Repli web — double opt-in, les deux `Enabled` désactivés par défaut (politique + transport SearxNG) |

### Sécurité, sandbox, outils

| Section | Configure | Consommateur | Opt-in |
|---|---|---|---|
| `Orkeon:Security:PermissionGate` | Barrière par appel d'outil (`Enabled` défaut `false`, `Interactive`) | `ModePermissionGate` | `AddOrkeonPermissionGate(configuration)` — appelé par `RunnerHost` ; sans effet tant que `Enabled = true` n'est pas posé |
| `Orkeon:Dlp` | Politiques DLP / détection PII (`Enabled`, `DefaultAction`, `ChannelPolicies`) | Infrastructure | `AddOrkeonDlp()` |
| `Orkeon:Guardian` | Pipeline de gardes : `Enabled` (défaut `true`), `DefaultPolicy` (`InputGuardEnabled`, `ToolGuardEnabled`, `DelegationGuardEnabled`, `MaxDelegationDepth` — 5) | `GuardianPipeline` — la phase d'entrée de chaque tour d'agent, les phases outil et délégation de chaque appel d'outil | — (actif par défaut, enregistré par `AddOrkeonInfrastructure()`) |
| `Orkeon:CodeSandbox` (+ `:Docker`) | Sandbox de l'interpréteur de code sécurisé (`TimeoutSeconds`, `MaxMemoryBytes`, `MaxOutputBytes`, `AllowHostExecution`, `DefaultPermissions`, `SecurityOptions` ; Docker : `ImageName`, `PullImageOnStartup`) | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement) |
| `Orkeon:Sandbox` | Montage sandbox du système de fichiers (`/sandbox`, Internal) : `EphemeralRoot`, `CleanupOrphansOlderThan` (24 h), `VirtualPath` | `AddOrkeonFileSystem(...)` | — |
| `Orkeon:FileSystem` (`Mounts`) | Montages VFS (voir [Conformité VFS](../architecture/vfs-compliance.md)). Une entrée peut porter un **identifiant** — `<ulid>|<physique>:<virtuel>:<droits>` (VFS-90) : ce par quoi le bloc `mounts:` d'une crew, le sidecar d'équipe de Studio et `--mount-id` la désignent ; Studio en écrit un à chaque enregistrement. Un `--mount` CLI sur la même racine virtuelle **remplace toutes les entrées de cette racine** pour ce run ; sur une racine neuve il est ajouté (jamais fusionné, jamais perdu). Une racine déclarée deux fois n'est légitime que si chacune de ses entrées porte un identifiant — `--mount-id`, ou le `mounts:` de la crew, en sélectionne alors une et les autres sont retirées pour le run ; si rien n'en sélectionne une, le run est refusé avant tout host (`'/output' is declared twice in <settings> (<idA>: <dossierA>, <idB>: <dossierB>) and nothing selects one. Pass --mount-id <id>, list '<id>|/output' under mounts: in the crew, or pass --mount <folder>:/output:rw to replace them all.`), de même qu'une racine déclarée deux fois avec une entrée sans identifiant (`… and '<entrée>' has no id. Give every entry an id …`) ou un identifiant porté par deux entrées. Le chemin de base de chaque entrée déclarée est **mis en liste blanche pour `PathValidator`** sans `--allow-external-mounts` — un dossier déclaré est l'intention du propriétaire de la machine, il reste donc accessible même hors du répertoire de travail du processus ; une entrée retirée ne l'est pas | `AddOrkeonFileSystem(...)` | — |
| `Orkeon:FileSystem` (`InternalMounts`) | Même grammaire que `Mounts`, enregistrés en `MountVisibility.Internal` : résolubles par le VFS, **absents de `list_mounts`, de la table de montages du prompt agent et des messages de refus d'accès**. C'est là qu'un hôte met ce que le VFS doit atteindre et qu'aucun agent n'a à adresser — le répertoire `--llm-log` y vit, et `/credentials`, les jetons OAuth des comptes e-mail, quand un tel compte est déclaré ([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)) | `AddOrkeonFileSystem(...)` | — |
| `Orkeon:Tools:Shell:AllowInterpreters` | Autorise interpréteurs/git mutant dans `ShellCommandTool` (**équivalent RCE**, avertissement émis) | `AddOrkeonCodeTools()` | config seule |
| `Orkeon:Tools:Shell:ExtraAllowedCommands` / `AllowedCommands` | Allowlist shell : additive / remplacement complet (le remplacement annule `AllowInterpreters`) | idem | config seule |

### E-mail (`Orkeon:Tools:Email`)

Lié par `AddOrkeonEmailTools(configuration)` — l'hôte partagé des runners et `orkeon-repl`
l'appellent — et validé paresseusement : un compte est validé la première fois qu'un outil ou une
commande `orkeon email` s'en sert, tous ses problèmes signalés d'un coup, et une valeur que le
binder ne sait même pas lire (un droit mal orthographié, un port en toutes lettres) met ce seul
compte de côté au lieu de faire échouer l'hôte — une section cassée ne casse jamais un crew qui
n'envoie pas de courrier. Les secrets n'y sont jamais des valeurs, seulement les **noms** des
variables d'environnement qui les contiennent. Parcours par fournisseur et table clé par clé :
[Outils e-mail](../guides/email.md).

| Section | Configure |
|---|---|
| `Orkeon:Tools:Email:DefaultAccount` | Le compte qu'utilise un appel qui n'en nomme aucun (facultatif avec un seul compte) |
| `Orkeon:Tools:Email:CredentialsDirectory` | Répertoire physique dont le sous-répertoire `email` contient les jetons OAuth ; un chemin relatif part du répertoire du fichier de réglages. Le runner le monte sur la racine interne `/credentials` quand un compte OAuth est déclaré ; par défaut : `credentials` à côté du fichier de réglages de l'utilisateur |
| `Orkeon:Tools:Email:Screening:WithholdRejected` | Retenir le corps d'un message que le filtre anti-injection de prompt rejette (défaut `false` : signaler seulement) |
| `Orkeon:Tools:Email:Accounts:<nom>` | Un compte. `Provider` (`Gmail`, `Outlook` ou `Custom` — le défaut), `Address`, `DisplayName`, `Rights` (**obligatoire** — `Read, Organize, Draft, Send, Delete, Purge`), `Incoming` (`Protocol` `Imap`, `Pop3` ou `Graph` ; `Host`, `Port`, `Security` `SslOnConnect`, `StartTls` ou `None` — ce dernier vers un hôte de bouclage seulement), `Outgoing` (`Protocol` `Smtp` ou `Graph` ; `Host`, `Port`, `Security`), `Auth` (`Method` `Password` ou `OAuth2` ; `Username`, `PasswordEnvVar`, `ClientId`, `ClientSecretEnvVar`, `Tenant`), `Send` (`AllowedRecipients` — une liste vide n'autorise personne —, `MaxRecipients`, `MaxPerHour`), `TimeoutSeconds`, `SaveSentCopy`. Le nom contient des lettres, des chiffres, `.`, `_` et `-`, commence par une lettre ou un chiffre (64 au plus) |

### Scripting et CLI

| Section | Configure | Consommateur |
|---|---|---|
| `Orkeon:Scripting:Limits` | Sandbox Jint : `MemoryLimitBytes` (défaut 100 Mo), `RecursionLimit` (64), `ExecutionTimeout` (30 s) | `Orkeon.Scripting` |
| `Orkeon:Scripting:Toolchain` | Résolution de la toolchain esbuild (`EsbuildPath`, premier de l'ordre de recherche ; `EsbuildTimeout` 30 s) | `EsbuildTranspiler.Create` — `orkeon run`, le runner partagé, `orkeon doctor`, `orkeon forge`, les commandes `*.cmd.ts` |
| `Orkeon:Cli:ScriptCommands` (+ `:Limits`) | Découverte des commandes CLI TypeScript (`Enabled`, `Directories`, `FailFastOnInvalidScript`, `EsbuildTranspile`, `MaxScripts` 50, `ContinueOnConflict`, `FallbackCommandName` `assistant`) + profil sandbox CLI plus strict | `Orkeon.Cli.Commands.Scripting` |
| `Orkeon:Cli:ScriptHost` | Résolution des crews `<nom>/crew.ork.ts` : `CrewDirectories`, `CrewFileName` (`crew.ork.ts`), `RunCrewTimeout` (10 min) | `ScriptHostFacade` |
| `Orkeon:Cli:ConsoleStreaming` | Deltas `ctx.llm.act` streamés sur la console REPL (`Enabled` défaut `false`) | `AddLlmConsoleStreaming(configuration)` |
| `Orkeon:Cli:Session:ContextWindowTokens` | Fenêtre de contexte utilisée par `token_budget` et le TUI | `TokenBudgetTool`, ConsoleApp |
| `Orkeon:Cli:Tui:SpinnerVerbs` | Verbes du spinner TUI | ConsoleApp |

### Hôte de service (`orkeon-host`)

| Section | Configure | Consommateur |
|---|---|---|
| `Orkeon:Host` | Le démon : `Crews` (chacune `Name` — unique, sans tenir compte de la casse — `Path`, `Profile:MaxConcurrentRuns` défaut 4, `Mounts` — l'espace de montages propre à la crew), `RunTimeout` (30 min), `ShutdownGracePeriod` (20 s), `LlmProfiles` (la liste blanche des `Llm:Profiles` que les crews hébergées peuvent nommer ; absente, tous sont offerts, `["default"]` n'offre que le défaut ; une entrée qui nomme un profil non défini refuse le démarrage) | `Orkeon.Host` — voir [Hôte de service](../architecture/service-host.md) |
| `Orkeon:Host:Discord` | Canal Discord : `Enabled`, `TokenEnvironmentVariable` (`ORKEON_DISCORD_TOKEN`), `AllowedUserIds`, `GuildIds`, `ProgressInterval` (2 s), `Routes` (identifiant de salon Discord → nom de crew : un fil ouvert dans ce salon démarre cette crew), `DefaultCrew` (la crew qu'atteint un salon sans route ; la première crew déclarée si absent). Une route vers une crew non déclarée, une clé de route qui n'est pas un identifiant de salon ou un `DefaultCrew` inconnu refusent le démarrage | idem |

### Multi-modal

| Section | Configure | Opt-in |
|---|---|---|
| `Orkeon:MultiModal` | Validation vision/contenu (`Enabled`, `MaxImageSizeBytes` 20 Mo, `SupportedImageFormats`, `SupportedAudioFormats`, `MaxAudioDurationSeconds`) — aucune image n'est redimensionnée | `AddOrkeonMultiModal(configuration)` |

---

> **Voir aussi** : [Sous-systèmes opt-in](./opt-in-subsystems.md) ·
> [Hébergement](./hosting.md) ·
> [Système de mémoire](../architecture/memory-system.md) ·
> [Pipeline RAG](../architecture/rag-pipeline.md) ·
> [Retour à l'index](../INDEX.md)
