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
| Contexte de codebase (RaggableTree) | `AddRaggableTree(options)` | Analysis | `ICodebaseContextProvider` | Bêta |
| Sous-système RAG (RAG-02…06) | `AddOrkeonRag(config)` (namespace `Orkeon.Rag.DependencyInjection`) + `AddOrkeonRagTools()` (`Orkeon.Tools.Rag`) ; profils `fast`/`balanced`/`quality`/`adaptive`/`corrective` via `Orkeon:Rag:Profile` (défaut `fast`) ; `balanced`/`quality`/`adaptive` exigent le cross-encoder ONNX — `AddOrkeonOnnxReranker()` (`Orkeon.Rag.Onnx` + `Orkeon.Rag.Onnx.Model`, poids embarqués, hors-ligne) ; `corrective` non (le graphe boucle au lieu de reranker) ; le repli web correctif est purement config — `AddOrkeonRag` câble déjà le transport, les deux interrupteurs `Enabled` gouvernent (`Orkeon:Rag:Corrective:WebFallback` politique, `Orkeon:Rag:WebFallback` transport) — voir la section détaillée plus bas | Orkeon.Rag.Abstractions / Orkeon.Rag / Orkeon.Tools.Rag / Orkeon.Rag.Onnx / Orkeon.Rag.Onnx.Model | `IRagPipeline` (à étages / graphe correctif), `IRagProfileResolver`, `IIngestionPipeline`, `IDocumentStore`, `IRagEvalHarness`, `rag_search`/`rag_ingest`/`rag_eval` (`IBaseTool`) | Bêta |
| Persistance d'état d'exécution (R3.8) | `AddCrewExecutionStatePersistence(...)` | Infrastructure | `ICrewExecutionStateManager` (durable via `IStateStore`) | Bêta |
| Shell : interpréteurs & git mutant | config seule : `Orkeon:Tools:Shell:AllowInterpreters = true` | Tools.Code | (ré-enregistre `ShellCommandTool` avec `allowInterpreters: true` — équivalent RCE, avertissement de sécurité émis) | Bêta |
| Shell : allowlist personnalisée | config seule : `Orkeon:Tools:Shell:ExtraAllowedCommands` (additive) / `Orkeon:Tools:Shell:AllowedCommands` (remplacement intégral — annule `AllowInterpreters`) | Tools.Code | (façonne l'allowlist d'exécutables de `ShellCommandTool` ; section absente/vide = défauts) | Bêta |

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
  `IA2AServer` n'est enregistré que si `EnableServer` est vrai.
- **Dépendances** : l'extension enregistre elle-même (TryAdd) `IHttpClientFactory`,
  `IDomainEventDispatcher`, `IUnitOfWork`, ainsi que l'annuaire d'agents A2A
  (`IAgentRegistrationStore` singleton + `IAgentRepository` scoped, voir ci-dessous)
  lorsque l'hôte ne les a pas déjà câblés via `AddOrkeonInfrastructure()`.
- **Cycle de vie du dépôt d'agents (R4.6 / ANT-001, décision « scoped par requête »)** :
  - `IAgentRepository` est **scoped** ; le serveur et le routeur (singletons) ne le
    capturent jamais — ils ouvrent un **scope DI par requête A2A** via
    `IServiceScopeFactory` et résolvent le dépôt dedans. La résolution passe avec
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
- **Limites connues** : le store in-memory est local au processus — pour un annuaire
  d'agents multi-instances, fournir un `IAgentRepository` custom adossé à un stockage
  partagé externe (il sera respecté tel quel par l'extension).

## Backend de monitoring — `AddOrkeonMonitoring(...)`

- **Rôle** : agrégation in-process des métriques Orkeon (`MeterListener` sur
  `OrkeonMetrics`) et exploration des traces récentes (buffer circulaire) pour
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

## Conformité NIST — `AddOrkeonNistCompliance()`

- **Rôle** : génération de rapports de conformité NIST SP 800-53 à partir de la piste
  d'audit (`IAuditLogger`).
- **Activation** : `services.AddOrkeonNistCompliance();`
- **Dépendances** : l'extension TryAdd une chaîne d'audit de repli
  (`StructuredLogAuditSink` + `AuditLogger`) ; quand l'hôte a appelé
  `AddOrkeonInfrastructure()`, la chaîne d'audit du socle (sinks structuré +
  in-memory, options `Security:Audit`) est utilisée.
- **Limites connues** : la profondeur du rapport dépend des événements réellement
  audités par l'hôte ; aucun contrôle automatisé n'est exécuté.

## DLP — `AddOrkeonDlp()`

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

- **Rôle** : limitation de débit par outil (`IToolRateLimiter`, section
  `ToolRateLimiting`) et suivi de budget de tokens par agent/crew
  (`ITokenBudgetTracker`, section `TokenBudget`).
- **Activation** : `services.AddOrkeonToolRateLimiting();`
- **Dépendances** : logging + `IConfiguration`. Si `AddOrkeonCostTracking()` (inclus
  dans le socle) a enregistré `IModelPricingRegistry`, le tracker l'utilise pour la
  conversion tokens → coût.
- **Limites connues** : non câblé dans l'exécution des outils — l'hôte interroge le
  limiteur/le tracker autour de ses appels d'outils. Le rate-limiting **LLM**
  (`ILlmRateLimiter`), lui, reste enregistré par défaut car consommé par
  l'orchestrateur d'exécution.

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
- **Dépendances** : aucune (la suite d'évaluation est fournie via `BenchmarkConfig`
  à l'appel).

## Contenu multi-modal (vision) — `AddOrkeonMultiModal(...)`

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
- **Limites connues** : seuls les providers **Anthropic** et **OpenAI** composent des
  payloads vision ; les autres providers dégradent les messages multi-modaux vers leur
  repli texte (`LlmMessage.Content`). Formats image supportés : png, jpeg, gif, webp.
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
- **Activation** : déjà opt-in — il est enregistré par `AddRaggableTree(options)`
  (projet `Orkeon.Analysis`), qui n'est jamais appelé par le socle. Voir
  [raggable-tree.md](../architecture/raggable-tree.md).
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
  [ADR-006](../../adr/ADR-006-rag-subsystem.md).
- **Activation** :
  ```csharp
  services.AddOrkeonRag(configuration);   // Orkeon.Rag.DependencyInjection
  services.AddOrkeonRagTools();           // Orkeon.Tools.Rag : rag_search / rag_ingest / rag_eval
  services.AddOrkeonOnnxReranker();       // opt-in — requis par balanced/quality/adaptive
  ```
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

- **Rôle** : persistance durable des états d'exécution de crew
  (`ScopedCrewExecutionStateManager`) dans un state store de checkpointing
  (`IStateStore`) : chaque transition (création, mise à jour, complétion, archivage)
  est persistée, et au redémarrage les états sont rechargés — y compris la **reprise**
  via `CreateStateAsync(crewId, executionId, input)` quand un état persisté existe
  déjà pour l'identifiant de reprise (crash recovery).
- **Activation** (deux conditions — un store **et** l'option) :
  ```csharp
  // 1. Un state store de checkpointing (in-memory, SQLite ou PostgreSQL)
  services.AddOrkeonSqliteCheckpointing("Data Source=orkeon-state.db");
  // ou : services.AddOrkeonCheckpointing();              // in-memory
  // ou : services.AddOrkeonPostgresCheckpointing(configuration);

  // 2. L'opt-in de persistance
  services.AddCrewExecutionStatePersistence();             // code-first
  services.AddCrewExecutionStatePersistence(configuration); // lie Orkeon:ExecutionState:Persistence
  ```
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

## Shell : interpréteurs, allowlist & réécriture VFS — `Orkeon:Tools:Shell:*`

- **`AllowInterpreters = true`** (config seule, lue par `AddOrkeonCodeTools()`) :
  réactive `node`/`dotnet`/`npm`/`find` ET lève la restriction git lecture-seule
  (`git add`/`commit`/`branch` fonctionnent). Équivalent RCE sur l'hôte — à réserver
  aux hôtes coding-agent de confiance (le REPL exp07 est le consommateur de
  référence ; `/commit` ne marche pas sans). Avertissement de sécurité loggé.
- **Allowlist personnalisée** (deux clés tableau de chaînes) :
  - `ExtraAllowedCommands` — **additive** sur l'allowlist par défaut (ou de
    remplacement) ; la voie recommandée pour autoriser `make`/`cargo`/etc. Compose
    avec `AllowInterpreters`.
  - `AllowedCommands` — **remplacement** intégral verbatim ; par contrat du ctor de
    `ShellCommandTool`, il annule `AllowInterpreters` et réactive la restriction git
    lecture-seule.
  - Une section absente ou vide se lie à `null` (défauts) — jamais à un tableau
    vide, qui bloquerait toute commande.
- **Réécriture de chemins VFS** (toujours active, sans flag) : les arguments qui
  nomment un chemin virtuel préfixé par un mount (y compris la forme
  `--out=/workspace/dist`) sont résolus virtuel→physique avant le démarrage du
  processus — un chemin refusé fait échouer l'appel avec la raison expurgée — et
  stdout/stderr sont réécrits physique→virtuel : le modèle ne voit jamais que des
  chemins virtuels. Mounts `AgentFacing`, matching ordinal ; sans mount, les deux
  passes sont des no-ops.
- **Défaut rétro-compatible** : aucune clé → allowlist stricte lecture-seule,
  comportement historique inchangé.

---

## Garanties de non-régression

Les tests d'enregistrement
(`tests/core/Orkeon.Infrastructure.Tests/DependencyInjection/OptInSubsystemsRegistrationTests.cs`
et `tests/core/Orkeon.Application.Tests/DependencyInjection/KickoffHookExtensionsTests.cs`)
vérifient :

1. qu'**aucun** port dormant n'est enregistré par `AddOrkeonApplication()` /
   `AddOrkeonInfrastructure()` (les deux surcharges) ;
2. que chaque `AddOrkeonXxx()` produit un graphe **résolvable** seul (avec logging et,
   le cas échéant, une `IConfiguration`) ;
3. que les opt-ins **composent** avec le socle (sémantique `TryAdd`, pas de doublons).
