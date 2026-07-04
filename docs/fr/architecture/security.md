> 🇬🇧 [English version](../../architecture/security.md)

# Sécurité, résilience et plugins

## Sécurité

Le framework intègre plusieurs couches de sécurité :

- **Outils** : `ToolAccessPolicy` (whitelist/blacklist/unrestricted) par agent, `IPathValidator` pour la protection path traversal, `IUrlValidator` pour la protection SSRF, `HttpHeaderSanitizer`. Les noms d'outils spécifiés dans les configurations YAML sont validés à la création via `IToolRegistry` — aucun outil non enregistré ne peut être assigné à un agent.
- **Code** : `CodeSecurityAnalyzer` et `CodeSandbox` (`Orkeon.Infrastructure.Sandbox`) pour l'exécution sécurisée de code C#
- **Données** : `IDatabaseSecurityPolicy` pour la validation des requêtes SQL/NoSQL, `SensitiveDataDetector` et `DlpInterceptor` (`Orkeon.Infrastructure.Security.DLP`). Le sous-système DLP est **opt-in** (`AddOrkeonDlp()`, voir [Sous-systèmes opt-in](../reference/opt-in-subsystems.md))
- **Chiffrement** : `IEncryptionProvider` avec implémentation AES, applicable aux providers mémoire via le pattern Decorator. La rotation de clés est **opt-in** (`AddOrkeonKeyRotation()`)
- **Audit** : `AuditLogger`, `ProvenanceTracker` (`Orkeon.Infrastructure.Security.Sinks`)
- **Conformité** : `ComplianceChecker` et `NistComplianceReporter` (`Orkeon.Infrastructure.Compliance`). Le reporting NIST est **opt-in** (`AddOrkeonNistCompliance()`)

### Résolution des outils en YAML

À la création d'une Crew depuis YAML, chaque nom d'outil dans `agents[].tools[]` est résolu via `IToolRegistry.GetToolByNameAsync(toolName)`. Si un outil n'existe pas :

- `CrewFactory` lève une exception de validation spécifiant les outils manquants
- La création de l'agent est bloquée — impossible d'assigner un outil invalide
- Un message d'erreur liste les outils disponibles dans le registre

Cela garantit qu'aucun agent ne peut opérer avec des capacités inexistantes.

### TLS mutuel A2A

Le sous-système A2A opt-in (`AddOrkeonA2A()`) applique la posture de sécurité déclarée dans `A2ASecurityOptions` :

- **Serveur** (`RequireMutualTls = true`) : le certificat client entrant est **authentifié, pas seulement exigé** — il doit chaîner vers l'une des `TrustedCertificateAuthorities` configurées (chaîne X509 construite en `CustomRootTrust`, qui couvre aussi la fenêtre de validité) ou correspondre à une empreinte épinglée de `TrustedClientCertificateThumbprints`. Les certificats auto-signés non épinglés sont rejetés (403). Démarrer le serveur avec `RequireMutualTls` sans aucune ancre de confiance **lève une exception** (fail-closed) : présence + dates valides ne font pas une authentification. La révocation n'est pas vérifiée — les ancres de confiance sont supposées être des CA privées sans endpoints CRL/OCSP.
- **Client** : `ValidateServerCertificate = true` (défaut) conserve la validation TLS complète. Configurer `TrustedCertificateAuthorities` épingle la confiance sur des CA privées ; cet épinglage ne couvre que la **chaîne inconnue** (`RemoteCertificateChainErrors`) — un mismatch de nom d'hôte ou un certificat absent n'est jamais contourné. L'opt-out explicite `ValidateServerCertificate = false` accepte n'importe quel certificat, **journalise un avertissement de sécurité**, et est strictement réservé au développement local.
- La terminaison TLS mutuelle réelle requiert un binding HTTPS de `HttpListener` au niveau de l'OS — voir [Limitations connues](../reference/limitations.md).

## Résilience

`RetryPolicy`, `CircuitBreaker` et `TimeoutPolicy` (`Orkeon.Infrastructure.Resilience`) fournissent des mécanismes de résilience basés sur Polly, utilisés par les providers LLM et les outils HTTP.

## Checkpointing

`CheckpointManager` et `ResumeEngine` (`Orkeon.Application.Services.Checkpointing`) permettent la sauvegarde et la reprise d'état des exécutions de crew. Utile pour les longs workflows qui doivent survivre aux redémarrages.

Depuis R3.8, les **états d'exécution** eux-mêmes (`ICrewExecutionStateManager` : statut, progression, sortie) peuvent aussi être persistés dans les mêmes state stores (`IStateStore` — in-memory, SQLite via `AddOrkeonSqliteCheckpointing`, PostgreSQL) pour la reprise après crash. Opt-in via `AddCrewExecutionStatePersistence(...)` ou la section `Orkeon:ExecutionState:Persistence` ; défaut : in-memory only. Voir [Sous-systèmes opt-in](../reference/opt-in-subsystems.md).

## Système de plugins

`IOrkeonPlugin`, `PluginAssemblyDiscovery`, `PluginLoader`/`PluginLoadContext` et `IPluginRegistry` (`Orkeon.Plugins`) fournissent un système de plugins pour étendre le framework avec des outils et providers personnalisés : découverte sur répertoire via le VFS, chargement isolé par `AssemblyLoadContext` collectible, activation DI opt-in `AddOrkeonPlugins(...)`.

> ⚠️ **Frontière de confiance** : charger un plugin exécute du code arbitraire avec les privilèges du processus hôte — pas de sandbox en v1, et l'`AssemblyLoadContext` n'est pas une frontière de sécurité. Ne charger que des plugins de confiance. Détails et règles d'exploitation : [Système de plugins](../architecture/plugins.md).

---

> **Voir aussi** : [Fournisseurs LLM](../architecture/llm-providers.md) · [Événements et CQRS](../architecture/domain-events.md) · [Retour à l'index](../INDEX.md)
