> 🇬🇧 [English version](../../tools/inventory.md)

> **Voir aussi** : [Créer un nouveau tool](./new-tool-pattern.md) · [Retour à l'index](../INDEX.md)

# Inventaire des tools Orkeon

Cette page est le catalogue de référence des **79 classes de tools intégrées**. La règle
de comptage est celle qu'applique `scripts/check-doc-claims.py` : chaque fichier
`*Tool.cs` sous `src/`, sauf l'interface (`IBaseTool.cs`) et l'infrastructure non-tool
qui matche le glob (`MockTool.cs`, `JsTool.cs`, `ObservedTool.cs`). Le même script
vérifie que **chaque nom de tool ci-dessous existe dans le code et que chaque tool du
code est nommé ici** — cette page ne peut plus dériver silencieusement de l'implémentation.

La colonne `Tool` est le nom exact que les agents et les listes YAML `tools:` utilisent
(le `UniqueName` du contrat). Les tools réellement disponibles à l'exécution dépendent de
la racine de composition — voir [Disponibilité par racine de composition](#disponibilité-par-racine-de-composition).

## Collaboration et humain dans la boucle (`Orkeon.Infrastructure`)

| Tool | Classe | Enregistrement | Cas d'usage | Exemple d'appel |
|-------|--------|------|-------------|-----------------|
| `ask_question_to_coworker` | `AskQuestionTool` | Construit par agent par `AgentDelegationToolsProvider` quand `AllowDelegation` est actif — jamais en DI | Poser une question à un agent coéquipier spécialisé | `{ "question": "What is the Q4 revenue?", "coworker": "Financial Analyst" }` |
| `delegate_work_to_coworker` | `DelegateWorkTool` | Même construction par agent (avec `AgentExecutionBudget` optionnel pour borner la profondeur récursive) | Déléguer une tâche complète à un agent spécialisé | `{ "task": "Analyze competitor pricing", "coworker": "Market Researcher", "context": "Focus on SaaS B2B" }` |
| `spawn_agent` | `SpawnAgentTool` | **Câblé par aucune racine de composition livrée** — l'hôte l'enregistre explicitement (`IAgentFactory` requis) | Créer et exécuter un sous-agent spécialisé à l'exécution (mode autonome) | `{ "role": "Fact checker", "goal": "Verify the claims", "task": "..." }` |
| `human_input` | `HumanInputTool` | `AddOrkeonHumanInput()` — câblé par `orkeon run` (`RunnerExecution`), où la question remonte sur le bus d'événements du run | Poser une question à l'humain (texte, approbation, choix) ; le runtime se suspend jusqu'à la réponse | `{ "prompt": "Deploy to production?", "kind": "approval" }` |

Les noms d'affichage des trois premiers contrats sont en prose (« Ask question to
coworker », « Delegate work to coworker », « Spawn sub-agent ») ; le nom du registre —
celui ci-dessus — est ce que le YAML référence.

## Tools de recherche et de connaissance (`Orkeon.Hosting` / `Orkeon.Tools.Rag`)

| Tool | Classe | Enregistrement | Cas d'usage | Exemple d'appel |
|-------|--------|------|-------------|-----------------|
| `semantic_search` | `SearchTool` | `AddSemanticSearchTool()` (`Orkeon.Hosting`, opt-in — `orkeon run` l'appelle) | Recherche sémantique par embeddings dans les mémoires | `{ "query": "customer churn patterns", "limit": 5 }` |
| `rag_search` | `RagSearchTool` | Opt-in : `AddOrkeonRag(config)` + `AddOrkeonRagTools()` (`Orkeon.Tools.Rag`) | Recherche RAG dans les bases de connaissances de l'agent via `IRagPipeline` | `{ "question": "What is our return policy?", "top_k": 3 }` |
| `rag_ingest` | `RagIngestTool` | Même opt-in que `rag_search` | Ingestion incrémentale dans une collection RAG (pilotée par manifeste — les sources inchangées coûtent 0 embedding) | `{ "collection": "docs", "sources": ["/kb/**/*.md"], "reindex": false }` |
| `rag_eval` | `RagEvalTool` | Même opt-in que `rag_search` | Évaluer une collection contre un dataset doré YAML : recall@k, precision@k, MRR, groundedness | `{ "collection": "docs", "dataset": "/kb/eval/golden.yaml" }` |

## Tools d'exécution de code (`Orkeon.Infrastructure.Sandbox` / `Orkeon.Tools.Code`)

| Tool | Classe | Enregistrement | Cas d'usage | Exemple d'appel |
|-------|--------|------|-------------|-----------------|
| `code_interpreter` | `SecureCodeInterpreterTool` | `AddOrkeonCodeSandbox()` — **type concret seulement**, pas sous `IBaseTool` : le registre ne peut pas le résoudre par nom ; l'hôte l'injecte ou l'enregistre explicitement | Exécuter du code C# dans un sandbox isolé avec analyse de sécurité statique | `{ "code": "return 2 + 2;", "timeout_seconds": 10 }` |
| `shell_command` | `ShellCommandTool` | `AddOrkeonCodeTools()` | Exécuter des commandes shell avec allowlist/blocklist et timeout ; les arguments en chemins virtuels montés sont résolus pour le processus et les chemins physiques de la sortie reviennent virtualisés | `{ "command": "cat /workspace/README.md", "timeout_seconds": 30 }` |

## Tools de session et de mémoire (`Orkeon.Infrastructure`) — `AddOrkeonSessionTools()`

| Tool | Classe | Cas d'usage |
|-------|--------|-------------|
| `session_store` | `SessionStoreTool` | Lire/écrire/tronquer/annoter le buffer de session et ses métadonnées |
| `session_snip` | `SessionSnipTool` | Tronquer immédiatement la conversation à une fenêtre minimale |
| `session_stats` | `SessionStatsTool` | Télémétrie complète de session : messages, tokens, coût, totaux d'appels |
| `session_cost` | `SessionCostTool` | Coût cumulé de la session en USD avec ventilation par modèle |
| `token_budget` | `TokenBudgetTool` | Taille de la fenêtre de contexte, tokens consommés, budget disponible |
| `memory_store` | `MemoryStoreTool` | Lister/ajouter/supprimer/lire des mémoires typées (user/project/feedback/reference) |

## Tools fichiers (`Orkeon.Tools.FileSystem`) — `AddOrkeonFileSystemTools()`

| Tool | Classe | Base | Cas d'usage | Exemple d'appel |
|-------|--------|------|-------------|-----------------|
| `file_read` | `FileReadTool` | `FileToolBase<FileReadRequest, FileReadResponse>` | Lire le contenu d'un fichier (texte, JSON, XML, .eml, .msg) | `{ "file_path": "/data/report.txt" }` |
| `file_write` | `FileWriteTool` | `FileToolBase<FileWriteRequest, FileWriteResponse>` | Écrire du contenu dans un fichier, en créant les dossiers si nécessaire | `{ "file_path": "/output/result.txt", "content": "Analysis complete.", "append": false }` |
| `directory_read` | `DirectoryReadTool` | `FileToolBase<DirectoryReadRequest, DirectoryReadResponse>` | Lister le contenu d'un répertoire avec filtrage glob et récursion | `{ "directory": "/data", "pattern": "*.csv", "recursive": true }` |
| `directory_search` | `DirectorySearchTool` | `ToolBase<DirectorySearchRequest, DirectorySearchResponse>` | Recherche sémantique RAG dans les fichiers d'un répertoire | `{ "directory": "/docs", "query": "deployment instructions", "file_pattern": "*.md" }` |
| `email_parser` | `EmailParserTool` | `FileToolBase<EmailParserRequest, EmailParserResponse>` | Parser des emails .eml/.msg et extraire en-têtes, corps, pièces jointes | `{ "file_path": "/emails/invoice.eml" }` |
| `count_pattern` | `CountPatternTool` | `FileToolBase<CountPatternRequest, CountPatternResponse>` | Compte déterministe des occurrences d'un motif regex dans un fichier (aucune estimation LLM) | `{ "file_path": "/data/log.txt", "patterns": ["ERROR", "WARN"] }` |

## Tools de données (`Orkeon.Tools.Data`) — `AddOrkeonDataTools()`

`AddOrkeonDataTools()` enregistre les 22 (il chaîne `AddRelationalDatabaseTools()`,
`AddMongoDbTools()` et `AddGraphDatabaseTools()` en interne).

| Tool | Classe | Cas d'usage | Exemple d'appel |
|-------|--------|-------------|-----------------|
| `csv_reader` | `CsvReaderTool` | Lire et structurer des fichiers CSV avec détection du délimiteur | `{ "file_path": "/data/sales.csv", "delimiter": "," }` |
| `pdf_reader` | `PdfReaderTool` | Extraire le texte de fichiers PDF avec sélection de pages | `{ "file_path": "/docs/contract.pdf", "start_page": 1, "end_page": 5 }` |
| `json_tool` | `JsonTool` | Manipuler du JSON : parse (analyse de structure), query (navigation dot-notation), format (pretty-print) | `{ "operation": "query", "json_content": "{...}", "path": "data.users[0].name" }` |
| `xml_parser` | `XmlParserTool` | Parser du XML depuis des fichiers ou des chaînes, avec support XPath | `{ "file_path": "/data/feed.xml", "xpath": "//item/title" }` |
| `docx_reader` | `DocxReadTool` | Lire des fichiers Word avec extraction du texte, des tableaux et des métadonnées | `{ "file_path": "/docs/report.docx" }` |
| `docx_writer` | `DocxWriteTool` | Créer des fichiers Word (.docx) avec titre, paragraphes et listes à puces | `{ "file_path": "/output/report.docx", "title": "Monthly Report", "paragraphs": ["Introduction text..."] }` |
| `xlsx_reader` | `XlsxReadTool` | Lire des fichiers Excel (.xlsx) : feuilles, lignes, colonnes, métadonnées (ClosedXML) | `{ "file_path": "/data/budget.xlsx", "sheet_name": "Q3" }` |
| `xlsx_writer` | `XlsxWriteTool` | Créer ou mettre à jour des fichiers Excel (.xlsx) avec plusieurs feuilles, en-têtes et lignes | `{ "file_path": "/output/summary.xlsx", "sheet_name": "Totals", "headers": ["Region", "Revenue"], "rows": [["EMEA", "1.2M"]] }` |
| `relational_database_query` | `RelationalDatabaseTool` | Exécuter des requêtes SQL (SQL Server, PostgreSQL, MySQL, MariaDB, SQLite) | `{ "connection_string": "...", "query": "SELECT * FROM orders WHERE status = 'pending'", "query_type": "select", "provider": "postgresql" }` |
| `sqlserver_query` | `SqlServerDatabaseTool` | Requêtes SQL Server spécialisées | — |
| `postgres_query` | `PostgresDatabaseTool` | Requêtes PostgreSQL spécialisées | — |
| `mysql_query` | `MySqlDatabaseTool` | Requêtes MySQL spécialisées | — |
| `mariadb_query` | `MariaDbDatabaseTool` | Requêtes MariaDB spécialisées | — |
| `database_schema` | `DatabaseSchemaTool` | Inspection de schéma relationnel (tables, colonnes, index, clés étrangères) | — |
| `mongodb_query` | `MongoDbTool` | Exécuter des opérations MongoDB (Find, Aggregate, CRUD) | `{ "connection_string": "...", "database": "shop", "collection": "products", "operation": "find", "filter": "{ \"price\": { \"$gt\": 100 } }" }` |
| `mongodb_schema` | `MongoDbSchemaTool` | Inférence du schéma d'une collection MongoDB par échantillonnage de documents | — |
| `arcadedb_query` | `ArcadeDbTool` | Requêtes contre la base graphe ArcadeDB | — |
| `janusgraph_query` | `JanusGraphTool` | Traversées Gremlin contre JanusGraph | — |
| `graph_schema` | `GraphSchemaTool` | Inspection du schéma d'une base graphe (labels de sommets/arêtes, propriétés, index) | — |
| `pdf_search` | `PdfSearchTool` | Recherche sémantique dans des fichiers PDF via embeddings | — |
| `txt_search` | `TxtSearchTool` | Recherche sémantique dans des fichiers texte | — |
| `mdx_search` | `MdxSearchTool` | Recherche sémantique dans des fichiers MDX/Markdown (frontmatter et JSX retirés) | — |

## Tools web (`Orkeon.Tools.Web`)

`AddOrkeonWebTools()` enregistre les cinq premiers ; chacun des autres a sa propre
extension opt-in parce qu'il exige une clé ou un service support supplémentaire. Les
secrets sont toujours référencés par nom de variable d'environnement — jamais stockés en
configuration.

| Tool | Classe | Enregistrement | Cas d'usage |
|-------|--------|------|-------------|
| `http_api` | `HttpApiTool` | `AddOrkeonWebTools()` | Appels HTTP REST (GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS) avec protection SSRF |
| `web_scrape` | `WebScrapeTool` | `AddOrkeonWebTools()` | Scraper une page web avec filtrage CSS optionnel ; `cached=true` découpe et embarque la page dans le cache RAG |
| `scrape_element` | `ScrapeElementTool` | `AddOrkeonWebTools()` | Scraping ciblé d'éléments DOM via sélecteurs CSS (texte, HTML, attributs) |
| `github` | `GitHubTool` | `AddOrkeonWebTools()` | API GitHub v3 : lister/créer des issues, lire des PRs, chercher des dépôts |
| `image_generation` | `ImageGenerationTool` | `AddOrkeonWebTools()` (clé OpenAI nécessaire à l'appel) | Génération d'images via l'API OpenAI DALL-E |
| `web_search` | `WebSearchTool` | `AddOrkeonWebSearchTool()` — clé Tavily résolue à l'exécution via `ISecretProvider` | Recherche web via l'API Tavily Search |
| `brave_search` | `BraveSearchTool` | `AddOrkeonBraveSearchTool(apiKey)` — `orkeon run` ne le câble que si `BRAVE_API_KEY` est posée | Recherche web via l'API Brave Search |
| `slack_send_message` | `SlackTool` | `AddOrkeonSlackTool(botToken)` — aucune racine livrée ne l'appelle (opt-in hôte pur) | Envoyer des messages vers des canaux/utilisateurs Slack via la Web API |
| `slack_read_messages` | `SlackReadTool` | `AddOrkeonSlackReadTool(botToken)` — même opt-in hôte | Lire des messages Slack (lecture seule) |
| `cache_search` | `CacheSearchTool` | `AddOrkeonCacheSearchTool()` | Recherche sémantique dans le cache RAG que d'autres tools alimentent (ex. `web_scrape` avec `cached=true`) |

## Tools du hub d'événements (`Orkeon.Tools.EventHub`) — `AddOrkeonEventHubTools()`

Sept tools, enregistrés avec le hub en mémoire (`AddOrkeonInMemoryEventHub()`).
La sémantique complète vit dans [EventHub et le cycle de vie du crew](../architecture/event-hub-and-crew-lifecycle.md).

| Tool | Classe | Cas d'usage |
|-------|--------|-------------|
| `publish_event` | `PublishEventTool` | Publier un événement sur un topic (broadcast 1→N), scope crew et métadonnées optionnels |
| `post_message` | `PostMessageTool` | Message fire-and-forget vers une boîte (`agent://`, `crew://`, `topic://`) |
| `send_request` | `SendRequestTool` | Envoyer une requête vers une boîte et attendre la réponse corrélée (`timeout_ms` requis) |
| `reply_to` | `ReplyToTool` | Répondre à une requête en attente identifiée par son `correlation_id` |
| `receive_message` | `ReceiveMessageTool` | Tirer le prochain message d'une boîte (lecture destructive, scope crew) |
| `wait_for_event` | `WaitForEventTool` | Attendre le prochain événement d'un topic (exactement un de `timeout_ms`/`wait_forever`) |
| `get_last_value` | `GetLastValueTool` | Lire la dernière charge retenue pour une clé (cache dernière-valeur) |

## Tools d'analyse de codebase (`Orkeon.Tools.Analysis`) — `AddRaggableTreeTools()`

Quinze tools sur le graphe sémantique de code RaggableTree — le guide complet est
[RaggableTree](../architecture/raggable-tree.md).

| Tool | Classe | Cas d'usage |
|-------|--------|-------------|
| `index_codebase` | `IndexCodebaseTool` | Scanner la codebase et construire l'index RaggableTree complet |
| `incremental_reindex` | `IncrementalReindexTool` | Réindexer seulement les fichiers modifiés depuis un commit ou une liste explicite |
| `index_status` | `IndexStatusTool` | Lister chaque racine virtuelle actuellement indexée |
| `is_path_indexed` | `IsPathIndexedTool` | Vérifier si un chemin virtuel est couvert par une racine indexée |
| `codebase_map` | `CodebaseMapTool` | Carte structurelle de la codebase à un niveau de zoom donné (L0–L3) |
| `package_summary` | `PackageSummaryTool` | Détails d'un package (L1) : comptes fichiers/symboles, exports, dépendances |
| `symbol_detail` | `SymbolDetailTool` | Détail étendu d'un symbole L3 : signature, doc, métriques, membres, appelants/appelés |
| `symbol_source` | `SymbolSourceTool` | Citation de source déterministe : chemin, lignes, code, SHA-256, drapeau de stabilité |
| `codebase_search` | `CodebaseSearchTool` | Recherche hybride (vecteur + BM25) avec résultats classés et cités |
| `dependency_graph` | `DependencyGraphTool` | Graphe de dépendances à une portée (packages L1, modules L2, symboles L3) |
| `sub_graph` | `SubGraphTool` | Expansion de sous-graphe autour de graines, bornée en profondeur et en nœuds |
| `flow_trace` | `FlowTraceTool` | Tracer le flux d'appels depuis un symbole dans un budget de profondeur |
| `impact_analysis` | `ImpactAnalysisTool` | Appelants directs et transitifs affectés par la modification d'un symbole |
| `complexity_report` | `ComplexityReportTool` | Top-N des méthodes par métriques de complexité, avec médiane/P95 |
| `statement_query` | `StatementQueryTool` | Requêter les statements L4 par genre, FQN parent, ou similarité sémantique |

## Montages et embeddings locaux

| Tool | Classe | Enregistrement | Cas d'usage |
|-------|--------|------|-------------|
| `list_mounts` | `ListMountsTool` (`Orkeon.Tools.Abstractions`) | `AddOrkeonAbstractionTools()` | Lister les montages VFS visibles par l'agent, avec chemins virtuels et droits d'accès |
| `local_embed_text` | `LocalEmbedTool` (`Orkeon.Tools.Embeddings.Local`) | `AddOrkeonLocalEmbeddings()` | Embarquer des textes sur l'appareil (BGE-micro-v2 ONNX, 384 dims, CPU — pas de réseau, pas de clé d'API) |

## Tools côté hôte

| Tool | Classe | Enregistrement | Cas d'usage |
|-------|--------|------|-------------|
| `progress_report` | `ProgressReportTool` (`Orkeon.Cli.Commands.Scripting`) | `AddScriptCommands(...)` (l'hôte des commandes scriptées) | Rapporter la progression d'une opération scriptée longue sur la ligne de statut du CLI |

## Hors catalogue

Délibérément **hors** des 79 classes de tools intégrées :

- `brief_submit` / `blueprint_submit` — internes à la commande `orkeon forge`
  (`ForgeSubmission.cs`, `Orkeon.Scripting.Cli`) ; les agents du moteur les utilisent,
  un crew ne les liste jamais.
- `McpToolAdapter` (`Orkeon.Infrastructure.MCP`) — adapte un tool d'un serveur MCP
  externe en `IBaseTool` ; son nom est celui du tool distant, décidé à l'exécution
  (voir [MCP](../architecture/mcp.md)).
- `JsTool` (tools dynamiques définis en script), `MockTool` (double de test),
  `ObservedTool` (décorateur de télémétrie) — infrastructure qui matche le glob de
  fichiers, exclue par la règle de comptage.

## Synthèse par catégorie

En comptant les **classes de tools concrètes** avec la règle énoncée en tête de page
(le « 75+ outils intégrés » du README plancher ce nombre) :

| Package | Classes de tools |
|---------|--------------|
| `Orkeon.Tools.Data` | 22 |
| `Orkeon.Tools.Analysis` (RaggableTree — voir [son guide](../architecture/raggable-tree.md)) | 15 |
| `Orkeon.Infrastructure` (collaboration, session, sandbox, entrée humaine) | 12 |
| `Orkeon.Tools.Web` | 10 |
| `Orkeon.Tools.EventHub` | 7 |
| `Orkeon.Tools.FileSystem` | 6 |
| `Orkeon.Tools.Rag` | 3 |
| `Orkeon.Tools.Abstractions` / `Orkeon.Tools.Code` / `Orkeon.Tools.Embeddings.Local` / `Orkeon.Cli.Commands.Scripting` | 1 chacun |
| **Total** | **79** |

## Disponibilité par racine de composition

Les deux racines de composition livrées n'enregistrent pas les mêmes suites. Sources :
`RunnerHost.cs` + `RunnerExecution.cs`/`RunCommand.cs` pour le CLI, `Program.cs` pour
le REPL.

| Suite / tool | `orkeon run` (CLI) | `orkeon-repl` (ConsoleApp) |
|---|---|---|
| FileSystem (6), Data (22), Web cœur (5), `shell_command`, `list_mounts`, session (6) | ✅ | ✅ |
| EventHub (7) | ✅ | ❌ |
| Analysis (15) | ✅ (sauf `RaggableTree:Enabled` = `false`) | ✅ |
| `local_embed_text` | ✅ (provider d'embeddings local par défaut) | ✅ |
| RAG (`rag_search`, `rag_ingest`, `rag_eval`) | ❌ | ✅ |
| `web_search`, `cache_search` | ✅ | ❌ |
| `brave_search` | ✅ seulement si `BRAVE_API_KEY` est posée | ❌ |
| `slack_send_message`, `slack_read_messages` | ❌ (opt-in hôte) | ❌ |
| `semantic_search` | ✅ (câblé par la commande run) | ❌ |
| `human_input` | ✅ (la question remonte sur le bus d'événements du run) | ❌ |
| `ask_question_to_coworker`, `delegate_work_to_coworker` | par agent, quand `AllowDelegation` est actif | par agent |
| `spawn_agent` | ❌ (l'hôte doit l'enregistrer) | ❌ |
| `code_interpreter` | enregistrement en type concret seulement — non résoluble par nom | idem |
| `progress_report` | ❌ | ✅ (commandes scriptées) |

## Résolution des tools par nom (YAML → instance)

Quand un crew est défini en YAML, les tools sont référencés par leur nom (la propriété `Name` de la classe du tool). La résolution passe par `IToolRegistry` (`Orkeon.Domain.Tools`).

### Pipeline de résolution

```
Config YAML : tools: ["relational_database_query", "csv_reader"]
       ↓
CrewFactory appelle IToolRegistry.GetToolByNameAsync("relational_database_query")
       ↓
IToolRegistry cherche le tool enregistré avec Name == "relational_database_query"
       ↓
Trouvé → ITool injecté dans l'Agent via AgentBuilder.WithTool()
Absent → CrewFactory lève une erreur de validation
```

### Enregistrement des tools

Les tools sont enregistrés dans `IToolRegistry` au démarrage de l'application via les
extensions DI (chaque table ci-dessus nomme celle qui possède ses tools) :

```csharp
services.AddOrkeonFileSystemTools();   // file_read, file_write, directory_read, directory_search, email_parser, count_pattern
services.AddOrkeonDataTools();         // les 22 tools de données (relationnel + MongoDB + graphe chaînés en interne)
services.AddOrkeonWebTools();          // http_api, web_scrape, scrape_element, github, image_generation
services.AddOrkeonCodeTools();         // shell_command
services.AddOrkeonAbstractionTools();  // list_mounts
services.AddOrkeonSessionTools();      // session_store, session_snip, session_stats, session_cost, token_budget, memory_store
services.AddOrkeonInMemoryEventHub();
services.AddOrkeonEventHubTools();     // les 7 tools du hub d'événements
services.AddRaggableTreeTools();       // les 15 tools d'analyse
services.AddOrkeonLocalEmbeddings();   // local_embed_text
services.AddSemanticSearchTool();      // semantic_search (Orkeon.Hosting)
services.AddOrkeonWebSearchTool();     // web_search
services.AddOrkeonCacheSearchTool();   // cache_search
services.AddOrkeonRag(configuration); services.AddOrkeonRagTools(); // rag_search, rag_ingest, rag_eval (opt-in)
```

`rag_search`/`rag_ingest`/`rag_eval` sont opt-in — voir `docs/reference/opt-in-subsystems.md`.

### Noms à utiliser en YAML

Le nom exact à utiliser dans la section YAML `tools:` est la valeur de la propriété
`Name` de la classe du tool — la colonne `Tool` de chaque table ci-dessus. Table de
correspondance alphabétique complète :

| Nom YAML | Classe | Package |
|----------|--------|---------|
| `arcadedb_query` | `ArcadeDbTool` | `Orkeon.Tools.Data` |
| `ask_question_to_coworker` | `AskQuestionTool` | `Orkeon.Infrastructure` |
| `brave_search` | `BraveSearchTool` | `Orkeon.Tools.Web` |
| `cache_search` | `CacheSearchTool` | `Orkeon.Tools.Web` |
| `code_interpreter` | `SecureCodeInterpreterTool` | `Orkeon.Infrastructure` |
| `codebase_map` | `CodebaseMapTool` | `Orkeon.Tools.Analysis` |
| `codebase_search` | `CodebaseSearchTool` | `Orkeon.Tools.Analysis` |
| `complexity_report` | `ComplexityReportTool` | `Orkeon.Tools.Analysis` |
| `count_pattern` | `CountPatternTool` | `Orkeon.Tools.FileSystem` |
| `csv_reader` | `CsvReaderTool` | `Orkeon.Tools.Data` |
| `database_schema` | `DatabaseSchemaTool` | `Orkeon.Tools.Data` |
| `delegate_work_to_coworker` | `DelegateWorkTool` | `Orkeon.Infrastructure` |
| `dependency_graph` | `DependencyGraphTool` | `Orkeon.Tools.Analysis` |
| `directory_read` | `DirectoryReadTool` | `Orkeon.Tools.FileSystem` |
| `directory_search` | `DirectorySearchTool` | `Orkeon.Tools.FileSystem` |
| `docx_reader` | `DocxReadTool` | `Orkeon.Tools.Data` |
| `docx_writer` | `DocxWriteTool` | `Orkeon.Tools.Data` |
| `email_parser` | `EmailParserTool` | `Orkeon.Tools.FileSystem` |
| `file_read` | `FileReadTool` | `Orkeon.Tools.FileSystem` |
| `file_write` | `FileWriteTool` | `Orkeon.Tools.FileSystem` |
| `flow_trace` | `FlowTraceTool` | `Orkeon.Tools.Analysis` |
| `get_last_value` | `GetLastValueTool` | `Orkeon.Tools.EventHub` |
| `github` | `GitHubTool` | `Orkeon.Tools.Web` |
| `graph_schema` | `GraphSchemaTool` | `Orkeon.Tools.Data` |
| `http_api` | `HttpApiTool` | `Orkeon.Tools.Web` |
| `human_input` | `HumanInputTool` | `Orkeon.Infrastructure` |
| `image_generation` | `ImageGenerationTool` | `Orkeon.Tools.Web` |
| `impact_analysis` | `ImpactAnalysisTool` | `Orkeon.Tools.Analysis` |
| `incremental_reindex` | `IncrementalReindexTool` | `Orkeon.Tools.Analysis` |
| `index_codebase` | `IndexCodebaseTool` | `Orkeon.Tools.Analysis` |
| `index_status` | `IndexStatusTool` | `Orkeon.Tools.Analysis` |
| `is_path_indexed` | `IsPathIndexedTool` | `Orkeon.Tools.Analysis` |
| `janusgraph_query` | `JanusGraphTool` | `Orkeon.Tools.Data` |
| `json_tool` | `JsonTool` | `Orkeon.Tools.Data` |
| `list_mounts` | `ListMountsTool` | `Orkeon.Tools.Abstractions` |
| `local_embed_text` | `LocalEmbedTool` | `Orkeon.Tools.Embeddings.Local` |
| `mariadb_query` | `MariaDbDatabaseTool` | `Orkeon.Tools.Data` |
| `mdx_search` | `MdxSearchTool` | `Orkeon.Tools.Data` |
| `memory_store` | `MemoryStoreTool` | `Orkeon.Infrastructure` |
| `mongodb_query` | `MongoDbTool` | `Orkeon.Tools.Data` |
| `mongodb_schema` | `MongoDbSchemaTool` | `Orkeon.Tools.Data` |
| `mysql_query` | `MySqlDatabaseTool` | `Orkeon.Tools.Data` |
| `package_summary` | `PackageSummaryTool` | `Orkeon.Tools.Analysis` |
| `pdf_reader` | `PdfReaderTool` | `Orkeon.Tools.Data` |
| `pdf_search` | `PdfSearchTool` | `Orkeon.Tools.Data` |
| `post_message` | `PostMessageTool` | `Orkeon.Tools.EventHub` |
| `postgres_query` | `PostgresDatabaseTool` | `Orkeon.Tools.Data` |
| `progress_report` | `ProgressReportTool` | `Orkeon.Cli.Commands.Scripting` |
| `publish_event` | `PublishEventTool` | `Orkeon.Tools.EventHub` |
| `rag_eval` | `RagEvalTool` | `Orkeon.Tools.Rag` (opt-in) |
| `rag_ingest` | `RagIngestTool` | `Orkeon.Tools.Rag` (opt-in) |
| `rag_search` | `RagSearchTool` | `Orkeon.Tools.Rag` (opt-in) |
| `receive_message` | `ReceiveMessageTool` | `Orkeon.Tools.EventHub` |
| `relational_database_query` | `RelationalDatabaseTool` | `Orkeon.Tools.Data` |
| `reply_to` | `ReplyToTool` | `Orkeon.Tools.EventHub` |
| `scrape_element` | `ScrapeElementTool` | `Orkeon.Tools.Web` |
| `semantic_search` | `SearchTool` | `Orkeon.Hosting` (opt-in) |
| `send_request` | `SendRequestTool` | `Orkeon.Tools.EventHub` |
| `session_cost` | `SessionCostTool` | `Orkeon.Infrastructure` |
| `session_snip` | `SessionSnipTool` | `Orkeon.Infrastructure` |
| `session_stats` | `SessionStatsTool` | `Orkeon.Infrastructure` |
| `session_store` | `SessionStoreTool` | `Orkeon.Infrastructure` |
| `shell_command` | `ShellCommandTool` | `Orkeon.Tools.Code` |
| `slack_read_messages` | `SlackReadTool` | `Orkeon.Tools.Web` (opt-in) |
| `slack_send_message` | `SlackTool` | `Orkeon.Tools.Web` (opt-in) |
| `spawn_agent` | `SpawnAgentTool` | `Orkeon.Infrastructure` (enregistré par l'hôte) |
| `sqlserver_query` | `SqlServerDatabaseTool` | `Orkeon.Tools.Data` |
| `statement_query` | `StatementQueryTool` | `Orkeon.Tools.Analysis` |
| `sub_graph` | `SubGraphTool` | `Orkeon.Tools.Analysis` |
| `symbol_detail` | `SymbolDetailTool` | `Orkeon.Tools.Analysis` |
| `symbol_source` | `SymbolSourceTool` | `Orkeon.Tools.Analysis` |
| `token_budget` | `TokenBudgetTool` | `Orkeon.Infrastructure` |
| `txt_search` | `TxtSearchTool` | `Orkeon.Tools.Data` |
| `wait_for_event` | `WaitForEventTool` | `Orkeon.Tools.EventHub` |
| `web_scrape` | `WebScrapeTool` | `Orkeon.Tools.Web` |
| `web_search` | `WebSearchTool` | `Orkeon.Tools.Web` |
| `xlsx_reader` | `XlsxReadTool` | `Orkeon.Tools.Data` |
| `xlsx_writer` | `XlsxWriteTool` | `Orkeon.Tools.Data` |
| `xml_parser` | `XmlParserTool` | `Orkeon.Tools.Data` |

### Enregistrer un tool custom dans le registre

Pour qu'un tool custom soit utilisable en YAML, il doit être enregistré dans `IToolRegistry` :

```csharp
// Option 1 — via DI
services.AddSingleton<IBaseTool, MonCustomTool>();

// Option 2 — enregistrement explicite au runtime
var registry = host.Services.GetRequiredService<IToolRegistry>();
await registry.RegisterToolAsync(new MonCustomTool());
```

Le tool sera alors accessible en YAML via son `Name` :

```yaml
agents:
  my_agent:
    tools:
      - "mon_custom_tool"  # Correspond à la propriété Name du tool
```

## Manques fonctionnels identifiés

Les catégories suivantes ne sont pas couvertes par les tools existants :

- **Écriture de fichiers structurés** : `docx_writer` et `xlsx_writer` couvrent Word et Excel, mais il n'y a pas d'écrivain structuré CSV ou PDF. `FileWriteTool` n'écrit que du texte brut (un CSV peut bien sûr s'écrire comme du texte).
- **Transformation de données** : pas de tool ETL pour convertir entre formats (CSV → JSON, XML → CSV, etc.).
- **Notifications et alertes** : pas de tool pour envoyer des emails (l'email n'est couvert qu'en lecture/parsing).
- **Contrôle de version** : pas de tool Git natif pour commit, branche, diff (`shell_command` autorise par défaut les sous-commandes git en lecture seule).
- **Calendrier / Planification** : pas de tool pour interagir avec des calendriers (Google Calendar, Outlook, etc.).
- **Stockage cloud** : pas de tool pour interagir avec S3, Azure Blob, GCS.
- **Authentification OAuth** : pas de tool générique pour les flux OAuth exigés par des APIs tierces.
- **Traitement d'images** : `MultiModalProcessor` existe dans l'infrastructure mais n'est pas exposé comme tool.
