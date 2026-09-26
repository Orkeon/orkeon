> 🇫🇷 [Version française](../fr/tools/inventory.md)

> **See also**: [Creating a new tool](./new-tool-pattern.md) · [Back to index](../INDEX.md)

# Orkeon tool inventory

This page is the reference catalogue of the **91 built-in tool classes**. The counting
rule is the one `scripts/check-doc-claims.py` enforces: every `*Tool.cs` file under
`src/` except the interface (`IBaseTool.cs`) and the non-tool infrastructure that
matches the glob (`MockTool.cs`, `JsTool.cs`, `ObservedTool.cs`, `AIAgentTool.cs`). The same script
verifies that **every tool name below exists in the code and every tool in the code is
named below** — this page cannot silently drift from the implementation.

The `Tool` column is the exact name agents and YAML `tools:` lists use (the contract's
`UniqueName`). Which tools are actually available at runtime depends on the composition
root — see [Availability by composition root](#availability-by-composition-root).

## Collaboration and human-in-the-loop (`Orkeon.Infrastructure`)

| Tool | Class | Registration | Use case | Call example |
|-------|--------|------|-------------|-----------------|
| `ask_question_to_coworker` | `AskQuestionTool` | Built per agent by `AgentDelegationToolsProvider` when `AllowDelegation` is on — never in DI | Ask a question to a specialized coworker agent | `{ "question": "What is the Q4 revenue?", "coworker": "Financial Analyst" }` |
| `delegate_work_to_coworker` | `DelegateWorkTool` | Same per-agent construction (optional `AgentExecutionBudget` for recursive depth control) | Delegate a complete task to a specialized agent | `{ "task": "Analyze competitor pricing", "coworker": "Market Researcher", "context": "Focus on SaaS B2B" }` |
| `spawn_agent` | `SpawnAgentTool` | **Not wired by any shipped composition root** — a host registers it explicitly (`IAgentFactory` required) | Create and execute a new specialised sub-agent at runtime (autonomous mode) | `{ "role": "Fact checker", "goal": "Verify the claims", "task": "..." }` |
| `human_input` | `HumanInputTool` | `AddOrkeonHumanInput()` — wired by `orkeon run` (`RunnerExecution`), where the question surfaces on the run event bus | Ask the human a question (text, approval, choice); the runtime suspends until the answer arrives | `{ "prompt": "Deploy to production?", "kind": "approval" }` |

The display names of the first three contracts are prose (“Ask question to coworker”,
“Delegate work to coworker”, “Spawn sub-agent”); the registry name — the one above — is
what YAML references.

## Search and knowledge tools (`Orkeon.Hosting` / `Orkeon.Tools.Rag`)

| Tool | Class | Registration | Use case | Call example |
|-------|--------|------|-------------|-----------------|
| `semantic_search` | `SearchTool` | `AddSemanticSearchTool()` (`Orkeon.Hosting`, opt-in — `orkeon run` calls it) | Embedding-based semantic search across memories | `{ "query": "customer churn patterns", "limit": 5 }` |
| `rag_search` | `RagSearchTool` | Opt-in: `AddOrkeonRag(config)` + `AddOrkeonRagTools()` (`Orkeon.Tools.Rag`) | RAG search in the agent's knowledge bases over `IRagPipeline` | `{ "question": "What is our return policy?", "top_k": 3 }` |
| `rag_ingest` | `RagIngestTool` | Same opt-in as `rag_search` | Incremental ingestion into a RAG collection (manifest-driven — unchanged sources cost 0 embeddings) | `{ "collection": "docs", "sources": ["/kb/**/*.md"], "reindex": false }` |
| `rag_eval` | `RagEvalTool` | Same opt-in as `rag_search` | Evaluate a collection against a golden YAML dataset: recall@k, precision@k, MRR, groundedness | `{ "collection": "docs", "dataset": "/kb/eval/golden.yaml" }` |

## Code execution tools (`Orkeon.Infrastructure.Sandbox` / `Orkeon.Tools.Code`)

| Tool | Class | Registration | Use case | Call example |
|-------|--------|------|-------------|-----------------|
| `code_interpreter` | `SecureCodeInterpreterTool` | `AddOrkeonCodeSandbox()` — **concrete type only**, not under `IBaseTool`: the registry cannot resolve it by name; a host injects or registers it explicitly | Execute C# code in an isolated sandbox with static security analysis | `{ "code": "return 2 + 2;", "timeout_seconds": 10 }` |
| `shell_command` | `ShellCommandTool` | `AddOrkeonCodeTools()` | Execute shell commands with allowlist/blocklist and timeout; mount-prefixed virtual path arguments are resolved for the process and physical paths in the output come back virtualized | `{ "command": "cat /workspace/README.md", "timeout_seconds": 30 }` |

## Session and memory tools (`Orkeon.Infrastructure`) — `AddOrkeonSessionTools()`

| Tool | Class | Use case |
|-------|--------|-------------|
| `session_store` | `SessionStoreTool` | Read/write/truncate/annotate the session buffer and its metadata |
| `session_snip` | `SessionSnipTool` | Immediately truncate the conversation to a minimal window |
| `session_stats` | `SessionStatsTool` | Full session telemetry: messages, tokens, cost, call totals |
| `session_cost` | `SessionCostTool` | Cumulative session cost in USD with a per-model breakdown |
| `token_budget` | `TokenBudgetTool` | Context window size, used tokens, available budget |
| `memory_store` | `MemoryStoreTool` | List/add/delete/get typed memories (user/project/feedback/reference) |

## File tools (`Orkeon.Tools.FileSystem`) — `AddOrkeonFileSystemTools()`

| Tool | Class | Base | Use case | Call example |
|-------|--------|------|-------------|-----------------|
| `file_read` | `FileReadTool` | `FileToolBase<FileReadRequest, FileReadResponse>` | Read the content of a file (text, JSON, XML and the like — an `.eml` message goes to `email_parser`) | `{ "file_path": "/data/report.txt" }` |
| `file_write` | `FileWriteTool` | `FileToolBase<FileWriteRequest, FileWriteResponse>` | Write content to a file, creating folders if necessary | `{ "file_path": "/output/result.txt", "content": "Analysis complete.", "append": false }` |
| `directory_read` | `DirectoryReadTool` | `FileToolBase<DirectoryReadRequest, DirectoryReadResponse>` | List the contents of a directory with glob filtering and recursion | `{ "directory": "/data", "pattern": "*.csv", "recursive": true }` |
| `directory_search` | `DirectorySearchTool` | `ToolBase<DirectorySearchRequest, DirectorySearchResponse>` | RAG semantic search across the files of a directory | `{ "directory": "/docs", "query": "deployment instructions", "file_pattern": "*.md" }` |
| `count_pattern` | `CountPatternTool` | `FileToolBase<CountPatternRequest, CountPatternResponse>` | Deterministic regex occurrence count per pattern in a file (no LLM guessing) | `{ "file_path": "/data/log.txt", "patterns": ["ERROR", "WARN"] }` |

## Data tools (`Orkeon.Tools.Data`) — `AddOrkeonDataTools()`

`AddOrkeonDataTools()` registers all 22 (it chains `AddRelationalDatabaseTools()`,
`AddMongoDbTools()` and `AddGraphDatabaseTools()` internally).

| Tool | Class | Use case | Call example |
|-------|--------|-------------|-----------------|
| `csv_reader` | `CsvReaderTool` | Read and structure CSV files with delimiter detection | `{ "file_path": "/data/sales.csv", "delimiter": "," }` |
| `pdf_reader` | `PdfReaderTool` | Extract text from PDF files with page selection | `{ "file_path": "/docs/contract.pdf", "start_page": 1, "end_page": 5 }` |
| `json_tool` | `JsonTool` | Manipulate JSON: parse (structure analysis), query (dot-notation navigation), format (pretty-print) | `{ "operation": "query", "json_content": "{...}", "path": "data.users[0].name" }` |
| `xml_parser` | `XmlParserTool` | Parse XML from files or strings with XPath support | `{ "file_path": "/data/feed.xml", "xpath": "//item/title" }` |
| `docx_reader` | `DocxReadTool` | Read Word files with text, table and metadata extraction | `{ "file_path": "/docs/report.docx" }` |
| `docx_writer` | `DocxWriteTool` | Create Word files (.docx) with title, paragraphs and bullet lists | `{ "file_path": "/output/report.docx", "title": "Monthly Report", "paragraphs": ["Introduction text..."] }` |
| `xlsx_reader` | `XlsxReadTool` | Read Excel files (.xlsx): sheets, rows, columns, metadata (ClosedXML) | `{ "file_path": "/data/budget.xlsx", "sheet_name": "Q3" }` |
| `xlsx_writer` | `XlsxWriteTool` | Create or update Excel files (.xlsx) with multiple sheets, headers and rows | `{ "file_path": "/output/summary.xlsx", "sheet_name": "Totals", "headers": ["Region", "Revenue"], "rows": [["EMEA", "1.2M"]] }` |
| `relational_database_query` | `RelationalDatabaseTool` | Execute SQL queries (SQL Server, PostgreSQL, MySQL, MariaDB, SQLite) | `{ "connection_string": "...", "query": "SELECT * FROM orders WHERE status = 'pending'", "query_type": "select", "provider": "postgresql" }` |
| `sqlserver_query` | `SqlServerDatabaseTool` | Specialized SQL Server queries | — |
| `postgres_query` | `PostgresDatabaseTool` | Specialized PostgreSQL queries | — |
| `mysql_query` | `MySqlDatabaseTool` | Specialized MySQL queries | — |
| `mariadb_query` | `MariaDbDatabaseTool` | Specialized MariaDB queries | — |
| `database_schema` | `DatabaseSchemaTool` | Relational schema inspection (tables, columns, indexes, foreign keys) | — |
| `mongodb_query` | `MongoDbTool` | Execute MongoDB operations (Find, Aggregate, CRUD) | `{ "connection_string": "...", "database": "shop", "collection": "products", "operation": "find", "filter": "{ \"price\": { \"$gt\": 100 } }" }` |
| `mongodb_schema` | `MongoDbSchemaTool` | MongoDB collection schema inference by sampling documents | — |
| `arcadedb_query` | `ArcadeDbTool` | Queries against the ArcadeDB graph database | — |
| `janusgraph_query` | `JanusGraphTool` | Gremlin traversals against JanusGraph | — |
| `graph_schema` | `GraphSchemaTool` | Graph database schema inspection (vertex/edge labels, properties, indexes) | — |
| `pdf_search` | `PdfSearchTool` | Semantic search in PDF files via embeddings | — |
| `txt_search` | `TxtSearchTool` | Semantic search in text files | — |
| `mdx_search` | `MdxSearchTool` | Semantic search in MDX/Markdown files (strips frontmatter and JSX) | — |

## Web tools (`Orkeon.Tools.Web`)

`AddOrkeonWebTools()` registers the first five; each of the others has its own opt-in
extension because it needs a key or an extra backing service. Secrets are always
referenced by environment-variable name — never stored in configuration.

| Tool | Class | Registration | Use case |
|-------|--------|------|-------------|
| `http_api` | `HttpApiTool` | `AddOrkeonWebTools()` | HTTP REST calls (GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS) with SSRF protection |
| `web_scrape` | `WebScrapeTool` | `AddOrkeonWebTools()` | Scrape a web page with optional CSS filtering; `cached=true` chunks and embeds the page into the RAG cache |
| `scrape_element` | `ScrapeElementTool` | `AddOrkeonWebTools()` | Targeted scraping of DOM elements via CSS selectors (text, HTML, attributes) |
| `github` | `GitHubTool` | `AddOrkeonWebTools()` | GitHub API v3: list/create issues, read PRs, search repositories |
| `image_generation` | `ImageGenerationTool` | `AddOrkeonWebTools()` (needs an OpenAI key at call time) | Image generation via the OpenAI DALL-E API |
| `web_search` | `WebSearchTool` | `AddOrkeonWebSearchTool()` — Tavily key resolved at runtime via `ISecretProvider` | Web search via the Tavily Search API |
| `brave_search` | `BraveSearchTool` | `AddOrkeonBraveSearchTool(apiKey)` — `orkeon run` wires it only when `BRAVE_API_KEY` is set | Web search via the Brave Search API |
| `slack_send_message` | `SlackTool` | `AddOrkeonSlackTool(botToken)` — no shipped root calls it (pure host opt-in) | Send messages to Slack channels/users via the Web API |
| `slack_read_messages` | `SlackReadTool` | `AddOrkeonSlackReadTool(botToken)` — same host opt-in | Read Slack messages (read-only) |
| `cache_search` | `CacheSearchTool` | `AddOrkeonCacheSearchTool()` | Semantic search over the RAG cache other tools populate (e.g. `web_scrape` with `cached=true`) |

## Event hub tools (`Orkeon.Tools.EventHub`) — `AddOrkeonEventHubTools()`

Seven tools, registered together with the in-memory hub (`AddOrkeonInMemoryEventHub()`).
The full semantics live in [EventHub and the crew lifecycle](../architecture/event-hub-and-crew-lifecycle.md).

| Tool | Class | Use case |
|-------|--------|-------------|
| `publish_event` | `PublishEventTool` | Publish an event on a topic (1→N broadcast), optional crew scope and metadata |
| `post_message` | `PostMessageTool` | Fire-and-forget message to a mailbox (`agent://`, `crew://`, `topic://`) |
| `send_request` | `SendRequestTool` | Send a request to a mailbox and wait for the correlated reply (`timeout_ms` required) |
| `reply_to` | `ReplyToTool` | Reply to a pending request identified by its `correlation_id` |
| `receive_message` | `ReceiveMessageTool` | Pull the next message from a mailbox (destructive read, crew-scoped) |
| `wait_for_event` | `WaitForEventTool` | Wait for the next event on a topic (exactly one of `timeout_ms`/`wait_forever`) |
| `get_last_value` | `GetLastValueTool` | Read the last retained payload for a key (last-value cache) |

## E-mail tools (`Orkeon.Tools.Email`) — `AddOrkeonEmailTools(configuration)`

Thirteen tools over the accounts an operator declares under `Orkeon:Tools:Email`: IMAP, POP3
and SMTP through MailKit, Outlook.com and Microsoft 365 through Microsoft Graph. Until an
account is declared the mailbox tools refuse every call, `email_accounts` lists none and
`email_parser`, which reads a file, works. An agent names an account (`account`, or the default one) —
never a server or a credential — and the account's `Rights` decide what it may do; `email_send`
reaches only the recipients the account's `Send:AllowedRecipients` list allows. Message ids are
opaque: pass them back exactly as `email_search` returned them. The **Access** column is the
class the [permission gate](../reference/opt-in-subsystems.md) sees. Every parameter is listed in
the [e-mail guide](../guides/email.md#parameters); results are sized to what the agent loop keeps,
and `cursor` / `offset` resume exactly where a page or a slice ends. Setup for Gmail,
Hotmail/Outlook.com and your own server, the security model and the `orkeon email` commands:
[E-mail tools](../guides/email.md).

| Tool | Class | Right needed | Access | Use case | Call example |
|-------|--------|------|------|-------------|-----------------|
| `email_accounts` | `EmailAccountsTool` | none | Read | List the configured accounts: the name to pass as `account`, their rights, whether each is ready | `{}` |
| `email_folders` | `EmailFoldersTool` | Read | Read | List the folders with their role (inbox, sent, drafts, trash, junk, archive) and message counts | `{ "account": "work" }` |
| `email_search` | `EmailSearchTool` | Read | Read | Search one folder, newest first: unread, flagged, from, to, subject, text, dates, attachments, or a provider-native `raw_query` (Gmail search syntax, Outlook KQL); 10 per page by default, 50 at most, `cursor` for the next page | `{ "folder": "inbox", "unread_only": true, "since": "2026-09-01" }` |
| `email_read` | `EmailReadTool` | Read (+ Organize with `mark_read`) | Read | Read one message: the untrusted-content notice and the screening verdict first, then headers, attachments and the body in slices (`offset`, `max_chars` 200–3000, default 2500, then `next_offset`) | `{ "id": "<id from email_search>" }` |
| `email_save_attachment` | `EmailSaveAttachmentTool` | Read, plus a writable mount | Edit | Save one attachment (`index`) or all of them into a virtual directory; names sanitized, nothing overwritten | `{ "id": "…", "directory": "/output/attachments" }` |
| `email_create_folder` | `EmailCreateFolderTool` | Organize | Edit | Create a folder (a label on Gmail); missing parents are created | `{ "path": "Clients/ACME" }` |
| `email_rename_folder` | `EmailRenameFolderTool` | Organize | Edit | Rename a folder's last segment; system folders are refused | `{ "path": "Clients/ACME", "new_name": "ACME Corp" }` |
| `email_move` | `EmailMoveTool` | Organize | Edit | Move messages to a folder path or role; answers each message's new id when the server gives it | `{ "ids": ["…"], "destination": "archive" }` |
| `email_mark` | `EmailMarkTool` | Organize | Edit | Mark read or unread (`seen`), flag or unflag (`flagged`, a star on Gmail) | `{ "ids": ["…"], "seen": true }` |
| `email_delete` | `EmailDeleteTool` | Delete; Purge with `permanent: true` | Execute | Move messages to the trash, or delete them for good | `{ "ids": ["…"] }` |
| `email_draft` | `EmailDraftTool` | Draft (+ Read to reply or forward) | Edit | Save a new message, a reply (`reply_to_id`, `reply_all`) or a forward (`forward_id`) in Drafts without sending it — the human-review path, no allow-list | `{ "to": ["client@example.com"], "subject": "Quote", "text": "…" }` |
| `email_send` | `EmailSendTool` | Send (+ Read to reply or forward) | Execute | Send a new message, a reply or a forward to the recipients `Send:AllowedRecipients` allows (an empty list allows nobody); `From` is always the account | `{ "reply_to_id": "…", "text": "Received, thank you." }` |
| `email_parser` | `EmailParserTool` | none (no account) | Read | Parse an `.eml` file from a virtual path; same output as `email_read` | `{ "path": "/workspace/mail/invoice.eml" }` |

## Codebase analysis tools (`Orkeon.Tools.Analysis`) — `AddRaggableTreeTools()`

Fifteen tools over the RaggableTree semantic code graph — the full guide is
[RaggableTree](../architecture/raggable-tree.md).

| Tool | Class | Use case |
|-------|--------|-------------|
| `index_codebase` | `IndexCodebaseTool` | Scan the codebase and build the full RaggableTree index |
| `incremental_reindex` | `IncrementalReindexTool` | Reindex only the files changed since a commit or an explicit list |
| `index_status` | `IndexStatusTool` | List every virtual root currently indexed |
| `is_path_indexed` | `IsPathIndexedTool` | Check whether a virtual path is covered by an indexed root |
| `codebase_map` | `CodebaseMapTool` | Structural map of the codebase at a given zoom level (L0–L3) |
| `package_summary` | `PackageSummaryTool` | Package (L1) details: file/symbol counts, exports, dependencies |
| `symbol_detail` | `SymbolDetailTool` | Expanded L3 symbol detail: signature, doc, metrics, members, callers/callees |
| `symbol_source` | `SymbolSourceTool` | Deterministic source citation: path, lines, code, SHA-256, stable flag |
| `codebase_search` | `CodebaseSearchTool` | Hybrid (vector + BM25) search with ranked, cited hits |
| `dependency_graph` | `DependencyGraphTool` | Dependency graph at a scope (L1 packages, L2 modules, L3 symbols) |
| `sub_graph` | `SubGraphTool` | Sub-graph expansion around seeds, bounded by depth and node count |
| `flow_trace` | `FlowTraceTool` | Trace the call flow from a symbol within a depth budget |
| `impact_analysis` | `ImpactAnalysisTool` | Direct and transitive callers affected by changing a symbol |
| `complexity_report` | `ComplexityReportTool` | Top-N methods by complexity metrics, with median/P95 |
| `statement_query` | `StatementQueryTool` | Query L4 statements by kind, parent FQN, or semantic similarity |

## Mounts and local embeddings

| Tool | Class | Registration | Use case |
|-------|--------|------|-------------|
| `list_mounts` | `ListMountsTool` (`Orkeon.Tools.Abstractions`) | `AddOrkeonAbstractionTools()` | List the VFS mounts visible to the agent, with virtual paths and access rights |
| `local_embed_text` | `LocalEmbedTool` (`Orkeon.Tools.Embeddings.Local`) | `AddOrkeonLocalEmbeddings()` | Embed texts on-device (BGE-micro-v2 ONNX, 384 dims, CPU — no network, no API key) |

## Host-side tools

| Tool | Class | Registration | Use case |
|-------|--------|------|-------------|
| `progress_report` | `ProgressReportTool` (`Orkeon.Cli.Commands.Scripting`) | `AddScriptCommands(...)` (the scripted-commands host) | Report the progress of a long-running scripted operation to the CLI status line |

## Outside the catalogue

Deliberately **not** part of the 91 built-in tool classes:

- `brief_submit` / `blueprint_submit` — internal to the `orkeon forge` command
  (`ForgeSubmission.cs`, `Orkeon.Scripting.Cli`); the engine's own agents use them, a
  crew never lists them.
- `McpToolAdapter` (`Orkeon.Infrastructure.MCP`) — bridges an external MCP server tool
  into `IBaseTool`; its name is the remote tool's, decided at runtime
  (see [MCP](../architecture/mcp.md)).
- `JsTool` (script-defined dynamic tools), `MockTool` (test double), `ObservedTool`
  (telemetry decorator) and `AIAgentTool` (`Orkeon.Interop.AgentFramework` — wraps a
  Microsoft Agent Framework agent you supply) — infrastructure matching the file glob,
  excluded by the counting rule.

## Summary by category

Counting **concrete tool classes** with the rule stated at the top of this page — the
number the README and the documentation index state, checked by
`scripts/check-doc-claims.py`:

| Package | Tool classes |
|---------|--------------|
| `Orkeon.Tools.Data` | 22 |
| `Orkeon.Tools.Analysis` (RaggableTree — see [its guide](../architecture/raggable-tree.md)) | 15 |
| `Orkeon.Tools.Email` (see [its guide](../guides/email.md)) | 13 |
| `Orkeon.Infrastructure` (collaboration, session, sandbox, human input) | 12 |
| `Orkeon.Tools.Web` | 10 |
| `Orkeon.Tools.EventHub` | 7 |
| `Orkeon.Tools.FileSystem` | 5 |
| `Orkeon.Tools.Rag` | 3 |
| `Orkeon.Tools.Abstractions` / `Orkeon.Tools.Code` / `Orkeon.Tools.Embeddings.Local` / `Orkeon.Cli.Commands.Scripting` | 1 each |
| **Total** | **91** |

## Availability by composition root

The two shipped composition roots do not register the same suites. Sources:
`RunnerHost.cs` + `RunnerExecution.cs`/`RunCommand.cs` for the CLI, `Program.cs` for
the REPL.

| Suite / tool | `orkeon run` (CLI) | `orkeon-repl` (ConsoleApp) |
|---|---|---|
| FileSystem (5), Data (22), Web core (5), `shell_command`, `list_mounts`, session (6) | ✅ | ✅ |
| E-mail (13) — the mailbox tools refuse every call until an account is declared under `Orkeon:Tools:Email` | ✅ (OAuth tokens under the internal `/credentials` root) | ✅ password accounts only — the REPL keeps no token store, so an OAuth account is refused |
| EventHub (7) | ✅ | ❌ |
| Analysis (15) | ✅ (unless `RaggableTree:Enabled` = `false`) | ✅ |
| `local_embed_text` | ✅ (default local embedding provider) | ✅ |
| RAG (`rag_search`, `rag_ingest`, `rag_eval`) | ❌ | ✅ |
| `web_search`, `cache_search` | ✅ | ❌ |
| `brave_search` | ✅ only if `BRAVE_API_KEY` is set | ❌ |
| `slack_send_message`, `slack_read_messages` | ❌ (host opt-in) | ❌ |
| The tools of the MCP servers `MCP:Servers` declares | ✅ connected before the crew loads, under their own names (STUDIO-21) | ❌ |
| `semantic_search` | ✅ (wired by the run command) | ❌ |
| `human_input` | ✅ (question surfaces on the run event bus) | ❌ |
| `ask_question_to_coworker`, `delegate_work_to_coworker` | per agent, when `AllowDelegation` is on | per agent |
| `spawn_agent` | ❌ (host must register it) | ❌ |
| `code_interpreter` | concrete-type registration only — not resolvable by name | idem |
| `progress_report` | ❌ | ✅ (scripted commands) |

`orkeon forge` builds on the runner host but removes `shell_command`, `code_interpreter` and
the twelve mailbox tools from the catalogue a forged crew draws from: a crew tried on the test
bench must not reach the operator's real mailbox (`email_parser`, which reads a file, stays).

## Tool resolution by name (YAML → instance)

When a crew is defined in YAML, tools are referenced by their name (the tool class's `Name` property). Resolution happens via `IToolRegistry` (`Orkeon.Domain.Tools`).

### Resolution pipeline

```
YAML config: tools: ["relational_database_query", "csv_reader"]
       ↓
CrewFactory calls IToolRegistry.GetToolByNameAsync("relational_database_query")
       ↓
IToolRegistry looks up the registered tool with Name == "relational_database_query"
       ↓
If found → ITool injected into the Agent via AgentBuilder.WithTool()
If missing → CrewFactory raises a validation error
```

### Tool registration

Tools are registered in `IToolRegistry` at application startup via the DI extensions
(each table above names the one that owns its tools):

```csharp
services.AddOrkeonFileSystemTools();   // file_read, file_write, directory_read, directory_search, count_pattern
services.AddOrkeonDataTools();         // the 22 data tools (relational + MongoDB + graph chained internally)
services.AddOrkeonWebTools();          // http_api, web_scrape, scrape_element, github, image_generation
services.AddOrkeonCodeTools();         // shell_command
services.AddOrkeonAbstractionTools();  // list_mounts
services.AddOrkeonSessionTools();      // session_store, session_snip, session_stats, session_cost, token_budget, memory_store
services.AddOrkeonInMemoryEventHub();
services.AddOrkeonEventHubTools();     // the 7 event-hub tools
services.AddOrkeonEmailTools(configuration); // the 13 email_* tools; the mailbox ones need a declared account
services.AddRaggableTreeTools();       // the 15 analysis tools
services.AddOrkeonLocalEmbeddings();   // local_embed_text
services.AddSemanticSearchTool();      // semantic_search (Orkeon.Hosting)
services.AddOrkeonWebSearchTool();     // web_search
services.AddOrkeonCacheSearchTool();   // cache_search
services.AddOrkeonRag(configuration); services.AddOrkeonRagTools(); // rag_search, rag_ingest, rag_eval (opt-in)
```

`rag_search`/`rag_ingest`/`rag_eval` are opt-in — see `docs/reference/opt-in-subsystems.md`.

### Names to use in YAML

The exact name to use in the YAML `tools:` section is the value of the tool class's
`Name` property — the `Tool` column of every table above. Complete alphabetical mapping:

| YAML name | Class | Package |
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
| `semantic_search` | `SearchTool` | `Orkeon.Hosting` (opt-in) |
| `send_request` | `SendRequestTool` | `Orkeon.Tools.EventHub` |
| `session_cost` | `SessionCostTool` | `Orkeon.Infrastructure` |
| `session_snip` | `SessionSnipTool` | `Orkeon.Infrastructure` |
| `session_stats` | `SessionStatsTool` | `Orkeon.Infrastructure` |
| `session_store` | `SessionStoreTool` | `Orkeon.Infrastructure` |
| `shell_command` | `ShellCommandTool` | `Orkeon.Tools.Code` |
| `slack_read_messages` | `SlackReadTool` | `Orkeon.Tools.Web` (opt-in) |
| `slack_send_message` | `SlackTool` | `Orkeon.Tools.Web` (opt-in) |
| `spawn_agent` | `SpawnAgentTool` | `Orkeon.Infrastructure` (host-registered) |
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

### Registering a custom tool in the registry

For a custom tool to be usable in YAML, it must be registered in `IToolRegistry`:

```csharp
// Option 1 — via DI
services.AddSingleton<IBaseTool, MonCustomTool>();

// Option 2 — explicit registration at runtime
var registry = host.Services.GetRequiredService<IToolRegistry>();
await registry.RegisterToolAsync(new MonCustomTool());
```

The tool will then be accessible in YAML through its `Name`:

```yaml
agents:
  my_agent:
    tools:
      - "mon_custom_tool"  # Matches the tool's Name property
```

## Identified functional gaps

The following categories are not covered by the existing tools:

- **Structured file writing**: `docx_writer` and `xlsx_writer` cover Word and Excel, but there is no structured CSV or PDF writer. `FileWriteTool` writes plain text only (a CSV can of course be written as text).
- **Data transformation**: no ETL tool to convert between formats (CSV → JSON, XML → CSV, etc.).
- **Notifications and alerts**: e-mail is covered — `email_send`, limited to each account's allow-list (see [E-mail tools](../guides/email.md)) — and Slack by a host opt-in; there is no SMS, push or Teams tool.
- **Version control**: no native Git tool for commit, branch, diff (`shell_command` allows read-only git subcommands by default).
- **Calendar / Scheduling**: no tool to interact with calendars (Google Calendar, Outlook, etc.).
- **Cloud storage**: no tool to interact with S3, Azure Blob, GCS.
- **OAuth authentication**: no generic tool for the OAuth flows required by third-party APIs — OAuth exists only for e-mail accounts, signed in once with `orkeon email login`.
- **Image processing**: `MultiModalProcessor` exists in the infrastructure but is not exposed as a tool.
