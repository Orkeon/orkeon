> 🇬🇧 [English version](../../tools/inventory.md)

> **Voir aussi** : [Créer un nouvel outil](./new-tool-pattern.md) · [Retour à l'index](../INDEX.md)

# Inventaire des outils Orkeon

## Outils de collaboration (`Orkeon.Infrastructure.Tools`)

| Outil | Classe | Base | Cas d'usage | Exemple d'appel |
|-------|--------|------|-------------|-----------------|
| `ask_question` | `AskQuestionTool` | `ToolBase<AskQuestionRequest, AskQuestionResponse>` | Poser une question à un collègue agent spécialisé | `{ "question": "What is the Q4 revenue?", "coworker": "Financial Analyst" }` |
| `delegate_work` | `DelegateWorkTool` | `ToolBase<DelegateWorkRequest, DelegateWorkResponse>` | Déléguer une tâche complète à un agent spécialisé | `{ "task": "Analyze competitor pricing", "coworker": "Market Researcher", "context": "Focus on SaaS B2B" }` |

## Outils de recherche et connaissance (`Orkeon.Infrastructure`)

| Outil | Classe | Base | Cas d'usage | Exemple d'appel |
|-------|--------|------|-------------|-----------------|
| `semantic_search` | `SearchTool` | `ToolBase<SearchRequest, SearchResponse>` | Recherche sémantique par embeddings dans les mémoires | `{ "query": "customer churn patterns", "limit": 5 }` |
| `rag_search` | `RagSearchTool` | `IBaseTool` (direct) | Recherche RAG dans les bases de connaissances de l'agent via `IRagPipeline` (opt-in : `AddOrkeonRag(config)` + `AddOrkeonRagTools()`, projet `Orkeon.Tools.Rag`) | `{ "question": "What is our return policy?", "top_k": 3 }` |
| `rag_ingest` | `RagIngestTool` | `IBaseTool` (direct) | Ingestion incrémentale dans une collection RAG (pilotée par manifeste — sources inchangées = 0 embedding ; même opt-in que `rag_search`) | `{ "collection": "docs", "sources": ["/kb/**/*.md"], "reindex": false }` |

## Outils d'exécution de code (`Orkeon.Infrastructure.Sandbox` / `Orkeon.Tools.Code`)

| Outil | Classe | Base | Cas d'usage | Exemple d'appel |
|-------|--------|------|-------------|-----------------|
| `code_interpreter` | `SecureCodeInterpreterTool` | `ToolBase` (non-generic) | Exécuter du code C# dans un sandbox isolé avec analyse statique de sécurité | `{ "code": "return 2 + 2;", "timeout_seconds": 10 }` |
| `shell_command` | `ShellCommandTool` | `ToolBase<ShellCommandRequest, ShellCommandResponse>` | Exécuter des commandes shell avec allowlist/blocklist et timeout ; les arguments en chemins virtuels (préfixe de mount) sont résolus pour le processus et les chemins physiques de la sortie reviennent virtualisés | `{ "command": "cat /workspace/README.md", "timeout_seconds": 30 }` |

## Outils fichiers (`Orkeon.Tools.FileSystem`)

| Outil | Classe | Base | Cas d'usage | Exemple d'appel |
|-------|--------|------|-------------|-----------------|
| `file_read` | `FileReadTool` | `FileToolBase<FileReadRequest, FileReadResponse>` | Lire le contenu d'un fichier (texte, JSON, XML, .eml, .msg) | `{ "file_path": "/data/report.txt" }` |
| `file_write` | `FileWriteTool` | `FileToolBase<FileWriteRequest, FileWriteResponse>` | Écrire du contenu dans un fichier, créer les dossiers si nécessaire | `{ "file_path": "/output/result.txt", "content": "Analysis complete.", "append": false }` |
| `directory_read` | `DirectoryReadTool` | `FileToolBase<DirectoryReadRequest, DirectoryReadResponse>` | Lister le contenu d'un répertoire avec filtrage glob et récursion | `{ "directory": "/data", "pattern": "*.csv", "recursive": true }` |
| `directory_search` | `DirectorySearchTool` | `ToolBase<DirectorySearchRequest, DirectorySearchResponse>` | Recherche sémantique RAG à travers les fichiers d'un répertoire | `{ "directory": "/docs", "query": "deployment instructions", "file_pattern": "*.md" }` |
| `email_parser` | `EmailParserTool` | `FileToolBase<EmailParserRequest, EmailParserResponse>` | Parser des emails .eml/.msg et extraire headers, corps, pièces jointes | `{ "file_path": "/emails/invoice.eml" }` |

## Outils données (`Orkeon.Tools.Data`)

| Outil | Classe | Base | Cas d'usage | Exemple d'appel |
|-------|--------|------|-------------|-----------------|
| `csv_reader` | `CsvReaderTool` | `FileToolBase<CsvReaderRequest, CsvReaderResponse>` | Lire et structurer des fichiers CSV avec détection de délimiteurs | `{ "file_path": "/data/sales.csv", "delimiter": "," }` |
| `pdf_reader` | `PdfReaderTool` | `FileToolBase<PdfReaderRequest, PdfReaderResponse>` | Extraire le texte de fichiers PDF avec sélection de pages | `{ "file_path": "/docs/contract.pdf", "start_page": 1, "end_page": 5 }` |
| `json_tool` | `JsonTool` | `ToolBase<JsonToolRequest, JsonToolResponse>` | Manipuler du JSON : parse (analyse structure), query (navigation dot-notation), format (pretty-print) | `{ "operation": "query", "json_content": "{...}", "path": "data.users[0].name" }` |
| `docx_reader` | `DocxReadTool` | `FileToolBase<DocxReadRequest, DocxReadResponse>` | Lire des fichiers Word avec extraction texte, tableaux et métadonnées | `{ "file_path": "/docs/report.docx" }` |
| `relational_database_query` | `RelationalDatabaseTool` | `ToolBase<RelationalDatabaseRequest, RelationalDatabaseResponse>` | Exécuter des requêtes SQL (SQL Server, PostgreSQL, MySQL, MariaDB, SQLite) | `{ "connection_string": "...", "query": "SELECT * FROM orders WHERE status = 'pending'", "query_type": "select", "provider": "postgresql" }` |
| `mongodb_query` | `MongoDbTool` | `ToolBase<MongoDbRequest, MongoDbResponse>` | Exécuter des opérations MongoDB (Find, Aggregate, CRUD) | `{ "connection_string": "...", "database": "shop", "collection": "products", "operation": "find", "filter": "{ \"price\": { \"$gt\": 100 } }" }` |
| `docx_writer` | `DocxWriteTool` | `ToolBase<DocxWriteRequest, DocxWriteResponse>` | Créer des fichiers Word (.docx) avec titre, paragraphes et listes à puces | `{ "file_path": "/output/report.docx", "title": "Monthly Report", "paragraphs": ["Introduction text..."] }` |

### Outils données supplémentaires

| Outil | Classe | Base | Cas d'usage |
|-------|--------|------|-------------|
| `arcadedb_query` | `ArcadeDbTool` | `ToolBase<...>` | Requêtes sur base de données graphe ArcadeDB |
| `janusgraph_query` | `JanusGraphTool` | `ToolBase<...>` | Requêtes sur base de données graphe JanusGraph |
| `graph_schema` | `GraphSchemaTool` | `ToolBase<...>` | Inspection de schéma de bases de données graphe |
| `database_schema` | `DatabaseSchemaTool` | `ToolBase<...>` | Inspection de schéma de bases relationnelles |
| `mongodb_schema` | `MongoDbSchemaTool` | `ToolBase<...>` | Inférence de schéma de collections MongoDB |
| `xml_parser` | `XmlParserTool` | `ToolBase<...>` | Parsing et extraction de données XML |
| `sqlserver_query` | `SqlServerTool` | `ToolBase<...>` | Requêtes spécialisées SQL Server |
| `postgres_query` | `PostgresTool` | `ToolBase<...>` | Requêtes spécialisées PostgreSQL |
| `mysql_query` | `MySqlTool` | `ToolBase<...>` | Requêtes spécialisées MySQL |
| `mariadb_query` | `MariaDbTool` | `ToolBase<...>` | Requêtes spécialisées MariaDB |
| `pdf_search` | `PdfSearchTool` | `ToolBase<...>` | Recherche sémantique dans des fichiers PDF via embeddings |
| `txt_search` | `TXTSearchTool` | `ToolBase<...>` | Recherche sémantique dans des fichiers texte |
| `mdx_search` | `MDXSearchTool` | `ToolBase<...>` | Recherche sémantique dans des fichiers MDX/Markdown |

## Outils web (`Orkeon.Tools.Web`)

| Outil | Classe | Base | Cas d'usage | Exemple d'appel |
|-------|--------|------|-------------|-----------------|
| `web_search` | `WebSearchTool` | `HttpToolBase<WebSearchRequest, WebSearchResponse>` | Recherche web via Tavily Search API | `{ "query": "best practices microservices 2025", "max_results": 5 }` |
| `brave_search` | `BraveSearchTool` | `HttpToolBase<WebSearchRequest, WebSearchResponse>` | Recherche web via Brave Search API | `{ "query": "C# performance optimization", "max_results": 3 }` |
| `web_scrape` | `WebScrapeTool` | `HttpToolBase<WebScrapeRequest, WebScrapeResponse>` | Scraper le contenu d'une page web avec filtrage CSS optionnel | `{ "url": "https://example.com/docs", "selector": "article.content" }` |
| `http_api` | `HttpApiTool` | `HttpToolBase<HttpApiRequest, HttpApiResponse>` | Appels HTTP REST (GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS) avec protection SSRF | `{ "url": "https://api.example.com/users", "method": "GET", "headers": { "Authorization": "Bearer ..." } }` |

### Outils web supplémentaires

| Outil | Classe | Base | Cas d'usage |
|-------|--------|------|-------------|
| `github` | `GitHubTool` | `HttpToolBase<...>` | Interaction avec l'API GitHub |
| `slack_send_message` | `SlackTool` | `HttpToolBase<...>` | Envoi de messages via API Slack |
| `slack_read_messages` | `SlackReadTool` | `HttpToolBase<...>` | Lecture de messages Slack (read-only) |
| `image_generation` | `ImageGenerationTool` | `HttpToolBase<...>` | Génération d'images via API externe |
| `scrape_element` | `ScrapeElementTool` | `HttpToolBase<...>` | Scraping ciblé d'éléments DOM |

## Outils recherche web (`Orkeon.Infrastructure.Tools.Search`)

| Outil | Classe | Base | Cas d'usage |
|-------|--------|------|-------------|
| `bing_search` | `BingSearchTool` | via `SearchTool` | Recherche web via Bing API |
| `google_search` | `GoogleSearchTool` | via `SearchTool` | Recherche web via Google Custom Search API |

## Récapitulatif par catégorie

| Catégorie | Nombre | Package |
|-----------|--------|---------|
| Collaboration | 2 | `Orkeon.Infrastructure` |
| Recherche / RAG | 2 | `Orkeon.Infrastructure` + `Orkeon.Tools.Rag` |
| Code | 2 | `Orkeon.Infrastructure` + `Orkeon.Tools.Code` |
| Fichiers | 5 | `Orkeon.Tools.FileSystem` |
| Données | 17+ | `Orkeon.Tools.Data` |
| Web | 8+ | `Orkeon.Tools.Web` |
| **Total** | **36+** | |

## Résolution des outils par nom (YAML → instance)

Quand une crew est définie en YAML, les outils sont référencés par leur nom (propriété `Name` de la classe outil). La résolution se fait via `IToolRegistry` (`Orkeon.Domain.Tools`).

### Pipeline de résolution

```
YAML config: tools: ["relational_database_query", "csv_reader"]
       ↓
CrewFactory appelle IToolRegistry.GetToolByNameAsync("relational_database_query")
       ↓
IToolRegistry cherche l'outil enregistré avec Name == "relational_database_query"
       ↓
Si trouvé → ITool injecté dans l'Agent via AgentBuilder.WithTool()
Si absent → CrewFactory lève une erreur de validation
```

### Enregistrement des outils

Les outils sont enregistrés dans `IToolRegistry` au démarrage de l'application via les extensions DI :

```csharp
// Chaque suite enregistre ses outils dans IToolRegistry
services.AddOrkeonFileSystemTools();   // file_read, file_write, directory_read, directory_search, email_parser
services.AddOrkeonDataTools();         // csv_reader, pdf_reader, json_tool, docx_reader, relational_database_query, mongodb_query, ...
services.AddOrkeonWebTools();          // web_search, brave_search, web_scrape, http_api, github, slack_send_message, ...
services.AddOrkeonCodeTools();         // shell_command
```

Les outils d'infrastructure (`ask_question`, `delegate_work`, `semantic_search`, `code_interpreter`) sont enregistrés par `AddOrkeonInfrastructure()`. `rag_search` est opt-in : il n'est enregistré que par `AddOrkeonRag(configuration)` (namespace `Orkeon.Rag.DependencyInjection`) + `AddOrkeonRagTools()` (`Orkeon.Tools.Rag`) — voir `docs/reference/opt-in-subsystems.md`.

### Noms à utiliser dans le YAML

Le nom exact à utiliser dans la section `tools:` du YAML est la valeur de la propriété `Name` de la classe outil. Voici la table de correspondance complète :

| Nom YAML | Classe | Package |
|----------|--------|---------|
| `file_read` | `FileReadTool` | `Orkeon.Tools.FileSystem` |
| `file_write` | `FileWriteTool` | `Orkeon.Tools.FileSystem` |
| `directory_read` | `DirectoryReadTool` | `Orkeon.Tools.FileSystem` |
| `directory_search` | `DirectorySearchTool` | `Orkeon.Tools.FileSystem` |
| `email_parser` | `EmailParserTool` | `Orkeon.Tools.FileSystem` |
| `csv_reader` | `CsvReaderTool` | `Orkeon.Tools.Data` |
| `pdf_reader` | `PdfReaderTool` | `Orkeon.Tools.Data` |
| `json_tool` | `JsonTool` | `Orkeon.Tools.Data` |
| `docx_reader` | `DocxReadTool` | `Orkeon.Tools.Data` |
| `docx_writer` | `DocxWriteTool` | `Orkeon.Tools.Data` |
| `relational_database_query` | `RelationalDatabaseTool` | `Orkeon.Tools.Data` |
| `mongodb_query` | `MongoDbTool` | `Orkeon.Tools.Data` |
| `xml_parser` | `XmlParserTool` | `Orkeon.Tools.Data` |
| `sqlserver_query` | `SqlServerTool` | `Orkeon.Tools.Data` |
| `postgres_query` | `PostgresTool` | `Orkeon.Tools.Data` |
| `mysql_query` | `MySqlTool` | `Orkeon.Tools.Data` |
| `mariadb_query` | `MariaDbTool` | `Orkeon.Tools.Data` |
| `arcadedb_query` | `ArcadeDbTool` | `Orkeon.Tools.Data` |
| `janusgraph_query` | `JanusGraphTool` | `Orkeon.Tools.Data` |
| `graph_schema` | `GraphSchemaTool` | `Orkeon.Tools.Data` |
| `database_schema` | `DatabaseSchemaTool` | `Orkeon.Tools.Data` |
| `mongodb_schema` | `MongoDbSchemaTool` | `Orkeon.Tools.Data` |
| `pdf_search` | `PdfSearchTool` | `Orkeon.Tools.Data` |
| `txt_search` | `TXTSearchTool` | `Orkeon.Tools.Data` |
| `mdx_search` | `MDXSearchTool` | `Orkeon.Tools.Data` |
| `web_search` | `WebSearchTool` | `Orkeon.Tools.Web` |
| `brave_search` | `BraveSearchTool` | `Orkeon.Tools.Web` |
| `web_scrape` | `WebScrapeTool` | `Orkeon.Tools.Web` |
| `http_api` | `HttpApiTool` | `Orkeon.Tools.Web` |
| `github` | `GitHubTool` | `Orkeon.Tools.Web` |
| `slack_send_message` | `SlackTool` | `Orkeon.Tools.Web` |
| `slack_read_messages` | `SlackReadTool` | `Orkeon.Tools.Web` |
| `image_generation` | `ImageGenerationTool` | `Orkeon.Tools.Web` |
| `scrape_element` | `ScrapeElementTool` | `Orkeon.Tools.Web` |
| `shell_command` | `ShellCommandTool` | `Orkeon.Tools.Code` |
| `ask_question` | `AskQuestionTool` | `Orkeon.Infrastructure` |
| `delegate_work` | `DelegateWorkTool` | `Orkeon.Infrastructure` |
| `semantic_search` | `SearchTool` | `Orkeon.Infrastructure` |
| `code_interpreter` | `SecureCodeInterpreterTool` | `Orkeon.Infrastructure` |
| `rag_search` | `RagSearchTool` | `Orkeon.Tools.Rag` (opt-in) |
| `rag_ingest` | `RagIngestTool` | `Orkeon.Tools.Rag` (opt-in) |

### Enregistrer un outil custom dans le registry

Pour qu'un outil custom soit utilisable dans le YAML, il doit être enregistré dans `IToolRegistry` :

```csharp
// Option 1 — via DI
services.AddSingleton<IBaseTool, MonCustomTool>();

// Option 2 — enregistrement explicite au runtime
var registry = host.Services.GetRequiredService<IToolRegistry>();
await registry.RegisterToolAsync(new MonCustomTool());
```

L'outil sera alors accessible en YAML par son `Name` :

```yaml
agents:
  my_agent:
    tools:
      - "mon_custom_tool"  # Correspond à Name du tool
```

## Gaps fonctionnels identifiés

Les catégories suivantes ne sont pas couvertes par les outils existants :

- **Écriture de fichiers structurés** : `DocxWriteTool` (`docx_writer`) permet de générer des fichiers Word, mais pas de CSV structuré, PDF ou XLSX. `FileWriteTool` écrit du texte brut uniquement.
- **Transformation de données** : pas d'outil ETL pour convertir entre formats (CSV → JSON, XML → CSV, etc.).
- **Notifications et alertes** : pas d'outil pour envoyer des emails (l'email n'est couvert qu'en lecture/parsing).
- **Gestion de versions** : pas d'outil Git natif pour commit, branch, diff.
- **Calendrier / Planning** : pas d'outil pour interagir avec des calendriers (Google Calendar, Outlook, etc.).
- **Cloud storage** : pas d'outil pour interagir avec S3, Azure Blob, GCS.
- **Authentification OAuth** : pas d'outil générique pour les flux OAuth nécessaires aux API tierces.
- **Traitement d'images** : `MultiModalProcessor` existe en infrastructure mais n'est pas exposé comme outil.
