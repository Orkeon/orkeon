using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// Top-level MCP configuration options.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpOptions
{
    /// <summary>
    /// Whether MCP is enabled at all.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to run an MCP server exposing Orkeon tools.
    /// </summary>
    public bool EnableServer { get; set; }

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
    /// Whether to expose resources capability.
    /// </summary>
    public bool ExposeResources { get; set; }

    /// <summary>
    /// Whether to expose prompts capability.
    /// </summary>
    public bool ExposePrompts { get; set; }
}
