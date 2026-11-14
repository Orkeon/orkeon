// Orkeon Scripting DSL - built-in tools, GENERATED: do not edit (#125).
// Written by BuiltInToolTypings from every tool a run registers: the input from the schema
// the tool publishes (its parameters, required flags, types, enums and descriptions), the
// same the model reads; `| null` where the request record takes a null. Keys are the tool's
// own snake_case names, as the binding hands the object to the tool. A result is the response record's members the tool hands back, a member the
// serializer drops when null optional; `unknown` for a tool without a response record. The
// e-mail and drive families are declared by hand in tools.d.ts.
// Regenerate after a tool change:
//   ORKEON_REGENERATE_TYPINGS=1 dotnet test tests/hosting/Orkeon.Hosting.Tests/Orkeon.Hosting.Tests.csproj --no-build --filter "FullyQualifiedName~BuiltInToolTypingsTests"

declare global {
    /** The built-in tools, by their camelCase name: `tools` (`OrkeonTools`) extends this. */
    interface OrkeonBuiltInTools {
        /** `access_cost` - Port-access cost of members, mirroring the access-cost.py script. */
        accessCost(input?: {
            /** Virtual paths of changed files whose members are costed. */
            changed_files?: readonly string[] | null;
            /** FQNs of the members to cost. */
            fqns?: readonly string[] | null;
            /** Regex of the receiver names that count as a port access; the default list when omitted. */
            receivers?: string | null;
            /** FQN prefixes the members are kept under. */
            roots?: readonly string[] | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly errors: readonly string[];
            readonly report?: { readonly cost: { readonly lambdas: number; readonly loops: number; readonly memory_filters: number; readonly reads: number; readonly writes: number; }; readonly members: readonly { readonly cost: { readonly lambdas: number; readonly loops: number; readonly memory_filters: number; readonly reads: number; readonly writes: number; }; readonly in_memory_filters: readonly { readonly line: number; readonly method: string; }[]; readonly member: string; readonly port_calls: readonly { readonly in_lambda: boolean; readonly in_loop: boolean; readonly line: number; readonly method: string; readonly receiver: string; }[]; }[]; };
        }>;

        /** `affected_tests` - Which tests cover this change? Returns the test projects and test classes the graph reaches from changed_files, or from the diff of the working tree against base (HEAD by default), with the changed files it could not resolve; all is true when the set could not be bounded and every test should run. For the symbols the change affects, impact_analysis. */
        affectedTests(input?: {
            /** Commit the diff is taken from when changed_files is omitted (default HEAD). */
            base?: string | null;
            /** Virtual paths of the changed files; omit them to read the diff against base and the working tree. */
            changed_files?: readonly string[] | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly all: boolean;
            readonly test_classes: readonly string[];
            readonly test_projects: readonly string[];
            readonly unresolved: readonly string[];
        }>;

        /** `arcadedb_query` - Execute Cypher or SQL queries on ArcadeDB via Bolt protocol */
        arcadedbQuery(input: {
            /** Bolt protocol URI for ArcadeDB */
            bolt_uri: string;
            /** Target database name in ArcadeDB */
            database: string;
            /** Maximum number of result records to return, at least 1 (default 1000) */
            max_results?: number;
            /** Query parameters as key-value pairs */
            parameters?: Record<string, unknown> | null;
            /** Password for ArcadeDB authentication */
            password?: string | null;
            /** Cypher or SQL query to execute */
            query: string;
            /** Query language: Cypher (default) or Sql */
            query_language?: "Cypher" | "Sql";
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

        /** `batch_anchors` - Closure of anchors (contains, callers, tests) and overlap of two anchor sets. */
        batchAnchors(input: {
            /** First anchor set (overlap): {fqns: [...], files: [...]}. */
            a?: Record<string, unknown> | null;
            /** closure expands fqns and files to the nodes they contain, their callers and their tests; overlap intersects the closures of a and b. */
            action: "closure" | "overlap";
            /** Second anchor set (overlap): {fqns: [...], files: [...]}. */
            b?: Record<string, unknown> | null;
            /** Virtual paths of files to expand (closure). */
            files?: readonly string[] | null;
            /** FQNs to expand (closure). */
            fqns?: readonly string[] | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly disjoint: boolean;
            readonly nodes: readonly string[];
            readonly shared: readonly string[];
            readonly truncated: boolean;
            readonly unresolved: readonly string[];
            readonly unresolved_a: readonly string[];
            readonly unresolved_b: readonly string[];
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

        /** `codebase_map` - What is the shape of this codebase? Lists the entries of one level (L1_Package by default, L2_Module, L3_Symbol) with their kind and path, under root_fqn when given, at most max_entries (200), truncated when more exist; include_metrics adds counts and complexity. The FQN of an entry feeds package_summary and symbol_detail. */
        codebaseMap(input?: {
            /** Add children_count, cyclomatic and loc to each entry. */
            include_metrics?: boolean;
            /** Level of the entries: L0_Monorepo, L1_Package (default), L2_Module or L3_Symbol. */
            level?: "L0_Monorepo" | "L1_Package" | "L2_Module" | "L3_Symbol" | "L4_Statement";
            /** Most entries returned (default 200); truncated says when more exist. */
            max_entries?: number;
            /** Keep only the entries at or under this node (a package or folder by path, a namespace by its files and name, a type by FQN); a node id, an FQN or a path. */
            root_fqn?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly entries: readonly { readonly children_count?: number; readonly cyclomatic?: number; readonly fqn: string; readonly kind: string; readonly language?: string; readonly lo_c?: number; readonly name: string; }[];
            readonly level: string;
            readonly totals: { readonly module_count: number; readonly node_count: number; readonly package_count: number; readonly symbol_count: number; };
            readonly truncated: boolean;
        }>;

        /** `codebase_search` - Where in the codebase is this concept handled? Searches the indexed symbols by meaning (vector) and by words (BM25) and returns at most top_k hits (10) ranked, each with its FQN, score and match origin; truncated says when more match. The FQN of a hit feeds symbol_detail, sub_graph and impact_analysis. For an exact text, grep; for a code shape, structural_search. */
        codebaseSearch(input: {
            /** Keep only these node kinds: Class, Interface, Method, Function, Property, Enum, Record, Module, Package... Empty keeps every kind. */
            filter_kinds?: readonly string[];
            /** Keep only the symbols of these languages (csharp, typescript, python...). */
            filter_languages?: readonly string[];
            /** Keep only the symbols of these packages (FQN prefixes). */
            filter_packages?: readonly string[];
            /** Return the signature of each hit. */
            include_signature?: boolean;
            /** Levels searched: L1_Package, L2_Module, L3_Symbol, L4_Statement; empty searches every level. */
            levels?: readonly string[];
            /** Drop the hits scored under this value (0 keeps every hit). */
            min_score?: number;
            /** Ranking: Hybrid (default, vector and BM25 fused), Vector (embedding only), Lexical (BM25 only). */
            mode?: "Hybrid" | "Vector" | "Lexical";
            /** A symbol name, or words describing what the code does. */
            query: string;
            /** Most hits returned (default 10); truncated says when more match. */
            top_k?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly hits: readonly { readonly fqn: string; readonly match_origin: string; readonly score: number; readonly signature?: string; readonly summary_short?: string; }[];
            readonly refreshed_files: number;
            readonly total_candidates: number;
            readonly truncated: boolean;
        }>;

        /** `complexity_report` - Top-N methods by complexity (Cyclomatic, NestingDepth, FanOut, LoC, Callers) with median and P95. */
        complexityReport(input?: {
            /** Metric ranked: Cyclomatic (default), NestingDepth, FanOut, LoC, Callers. */
            metric?: "Cyclomatic" | "NestingDepth" | "FanOut" | "LoC" | "Callers";
            /** Keep only the symbols under this FQN. */
            root_fqn?: string | null;
            /** How many symbols are listed, the highest first (default 20). */
            top_n?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly evaluated_count: number;
            readonly median?: number;
            readonly metric: string;
            readonly p95?: number;
            readonly top: readonly { readonly fqn: string; readonly metric: string; readonly value: number; }[];
        }>;

        /** `count_pattern` - How many times does each pattern occur in this file? Counts the matches of each regex over the whole file, independently and exactly, for a check that must not rest on an estimate; distinct counts the unique matched strings instead. One file per call; to count over a tree, grep with output_mode "count". */
        countPattern(input: {
            /** Count unique matched substrings instead of raw matches */
            distinct_matches?: boolean;
            /** Return the matched substrings themselves (capped by max_matches_returned) */
            include_matches?: boolean;
            /** Cap on the returned matches list size, at least 1 (default: 200); read only when include_matches is true. To count without listing matches, leave include_matches false */
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
            /** Maximum number of rows to read, at least 1 (default: no limit); truncated tells whether rows were left out */
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
            schema_scope?: "Tables" | "Columns" | "Indexes" | "ForeignKeys" | "All";
            /** Optional table name filter with wildcard support (e.g. 'user*', '*order*') */
            table_filter?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly database_name: string;
            readonly foreign_keys?: readonly { readonly name: string; readonly source_columns: readonly string[]; readonly source_table: string; readonly target_columns: readonly string[]; readonly target_table: string; }[];
            readonly indexes?: readonly { readonly columns: readonly string[]; readonly is_unique: boolean; readonly name: string; readonly table_name: string; readonly type?: string; }[];
            readonly provider_name: string;
            readonly tables: readonly { readonly columns: readonly { readonly data_type: string; readonly default_value?: string; readonly is_nullable: boolean; readonly is_primary_key: boolean; readonly max_length?: number; readonly name: string; }[]; readonly name: string; readonly row_count_estimate?: number; readonly schema?: string; }[];
        }>;

        /** `dependency_graph` - What depends on what at this scope? Returns the nodes and the edges of the given edge_kinds (imports by default) among the packages (L1_Package), modules (L2_Module) or symbols (L3_Symbol) of the scope, at most max_nodes nodes (200) selected by root_fqn and node_kinds, truncated when the scope holds more; cycles and cycle_count cover the whole scope, each cycle naming its members as FQNs. For every edge of a kind across the index, edge_list; for the neighbourhood of a symbol, sub_graph. */
        dependencyGraph(input?: {
            /** Edge kinds to follow, by name: contains, calls, imports, inherits, extends, implements, tests, documents, project_depends, case-insensitively; inherits is extends and implements together. */
            edge_kinds?: readonly string[];
            /** Add a Graphviz DOT rendering (dot). */
            include_dot?: boolean;
            /** Keep the edges whose target is outside the scope (an external package); false drops them. */
            include_external?: boolean;
            /** Add a Mermaid flowchart of the nodes and edges (mermaid_flowchart), for a human reader; false by default: the data alone answers an agent. */
            include_mermaid?: boolean;
            /** Most members returned per cycle, at least 1 (default 50). member_count keeps the full size. */
            max_cycle_members?: number;
            /** Most cycles returned, at least 0 (default 20), the largest first; 0 lists none. cycle_count keeps the full count. */
            max_cycles?: number;
            /** Most nodes returned, at least 1 (default 200); truncated says when more exist. */
            max_nodes?: number;
            /** Restrict nodes to these kinds, by name: file, project, namespace, type, member, doc_section, test. Empty keeps every node of the scope. */
            node_kinds?: readonly string[];
            /** Keep only the nodes at or under this node (a package or folder by path, a namespace by its files and name, a type by FQN); a node id, an FQN or a path. */
            root_fqn?: string | null;
            /** Level of the nodes: L1_Package (default), L2_Module or L3_Symbol. */
            scope?: "L0_Monorepo" | "L1_Package" | "L2_Module" | "L3_Symbol" | "L4_Statement";
        }, ctx?: ExecutionContext): Promise<{
            readonly cycles: readonly { readonly edge_kind: string; readonly fqns: readonly string[]; readonly member_count: number; }[];
            readonly cycles_truncated: boolean;
            readonly dot?: string;
            readonly edges: readonly { readonly from: string; readonly kind: string; readonly to: string; }[];
            readonly mermaid_flowchart?: string;
            readonly metrics: { readonly avg_fan_in: number; readonly avg_fan_out: number; readonly cycle_count: number; readonly edge_count: number; readonly node_count: number; };
            readonly nodes: readonly { readonly fqn: string; readonly is_seed: boolean; readonly kind: string; readonly level: string; readonly name: string; }[];
            readonly truncated: boolean;
        }>;

        /** `directory_read` - What is in this folder? Lists the files and sub-folders of a directory, recursive on request, filtered by a glob pattern, at most max_results entries (200 by default; truncated says when the folder holds more). To find files by name across a tree, glob; to find files by content, grep. */
        directoryRead(input: {
            /** Maximum number of entries to return (default: 200); truncated says when the directory holds more. Use a smaller value to avoid overloading context windows. */
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

        /** `documented_in` - Markdown sections that document a symbol. */
        documentedIn(input: {
            /** FQN of the symbol. */
            fqn: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly sections: readonly { readonly heading_path: string; readonly path: string; readonly start_line: number; }[];
            readonly unresolved: readonly string[];
        }>;

        /** `documents_of` - Symbols documented by the sections of a Markdown file. */
        documentsOf(input: {
            /** Virtual path of the Markdown file. */
            path: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly symbols: readonly { readonly file: string; readonly fqn: string; readonly heading_path: string; readonly kind: string; }[];
            readonly unresolved: readonly string[];
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

        /** `edge_list` - Which edges of these kinds exist across the whole index? Lists every edge of edge_kinds (required), from and to as FQNs with their ids, in a stable order, page_size at a time (200, up to 1000), with total and the next_cursor to pass back as cursor until it is null; root_fqn keeps the edges with an end at or under one node (a package or file by path, a type or member by FQN, a namespace by its files and name). For the edges among the nodes of a scope with its cycles, dependency_graph; for the neighbourhood of one symbol, sub_graph. */
        edgeList(input: {
            /** The next_cursor of the previous page; omit it for the first page. */
            cursor?: string | null;
            /** Edge kinds to list, by name: contains, calls, imports, inherits, extends, implements, tests, documents, project_depends, case-insensitively; inherits is extends and implements together. */
            edge_kinds: readonly string[];
            /** Edges per page, 1 to 1000 (default 200). */
            page_size?: number;
            /** Keep only the edges with an end at or under this node: a package or file (by path), a namespace (its files and name), a type or member (by FQN); a node id, an FQN or a path. */
            root_fqn?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly edges: readonly { readonly from: string; readonly from_id: string; readonly kind: string; readonly to: string; readonly to_id: string; }[];
            readonly next_cursor?: string;
            readonly total: number;
        }>;

        /** `edit_batch` - Applies node_replace, node_insert and idempotent intent operations over several files as one transaction. Each operation is {kind, path, selector, text} plus expected_hash (required, node_replace only) or position (node_insert: 'inside', 'before' or 'after'). An intent operation is {kind, path} with kind ensure_using / ensure_import (name, module for TypeScript or JavaScript) or ensure_field / ensure_member (selector of the type, text): it adds the line or member only when absent and reports applied false when it was already there. Operations on one file apply in order, each on the bytes the previous one produced. validate 'parse' (default) refuses the batch when an operation introduces a parse error; 'build' also runs dotnet build on the project above the edited files and restores every file when it is red. On any failure all files are left as they were. */
        editBatch(input: {
            /** The operations, applied as one transaction; those on one file apply in order, each on the bytes the previous one produced. */
            operations: readonly { readonly expected_hash?: string; readonly index?: number; readonly kind: "node_replace" | "node_insert" | "ensure_using" | "ensure_import" | "ensure_field" | "ensure_member" | "ensure_section" | "insert_under_heading" | "replace_mermaid"; readonly level?: number; readonly module?: string; readonly name?: string; readonly path: string; readonly position?: "inside" | "before" | "after"; readonly selector?: string; readonly text?: string; }[];
            /** What refuses the batch: 'parse' when an operation introduces a parse error, 'build' when dotnet build on the project above the edited files is red too. */
            validate?: "parse" | "build";
        }, ctx?: ExecutionContext): Promise<{
            readonly operations: readonly { readonly applied: boolean; readonly path: string; readonly range: { readonly end_byte: number; readonly end_line: number; readonly start_byte: number; readonly start_line: number; }; }[];
            readonly validation: string;
        }>;

        /** `edit_file` - Replaces old_string by new_string in a file: refused when old_string is absent or occurs more than once, unless replace_all is true, which replaces every occurrence. Read the file first (file_read) to copy the exact text; to rewrite a whole file, file_write. */
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

        /** `file_read` - What does this file say? Reads a text file (source, JSON, XML, Markdown...) by virtual path, whole or by line range with offset and limit; has_more says when the range or max_length cut it. To read one symbol of a source file, node_read; to find which file holds a text, grep. */
        fileRead(input: {
            /** The text encoding to use (default: UTF-8) */
            encoding?: "UTF-8" | "ASCII" | "Unicode" | "UTF-32";
            /** Maximum number of lines to return (default: up to the end of the file); max_length caps the result further */
            limit?: number | null;
            /** Maximum number of characters to read, at least 1 (default: no limit; omit it for no limit). Applied after offset/limit: when it cuts a line range, only whole lines are kept and has_more is true, except that when the first line alone exceeds max_length it is cut by characters. */
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

        /** `file_write` - Writes content to a file by virtual path, creating the file and its folders, replacing the content unless append is true. For a change inside an existing file, edit_file; the whole content is written, so a large file goes through edit_file rather than a rewrite. */
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

        /** `firefox` - Drive a Firefox browser: open a page (navigate), read it as an outline of sections, links and controls (snapshot, find, read), find its sections by meaning (search), then act on a node by its id (click, type), press keys, run a script (evaluate) or save a screenshot. Tabs of its own are opened, listed, switched and closed (new_tab, tabs, switch_tab, close_tab). The browser stays open between calls of the same run, or from one run to the next when the host keeps it; close ends it. */
        firefox(input: {
            /** What to do: navigate (url), back, snapshot (the outline of the page: sections, links, controls, each with a node id), read (the text of the page, or of node_id), find (nodes by kind and name, under node_id), search (query: the sections of the page closest in meaning), click (node_id), type (node_id, text), press (key), evaluate (script), screenshot (save_to_path), tabs (the tabs of this agent), new_tab (url, optional), switch_tab (tab), close_tab (tab, the current one by default), close */
            action: "navigate" | "back" | "snapshot" | "read" | "find" | "search" | "click" | "type" | "press" | "evaluate" | "screenshot" | "tabs" | "new_tab" | "switch_tab" | "close_tab" | "close";
            /** press: a key or a chord on the focused element - Enter, Tab, Escape, ArrowDown, PageDown, a, Control+a, Shift+Tab */
            key?: string | null;
            /** find: only nodes of this kind */
            kind?: "section" | "link" | "control" | null;
            /** snapshot, find: the most nodes to return (the default is set by the host, 200 at most); search: the most hits (5 by default, 20 at most) */
            max_nodes?: number | null;
            /** find: only nodes whose name holds this text, whatever the case */
            name?: string | null;
            /** A node id returned by snapshot or find (p1#12). click, type: the element; read: the node to read; find: search under this node */
            node_id?: string | null;
            /** snapshot, find: nodes to skip, to read the next part of a listing; read: characters to skip */
            offset?: number | null;
            /** The profile the browser starts with, at the first action of a run: disposable, or a profile name the host allows; a browser already open keeps its profile */
            profile?: string | null;
            /** search: what to look for in the page, in plain words; the sections closest in meaning are returned */
            query?: string | null;
            /** screenshot: the virtual path the PNG image of the visible part of the page is written to (/output/page.png) */
            save_to_path?: string | null;
            /** evaluate: a JavaScript expression run in the page; its value is returned, a promise is awaited */
            script?: string | null;
            /** switch_tab, close_tab: the handle of a tab of this agent (t1, t2), as tabs lists them */
            tab?: string | null;
            /** type: the text that replaces what the element holds; a line feed presses Enter. Never a secret: the text goes through the conversation */
            text?: string | null;
            /** navigate, new_tab: the http or https address to open */
            url?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly action: string;
            readonly dialogs?: readonly string[];
            readonly error?: string;
            readonly hits?: readonly { readonly excerpt: string; readonly name: string; readonly node_id: string; readonly score: number; }[];
            readonly nodes?: readonly { readonly id: string; readonly kind: string; readonly name: string; readonly parent_id?: string; readonly role: string; readonly text: string; }[];
            readonly profile?: string;
            readonly saved_path?: string;
            readonly success: boolean;
            readonly tab?: string;
            readonly tabs?: readonly { readonly current: boolean; readonly id: string; readonly title: string; readonly url: string; }[];
            readonly text?: string;
            readonly title?: string;
            readonly total_nodes?: number;
            readonly truncated: boolean;
            readonly url?: string;
            readonly value?: string;
        }>;

        /** `flow_trace` - How does control get from this symbol to that one? Returns the shortest call path from from to to, or with include_all_paths every path up to max_depth hops and max_paths paths (20), truncated when more exist; without to, the paths from from. edge_kinds takes calls by default. For the neighbourhood of one symbol, sub_graph. */
        flowTrace(input: {
            /** Forward follows calls out of from; Backward follows them into from; Both. */
            direction?: "Forward" | "Backward" | "Both";
            /** Edge kinds to follow, by name: contains, calls, imports, inherits, extends, implements, tests, documents, project_depends, case-insensitively; inherits is extends and implements together. Empty follows calls. */
            edge_kinds?: readonly string[];
            /** FQN the paths start from. */
            from: string;
            /** With to: every path up to max_paths instead of the shortest one. */
            include_all_paths?: boolean;
            /** Most hops per path, at least 1 (default 4). */
            max_depth?: number;
            /** Most paths returned (default 20); truncated says when more exist. */
            max_paths?: number;
            /** FQN the paths must reach; omit it to list every path from the start. */
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

        /** `glob` - Which files match this name pattern? Expands a wildcard pattern (**\/*.cs, src/**\/test_*.py) relative to path and returns the matching virtual paths, sorted, at most max_results (200). To find files by content, grep; to list one folder, directory_read. */
        glob(input: {
            /** Maximum number of paths returned (default: 200) */
            max_results?: number;
            /** Root directory (virtual path): an existing mounted directory */
            path: string;
            /** Wildcard pattern relative to path; an absolute pattern is refused */
            pattern: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly paths: readonly string[];
            readonly truncated: boolean;
        }>;

        /** `graph_schema` - Inspect graph database schema (vertex labels, edge labels, properties, indexes) */
        graphSchema(input: {
            /** Database name (required for ArcadeDB) */
            database?: string | null;
            /** Endpoint URI for the graph database (Bolt URI for ArcadeDB, WebSocket URI for JanusGraph) */
            endpoint: string;
            /** Graph database type: ArcadeDB or JanusGraph */
            graph_type: "ArcadeDB" | "JanusGraph";
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

        /** `grep` - Which files contain this text? Searches the lines of the text files under path with a regex, glob filtering the files relative to path (a file as path is searched alone, without glob), and returns path:line:text hits with total_matches and files_matched; output_mode "count" returns the counts alone, so a "how many" question takes one call. At most max_results hits (200) over max_files files (2000) within a 20 s budget; binary and over-1-MiB files are skipped; truncated and scan_complete say when the scan was cut. To find files by name, glob; to search code by structure, structural_search; to search by meaning, codebase_search. */
        grep(input: {
            /** Case-insensitive matching */
            case_insensitive?: boolean;
            /** Optional file filter relative to a folder path (never absolute, never with a file path); all files when absent */
            glob?: string | null;
            /** Maximum number of files scanned; binary or unreadable files are skipped and counted (default: 2000) */
            max_files?: number;
            /** Maximum number of hits (default: 200) */
            max_results?: number;
            /** hits: one path:line:text per match (default); the scan is not stopped by max_results, it reads up to max_files or the 20 s budget, so a wide search can come back timed_out or truncated. count: no hit lines, only total_matches, files_matched and the per-file counts (the max_results highest) */
            output_mode?: "hits" | "count";
            /** Directory searched, or one file searched alone (then no glob); a virtual path inside the agent root */
            path: string;
            /** Regular expression searched in each line (per-match timeout; the scan stops and timed_out is set on a timeout) */
            pattern: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly counts: readonly { readonly count: number; readonly path: string; }[];
            readonly files_matched: number;
            readonly files_scanned: number;
            readonly files_skipped: number;
            readonly hits: readonly { readonly line: number; readonly path: string; readonly text: string; }[];
            readonly scan_complete: boolean;
            readonly timed_out: boolean;
            readonly total_matches: number;
            readonly truncated: boolean;
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

        /** `impact_analysis` - Who is affected if this symbol changes? Returns the direct and transitive dependents of target (callers by default; Forward lists what it depends on) up to max_depth hops, counted by package, with the top callers; at most max_nodes symbols are visited (500), truncated when the walk stopped early. For the tests to run after the change, affected_tests. */
        impactAnalysis(input: {
            /** Backward (default) lists what depends on target; Forward what target depends on; Both. */
            direction?: "Forward" | "Backward" | "Both";
            /** Most hops followed for the transitive impact, at least 1 (default 3). */
            max_depth?: number;
            /** Most symbols visited, at least 1 (default 500); truncated says when the walk stopped early. */
            max_nodes?: number;
            /** FQN of the symbol about to change. */
            target: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly by_package: Record<string, number>;
            readonly direct_impact: readonly string[];
            readonly target: string;
            readonly top_callers: readonly { readonly fqn: string; readonly kind: string; readonly signature?: string; readonly summary_short?: string; }[];
            readonly transitive_impact: readonly string[];
            readonly truncated: boolean;
        }>;

        /** `incremental_reindex` - Reindex only files changed since a prior commit or from an explicit list. Cost is proportional to the changed files, not the repository: one file of 2 000 lines reindexes in under 2 s. */
        incrementalReindex(input: {
            /** Virtual paths of the files to reindex; omit them to take the files changed between from_commit and to_commit. */
            changed_files?: readonly string[];
            /** Commit the diff starts from when changed_files is omitted. */
            from_commit?: string | null;
            /** Virtual path of the indexed root. */
            root_path: string;
            /** Commit the diff ends at (default HEAD). */
            to_commit?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly cache: string;
            readonly edge_count: number;
            readonly elapsed: string;
            readonly embedding_stats?: { readonly dimensions: number; readonly elapsed: string; readonly model: string; readonly nodes_embedded: number; readonly nodes_skipped: number; readonly provider: string; };
            readonly errors: readonly string[];
            readonly file_count: number;
            readonly filter_stats: { readonly excluded_by_exclude_set: number; readonly excluded_by_gitignore: number; readonly excluded_by_language: number; readonly excluded_by_policy: number; readonly excluded_by_policy_paths: readonly string[]; readonly excluded_by_suffix: number; readonly physical_entries_probe: number; readonly physical_root: string; readonly total_enumerated: number; readonly vfs_file_entries_yielded: number; };
            readonly index_id: string;
            readonly node_count: number;
            readonly reindexed_file_count: number;
            readonly statement_count: number;
        }>;

        /** `index_codebase` - Scan the codebase and build the full RaggableTree index, reusing the one saved by an earlier session when the host wires the cache. */
        indexCodebase(input: {
            /** Embedding model to record when the host's provider does not name its own (default text-embedding-3-small). */
            embedding_model?: string;
            /** Ask the LLM for a summary of each symbol while indexing (slow). */
            enrich_with_llm?: boolean;
            /** Folder names skipped (default: node_modules, dist, .git, bin, obj). */
            exclude?: readonly string[];
            /** Languages indexed (csharp, typescript, python...); empty indexes every supported language. */
            languages?: readonly string[];
            /** How many files are parsed at once; 0 (default) uses one per processor. */
            max_parallelism?: number;
            /** Skip the files git ignores (.gitignore, .git/info/exclude). */
            respect_gitignore?: boolean;
            /** Name substituted for the root path as the prefix of every FQN; empty keeps the path. */
            root_alias?: string;
            /** Virtual path of the root to index (a mounted folder). */
            root_path: string;
            /** Reload the index saved by an earlier call when the host wires the cache; false rebuilds from scratch. */
            use_cache?: boolean;
        }, ctx?: ExecutionContext): Promise<{
            readonly cache: string;
            readonly edge_count: number;
            readonly elapsed: string;
            readonly embedding_stats?: { readonly dimensions: number; readonly elapsed: string; readonly model: string; readonly nodes_embedded: number; readonly nodes_skipped: number; readonly provider: string; };
            readonly errors: readonly string[];
            readonly file_count: number;
            readonly filter_stats: { readonly excluded_by_exclude_set: number; readonly excluded_by_gitignore: number; readonly excluded_by_language: number; readonly excluded_by_policy: number; readonly excluded_by_policy_paths: readonly string[]; readonly excluded_by_suffix: number; readonly physical_entries_probe: number; readonly physical_root: string; readonly total_enumerated: number; readonly vfs_file_entries_yielded: number; };
            readonly index_id: string;
            readonly node_count: number;
            readonly reindexed_file_count: number;
            readonly statement_count: number;
        }>;

        /** `index_status` - List every virtual root currently indexed in the RaggableTree store. */
        indexStatus(input?: Record<string, never>, ctx?: ExecutionContext): Promise<{
            readonly dirty_count: number;
            readonly dirty_paths: readonly string[];
            readonly roots: readonly { readonly edge_count: number; readonly indexed_at: string; readonly language_summary?: string; readonly node_count: number; readonly virtual_root: string; }[];
        }>;

        /** `is_path_indexed` - Check whether a virtual path is covered by any indexed root (longest match wins). */
        isPathIndexed(input: {
            /** Virtual path of the file or folder to check. */
            virtual_path: string;
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
            /** Maximum number of results to return, at least 1 (default 1000) */
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
            operation: "Parse" | "Query" | "Format";
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
            /** Maximum number of rows to return for SELECT queries, at least 1 and at most 100000 on a SELECT (default 1000); truncated tells whether rows were left out */
            max_rows?: number;
            /** Optional named parameters for parameterized queries (e.g. {"active": true}) */
            parameters?: Record<string, unknown> | null;
            /** ADO.NET provider name: Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Microsoft.Data.Sqlite */
            provider_name: string;
            /** SQL query to execute */
            query: string;
            /** Type of query: Select (returns rows), Execute (returns affected rows count), or Scalar (returns single value) */
            query_type?: "Select" | "Execute" | "Scalar";
            /** Query timeout in seconds, at least 1 (default 30) */
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
            readonly truncated?: boolean;
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

        /** `memory_store` - List, add, replace, delete, and get typed memory entries (user/project/feedback/reference). add returns the entry's id; every entry carries created_at; replace retires a stale fact in place, keeping its id; list takes newest_first. */
        memoryStore(input?: {
            /** Category of the entry for add, filter for list (user / project / feedback / reference); add defaults to user, list to every category. */
            category?: string | null;
            /** The fact to keep, for add and replace. */
            content?: string | null;
            /** Entry id for get, replace and delete: what add returned. */
            id?: number;
            /** For list: true orders the entries newest first (created_at descending, then id descending); false, the default, keeps the order they were added in. */
            newest_first?: boolean;
            /** Operation: list (entries, each with id, category, content and created_at), add (returns the new id), replace (retires a stale fact in place: same id and category, new content and created_at), delete, get. */
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
            /** Maximum number of documents to return for Find, at least 1 (default 100) */
            limit?: number;
            /** Operation to perform: Find, Aggregate, InsertOne, UpdateOne, DeleteOne, Count, Distinct */
            operation: "Find" | "Aggregate" | "InsertOne" | "UpdateOne" | "DeleteOne" | "Count" | "Distinct";
            /** JSON array of pipeline stages for Aggregate operations */
            pipeline?: string | null;
            /** JSON projection document for Find operations */
            projection?: string | null;
            /** Number of documents to skip for Find, at least 0 (default 0) */
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
            /** Maximum number of rows to return for SELECT queries, at least 1 and at most 100000 on a SELECT (default 1000); truncated tells whether rows were left out */
            max_rows?: number;
            /** Optional named parameters for parameterized queries (e.g. {"active": true}) */
            parameters?: Record<string, unknown> | null;
            /** ADO.NET provider name: Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Microsoft.Data.Sqlite */
            provider_name: string;
            /** SQL query to execute */
            query: string;
            /** Type of query: Select (returns rows), Execute (returns affected rows count), or Scalar (returns single value) */
            query_type?: "Select" | "Execute" | "Scalar";
            /** Query timeout in seconds, at least 1 (default 30) */
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
            readonly truncated?: boolean;
        }>;

        /** `node_ensure` - Applies one idempotent intent operation on one file: ensure_using / ensure_import (name, plus module for TypeScript or JavaScript) adds the using or import line in its place only when absent; ensure_field / ensure_member (selector of the type, text) adds a field, property or method next to its peers only when no member of that name exists. On Markdown: ensure_section (name = heading path, optional level) creates the missing headings; insert_under_heading (selector, text) appends at the end of a section; replace_mermaid (selector, index 0-based, text) replaces the body of the n-th mermaid fence of a section. Calling it again changes nothing and writes nothing: applied is false and range is the existing line or member. The insert is refused when it introduces a parse error that was not there before, the file is then untouched. */
        nodeEnsure(input: {
            /** 0-based index of the mermaid fence to replace in the section (replace_mermaid). */
            index?: number | null;
            /** Heading level of the section to create (ensure_section); the parent's plus one when omitted. */
            level?: number | null;
            /** The module an import comes from, for TypeScript or JavaScript (ensure_import). */
            module?: string | null;
            /** The namespace or module to import (ensure_using, ensure_import), or the heading path to create (ensure_section). */
            name?: string | null;
            /** The intent operation. */
            op: "ensure_using" | "ensure_import" | "ensure_field" | "ensure_member" | "ensure_section" | "insert_under_heading" | "replace_mermaid";
            /** Path of the file to edit, absolute under a mounted root or relative to the working root. */
            path: string;
            /** Selector of the type (ensure_field, ensure_member) or of the Markdown section (insert_under_heading, replace_mermaid), as node_outline lists it. */
            selector?: string | null;
            /** The member to add (ensure_field, ensure_member), the text to append (insert_under_heading) or the new fence body (replace_mermaid). */
            text?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly applied: boolean;
            readonly new_hash: string;
            readonly range: { readonly end_byte: number; readonly end_line: number; readonly start_byte: number; readonly start_line: number; };
        }>;

        /** `node_insert` - Inserts text relative to an anchor node: position 'inside' appends to a type or namespace body (after the last member of an indented or end-closed body; an enum member or a JSON pair gets its separating comma) or to the end of a Markdown section, 'before' puts it above the anchor's leading comments and attributes, 'after' below the anchor. There is no expected_hash: no anchor hash is required for before/after, the anchor is only a location. The text is re-indented line by line to the siblings' indentation (tabs or spaces as the file does); the insert is refused when it introduces a parse error that was not there before, the file is then untouched. */
        nodeInsert(input: {
            /** Path of the file to edit, absolute under a mounted root or relative to the working root. */
            path: string;
            /** Where the text goes relative to the anchor: inside its body, before it or after it. */
            position: "inside" | "before" | "after";
            /** Selector of the anchor node, as node_outline lists it: Ns.Type.Member(paramTypes), Section/Sub for Markdown, or @ts:<node-path>. */
            selector: string;
            /** The text to insert; re-indented to the siblings' indentation. */
            text: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly new_hash: string;
            readonly range: { readonly end_byte: number; readonly end_line: number; readonly start_byte: number; readonly start_line: number; };
        }>;

        /** `node_outline` - Outline of a file's members. */
        nodeOutline(input: {
            /** Path of the file to outline, absolute under a mounted root or relative to the working root. */
            path: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly members: readonly { readonly end_byte: number; readonly end_line: number; readonly kind: string; readonly selector: string; readonly start_byte: number; readonly start_line: number; }[];
        }>;

        /** `node_read` - What is the text of this member? Reads one member of a source file by selector (Ns.Type.Member(paramTypes)) or one section of a Markdown file and returns its text uncut, with its leading comments and attributes, its line range and its SHA-256 for node_replace. To list the selectors of a file, node_outline; for a whole file, file_read. */
        nodeRead(input: {
            /** Path of the file holding the member, absolute under a mounted root or relative to the working root. */
            path: string;
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
        nodeReplace(input: {
            /** SHA-256 of the node's current text, as node_read returned it; the replacement is refused when it is stale. */
            expected_hash: string;
            /** Path of the file to edit, absolute under a mounted root or relative to the working root. */
            path: string;
            /** Selector of the node to replace, as node_outline lists it: Ns.Type.Member(paramTypes), Section/Sub for Markdown, or @ts:<node-path>. */
            selector: string;
            /** The replacement text of the whole node, its leading comments and attributes included. */
            text: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly new_hash: string;
            readonly range: { readonly end_byte: number; readonly end_line: number; readonly start_byte: number; readonly start_line: number; };
        }>;

        /** `package_summary` - What is in this package? Returns the file and symbol counts of one package by FQN (an L1_Package entry of codebase_map), its entry points, its public exports and the packages it depends on, at most max_exports exports (50), truncated when the list was cut. For the dependency edges between packages, dependency_graph. */
        packageSummary(input: {
            /** FQN of the package (an entry of codebase_map at L1_Package). */
            fqn: string;
            /** Return the packages this one depends on. */
            include_dependencies?: boolean;
            /** Return the public exports of the package. */
            include_public_exports?: boolean;
            /** Most exports returned, at least 1 (default 50); truncated says when more exist. */
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
            /** Mailbox URI (agent://, crew://, topic://, or client:// for an external client) */
            target_mailbox: string;
        }, ctx?: ExecutionContext): Promise<{
            readonly message_id: string;
            readonly posted_at: string;
        }>;

        /** `postgres_query` - Execute SQL queries on PostgreSQL databases */
        postgresQuery(input: {
            /** Database connection string */
            connection_string: string;
            /** Maximum number of rows to return for SELECT queries, at least 1 and at most 100000 on a SELECT (default 1000); truncated tells whether rows were left out */
            max_rows?: number;
            /** Optional named parameters for parameterized queries (e.g. {"active": true}) */
            parameters?: Record<string, unknown> | null;
            /** ADO.NET provider name: Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Microsoft.Data.Sqlite */
            provider_name: string;
            /** SQL query to execute */
            query: string;
            /** Type of query: Select (returns rows), Execute (returns affected rows count), or Scalar (returns single value) */
            query_type?: "Select" | "Execute" | "Scalar";
            /** Query timeout in seconds, at least 1 (default 30) */
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
            readonly truncated?: boolean;
        }>;

        /** `progress_report` - Report the progress of a long-running scripted operation (label, step/total or percent, message). */
        progressReport(input: {
            /** true ends the operation and releases its slot. */
            done?: boolean;
            /** Headline of the running operation. */
            label: string;
            /** Current phase, one line. */
            message?: string | null;
            /** Completion ratio in [0, 100], when the operation can measure one. */
            percent?: number | null;
            /** Current step, 1-based, for a step-wise operation. */
            step?: number | null;
            /** Total step count, when known. */
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
        ragEval(input: {
            /** Collection to evaluate (default: the dataset's collection; for a dataset without corpus, the configured default collection; else 'rag-eval-{dataset name}') */
            collection?: string | null;
            /** Comma-separated profiles to compare (e.g. 'fast,balanced,quality'); overrides 'profile' */
            compare?: string | null;
            /** Virtual path of the golden dataset YAML (e.g. '/workspace/examples/rag/eval/golden.yaml') */
            dataset: string;
            /** Metric cutoff for recall@k / precision@k, at least 1 (default: 5) */
            k?: number;
            /** Profile to evaluate (default: 'default') */
            profile?: string | null;
            /** Force a full corpus reindex before evaluating (required after an embedding model change) */
            reindex?: boolean;
            /** Judge generation with the configured LLM (falls back to the deterministic heuristic, always labelled) */
            use_llm_judge?: boolean;
        }, ctx?: ExecutionContext): Promise<{
            readonly cases: readonly { readonly case_id: string; readonly profile: string; readonly recall_at_k?: number; readonly reciprocal_rank?: number; }[];
            readonly metrics: readonly { readonly answer_relevance?: number; readonly groundedness?: number; readonly judge: string; readonly k: number; readonly mrr?: number; readonly precision_at_k?: number; readonly profile: string; readonly recall_at_k?: number; }[];
            readonly reports: readonly string[];
        }>;

        /** `rag_ingest` - Makes documents searchable by rag_search: ingests the sources (virtual file paths, glob patterns, http(s) addresses) into a named collection, chunked and embedded. Incremental: a source unchanged since its last ingestion costs nothing, unless reindex is true. Returns a report: sources added, unchanged, re-ingested and pruned, chunks created and embedded. */
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

        /** `rag_search` - What do the ingested documents say about this? Searches the knowledge collections rag_ingest filled (documents, pages, notes) and returns an answer grounded in the top_k most relevant chunks (3), with their citations. For the code of the workspace, codebase_search, grep and file_read; rag_search knows only what was ingested. */
        ragSearch(input: {
            /** Collection to search, as rag_ingest named it (default: the configured default collection) */
            collection?: string | null;
            /** The question to search for in knowledge bases */
            question: string;
            /** Number of relevant chunks to retrieve, at least 1 (default: 3) */
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
            readonly message?: { readonly correlation_id?: string; readonly message_id: string; readonly metadata: Record<string, string>; readonly payload?: unknown; readonly published_at: string; readonly source_agent_id?: string; readonly source_crew_id: string; readonly topic: string; };
            readonly timed_out: boolean;
        }>;

        /** `relational_database_query` - Execute SQL queries on relational databases (SQL Server, PostgreSQL, MySQL, MariaDB, SQLite) */
        relationalDatabaseQuery(input: {
            /** Database connection string */
            connection_string: string;
            /** Maximum number of rows to return for SELECT queries, at least 1 and at most 100000 on a SELECT (default 1000); truncated tells whether rows were left out */
            max_rows?: number;
            /** Optional named parameters for parameterized queries (e.g. {"active": true}) */
            parameters?: Record<string, unknown> | null;
            /** ADO.NET provider name: Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Microsoft.Data.Sqlite */
            provider_name: string;
            /** SQL query to execute */
            query: string;
            /** Type of query: Select (returns rows), Execute (returns affected rows count), or Scalar (returns single value) */
            query_type?: "Select" | "Execute" | "Scalar";
            /** Query timeout in seconds, at least 1 (default 30) */
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
            readonly truncated?: boolean;
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

        /** `session_snip` - Immediately truncate the conversation to the head message plus the last retain_count turns, without analysis; a turn is a user request with the tool traces and the answer that follow it, so the cut never splits one. */
        sessionSnip(input?: {
            /** Number of recent turns to keep (default 3); a turn is a user request with the tool traces and the answer that follow it. */
            retain_count?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly kept_from: number;
            readonly removed_count: number;
            readonly retained_count: number;
        }>;

        /** `session_stats` - Report full session telemetry: message count, estimated tokens, cost, and call totals. */
        sessionStats(input?: Record<string, never>, ctx?: ExecutionContext): Promise<{
            readonly by_model: Record<string, string>;
            readonly by_model_tokens: readonly { readonly cache_read_tokens?: number; readonly cache_write_tokens?: number; readonly completion_tokens: number; readonly model: string; readonly prompt_tokens: number; readonly reasoning_tokens?: number; }[];
            readonly estimated_tokens: number;
            readonly message_count: number;
            readonly total_calls: number;
            readonly total_cost_usd: string;
            readonly total_tokens: number;
        }>;

        /** `session_store` - Read, write, append to, truncate, and annotate the current session conversation buffer and metadata; get_state reads one key or several (keys), set_state writes one (key, value) or several (values) and removes several (keys). */
        sessionStore(input?: {
            /** Content of the message append_message adds. */
            content?: string | null;
            /** State key for get_state / set_state. */
            key?: string | null;
            /** Several state keys: read in one call by get_state (the answer is values), removed in one call by set_state. */
            keys?: readonly string[] | null;
            /** New buffer contents for write_messages: {role, content, timestamp} each. */
            messages?: readonly unknown[] | null;
            /** Note text for append_note. */
            note?: string | null;
            /** What to do with the session. */
            operation?: "read_messages" | "write_messages" | "append_message" | "append_note" | "get_metadata" | "set_title" | "truncate" | "reset" | "get_state" | "set_state";
            /** Number of recent turns to keep for truncate (default 3); a turn is a user request with the tool traces and the answer that follow it. */
            retain_count?: number;
            /** Role of the message append_message adds: user, assistant, system or note. */
            role?: string | null;
            /** Timestamp of the message append_message adds, as write_messages takes it. */
            timestamp?: string | null;
            /** Title for set_title. */
            title?: string | null;
            /** State value for set_state; null removes the key. */
            value?: string | null;
            /** Several state entries for set_state, written in one call. */
            values?: Record<string, string> | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly messages?: readonly unknown[];
            readonly metadata?: unknown;
            readonly removed_count: number;
            readonly success: boolean;
            readonly value?: string;
            readonly values?: Record<string, string>;
        }>;

        /** `shell_command` - Runs a shell command in the working directory and returns its exit code, stdout and stderr, cut at 10 000 characters; a command the allowlist or blocklist refuses is not run. action run (default) waits up to timeout seconds; start_job starts it detached and returns a job_id, which job_status and job_tail read later in the same run. To read or search files, file_read, grep and glob cost less than cat and find. */
        shellCommand(input: {
            /** run (default): run the command and wait; start_job: start it detached, output to a log file; job_status: running or exit code of a job; job_tail: last lines of a job's log */
            action?: "run" | "start_job" | "job_status" | "job_tail";
            /** Shell command to execute; required by the run and start_job actions */
            command: string;
            /** Id returned by start_job, for job_status and job_tail */
            job_id?: string;
            /** Lines job_tail returns from the end of the log */
            tail_lines?: number;
            /** Timeout in seconds */
            timeout_seconds?: number;
            /** Virtual working directory for command execution, e.g. /workspace. Defaults to the working directory relative file paths resolve against (the agent's root, else /workspace); outside an agent root, a writing command that directory does not allow runs in the write directory (/output). */
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
            /** Maximum number of rows to return for SELECT queries, at least 1 and at most 100000 on a SELECT (default 1000); truncated tells whether rows were left out */
            max_rows?: number;
            /** Optional named parameters for parameterized queries (e.g. {"active": true}) */
            parameters?: Record<string, unknown> | null;
            /** ADO.NET provider name: Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Microsoft.Data.Sqlite */
            provider_name: string;
            /** SQL query to execute */
            query: string;
            /** Type of query: Select (returns rows), Execute (returns affected rows count), or Scalar (returns single value) */
            query_type?: "Select" | "Execute" | "Scalar";
            /** Query timeout in seconds, at least 1 (default 30) */
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
            readonly truncated?: boolean;
        }>;

        /** `statement_query` - Query L4 statements by kind, parent FQN, or semantic similarity. */
        statementQuery(input?: {
            /** Statement kinds kept: If, For, While, Return, Throw, FunctionCall, MethodCall, Await, TryCatch, Assignment, VariableDecl... Empty keeps every kind. */
            kinds?: readonly string[];
            /** FQNs of the symbols whose statements are listed; empty scans every indexed symbol. */
            parent_fqns?: readonly string[];
            /** Words describing the statements wanted; ranks the hits by similarity. */
            semantic_query?: string | null;
            /** Most hits returned (default 50); truncated says when more exist. */
            top_k?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly hits: readonly { readonly condition?: string; readonly end_line?: number; readonly expression?: string; readonly kind: string; readonly parent_fqn: string; readonly score?: number; readonly start_line: number; readonly statement_id: string; }[];
            readonly truncated: boolean;
        }>;

        /** `structural_rewrite` - Rewrites every node a pattern or rule matches with its fix template, over the files of one language: a dry run by default, returning the per-file diff; dry_run false writes the files. A file whose rewrite no longer parses is refused alone; at most max_files files (500), truncated when cut. To preview the matches first, structural_search; for one replacement by text, edit_file. */
        structuralRewrite(input?: {
            /** true (default) returns the diffs without writing; false writes the files. */
            dry_run?: boolean;
            /** Replacement template; $NAME and $$$NAME are substituted by their captures. Required with pattern or rule; a rule_file carries its own. */
            fix?: string | null;
            /** Glob kept among the candidate files (**\/*.cs). */
            glob?: string | null;
            /** Language of the pattern and of the files rewritten (csharp, typescript, python...). Required with pattern or rule; a rule_file names its own. */
            language?: string;
            /** Most files scanned, at least 1 (default 500); truncated says when more were candidates. */
            max_files?: number;
            /** Virtual paths of the files or folders rewritten; empty takes the mounted roots. */
            paths?: readonly string[];
            /** Code pattern with $NAME / $$$NAME metavariables, as structural_search takes it. */
            pattern?: string | null;
            /** Structural rule object, as structural_search takes it. Give exactly one of pattern, rule or rule_file. */
            rule?: unknown;
            /** Virtual path of a YAML rule file (id, language, rule, fix) under a mounted root. */
            rule_file?: string | null;
        }, ctx?: ExecutionContext): Promise<{
            readonly files: readonly { readonly applied: number; readonly diff: string; readonly matches: number; readonly path: string; readonly refused_reason?: string; readonly skipped: readonly string[]; }[];
            readonly files_scanned: number;
            readonly truncated: boolean;
        }>;

        /** `structural_search` - Where does this code shape occur? Searches the files of one language (any tree-sitter grammar Orkeon loads, or Markdown) for a pattern parsed with that grammar: $NAME matches one node and captures its text, $$$NAME zero or more sibling nodes; a rule object or a YAML rule_file constrains the match further. Returns path, line, column, text, captures and the enclosing symbol of each match, at most max_results matches (100) over max_files files (500), truncated when cut. For a plain text, grep; to change the matches, structural_rewrite. */
        structuralSearch(input?: {
            /** Glob kept among the candidate files (**\/*.cs). */
            glob?: string | null;
            /** Language of the pattern and of the files searched (csharp, typescript, python, markdown...). Required with pattern or rule; a rule_file names its own. */
            language?: string;
            /** Most files scanned, at least 1 (default 500); truncated says when more were candidates. */
            max_files?: number;
            /** Most matches returned, at least 1 (default 100); truncated says when more exist. */
            max_results?: number;
            /** Virtual paths of the files or folders searched; empty searches the mounted roots. */
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

        /** `sub_graph` - What surrounds these symbols? Expands the graph from the seeds (FQNs) along edge_kinds, Forward (what they call or extend) or Backward (who calls or extends them), up to depth hops and max_nodes nodes (100); truncated says when the expansion stopped early. For the whole graph at a scope, dependency_graph; for the paths between two symbols, flow_trace. */
        subGraph(input: {
            /** How many hops from the seeds, at least 1 (default 2). */
            depth?: number;
            /** Forward follows the edges from the seeds (what X calls, what X extends); Backward follows them into the seeds (who calls X, who extends X); Both. */
            direction?: "Forward" | "Backward" | "Both";
            /** Edge kinds to follow, by name: contains, calls, imports, inherits, extends, implements, tests, documents, project_depends, case-insensitively; inherits is extends and implements together. */
            edge_kinds?: readonly string[];
            /** Add a Mermaid flowchart of the nodes and edges (mermaid_flowchart), for a human reader; false by default: the data alone answers an agent. */
            include_mermaid?: boolean;
            /** Most nodes returned, at least 1 (default 100); truncated says when the expansion stopped before its depth. */
            max_nodes?: number;
            /** Keep only expanded nodes of these kinds, by name: file, project, namespace, type, member, doc_section, test. Empty keeps every node. */
            node_kinds?: readonly string[];
            /** FQNs (or node ids) to expand from; a hit of codebase_search is one. */
            seeds: readonly string[];
        }, ctx?: ExecutionContext): Promise<{
            readonly edges: readonly { readonly from: string; readonly kind: string; readonly to: string; }[];
            readonly mermaid_flowchart?: string;
            readonly nodes: readonly { readonly fqn: string; readonly is_seed: boolean; readonly kind: string; readonly level: string; readonly name: string; }[];
            readonly truncated: boolean;
        }>;

        /** `symbol_detail` - What is this symbol? Returns the signature, doc comment and body metrics of one symbol by FQN (a hit of codebase_search), and on expand its members (at most max_children, 50), callers, callees, statements and inheritance: what it extends and implements, and what derives from or implements it (derived_by, implemented_by), as FQNs. For the source text, node_read or symbol_source; for who is affected by changing it, impact_analysis. */
        symbolDetail(input: {
            /** What to expand, one name or several joined by commas: Members, Inheritance (extends, implements, derived_by, implemented_by), Callers, Callees, PublicExports, Statements, Decorators, ParentChain, All; None by default. */
            expand?: string;
            /** FQN of the symbol (a hit of codebase_search). */
            fqn: string;
            /** Return the body metrics (lines, cyclomatic complexity). */
            include_body_metrics?: boolean;
            /** Return the doc comment. */
            include_doc?: boolean;
            /** Return the signature. */
            include_signature?: boolean;
            /** Most members returned under Members (default 50); truncated says when more exist. */
            max_children?: number;
        }, ctx?: ExecutionContext): Promise<{
            readonly body?: { readonly await_count: number; readonly branch_count: number; readonly call_count: number; readonly closure_count: number; readonly cyclomatic_complexity: number; readonly early_return_count: number; readonly is_partial: boolean; readonly loop_count: number; readonly max_depth: number; readonly statement_count: number; readonly try_catch_count: number; };
            readonly derived_by: readonly string[];
            readonly doc_comment?: string;
            readonly extends: readonly string[];
            readonly fqn: string;
            readonly implemented_by: readonly string[];
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
        symbolSource(input: {
            /** FQN of the symbol to cite. */
            fqn: string;
            /** Most lines returned (default 40); truncated says when the span is longer. */
            max_lines?: number;
            /** Span cited: SignatureOnly, SignatureAndDoc, SignatureAndBody (default), FullSpan, StatementSpan (with statement_id). */
            mode?: "SignatureOnly" | "SignatureAndDoc" | "SignatureAndBody" | "FullSpan" | "StatementSpan";
            /** Id of one statement (from statement_query) to cite under StatementSpan. */
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

        /** `token_budget` - Return the current model's context window size, used tokens (split by kind: system, user, assistant, tool, other), and available budget. */
        tokenBudget(input?: Record<string, never>, ctx?: ExecutionContext): Promise<{
            readonly available_tokens: number;
            readonly context_window_tokens: number;
            readonly model: string;
            readonly used_tokens: number;
            readonly used_tokens_by_kind: { readonly assistant: number; readonly other: number; readonly system: number; readonly tool: number; readonly user: number; };
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
            readonly message?: { readonly correlation_id?: string; readonly message_id: string; readonly metadata: Record<string, string>; readonly payload?: unknown; readonly published_at: string; readonly source_agent_id?: string; readonly source_crew_id: string; readonly topic: string; };
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
            sheets: readonly { readonly headers?: readonly string[]; readonly name: string; readonly rows?: readonly (readonly unknown[])[]; }[];
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
            operation?: "Parse" | "Query";
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
