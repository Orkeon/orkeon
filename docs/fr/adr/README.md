> 🇬🇧 [English version](../../adr/README.md)

# Architecture Decision Records

Archives datées et immuables des décisions structurantes d'Orkeon. Un ADR
remplacé conserve son texte d'origine ; celui qui le remplace pointe en retour.

> **Pourquoi la numérotation commence-t-elle à 002 ?** Il n'existe pas
> d'ADR-001 — la première décision enregistrée en ADR a reçu le numéro 002 et
> le trou a été conservé plutôt que de renuméroter, pour garder stables les
> références croisées existantes.

| ADR | Décision | Statut |
|---|---|---|
| [ADR-002](./ADR-002-tool-abstractions-shared-kernel.md) | `Orkeon.Tools.Abstractions` en shared kernel (l'exception `Infrastructure → Tools.Abstractions`) | Accepté |
| [ADR-003](./ADR-003-shared-kernels-secondaires.md) | Shared kernels secondaires : `Application → Analysis.Abstractions`, `Infrastructure → Analysis` | Accepté |
| [ADR-004](./ADR-004-jumeaux-de-nommage-scripting.md) | Les jumeaux de nommage scripting, documentés sans renommage | Remplacé par l'ADR-007 |
| [ADR-005](./ADR-005-famille-tools-heterogene.md) | La famille `Tools.*` hétérogène (`Tools.Web`/`Tools.EventHub → Application`) | Accepté |
| [ADR-006](./ADR-006-rag-subsystem.md) | Le sous-système `src/rag/` : shared kernel `Orkeon.Rag.Abstractions`, anciens namespaces RAG retirés sans shims | Accepté |
| [ADR-007](./ADR-007-d3-renommage-cli-commands-scripting.md) | Décision D3 : `Orkeon.Cli.Scripting` renommé en `Orkeon.Cli.Commands.Scripting` avant toute publication NuGet | Accepté |
| [ADR-008](./ADR-008-virtual-paths-are-the-only-currency.md) | Un chemin physique n'est jamais un chemin virtuel : les runners montent sous un nom, les montages d'infrastructure sont invisibles aux agents | Accepté — son `RunnerMounts` est devenu `RunnerVirtualRoots` (ADR-009) ; la décision tient, le type a bougé |
| [ADR-009](./ADR-009-shared-constants-satellites.md) | Une constante sur laquelle deux projets doivent s'accorder vit dans un satellite sans dépendance, pas dans une copie manuelle gardée par un test de drift | Accepté — mis en œuvre (cinq satellites sous `src/constants/`) |
| [ADR-010](./ADR-010-agent-framework-interop.md) | L'interop Microsoft Agent Framework est un paquet séparé (`Orkeon.Interop.AgentFramework`), dans les deux sens : un crew comme `AIAgent`, un `AIAgent` comme cerveau ou outil d'un agent Orkeon ; le contexte initial atteint enfin les prompts | Accepté |
| [ADR-011](./ADR-011-aspire-dashboard-observability.md) | Les runners exportent OpenTelemetry selon le contrat standard (`OTEL_EXPORTER_OTLP_ENDPOINT`) ; `Orkeon.Hosting.Aspire` les ajoute à un AppHost ; le dashboard Aspire est la surface d'observabilité cross-platform et aucun Studio web ne sera construit | Accepté |
| [ADR-012](./ADR-012-email-tool-family.md) | La famille d'outils e-mail (`Orkeon.Tools.Email`) est la deuxième exception motivée au gel du périmètre : MailKit/MimeKit pour IMAP, POP3 et SMTP, Microsoft Graph pour Outlook.com, OAuth2 écrit à la main avec les jetons dans la racine interne `/credentials` derrière la vue privilégiée du VFS, droits par compte et liste d'autorisation d'envoi fermée par défaut ; `email_parser` y est reconstruit | Accepté |
| [ADR-013](./ADR-013-drive-tool-family.md) | La famille d'outils drive (`Orkeon.Tools.Drive`) est la troisième exception motivée au gel du périmètre : Google Drive et OneDrive en HTTP simple, la couche OAuth déplacée dans `Orkeon.Tools.Abstractions` et partagée avec l'e-mail, droits et dossier racine par compte, liste d'autorisation de partage fermée par défaut, jetons dans le sous-répertoire `drive` de `/credentials` | Accepté |
| [ADR-014](./ADR-014-shell-confinement-by-default.md) | `shell_command` est confiné par le système d'exploitation par défaut : `Auto` choisit bubblewrap, Seatbelt ou un AppContainer, rien dans un conteneur, l'appel échoue franchement quand le moteur ne peut pas tourner ; une session shell persistante par exécution d'agent, tuée avec elle | Accepté |
| [ADR-015](./ADR-015-model-provider-categories-and-decision-stages.md) | Les fournisseurs de modèles se rangent en quatre catégories (génération, embedding, reranking, décision), chacune avec son contrat et listée dans `ModelProviderCatalog` ; le Jev de TypeSafe se tient derrière un port distinct `IDecisionModel`, pas comme 17ᵉ fournisseur LLM ; une étape de décision autour d'un appel au LLM bloque, route ou escalade et ne réécrit jamais | Accepté |
| [ADR — RaggableTree](../architecture/raggable-tree-adr.md) | Graphe sémantique stratifié à 5 niveaux plus arêtes via Tree-sitter (non numéroté — vit avec son guide d'architecture ; amendé le 2026-08-18) | Accepté |
