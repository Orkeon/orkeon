> 🇬🇧 [English version](../../reference/opt-in-subsystems.md)

# Sous-systèmes opt-in

> Décision R4.9 (QCM 2026-06-11) : les 13 ports DI dormants identifiés par l'audit
> (MORT-003) et les sous-systèmes associés sont **conservés**, **sortis de
> l'enregistrement par défaut** et **activables explicitement**. Aucun de ces services
> n'est enregistré par `AddOrkeonApplication()` ni par `AddOrkeonInfrastructure()`
> (les deux surcharges) : chacun s'active par son extension `AddOrkeonXxx()` dédiée.

## Principe

Ces sous-systèmes sont complets et testés unitairement, mais **aucun chemin
d'exécution de production ne les consomme encore** : les enregistrer par défaut
gonflait le conteneur DI sans bénéfice et masquait leur statut réel. Le passage en
opt-in rend leur activation **intentionnelle, documentée et vérifiable**.

**Aucun binaire livré ne les active.** `orkeon run`, `orkeon-host` et `orkeon-repl` n'appellent
aucune des extensions qui lisent ces sections : `ToolRateLimiting`, `TokenBudget`, `Orkeon:Dlp`, `Orkeon:Monitoring`,
`Orkeon:CognitiveMemory`, `Orkeon:MultiModal`, `Plugins`, `Evaluation`, `Orkeon:VectorSearch`,
`Orkeon:Checkpointing` et `Orkeon:ExecutionState:Persistence`. Écrite dans
les réglages de l'un de ces binaires, une telle section n'est lue par rien : le run démarre sans
la limite, le budget ou le filtrage qu'elle décrit. Le binaire le dit à son démarrage — une ligne
sur stderr par section, qui nomme l'extension par laquelle un hôte écrit en C# la lit — et
`orkeon doctor` le rapporte en ligne `warn` de `runner-settings`. Il démarre quand même : un
fichier de réglages partagé avec un hôte C# est légitime. Voir
[Quand un réglage est refusé](./configuration.md#quand-un-réglage-est-refusé).

Chaque extension d'activation est **autoporteuse** : elle enregistre (via `TryAdd*`)
toutes les dépendances dont le sous-système a besoin et qui ne sont pas déjà
fournies par l'hôte. Prérequis communs à tous : le logging
(`services.AddLogging()`) et, pour les sous-systèmes qui lient des options de
configuration, un `IConfiguration` enregistré (toujours présent dans une
application basée sur `Host`/`WebApplication`).

L'ordre recommandé est d'appeler l'extension **après** `AddOrkeonInfrastructure()` :
les `TryAdd*` laissent alors la priorité aux services déjà câblés par le socle.

```csharp
services.AddOrkeonApplication();
services.AddOrkeonInfrastructure();

// Activations explicites (exemples)
services.AddOrkeonMonitoring(configuration);
services.AddOrkeonDlp();
services.AddOrkeonA2A(options => options.EnableServer = true);
```

## Catalogue

| Sous-système | Activation | Projet | Ports exposés | Maturité |
|---|---|---|---|---|
| Serveur/protocole A2A | `AddOrkeonA2A(...)` | Infrastructure | `IA2AServer`, `IA2AClient`, `IA2AAgentDiscovery`, `IA2ATaskRouter` | Bêta |
| Backend de monitoring | `AddOrkeonMonitoring(...)` | Infrastructure | `IMetricsAggregation`, `ITraceExplorer` | Bêta |
| Conformité NIST | `AddOrkeonNistCompliance()` | Infrastructure | `INistComplianceReporter` | Expérimental |
| DLP (Data Loss Prevention) | `AddOrkeonDlp()` | Infrastructure | `IDlpPolicyProvider`, `IPiiDetector`, `IDlpInterceptor` (×5) | Expérimental |
| Rate-limiting d'outils & budget de tokens | `AddOrkeonToolRateLimiting()` | Infrastructure | `IToolRateLimiter`, `ITokenBudgetTracker` | Expérimental |
| Rotation de clés | `AddOrkeonKeyRotation()` | Infrastructure | `IKeyRotationService` | Bêta |
| Benchmarking d'évaluation | `AddOrkeonBenchmarking()` | Infrastructure | `IBenchmarkRunner` | Bêta |
| Contenu multi-modal (vision) | `AddOrkeonMultiModal(...)` | Infrastructure | `IContentValidationService`, `IMultiModalContentLoader` | Bêta (réel depuis R3.9) |
| Hooks de kickoff | `AddOrkeonKickoffHooks()` | Application | `ICrewKickoffHookRunner` | Expérimental |
| Contexte de codebase (RaggableTree) | `AddRaggableTree(options)` — ⚠️ **les hôtes runner font l'inverse (opt-out)** : `RunnerHost` l'enregistre par défaut et `RaggableTree:Enabled = false` le désactive | Analysis | `ICodebaseContextProvider` | Bêta |
| Sous-système RAG (RAG-02…06) | `AddOrkeonRag(config)` (namespace `Orkeon.Rag.DependencyInjection`) + `AddOrkeonRagTools()` (`Orkeon.Tools.Rag`) ; profils `fast`/`balanced`/`quality`/`adaptive`/`corrective` via `Orkeon:Rag:Profile` (défaut `fast`) ; `balanced`/`quality`/`adaptive` exigent le cross-encoder ONNX — `AddOrkeonOnnxReranker()` (`Orkeon.Rag.Onnx` + `Orkeon.Rag.Onnx.Model`, poids embarqués, hors-ligne — enregistré par les trois binaires livrés, `orkeon`, `orkeon-host` et le REPL, pas par `RunnerHost`) ; `corrective` non (le graphe boucle au lieu de reranker) ; le repli web correctif est purement config — `AddOrkeonRag` câble déjà le transport, les deux interrupteurs `Enabled` gouvernent (`Orkeon:Rag:Corrective:WebFallback` politique, `Orkeon:Rag:WebFallback` transport) — voir la section détaillée plus bas | Orkeon.Rag.Abstractions / Orkeon.Rag / Orkeon.Tools.Rag / Orkeon.Rag.Onnx / Orkeon.Rag.Onnx.Model | `IRagPipeline` (à étages / graphe correctif), `IRagProfileResolver`, `IIngestionPipeline`, `IDocumentStore`, `IRagEvalHarness`, `rag_search`/`rag_ingest`/`rag_eval` (`IBaseTool`) | Bêta |
| Étages de middleware EventHub | `AddOrkeonEventHubObservability()` (journalisation + télémétrie) · `AddOrkeonEventHubAcl(policy?)` (applique les blocs `links:` des crews — **déjà câblé par le host du runner et `orkeon-host`, avec le défaut permissif** : une crew sans liens se comporte comme avant, une crew qui déclare y est tenue ; seule la politique fermée reste un opt-in) · `AddOrkeonEventHubIdempotency(capacity?)` (point à point seulement, en mémoire, ne survit pas au processus) · `AddOrkeonEventHubValidation()` (un `SchemaId` déclaré doit être enregistré — l'existence du contrat, pas la conformité de la charge utile). Chacun est indépendant ; l'ordre d'enregistrement est l'ordre du pipeline — voir [EventHub §12](../architecture/event-hub-and-crew-lifecycle.md) | Infrastructure | `IEventHubMiddleware`, `ICrewLinkProvider`/`ICrewLinkRegistry`, `IEventSchemaRegistry` | Bêta |
| Persistance d'état d'exécution (R3.8) | `AddCrewExecutionStatePersistence(...)` | Infrastructure | `ICrewExecutionStateManager` (durable via `IStateStore`) | Bêta |
| Barrière de permissions (par appel d'outil) | `AddOrkeonPermissionGate(config)` + `Orkeon:Security:PermissionGate:Enabled = true` | Infrastructure | `IPermissionGate` (`ModePermissionGate`) — consommé par la boucle scriptée `ctx.llm.act` | Bêta |
| Shell : interpréteurs & git mutant | config seule : `Orkeon:Tools:Shell:AllowInterpreters = true` | Tools.Code | (ré-enregistre `ShellCommandTool` avec `allowInterpreters: true` — équivalent RCE, avertissement de sécurité émis) | Bêta |
| Shell : allowlist personnalisée | config seule : `Orkeon:Tools:Shell:ExtraAllowedCommands` (additive) / `Orkeon:Tools:Shell:AllowedCommands` (remplacement intégral — annule `AllowInterpreters`) | Tools.Code | (façonne l'allowlist d'exécutables de `ShellCommandTool` ; section absente/vide = défauts) | Bêta |
| Streaming console LLM natif | `AddLlmConsoleStreaming(config)` + `Orkeon:Cli:ConsoleStreaming:Enabled = true` | Cli.Commands.Scripting | `ILlmDeltaSink` (`ConsoleLlmDeltaSink`) — deltas `ctx.llm.act` streamés rendus sur la console REPL, l'act de premier plan à mesure, tout autre tour ligne entière par ligne entière | Bêta |
| Système de plugins | `AddOrkeonPlugins(...)` (jamais enregistré implicitement ; `orkeon-repl` l'appelle quand ses réglages portent une section `Plugins`, aucun autre binaire livré ne le fait) | Orkeon.Plugins | `IOrkeonPlugin`, `IPluginRegistry` — découverte par répertoire, `AssemblyLoadContext` collectables isolés ; ⚠️ les assemblies chargées s'exécutent en pleine confiance — voir [Plugins](../architecture/plugins.md) | Bêta |
| Embeddings locaux sur machine | `AddOrkeonLocalEmbeddings()` | Tools.Embeddings.Local | `IEmbeddingProvider` (BGE-micro-v2 ONNX, 384 dims, CPU, sans clé API) — premier maillon de la chaîne de résolution des embeddings | Bêta |
| Fournisseurs de mémoire externes (Redis, SQLite, ChromaDB, Pinecone, LanceDB) + migration | Un type — `Memory:Provider`, le `memoryProvider:` d'une crew, `Orkeon:Rag:Provider` — plus la section hôte de ce provider (`Orkeon:Redis`, `Orkeon:Sqlite`, `Orkeon:ChromaDb`, `Orkeon:Pinecone`, `Orkeon:LanceDb`), toutes liées par `AddOrkeonInfrastructure()` ; `MemoryProviderFactory` distribue une instance partagée par type, qui se connecte au premier usage. `AddOrkeonRedisMemory(configuration)` fait de Redis l'`IMemoryProvider` de l'application ; `AddOrkeonLanceDb`/`AddOrkeonChromaDb`/`AddOrkeonPinecone(configuration)` exposent le provider partagé par sa classe (les deux dernières sont appelées par `AddOrkeonInfrastructure(configuration)` quand leur section existe) · `AddOrkeonMemoryMigration()` | Infrastructure | `RedisMemoryProvider`, `LanceDbMemoryProvider` (+ `LanceDbMigrationService`), `MemoryMigrationService` — voir [Système de mémoire](../architecture/memory-system.md#sélection-par-configuration) | Bêta |
| Mémoire cognitive | `AddOrkeonCognitiveMemory(configuration)` (`Orkeon:CognitiveMemory` ; aucun binaire livré ne l'appelle) | Infrastructure | `ICognitiveMemoryService` — analyse LLM, détection de contradictions, consolidation, score de rappel composite ; requiert un `ILlmProvider` et un `IEmbeddingProvider` — voir [Système de mémoire](../architecture/memory-system.md#mémoire-cognitive) | Expérimental |
| Journalisation des échanges LLM | `AddLlmExchangeLogging(logDirectory, options?)` (capture JSONL complète + résumé structuré, handler injecté dans tous les clients `IHttpClientFactory` sauf ceux qu'une famille d'outils exclut par `LlmExchangeLogExclusion` — les clients drive et mail) — câblé par `RunnerHost` uniquement quand un run passe `--llm-log` (section `LlmLogging`) | Infrastructure | `ILlmExchangeLogger`, `LlmLoggingDelegatingHandler` — voir [Fournisseurs LLM](../architecture/llm-providers.md) | Bêta |
| Outil d'entrée humaine | `AddOrkeonHumanInput()` (provider de repli qui approuve tout) / `AddOrkeonHumanInput<TProvider>()` — câblé par le chemin d'exécution des runners | Infrastructure | `human_input` (`IBaseTool`), `IHumanInputProvider` | Bêta |
| Outils de session | `AddOrkeonSessionTools(configuration?)` (`Llm:AvailableModels`) — câblé par `RunnerHost` et le REPL | Infrastructure | `session_store`, `session_snip`, `token_budget`, `memory_store`, `session_cost`, `session_stats` ; `ISessionBufferService`, `ICategoryMemoryStore` — voir [Agent de code (TS)](../architecture/coding-agent-ts.md) | Bêta |
| Outil de recherche sémantique | `AddSemanticSearchTool()` (`Orkeon.Hosting`) — câblé par `orkeon run` | Hosting | `semantic_search` (`SearchTool`) + adaptateur `IEmbeddingService` sur `IEmbeddingProvider` + `IVectorMemoryStore` en mémoire | Bêta |
| Cœur EventHub + outils agents | `AddOrkeonInMemoryEventHub()` · `AddOrkeonEventHubTools()` — tous deux câblés par `RunnerHost` (donc `orkeon run` et `orkeon-host`) | Infrastructure / Tools.EventHub | `IEventHub` (in-process), les sept outils EventHub — voir [EventHub](../architecture/event-hub-and-crew-lifecycle.md) | Bêta |
| Outils e-mail | `AddOrkeonEmailTools(configuration)` (`Orkeon:Tools:Email`, inerte tant qu'aucun compte n'est déclaré) · `AddOrkeonEmailTokenStore(fileSystem, virtualDirectory)` (store des jetons OAuth) — tous deux câblés par `RunnerHost` ; le REPL ne câble que les outils | Tools.Email | les 13 outils `email_*` — voir [Outils e-mail](../guides/email.md) | Bêta |
| Pont Microsoft Agent Framework | `AddOrkeonAgentFramework()` | Interop.AgentFramework | `ICrewAgentFactory` (une crew vue comme `AIAgent`, chargée et lancée dans un scope à elle à chaque tour) — voir [ADR-010](../adr/ADR-010-agent-framework-interop.md) | Bêta |


> **Hors de ce catalogue — enregistrés par `AddOrkeonInfrastructure()` et
> gouvernés par la configuration, pas par un geste d'enregistrement** : MCP
> (section `MCP`, via la surcharge `IConfiguration`, ou l'hôte des runners dès que
> `MCP:Servers` déclare un serveur), la télémétrie et la recherche vectorielle (même
> surcharge), le store de checkpointing sans paramètre (`AddOrkeonCheckpointing()` ;
> les variantes SQLite/Postgres restent explicites), Evaluation, Consensus,
> CostTracking, YAML, Encryption et CodeSandbox. Le
> pipeline Guardian, les sanitizers de prompt et de résultats d'outils et le point
> d'invocation des outils sont eux aussi enregistrés, et **chaque tour d'agent les traverse**
> (`Orkeon:Guardian:Enabled` en est l'interrupteur — voir [Sécurité](../architecture/security.md)).
> Leurs sections de configuration sont cartographiées dans la
> [Référence de configuration](./configuration.md).
>
> **Les familles d'outils** s'enregistrent par leurs propres extensions, que l'hôte des
> runners et le REPL appellent : `AddOrkeonFileSystemTools`, `AddOrkeonWebTools` (plus les
> extensions à outil unique `AddOrkeonWebSearchTool`, `AddOrkeonBraveSearchTool`,
> `AddOrkeonCacheSearchTool`, `AddOrkeonSlackTool`, `AddOrkeonSlackReadTool`),
> `AddOrkeonDataTools` (plus `AddRelationalDatabaseTools`, `AddMongoDbTools`,
> `AddGraphDatabaseTools`), `AddOrkeonCodeTools`, `AddOrkeonAbstractionTools`,
> `AddRaggableTreeTools` — voir l'[inventaire des outils](../tools/inventory.md).

## Briques enregistrées par `AddOrkeonInfrastructure()`

Ce ne sont pas des opt-ins, mais des extensions publiques qu'un hôte croise quand il compose
son propre conteneur : chacune est appelée par `AddOrkeonInfrastructure()` (toutes utilisent
`TryAdd*`, les rappeler est donc sans effet), et chacune peut aussi s'appeler seule quand un
hôte construit un conteneur minimal sans l'infrastructure complète.

| Extension | Enregistre | Consommée par | L'appeler soi-même quand |
|---|---|---|---|
| `AddOrkeonYaml()` | `CrewFactoryOptions` (options simples, résolution d'outils tolérante), `ICrewDefinitionLoader` → `YamlCrewDefinitionLoader`, `ICrewFactory` → `CrewFactory` (scoped), `YamlCrewExporter` | le chargement de crews des runners ; `orkeon forge` | vous chargez ou exportez des crews YAML sans `AddOrkeonInfrastructure()`. La résolution stricte des outils est `Orkeon:CrewFactory:StrictTools`, que lit `RunnerHost` (défaut `true` chez lui) |
| `AddOrkeonGuardian()` | `GuardianOptions` (`Orkeon:Guardian`), `GuardianPolicyEngine`, `InputGuard`/`ToolGuard`/`DelegationGuard`, `GuardianPipeline` (aussi comme `IGuardianPipeline`) avec les trois gardes branchés sur leurs phases — aucun quand `Enabled = false` | l'orchestrateur d'exécution (phase d'entrée), le service de streaming, et `IToolInvocationPipeline` (phases outil et délégation) — enregistré par `AddOrkeonApplication()` | vous composez un conteneur sans `AddOrkeonInfrastructure()` et voulez quand même les gardes — voir [Sécurité](../architecture/security.md) |

Le service de migration de mémoire est la seule extension de déplacement de données qui n'est
*pas* enregistrée par défaut : `AddOrkeonMemoryMigration()` — voir [Système de mémoire](../architecture/memory-system.md#sélection-par-configuration).

**Maturité** — *Bêta* : implémentation complète et testée, API susceptible d'évoluer
avant la v1. *Expérimental* : implémentation fonctionnelle mais non câblée dans le
pipeline d'exécution (l'hôte invoque le service lui-même) ou comportement encore
partiel (signalé au cas par cas ci-dessous).

---

## Serveur/protocole A2A — `AddOrkeonA2A(...)`

> **Complément persistance des tâches (PUB-08)** : `AddOrkeonA2ATaskPersistence()` stocke
> les cycles de vie des tâches A2A sur l'`IStateStore` de checkpointing opt-in (à
> enregistrer d'abord), transformant `GET /a2a/tasks/{id}` en vrai endpoint 200/404 au
> lieu du 501 explicite.

- **Rôle** : communication agent-à-agent inter-processus (protocole A2A) :
  découverte d'agents (`/.well-known/agent.json`), client HTTP, routage de tâches
  et, en option, un serveur HTTP (`HttpListener`) exposant `POST /a2a/tasks/send`,
  `sendSubscribe` (SSE), `GET/DELETE /a2a/tasks/{id}`.
- **Activation** :
  ```csharp
  // Par configuration (section "A2A" ; "A2A:Security" pour mTLS/auth)
  services.AddOrkeonA2A(configuration);

  // Ou par délégué, sans IConfiguration
  services.AddOrkeonA2A(options => options.EnableServer = true);
  ```
  `IA2AServer` n'est enregistré que si `EnableServer` est vrai, avec un service hébergé :
  un hôte générique démarre le serveur avec lui et l'arrête à l'extinction — aucun code
  de l'hôte ne résout `IA2AServer` ni n'appelle `StartAsync`. Un processus qui ne fait
  jamais tourner l'hôte ne le démarre jamais.
- **Exécution** : la carte liste les skills du routeur enregistré
  (`IA2ATaskRouter.GetSkillsAsync`) — celui par défaut, une skill par agent disponible — et
  le routeur exécute l'agent dont l'id égale le `skillId` de la requête (l'`id` que publie
  la carte) via `IAgentExecutionService` ; l'hôte appelle donc aussi
  `AddOrkeonApplication()` — sans lui, une tâche répond `Failed` et le dit. Un hôte qui
  route autrement enregistre d'abord son propre `IA2ATaskRouter` : le routeur par défaut, et
  l'annuaire d'agents ci-dessous, ne sont enregistrés que si aucun routeur ne l'est.
- **Dépendances** : l'extension enregistre elle-même (TryAdd) `IHttpClientFactory`,
  `IDomainEventDispatcher`, `IUnitOfWork`, ainsi que — avec le routeur par défaut —
  l'annuaire d'agents A2A (`IAgentRegistrationStore` singleton + `IAgentRepository`
  scoped, voir ci-dessous) lorsque l'hôte ne les a pas déjà câblés via
  `AddOrkeonInfrastructure()`.
- **Cycle de vie du dépôt d'agents (R4.6 / ANT-001, décision « scoped par requête »)** :
  - `IAgentRepository` est **scoped** ; le routeur (un singleton) ne le capture jamais —
    il ouvre un **scope DI par requête A2A** (la carte, chaque tâche) via
    `IServiceScopeFactory` et résout le dépôt dedans. La résolution passe avec
    `ValidateScopes = true` (défaut en Development).
  - Un dépôt scoped ne conserve rien entre deux requêtes : les enregistrements vivent
    dans `IAgentRegistrationStore`, un **backing store singleton thread-safe**
    (`InMemoryAgentRegistrationStore`, `ConcurrentDictionary`) dont le dépôt scoped
    (`SharedStoreAgentRepository`) s'hydrate à chaque appel. Les agents enregistrés par
    le pipeline (dans ses propres scopes) sont donc visibles du routeur A2A, et
    réciproquement.
  - Composition : si l'hôte a appelé `AddOrkeonInfrastructure()` (qui enregistre le
    `InMemoryAgentRepository` à store **par scope**), `AddOrkeonA2A(...)` **surclasse
    cette implémentation par défaut** vers `SharedStoreAgentRepository` (même durée de
    vie scoped, données partagées) — sinon le routeur ne retrouverait jamais les agents.
    Tout **autre** enregistrement d'`IAgentRepository` (dépôt custom/BD, qui fournit déjà
    un stockage inter-requêtes) est laissé intact. Appeler `AddOrkeonA2A(...)` **après**
    `AddOrkeonInfrastructure()` et après un éventuel dépôt custom (ordre recommandé,
    cf. [Principe](#principe)) ; un dépôt custom enregistré *après* `AddOrkeonA2A(...)`
    gagne (dernier enregistrement).
- **Binaires livrés** : `orkeon-host` appelle `AddOrkeonA2A` quand `Orkeon:Host:A2A:Enabled`
  est posé, après avoir enregistré son propre routeur — une skill par crew que la section
  expose, une tâche étant un run de cette crew — de sorte que ses runs gardent leur dépôt
  d'agents par scope (voir [Hôte de service](../architecture/service-host.md#5-autres-agents-a2a)) ;
  `orkeon run` et le REPL ne l'appellent pas. Voir [Conformité A2A — Exécution des tâches](./a2a-conformance.md#exécution-des-tâches)
  et [Activation](./a2a-conformance.md#activation).
- **Limites connues** : le store in-memory est local au processus — pour un annuaire
  d'agents multi-instances, fournir un `IAgentRepository` custom adossé à un stockage
  partagé externe (il sera respecté tel quel par l'extension).

## Backend de monitoring — `AddOrkeonMonitoring(...)`

- **Binaires livrés** : aucun ne l'active. `orkeon run`, `orkeon-host` et `orkeon-repl` ne lisent
  pas `Orkeon:Monitoring`, et le disent à leur démarrage quand les réglages l'écrivent.
- **Rôle** : agrégation in-process des métriques Orkeon (`MeterListener` sur
  le meter `Orkeon`) et exploration des traces récentes (buffer circulaire) pour
  exposition par un endpoint hôte (dashboard, API de diagnostic).
- **Activation** :
  ```csharp
  services.AddOrkeonMonitoring(configuration); // lie Orkeon:Monitoring
  services.AddOrkeonMonitoring();              // options par défaut
  ```
- **Dépendances** : logging uniquement (les options par défaut sont enregistrées si
  aucune configuration n'est passée).
- **Note** : ne remplace pas la télémétrie OpenTelemetry (`AddOrkeonTelemetry`),
  qui reste câblée par la surcharge `AddOrkeonInfrastructure(IConfiguration)`.

## Traces et métriques suivent les conventions GenAI d'OpenTelemetry

Pas un opt-in — chaque run les émet, et les runners (`orkeon run`, `orkeon-host`) les
exportent là où pointe la variable standard `OTEL_EXPORTER_OTLP_ENDPOINT` (un AppHost .NET
Aspire la pose — voir [ADR-011](../adr/ADR-011-aspire-dashboard-observability.md)) ou là
où la section `Telemetry` des réglages le dit (`Telemetry:OtlpEndpoint`). Depuis le 2026-09-11 le chemin d'exécution lui-même
porte les spans (ils vivaient dans des helpers que rien n'appelait en production), nommés
et attribués selon les [conventions sémantiques OpenTelemetry pour l'IA générative](https://opentelemetry.io/docs/specs/semconv/gen-ai/),
si bien que Langfuse, Honeycomb, Application Insights ou le dashboard Aspire les lisent
sans mapping. Les noms sont les constantes de `Orkeon.Constants.Llm.GenAiAttributes`.

| Span (`ActivitySource`) | Nom | Attributs |
|---|---|---|
| Tour d'agent (`Orkeon.Agent`) | `invoke_agent {agent}` | `gen_ai.operation.name=invoke_agent`, `gen_ai.agent.name`, `gen_ai.agent.id`, `gen_ai.provider.name`, `gen_ai.request.model`, `orkeon.task.id` |
| Appel au modèle (`Orkeon.Llm`, kind Client) | `chat {model}` | `gen_ai.operation.name=chat`, `gen_ai.provider.name` (minuscules), `gen_ai.request.model`, `gen_ai.request.temperature`, `gen_ai.request.max_tokens`, `gen_ai.response.model`, `gen_ai.response.id`, `gen_ai.response.finish_reasons`, `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `error.type` en cas d'échec ; hors d'un tour d'agent (le manager, le planificateur, un appel propre à l'hôte) l'entrée des fournisseurs l'ouvre, avec `orkeon.llm.operation`, `gen_ai.agent.name` et `orkeon.task.id` quand ils sont connus — un par appel, jamais un second dans un `chat` courant |
| Embedding (`Orkeon.Llm`) | `embeddings {model}` | `gen_ai.operation.name=embeddings`, `gen_ai.provider.name`, `gen_ai.request.model`, `orkeon.embeddings.text_count`, `error.type` en cas d'échec ; un par appel au modèle d'embedding — un embedding servi par le cache n'en a pas |
| Exécution d'outil (`Orkeon.Tool`) | `execute_tool {tool}` | `gen_ai.operation.name=execute_tool`, `gen_ai.tool.name`, `gen_ai.tool.call.id` (l'identifiant donné par le modèle), `gen_ai.agent.name`, `error.type` en cas d'échec |
| Exécution d'un crew (`Orkeon.Crew`) | `crew.run` | `orkeon.crew.id`, `orkeon.crew.name`, `orkeon.crew.process`, `orkeon.crew.task_count` ; en erreur quand l'exécution échoue ou est annulée |
| Tâche (`Orkeon.Task`) | `task.run` | une par tâche, `orkeon.task.id` ; en erreur quand la tâche échoue ou est annulée |
| Mémoire (`Orkeon.Memory`) | `memory.recall`, `memory.store` | sous la tâche qui rappelle ou stocke |
| Runtime de scripting (`Orkeon.Scripting`) | les trois mêmes, plus `crew.run` | mêmes attributs ; `orkeon.llm.method` (`complete`/`act`), `orkeon.crew.name`, `orkeon.crew.process` ; la source figure dans `OrkeonActivitySources.AllNames`, donc les spans du script sont exportés |

Une exécution est une trace : `crew.run` > `task.run` > `invoke_agent` et `memory.*` ; les `chat` du manager et du planificateur se placent sous `crew.run`, les `embeddings` d'une mémoire sous son span `memory.*`.

Métriques (meter `Orkeon`, `OrkeonMeter` ; le `MeterListener` l'écoute) : `gen_ai.client.token.usage` (histogramme,
tokens, `gen_ai.token.type` = `input` | `output`) et `gen_ai.client.operation.duration`
(histogramme, secondes), toutes deux étiquetées `gen_ai.provider.name` et
`gen_ai.request.model` ; les compteurs `orkeon.*` (appels, exécutions d'outils, coût)
gardent leurs noms. Ce qui n'a pas de convention garde le préfixe `orkeon.` — le crew, la
tâche, le coût estimé.

| Instrument | Type, unité | Attributs | Mesuré |
|---|---|---|---|
| `orkeon.llm.calls` | compteur, `{call}` | opération, fournisseur, modèle | chaque appel au modèle, une fois |
| `gen_ai.client.operation.duration` | histogramme, `s` | opération, fournisseur, modèle, `error.type` en cas d'échec | chaque appel au modèle |
| `gen_ai.client.token.usage` | histogramme, `{token}` | opération, fournisseur, modèle, `gen_ai.token.type` | l'usage rapporté par le fournisseur ; un usage non rapporté n'est pas enregistré |
| `orkeon.cost.total_usd` | jauge observable, `USD` | aucun | total, à l'échelle du processus, des coûts en USD rapportés depuis son démarrage ; un coût dans une autre devise n'y entre pas |
| `orkeon.crew.executions` | compteur, `{execution}` | `orkeon.crew.name`, `orkeon.crew.process`, `orkeon.outcome` (`completed`, `failed`, `cancelled`) | chaque exécution de crew, une fois, à sa fin — avec ou sans écouteur de spans |
| `orkeon.crew.duration` | histogramme, `s` | ceux de `orkeon.crew.executions` | chaque exécution de crew, à sa fin |
| `orkeon.crew.active` | compteur haut-bas, `{crew}` | `orkeon.crew.name`, `orkeon.crew.process` | +1 au début d'une exécution, -1 à sa fin |
| `orkeon.task.executions` | compteur, `{execution}` | `orkeon.crew.name`, `orkeon.agent.role` quand la tâche a un seul agent, `orkeon.outcome` (`completed`, `failed`, `cancelled`) | chaque tâche, une fois, à sa fin ; une tâche écartée parce qu'une dépendance a échoué, ou encore ouverte à la fin de l'exécution, compte `cancelled` |
| `orkeon.task.duration` | histogramme, `s` | ceux de `orkeon.task.executions` | chaque tâche, à sa fin |
| `orkeon.task.active` | compteur haut-bas, `{task}` | `orkeon.crew.name`, `orkeon.agent.role` quand la tâche a un seul agent | +1 au début d'une tâche, -1 à sa fin |
| `orkeon.tool.executions` | compteur, `{execution}` | `orkeon.tool.name`, `orkeon.outcome` (`success`, `failure`, `blocked`, `cancelled`) | chaque appel passé par le pipeline d'outils, une fois, avec ou sans journal d'audit ; une réponse `Success=false` compte `failure` |
| `orkeon.tool.duration` | histogramme, `s` | `orkeon.tool.name`, `orkeon.outcome` | chaque appel exécuté ; un appel `blocked` n'a pas de durée |
| `orkeon.security.events` | compteur, `{event}` | `orkeon.security.kind` (`block`, `warn`, `injection`), `orkeon.tool.name` quand il est connu | un par appel que le Guardian bloque (une garde qui lève une exception compte `block`), un par résultat `Warn`, un par résultat d'outil où un motif d'injection est repéré (pas un par motif) |
| `orkeon.llm.active` | compteur haut-bas, `{call}` | aucun | déclaré, pas encore alimenté |

Aucun attribut ne porte l'identifiant d'un crew, d'une tâche ou d'un agent.

## Conformité NIST — `AddOrkeonNistCompliance()`

- **Rôle** : génération de rapports de conformité NIST SP 800-53 à partir de la piste
  d'audit (`IAuditLogger`).
- **Activation** : `services.AddOrkeonNistCompliance();`
- **Dépendances** : l'extension TryAdd une chaîne d'audit de repli
  (`StructuredLogAuditSink` + `AuditLogger`) ; quand l'hôte a appelé
  `AddOrkeonInfrastructure()`, la chaîne d'audit du socle (sinks structuré +
  in-memory, options `Security:Audit`) est utilisée.
- **Limites connues** : le rapport lit la piste qu'écrivent les tours d'agent — un
  événement `ToolExecution` par appel d'outil et un `SecurityEvent` par blocage ou
  avertissement du Guardian et par motif d'injection trouvé dans un résultat d'outil ; les
  appels LLM, les accès fichiers et les requêtes HTTP ne sont pas des événements audités ;
  aucun contrôle automatisé n'est exécuté.

## DLP — `AddOrkeonDlp()`

- **Binaires livrés** : aucun ne l'active. `orkeon run`, `orkeon-host` et `orkeon-repl` ne lisent
  pas `Orkeon:Dlp`, et le disent à leur démarrage quand les réglages l'écrivent : la DLP n'y
  filtre rien. Ce qu'ils filtrent se règle par `Security:Prompt` et `Security:ToolResults`.
- **Rôle** : prévention de fuite de données — détection de PII par expressions
  régulières (`IPiiDetector`), politiques par canal (`IDlpPolicyProvider`, section
  `Orkeon:Dlp`) et 5 intercepteurs de canaux (sortie d'outil, délégation, logs,
  mémoire, sortie externe).
- **Activation** : `services.AddOrkeonDlp();`
- **Dépendances** : logging + `IConfiguration` (liaison `Orkeon:Dlp`). Autoporteuse
  pour le reste.
- **Limites connues** : les intercepteurs ne sont **pas** invoqués automatiquement par
  le pipeline d'exécution — l'hôte les résout (`GetServices<IDlpInterceptor>()`) et
  les applique aux canaux qu'il veut protéger. À ne pas confondre avec le
  `PiiDetectionValidator` du pipeline de validation de sortie, qui reste enregistré
  par défaut et est indépendant.

## Rate-limiting d'outils & budget de tokens — `AddOrkeonToolRateLimiting()`

- **Binaires livrés** : aucun ne l'active. `orkeon run`, `orkeon-host` et `orkeon-repl` ne lisent
  ni `ToolRateLimiting` ni `TokenBudget`, et le disent à leur démarrage quand les réglages en
  écrivent une. Les limites qu'ils appliquent sont celles des appels au modèle : `RateLimiting`.
- **Rôle** : limitation de débit par outil (`IToolRateLimiter`, section
  `ToolRateLimiting`) et suivi de budget de tokens par agent/crew
  (`ITokenBudgetTracker`, section `TokenBudget`).
- **Activation** : `services.AddOrkeonToolRateLimiting();`
- **Dépendances** : logging + `IConfiguration`. Si `AddOrkeonCostTracking()` (inclus
  dans le socle) a enregistré `IModelPricingRegistry`, le tracker l'utilise pour la
  conversion tokens → coût.
- **Limites connues** : non câblé dans l'exécution des outils — l'hôte interroge le
  limiteur/le tracker autour de ses appels d'outils. Le rate-limiting **LLM**
  (`ILlmRateLimiter`), lui, reste enregistré par défaut car il est posé à l'entrée de
  chaque provider que l'hôte construit ou enregistre — chaque appel au modèle y prend un
  bail, tour d'agent ou non (GAP-38).

## Rotation de clés — `AddOrkeonKeyRotation()`

- **Rôle** : re-chiffrement **réel** (R2.8) des entrées d'un store mémoire chiffré lors
  d'une rotation de clé —
  `IKeyRotationService.RotateAsync(store, oldProvider, newProvider, options?)` — plus la
  génération et la persistance d'une nouvelle clé via
  `ProvisionNewKeyAsync(secretStore, secretName, keySizeInBits)`
  (`IWritableSecretProvider`, implémenté par `DpapiSecretProvider`).
- **Fonctionnement** : parcours en deux phases. *Staging* : chaque entrée est déchiffrée
  avec l'ancienne clé, re-chiffrée avec la nouvelle et écrite sous une clé de staging
  (`<clé>::rotation-staging`) — les originaux restent intacts. *Bascule* : chaque copie
  re-chiffrée remplace son original puis le staging est supprimé. Un marqueur d'état
  (`__orkeon.key-rotation.state`, JSON en clair) persiste la version de clé et la phase ;
  chaque entrée re-chiffrée porte la propriété `orkeon-key-version`.
- **Échecs et reprise** :
  - échec en *staging* → **rollback automatique** des copies de staging : le store reste
    intégralement lisible avec l'ancienne clé ;
  - échec en *bascule* → **roll-forward** : relancer `RotateAsync` avec les mêmes
    providers reprend la rotation là où elle s'est arrêtée. La classification par
    déchiffrement authentifié (AES-GCM) garantit l'idempotence : aucune entrée perdue,
    aucune double-chiffrée ;
  - entrée illisible avec les deux clés → échec **fail-closed** par défaut (rollback) ;
    `KeyRotationOptions.ContinueOnUnreadableEntries` permet de l'ignorer (entrée laissée
    telle quelle et signalée dans `KeyRotationResult.Errors`).
- **Activation** : `services.AddOrkeonKeyRotation();` — à coupler avec
  `AddOrkeonEncryption()` (le fournisseur AES-256-GCM, lui, reste dans le socle).
- **Dépendances** : logging uniquement (le store et les providers sont passés en
  arguments de méthode).
- **Marche à suivre** :
  ```csharp
  var rotation = provider.GetRequiredService<IKeyRotationService>();

  // 1. Générer et persister la nouvelle clé sous un nom de secret versionné
  //    (refuse d'écraser un secret existant — fail-closed)
  await rotation.ProvisionNewKeyAsync(writableSecrets, "orkeon-encryption-key-v2");

  // 2. Re-chiffrer le store brut (sous le décorateur) de l'ancienne vers la nouvelle clé
  var result = await rotation.RotateAsync(rawStore, oldProvider, newProvider);

  // 3. Basculer la configuration de l'hôte sur le nouveau nom de secret
  ```
- **Limites connues** : la rotation s'exécute sur le provider mémoire **brut** (sous le
  décorateur `EncryptedMemoryProviderDecorator`, type `MemoryProviderBase` pour
  l'énumération des clés) et suppose qu'aucune écriture concurrente n'a lieu pendant la
  fenêtre de rotation. `AesEncryptionProvider.RotateKeyAsync` est neutralisé
  (`NotSupportedException`, plus aucun faux log de succès) : une rotation « sur place »
  orphelinerait les données existantes — passer par `IKeyRotationService`.

## Benchmarking d'évaluation — `AddOrkeonBenchmarking()`

- **Rôle** : exécution répétée d'une suite d'évaluation par cas de test avec
  statistiques mean/stddev (`IBenchmarkRunner`).
- **Activation** : `services.AddOrkeonBenchmarking();` — à coupler avec
  `AddOrkeonEvaluation()` (inclus dans le socle) pour construire les suites.
  L'`IEvaluationSuite` par défaut porte les juges LLM quand `Evaluation:EnableLlmJudge`
  vaut `true` — lié par `AddOrkeonInfrastructure(configuration)`.
- **Dépendances** : aucune (la suite d'évaluation est fournie via `BenchmarkConfig`
  à l'appel).

## Contenu multi-modal (vision) — `AddOrkeonMultiModal(...)`

- **Binaires livrés** : aucun ne l'active. `orkeon run`, `orkeon-host` et `orkeon-repl` ne lisent
  pas `Orkeon:MultiModal`, et le disent à leur démarrage quand les réglages l'écrivent.
- **Statut : réel (R3.9)** — la vision est câblée de bout en bout : les contenus image
  circulent des abstractions `MultiModalContent` (Domain) aux payloads providers via
  `LlmMessage.MultiModalContent`. `AnthropicLlmProvider` émet des blocs de contenu
  image conformes à l'API Messages (`{"type":"image","source":{"type":"base64"|"url",…}}`) ;
  `OpenAIProvider` émet des parts `image_url` conformes à Chat Completions (URL http(s)
  ou data URL base64). Voir le [guide multi-modal](../guides/multimodal.md).
- **Rôle** : validation des contenus multi-modaux (taille, format, durée) via
  `IContentValidationService` (options `Orkeon:MultiModal`) et chargement d'images
  depuis le système de fichiers virtuel via `IMultiModalContentLoader`
  (lecture VFS → bytes → base64, type MIME inféré de l'extension, validation des
  contraintes configurées).
- **Activation** :
  ```csharp
  services.AddOrkeonMultiModal(configuration); // lie Orkeon:MultiModal
  services.AddOrkeonMultiModal();              // options par défaut
  ```
- **Dépendances** : `IContentValidationService` n'a besoin que des options ;
  `IMultiModalContentLoader` requiert un `IFileSystemService` résolvable
  (`AddOrkeonFileSystem(configuration)` dans les hôtes réels).
- **Limites connues** : la composition des payloads vision est pilotée par capacité
  (`LlmProviderCapabilities.Vision`, traduite une fois par `OpenAICompatibleProviderBase`)
  — **les 16 providers** la déclarent (DeepSeek depuis la campagne du 2026-08-30 qui a
  mesuré `deepseek-v4-flash-vision-exp`, natif sur `deepseek-flash` depuis V4.1) ; un provider sans la capacité dégrade les
  messages multi-modaux vers leur repli texte
  (`LlmMessage.Content`). Formats image supportés : png, jpeg, gif, webp.
  Les parts audio/fichier ne sont envoyées par aucun provider et lèvent une
  `NotSupportedException` explicite si elles atteignent un payload vision.

## Hooks de kickoff — `AddOrkeonKickoffHooks()`

- **Rôle** : exécution de callbacks `BeforeKickoffHook` / `AfterKickoffHook` autour du
  kickoff d'un crew, par ordre de `Priority` croissante, avec sémantique
  `ContinueOnError` (les exceptions des hooks tolérants sont loguées puis ignorées).
- **Activation** :
  ```csharp
  services.AddOrkeonKickoffHooks(); // runner seul
  services.AddBeforeKickoffHook(new BeforeKickoffHook
  {
      Name = "audit",
      Priority = 1,
      Execute = crew => /* ... */ System.Threading.Tasks.Task.CompletedTask,
  }); // enregistre le hook ET le runner
  ```
- **Dépendances** : aucune (collections de hooks vides par défaut, logging optionnel).
- **Limites connues** : l'orchestrateur n'invoque pas (encore) le runner — son câblage
  dans le pipeline d'exécution relève de la décomposition de l'orchestrateur (R4.1).
  En attendant, l'hôte appelle lui-même :
  ```csharp
  var runner = provider.GetRequiredService<ICrewKickoffHookRunner>();
  await runner.RunBeforeKickoffAsync(crew, ct);
  var result = await orchestrator.KickoffAsync(crew.Id, input, ct);
  await runner.RunAfterKickoffAsync(crew, result, ct);
  ```

## Contexte de codebase (RaggableTree) — `AddRaggableTree(options)`

- **Rôle** : `ICodebaseContextProvider` produit des résumés de codebase destinés à
  être injectés dans le contexte des agents (fonctionnalité RaggableTree).
- **Activation** : enregistré par `AddRaggableTree(options)` (projet
  `Orkeon.Analysis`). La bibliothèque cœur ne l'appelle jamais — mais le **runner
  host, lui, l'appelle par défaut** (`orkeon run`/`orkeon-host` ; opt-out via
  `"RaggableTree:Enabled": false`), en cohérence avec l'entrée de ce catalogue plus
  haut. Voir [raggable-tree.md](../architecture/raggable-tree.md).
- **Limites connues** : aucun composant du framework ne le consomme automatiquement —
  l'hôte le résout et injecte les résumés là où il le souhaite (prompt système,
  contexte de tâche…).

## Sous-système RAG — `AddOrkeonRag(configuration)` (RAG-02…06)

- **Rôle** : génération augmentée par récupération — ingestion (loaders,
  chunking, validation, manifeste incrémental), pipeline de récupération à
  étages (transform → retrieve → fuse (+ MMR opt-in) → rerank → assemble →
  generate → groundedness), graphe correctif CRAG, presets de profils et
  harnais d'évaluation hors ligne. Guide complet :
  [rag-pipeline.md](../architecture/rag-pipeline.md), décisions :
  [ADR-006](../adr/ADR-006-rag-subsystem.md).
- **Activation** :
  ```csharp
  services.AddOrkeonRag(configuration);   // Orkeon.Rag.DependencyInjection
  services.AddOrkeonRagTools();           // Orkeon.Tools.Rag : rag_search / rag_ingest / rag_eval
  services.AddOrkeonOnnxReranker();       // opt-in — requis par balanced/quality/adaptive
  ```
  Tout hôte runner bâti sur `RunnerHost` (`orkeon run`, `orkeon-host`) fait lui-même
  les deux premiers appels, le REPL `orkeon-repl` aussi ; les trois binaires livrés —
  `orkeon`, `orkeon-host`, `orkeon-repl` — ajoutent le troisième (`RunnerHost` non : le
  paquet `Orkeon.Hosting` reste sans ONNX).
  `AddOrkeonRag` câble déjà les transformers de requête, le routage, l'hybride
  BM25+RRF, le graphe correctif **et l'enregistrement du transport du repli
  web** — appeler soi-même `AddOrkeonRagWebFallback(configuration)` est un
  no-op ; la fonctionnalité est gouvernée par les deux interrupteurs de
  configuration ci-dessous.
- **Profils** (`Orkeon:Rag:Profile`, défaut `fast` ; les surcharges clé par clé
  s'appliquent par-dessus le preset) : `fast` (vectoriel seul) et `corrective`
  (graphe CRAG — boucle au lieu d'un étage de rerank linéaire) fonctionnent
  sans les paquets ONNX ; `balanced`/`quality` activent l'étage de rerank
  cross-encoder et `adaptive` délègue sa route `SingleShot` à `balanced`, donc
  ces trois-là **échouent explicitement à la première requête** (exception
  actionnable) tant qu'`AddOrkeonOnnxReranker()` (`Orkeon.Rag.Onnx` +
  `Orkeon.Rag.Onnx.Model`, poids int8 embarqués, hors-ligne) n'est pas
  enregistré.
- **Repli web (double opt-in, désactivé par défaut)** : le nœud `web_fallback`
  du graphe correctif ne s'exécute que quand **les deux**
  `Orkeon:Rag:Corrective:WebFallback:Enabled` (politique) et
  `Orkeon:Rag:WebFallback:Enabled` + un `Endpoint` non vide (transport,
  SearxNG) sont posés. Chaque page téléchargée est filtrée par
  `PromptInjectionDocumentValidator` (`Rejected` n'entre jamais dans le working
  set ; `Suspicious` suit la politique configurée) — voir
  [security.md](../architecture/security.md).
- **Projets** : `Orkeon.Rag.Abstractions` (contrats, dépendance Domain seule),
  `Orkeon.Rag` (implémentations), `Orkeon.Tools.Rag` (outils agents),
  `Orkeon.Rag.Onnx` + `Orkeon.Rag.Onnx.Model` (reranker opt-in + poids).
- **Limites connues** : voir [limitations.md](./limitations.md) — dégradation
  hors ligne des nœuds dépendant d'un LLM (`corrective`/`adaptive`),
  classifieur heuristique par défaut, BM25 in-process non persisté.

## Persistance d'état d'exécution — `AddCrewExecutionStatePersistence(...)` (R3.8)

- **Binaires livrés** : aucun ne l'active. `orkeon run`, `orkeon-host` et `orkeon-repl` ne lisent
  ni `Orkeon:ExecutionState:Persistence` ni `Orkeon:Checkpointing`, celle du store Postgres, et le
  disent à leur démarrage quand les réglages en écrivent une.
- **Rôle** : persistance durable des états d'exécution de crew
  (`ScopedCrewExecutionStateManager`) dans un state store de checkpointing
  (`IStateStore`) : chaque transition (création, mise à jour, complétion, archivage)
  est persistée, et au redémarrage les états sont rechargés — y compris la **reprise**
  via `CreateStateAsync(crewId, executionId, input)` quand un état persisté existe
  déjà pour l'identifiant de reprise (crash recovery).
- **Activation** (deux conditions — un store **et** l'option) :
  ```csharp
  // 1. Un state store de checkpointing (in-memory, SQLite ou PostgreSQL)
  services.AddOrkeonSqliteCheckpointing("Data Source=/output/orkeon-state.db"); // chemin virtuel, montage en écriture
  // ou : services.AddOrkeonCheckpointing();              // in-memory
  // ou : services.AddOrkeonPostgresCheckpointing(configuration); // Orkeon:Checkpointing:*

  // 2. L'opt-in de persistance
  services.AddCrewExecutionStatePersistence();             // code-first
  services.AddCrewExecutionStatePersistence(configuration); // lie Orkeon:ExecutionState:Persistence
  ```
  Le store SQLite résout sa `Data Source` via le VFS comme le provider mémoire SQLite :
  il lui faut un `IFileSystemService` enregistré (`AddOrkeonFileSystem(...)`) et un chemin
  sur un montage accessible en écriture, sinon il lève `FileAccessDeniedException`.
  Avec la surcharge `AddOrkeonInfrastructure(IConfiguration)`, la présence de la
  section `Orkeon:ExecutionState:Persistence` (`Enabled`, `DeleteFromStoreOnArchive`)
  suffit à lier les options.
- **Défaut rétro-compatible** : sans activation, les états restent **in-memory only**
  (aucune reprise après crash) — comportement historique inchangé. Si l'option est
  activée sans store enregistré, le manager logue un avertissement et continue en
  mémoire.
- **Cohabitation** : les sessions d'état d'exécution sont **espacées de noms**
  (préfixe `crew-exec:` sur l'id de session et l'id de crew) et ne polluent jamais les
  sessions de checkpointing de tâches (`CheckpointManager`/`ResumeEngine`) partageant
  le même store.
- **Limites connues** : les métadonnées d'exécution sont persistées en chaînes
  invariantes (les lectures primitives restent converties via `GetValue<T>`) ; la
  télémétrie `ToolsUsed` des sorties de tâches n'est pas persistée (v1).

---

## Barrière de permissions — `AddOrkeonPermissionGate(configuration)`

- **Activation** : appelée par `RunnerHost` et le bootstrap de la ConsoleApp, mais
  n'enregistre rien tant que `Orkeon:Security:PermissionGate:Enabled = true` n'est pas
  posé. Quand l'hôte enregistre un `IToolApprovalChannel` (`orkeon-repl` le fait), la
  barrière lui soumet chaque demande et rend une autorisation ou un refus. Sans canal,
  `Interactive` (défaut `false`) déclare un appelant qui tranche lui-même les demandes ;
  laissé à `false`, une demande vaut refus.
- **Effet** : `ModePermissionGate` est consulté par la boucle scriptée `ctx.llm.act`
  avant CHAQUE exécution d'outil. Modes : `bypassPermissions` → tout autoriser ;
  `plan` → refuser tout ce qui n'est pas une lecture ; `dontAsk` → autoriser les
  lectures, refuser le reste sans jamais demander ; `acceptEdits` → accepter
  automatiquement les lectures + `file_write`, demander pour le shell ; `default` →
  accepter les lectures, demander pour tout le reste. Avec un canal d'approbation, une demande
  attend l'opérateur, et l'`ExecutionTimeout` du script comme le budget de temps mural de l'agent
  (`.budget({ wallTime })`) s'arrêtent pendant ce temps ; un refus se lit
  `DENIED: … The operator denied the call.` En headless, chaque demande dégrade en un `DENIED:` motivé renvoyé
  au modèle comme résultat d'outil (pas d'exception). Les outils inconnus sont
  classés comme écritures (fail-closed).
- **Classification déclarative** : les outils peuvent auto-déclarer leur classe via
  `IBaseTool.Access` (`ToolAccess.Read` / `Edit` / `Execute`) — une déclaration
  l'emporte sur les tables de noms de la barrière ; `Unspecified` (le défaut) se
  replie sur les tables. Un outil dont la classe dépend de l'opération demandée surcharge
  `IBaseTool.AccessFor(arguments)` (défaut : `Access`), que la barrière lit à chaque appel :
  `github` est `Read` pour `list_issues`, `get_pr`, `search_repos` et `get_repo`, `Execute`
  pour `create_issue`, `create_pr` et toute action inconnue ou absente ; `http_api` est
  `Read` pour `GET`, `HEAD` et `OPTIONS` (une méthode absente est son défaut `GET`) sans
  corps ni en-tête de substitution de méthode (`X-HTTP-Method-Override`, `X-HTTP-Method`,
  `X-Method-Override`, quelle que soit la casse), `Execute` pour toute autre méthode ou pour
  une méthode de lecture qui porte l'un ou l'autre. Leur `Access` statique reste `Execute`, la classe la
  plus restrictive qu'ils puissent prendre. Les outils de lecture intégrés
  (file/directory/session/memory/analysis, `web_scrape`, `scrape_element`, `rag_search`,
  `web_search`), `file_write`, `rag_ingest` (Edit : il écrit dans l'index de
  connaissances, jamais dans l'espace de travail) et `rag_eval` (Edit : il ingère le corpus du
  jeu de données dans la collection — `reindex` la reconstruit — et écrit ses rapports par le
  VFS) et `shell_command` (Execute) se déclarent tous. Un outil MCP ne prend sa classe des
  `annotations` du serveur que si l'opérateur fait confiance à ce serveur
  (`MCP:Servers:<name>:TrustAnnotations = true` : `readOnlyHint: true` → `Read`, d'autres
  annotations → `Execute`) ; les outils d'un serveur non fiable — le défaut — et d'un serveur
  qui n'envoie aucune annotation restent `Unspecified`, quoi que disent leurs indications.
  `BuiltInToolAccessTests` liste les outils intégrés encore laissés à `Unspecified`.
- **Défaut rétro-compatible** : flag absent → aucune barrière enregistrée → `act` se
  comporte exactement comme avant. Les scripts optent pour un mode à l'appel via
  l'option d'act `permissionMode`. Un script lit si une barrière contrôle ses appels avec
  `ctx.llm.permissionGate.enabled` ; un `permissionMode` passé à `act` sur un hôte sans barrière
  journalise un avertissement (event id `2` de `JsLlmFacade`) au lieu d'être ignoré en silence.

## Shell : interpréteurs & git mutant — `Orkeon:Tools:Shell:AllowInterpreters`

- **Activation** : config seule — `AddOrkeonCodeTools()` lit le flag et, quand il vaut
  `true`, construit `ShellCommandTool` avec `allowInterpreters: true` (réactive
  `node`/`dotnet`/`npm`/`find` ET lève la restriction git lecture-seule, si bien que
  `git add`/`commit`/`branch` fonctionnent). L'outil journalise son avertissement de
  sécurité quand il est actif.
- **Sécurité** : équivalent RCE sur l'hôte — à réserver aux hôtes coding-agent de
  confiance qui tournent dans un espace de travail confiné (le REPL scripted-commands est
  le consommateur de référence — voir `examples/cli-ts-commands/` ; une commande de commit
  ne peut pas marcher sans).
- **Défaut rétro-compatible** : flag absent → allowlist stricte lecture-seule,
  comportement historique inchangé.
- **Allowlist personnalisée** (deux clés tableau de chaînes, toutes deux lues par
  `AddOrkeonCodeTools()`) :
  - `Orkeon:Tools:Shell:ExtraAllowedCommands` — **additive** sur l'allowlist par défaut
    (ou sur une liste de remplacement) ; la voie recommandée pour autoriser
    `make`/`cargo`/etc. sur un hôte de confiance. Compose avec `AllowInterpreters`.
  - `Orkeon:Tools:Shell:AllowedCommands` — **remplacement** intégral verbatim ; par
    contrat du ctor de `ShellCommandTool`, il annule `AllowInterpreters` et réactive la
    restriction git lecture-seule.
  - Une section absente ou vide se lie à `null` (défauts) — jamais à un tableau vide,
    qui bloquerait toute commande.
- **Réécriture de chemins VFS** (toujours active, sans flag ; sous bubblewrap, le processus voit les chemins virtuels et rien n'est réécrit) : les arguments qui nomment
  un chemin virtuel préfixé par un montage (y compris `--out=/workspace/dist`) sont
  résolus virtuel→physique avant le démarrage du processus — un chemin refusé fait
  échouer l'appel avec la raison expurgée — et stdout/stderr sont réécrits
  physique→virtuel : le modèle ne voit jamais que des chemins virtuels. Montages
  `AgentFacing`, correspondance ordinale ; sans montage, les deux passes sont sans effet.
- **Le confinement n'est pas sur option** (`Orkeon:Tools:Shell:Confinement`) : depuis
  `1.0.0-rc.6`, le processus tourne confiné par défaut (`Mode = Auto` : bubblewrap sous
  Linux, Seatbelt sous macOS, un AppContainer sous Windows, coupé dans un conteneur), et
  `AllowInterpreters` laisse passer plus de commandes, pas une vue plus large. `Mode = None`
  est la sortie — voir [Confinement du shell](../architecture/security.md#confinement-du-shell).

## Streaming console LLM natif — `AddLlmConsoleStreaming(configuration)`

- **Activation** : appelée par le bootstrap de la ConsoleApp, mais n'enregistre rien
  tant que `Orkeon:Cli:ConsoleStreaming:Enabled = true` n'est pas posé.
- **Effet** : enregistre `ConsoleLlmDeltaSink` comme `ILlmDeltaSink`. Quand un sink
  est présent et que le provider LLM streame (`IStreamingLlmProvider`), la boucle
  scriptée `ctx.llm.act` bascule sur le chemin SSE et chaque delta de contenu atteint
  l'`IConsoleAdapter` de l'hôte au fil de son arrivée — le REPL rend les tokens de l'act
  de premier plan à mesure sans que le script passe `onDelta`. Tout autre tour
  (`ILlmDeltaSink.BeginTurn`) — un second act qui streame en même temps, un act sous une
  instance de commande en arrière-plan, une tâche de crew — tient son propre tampon de
  ligne et écrit une ligne entière à la fois, préfixée du ticket de l'instance (`[t2] `),
  comme les lignes de complétion, une tâche de crew ajoutant son id (`[t2 task-1] `) — le
  préfixe reste en mode simple, alors que la console à volets abandonne le préfixe, montre la
  ligne sur l'écran de la tâche et garde l'étiquette de crew (`[task-1] `) ;
  une de leurs lignes qui arrive pendant que l'act de premier plan est au milieu d'une
  ligne attend la fin de celle-ci. Ainsi deux acts qui streament en même temps — deux
  sous-exécutions `runAgent` en arrière-plan, les tâches d'une vague parallèle de crew —
  ne s'écrivent jamais l'un dans l'autre et aucune ligne n'est coupée (#211). Les tours
  qui ne streament aucun contenu visible (appels d'outils purs) n'émettent rien. Le sink
  compose avec un `onDelta` côté script (les deux reçoivent chaque delta).
- **Dire au script que la réponse a déjà été streamée** (#228) : la `CommandResponse` que
  retourne un `request()` ou que reçoit un `completed(result)`, et la `CrewRunOutput` de
  `runCrew`, portent `streamed: boolean`, vrai quand un tour quelconque de la requête a été
  écrit sur la console au premier plan (en ligne, ou en lignes entières sans préfixe) ; un script n'imprime le payload
  que quand `streamed` est faux, et la réponse s'affiche une fois. Une instance en
  arrière-plan (`post`, `runCrewAsync`) et une tâche de crew écrivent des lignes entières
  préfixées et rapportent faux. Le dispatch ouvre un reçu par requête d'agent et par
  exécution de crew dans le flux asynchrone de ce travail ; les tours du sink y
  enregistrent, il ne repasse jamais à faux au sein d'une requête, et des commandes qui tournent en même temps lisent chacune le leur.
- **Défaut rétro-compatible** : flag absent → aucun sink enregistré → `act` conserve
  son rendu bufferisé (identique à l'octet près), les scripts peuvent toujours
  streamer via `onDelta`.

---

## Garanties de non-régression

Les tests d'enregistrement
(`tests/core/Orkeon.Infrastructure.Tests/DependencyInjection/OptInSubsystemsRegistrationTests.cs`
et `tests/core/Orkeon.Application.Tests/DependencyInjection/KickoffHookExtensionsTests.cs`)
vérifient :

1. qu'**aucun port dormant du jeu opt-in d'origine** n'est enregistré par
   `AddOrkeonApplication()` / `AddOrkeonInfrastructure()` (les deux
   surcharges) — les lignes ajoutées ensuite (plugins, embeddings locaux, providers
   de mémoire, mémoire cognitive, les enregistrements d'outils et de journalisation…)
   sont gardées par leurs propres suites, pas par ce test ;
2. que chaque `AddOrkeonXxx()` produit un graphe **résolvable** seul (avec logging et,
   le cas échéant, une `IConfiguration`) ;
3. que les opt-ins **composent** avec le socle (sémantique `TryAdd`, pas de doublons).
