using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// Transport type for connecting to an MCP server.
/// </summary>
public enum McpTransportType
{
    /// <summary>Standard input/output transport (subprocess).</summary>
    Stdio,

    /// <summary>Server-sent events transport (HTTP).</summary>
    Sse
}

/// <summary>
/// Configuration for connecting to an MCP server.
/// </summary>
public class McpServerConfig
{
    /// <summary>
    /// Command to launch the MCP server process (for stdio transport).
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>
    /// Command-line arguments for the server process.
    /// </summary>
    public IReadOnlyList<string>? Args { get; set; }

    /// <summary>
    /// Environment variables for the server process.
    /// </summary>
    public Dictionary<string, string> Env { get; } = [];

    /// <summary>
    /// URL endpoint for SSE transport.
    /// </summary>
    public Uri? Url { get; set; }

    /// <summary>
    /// Transport type to use.
    /// </summary>
    public McpTransportType Transport { get; set; } = McpTransportType.Stdio;
}

/// <summary>
/// MCP tool definition as returned by tools/list.
/// </summary>
public class McpToolDefinition
{
    /// <summary>Gets or sets the tool name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the human-readable tool description.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>Gets or sets the JSON Schema describing the tool's input parameters.</summary>
    [JsonPropertyName("inputSchema")]
    public JsonElement? InputSchema { get; set; }
}

/// <summary>
/// Result of an MCP tool call.
/// </summary>
public class McpToolCallResult
{
    /// <summary>Gets the content blocks returned by the tool.</summary>
    [JsonPropertyName("content")]
    public IReadOnlyList<McpContent> Content { get; init; } = [];

    /// <summary>Gets or sets whether the result represents an error.</summary>
    [JsonPropertyName("isError")]
    public bool? IsError { get; set; }
}

/// <summary>
/// Content block in an MCP response.
/// </summary>
public class McpContent
{
    /// <summary>Gets or sets the content type (e.g., "text", "image").</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    /// <summary>Gets or sets the text content for text-type blocks.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Gets or sets the MIME type for binary content blocks.</summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>Gets or sets the Base64-encoded data for binary content blocks.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }
}

/// <summary>
/// MCP resource descriptor.
/// </summary>
public class McpResource
{
    /// <summary>Gets or sets the resource URI.</summary>
    [JsonPropertyName("uri")]
    public Uri? Uri { get; set; }

    /// <summary>Gets or sets the resource name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the optional resource description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets or sets the MIME type of the resource content.</summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }
}

/// <summary>
/// Capabilities advertised by an MCP server.
/// </summary>
public class McpServerCapabilities
{
    /// <summary>Gets or sets the tools capability info, if supported.</summary>
    [JsonPropertyName("tools")]
    public McpCapabilityInfo? Tools { get; set; }

    /// <summary>Gets or sets the resources capability info, if supported.</summary>
    [JsonPropertyName("resources")]
    public McpCapabilityInfo? Resources { get; set; }

    /// <summary>Gets or sets the prompts capability info, if supported.</summary>
    [JsonPropertyName("prompts")]
    public McpCapabilityInfo? Prompts { get; set; }
}

/// <summary>
/// Info about a specific capability.
/// </summary>
public class McpCapabilityInfo
{
    /// <summary>Gets or sets whether the server supports list-changed notifications for this capability.</summary>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }
}

/// <summary>
/// Runtime status of a connected MCP server.
/// </summary>
public class McpServerStatus
{
    /// <summary>Gets or sets the server identifier.</summary>
    public string ServerId { get; set; } = "";

    /// <summary>Gets or sets whether the server transport is currently connected.</summary>
    public bool IsConnected { get; set; }

    /// <summary>Gets or sets the capabilities advertised by the server.</summary>
    public McpServerCapabilities? Capabilities { get; set; }
}

/// <summary>
/// Parameters for tools/call.
/// </summary>
public class McpToolCallParams
{
    /// <summary>Gets or sets the name of the tool to call.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the tool arguments as a JSON object.</summary>
    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; set; }
}

/// <summary>
/// Result of tools/list.
/// </summary>
public class McpToolListResult
{
    /// <summary>Gets the list of available tools.</summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<McpToolDefinition> Tools { get; init; } = [];
}

/// <summary>
/// Result of resources/list.
/// </summary>
public class McpResourceListResult
{
    /// <summary>Gets the list of available resources.</summary>
    [JsonPropertyName("resources")]
    public IReadOnlyList<McpResource> Resources { get; init; } = [];
}

/// <summary>
/// Result of initialize handshake.
/// </summary>
public class McpInitializeResult
{
    /// <summary>Gets or sets the MCP protocol version the server implements.</summary>
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "";

    /// <summary>Gets or sets the server's declared capabilities.</summary>
    [JsonPropertyName("capabilities")]
    public McpServerCapabilities Capabilities { get; set; } = new();

    /// <summary>Gets or sets optional server identification metadata.</summary>
    [JsonPropertyName("serverInfo")]
    public McpServerInfo? ServerInfo { get; set; }
}

/// <summary>
/// MCP server identification.
/// </summary>
public class McpServerInfo
{
    /// <summary>Gets or sets the server implementation name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the server version string.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
