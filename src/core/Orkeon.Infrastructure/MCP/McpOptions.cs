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
}
