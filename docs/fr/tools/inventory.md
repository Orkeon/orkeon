> 🇬🇧 [English version](../../tools/inventory.md)

> **Voir aussi** : [Créer un nouveau tool](./new-tool-pattern.md) · [Retour à l'index](../INDEX.md)

# Inventaire des tools Orkeon

Cette page est le catalogue de référence des **91 classes de tools intégrées**. La règle
de comptage est celle qu'applique `scripts/check-doc-claims.py` : chaque fichier
`*Tool.cs` sous `src/`, sauf l'interface (`IBaseTool.cs`) et l'infrastructure non-tool
qui matche le glob (`MockTool.cs`, `JsTool.cs`, `ObservedTool.cs`, `AIAgentTool.cs`). Le même script
vérifie que **chaque nom de tool ci-dessous existe dans le code et que chaque tool du
code est nommé ici** — cette page ne peut plus dériver silencieusement de l'implémentation.

La colonne `Tool` est le nom exact que les agents et les listes YAML `tools:` utilisent
(le `UniqueName` du contrat). Les tools réellement disponibles à l'exécution dépendent de
la racine de composition — voir [Disponibilité par racine de composition](#disponibilité-par-racine-de-composition).

## Collaboration et humain dans la boucle (`Orkeon.Infrastructure`)

| Tool | Classe | Enregistrement | Cas d'usage | Exemple d'appel |
|-------|--------|------|-------------|-----------------|
| `ask_question_to_coworker` | `AskQuestionTool` | Construit par agent par `AgentDelegationToolsProvider` quand `AllowDelegation` est actif — jamais en DI | Poser une question à un agent coéquipier spécialisé | `{ "question": "What is the Q4 revenue?", "coworker_role": "Financial Analyst", "context": "Fiscal year 2025" }` |
| `delegate_work_to_coworker` | `DelegateWorkTool` | Même construction par agent (avec `AgentExecutionBudget` optionnel pour borner la profondeur récursive) | Déléguer une tâche complète à un agent spécialisé | `{ "task": "Analyze competitor pricing", "coworker_role": "Market Researcher", "context": "Focus on SaaS B2B" }` |
| `spawn_agent` | `SpawnAgentTool` | **Câblé par aucune racine de composition livrée** — l'hôte l'enregistre explicitement (`IAgentFactory` requis) | Créer et exécuter un sous-agent spécialisé à l'exécution (mode autonome) | `{ "role": "Fact checker", "goal": "Verify the claims", "task": "..." }` |
| `human_input` | `HumanInputTool` | `AddOrkeonHumanInput()` — câblé par `orkeon run` pour un crew YAML (`RunnerExecution`) ; sous `--events` la question remonte sur le flux d'événements du run, sinon c'est le `AutoApproveHumanInputProvider` par défaut qui répond | Poser une question à l'humain (`input_type` : `text`, `approval`, ou `choice` avec `choices`) ; le runtime se suspend jusqu'à la réponse | `{ "prompt": "Deploy to production?", "input_type": "approval" }` |

Les noms d'affichage des trois premiers contrats sont en prose (« Ask question to
coworker », « Delegate work to coworker », « Spawn sub-agent ») ; le nom du registre —
celui ci-dessus — est ce que le YAML référence.

## Tools de recherche et de connaissance (`Orkeon.Infrastructure` / `Orkeon.Tools.Rag`)

| Tool | Classe | Enregistrement | Cas d'usage | Exemple d'appel |
|-------|--------|------|-------------|-----------------|
| `semantic_search` | `SearchTool` | `AddSemanticSearchTool()` (`Orkeon.Hosting`, opt-in — `orkeon run` l'appelle pour un crew YAML) | Recherche sémantique par embeddings dans les mémoires | `{ "query": "customer churn patterns", "top_k": 5 }` |
| `rag_search` | `RagSearchTool` | `AddOrkeonRag(config)` + `AddOrkeonRagTools()` (`Orkeon.Tools.Rag`) — tout hôte runner appelle les deux | Recherche RAG dans les bases de connaissances de l'agent via `IRagPipeline` | `{ "question": "What is our return policy?", "top_k": 3 }` |
| `rag_ingest` | `RagIngestTool` | Même opt-in que `rag_search` | Ingestion incrémentale dans une collection RAG (pilotée par manifeste — les sources inchangées coûtent 0 embedding) | `{ "collection": "docs", "sources": ["/kb/**/*.md"], "reindex": false }` |
| `rag_eval` | `RagEvalTool` | Même opt-in que `rag_search` | Évaluer une collection contre un dataset doré YAML : recall@k, precision@k, MRR, groundedness | `{ "collection": "docs", "dataset": "/kb/eval/golden.yaml" }` |

> **Enregistrés par tout hôte runner.** `orkeon run` (une crew YAML, un répertoire de crew,
> un script `.ork.ts`) et `orkeon-host` enregistrent le sous-système RAG et ses trois tools :
> un agent de crew peut donc les lister dans `tools:` ; `orkeon-repl` aussi, et un hôte C#
> qui appelle `AddOrkeonRag(configuration)` + `AddOrkeonRagTools()`. La façade de scripting
> (`tools.ragSearch`, l'espace de noms `rag.*`) et `orkeon rag` atteignent les mêmes
> pipelines. `rag_search` et `rag_eval` se replient sur `Orkeon:Rag:Collection` quand
> l'appel ne nomme aucune collection. Voir
> [Pipeline RAG](../architecture/rag-pipeline.md#surfaces-scripting-et-cli).

## Tools d'exécution de code (`Orkeon.Infrastructure.Sandbox` / `Orkeon.Tools.Code`)

| Tool | Classe | Enregistrement | Cas d'usage | Exemple d'appel |
|-------|--------|------|-------------|-----------------|
| `code_interpreter` | `SecureCodeInterpreterTool` | `AddOrkeonCodeSandbox()` — **type concret seulement**, pas sous `IBaseTool` : le registre ne peut pas le résoudre par nom ; l'hôte l'injecte ou l'enregistre explicitement | Exécuter du code C# dans un sandbox isolé avec analyse de sécurité statique | `{ "code": "return 2 + 2;", "timeout_seconds": 10 }` |
| `shell_command` | `ShellCommandTool` | `AddOrkeonCodeTools()` — allowlist en lecture seule par défaut (git limité aux sous-commandes en lecture seule) ; `Orkeon:Tools:Shell:ExtraAllowedCommands` ajoute des commandes, `Orkeon:Tools:Shell:AllowedCommands` remplace la liste, `Orkeon:Tools:Shell:AllowInterpreters` = `true` ouvre les interpréteurs et git en écriture | Exécuter des commandes shell avec allowlist/blocklist et timeout ; les arguments en chemins virtuels montés sont résolus pour le processus et les chemins physiques de la sortie reviennent virtualisés | `{ "command": "cat /workspace/README.md", "timeout_seconds": 30 }` |

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
| `file_read` | `FileReadTool` | `FileToolBase<FileReadRequest, FileReadResponse>` | Lire le contenu d'un fichier (texte, JSON, XML et assimilés — un message `.eml` passe par `email_parser`) | `{ "path": "/data/report.txt" }` |
| `file_write` | `FileWriteTool` | `FileToolBase<FileWriteRequest, FileWriteResponse>` | Écrire du contenu dans un fichier, en créant les dossiers si nécessaire | `{ "path": "/output/result.txt", "content": "Analysis complete.", "append": false }` |
| `directory_read` | `DirectoryReadTool` | `FileToolBase<DirectoryReadRequest, DirectoryReadResponse>` | Lister le contenu d'un répertoire avec filtrage glob et récursion | `{ "path": "/data", "pattern": "*.csv", "recursive": true }` |
| `directory_search` | `DirectorySearchTool` | `ToolBase<DirectorySearchRequest, DirectorySearchResponse>` | Recherche sémantique RAG dans les fichiers d'un répertoire (collection éphémère — exige le sous-système RAG à l'appel, voir ci-dessous) | `{ "path": "/docs", "query": "deployment instructions", "file_patterns": "*.md;*.txt" }` |
| `count_pattern` | `CountPatternTool` | `FileToolBase<CountPatternRequest, CountPatternResponse>` | Compte déterministe des occurrences d'un motif regex dans un fichier (aucune estimation LLM) | `{ "path": "/data/log.txt", "patterns": ["ERROR", "WARN"] }` |

`directory_search` — comme `txt_search`, `mdx_search` et `pdf_search` plus bas — cherche au travers
des collections éphémères du sous-système RAG : l'extension l'enregistre et le construit sans lui,
mais une recherche échoue à l'appel avec un message explicite tant que l'hôte n'a pas appelé
`AddOrkeonRag(configuration)` et fourni un provider d'embeddings.

## Tools de données (`Orkeon.Tools.Data`) — `AddOrkeonDataTools()`

`AddOrkeonDataTools()` enregistre les 22 (il chaîne `AddRelationalDatabaseTools()`,
`AddMongoDbTools()` et `AddGraphDatabaseTools()` en interne).

| Tool | Classe | Cas d'usage | Exemple d'appel |
|-------|--------|-------------|-----------------|
| `csv_reader` | `CsvReaderTool` | Lire et structurer des fichiers CSV avec détection du délimiteur | `{ "path": "/data/sales.csv", "delimiter": "," }` |
| `pdf_reader` | `PdfReaderTool` | Extraire le texte de fichiers PDF avec sélection de pages | `{ "path": "/docs/contract.pdf", "page_range": "1-5" }` |
| `json_tool` | `JsonTool` | Manipuler du JSON : parse (analyse de structure), query (navigation dot-notation), format (pretty-print) | `{ "operation": "query", "input": "{...}", "query": "data.users[0].name" }` |
| `xml_parser` | `XmlParserTool` | Parser du XML depuis un chemin de fichier ou une chaîne, avec requêtes XPath | `{ "input": "/data/feed.xml", "operation": "query", "xpath": "//item/title" }` |
| `docx_reader` | `DocxReadTool` | Lire des fichiers Word avec extraction du texte, des tableaux et des métadonnées | `{ "file_path": "/docs/report.docx" }` |
| `docx_writer` | `DocxWriteTool` | Créer des fichiers Word (.docx) avec titre, paragraphes et listes à puces | `{ "file_path": "/output/report.docx", "title": "Monthly Report", "paragraphs": ["Introduction text..."] }` |
| `xlsx_reader` | `XlsxReadTool` | Lire des fichiers Excel (.xlsx) : feuilles, lignes, colonnes, métadonnées (ClosedXML) | `{ "file_path": "/data/budget.xlsx", "sheet_name": "Q3" }` |
| `xlsx_writer` | `XlsxWriteTool` | Créer ou mettre à jour des fichiers Excel (.xlsx) avec plusieurs feuilles, en-têtes et lignes | `{ "file_path": "/output/summary.xlsx", "sheets": [{ "name": "Totals", "headers": ["Region", "Revenue"], "rows": [["EMEA", "1.2M"]] }] }` |
| `relational_database_query` | `RelationalDatabaseTool` | Exécuter des requêtes SQL (SQL Server, PostgreSQL, MySQL, MariaDB, SQLite) ; `provider_name` est le provider ADO.NET (`Microsoft.Data.SqlClient`, `Npgsql`, `MySqlConnector`, `Microsoft.Data.Sqlite`) | `{ "connection_string": "...", "provider_name": "Npgsql", "query": "SELECT * FROM orders WHERE status = @status", "parameters": { "status": "pending" }, "query_type": "select" }` |
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
| `pdf_search` | `PdfSearchTool` | Recherche sémantique dans des fichiers PDF via embeddings (sous-système RAG requis à l'appel) | — |
| `txt_search` | `TxtSearchTool` | Recherche sémantique dans des fichiers texte (sous-système RAG requis à l'appel) | — |
| `mdx_search` | `MdxSearchTool` | Recherche sémantique dans des fichiers MDX/Markdown (frontmatter et JSX retirés ; sous-système RAG requis à l'appel) | — |

## Tools web (`Orkeon.Tools.Web`)

`AddOrkeonWebTools()` enregistre les cinq premiers ; chacun des autres a sa propre
extension opt-in parce qu'il exige une clé ou un service support supplémentaire. Les clés
ne voyagent pas toutes de la même façon : les clés Tavily et OpenAI sont des secrets résolus à
l'appel, les jetons Brave et Slack sont remis à leur extension par l'hôte. Aucun tool ne prend
de secret en argument d'appel — ce qu'un argument porte traverse la conversation, le journal
des appels d'outils et la trace d'usage. Tout tool qui récupère une URL passe par la garde SSRF
(fail-closed même sans `IUrlValidator` enregistré) et par un client nommé qui refuse les
redirections.

| Tool | Classe | Enregistrement | Cas d'usage |
|-------|--------|------|-------------|
| `http_api` | `HttpApiTool` | `AddOrkeonWebTools()` | Appels HTTP REST (GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS) avec protection SSRF |
| `web_scrape` | `WebScrapeTool` | `AddOrkeonWebTools()` | Scraper une page web avec filtrage CSS optionnel ; `cached=true` découpe et embarque la page dans le cache RAG |
| `scrape_element` | `ScrapeElementTool` | `AddOrkeonWebTools()` | Scraping ciblé d'éléments DOM via sélecteurs CSS (texte, HTML, attributs) |
| `github` | `GitHubTool` | `AddOrkeonWebTools()` — enregistré **sans jeton**, ses appels sont donc anonymes ; un hôte qui a besoin de `create_issue` enregistre lui-même `new GitHubTool(personalAccessToken)` | API GitHub v3 (`action` : `list_issues`, `create_issue`, `get_pr`, `search_repos`, `get_repo`) |
| `image_generation` | `ImageGenerationTool` | `AddOrkeonWebTools()` (exige un `IFileSystemService` et un `ISecretProvider`) — le secret `OPENAI_API_KEY` est résolu à l'appel (par défaut la variable d'environnement `ORKEON_OPENAI_API_KEY`, puis `Secrets:OPENAI_API_KEY`) ; sans lui un appel échoue en nommant le secret | Génération d'images via l'API OpenAI DALL-E, enregistrables sous un chemin virtuel (`save_to_path`) |
| `web_search` | `WebSearchTool` | `AddOrkeonWebSearchTool()` — le secret `TAVILY_API_KEY` est résolu à l'appel via `ISecretProvider` (par défaut la variable d'environnement `ORKEON_TAVILY_API_KEY`, puis `Secrets:TAVILY_API_KEY` en configuration) | Recherche web via l'API Tavily Search |
| `brave_search` | `BraveSearchTool` | `AddOrkeonBraveSearchTool(apiKey)` — `orkeon run` ne le câble que si `BRAVE_API_KEY` est posée | Recherche web via l'API Brave Search |
| `slack_send_message` | `SlackTool` | `AddOrkeonSlackTool(botToken)` — aucune racine livrée ne l'appelle (opt-in hôte pur) | Envoyer des messages vers des canaux/utilisateurs Slack via la Web API |
| `slack_read_messages` | `SlackReadTool` | `AddOrkeonSlackReadTool(botToken)` — même opt-in hôte | Lire des messages Slack (lecture seule) |
| `cache_search` | `CacheSearchTool` | `AddOrkeonCacheSearchTool()` (exige `IEmbeddingService` et `IMemoryProvider`) | Recherche sémantique dans le cache RAG que d'autres tools alimentent (ex. `web_scrape` avec `cached=true`) |

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

## Tools e-mail (`Orkeon.Tools.Email`) — `AddOrkeonEmailTools(configuration)`

Treize tools sur les comptes qu'un opérateur déclare sous `Orkeon:Tools:Email` : IMAP, POP3 et
SMTP via MailKit, Outlook.com et Microsoft 365 via Microsoft Graph. Tant qu'aucun compte n'est
déclaré, les tools de boîte aux lettres refusent tout appel, `email_accounts` n'en liste aucun et
`email_parser`, qui lit un fichier, fonctionne. Un agent nomme un compte (`account`, ou celui par défaut) — jamais
un serveur ni un identifiant — et les `Rights` du compte décident de ce qu'il peut faire ;
`email_send` n'atteint que les destinataires qu'autorise la liste `Send:AllowedRecipients` du
compte. Les ids de message sont opaques : repassez-les tels que `email_search` les a rendus. La
colonne **Accès** est la classe que voit la [permission gate](../reference/opt-in-subsystems.md).
Nouveau venu dans la famille ? Le [tutoriel boîte aux lettres](../getting-started/give-your-agents-a-mailbox.md) met un compte en service de bout en bout.
Chaque paramètre est listé dans le [guide e-mail](../guides/email.md#paramètres) ; les résultats
sont dimensionnés à ce que garde la boucle d'agent, et `cursor` / `offset` reprennent exactement
là où une page ou une tranche s'arrête. Mise en place pour Gmail, Hotmail/Outlook.com et votre propre serveur, modèle de sécurité et
commandes `orkeon email` : [Outils e-mail](../guides/email.md).

| Tool | Classe | Droit requis | Accès | Cas d'usage | Exemple d'appel |
|-------|--------|------|------|-------------|-----------------|
| `email_accounts` | `EmailAccountsTool` | aucun | Read | Lister les comptes configurés : le nom à passer en `account`, leurs droits, si chacun est prêt | `{}` |
| `email_folders` | `EmailFoldersTool` | Read | Read | Lister les dossiers avec leur rôle (inbox, sent, drafts, trash, junk, archive) et leurs compteurs | `{ "account": "work" }` |
| `email_search` | `EmailSearchTool` | Read | Read | Chercher dans un dossier, du plus récent au plus ancien : non lus, suivis, expéditeur, destinataire, objet, texte, dates, pièces jointes, ou une `raw_query` native (syntaxe de recherche Gmail, KQL Outlook) ; 10 par page par défaut, 50 au plus, `cursor` pour la page suivante | `{ "folder": "inbox", "unread_only": true, "since": "2026-09-01" }` |
| `email_read` | `EmailReadTool` | Read (+ Organize avec `mark_read`) | Read | Lire un message : l'avis de contenu non fiable et le verdict de filtrage d'abord, puis les en-têtes, les pièces jointes et le corps par tranches (`offset`, `max_chars` 200–3000, 2500 par défaut, puis `next_offset`) | `{ "id": "<id rendu par email_search>" }` |
| `email_save_attachment` | `EmailSaveAttachmentTool` | Read, plus un montage accessible en écriture | Edit | Enregistrer une pièce jointe (`index`) ou toutes dans un répertoire virtuel ; noms assainis, rien n'est écrasé | `{ "id": "…", "directory": "/output/attachments" }` |
| `email_create_folder` | `EmailCreateFolderTool` | Organize | Edit | Créer un dossier (un libellé sur Gmail) ; les parents manquants sont créés | `{ "path": "Clients/ACME" }` |
| `email_rename_folder` | `EmailRenameFolderTool` | Organize | Edit | Renommer le dernier segment d'un dossier ; les dossiers système sont refusés | `{ "path": "Clients/ACME", "new_name": "ACME Corp" }` |
| `email_move` | `EmailMoveTool` | Organize | Edit | Déplacer des messages vers un chemin de dossier ou un rôle ; rend le nouvel id de chaque message quand le serveur le donne | `{ "ids": ["…"], "destination": "archive" }` |
| `email_mark` | `EmailMarkTool` | Organize | Edit | Marquer lu ou non lu (`seen`), suivre ou non (`flagged`, une étoile sur Gmail) | `{ "ids": ["…"], "seen": true }` |
| `email_delete` | `EmailDeleteTool` | Delete ; Purge avec `permanent: true` | Execute | Mettre des messages à la corbeille, ou les supprimer définitivement | `{ "ids": ["…"] }` |
| `email_draft` | `EmailDraftTool` | Draft (+ Read pour répondre ou transférer) | Edit | Enregistrer un nouveau message, une réponse (`reply_to_id`, `reply_all`) ou un transfert (`forward_id`) dans les brouillons sans l'envoyer — la voie de la relecture humaine, sans liste d'autorisation | `{ "to": ["client@example.com"], "subject": "Devis", "text": "…" }` |
| `email_send` | `EmailSendTool` | Send (+ Read pour répondre ou transférer) | Execute | Envoyer un nouveau message, une réponse ou un transfert aux destinataires qu'autorise `Send:AllowedRecipients` (une liste vide n'autorise personne) ; `From` est toujours le compte | `{ "reply_to_id": "…", "text": "Bien reçu, merci." }` |
| `email_parser` | `EmailParserTool` | aucun (pas de compte) | Read | Parser un fichier `.eml` depuis un chemin virtuel ; même sortie que `email_read` | `{ "path": "/workspace/mail/invoice.eml" }` |

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

Délibérément **hors** des 91 classes de tools intégrées :

- `brief_submit` / `blueprint_submit` — internes à la commande `orkeon forge`
  (`ForgeSubmission.cs`, `Orkeon.Scripting.Cli`) ; les agents du moteur les utilisent,
  un crew ne les liste jamais.
- `McpToolAdapter` (`Orkeon.Infrastructure.MCP`) — adapte un tool d'un serveur MCP
  externe en `IBaseTool` ; son nom est celui du tool distant, décidé à l'exécution
  (voir [MCP](../architecture/mcp.md)).
- `JsTool` (tools dynamiques définis en script), `MockTool` (double de test),
  `ObservedTool` (décorateur de télémétrie) et `AIAgentTool`
  (`Orkeon.Interop.AgentFramework` — enveloppe un agent Microsoft Agent Framework que vous
  fournissez) — infrastructure qui matche le glob de fichiers, exclue par la règle de
  comptage.

## Synthèse par catégorie

En comptant les **classes de tools concrètes** avec la règle énoncée en tête de page —
le nombre qu'affichent le README et l'index de la documentation, vérifié par
`scripts/check-doc-claims.py` :

| Package | Classes de tools |
|---------|--------------|
| `Orkeon.Tools.Data` | 22 |
| `Orkeon.Tools.Analysis` (RaggableTree — voir [son guide](../architecture/raggable-tree.md)) | 15 |
| `Orkeon.Tools.Email` (voir [son guide](../guides/email.md)) | 13 |
| `Orkeon.Infrastructure` (collaboration, session, sandbox, entrée humaine, `semantic_search`) | 12 |
| `Orkeon.Tools.Web` | 10 |
| `Orkeon.Tools.EventHub` | 7 |
| `Orkeon.Tools.FileSystem` | 5 |
| `Orkeon.Tools.Rag` | 3 |
| `Orkeon.Tools.Abstractions` / `Orkeon.Tools.Code` / `Orkeon.Tools.Embeddings.Local` / `Orkeon.Cli.Commands.Scripting` | 1 chacun |
| **Total** | **91** |

## Disponibilité par racine de composition

Les deux racines de composition livrées n'enregistrent pas les mêmes suites. Sources :
`RunnerHost.cs` + `RunnerExecution.cs`/`RunCommand.cs` pour le CLI, `Program.cs` pour
le REPL. `orkeon run` construit un hôte différent pour un crew YAML (via `RunnerExecution`)
et pour un script `.ork.ts` (directement sur `RunnerHost`) ; là où les deux diffèrent, la
table le dit. L'hôte de service `orkeon-host` s'appuie sur le même `RunnerHost` : il a donc
la suite de `RunnerHost` — tout ce que liste la première colonne sauf `semantic_search`,
`human_input` et les tools RAG réservés aux scripts.

| Suite / tool | `orkeon run` (CLI) | `orkeon-repl` (ConsoleApp) |
|---|---|---|
| FileSystem (5), Data (22), Web cœur (5), `shell_command`, `list_mounts`, session (6) | ✅ | ✅ |
| E-mail (13) — les tools de boîte aux lettres refusent tout appel tant qu'aucun compte n'est déclaré sous `Orkeon:Tools:Email` | ✅ (jetons OAuth sous la racine interne `/credentials`) | ✅ comptes à mot de passe seulement — le REPL ne tient aucun magasin de jetons, un compte OAuth y est refusé |
| EventHub (7) | ✅ | ❌ |
| Analysis (15) | ✅ (sauf `RaggableTree:Enabled` = `false`) | ✅ |
| `local_embed_text` | ✅ tant que RaggableTree est actif avec son provider d'embeddings local par défaut | ✅ |
| RAG (`rag_search`, `rag_ingest`, `rag_eval`) | ❌ pour un crew YAML (ni enregistrés ni attachables) ; ✅ pour un script `.ork.ts` (`tools.ragSearch`, `rag.*`, avec le reranker ONNX) | ✅ pour les scripts (un agent YAML ne peut toujours pas les recevoir) |
| `web_search`, `cache_search` | ✅ | ❌ |
| `brave_search` | ✅ seulement si `BRAVE_API_KEY` est posée | ❌ |
| `slack_send_message`, `slack_read_messages` | ❌ (opt-in hôte) | ❌ |
| Les outils des serveurs MCP déclarés par `MCP:Servers` | ✅ connectés avant le chargement de la crew, sous leur propre nom (STUDIO-21) — joignables par nom dans le registre, mais pas encore attachables depuis une liste YAML `tools:` (voir le pipeline de résolution plus bas) | ❌ |
| `semantic_search` | ✅ pour un crew YAML (câblé par la commande run) | ❌ |
| `human_input` | ✅ pour un crew YAML (sur le flux d'événements sous `--events`, approuvé automatiquement sinon) | ❌ |
| `ask_question_to_coworker`, `delegate_work_to_coworker` | par agent, quand `AllowDelegation` est actif | par agent |
| `spawn_agent` | ❌ (l'hôte doit l'enregistrer) | ❌ |
| `code_interpreter` | enregistrement en type concret seulement — non résoluble par nom | idem |
| `progress_report` | ❌ | ✅ (commandes scriptées) |

`orkeon forge` s'appuie sur l'hôte des runners mais retire `shell_command`,
`code_interpreter` et les douze tools de boîte aux lettres du catalogue où puise un crew
forgé : un crew essayé sur le banc de test ne doit pas atteindre la vraie boîte de
l'opérateur (`email_parser`, qui lit un fichier, reste).

## Résolution des tools par nom (YAML → instance)

Quand un crew est défini en YAML, les tools sont référencés par leur nom (la propriété `Name` de la classe du tool). La résolution passe par `IToolRegistry` (`Orkeon.Domain.Tools`) ; `AddOrkeonInfrastructure()` enregistre le `ToolRegistry` par défaut (`Orkeon.Infrastructure.Tools`, dans le paquet `Orkeon`), qui indexe par nom, sans tenir compte de la casse, chaque `IBaseTool` enregistré en DI — les runners et tout hôte qui embarque utilisent le même.

### Pipeline de résolution

```
Config YAML : tools: ["relational_database_query", "csv_reader"]
       ↓
CrewFactory appelle IToolRegistry.GetToolByNameAsync("relational_database_query")
       ↓
IToolRegistry cherche le tool enregistré avec Name == "relational_database_query"
       ↓
Trouvé → attaché à l'Agent (AgentBuilder.WithTools())
Absent →
    StrictTools (défaut des runners)       → le crew ne se charge pas, avec la liste des tools disponibles
    mode tolérant (défaut de la bibliothèque) → un avertissement, et l'agent tourne sans ce tool
```

La sévérité est `Orkeon:CrewFactory:StrictTools` (`true` dans les runners, `CrewFactoryOptions.StrictTools`
à `false` pour un hôte à vous). Tout tool que tient le registre atteint un agent qui le
nomme — un tool `ToolBase`, un tool de script, un tool RAG, le tool d'un serveur MCP
connecté : `IBaseTool` est l'unique contrat.

Un nom appartient à un seul tool. Deux tools enregistrés dans la DI sous le même nom
arrêtent l'hôte au démarrage avec une erreur qui nomme le nom et les deux types ; un tool
enregistré plus tard sous un nom déjà tenu (`IToolRegistry.RegisterToolAsync`) est refusé
— l'appel renvoie `false` et le tool enregistré garde le nom.

### Enregistrement des tools

Les tools sont enregistrés dans `IToolRegistry` au démarrage de l'application via les
extensions DI (chaque table ci-dessus nomme celle qui possède ses tools) :

```csharp
services.AddOrkeonFileSystemTools();   // file_read, file_write, directory_read, directory_search, count_pattern
services.AddOrkeonDataTools();         // les 22 tools de données (relationnel + MongoDB + graphe chaînés en interne)
services.AddOrkeonWebTools();          // http_api, web_scrape, scrape_element, github, image_generation
services.AddOrkeonCodeTools();         // shell_command
services.AddOrkeonAbstractionTools();  // list_mounts
services.AddOrkeonSessionTools();      // session_store, session_snip, session_stats, session_cost, token_budget, memory_store
services.AddOrkeonInMemoryEventHub();
services.AddOrkeonEventHubTools();     // les 7 tools du hub d'événements
services.AddOrkeonEmailTools(configuration); // les 13 tools email_* ; ceux de boîte aux lettres demandent un compte déclaré
services.AddRaggableTreeTools();       // les 15 tools d'analyse
services.AddOrkeonLocalEmbeddings();   // local_embed_text
services.AddSemanticSearchTool();      // semantic_search (l'extension vit dans Orkeon.Hosting)
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
| `email_accounts` | `EmailAccountsTool` | `Orkeon.Tools.Email` |
| `email_create_folder` | `EmailCreateFolderTool` | `Orkeon.Tools.Email` |
| `email_delete` | `EmailDeleteTool` | `Orkeon.Tools.Email` |
| `email_draft` | `EmailDraftTool` | `Orkeon.Tools.Email` |
| `email_folders` | `EmailFoldersTool` | `Orkeon.Tools.Email` |
| `email_mark` | `EmailMarkTool` | `Orkeon.Tools.Email` |
| `email_move` | `EmailMoveTool` | `Orkeon.Tools.Email` |
| `email_parser` | `EmailParserTool` | `Orkeon.Tools.Email` |
| `email_read` | `EmailReadTool` | `Orkeon.Tools.Email` |
| `email_rename_folder` | `EmailRenameFolderTool` | `Orkeon.Tools.Email` |
| `email_save_attachment` | `EmailSaveAttachmentTool` | `Orkeon.Tools.Email` |
| `email_search` | `EmailSearchTool` | `Orkeon.Tools.Email` |
| `email_send` | `EmailSendTool` | `Orkeon.Tools.Email` |
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
| `semantic_search` | `SearchTool` | `Orkeon.Infrastructure` (opt-in, enregistré par `Orkeon.Hosting`) |
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
// Option 1 — via DI (lu par le ToolRegistry par défaut)
services.AddSingleton<IBaseTool, MyCustomTool>();

// Option 2 — enregistrement explicite au runtime
var registry = host.Services.GetRequiredService<IToolRegistry>();
await registry.RegisterToolAsync(new MyCustomTool());
```

Dérivez le tool de `ToolBase` (ou implémentez directement `IBaseTool`) — voir
[Créer un nouveau tool](./new-tool-pattern.md). Le tool sera alors accessible en YAML via
son `Name` :

```yaml
agents:
  my_agent:
    tools:
      - "my_custom_tool"  # Correspond à la propriété Name du tool
```

## Manques fonctionnels identifiés

Les catégories suivantes ne sont pas couvertes par les tools existants :

- **Écriture de fichiers structurés** : `docx_writer` et `xlsx_writer` couvrent Word et Excel, mais il n'y a pas d'écrivain structuré CSV ou PDF. `FileWriteTool` n'écrit que du texte brut (un CSV peut bien sûr s'écrire comme du texte).
- **Transformation de données** : pas de tool ETL pour convertir entre formats (CSV → JSON, XML → CSV, etc.).
- **Notifications et alertes** : l'e-mail est couvert — `email_send`, limité à la liste d'autorisation de chaque compte (voir [Outils e-mail](../guides/email.md)) — et Slack par un opt-in de l'hôte ; il n'y a pas de tool SMS, push ou Teams.
- **Contrôle de version** : pas de tool Git natif pour commit, branche, diff (`shell_command` autorise par défaut les sous-commandes git en lecture seule).
- **Calendrier / Planification** : pas de tool pour interagir avec des calendriers (Google Calendar, Outlook, etc.).
- **Stockage cloud** : pas de tool pour interagir avec S3, Azure Blob, GCS.
- **Authentification OAuth** : pas de tool générique pour les flux OAuth exigés par des APIs tierces — OAuth n'existe que pour les comptes e-mail, connectés une fois avec `orkeon email login`.
- **Traitement d'images** : les images atteignent un modèle par le pipeline multimodal opt-in (`AddOrkeonMultiModal`, `IMultiModalContentLoader`), mais aucun tool ne transforme ni n'analyse une image ; `image_generation` ne fait qu'en créer une.
