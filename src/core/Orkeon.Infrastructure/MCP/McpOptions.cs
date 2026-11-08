using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// Top-level MCP configuration options: the client's. No option starts the server —
/// <c>AddOrkeonMcpServer</c> registers it and <c>orkeon mcp serve</c> runs it (GAP-24).
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpOptions
{
    /// <summary>
    /// Whether MCP is enabled at all.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// MCP server connections to establish as a client.
    /// Key is the server identifier.
    /// </summary>
    public Dictionary<string, McpServerConfig> Servers { get; } = [];
}

/// <summary>
/// Configuration for the Orkeon MCP server.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpServerOptions
{
    /// <summary>
    /// Server name advertised during MCP initialization.
    /// </summary>
    public string Name { get; set; } = "Orkeon";

    /// <summary>
    /// Server version advertised during MCP initialization.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// How many requests the stdio loop runs at once; the others wait their turn. At least 1.
    /// <c>orkeon mcp serve --max-concurrency</c> overrides it.
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// Whether the server serves <c>/output</c> as read-only MCP resources
    /// (<c>orkeon:///output/&lt;path&gt;</c>). Off by default: nothing but the tools is exposed.
    /// <c>/workspace</c> and every other mount are never exposed. <c>orkeon mcp serve --expose-output</c> sets it.
    /// </summary>
    public bool ExposeOutput { get; set; }

    /// <summary>
    /// The registry tools the server exposes and runs, by name (case-insensitive); empty serves them all. A tool outside
    /// the list is neither listed nor callable, whoever registers it and whenever: a tool an MCP server adds later
    /// stays out. The tools an extension serves (<c>--crew</c>) are not subject to it. <c>orkeon mcp serve --tools</c> sets it.
    /// </summary>
    public IList<string> AllowedTools { get; } = [];

    /// <summary>How many resources one <c>resources/list</c> page carries. At least 1.</summary>
    public int ResourcesPageSize { get; set; } = 100;

    /// <summary>The largest file <c>resources/read</c> returns, in bytes; a bigger one is refused.</summary>
    public int MaxResourceBytes { get; set; } = 1_048_576;
}
