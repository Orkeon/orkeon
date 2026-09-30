> 🇬🇧 [English version](../../architecture/security.md)

# Sécurité, résilience et plugins

## Sécurité

Le framework intègre plusieurs couches de sécurité :

- **Outils** : `ToolAccessPolicy` (whitelist/blacklist/unrestricted) par agent, vérifiée quand un outil est attaché à l'agent ; `IPathValidator` pour la protection path traversal, `IUrlValidator` pour la protection SSRF, `HttpHeaderSanitizer`. Les noms d'outils spécifiés dans les configurations YAML sont validés à la création via `IToolRegistry` ; sous `Orkeon:CrewFactory:StrictTools` (le défaut des runners) aucun outil non enregistré ne peut être assigné à un agent — le défaut bibliothèque est tolérant (saut + Warning).
- **Système de fichiers** : toute E/S du framework passe par `IFileSystemService` — montages, `FileAccessRights` par montage (Read/Write/Create/Delete), chemins physiques masqués dans les messages d'erreur ; l'analyseur Roslyn `Orkeon.Compliance.Vfs` interdit le `System.IO` direct dans le code du framework ([conformité VFS](./vfs-compliance.md)). En dessous, `PathValidator` (section `PathSecurity`) refuse le path traversal, les chemins système sensibles et les extensions dangereuses : `AdditionalAllowedDirectories`, `AdditionalBlockedExtensions`, `ResolveSymlinks` (défaut `true`), `MaxFileSizeBytes` (défaut 50 Mo), `DefaultWorkspaceRoot`.
- **Réseau (SSRF)** : `UrlValidator` (section `Security:Url`) n'autorise que `http`/`https` (`AllowedSchemes`), bloque les adresses privées et de bouclage (`BlockPrivateIPs`, défaut `true`) après résolution DNS (`ResolveDNS`, défaut `true`), refuse les ports des services internes courants (22, 23, 25, 110, 143, 445, 3306, 5432, 6379, 27017 — `BlockedPorts`) et respecte `AllowedDomains`/`BlockedDomains`. Il est consulté par les bases d'outils HTTP, `http_api`, les outils de scraping, `WebPageLoader` et le repli web RAG.
- **Shell** : `ShellCommandTool` applique par défaut une liste d'autorisation en lecture seule (`ls`, `cat`, `pwd`, `which`, `grep`, `wc`, `echo`, `dir`, `type`, `where`, et `git` restreint à `status`/`log`/`diff`/`show`) ; les interpréteurs (`dotnet`, `npm`, `node`, `find`) et le git mutant ne reviennent qu'avec `Orkeon:Tools:Shell:AllowInterpreters` (équivalent RCE, avertissement journalisé) — voir [Sous-systèmes opt-in](../reference/opt-in-subsystems.md).
- **Code** : `RoslynCodeSecurityAnalyzer` (`ICodeSecurityAnalyzer`) et les implémentations d'`ICodeSandbox` — `DockerSandbox`, `HostProcessRunner` derrière `LazyProbingCodeSandbox` (`Orkeon.Infrastructure.Sandbox`) — pour l'exécution de code C# par `SecureCodeInterpreterTool`. `DockerSandbox` lance `docker run --rm --cap-drop=ALL --security-opt=no-new-privileges --pids-limit=…` avec un plafond mémoire, `--network=none` sauf si la requête autorise le réseau et `--read-only` (+ un tmpfs `noexec`) sauf si elle autorise l'écriture ; le conteneur tourne toujours en root dans son espace de noms, sans capacités. `HostProcessRunner` n'offre **aucune** isolation au niveau de l'OS, d'où une barrière fermée par défaut : sans Docker, l'exécution est refusée sauf si l'opérateur pose `Orkeon:CodeSandbox:AllowHostExecution = true` (équivalent RCE — code de confiance uniquement). Autres clés : `TimeoutSeconds` (30), `MaxMemoryBytes` (256 Mo), `MaxOutputBytes` (50 000) ; `Orkeon:CodeSandbox:Docker` porte `ImageName` (`mcr.microsoft.com/dotnet/sdk:10.0-alpine`) et `PullImageOnStartup`.
- **Données** : `IDatabaseSecurityPolicy` (`Orkeon.Tools.Abstractions.Data`) pour la validation des requêtes SQL dans les outils de bases relationnelles, `PiiDetector` et les cinq implémentations d'`IDlpInterceptor` (sortie d'outil, délégation, log, mémoire, sortie externe — `Orkeon.Infrastructure.Security.Dlp`). Le sous-système DLP est **opt-in** (`AddOrkeonDlp()`, voir [Sous-systèmes opt-in](../reference/opt-in-subsystems.md))
- **Validation des sorties** : les six `IOutputValidator` (`LengthValidator`, `FormatValidator`, `SchemaValidator`, `CompletenessValidator`, `ContentSafetyValidator`, `PiiDetectionValidator`) forment l'`OutputValidationPipeline` que l'orchestrateur d'exécution applique à la sortie d'une tâche quand celle-ci déclare des attentes de sortie, avec des relances de correction.
- **Chiffrement** : `IEncryptionProvider` avec une implémentation AES-256-GCM (`AesEncryptionProvider`, section `Orkeon:Encryption` : `Enabled` — défaut `false` —, `SecretName` — défaut `orkeon-encryption-key`, résolu via `ISecretProvider` —, `KeySizeInBits` — 256), applicable à n'importe quel provider mémoire via l'`EncryptedMemoryProviderDecorator` — rien n'enveloppe un provider automatiquement, c'est l'hôte qui compose le décorateur. La rotation de clés est **opt-in** (`AddOrkeonKeyRotation()`)
- **Secrets** : `ISecretProvider` est une chaîne — variables d'environnement `ORKEON_<NOM>`, puis la section de configuration `Secrets`, puis, quand `Security:Vault` les configure, Azure Key Vault (`AzureKeyVaultUri`), AWS Secrets Manager (`UseAwsSecretsManager`, avec un `IAmazonSecretsManager` enregistré par l'hôte) et DPAPI sous Windows (`DpapiSecretsDirectory`) ; `CacheTtl` met en cache les lectures du coffre. `LogSanitizer` masque les secrets dans les erreurs et les échanges LLM journalisés.
- **Limitation de débit LLM** : `LlmRateLimiter` (section `RateLimiting` : `GlobalRequestsPerMinute` 60, `ProviderRequestsPerMinute` 30, `AgentRequestsPerMinute` 20, `MaxConcurrentRequests`, `QueueLimit` 5) régule chaque appel de modèle de l'orchestrateur d'exécution. Les limites par outil et les budgets de jetons sont opt-in (`AddOrkeonToolRateLimiting()`).
- **Permission gate** : `ModePermissionGate` (`IPermissionGate`) demande avant chaque appel d'outil du runtime scripté (`.ork.ts`, `ctx.llm.act`) selon un mode (`default`, `acceptEdits`, `plan`, `bypassPermissions`) et le `ToolAccess` déclaré par l'outil (Read/Edit/Execute) ; inactive sauf si `Orkeon:Security:PermissionGate:Enabled = true` — voir [Sous-systèmes opt-in](../reference/opt-in-subsystems.md).
- **Audit** : `AuditLogger` (`Orkeon.Infrastructure.Security`, section `Security:Audit` : `Enabled`, `MinSeverity`, `EnabledCategories`, `RetentionDays` — 90) avec ses sinks (`Orkeon.Infrastructure.Security.Sinks` : `StructuredLogAuditSink` et `InMemoryAuditSink` enregistrés par défaut, `JsonFileAuditSink` pour un hôte qui veut des fichiers sous `AuditDirectory`) ; `ProvenanceTracker` vit côté RAG (`Orkeon.Rag.Validation`)
- **Conformité** : `NistComplianceReportGenerator` (`INistComplianceReporter`, `Orkeon.Infrastructure.Compliance`). Le reporting NIST est **opt-in** (`AddOrkeonNistCompliance()`)

### Ce qui s'applique seul, et ce que l'hôte appelle

`AddOrkeonInfrastructure()` enregistre plus de composants de sécurité que le chemin
d'exécution n'en consomme. Enregistré ne veut pas dire appliqué :

| Composant | Sur le chemin d'exécution ? |
|---|---|
| Résolution des outils (`StrictTools`), `ToolAccessPolicy`, droits VFS + `PathValidator`, `UrlValidator`, liste d'autorisation shell, barrière du bac à sable, pipeline de validation des sorties, `LlmRateLimiter`, chaîne de secrets | **Oui** — appliqués là où ils s'appliquent, sans code hôte |
| Permission gate | Oui pour le runtime scripté une fois activée ; les crews YAML ne la consultent pas |
| `GuardianPipeline` (`Orkeon:Guardian` — `InputGuard`, `OutputGuard`, `ToolGuard`, `DelegationGuard`), `IPromptSanitizer` + `PromptShieldBuilder` (`Security:Prompt`), `ToolResultSanitizer` (`Security:ToolResults`) | **Non** — briques enregistrées (`AddOrkeonGuardian()` lie `Orkeon:Guardian` et branche les quatre gardes dans le pipeline ; les sanitizers sont enregistrés avec le socle de sécurité ; le tout est appelé par `AddOrkeonInfrastructure()`) ; aucun orchestrateur, boucle d'agent ni outil ne les appelle. Un hôte qui en veut résout `GuardianPipeline` / `IPromptSanitizer` / `ToolResultSanitizer` et les invoque lui-même. La piste d'audit est alimentée par le pipeline Guardian : elle reste vide tant que l'hôte ne l'exécute pas |
| `AuthenticationGuard`, `AzureAdAuthProvider`, `OidcAuthProvider` (`Orkeon:Auth:AzureAD`, `Orkeon:Auth:OIDC`) | **Non** — `AddOrkeonAuth()` (appelée par `AddOrkeonInfrastructure()`) lie les options et enregistre le garde, mais aucun `IAuthenticationProvider` n'est enregistré et le garde n'est pas dans le pipeline Guardian : un hôte qui authentifie enregistre un provider et ajoute le garde lui-même |
| Intercepteurs DLP, reporting NIST, rotation de clés, limitation de débit des outils | Opt-in, et invoqués par l'hôte une fois enregistrés — voir [Sous-systèmes opt-in](../reference/opt-in-subsystems.md) |

### Résolution des outils en YAML

À la création d'une Crew depuis YAML, chaque nom d'outil dans `agents[].tools[]` est résolu via `IToolRegistry.GetToolByNameAsync(toolName)`. Le sort d'un outil inexistant dépend de `CrewFactoryOptions.StrictTools` :

- **Mode strict** (`Orkeon:CrewFactory:StrictTools=true` — le défaut d'`orkeon run` et des runners) : `CrewFactory` lève, nomme les outils manquants et liste les noms disponibles du registre. Aucun agent ne peut opérer avec des capacités inexistantes.
- **Mode tolérant** (le défaut bibliothèque, `false`) : l'outil est sauté avec un log Warning et la crew se charge sans lui — un hôte qui embarque la bibliothèque et veut la garantie active le mode strict.

### TLS mutuel A2A

Le sous-système A2A opt-in (`AddOrkeonA2A()`) applique la posture de sécurité déclarée dans `A2ASecurityOptions` :

- **Serveur** (`RequireMutualTls = true`) : le certificat client entrant est **authentifié, pas seulement exigé** — il doit chaîner vers l'une des `TrustedCertificateAuthorities` configurées (chaîne X509 construite en `CustomRootTrust`, qui couvre aussi la fenêtre de validité) ou correspondre à une empreinte épinglée de `TrustedClientCertificateThumbprints`. Les certificats auto-signés non épinglés sont rejetés (403). Démarrer le serveur avec `RequireMutualTls` sans aucune ancre de confiance **lève une exception** (fail-closed) : présence + dates valides ne font pas une authentification. La révocation n'est pas vérifiée — les ancres de confiance sont supposées être des CA privées sans endpoints CRL/OCSP.
- **Client** : la validation TLS complète s'applique toujours — il n'existe délibérément **aucun opt-out « accepter n'importe quel certificat »**. Configurer `TrustedCertificateAuthorities` épingle la confiance sur des CA privées (un serveur auto-signé ou de dev local doit épingler sa CA ainsi) ; cet épinglage ne couvre que la **chaîne inconnue** (`RemoteCertificateChainErrors`) — un mismatch de nom d'hôte ou un certificat absent n'est jamais contourné.
- La terminaison TLS mutuelle réelle requiert un binding HTTPS de `HttpListener` au niveau de l'OS — voir [Limitations connues](../reference/limitations.md).

### Repli web RAG & injection de prompt

Le pipeline RAG correctif peut, en option, se replier sur une recherche web (`WebSearchDocumentRetriever`, `Orkeon.Rag.WebFallback`) quand la récupération locale échoue. Le contenu web est le vecteur classique de l'**injection de prompt indirecte** : une page fabriquée pour que, une fois récupérée et insérée dans un prompt LLM, son texte soit interprété comme des instructions — détournant l'agent, exfiltrant la conversation, ou déclenchant des appels d'outils.

La défense est en couches ; aucune couche n'est digne de confiance à elle seule :

- **Opt-in strict** : le repli est éteint par défaut derrière **deux** interrupteurs indépendants — le transport (`Orkeon:Rag:WebFallback:Enabled` + un `Endpoint`, enregistré par `AddOrkeonRag(configuration)`) et la politique du graphe correctif (`Orkeon:Rag:Corrective:WebFallback:Enabled`) ; les deux doivent être actifs pour qu'un document web atteigne jamais le graphe. L'activer sans configurer d'endpoint le laisse inerte et journalise un avertissement — aucune sortie réseau silencieuse. La clé d'API est lue depuis une variable d'environnement (`ApiKeyEnvVar`), jamais depuis les fichiers de configuration.
- **Validation déterministe** : chaque page téléchargée passe par `PromptInjectionDocumentValidator` (`Orkeon.Rag.Validation`) — heuristiques pures, sans LLM, testables hors ligne. Signaux détectés : directives adressées au modèle (EN + FR : « ignore previous instructions », « you are now… », « system: », « nouvelles instructions : »…), tokens de contrôle de gabarits de chat (`<|im_start|>`, `<<SYS>>`, `[INST]`…) avec accumulation par occurrence, balisage HTML caché (`display:none`, `visibility:hidden`, opacité/taille de police quasi nulles, `aria-hidden`), commentaires HTML massifs, vecteurs d'exfiltration (images markdown auto-chargées avec payloads encodés en query, payloads de liens percent-encodés, data URIs base64) et longs blobs base64. Le verdict par document est `Clean | Suspicious | Rejected` avec raisons et extraits incriminés.
- **Traçabilité, pas de nettoyage silencieux** : les documents `Rejected` ne quittent jamais le retriever et sont journalisés avec leurs raisons et leur URL. Les documents `Suspicious` sont — selon `SuspiciousAction` — soit écartés (journalisés), soit conservés **marqués** dans les métadonnées (`injection_verdict`, `injection_reasons`, `injection_risk_score`). Le contenu n'est jamais réécrit : le validateur détecte, il n'assainit pas. Sur le chemin d'ingestion, le même validateur siège dans le `DataValidationPipeline` (chaîne `IDataValidator`) avec quarantaine et suivi de provenance.
- **Fail-quiet sur le réseau, fail-loud sur la sécurité** : les erreurs HTTP de recherche et les timeouts dégradent vers une liste de résultats vide avec un avertissement (le graphe correctif poursuit simplement sans contexte web) ; les rejets sont toujours des avertissements avec les raisons complètes.

**Limites honnêtes** : ces heuristiques sont fondées sur des motifs et peuvent être contournées — directives paraphrasées, langues autres que l'anglais/le français, encodages inédits ou payloads fractionnés passeront. Inversement, des documents techniques légitimes *sur* les prompts ou le CSS peuvent scorer `Suspicious` (ils sont marqués, jamais écartés en silence). Un validateur de contenu est un filtre, **pas une frontière de privilèges** : le vrai confinement est architectural — le texte récupéré doit rester de la donnée (jamais exécuté comme instruction), les agents consommant du contenu web devraient tourner avec des outils au moindre privilège, et les actions sensibles ne doivent pas être déclenchables par du seul contenu récupéré.

### Outils e-mail

La famille e-mail (`Orkeon.Tools.Email` — [guide](../guides/email.md)) lit du courrier écrit par
des inconnus et agit sur une vraie boîte aux lettres : sa défense ne repose donc pas sur le
jugement du modèle.

- **Le modèle ne choisit qu'un nom de compte.** Serveurs, identifiants et droits relèvent de la
  configuration de l'opérateur (`Orkeon:Tools:Email`). Un secret est le *nom* d'une variable
  d'environnement, jamais une valeur de cette configuration, et jamais un argument d'outil — les
  arguments d'outil sont journalisés.
- **Les droits sont déclarés par compte, et obligatoires.** `Read`, `Organize`, `Draft`, `Send`,
  `Delete`, `Purge` : un compte qui n'en déclare aucun est refusé, et chaque appel est vérifié
  au regard du droit qu'exige son opération. Chaque outil déclare aussi son `ToolAccess` (Read,
  Edit, Execute — `email_send` et `email_delete` sont Execute), ce que lit la permission gate.
- **L'envoi est fermé par défaut.** `email_send` n'atteint que les adresses qu'autorise
  `Send:AllowedRecipients` (une adresse, `*@domaine` ou `*` ; une liste vide n'autorise
  personne), contrôlées sur To, Cc et Bcc par adresse, jamais par nom affiché. L'enveloppe SMTP
  est passée explicitement — la liste contrôlée, jamais déduite des en-têtes, si bien qu'aucun
  en-tête `Resent-*` ne peut l'élargir —, `From` est imposé au compte, et `Send:MaxRecipients` /
  `Send:MaxPerHour` plafonnent le volume. Via Graph, qui lit l'enveloppe dans les en-têtes, un
  message qui nomme quelqu'un hors de la liste contrôlée est refusé. `email_draft` est la voie de
  la relecture humaine : l'agent rédige, une personne envoie.
- **Le contenu reçu n'est pas fiable.** Chaque page de recherche et chaque résultat de lecture
  s'ouvre sur un avis disant que c'est une donnée, jamais une instruction. `email_read` et
  `email_parser` portent le verdict du même `PromptInjectionDocumentValidator` que le repli web
  ci-dessus, calculé sur le texte **rendu** — ce que voit l'agent, pas le HTML ni le MIME bruts.
  Le texte que le HTML cache à un lecteur humain par les déclarations en ligne que connaît le
  rendu (`display:none`, `font-size:0`…) ou par l'attribut `hidden` est laissé de côté et
  signalé par `hidden_content` ; une classe CSS, une police d'un pixel ou du blanc sur blanc ne
  sont pas détectés, et une partie texte brut — ce que lit l'agent quand il y en a une — n'est
  pas comparée au HTML. La politique par défaut signale ; `Screening:WithholdRejected` retient le
  corps d'un message rejeté.
- **Les jetons restent hors de portée des agents.** Les jetons OAuth sont écrits par
  `orkeon email login` dans la racine interne `/credentials`, montée seulement quand un compte
  OAuth est déclaré et atteinte par `PrivilegedFileSystemAccess`, qu'aucun outil destiné aux
  agents ne résout ([conformité VFS](./vfs-compliance.md)). Les outils ne lancent jamais de
  connexion interactive, et un lien de pagination Graph n'est suivi que s'il pointe encore vers
  `graph.microsoft.com` en HTTPS : le jeton porteur ne part jamais vers un autre hôte. Le
  transport est en TLS (`SslOnConnect` ou `StartTls`) — `None` n'est accepté que vers un serveur
  de test en bouclage, et aucune option n'accepte un certificat invalide.
- **Les crews forgés n'ont pas de boîte aux lettres.** `orkeon forge` retire les douze outils de
  boîte aux lettres du catalogue des crews qu'il essaie sur son banc (`email_parser`, qui lit un
  fichier, reste).

**Limites honnêtes.** Le filtre est le détecteur par motifs décrit plus haut : il signale, et une
injection paraphrasée passe. La frontière, ce sont les droits du compte, la liste d'autorisation
fermée par défaut et les brouillons. Chaque compte est visible de chaque crew et de chaque
script `.ork.ts` qui résout le même fichier de réglages — les scripts appellent `tools.email*`
directement, et les crews hébergés par `orkeon-host` partagent les réglages de l'hôte — : déclarez
donc les comptes dans le propre `appsettings.json` du crew (un run résout un seul fichier de
réglages ; gardez les comptes hors d'un `appsettings.json` du répertoire d'où partent les runs et
hors des variables d'environnement `Orkeon__Tools__Email__…`, que l'hôte .NET lit pour chaque
run), accordez le moins de droits possible, préférez `email_draft`, et ne donnez pas à un
crew non fiable à la fois le `Read` e-mail et un canal sortant (`http_api`, les outils web) : un
message pourrait demander à l'agent d'exfiltrer le contenu de la boîte. Les fichiers de jetons
sont du JSON en clair : à l'abri des outils du VFS, **pas** d'un outil shell ou de code qui
tourne sous le même utilisateur du système.

## Résilience

`ResiliencePolicies` (`Orkeon.Infrastructure.Resilience`) expose les politiques Polly. Deux sont sur le chemin d'exécution :

- `GetLlmApiPolicy` — construite par `HttpLlmProviderBase` pour chaque provider LLM : relance les échecs HTTP transitoires (5xx, 408, erreurs réseau) et les 429, en respectant un en-tête `Retry-After` plafonné à 30 s ; le budget de relances est `Llm:MaxRetries` (défaut 10), sur une échelle linéaire pour les deux premières relances puis ×3, chaque attente plafonnée à 30 s. Un appel qui atteint le timeout HTTP est relancé **une fois**, puis échoue en nommant le réglage (LLM-11).
- `GetRedisRetryPolicy` — backoff exponentiel (2^n s, 3 tentatives) sur les erreurs Redis (connexion et timeout compris), utilisée par `RedisMemoryProvider`.

`GetRetryPolicy`, `GetCircuitBreakerPolicy`, `GetTimeoutPolicy`, `GetCombinedPolicy` et `GetDatabaseRetryPolicy` sont des utilitaires pour le code hôte ; aucun composant du framework ne s'en sert. La section de configuration `Resilience` (`ResilienceOptions` : `LlmMaxRetries`, `LlmTimeoutSeconds`, `CircuitBreakerThreshold`, `CircuitBreakerDurationSeconds`, `DatabaseMaxRetries`, `RedisMaxRetries`) est liée par `AddOrkeonInfrastructure()` mais **lue par rien** aujourd'hui — réglez plutôt `Llm:MaxRetries` et `Llm:TimeoutSeconds`.

## Checkpointing

`CheckpointManager` et `ResumeEngine` (`Orkeon.Application.Services.Checkpointing`) permettent la sauvegarde et la reprise d'état des exécutions de crew. Utile pour les longs workflows qui doivent survivre aux redémarrages.

Depuis R3.8, les **états d'exécution** eux-mêmes (`ICrewExecutionStateManager` : statut, progression, sortie) peuvent aussi être persistés dans les mêmes state stores (`IStateStore` — in-memory, SQLite via `AddOrkeonSqliteCheckpointing`, PostgreSQL) pour la reprise après crash. Opt-in via `AddCrewExecutionStatePersistence(...)` — ou la section `Orkeon:ExecutionState:Persistence`, qui n'a d'effet qu'à travers la surcharge `AddOrkeonInfrastructure(IConfiguration)` (les runners livrés utilisent la surcharge sans paramètre : pour eux, l'appel explicite est la voie) ; défaut : in-memory only. Voir [Sous-systèmes opt-in](../reference/opt-in-subsystems.md).

## Système de plugins

`IOrkeonPlugin`, `PluginAssemblyDiscovery`, `PluginLoader`/`PluginLoadContext` et `IPluginRegistry` (`Orkeon.Plugins`) fournissent un système de plugins pour étendre le framework avec des outils et providers personnalisés : découverte sur répertoire via le VFS, chargement isolé par `AssemblyLoadContext` collectible, activation DI opt-in `AddOrkeonPlugins(...)`.

> ⚠️ **Frontière de confiance** : charger un plugin exécute du code arbitraire avec les privilèges du processus hôte — pas de sandbox en v1, et l'`AssemblyLoadContext` n'est pas une frontière de sécurité. Ne charger que des plugins de confiance. Détails et règles d'exploitation : [Système de plugins](./plugins.md).

---

> **Voir aussi** : [Fournisseurs LLM](./llm-providers.md) · [Événements et CQRS](./domain-events.md) · [Retour à l'index](../INDEX.md)
