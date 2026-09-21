using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Tools;

/// <summary>What a tool needs before an agent can use it, as the Settings screen says it.</summary>
public enum ToolRequirement
{
    /// <summary>Nothing: the tool works as registered.</summary>
    None,

    /// <summary>A key the user remembers once, under the environment variable named in <see cref="ToolInfo.Argument"/>.</summary>
    StoredKey,

    /// <summary>The tool is registered only once that key is in place; without it, it does not exist.</summary>
    OnlyWithStoredKey,

    /// <summary>The key travels in the call itself, supplied by the agent.</summary>
    KeyAtCall,

    /// <summary>Connection parameters (strings, credentials) travel in the call itself.</summary>
    ParametersAtCall,

    /// <summary>An expert setting of the file, under the section named in <see cref="ToolInfo.Argument"/>.</summary>
    ExpertSetting,
}

/// <summary>One tool of the catalogue: its registry name and what it needs.</summary>
/// <param name="Name">The registry name, the one a crew's YAML references.</param>
/// <param name="Requirement">What the tool needs.</param>
/// <param name="Argument">The environment variable or the section the requirement points at.</param>
public sealed record ToolInfo(string Name, ToolRequirement Requirement = ToolRequirement.None, string? Argument = null);

/// <summary>A family of the catalogue, in the order the Settings screen lists them.</summary>
/// <param name="Key">The family key, resolved to a localized label by the screen.</param>
/// <param name="Tools">The tools, in display order.</param>
public sealed record ToolFamily(string Key, IReadOnlyList<ToolInfo> Tools);

/// <summary>A key a tool needs: the variable to set, the tool that reads it, where to get one.</summary>
/// <param name="EnvName">The environment variable, in the spelling the runtime reads.</param>
/// <param name="UsedBy">The tool name, for the row's "used by" line.</param>
/// <param name="ConsoleUrl">Where the key is issued.</param>
public sealed record ToolSecret(string EnvName, string UsedBy, Uri ConsoleUrl);

/// <summary>
/// The tools <c>orkeon run</c> registers, by family, with what each one needs (STUDIO-21).
/// Declared here rather than read from the runtime: the framework carries no "required
/// settings" metadata, the registry lists names only and a tool absent from it (a key not
/// set) is invisible there. The list is the <c>orkeon run</c> column of the availability
/// matrix in <c>docs/tools/inventory.md</c>, and a test pins every name against that file.
/// </summary>
public static class ToolCatalog
{
    /// <summary>Family keys, in display order.</summary>
    public const string WebFamily = "web";
    /// <summary>Search and knowledge.</summary>
    public const string SearchFamily = "search";
    /// <summary>Files.</summary>
    public const string FilesFamily = "files";
    /// <summary>Data.</summary>
    public const string DataFamily = "data";
    /// <summary>Code.</summary>
    public const string CodeFamily = "code";
    /// <summary>Session and memory.</summary>
    public const string SessionFamily = "session";
    /// <summary>Events.</summary>
    public const string EventsFamily = "events";
    /// <summary>Code analysis.</summary>
    public const string AnalysisFamily = "analysis";
    /// <summary>Collaboration.</summary>
    public const string CollaborationFamily = "collaboration";
    /// <summary>Folders.</summary>
    public const string MountsFamily = "mounts";

    /// <summary>The Tavily key of <c>web_search</c>, resolved through the secret chain (<c>ORKEON_</c> + name).</summary>
    public const string TavilyKeyEnv = "ORKEON_TAVILY_API_KEY";

    /// <summary>The Brave key of <c>brave_search</c>, read as-is by the runner host, without a prefix.</summary>
    public const string BraveKeyEnv = "BRAVE_API_KEY";

    /// <summary>The keys the tools need, in the order the card lists them.</summary>
#pragma warning disable S1075 // URIs should not be hardcoded — the vendors' own key consoles, public and stable; the card opens them for the user
    public static IReadOnlyList<ToolSecret> Secrets { get; } =
    [
        new(TavilyKeyEnv, "web_search", new Uri("https://app.tavily.com")),
        new(BraveKeyEnv, "brave_search", new Uri("https://api-dashboard.search.brave.com")),
    ];
#pragma warning restore S1075

    /// <summary>The families, in display order.</summary>
    public static IReadOnlyList<ToolFamily> Families { get; } =
    [
        new(WebFamily,
        [
            new("http_api"),
            new("web_scrape"),
            new("scrape_element"),
            new("github"),
            new("image_generation", ToolRequirement.KeyAtCall),
        ]),
        new(SearchFamily,
        [
            new("web_search", ToolRequirement.StoredKey, TavilyKeyEnv),
            new("brave_search", ToolRequirement.OnlyWithStoredKey, BraveKeyEnv),
            new("cache_search"),
            new("semantic_search"),
            new("local_embed_text"),
        ]),
        new(FilesFamily,
        [
            new("file_read"),
            new("file_write"),
            new("directory_read"),
            new("directory_search"),
            new("email_parser"),
            new("count_pattern"),
        ]),
        new(DataFamily,
        [
            new("csv_reader"),
            new("pdf_reader"),
            new("json_tool"),
            new("xml_parser"),
            new("docx_reader"),
            new("docx_writer"),
            new("xlsx_reader"),
            new("xlsx_writer"),
            new("pdf_search"),
            new("txt_search"),
            new("mdx_search"),
            new("relational_database_query", ToolRequirement.ParametersAtCall),
            new("sqlserver_query", ToolRequirement.ParametersAtCall),
            new("postgres_query", ToolRequirement.ParametersAtCall),
            new("mysql_query", ToolRequirement.ParametersAtCall),
            new("mariadb_query", ToolRequirement.ParametersAtCall),
            new("database_schema", ToolRequirement.ParametersAtCall),
            new("mongodb_query", ToolRequirement.ParametersAtCall),
            new("mongodb_schema", ToolRequirement.ParametersAtCall),
            new("arcadedb_query", ToolRequirement.ParametersAtCall),
            new("janusgraph_query", ToolRequirement.ParametersAtCall),
            new("graph_schema", ToolRequirement.ParametersAtCall),
        ]),
        new(CodeFamily,
        [
            new("shell_command", ToolRequirement.ExpertSetting, ShellToolsSection.SectionPath),
        ]),
        new(SessionFamily,
        [
            new("session_store"),
            new("session_snip"),
            new("session_stats"),
            new("session_cost"),
            new("token_budget"),
            new("memory_store"),
        ]),
        new(EventsFamily,
        [
            new("publish_event"),
            new("post_message"),
            new("send_request"),
            new("reply_to"),
            new("receive_message"),
            new("wait_for_event"),
            new("get_last_value"),
        ]),
        new(AnalysisFamily,
        [
            new("index_codebase"),
            new("incremental_reindex"),
            new("index_status"),
            new("is_path_indexed"),
            new("codebase_map"),
            new("package_summary"),
            new("symbol_detail"),
            new("symbol_source"),
            new("codebase_search"),
            new("dependency_graph"),
            new("sub_graph"),
            new("flow_trace"),
            new("impact_analysis"),
            new("complexity_report"),
            new("statement_query"),
        ]),
        new(CollaborationFamily,
        [
            new("human_input"),
            new("ask_question_to_coworker"),
            new("delegate_work_to_coworker"),
        ]),
        new(MountsFamily,
        [
            new("list_mounts"),
        ]),
    ];

    /// <summary>Every tool of every family.</summary>
    public static IEnumerable<ToolInfo> All => Families.SelectMany(family => family.Tools);
}
