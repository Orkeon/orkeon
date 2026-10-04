> 🇬🇧 [English version](../INDEX.md)

# Documentation Orkeon

## Structure

La documentation est organisée en 7 sections thématiques.

> **Politique de langue** — chaque page sous `docs/` est maintenue en anglais et en français en parallèle (`docs/fr/` reflète l'arborescence chemin pour chemin) ; un miroir manquant fait échouer la CI (`scripts/check-docs-parity.sh`). Voir [CONTRIBUTING.fr.md](../../CONTRIBUTING.fr.md) pour le contrat.

> **En ligne** — cette arborescence et la référence d'API générée sont publiées à l'adresse <https://orkeon.github.io/orkeon/>, déployées par `docs.yml` à chaque tag `v*`. Le site paraît donc avec la première release taguée et documente toujours une version taguée.

### Démarrage

| Fichier | Description |
|---------|-------------|
| [Trois façons d'exécuter Orkeon](./getting-started/three-ways-to-run-orkeon.md) | Page centrale : depuis les sources vs binaire de release vs conteneur — prérequis, commandes, canaux d'installation par OS et tableau comparatif ; la mise à jour, vers la dernière release ou vers le dernier `main` (canal dev) |
| [Exécuter votre premier exemple (depuis les sources)](./getting-started/run-your-first-example.md) | Première exécution de bout en bout : prérequis, matrice de profils LLM, la commande complète, chaque flag du runner, résolution des settings, dépannage |
| [Vue d'ensemble](./getting-started/overview.md) | Architecture, concepts fondamentaux (Agent, Task, Tool, Crew), YAML vs Fluent Builder |
| [Bootstrap et exécution](./getting-started/bootstrap.md) | Packages, câblage DI d'un hôte embarquant Orkeon (modèle, montages VFS, registre d'outils), exécution d'une Crew, modes batch/streaming/fire-and-forget |
| [YAML, Builders et CrewFactory](./getting-started/yaml-and-builders.md) | Fluent Builders, schéma YAML, pipeline CrewFactory, modes de chargement |
| [Comportements par défaut](./getting-started/default-behaviors.md) | Les défauts DI délibérément minimaux (planner, delegator, knowledge store…) : ce que chacun fait, le signal warn-once, et le geste de remplacement — et les deux services sans défaut utilisable (VFS, modèle) |
| [Forger une équipe à partir d'un besoin](./getting-started/forge-a-team-from-a-need.md) | L'Atelier (`orkeon forge`) : besoin → entretien → équipe → essai en bac à sable → verdict contre vos propres critères → promotion, sessions reprenables sur disque ; autres options (`--read`, `--reference`, `--with-settings`, `forge rename`) |

### E-mail

Donner une boîte aux lettres aux agents est l'une des fonctionnalités les plus demandées : le tutoriel en met une en service en quelques minutes, le guide couvre chaque fournisseur et chaque réglage.

| Fichier | Description |
|---------|-------------|
| [Donner une boîte aux lettres à vos agents](./getting-started/give-your-agents-a-mailbox.md) | Tutoriel : un compte Gmail, un mot de passe d'application, et un agent qui range le courrier non lu dans des dossiers et laisse chaque réponse en brouillon — rien n'est envoyé |
| [Outils e-mail](./guides/email.md) | Donner une boîte aux lettres aux agents : Gmail (mot de passe d'application ou OAuth2), Hotmail/Outlook.com via Microsoft Graph, votre propre serveur IMAP/POP3/SMTP ; droits par compte, liste d'autorisation d'envoi fermée par défaut, filtrage anti-injection, `orkeon email login`, emplacement des jetons, dépannage — campagne en attente |
| [Commandes `orkeon email`](./reference/cli.md#orkeon-email) | `accounts` (ce qui est déclaré, ce qui est prêt), `check` (se connecter et lister les dossiers), `login` / `logout` pour les comptes OAuth2 — options et codes de sortie |
| [ADR-012 — la famille d'outils e-mail](./adr/ADR-012-email-tool-family.md) | Pourquoi l'e-mail vit dans Orkeon même, et les alternatives écartées |

### Architecture

| Fichier | Description |
|---------|-------------|
| [Fournisseurs LLM](./architecture/llm-providers.md) | 16 providers (OpenAI, Anthropic, Azure, Grok, Ollama, les agrégateurs OpenRouter et Mammouth, etc.), capacités et points d'extension du dialecte, résolution du provider, décorateurs et journalisation des échanges LLM, kit de campagnes sur API réelle |
| [Système de mémoire](./architecture/memory-system.md) | 5 types de mémoire, 6 providers (InMemory, Redis, SQLite, ChromaDB, Pinecone, LanceDB), mémoire cognitive |
| [Événements, CQRS et observabilité](./architecture/domain-events.md) | 31 domain events — ceux des tâches et des agents au fil du run, ceux de la crew en fin de kickoff, et par les commandes CQRS —, pipeline CQRS, 2 surfaces d'observation (`ICrewExecutionHook`, `ICallbackHandler` avec une étape par appel d'outil) |
| [EventHub et cycle de vie des crews](./architecture/event-hub-and-crew-lifecycle.md) | Spécification de référence du messaging inter-agents et inter-crews (EventHub) et de la mise en sommeil/réveil des crews — ports Application, l'adaptateur en mémoire et ses cinq étages de middleware (les hôtes livrés ne câblent que l'ACL), les outils agents ; la persistance SQLite et le sommeil/réveil sont spécifiés, pas encore construits |
| [Le host de service et la passerelle de chat](./architecture/service-host.md) | `orkeon-host` : héberger des crews en daemon (systemd, service Windows, conteneur), isolation par run, et la passerelle Discord avec sa liste d'autorisation, son routage thread-est-run et son bouton d'arrêt |
| [Le bus d'événements du run](./architecture/run-event-bus.md) | `orkeon run --events jsonl` : le protocole versionné qu'un autre programme lit, les commandes qu'il peut renvoyer, et le siège `client://` qu'il obtient sur le hub du run |
| [Sécurité, résilience et plugins](./architecture/security.md) | 6 couches de sécurité, politiques Polly, checkpointing, système de plugins |
| [Conformité VFS](./architecture/vfs-compliance.md) | Principe VFS-only (tout I/O via `IFileSystemService`) : analyseur Roslyn `Orkeon.Compliance.Vfs`, 7 diagnostics, périmètres exemptés, critères de sortie de la migration |
| [Système de plugins](./architecture/plugins.md) | Contrat `IOrkeonPlugin`, découverte VFS, isolation `AssemblyLoadContext`, activation opt-in `AddOrkeonPlugins`, ⚠️ frontière de confiance |
| [DSL de scripting](./architecture/scripting.md) | DSL à syntaxe TypeScript (`.ork.ts`) : transpilation esbuild, exécution sandboxée Jint, toute la surface Orkeon (agents, crews, tools, FSM, graphes, événements) via builders fluides |
| [Commandes CLI TypeScript](./architecture/cli-ts-commands.md) | Commandes REPL interactives en `*.cmd.ts` (`defineCommand`) chargées au démarrage sans recompilation .NET, avec dispatch de travail vers les agents |
| [Client & serveur MCP](./architecture/mcp.md) | Intégration Model Context Protocol bi-ère (`2026-07-28` stateless + révisions legacy), transports, activation, outils MCP et crews, limites honnêtes |
| [Orkeon Studio](./architecture/studio.md) | Les front-ends graphique/terminal au-dessus des workflows CLI : les quatre projets, le cœur partagé, la localisation, la distribution |
| [Piloter des crews depuis le REPL](./architecture/coding-agent-ts.md) | Le pont `.cmd.ts` → `crew.ork.ts` : plan de contrôle `*.cmd.ts` vs moteur `crew.ork.ts` via le service `script-host`, `ctx.llm.act` comme boucle d'agent, le garde de permissions et les ports de session — avec une session REPL sans clé, exécutable |
| [Référence YAML](./architecture/yaml-schema.md) | **Source unique** du schéma YAML complet (crew, agents, tasks, graphConfig) |
| [RaggableTree — graphe sémantique](./architecture/raggable-tree.md) | Pipeline 6 phases, 15 tools, 5 langages, réindexation incrémentale, fraîcheur paresseuse, recherche hybride, injection de contexte, ce que les hôtes câblent réellement |
| [Pipeline RAG](./architecture/rag-pipeline.md) | Le sous-système `src/rag/` : ingestion, pipeline à 7 étages (transform → retrieve → fuse/MMR → rerank → assemble → generate → groundedness), graphe correctif CRAG, repli web, 5 profils, évaluation mesurée |
| [ADR — RaggableTree](./architecture/raggable-tree-adr.md) | Décision graphe stratifié via Tree-sitter (5 niveaux L0–L4 plus la couche d'arêtes), alternatives rejetées, conséquences, amendement d'état d'implémentation du 2026-09-30 |
| [Décisions d'architecture (ADR)](./adr/README.md) | ADR-002 (shared kernel Tools.Abstractions), ADR-003 (shared kernels Analysis), ADR-004 (jumeaux de nommage scripting — remplacé par l'ADR-007), ADR-005 (famille Tools.* hétérogène), ADR-006 (sous-système RAG `src/rag/`), ADR-007 (D3 : renommage `Orkeon.Cli.Commands.Scripting`), ADR-008 (les chemins virtuels sont la seule monnaie versée aux agents), ADR-009 (satellites de constantes partagées), ADR-010 (interop Microsoft Agent Framework en paquet séparé, dans les deux sens), ADR-011 (le dashboard Aspire est la surface d'observabilité ; pas de Studio web), ADR-012 (la famille d'outils e-mail, deuxième exception motivée au gel du périmètre) |

### Orchestration

| Fichier | Description |
|---------|-------------|
| [Guide comparatif ProcessTypes](./orchestration/process-types.md) | Les 6 stratégies côte à côte : matrice, arbre de décision, pros/cons, coûts |
| [FSM — Machine à états](./orchestration/fsm.md) | Moteur FSM générique du domaine (utilisé par `orkeon forge` ; sa policy de circuit breaker borne Graph et le graphe RAG correctif), 4 mécanismes, presets, guards |
| [Graph — Graphe d'états](./orchestration/graph.md) | Le mode de process Graph (topologie fixe, circuit breaker à 3 mécanismes) et l'API C# `StateGraph` style LangGraph : edges conditionnels, cycles contrôlés, retry |
| [Autonomous — Auto-organisation](./orchestration/autonomous.md) | Budget multi-dimensions, tâches assignées par le manager, délégation à un pair en cas d'échec, spawn fourni par l'hôte, canal A2A |

### Outils

| Fichier | Description |
|---------|-------------|
| [Inventaire des outils](./tools/inventory.md) | 91 outils par catégorie avec leurs paramètres d'appel, résolution YAML (stricte/tolérante), enregistrement DI, disponibilité par hôte, gaps identifiés |
| [Créer un nouvel outil](./tools/new-tool-pattern.md) | Pipeline typé, attributs FieldSchema/ReturnSchema, pattern composition, enregistrement |

### Guides

| Fichier | Description |
|---------|-------------|
| [Écrire une crew en TypeScript](./guides/write-a-crew-in-typescript.md) | Le DSL `.ork.ts` de bout en bout : les deux formes de script et pourquoi choisir la mauvaise abandonne la moitié de ce que vous avez écrit (bruyamment, depuis rc.3) : tâches, agents, outils, le DAG `withContext`, livrables, `ctx.llm.act`, état, réglage de l'éditeur |
| [Méthodologie de portage](./guides/porting-methodology.md) | 5 étapes pour migrer une application, YAML-first vs Code-first, estimation effort |
| [Exemple de portage](./guides/porting-example.md) | Pipeline e-commerce complet : analyse, mapping agents, YAML, bootstrap C# |
| [Blueprint nouvelle orchestration](./guides/blueprint.md) | Template 8 étapes pour ajouter un nouveau ProcessType au framework |
| [Contenu multi-modal (vision)](./guides/multimodal.md) | Vision réelle (R3.9) : `MultiModalContent` → `LlmMessage` → blocs Anthropic / parts OpenAI (14 providers compatibles) / images Ollama, chargeur VFS, activation opt-in |
| [Format de réponse LLM](./guides/llm-response-format.md) | Sortie contrainte à la frontière provider (`json_object` / `json_schema`), traduction pilotée par les capacités de chaque provider, cascade crew → agent → task → script → appel |
| [Quality Gate SonarQube](./guides/quality-gate.md) | Analyse SonarQube locale avec la gate « Orkeon Transitional » : seuils transitoires, trajectoire de durcissement, provisionnement automatique par les scripts |
| [Modèles locaux](./guides/local-models.md) | Tout exécuter sur sa machine : Docker Model Runner (pull/configure/inspect, contextes 128K), Ollama, variante d'image `local-llm` embarquée, changement de modèle, dépannage |
| [Vérifier ce que vous installez](./guides/verify-what-you-install.md) | Ce que la chaîne de provenance prouve et ne prouve pas (Trusted Publishing OIDC, attestations SLSA, `SHA256SUMS`, SBOM), les commandes exactes `gh attestation verify` / `dotnet nuget verify`, et pourquoi un paquet nuget.org doit être dé-signé avant que son digest corresponde |
| [Installer avec apt](./guides/install-with-apt.md) | Debian / Ubuntu (amd64, arm64) : ajouter une fois la source apt signée d'Orkeon, puis `apt install` / `apt upgrade` ; les canaux `stable`, `rc` et `dev`, le retour à une version, la désinstallation, les builds Docker, le dépannage |

### Référence

| Fichier | Description |
|---------|-------------|
| [Référence du DSL de scripting](./reference/scripting-dsl.md) | Chaque builder et chaque méthode `.ork.ts`, avec la colonne qui n'existe nulle part ailleurs : laquelle des deux formes l'honore. Plus les écarts connus entre les typings et le runtime |
| [Catalogue des exemples](./reference/examples-catalog.md) | Carte éditoriale d'`examples/` (9 catégories métier + vitrines RAG/RaggableTree/scripting) ; l'`examples/INDEX.md` généré est l'inventaire faisant foi |
| [Référence CLI `orkeon`](./reference/cli.md) | Chaque commande (`run`, `init`, `llm`, `rag`, `forge`, `usecases`, `email`, `doctor`) avec options et exemples, plus `orkeon-repl` |
| [Référence de configuration](./reference/configuration.md) | La carte unique des sections d'`appsettings.json` (`Llm`, `Orkeon:*`, `MCP`), sources et précédence, colonne opt-in |
| [Limites et contraintes](./reference/limitations.md) | Contraintes connues de la version courante, dont ce qui est livré mais pas encore câblé |
| [Matrice de conformité A2A](./reference/a2a-conformance.md) | Position honnête face à la spec A2A v1.0 : opérations, modèle de données, bindings, sécurité — ce qui interopère et ce qui n'interopère pas |
| [APIs expérimentales](./reference/experimental-apis.md) | Surfaces `[Experimental]` (A2A, Autonomous, RAG correctif, MCP), IDs de diagnostic `ORKEXP001–004`, comment s'inscrire |
| [Politique de données des exemples](./reference/example-data-policy.md) | Pourquoi les exemples livrent de la config et pas des datasets, comment monter vos entrées (`/data:ro`, `/output:rw`), règles contributeurs pour les fixtures |
| [Hosting & bootstrap des runners](./reference/hosting.md) | `Orkeon.Hosting` : `RunnerHost.Build`, ordre de câblage `ConfigureRunnerServices` (LLM d'abord, suites d'outils, VFS, le `ToolRegistry` par défaut), flux `RunnerExecution`, télémétrie (OTLP), .NET Aspire (`AddOrkeonHost`, `AddOrkeonCrewRun`), pattern hôte web |
| [Interop Microsoft Agent Framework](./reference/agent-framework-interop.md) | Une crew Orkeon comme `AIAgent` MAF (`CrewAgent`, `AddOrkeonAgentFramework`) ; un agent MAF comme modèle d'un agent Orkeon (`WithAgentFrameworkAgent`) ou comme outil (`WithAgentFrameworkTool`) |
| [Gabarit de README d'exemple](./templates/example-readme.md) | Gabarit pour `examples/**/README.md` : Ce qu'il fait / Prérequis / Données requises / L'exécuter / Sortie attendue / Durée & coût |
| [Sous-systèmes opt-in](./reference/opt-in-subsystems.md) | A2A, monitoring, NIST, DLP, rate-limiting d'outils, rotation de clés, benchmarking, multi-modal, hooks de kickoff, sous-système RAG — activation explicite `AddOrkeonXxx()` (hors DI par défaut) ; plus les briques que `AddOrkeonInfrastructure()` enregistre et celles que rien ne consomme |
| [Comparatif des fournisseurs LLM](./reference/llm-providers-comparison.md) | Matrice de capacités par provider (streaming SSE, tool calling natif, grammaire GBNF, `response_format`, thinking, métriques, résilience), dérivée du code source |
| [Matrice de publication](./reference/publication-matrix.md) | **Source de vérité** de ce qui est publié où : NuGet.org vs GitHub Packages, tools dotnet, artefacts d'installation, flux de version, le canal dev (le dernier `main` entre deux tags) |

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

### "Je veux que mes agents traitent des e-mails"

```
give-your-agents-a-mailbox → email (guide) → cli (orkeon email) → security
```

Mettez une boîte aux lettres en service de bout en bout avec le tutoriel, puis ouvrez le guide pour votre fournisseur (Gmail OAuth2, Outlook.com/Hotmail via Microsoft Graph, votre propre serveur IMAP/POP3/SMTP), le modèle de droits et la liste d'autorisation d'envoi. La référence CLI liste les commandes `orkeon email` ; la page sécurité situe le filtrage anti-injection parmi les autres couches.
