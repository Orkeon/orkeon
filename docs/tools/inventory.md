> 🇫🇷 [Version française](../fr/tools/inventory.md)

> **See also**: [Creating a new tool](./new-tool-pattern.md) · [Back to index](../INDEX.md)

# Orkeon tool inventory

## Collaboration tools (`Orkeon.Infrastructure.Tools`)

| Tool | Class | Base | Use case | Call example |
|-------|--------|------|-------------|-----------------|
| `ask_question` | `AskQuestionTool` | `ToolBase<AskQuestionRequest, AskQuestionResponse>` | Ask a question to a specialized coworker agent | `{ "question": "What is the Q4 revenue?", "coworker": "Financial Analyst" }` |
| `delegate_work` | `DelegateWorkTool` | `ToolBase<DelegateWorkRequest, DelegateWorkResponse>` | Delegate a complete task to a specialized agent | `{ "task": "Analyze competitor pricing", "coworker": "Market Researcher", "context": "Focus on SaaS B2B" }` |

## Search and knowledge tools (`Orkeon.Infrastructure`)

| Tool | Class | Base | Use case | Call example |
|-------|--------|------|-------------|-----------------|
| `semantic_search` | `SearchTool` | `ToolBase<SearchRequest, SearchResponse>` | Embedding-based semantic search across memories | `{ "query": "customer churn patterns", "limit": 5 }` |
| `rag_search` | `RagTool` | `IBaseTool` (direct) | RAG search in the agent's knowledge bases (opt-in: requires `AddOrkeonRag`) | `{ "question": "What is our return policy?", "top_k": 3 }` |

## Code execution tools (`Orkeon.Infrastructure.Sandbox` / `Orkeon.Tools.Code`)

| Tool | Class | Base | Use case | Call example |
|-------|--------|------|-------------|-----------------|
| `code_interpreter` | `SecureCodeInterpreterTool` | `ToolBase` (non-generic) | Execute C# code in an isolated sandbox with static security analysis | `{ "code": "return 2 + 2;", "timeout_seconds": 10 }` |
| `shell_command` | `ShellCommandTool` | `ToolBase<ShellCommandRequest, ShellCommandResponse>` | Execute shell commands with allowlist/blocklist and timeout | `{ "command": "ls -la /data", "timeout_seconds": 30 }` |

## File tools (`Orkeon.Tools.FileSystem`)

| Tool | Class | Base | Use case | Call example |
|-------|--------|------|-------------|-----------------|
| `file_read` | `FileReadTool` | `FileToolBase<FileReadRequest, FileReadResponse>` | Read the content of a file (text, JSON, XML, .eml, .msg) | `{ "file_path": "/data/report.txt" }` |
| `file_write` | `FileWriteTool` | `FileToolBase<FileWriteRequest, FileWriteResponse>` | Write content to a file, creating folders if necessary | `{ "file_path": "/output/result.txt", "content": "Analysis complete.", "append": false }` |
| `directory_read` | `DirectoryReadTool` | `FileToolBase<DirectoryReadRequest, DirectoryReadResponse>` | List the contents of a directory with glob filtering and recursion | `{ "directory": "/data", "pattern": "*.csv", "recursive": true }` |
| `directory_search` | `DirectorySearchTool` | `ToolBase<DirectorySearchRequest, DirectorySearchResponse>` | RAG semantic search across the files of a directory | `{ "directory": "/docs", "query": "deployment instructions", "file_pattern": "*.md" }` |
| `email_parser` | `EmailParserTool` | `FileToolBase<EmailParserRequest, EmailParserResponse>` | Parse .eml/.msg emails and extract headers, body, attachments | `{ "file_path": "/emails/invoice.eml" }` |

## Data tools (`Orkeon.Tools.Data`)

| Tool | Class | Base | Use case | Call example |
|-------|--------|------|-------------|-----------------|
| `csv_reader` | `CsvReaderTool` | `FileToolBase<CsvReaderRequest, CsvReaderResponse>` | Read and structure CSV files with delimiter detection | `{ "file_path": "/data/sales.csv", "delimiter": "," }` |
| `pdf_reader` | `PdfReaderTool` | `FileToolBase<PdfReaderRequest, PdfReaderResponse>` | Extract text from PDF files with page selection | `{ "file_path": "/docs/contract.pdf", "start_page": 1, "end_page": 5 }` |
| `json_tool` | `JsonTool` | `ToolBase<JsonToolRequest, JsonToolResponse>` | Manipulate JSON: parse (structure analysis), query (dot-notation navigation), format (pretty-print) | `{ "operation": "query", "json_content": "{...}", "path": "data.users[0].name" }` |
| `docx_reader` | `DocxReadTool` | `FileToolBase<DocxReadRequest, DocxReadResponse>` | Read Word files with text, table and metadata extraction | `{ "file_path": "/docs/report.docx" }` |
| `relational_database_query` | `RelationalDatabaseTool` | `ToolBase<RelationalDatabaseRequest, RelationalDatabaseResponse>` | Execute SQL queries (SQL Server, PostgreSQL, MySQL, MariaDB, SQLite) | `{ "connection_string": "...", "query": "SELECT * FROM orders WHERE status = 'pending'", "query_type": "select", "provider": "postgresql" }` |
| `mongodb_query` | `MongoDbTool` | `ToolBase<MongoDbRequest, MongoDbResponse>` | Execute MongoDB operations (Find, Aggregate, CRUD) | `{ "connection_string": "...", "database": "shop", "collection": "products", "operation": "find", "filter": "{ \"price\": { \"$gt\": 100 } }" }` |
| `docx_writer` | `DocxWriteTool` | `ToolBase<DocxWriteRequest, DocxWriteResponse>` | Create Word files (.docx) with title, paragraphs and bullet lists | `{ "file_path": "/output/report.docx", "title": "Monthly Report", "paragraphs": ["Introduction text..."] }` |

### Additional data tools

| Tool | Class | Base | Use case |
|-------|--------|------|-------------|
| `arcadedb_query` | `ArcadeDbTool` | `ToolBase<...>` | Queries against the ArcadeDB graph database |
| `janusgraph_query` | `JanusGraphTool` | `ToolBase<...>` | Queries against the JanusGraph graph database |
| `graph_schema` | `GraphSchemaTool` | `ToolBase<...>` | Graph database schema inspection |
| `database_schema` | `DatabaseSchemaTool` | `ToolBase<...>` | Relational database schema inspection |
| `mongodb_schema` | `MongoDbSchemaTool` | `ToolBase<...>` | MongoDB collection schema inference |
| `xml_parser` | `XmlParserTool` | `ToolBase<...>` | XML data parsing and extraction |
| `sqlserver_query` | `SqlServerTool` | `ToolBase<...>` | Specialized SQL Server queries |
| `postgres_query` | `PostgresTool` | `ToolBase<...>` | Specialized PostgreSQL queries |
| `mysql_query` | `MySqlTool` | `ToolBase<...>` | Specialized MySQL queries |
| `mariadb_query` | `MariaDbTool` | `ToolBase<...>` | Specialized MariaDB queries |
| `pdf_search` | `PdfSearchTool` | `ToolBase<...>` | Semantic search in PDF files via embeddings |
| `txt_search` | `TXTSearchTool` | `ToolBase<...>` | Semantic search in text files |
| `mdx_search` | `MDXSearchTool` | `ToolBase<...>` | Semantic search in MDX/Markdown files |

## Web tools (`Orkeon.Tools.Web`)

| Tool | Class | Base | Use case | Call example |
|-------|--------|------|-------------|-----------------|
| `web_search` | `WebSearchTool` | `HttpToolBase<WebSearchRequest, WebSearchResponse>` | Web search via the Tavily Search API | `{ "query": "best practices microservices 2025", "max_results": 5 }` |
| `brave_search` | `BraveSearchTool` | `HttpToolBase<WebSearchRequest, WebSearchResponse>` | Web search via the Brave Search API | `{ "query": "C# performance optimization", "max_results": 3 }` |
| `web_scrape` | `WebScrapeTool` | `HttpToolBase<WebScrapeRequest, WebScrapeResponse>` | Scrape the content of a web page with optional CSS filtering | `{ "url": "https://example.com/docs", "selector": "article.content" }` |
| `http_api` | `HttpApiTool` | `HttpToolBase<HttpApiRequest, HttpApiResponse>` | HTTP REST calls (GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS) with SSRF protection | `{ "url": "https://api.example.com/users", "method": "GET", "headers": { "Authorization": "Bearer ..." } }` |

### Additional web tools

| Tool | Class | Base | Use case |
|-------|--------|------|-------------|
| `github` | `GitHubTool` | `HttpToolBase<...>` | Interaction with the GitHub API |
| `slack_send_message` | `SlackTool` | `HttpToolBase<...>` | Sending messages via the Slack API |
| `slack_read_messages` | `SlackReadTool` | `HttpToolBase<...>` | Reading Slack messages (read-only) |
| `image_generation` | `ImageGenerationTool` | `HttpToolBase<...>` | Image generation via an external API |
| `scrape_element` | `ScrapeElementTool` | `HttpToolBase<...>` | Targeted scraping of DOM elements |

## Web search tools (`Orkeon.Infrastructure.Tools.Search`)

| Tool | Class | Base | Use case |
|-------|--------|------|-------------|
| `bing_search` | `BingSearchTool` | via `SearchTool` | Web search via the Bing API |
| `google_search` | `GoogleSearchTool` | via `SearchTool` | Web search via the Google Custom Search API |

## Summary by category

| Category | Count | Package |
|-----------|--------|---------|
| Collaboration | 2 | `Orkeon.Infrastructure` |
| Search / RAG | 2 | `Orkeon.Infrastructure` |
| Code | 2 | `Orkeon.Infrastructure` + `Orkeon.Tools.Code` |
| Files | 5 | `Orkeon.Tools.FileSystem` |
| Data | 17+ | `Orkeon.Tools.Data` |
| Web | 8+ | `Orkeon.Tools.Web` |
| **Total** | **36+** | |

## Tool resolution by name (YAML → instance)

When a crew is defined in YAML, tools are referenced by their name (the tool class's `Name` property). Resolution happens via `IToolRegistry` (`Orkeon.Domain.Tools`).

### Resolution pipeline

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

### Tool registration

Tools are registered in `IToolRegistry` at application startup via the DI extensions:

```csharp
// Chaque suite enregistre ses outils dans IToolRegistry
services.AddOrkeonFileSystemTools();   // file_read, file_write, directory_read, directory_search, email_parser
services.AddOrkeonDataTools();         // csv_reader, pdf_reader, json_tool, docx_reader, relational_database_query, mongodb_query, ...
services.AddOrkeonWebTools();          // web_search, brave_search, web_scrape, http_api, github, slack_send_message, ...
services.AddOrkeonCodeTools();         // shell_command
```

The infrastructure tools (`ask_question`, `delegate_work`, `semantic_search`, `code_interpreter`) are registered by `AddOrkeonInfrastructure()`. `rag_search` is opt-in: it is only registered by `AddOrkeonRag(configuration)` (see `docs/reference/opt-in-subsystems.md`).

### Names to use in YAML

The exact name to use in the YAML `tools:` section is the value of the tool class's `Name` property. Here is the complete mapping table:

| YAML name | Class | Package |
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
| `rag_search` | `RagTool` | `Orkeon.Infrastructure` |

### Registering a custom tool in the registry

For a custom tool to be usable in YAML, it must be registered in `IToolRegistry`:

```csharp
// Option 1 — via DI
services.AddSingleton<IBaseTool, MonCustomTool>();

// Option 2 — enregistrement explicite au runtime
var registry = host.Services.GetRequiredService<IToolRegistry>();
await registry.RegisterToolAsync(new MonCustomTool());
```

The tool will then be accessible in YAML through its `Name`:

```yaml
agents:
  my_agent:
    tools:
      - "mon_custom_tool"  # Correspond à Name du tool
```

## Identified functional gaps

The following categories are not covered by the existing tools:

- **Structured file writing**: `DocxWriteTool` (`docx_writer`) can generate Word files, but no structured CSV, PDF or XLSX. `FileWriteTool` writes plain text only.
- **Data transformation**: no ETL tool to convert between formats (CSV → JSON, XML → CSV, etc.).
- **Notifications and alerts**: no tool to send emails (email is only covered for reading/parsing).
- **Version control**: no native Git tool for commit, branch, diff.
- **Calendar / Scheduling**: no tool to interact with calendars (Google Calendar, Outlook, etc.).
- **Cloud storage**: no tool to interact with S3, Azure Blob, GCS.
- **OAuth authentication**: no generic tool for the OAuth flows required by third-party APIs.
- **Image processing**: `MultiModalProcessor` exists in the infrastructure but is not exposed as a tool.
