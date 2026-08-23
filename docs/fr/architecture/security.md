> 🇬🇧 [English version](../../architecture/security.md)

# Sécurité, résilience et plugins

## Sécurité

Le framework intègre plusieurs couches de sécurité :

- **Outils** : `ToolAccessPolicy` (whitelist/blacklist/unrestricted) par agent, `IPathValidator` pour la protection path traversal, `IUrlValidator` pour la protection SSRF, `HttpHeaderSanitizer`. Les noms d'outils spécifiés dans les configurations YAML sont validés à la création via `IToolRegistry` ; sous `Orkeon:CrewFactory:StrictTools` (le défaut des runners) aucun outil non enregistré ne peut être assigné à un agent — le défaut bibliothèque est tolérant (saut + Warning).
- **Code** : `RoslynCodeSecurityAnalyzer` (`ICodeSecurityAnalyzer`) et les implémentations d'`ICodeSandbox` — `DockerSandbox`, `HostProcessRunner` derrière `LazyProbingCodeSandbox` (`Orkeon.Infrastructure.Sandbox`) — pour l'exécution sécurisée de code C#
- **Données** : `IDatabaseSecurityPolicy` pour la validation des requêtes SQL/NoSQL, `PiiDetector` et les cinq implémentations d'`IDlpInterceptor` (sortie d'outil, délégation, log, mémoire, sortie externe — `Orkeon.Infrastructure.Security.Dlp`). Le sous-système DLP est **opt-in** (`AddOrkeonDlp()`, voir [Sous-systèmes opt-in](../reference/opt-in-subsystems.md))
- **Chiffrement** : `IEncryptionProvider` avec implémentation AES, applicable aux providers mémoire via le pattern Decorator. La rotation de clés est **opt-in** (`AddOrkeonKeyRotation()`)
- **Audit** : `AuditLogger` (`Orkeon.Infrastructure.Security`) avec ses sinks (`Orkeon.Infrastructure.Security.Sinks`) ; `ProvenanceTracker` vit côté RAG (`Orkeon.Rag.Validation`)
- **Conformité** : `NistComplianceReportGenerator` (`INistComplianceReporter`, `Orkeon.Infrastructure.Compliance`). Le reporting NIST est **opt-in** (`AddOrkeonNistCompliance()`)

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

- **Opt-in strict** : le repli est éteint par défaut derrière **deux** interrupteurs indépendants — le transport (`Orkeon:Rag:WebFallback:Enabled`, câblé par `AddOrkeonRagWebFallback(configuration)`) et la politique du graphe correctif (`Orkeon:Rag:Corrective:WebFallback:Enabled`) ; les deux doivent être actifs pour qu'un document web atteigne jamais le graphe. L'activer sans configurer d'endpoint le laisse inerte et journalise un avertissement — aucune sortie réseau silencieuse. La clé d'API est lue depuis une variable d'environnement (`ApiKeyEnvVar`), jamais depuis les fichiers de configuration.
- **Validation déterministe** : chaque page téléchargée passe par `PromptInjectionDocumentValidator` (`Orkeon.Rag.Validation`) — heuristiques pures, sans LLM, testables hors ligne. Signaux détectés : directives adressées au modèle (EN + FR : « ignore previous instructions », « you are now… », « system: », « nouvelles instructions : »…), tokens de contrôle de gabarits de chat (`<|im_start|>`, `<<SYS>>`, `[INST]`…) avec accumulation par occurrence, balisage HTML caché (`display:none`, `visibility:hidden`, opacité/taille de police quasi nulles, `aria-hidden`), commentaires HTML massifs, vecteurs d'exfiltration (images markdown auto-chargées avec payloads encodés en query, payloads de liens percent-encodés, data URIs base64) et longs blobs base64. Le verdict par document est `Clean | Suspicious | Rejected` avec raisons et extraits incriminés.
- **Traçabilité, pas de nettoyage silencieux** : les documents `Rejected` ne quittent jamais le retriever et sont journalisés avec leurs raisons et leur URL. Les documents `Suspicious` sont — selon `SuspiciousAction` — soit écartés (journalisés), soit conservés **marqués** dans les métadonnées (`injection_verdict`, `injection_reasons`, `injection_risk_score`). Le contenu n'est jamais réécrit : le validateur détecte, il n'assainit pas. Sur le chemin d'ingestion, le même validateur siège dans le `DataValidationPipeline` (chaîne `IDataValidator`) avec quarantaine et suivi de provenance.
- **Fail-quiet sur le réseau, fail-loud sur la sécurité** : les erreurs HTTP de recherche et les timeouts dégradent vers une liste de résultats vide avec un avertissement (le graphe correctif poursuit simplement sans contexte web) ; les rejets sont toujours des avertissements avec les raisons complètes.

**Limites honnêtes** : ces heuristiques sont fondées sur des motifs et peuvent être contournées — directives paraphrasées, langues autres que l'anglais/le français, encodages inédits ou payloads fractionnés passeront. Inversement, des documents techniques légitimes *sur* les prompts ou le CSS peuvent scorer `Suspicious` (ils sont marqués, jamais écartés en silence). Un validateur de contenu est un filtre, **pas une frontière de privilèges** : le vrai confinement est architectural — le texte récupéré doit rester de la donnée (jamais exécuté comme instruction), les agents consommant du contenu web devraient tourner avec des outils au moindre privilège, et les actions sensibles ne doivent pas être déclenchables par du seul contenu récupéré.

## Résilience

`ResiliencePolicies` (`Orkeon.Infrastructure.Resilience`) expose les politiques Polly — `GetRetryPolicy`, `GetCircuitBreakerPolicy`, `GetTimeoutPolicy`, `GetCombinedPolicy`, `GetLlmApiPolicy` — utilisées par les providers LLM et les outils HTTP.

## Checkpointing

`CheckpointManager` et `ResumeEngine` (`Orkeon.Application.Services.Checkpointing`) permettent la sauvegarde et la reprise d'état des exécutions de crew. Utile pour les longs workflows qui doivent survivre aux redémarrages.

Depuis R3.8, les **états d'exécution** eux-mêmes (`ICrewExecutionStateManager` : statut, progression, sortie) peuvent aussi être persistés dans les mêmes state stores (`IStateStore` — in-memory, SQLite via `AddOrkeonSqliteCheckpointing`, PostgreSQL) pour la reprise après crash. Opt-in via `AddCrewExecutionStatePersistence(...)` — ou la section `Orkeon:ExecutionState:Persistence`, qui n'a d'effet qu'à travers la surcharge `AddOrkeonInfrastructure(IConfiguration)` (les runners livrés utilisent la surcharge sans paramètre : pour eux, l'appel explicite est la voie) ; défaut : in-memory only. Voir [Sous-systèmes opt-in](../reference/opt-in-subsystems.md).

## Système de plugins

`IOrkeonPlugin`, `PluginAssemblyDiscovery`, `PluginLoader`/`PluginLoadContext` et `IPluginRegistry` (`Orkeon.Plugins`) fournissent un système de plugins pour étendre le framework avec des outils et providers personnalisés : découverte sur répertoire via le VFS, chargement isolé par `AssemblyLoadContext` collectible, activation DI opt-in `AddOrkeonPlugins(...)`.

> ⚠️ **Frontière de confiance** : charger un plugin exécute du code arbitraire avec les privilèges du processus hôte — pas de sandbox en v1, et l'`AssemblyLoadContext` n'est pas une frontière de sécurité. Ne charger que des plugins de confiance. Détails et règles d'exploitation : [Système de plugins](../architecture/plugins.md).

---

> **Voir aussi** : [Fournisseurs LLM](../architecture/llm-providers.md) · [Événements et CQRS](../architecture/domain-events.md) · [Retour à l'index](../INDEX.md)
