// Orkeon Scripting DSL - built-in tools, GENERATED: do not edit (#125).
// Written by BuiltInToolTypings from every tool a run registers: the input from the request
// record of a ToolBase<TReq, TRes> (its JSON names, its required members), else from the
// tool's schema. Keys are the tool's own snake_case names, as the binding hands the object
// to the tool. A result is the response record's members the tool hands back, a member the
// serializer drops when null optional; `unknown` for a tool without a response record. The
// e-mail and drive families are declared by hand in tools.d.ts.
// Regenerate after a tool change:
//   ORKEON_REGENERATE_TYPINGS=1 dotnet test tests/hosting/Orkeon.Hosting.Tests/Orkeon.Hosting.Tests.csproj --no-build --filter "FullyQualifiedName~BuiltInToolTypingsTests"

declare global {
    /** The built-in tools, by their camelCase name: `tools` (`OrkeonTools`) extends this. */
    interface OrkeonBuiltInTools {
        /** `access_cost` - Port-access cost of members, mirroring the access-cost.py script. */
        accessCost(input?: {
            changed_files?: readonly string[] | null;
            fqns?: readonly string[] | null;
            receivers?: string | null;
            roots?: readonly string[] | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly errors: readonly string[];
            readonly report?: Record<string, unknown>;
        }>;

        /** `arcadedb_query` - Execute Cypher or SQL queries on ArcadeDB via Bolt protocol */
        arcadedbQuery(input: {
            /** Bolt protocol URI for ArcadeDB */
            bolt_uri: string;
            /** Target database name in ArcadeDB */
            database: string;
            /** Maximum number of result records to return */
            max_results?: number;
            /** Query parameters as key-value pairs */
            parameters?: Record<string, unknown> | null;
            /** Password for ArcadeDB authentication */
            password?: string | null;
            /** Cypher or SQL query to execute */
            query: string;
            /** Query language: Cypher (default) or Sql */
            query_language?: string;
            /** Username for ArcadeDB authentication */
            username?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly execution_time_ms: number;
            readonly nodes_created: number;
            readonly properties_set: number;
            readonly query_language: string;
            readonly record_count: number;
            readonly records?: readonly Record<string, unknown>[];
            readonly relationships_created: number;
        }>;

        /** `cache_search` - Semantic search over content previously stored in the RAG cache (e.g. by web_scrape with cached=true). source filter scopes by producing tool; url_filter scopes by URL substring; both are optional — empty means global search. */
        cacheSearch(input: {
            /** Minimum cosine-similarity score (0..1) for a chunk to be returned (default 0.0) */
            min_score?: number;
            /** Natural-language query to search semantically against the cached content */
            query: string;
            /** Optional source tag filter (e.g. "web_scrape"). Empty = search all sources. */
            source?: string | null;
            /** Maximum number of chunks to return (default 5) */
            top_k?: number;
            /** Optional URL substring filter — only matches chunks whose source URL contains this string. Empty = search all URLs. */
            url_filter?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly hit_count: number;
            readonly hits: readonly { readonly content: string; readonly score: number; readonly source: string; readonly title: string; readonly url?: string; }[];
            readonly query: string;
        }>;

        /** `codebase_map` - Structural map of the codebase at a given zoom level (L0-L3). */
        codebaseMap(input?: {
            include_metrics?: boolean;
            level?: string;
            max_entries?: number;
            root_fqn?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly entries: readonly { readonly children_count?: number; readonly cyclomatic?: number; readonly fqn: string; readonly kind: string; readonly language?: string; readonly lo_c?: number; readonly name: string; }[];
            readonly level: string;
            readonly totals: Record<string, unknown>;
            readonly truncated: boolean;
        }>;

        /** `codebase_search` - Hybrid (vector + BM25) search over the codebase. Returns ranked hits with FQN, score and match origin. */
        codebaseSearch(input?: {
            filter_kinds?: readonly string[];
            filter_languages?: readonly string[];
            filter_packages?: readonly string[];
            include_signature?: boolean;
            levels?: readonly string[];
            min_score?: number;
            mode?: string;
            query?: string;
            top_k?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly hits: readonly { readonly fqn: string; readonly match_origin: string; readonly score: number; readonly signature?: string; readonly summary_short?: string; }[];
            readonly refreshed_files: number;
            readonly total_candidates: number;
            readonly truncated: boolean;
        }>;

        /** `complexity_report` - Top-N methods by complexity (Cyclomatic, NestingDepth, FanOut, LoC, Callers) with median and P95. */
        complexityReport(input?: {
            metric?: string;
            root_fqn?: string | null;
            top_n?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly evaluated_count: number;
            readonly median?: number;
            readonly metric: string;
            readonly p95?: number;
            readonly top: readonly { readonly fqn: string; readonly metric: string; readonly value: number; }[];
        }>;

        /** `count_pattern` - Count regex occurrences in a file (deterministic, per-pattern). Use when verification requires exact counts rather than LLM estimation. */
        countPattern(input: {
            /** Count unique matched substrings instead of raw matches */
            distinct_matches?: boolean;
            /** Return the matched substrings themselves (capped by max_matches_returned) */
            include_matches?: boolean;
            /** Cap on the returned matches list size (only applies when include_matches=true) */
            max_matches_returned?: number | null;
            /** Apply RegexOptions.Multiline so ^/$ match line starts/ends */
            multiline?: boolean;
            /** Virtual file path to scan */
            path: string;
            /** Regex patterns to count (each evaluated independently over the full file) */
            patterns: readonly string[];
        }, ctx?: ExecutionContext): Promise<{
            readonly bom_stripped: boolean;
            readonly bytes_scanned: number;
            readonly counts: readonly { readonly count: number; readonly error?: string; readonly matches?: readonly string[]; readonly pattern: string; }[];
        }>;

        /** `csv_reader` - Read CSV files and return structured data. Supports custom delimiters and header detection. */
        csvReader(input: {
            /** Column delimiter character (default: ',') */
            delimiter?: string;
            /** Whether the CSV file has a header row (default: true) */
            has_header?: boolean;
            /** Maximum number of rows to read (default: no limit) */
            max_rows?: number | null;
            /** The file path of the CSV file to read */
            path: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly column_count: number;
            readonly headers: readonly string[];
            readonly row_count: number;
            readonly rows: readonly Record<string, unknown>[];
            readonly truncated: boolean;
        }>;

        /** `database_schema` - Inspect database schema (tables, columns, indexes, foreign keys) */
        databaseSchema(input: {
            /** Database connection string */
            connection_string: string;
            /** ADO.NET provider name: Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Microsoft.Data.Sqlite */
            provider_name: string;
            /** Schema scope: Tables (default), Columns, Indexes, ForeignKeys, or All */
            schema_scope?: string;
            /** Optional table name filter with wildcard support (e.g. 'user*', '*order*') */
            table_filter?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly database_name: string;
            readonly foreign_keys?: readonly { readonly name: string; readonly source_columns: readonly string[]; readonly source_table: string; readonly target_columns: readonly string[]; readonly target_table: string; }[];
            readonly indexes?: readonly { readonly columns: readonly string[]; readonly is_unique: boolean; readonly name: string; readonly table_name: string; readonly type?: string; }[];
            readonly provider_name: string;
            readonly tables: readonly { readonly columns: readonly { readonly data_type: string; readonly default_value?: string; readonly is_nullable: boolean; readonly is_primary_key: boolean; readonly max_length?: number; readonly name: string; }[]; readonly name: string; readonly row_count_estimate?: number; readonly schema?: string; }[];
        }>;

        /** `dependency_graph` - Dependency graph at a given scope (L1 packages, L2 modules, L3 symbols). */
        dependencyGraph(input?: {
            /** Edge kinds to follow, by name: contains, calls, imports, inherits, tests, documents, project_depends. Legacy enum names (Imports, Extends, Implements...) are still accepted. */
            edge_kinds?: readonly string[];
            include_dot?: boolean;
            include_external?: boolean;
            include_mermaid?: boolean;
            max_nodes?: number;
            /** Restrict nodes to these kinds, by name: file, project, namespace, type, member, doc_section, test. Empty keeps every node of the scope. */
            node_kinds?: readonly string[];
            root_fqn?: string | null;
            scope?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly dot?: string;
            readonly edges: readonly { readonly from: string; readonly kind: string; readonly to: string; }[];
            readonly mermaid_flowchart?: string;
            readonly metrics: Record<string, unknown>;
            readonly nodes: readonly { readonly fqn: string; readonly is_seed: boolean; readonly kind: string; readonly level: string; readonly name: string; }[];
            readonly truncated: boolean;
        }>;

        /** `directory_read` - List directory contents including files and subdirectories. Supports recursive listing and glob pattern filtering. */
        directoryRead(input: {
            /** Maximum number of entries to return (default: 500). Use a smaller value to avoid overloading context windows. */
            max_results?: number;
            /** The directory path to list */
            path: string;
            /** Glob pattern to filter results (e.g., '*.cs', '*.txt') */
            pattern?: string;
            /** Whether to list contents recursively (default: false) */
            recursive?: boolean;
        }, ctx?: ExecutionContext): Promise<{
            readonly directories: readonly { readonly name: string; readonly path: string; readonly size?: number; readonly type: string; }[];
            readonly files: readonly { readonly name: string; readonly path: string; readonly size?: number; readonly type: string; }[];
            readonly path: string;
            readonly total_directories: number;
            readonly total_entries_before_truncation?: number;
            readonly total_files: number;
            readonly truncated: boolean;
            readonly warning?: string;
        }>;

        /** `directory_search` - Perform semantic search across all files in a directory using RAG embeddings. Finds the most relevant file content matching a query. */
        directorySearch(input: {
            /** Maximum chunk size in characters (default: 500) */
            chunk_size?: number;
            /** Glob patterns separated by ';' to filter files (default: '*') */
            file_patterns?: string;
            /** Skip files larger than this size in KB (default: 512) */
            max_file_size_kb?: number;
            /** Root directory to search */
            path: string;
            /** Semantic search query */
            query: string;
            /** Whether to search subdirectories recursively (default: true) */
            recursive?: boolean;
            /** Minimum similarity threshold 0-1 (default: 0.3) */
            threshold?: number;
            /** Number of top results to return (default: 5) */
            top_k?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly files_processed: number;
            readonly files_skipped: number;
            readonly query: string;
            readonly result_count: number;
            readonly results: readonly { readonly approximate_line: number; readonly chunk_index: number; readonly content: string; readonly file_extension: string; readonly score: number; readonly source_file: string; }[];
            readonly total_chunks: number;
        }>;

        /** `docx_reader` - Read DOCX (Word) files and return structured data including text, tables, and metadata. */
        docxReader(input: {
            /** Path to the .docx file to read */
            file_path: string;
            /** Whether to include document metadata */
            include_metadata?: boolean;
            /** Whether to extract tables */
            include_tables?: boolean;
        }, ctx?: ExecutionContext): Promise<{
            readonly content: string;
            readonly metadata: Record<string, string>;
            readonly paragraph_count: number;
            readonly tables: readonly (readonly string[])[];
        }>;

        /** `docx_writer` - Create DOCX (Word) files with title, paragraphs, and bullet lists. */
        docxWriter(input: {
            /** Bullet list items (optional) */
            bullet_items?: readonly string[];
            /** Output file path for the .docx */
            file_path: string;
            /** Content paragraphs to write */
            paragraphs: readonly string[];
            /** Document title */
            title?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly file_path: string;
            readonly file_size: number;
            readonly success: boolean;
        }>;

        /** `edit_batch` - Applies node_replace and node_insert operations over several files as one transaction. Each operation is {kind, path, selector, text} plus expected_hash (required, node_replace only) or position (node_insert: 'inside', 'before' or 'after'). Operations on one file apply in order, each on the bytes the previous one produced. validate 'parse' (default) refuses the batch when an operation introduces a parse error; 'build' also runs dotnet build on the project above the edited files and restores every file when it is red. On any failure all files are left as they were. */
        editBatch(input?: {
            operations?: readonly { readonly expected_hash?: string; readonly kind: string; readonly path: string; readonly position?: string; readonly selector: string; readonly text: string; }[];
            validate?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly operations: readonly { readonly path: string; readonly range: { readonly end_byte: number; readonly end_line: number; readonly start_byte: number; readonly start_line: number; }; }[];
            readonly validation: string;
        }>;

        /** `edit_file` - Replace a string in a file when it occurs exactly once. */
        editFile(input: {
            /** Create a backup before writing */
            create_backup?: boolean;
            /** The replacement text */
            new_string: string;
            /** The text to replace */
            old_string: string;
            /** The file to edit */
            path: string;
            /** Replace every occurrence */
            replace_all?: boolean;
        }, ctx?: ExecutionContext): Promise<{
            readonly backup_path?: string;
            readonly errors?: readonly string[];
            readonly file_info: Record<string, unknown>;
            readonly path: string;
            readonly replacements: number;
            readonly success: boolean;
        }>;

        /** `file_read` - Read content from files: text, JSON, XML and the like. Parse .eml e-mail files with email_parser. */
        fileRead(input: {
            /** The text encoding to use (default: UTF-8) */
            encoding?: "UTF-8" | "ASCII" | "Unicode" | "UTF-32";
            /** Maximum number of lines to return (default: up to the end of the file); max_length caps the result further */
            limit?: number | null;
            /** Maximum number of characters to read (default: no limit). Applied after offset/limit: when it cuts a line range, only whole lines are kept and has_more is true, except that when the first line alone exceeds max_length it is cut by characters. */
            max_length?: number | null;
            /** First line to return (1-based, default 1 when limit is given). With offset/limit the file is streamed and not read beyond the range; max_length then caps the range */
            offset?: number | null;
            /** The file path to read from */
            path: string;
            /** Serve from RaggableTree index when available (path + SHA-256 match); falls back to disk otherwise. */
            use_raggable_cache?: boolean;
        }, ctx?: ExecutionContext): Promise<{
            readonly content: string;
            readonly end_line?: number;
            readonly file_info: Record<string, unknown>;
            readonly has_more: boolean;
            readonly served_from_cache: boolean;
            readonly start_line?: number;
            readonly truncated: boolean;
        }>;

        /** `file_write` - Write content to files. Can create new files or overwrite existing ones. Creates directories if needed. */
        fileWrite(input: {
            /** Whether to append to the file instead of overwriting (default: false) */
            append?: boolean;
            /** The content to write to the file */
            content: string;
            /** Whether to create a backup before writing (default: false) */
            create_backup?: boolean;
            /** The text encoding to use (default: UTF-8) */
            encoding?: "UTF-8" | "ASCII" | "Unicode" | "UTF-32";
            /** The file path to write to */
            path: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly backup_path?: string;
            readonly bytes_written: number;
            readonly errors?: readonly string[];
            readonly path: string;
            readonly success: boolean;
        }>;

        /** `flow_trace` - Trace call flow from a symbol (optionally to a target) within a depth budget. */
        flowTrace(input?: {
            direction?: string;
            edge_kinds?: readonly string[];
            from?: string;
            include_all_paths?: boolean;
            max_depth?: number;
            max_paths?: number;
            to?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly paths: readonly { readonly call_sites: readonly { readonly end_line: number; readonly sha256: string; readonly start_line: number; readonly virtual_file_path: string; }[]; readonly fqns: readonly string[]; readonly length: number; }[];
            readonly truncated: boolean;
        }>;

        /** `get_last_value` - Read the last retained payload for a given key (LastValueCache). */
        getLastValue(input: {
            /** Optional crew scope. Defaults to caller's crew. */
            crew_scope?: string | null;
            /** Cache key */
            key: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly found: boolean;
            readonly set_at?: string;
            readonly value?: unknown;
        }>;

        /** `github` - Interact with GitHub API v3: list/create issues, create/read PRs, search repos. create_pr (base, head, title, optional body) needs a token with pull-request write scope. */
        github(input: {
            /** Action to perform: list_issues, create_issue, create_pr, get_pr, search_repos, get_repo */
            action: string;
            /** Base branch for create_pr */
            base?: string | null;
            /** Body content for create operations */
            body?: string | null;
            /** Head branch for create_pr */
            head?: string | null;
            /** Issue or PR number (for get operations) */
            number?: number | null;
            /** Repository owner (GitHub username or organization) */
            owner?: string;
            /** Search query string for search operations */
            query?: string | null;
            /** Repository name */
            repo?: string;
            /** Title for create operations */
            title?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly action: string;
            readonly error?: string;
            readonly items: readonly { readonly body: string; readonly created_at: string; readonly number: number; readonly state: string; readonly title: string; readonly url?: string; }[];
            readonly success: boolean;
            readonly total_count: number;
        }>;

        /** `graph_schema` - Inspect graph database schema (vertex labels, edge labels, properties, indexes) */
        graphSchema(input: {
            /** Database name (required for ArcadeDB) */
            database?: string | null;
            /** Endpoint URI for the graph database (Bolt URI for ArcadeDB, WebSocket URI for JanusGraph) */
            endpoint: string;
            /** Graph database type: ArcadeDB or JanusGraph */
            graph_type: string;
            /** Password for authentication */
            password?: string | null;
            /** Username for authentication */
            username?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly edge_labels: readonly { readonly label: string; readonly properties: readonly string[]; readonly source_label?: string; readonly target_label?: string; }[];
            readonly graph_type: string;
            readonly indexes: readonly { readonly index_type?: string; readonly is_unique: boolean; readonly name: string; readonly property_keys: readonly string[]; }[];
            readonly property_keys: readonly { readonly cardinality?: string; readonly data_type: string; readonly name: string; }[];
            readonly vertex_labels: readonly { readonly count_estimate?: number; readonly label: string; readonly properties: readonly string[]; }[];
        }>;

        /** `http_api` - Make HTTP API calls with support for GET, POST, PUT, DELETE, PATCH methods. Returns response body, status code, and headers. */
        httpApi(input: {
            /** Optional request body (for POST, PUT, PATCH) */
            body?: string | null;
            /** Content-Type header for request body (default: application/json) */
            content_type?: string;
            /** Optional HTTP headers as key-value pairs */
            headers?: unknown;
            /** HTTP method (default: GET) */
            method?: "GET" | "POST" | "PUT" | "DELETE" | "PATCH" | "HEAD" | "OPTIONS";
            /** The URL to call */
            url: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly body: string;
            readonly header_warnings?: readonly string[];
            readonly headers: Record<string, string>;
            readonly is_success: boolean;
            readonly status_code: number;
            readonly status_description: string;
        }>;

        /** `human_input` - Ask the human user for input (text, approval, or a choice). The runtime suspends until the user responds. */
        humanInput(input: {
            /** Allowed choices when input_type=choice */
            choices?: readonly string[];
            /** Default value returned if the user gives no answer */
            default_value?: string | null;
            /** Optional virtual path the user may edit before approving. Capable providers open an inline editor when a choice whose value contains 'edit' is selected. Path semantics are owned by the caller; the provider routes I/O through IFileSystemService. */
            edit_file_path?: string | null;
            /** Input type: text (free text), approval (yes/no), choice (one of choices[]), or multi (several of choices[]) */
            input_type?: string;
            /** The prompt text shown to the user */
            prompt: string;
            /** Index into choices[] of the recommended option */
            recommended?: number | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly input_type: string;
            readonly is_approved: boolean;
            readonly response: string;
            readonly values: readonly string[];
            readonly was_default: boolean;
        }>;

        /** `image_generation` - Generate images using OpenAI DALL-E API. Supports text-to-image generation with configurable size, quality, and format. */
        imageGeneration(input: {
            /** DALL-E model to use */
            model?: string;
            /** Number of images to generate (1 for dall-e-3, 1-10 for dall-e-2) */
            number_of_images?: number;
            /** Text description of the image to generate (max 4000 chars) */
            prompt: string;
            /** Image quality: standard or hd */
            quality?: string;
            /** Response format: url or b64_json */
            response_format?: string;
            /** Optional file path to save the generated image locally */
            save_to_path?: string | null;
            /** Image size (e.g. 1024x1024, 1024x1792, 1792x1024) */
            size?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly image_count: number;
            readonly images: readonly { readonly base64?: string; readonly revised_prompt?: string; readonly saved_path?: string; readonly url?: string; }[];
            readonly model: string;
        }>;

        /** `impact_analysis` - Who is affected if we change this symbol? Direct + transitive callers, grouped by package. */
        impactAnalysis(input?: {
            direction?: string;
            max_depth?: number;
            max_nodes?: number;
            target?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly by_package: Record<string, number>;
            readonly direct_impact: readonly string[];
            readonly target: string;
            readonly top_callers: readonly { readonly fqn: string; readonly kind: string; readonly signature?: string; readonly summary_short?: string; }[];
            readonly transitive_impact: readonly string[];
            readonly truncated: boolean;
        }>;

        /** `incremental_reindex` - Reindex only files changed since a prior commit or from an explicit list. Cost is proportional to the changed files, not the repository: one file of 2 000 lines reindexes in under 2 s. */
        incrementalReindex(input?: {
            changed_files?: readonly string[];
            enrich_with_llm?: boolean;
            from_commit?: string | null;
            languages?: readonly string[];
            root_path?: string;
            to_commit?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly edge_count: number;
            readonly elapsed: string;
            readonly embedding_stats?: Record<string, unknown>;
            readonly errors: readonly string[];
            readonly file_count: number;
            readonly filter_stats: Record<string, unknown>;
            readonly index_id: string;
            readonly node_count: number;
            readonly statement_count: number;
        }>;

        /** `index_codebase` - Scan the codebase and build the full RaggableTree index. */
        indexCodebase(input?: {
            embedding_model?: string;
            enrich_with_llm?: boolean;
            exclude?: readonly string[];
            languages?: readonly string[];
            respect_gitignore?: boolean;
            root_alias?: string;
            root_path?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly edge_count: number;
            readonly elapsed: string;
            readonly embedding_stats?: Record<string, unknown>;
            readonly errors: readonly string[];
            readonly file_count: number;
            readonly filter_stats: Record<string, unknown>;
            readonly index_id: string;
            readonly node_count: number;
            readonly statement_count: number;
        }>;

        /** `index_status` - List every virtual root currently indexed in the RaggableTree store. */
        indexStatus(input?: Record<string, never>, ctx?: ExecutionContext): Promise<{
            readonly dirty_count: number;
            readonly dirty_paths: readonly string[];
            readonly roots: readonly { readonly edge_count: number; readonly indexed_at: string; readonly language_summary?: string; readonly node_count: number; readonly virtual_root: string; }[];
        }>;

        /** `is_path_indexed` - Check whether a virtual path is covered by any indexed root (longest match wins). */
        isPathIndexed(input?: {
            virtual_path?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly indexed: boolean;
            readonly indexed_at?: string;
            readonly root_parent?: string;
        }>;

        /** `janusgraph_query` - Execute Gremlin traversals on JanusGraph databases */
        janusgraphQuery(input: {
            /** Parameter bindings for the traversal as key-value pairs */
            bindings?: Record<string, unknown> | null;
            /** Gremlin WebSocket endpoint URI */
            gremlin_endpoint: string;
            /** Maximum number of results to return */
            max_results?: number;
            /** Timeout in milliseconds for traversal execution */
            timeout_ms?: number;
            /** Gremlin traversal string to execute */
            traversal: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly execution_time_ms: number;
            readonly result_count: number;
            readonly result_type: string;
            readonly results?: readonly Record<string, unknown>[];
        }>;

        /** `json_tool` - JSON manipulation tool supporting parse, query, and format operations. */
        jsonTool(input: {
            /** The JSON string to process */
            input: string;
            /** The operation to perform: parse, query, or format */
            operation: string;
            /** Dot-notation path for query operation (e.g., 'data.items[0].name') */
            query?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly formatted?: string;
            readonly info?: Record<string, unknown>;
            readonly length?: number;
            readonly parsed?: boolean;
            readonly query?: string;
            readonly type?: string;
            readonly value?: unknown;
        }>;

        /** `list_mounts` - Lists all available file system mounts with their virtual paths and access rights. */
        listMounts(input?: Record<string, never>, ctx?: ExecutionContext): Promise<{
            readonly mounts: readonly { readonly access: string; readonly overrides: readonly { readonly access: string; readonly virtual_path: string; }[]; readonly virtual_path: string; }[];
            readonly success: boolean;
        }>;

        /** `local_embed_text` - Generate embeddings for one or more texts using the on-device BGE-micro-v2 model. */
        localEmbedText(input: {
            /** Texts to embed. One vector is returned per entry, in the same order. */
            inputs: readonly string[];
            /** Optional model alias (e.g. "bge-micro-v2"). Ignored when the host has a single local model registered. */
            model?: string | null;
            /** Hint for the desired vector dimensionality. Honoured only if the underlying model supports projection / truncation. Otherwise ignored and the model's native dimension is returned. */
            requested_dimensions?: number | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly dimensions: number;
            readonly embeddings: readonly (readonly number[])[];
            readonly model: string;
            readonly truncated_count: number;
        }>;

        /** `mariadb_query` - Execute SQL queries on MariaDB databases */
        mariadbQuery(input: {
            /** Database connection string */
            connection_string: string;
            /** Maximum number of rows to return for SELECT queries */
            max_rows?: number;
            /** Optional named parameters for parameterized queries (e.g. {"active": true}) */
            parameters?: Record<string, unknown> | null;
            /** ADO.NET provider name: Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Microsoft.Data.Sqlite */
            provider_name: string;
            /** SQL query to execute */
            query: string;
            /** Type of query: Select (returns rows), Execute (returns affected rows count), or Scalar (returns single value) */
            query_type?: string;
            /** Query timeout in seconds */
            timeout_seconds?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly affected_rows?: number;
            readonly column_count?: number;
            readonly columns?: readonly string[];
            readonly execution_time_ms: number;
            readonly provider_name: string;
            readonly query_type: string;
            readonly row_count?: number;
            readonly rows?: readonly Record<string, unknown>[];
            readonly scalar_result?: unknown;
        }>;

        /** `mdx_search` - Perform semantic search within Markdown (.md, .mdx) files using RAG embeddings. Strips frontmatter and JSX before searching. */
        mdxSearch(input: {
            /** Maximum chunk size in characters (default: 500) */
            chunk_size?: number;
            /** File or directory path to search within */
            path: string;
            /** Semantic search query */
            query: string;
            /** Minimum similarity threshold 0-1 (default: 0.3) */
            threshold?: number;
            /** Number of top results to return (default: 5) */
            top_k?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly files_processed: number;
            readonly query: string;
            readonly result_count: number;
            readonly results: readonly { readonly approximate_line: number; readonly chunk_index: number; readonly content: string; readonly score: number; readonly source_file: string; }[];
            readonly total_chunks: number;
        }>;

        /** `memory_store` - List, add, delete, and get typed memory entries (user/project/feedback/reference). */
        memoryStore(input?: {
            category?: string | null;
            content?: string | null;
            id?: number;
            operation?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly entries?: readonly unknown[];
            readonly entry?: unknown;
            readonly error?: string;
            readonly id?: number;
            readonly success: boolean;
        }>;

        /** `mongodb_query` - Execute operations on MongoDB databases (Find, Aggregate, CRUD) */
        mongodbQuery(input: {
            /** Collection name to operate on */
            collection: string;
            /** MongoDB connection string */
            connection_string: string;
            /** Database name to operate on */
            database: string;
            /** JSON document to insert for InsertOne operations */
            document?: string | null;
            /** JSON filter document for Find/Update/Delete/Count/Distinct operations */
            filter?: string | null;
            /** Maximum number of documents to return (default 100) */
            limit?: number;
            /** Operation to perform: Find, Aggregate, InsertOne, UpdateOne, DeleteOne, Count, Distinct */
            operation: string;
            /** JSON array of pipeline stages for Aggregate operations */
            pipeline?: string | null;
            /** JSON projection document for Find operations */
            projection?: string | null;
            /** Number of documents to skip */
            skip?: number;
            /** JSON sort document for Find operations */
            sort?: string | null;
            /** JSON update document for UpdateOne operations */
            update?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly distinct_values?: readonly string[];
            readonly document_count: number;
            readonly documents?: readonly string[];
            readonly execution_time_ms: number;
            readonly inserted_id?: string;
            readonly matched_count: number;
            readonly modified_count: number;
            readonly operation: string;
        }>;

        /** `mongodb_schema` - Inspect MongoDB database schema by sampling documents */
        mongodbSchema(input: {
            /** Optional collection name to inspect (if omitted, inspects all collections) */
            collection?: string | null;
            /** MongoDB connection string */
            connection_string: string;
            /** Database name to inspect */
            database: string;
            /** Number of documents to sample for schema inference (default 100) */
            sample_size?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly collections: readonly { readonly document_count: number; readonly indexes: readonly { readonly is_sparse: boolean; readonly is_unique: boolean; readonly keys: Record<string, number>; readonly name: string; }[]; readonly inferred_fields: readonly { readonly bson_type: string; readonly frequency: number; readonly is_indexed: boolean; readonly name: string; readonly sample_values: readonly string[]; }[]; readonly name: string; }[];
            readonly database_name: string;
        }>;

        /** `mysql_query` - Execute SQL queries on MySQL databases */
        mysqlQuery(input: {
            /** Database connection string */
            connection_string: string;
            /** Maximum number of rows to return for SELECT queries */
            max_rows?: number;
            /** Optional named parameters for parameterized queries (e.g. {"active": true}) */
            parameters?: Record<string, unknown> | null;
            /** ADO.NET provider name: Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Microsoft.Data.Sqlite */
            provider_name: string;
            /** SQL query to execute */
            query: string;
            /** Type of query: Select (returns rows), Execute (returns affected rows count), or Scalar (returns single value) */
            query_type?: string;
            /** Query timeout in seconds */
            timeout_seconds?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly affected_rows?: number;
            readonly column_count?: number;
            readonly columns?: readonly string[];
            readonly execution_time_ms: number;
            readonly provider_name: string;
            readonly query_type: string;
            readonly row_count?: number;
            readonly rows?: readonly Record<string, unknown>[];
            readonly scalar_result?: unknown;
        }>;

        /** `node_insert` - Inserts text relative to an anchor node: position 'inside' appends to a type or namespace body (an enum member gets its separating comma) or to the end of a Markdown section, 'before' puts it above the anchor's leading comments and attributes, 'after' below the anchor. There is no expected_hash: no anchor hash is required for before/after, the anchor is only a location. The text is re-indented line by line to the siblings' indentation (tabs or spaces as the file does); the insert is refused when it introduces a parse error that was not there before, the file is then untouched. */
        nodeInsert(input?: {
            path?: string;
            position?: string;
            selector?: string;
            text?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly new_hash: string;
            readonly range: Record<string, unknown>;
        }>;

        /** `node_outline` - Outline of a file's members. */
        nodeOutline(input?: {
            path?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly members: readonly { readonly end_byte: number; readonly end_line: number; readonly kind: string; readonly selector: string; readonly start_byte: number; readonly start_line: number; }[];
        }>;

        /** `node_read` - Reads one member of a file by selector: text, range and SHA-256, uncut. */
        nodeRead(input: {
            path?: string;
            /** Member to read. Readable form: Ns.Type.Member(paramTypes), e.g. Demo.Calc.Add(int). Markdown form: Section/Sub; sibling headings of the same name take a 1-based ordinal, e.g. Section/Notes[2]. Fallback form: @ts:<node-path>, the tree-sitter path root/<index>/<kind>/... returned when a readable selector is ambiguous. */
            selector: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly end_byte: number;
            readonly end_line: number;
            readonly kind: string;
            readonly path: string;
            readonly selector: string;
            readonly sha256: string;
            readonly start_byte: number;
            readonly start_line: number;
            readonly text: string;
        }>;

        /** `node_replace` - Replaces one node of a file by selector and expected SHA-256 (from node_read): refused when the hash is stale or when the replacement introduces a parse error that was not there before; the file is then untouched. The replaced span includes the node's leading comments and attributes, so a replacement that omits them removes them; a Markdown section replaces its sub-sections too. Line endings are normalised to the file's. */
        nodeReplace(input?: {
            expected_hash?: string;
            path?: string;
            selector?: string;
            text?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly new_hash: string;
            readonly range: Record<string, unknown>;
        }>;

        /** `package_summary` - Details about a package (L1): file count, symbol count, exports, dependencies. */
        packageSummary(input?: {
            fqn?: string;
            include_dependencies?: boolean;
            include_public_exports?: boolean;
            max_exports?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly dependencies: readonly string[];
            readonly entry_points: readonly string[];
            readonly file_count: number;
            readonly fqn: string;
            readonly name?: string;
            readonly public_exports: readonly string[];
            readonly summary_short?: string;
            readonly symbol_count: number;
            readonly truncated: boolean;
        }>;

        /** `pdf_reader` - Extract text content from PDF files. Supports page range selection. */
        pdfReader(input: {
            /** Optional page range (e.g., '1-5', '1,3,5', '2-'). Default: all pages. */
            page_range?: string | null;
            /** The file path of the PDF to read */
            path: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly content: string;
            readonly page_range: string;
            readonly pages: readonly { readonly height: number; readonly page_number: number; readonly text: string; readonly width: number; }[];
            readonly pages_read: number;
            readonly total_pages: number;
        }>;

        /** `pdf_search` - Perform semantic search within PDF documents using RAG embeddings. */
        pdfSearch(input: {
            /** Chunk size in characters for text splitting */
            chunk_size?: number;
            /** Optional page range (e.g., '1-5', '1,3,5', '2-'). Default: all pages. */
            page_range?: string | null;
            /** Path to a PDF file or a directory of PDFs */
            path: string;
            /** The semantic search query */
            query: string;
            /** Minimum cosine similarity threshold (0.0 to 1.0) */
            threshold?: number;
            /** Maximum number of results to return */
            top_k?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly files_processed: number;
            readonly query: string;
            readonly result_count: number;
            readonly results: readonly { readonly chunk_index: number; readonly content: string; readonly page_number: number; readonly score: number; readonly source_file: string; }[];
            readonly total_chunks: number;
            readonly total_pages: number;
        }>;

        /** `post_message` - Fire-and-forget a message to a mailbox (agent://, crew://, topic://, or client://). */
        postMessage(input: {
            /** Optional metadata key/value pairs, copied into the envelope the recipient reads */
            metadata?: Record<string, string> | null;
            /** JSON payload */
            payload?: unknown;
            /** Mailbox URI (agent://, crew://, topic://, or client:// for an external client such as Studio) */
            target_mailbox: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly message_id: string;
            readonly posted_at: string;
        }>;

        /** `postgres_query` - Execute SQL queries on PostgreSQL databases */
        postgresQuery(input: {
            /** Database connection string */
            connection_string: string;
            /** Maximum number of rows to return for SELECT queries */
            max_rows?: number;
            /** Optional named parameters for parameterized queries (e.g. {"active": true}) */
            parameters?: Record<string, unknown> | null;
            /** ADO.NET provider name: Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Microsoft.Data.Sqlite */
            provider_name: string;
            /** SQL query to execute */
            query: string;
            /** Type of query: Select (returns rows), Execute (returns affected rows count), or Scalar (returns single value) */
            query_type?: string;
            /** Query timeout in seconds */
            timeout_seconds?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly affected_rows?: number;
            readonly column_count?: number;
            readonly columns?: readonly string[];
            readonly execution_time_ms: number;
            readonly provider_name: string;
            readonly query_type: string;
            readonly row_count?: number;
            readonly rows?: readonly Record<string, unknown>[];
            readonly scalar_result?: unknown;
        }>;

        /** `progress_report` - Report the progress of a long-running scripted operation (label, step/total or percent, message). */
        progressReport(input?: {
            done?: boolean;
            label?: string;
            message?: string | null;
            percent?: number | null;
            step?: number | null;
            total?: number | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly acknowledged: boolean;
        }>;

        /** `publish_event` - Publish an event on a topic (broadcast 1→N). Optional crew scope and metadata. */
        publishEvent(input: {
            /** Cache key when retain_as_last_value is true */
            last_value_key?: string | null;
            /** Optional metadata key/value pairs */
            metadata?: Record<string, string> | null;
            /** JSON payload to publish */
            payload?: unknown;
            /** Retain the message in the LastValueCache (requires last_value_key) */
            retain_as_last_value?: boolean;
            /** Crew scope. Omit for global broadcast. */
            target_crew_id?: string | null;
            /** Topic name (must not start with _system.) */
            topic: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly event_id: string;
            readonly published_at: string;
        }>;

        /** `rag_eval` - Evaluate a RAG knowledge collection against a golden dataset (YAML): recall@k, precision@k, MRR, groundedness and answer-relevance (judge mode labelled: llm or heuristic). Writes markdown/JSON reports and returns the aggregate summary. */
        ragEval(input?: {
            collection?: string | null;
            compare?: unknown;
            dataset?: string;
            k?: number;
            profile?: string | null;
            reindex?: boolean;
            use_llm_judge?: boolean;
        }, ctx?: ExecutionContext): Promise<{
            readonly cases: readonly { readonly case_id: string; readonly profile: string; readonly recall_at_k?: number; readonly reciprocal_rank?: number; }[];
            readonly metrics: readonly { readonly answer_relevance?: number; readonly groundedness?: number; readonly judge: string; readonly k: number; readonly mrr?: number; readonly precision_at_k?: number; readonly profile: string; readonly recall_at_k?: number; }[];
            readonly reports: readonly string[];
        }>;

        /** `rag_ingest` - Ingest documents into a knowledge collection so they become searchable via rag_search. Accepts file paths, glob patterns and http(s) addresses; unchanged sources are skipped (incremental). */
        ragIngest(input: {
            /** Optional chunking strategy name (recursive, sentence, structural, semantic); pipeline default when omitted */
            chunking_strategy?: string;
            /** Target collection in the document store */
            collection: string;
            /** Delete the chunks and manifest entries of previously ingested sources absent from 'sources' */
            prune?: boolean;
            /** Force a full reindex of the collection (required after an embedding model change) */
            reindex?: boolean;
            /** File paths, glob patterns or http(s) addresses to ingest (e.g. '/workspace/docs/**\/*.md') */
            sources: readonly unknown[];
        }, ctx?: ExecutionContext): Promise<unknown>;

        /** `rag_search` - Search knowledge bases and retrieve answers grounded in documents. Use this when you need factual information from the agent's knowledge sources. */
        ragSearch(input?: {
            collection?: string | null;
            question?: string;
            top_k?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly answer: string;
            readonly citations: readonly { readonly chunk_id: string; readonly content?: string; readonly document_id?: string; readonly end_offset: number; readonly marker: number; readonly score: number; readonly snippet?: string; readonly source_id: string; readonly start_offset: number; }[];
        }>;

        /** `receive_message` - Pull the next message from a mailbox (default: current agent). Only mailboxes of the calling crew are readable — reading is destructive, and a mailbox belongs to its owner. Requires exactly one of timeout_ms / wait_forever. */
        receiveMessage(input?: {
            /** Optional mailbox URI, restricted to the calling crew's own mailboxes (agent:// or crew:// of this crew). Defaults to the calling agent's mailbox. */
            mailbox?: string | null;
            /** Wait timeout in ms (mutually exclusive with wait_forever) */
            timeout_ms?: number | null;
            /** Wait indefinitely (mutually exclusive with timeout_ms) */
            wait_forever?: boolean;
        }, ctx?: ExecutionContext): Promise<{
            readonly message?: Record<string, unknown>;
            readonly timed_out: boolean;
        }>;

        /** `relational_database_query` - Execute SQL queries on relational databases (SQL Server, PostgreSQL, MySQL, MariaDB, SQLite) */
        relationalDatabaseQuery(input: {
            /** Database connection string */
            connection_string: string;
            /** Maximum number of rows to return for SELECT queries */
            max_rows?: number;
            /** Optional named parameters for parameterized queries (e.g. {"active": true}) */
            parameters?: Record<string, unknown> | null;
            /** ADO.NET provider name: Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Microsoft.Data.Sqlite */
            provider_name: string;
            /** SQL query to execute */
            query: string;
            /** Type of query: Select (returns rows), Execute (returns affected rows count), or Scalar (returns single value) */
            query_type?: string;
            /** Query timeout in seconds */
            timeout_seconds?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly affected_rows?: number;
            readonly column_count?: number;
            readonly columns?: readonly string[];
            readonly execution_time_ms: number;
            readonly provider_name: string;
            readonly query_type: string;
            readonly row_count?: number;
            readonly rows?: readonly Record<string, unknown>[];
            readonly scalar_result?: unknown;
        }>;

        /** `reply_to` - Reply to a pending request identified by correlation_id. */
        replyTo(input: {
            /** Correlation id from the original Send/request envelope */
            correlation_id: string;
            /** JSON payload to return */
            payload?: unknown;
        }, ctx?: ExecutionContext): Promise<{
            readonly replied_at: string;
        }>;

        /** `scrape_element` - Extract targeted elements from a web page using CSS selectors. Returns text, optional HTML, and attributes for matched elements. */
        scrapeElement(input: {
            /** CSS selector to find elements */
            css_selector: string;
            /** Include HTML attributes for each element */
            extract_attributes?: boolean;
            /** Include raw inner HTML for each element */
            include_html?: boolean;
            /** Maximum number of elements to return */
            max_elements?: number;
            /** The URL of the web page to scrape */
            url: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly css_selector: string;
            readonly element_count: number;
            readonly elements: readonly { readonly attributes?: Record<string, string>; readonly html?: string; readonly index: number; readonly text: string; }[];
            readonly returned_count: number;
            readonly url?: string;
        }>;

        /** `send_request` - Send a request to a mailbox and wait for a correlated reply (timeout_ms required). */
        sendRequest(input: {
            /** Optional metadata key/value pairs, copied into the envelope the responder reads */
            metadata?: Record<string, string> | null;
            /** JSON payload for the request */
            payload?: unknown;
            /** Mailbox URI of the responder */
            target_mailbox: string;
            /** Timeout in milliseconds. Must be strictly positive. Forever not allowed. */
            timeout_ms?: number | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly response_payload?: unknown;
        }>;

        /** `session_cost` - Report cumulative session cost in USD, token usage, and a per-model breakdown. */
        sessionCost(input?: Record<string, never>, ctx?: ExecutionContext): Promise<{
            readonly by_model: Record<string, string>;
            readonly total_calls: number;
            readonly total_cost_usd: string;
            readonly total_tokens: number;
        }>;

        /** `session_snip` - Immediately truncate the conversation to a minimal window without analysis. */
        sessionSnip(input?: {
            retain_count?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly removed_count: number;
            readonly retained_count: number;
        }>;

        /** `session_stats` - Report full session telemetry: message count, estimated tokens, cost, and call totals. */
        sessionStats(input?: Record<string, never>, ctx?: ExecutionContext): Promise<{
            readonly by_model: Record<string, string>;
            readonly by_model_tokens: readonly { readonly cache_read_tokens: number; readonly cache_write_tokens: number; readonly completion_tokens: number; readonly model: string; readonly prompt_tokens: number; }[];
            readonly estimated_tokens: number;
            readonly message_count: number;
            readonly total_calls: number;
            readonly total_cost_usd: string;
            readonly total_tokens: number;
        }>;

        /** `session_store` - Read, write, truncate, and annotate the current session conversation buffer and metadata. */
        sessionStore(input?: {
            key?: string | null;
            messages?: readonly unknown[] | null;
            note?: string | null;
            operation?: string;
            retain_count?: number;
            title?: string | null;
            value?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly messages?: readonly unknown[];
            readonly metadata?: unknown;
            readonly removed_count: number;
            readonly success: boolean;
            readonly value?: string;
        }>;

        /** `shell_command` - Execute shell commands with security controls. Supports allowlist/blocklist, timeout, and output capture. */
        shellCommand(input: {
            /** run (default): run the command and wait; start_job: start it detached, output to a log file; job_status: running or exit code of a job; job_tail: last lines of a job's log */
            action?: string;
            /** Shell command to execute; required by the run and start_job actions */
            command: string;
            /** Id returned by start_job, for job_status and job_tail */
            job_id?: string;
            /** Lines job_tail returns from the end of the log */
            tail_lines?: number;
            /** Timeout in seconds */
            timeout_seconds?: number;
            /** Virtual working directory for command execution, e.g. /workspace. Defaults to the first mounted directory the command may use: readable for a read-only command, writable for any other. */
            working_directory?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly completed: boolean;
            readonly exit_code: number;
            readonly job_id: string;
            readonly output_path: string;
            readonly status: string;
            readonly stderr: string;
            readonly stdout: string;
            readonly tail: string;
        }>;

        /** `sqlserver_query` - Execute SQL queries on Microsoft SQL Server databases */
        sqlserverQuery(input: {
            /** Database connection string */
            connection_string: string;
            /** Maximum number of rows to return for SELECT queries */
            max_rows?: number;
            /** Optional named parameters for parameterized queries (e.g. {"active": true}) */
            parameters?: Record<string, unknown> | null;
            /** ADO.NET provider name: Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Microsoft.Data.Sqlite */
            provider_name: string;
            /** SQL query to execute */
            query: string;
            /** Type of query: Select (returns rows), Execute (returns affected rows count), or Scalar (returns single value) */
            query_type?: string;
            /** Query timeout in seconds */
            timeout_seconds?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly affected_rows?: number;
            readonly column_count?: number;
            readonly columns?: readonly string[];
            readonly execution_time_ms: number;
            readonly provider_name: string;
            readonly query_type: string;
            readonly row_count?: number;
            readonly rows?: readonly Record<string, unknown>[];
            readonly scalar_result?: unknown;
        }>;

        /** `statement_query` - Query L4 statements by kind, parent FQN, or semantic similarity. */
        statementQuery(input?: {
            kinds?: readonly string[];
            parent_fqns?: readonly string[];
            semantic_query?: string | null;
            top_k?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly hits: readonly { readonly condition?: string; readonly end_line?: number; readonly expression?: string; readonly kind: string; readonly parent_fqn: string; readonly score?: number; readonly start_line: number; readonly statement_id: string; }[];
            readonly truncated: boolean;
        }>;

        /** `structural_rewrite` - Applies a rule's fix template to the matching files; dry run by default. */
        structuralRewrite(input?: {
            dry_run?: boolean;
            fix?: string | null;
            glob?: string | null;
            language?: string;
            max_files?: number;
            paths?: readonly string[];
            pattern?: string | null;
            rule?: unknown;
            rule_file?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly files: readonly { readonly applied: number; readonly diff: string; readonly matches: number; readonly path: string; readonly refused_reason?: string; readonly skipped: readonly string[]; }[];
            readonly files_scanned: number;
            readonly truncated: boolean;
        }>;

        /** `structural_search` - Searches C#, TypeScript or Markdown files for a code pattern parsed with the target grammar: $NAME matches one node and captures its text, $$$NAME matches zero or more sibling nodes. Returns path, line, column, text, captures and the enclosing symbol of each match. */
        structuralSearch(input?: {
            glob?: string | null;
            language?: string;
            max_files?: number;
            max_results?: number;
            paths?: readonly string[];
            /** Code pattern with $NAME / $$$NAME metavariables. Give exactly one of pattern, rule or rule_file. */
            pattern?: string | null;
            /** Structural rule object. Keys: kind, pattern, regex, field, inside, has, not, all, any, stopBy. Any other key is refused by name. */
            rule?: unknown;
            /** Virtual path of a YAML rule file (id, language, rule, message, severity, fix) under a mounted root. */
            rule_file?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly files_scanned: number;
            readonly matches: readonly { readonly captures: Record<string, string>; readonly column: number; readonly enclosing_symbol?: string; readonly line: number; readonly path: string; readonly text: string; }[];
            readonly truncated: boolean;
        }>;

        /** `sub_graph` - Sub-graph expansion around seeds, bounded by depth and node count. */
        subGraph(input?: {
            depth?: number;
            direction?: string;
            /** Edge kinds to follow, by name: contains, calls, imports, inherits, tests, documents, project_depends. Legacy enum names (Imports, Extends, Implements...) are still accepted. */
            edge_kinds?: readonly string[];
            include_mermaid?: boolean;
            max_nodes?: number;
            /** Keep only expanded nodes of these kinds, by name: file, project, namespace, type, member, doc_section, test. Empty keeps every node. */
            node_kinds?: readonly string[];
            seeds?: readonly string[];
        }, ctx?: ExecutionContext): Promise<{
            readonly edges: readonly { readonly from: string; readonly kind: string; readonly to: string; }[];
            readonly mermaid_flowchart?: string;
            readonly nodes: readonly { readonly fqn: string; readonly is_seed: boolean; readonly kind: string; readonly level: string; readonly name: string; }[];
            readonly truncated: boolean;
        }>;

        /** `symbol_detail` - Expanded detail for an L3 symbol: signature, doc, body metrics, members, callers, callees, statements. */
        symbolDetail(input?: {
            expand?: string;
            fqn?: string;
            include_body_metrics?: boolean;
            include_doc?: boolean;
            include_signature?: boolean;
            max_children?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly body?: Record<string, unknown>;
            readonly doc_comment?: string;
            readonly extends: readonly string[];
            readonly fqn: string;
            readonly implements: readonly string[];
            readonly kind?: string;
            readonly level?: string;
            readonly members: readonly { readonly fqn: string; readonly kind: string; readonly signature?: string; readonly summary_short?: string; }[];
            readonly name?: string;
            readonly signature?: string;
            readonly statements: readonly { readonly children: readonly Record<string, unknown>[]; readonly condition?: string; readonly depth: number; readonly embedding?: Record<string, unknown>; readonly end_line?: number; readonly expression: string; readonly id: string; readonly in_lambda: boolean; readonly in_loop: boolean; readonly kind: string; readonly parent_symbol_id: string; readonly receiver?: string; readonly references: readonly { readonly kind: string; readonly name: string; readonly resolved_id?: string; }[]; readonly start_line: number; }[];
            readonly summary_short?: string;
            readonly top_callees: readonly { readonly fqn: string; readonly kind: string; readonly signature?: string; readonly summary_short?: string; }[];
            readonly top_callers: readonly { readonly fqn: string; readonly kind: string; readonly signature?: string; readonly summary_short?: string; }[];
            readonly truncated: boolean;
        }>;

        /** `symbol_source` - Deterministic source citation for a symbol. Returns file path, lines, code, SHA-256, and stable flag. */
        symbolSource(input?: {
            fqn?: string;
            max_lines?: number;
            mode?: string;
            statement_id?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly end_line: number;
            readonly fqn: string;
            readonly language: string;
            readonly markdown_ready_block: string;
            readonly sha256: string;
            readonly short_sha: string;
            readonly source: string;
            readonly stable: boolean;
            readonly start_line: number;
            readonly truncated: boolean;
            readonly virtual_file_path: string;
        }>;

        /** `token_budget` - Return the current model's context window size, used tokens, and available budget. */
        tokenBudget(input?: Record<string, never>, ctx?: ExecutionContext): Promise<{
            readonly available_tokens: number;
            readonly context_window_tokens: number;
            readonly model: string;
            readonly used_tokens: number;
        }>;

        /** `txt_search` - Perform semantic search within plain text (.txt) files using RAG embeddings. */
        txtSearch(input: {
            /** Maximum chunk size in characters (default: 500) */
            chunk_size?: number;
            /** File or directory path to search within */
            path: string;
            /** Semantic search query */
            query: string;
            /** Minimum similarity threshold 0-1 (default: 0.3) */
            threshold?: number;
            /** Number of top results to return (default: 5) */
            top_k?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly files_processed: number;
            readonly query: string;
            readonly result_count: number;
            readonly results: readonly { readonly approximate_line: number; readonly chunk_index: number; readonly content: string; readonly score: number; readonly source_file: string; }[];
            readonly total_chunks: number;
        }>;

        /** `wait_for_event` - Wait for the next event on a topic. Requires exactly one of timeout_ms / wait_forever. */
        waitForEvent(input: {
            /** Optional metadata match constraints */
            metadata_match?: Record<string, string> | null;
            /** Wait timeout in ms (mutually exclusive with wait_forever) */
            timeout_ms?: number | null;
            /** Topic to wait on */
            topic: string;
            /** Wait indefinitely (mutually exclusive with timeout_ms) */
            wait_forever?: boolean;
        }, ctx?: ExecutionContext): Promise<{
            readonly message?: Record<string, unknown>;
            readonly timed_out: boolean;
        }>;

        /** `web_scrape` - Scrape a web page. cached=false returns full text; cached=true chunks+embeds+stores the page in the RAG cache and returns only a short summary (use cache_search to retrieve chunks). */
        webScrape(input: {
            /** When true, chunk + embed + store in the RAG cache and return a short summary instead of full content. Query chunks back via cache_search. */
            cached?: boolean;
            /** Maximum chunk size in characters when cached=true (default 1500) */
            chunk_size?: number;
            /** Optional CSS/XPath selector to filter content */
            selector?: string | null;
            /** The URL of the web page to scrape */
            url: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly cached: boolean;
            readonly content: string;
            readonly content_length: number;
            readonly key_prefix: string;
            readonly selector_matched: boolean;
            readonly summary: string;
            readonly title: string;
            readonly total_chunks: number;
            readonly url?: string;
        }>;

        /** `web_search` - Search the web using the Tavily Search API. Returns relevant results with titles, URLs, content snippets, and scores. */
        webSearch(input: {
            /** Maximum number of results to return (default: 5) */
            max_results?: number;
            /** The search query text */
            query: string;
            /** Search depth: basic or advanced (default: basic) */
            search_depth?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly query: string;
            readonly result_count: number;
            readonly results: readonly { readonly content: string; readonly score: number; readonly title: string; readonly url?: string; }[];
        }>;

        /** `xlsx_reader` - Read Excel (.xlsx) files and return structured data including sheets, rows, columns, and metadata. */
        xlsxReader(input: {
            /** Path to the .xlsx file to read */
            file_path: string;
            /** Whether to treat first row as header */
            has_header_row?: boolean;
            /** Whether to include workbook metadata */
            include_metadata?: boolean;
            /** Specific sheet to read (empty = all sheets) */
            sheet_name?: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly metadata: Record<string, string>;
            readonly sheet_count: number;
            readonly sheet_names: readonly string[];
            readonly sheets: readonly { readonly column_count: number; readonly headers: readonly string[]; readonly name: string; readonly row_count: number; readonly rows: readonly (readonly string[])[]; }[];
        }>;

        /** `xlsx_writer` - Create or update Excel (.xlsx) files with structured data including multiple sheets, headers, and rows. */
        xlsxWriter(input: {
            /** Whether to append sheets to an existing file */
            append_to_existing?: boolean;
            /** Output file path for the .xlsx */
            file_path: string;
            /** Sheets to write (each with name, headers, rows) */
            sheets: readonly { readonly headers: readonly string[]; readonly name: string; readonly rows: readonly (readonly string[])[]; }[];
        }, ctx?: ExecutionContext): Promise<{
            readonly file_path: string;
            readonly file_size: number;
            readonly sheet_count: number;
            readonly success: boolean;
        }>;

        /** `xml_parser` - Parse XML data from files or strings with XPath query support. */
        xmlParser(input: {
            /** File path to an XML file, or an XML string */
            input: string;
            /** Operation to perform: Parse or Query */
            operation?: string;
            /** Optional XPath expression to query specific elements */
            xpath?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly attribute_count?: number;
            readonly attributes?: Record<string, unknown>;
            readonly child_count?: number;
            readonly children?: readonly { readonly attributes: Record<string, unknown>; readonly name: string; readonly value: string; }[];
            readonly declaration?: string;
            readonly match_count?: number;
            readonly matches?: readonly { readonly attributes: Record<string, unknown>; readonly name: string; readonly value: string; }[];
            readonly namespace?: string;
            readonly root_element?: string;
            readonly source?: string;
            readonly xpath?: string;
        }>;
    }
}

export { };
