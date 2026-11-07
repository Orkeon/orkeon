using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// Transport type for connecting to an MCP server.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
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
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
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

    /// <summary>
    /// Whether the permission gate takes this server's tool annotations at their word: a tool marked
    /// readOnlyHint is then a read, which plan mode lets run. Off by default: an untrusted server could
    /// label a destructive tool read-only, so its tools stay classified fail-closed.
    /// </summary>
    public bool TrustAnnotations { get; set; }

    /// <summary>
    /// Extra HTTP headers (SSE transport) sent on every request: a literal value, or <c>env:VAR</c> to read
    /// it from an environment variable when the server is connected. An <c>Authorization</c> header is
    /// refused here; credentials go through <see cref="Auth"/>.
    /// </summary>
    public Dictionary<string, string> Headers { get; } = [];

    /// <summary>Credentials sent with every request, read from the environment (SSE transport).</summary>
    public McpAuthConfig Auth { get; } = new();

    /// <summary>Which of the server's tools are registered.</summary>
    public McpToolFilterConfig Tools { get; } = new();

    /// <summary>How long one tool call may take before it ends as a tool error. 60 seconds by default.</summary>
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long the handshake and the tool listing of this server may take before the connection fails.
    /// 30 seconds by default; a value that is not positive is refused.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>How a server authenticates the client.</summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public enum McpAuthType
{
    /// <summary>No credential.</summary>
    None,

    /// <summary><c>Authorization: Bearer</c>, the token read from <see cref="McpAuthConfig.TokenEnv"/>.</summary>
    Bearer,

    /// <summary><c>Authorization: Basic</c>, from <see cref="McpAuthConfig.UserEnv"/> and <see cref="McpAuthConfig.PasswordEnv"/>.</summary>
    Basic
}

/// <summary>The <c>Auth</c> block of a server: the names of the environment variables that hold the credentials.</summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpAuthConfig
{
    /// <summary>The authentication scheme.</summary>
    public McpAuthType Type { get; set; } = McpAuthType.None;

    /// <summary>The environment variable holding the bearer token.</summary>
    public string? TokenEnv { get; set; }

    /// <summary>The environment variable holding the basic-auth user name.</summary>
    public string? UserEnv { get; set; }

    /// <summary>The environment variable holding the basic-auth password.</summary>
    public string? PasswordEnv { get; set; }
}

/// <summary>
/// The <c>Tools</c> block of a server. An empty <see cref="Allow"/> keeps every tool; <see cref="Deny"/>
/// then removes names, so a name in both is denied.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpToolFilterConfig
{
    /// <summary>Tool names to register; empty means all.</summary>
    public Collection<string> Allow { get; } = [];

    /// <summary>Tool names never registered, applied after <see cref="Allow"/>.</summary>
    public Collection<string> Deny { get; } = [];
}

/// <summary>A tool call that outlived <see cref="McpServerConfig.CallTimeout"/>.</summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
[SuppressMessage("Design", "CA1032", Justification = "Raised only by this library with a settings key; the standard constructors would build an exception without one.")]
public sealed class McpCallTimeoutException : TimeoutException
{
    /// <summary>Creates the exception for <paramref name="toolName"/>.</summary>
    public McpCallTimeoutException(string toolName, TimeSpan timeout)
        : base($"The MCP tool '{toolName}' gave no answer within {timeout.TotalSeconds:0.###} s (MCP CallTimeout).")
    {
        ToolName = toolName;
        Timeout = timeout;
    }

    /// <summary>The tool that did not answer.</summary>
    public string ToolName { get; }

    /// <summary>The delay it was given.</summary>
    public TimeSpan Timeout { get; }
}

/// <summary>
/// MCP tool definition as returned by tools/list.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
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

    /// <summary>
    /// Gets or sets the behaviour hints the server declares for the tool (the MCP <c>annotations</c>);
    /// <see langword="null"/> when the server sends none.
    /// </summary>
    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpToolAnnotations? Annotations { get; set; }
}

/// <summary>
/// Behaviour hints of an MCP tool (<c>annotations</c> of the MCP specification). Each hint is
/// <see langword="null"/> when the server leaves it out; they are hints, not guarantees.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpToolAnnotations
{
    /// <summary>Gets or sets a human-readable title for the tool.</summary>
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    /// <summary>Gets or sets whether the tool does not modify its environment.</summary>
    [JsonPropertyName("readOnlyHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReadOnlyHint { get; set; }

    /// <summary>Gets or sets whether the tool may perform destructive updates.</summary>
    [JsonPropertyName("destructiveHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DestructiveHint { get; set; }

    /// <summary>Gets or sets whether repeating a call with the same arguments has no further effect.</summary>
    [JsonPropertyName("idempotentHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IdempotentHint { get; set; }

    /// <summary>Gets or sets whether the tool reaches an open world of external entities.</summary>
    [JsonPropertyName("openWorldHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? OpenWorldHint { get; set; }
}

/// <summary>
/// Result of an MCP tool call.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpToolCallResult
{
    /// <summary>Gets the content blocks returned by the tool.</summary>
    [JsonPropertyName("content")]
    public IReadOnlyList<McpContent> Content { get; init; } = [];

    /// <summary>Gets or sets whether the result represents an error.</summary>
    [JsonPropertyName("isError")]
    public bool? IsError { get; set; }

    /// <summary>
    /// Gets or sets the result type: "complete" for ordinary results, "input_required"
    /// for multi-round-trip interim results (absent on legacy servers = complete).
    /// </summary>
    [JsonPropertyName("resultType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResultType { get; set; }
}

/// <summary>
/// Content block in an MCP response.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
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
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
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
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
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
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpCapabilityInfo
{
    /// <summary>Gets or sets whether the server supports list-changed notifications for this capability.</summary>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }
}

/// <summary>
/// Runtime status of a connected MCP server.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpServerStatus
{
    /// <summary>Gets or sets the server identifier.</summary>
    public string ServerId { get; set; } = "";

    /// <summary>Gets or sets whether the server transport is currently connected.</summary>
    public bool IsConnected { get; set; }

    /// <summary>Gets or sets the capabilities advertised by the server.</summary>
    public McpServerCapabilities? Capabilities { get; set; }

    /// <summary>
    /// Gets the names of the tools this server exposes that were not registered because another
    /// tool already holds the name (a built-in tool, a script tool, or another server's tool).
    /// </summary>
    public IReadOnlyList<string> RejectedToolNames { get; init; } = [];

    /// <summary>
    /// Gets the <c>Tools:Allow</c> / <c>Tools:Deny</c> entries that matched no tool of the server, as
    /// <c>Allow:name</c> or <c>Deny:name</c>: likely a typo.
    /// </summary>
    public IReadOnlyList<string> UnmatchedToolFilters { get; init; } = [];
}

/// <summary>
/// Parameters for tools/call.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
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
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpToolListResult
{
    /// <summary>Gets the list of available tools.</summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<McpToolDefinition> Tools { get; init; } = [];

    /// <summary>Gets the opaque cursor of the next page, absent on the last one.</summary>
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }

    /// <summary>Gets or sets the result type ("complete"; absent on legacy servers, to be treated as complete).</summary>
    [JsonPropertyName("resultType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResultType { get; set; }

    /// <summary>Gets or sets the freshness hint, in milliseconds (2026-07-28 CacheableResult).</summary>
    [JsonPropertyName("ttlMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TtlMs { get; set; }

    /// <summary>Gets or sets the cache scope: "public" or "private" (2026-07-28 CacheableResult).</summary>
    [JsonPropertyName("cacheScope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CacheScope { get; set; }

    /// <summary>Gets or sets result metadata (carries the server identity on modern results).</summary>
    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Meta { get; set; }
}

/// <summary>
/// Result of resources/list.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpResourceListResult
{
    /// <summary>Gets the list of available resources.</summary>
    [JsonPropertyName("resources")]
    public IReadOnlyList<McpResource> Resources { get; init; } = [];

    /// <summary>Gets the opaque cursor of the next page, absent on the last one.</summary>
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }
}

/// <summary>
/// Result of initialize handshake.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
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
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpServerInfo
{
    /// <summary>Gets or sets the server implementation name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the server version string.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

/// <summary>
/// The protocol era a connected MCP server speaks (2026-07-28 stateless vs
/// initialize-handshake revisions).
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public enum McpProtocolEra
{
    /// <summary>Era not yet detected — no request has been exchanged.</summary>
    Unknown,

    /// <summary>Modern, stateless lineage: per-request `_meta`, `server/discover` (2026-07-28+).</summary>
    Modern,

    /// <summary>Legacy lineage: `initialize` handshake sessions (2025-11-25 and earlier).</summary>
    Legacy
}

/// <summary>
/// Result of the mandatory `server/discover` RPC (2026-07-28).
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpDiscoverResult
{
    /// <summary>Gets or sets the result type (always "complete" for discovery).</summary>
    [JsonPropertyName("resultType")]
    public string ResultType { get; set; } = "complete";

    /// <summary>Gets the protocol versions the server supports.</summary>
    [JsonPropertyName("supportedVersions")]
    public IReadOnlyList<string> SupportedVersions { get; init; } = [];

    /// <summary>Gets or sets the capabilities the server supports.</summary>
    [JsonPropertyName("capabilities")]
    public McpServerCapabilities Capabilities { get; set; } = new();

    /// <summary>Gets or sets optional natural-language guidance for LLMs using this server.</summary>
    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; set; }

    /// <summary>Gets or sets the freshness hint, in milliseconds.</summary>
    [JsonPropertyName("ttlMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TtlMs { get; set; }

    /// <summary>Gets or sets the cache scope ("public" or "private").</summary>
    [JsonPropertyName("cacheScope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CacheScope { get; set; }

    /// <summary>Gets or sets result metadata (carries the server identity under `io.modelcontextprotocol/serverInfo`).</summary>
    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Meta { get; set; }
}

/// <summary>An argument a prompt template takes.</summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpPromptArgument
{
    /// <summary>Gets or sets the argument name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Gets or sets what the argument is for.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets or sets whether the server needs the argument.</summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

/// <summary>A prompt template a server offers (<c>prompts/list</c>).</summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpPrompt
{
    /// <summary>Gets or sets the prompt name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Gets or sets what the prompt does.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets the arguments the prompt takes.</summary>
    [JsonPropertyName("arguments")]
    public IReadOnlyList<McpPromptArgument> Arguments { get; init; } = [];
}

/// <summary>One message of a prompt (<c>prompts/get</c>).</summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpPromptMessage
{
    /// <summary>Gets or sets the speaker: <c>user</c> or <c>assistant</c>.</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    /// <summary>Gets or sets the content block of the message.</summary>
    [JsonPropertyName("content")]
    public McpContent Content { get; set; } = new();
}

/// <summary>Result of <c>prompts/list</c>: one page.</summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpPromptListResult
{
    /// <summary>Gets the prompts of the page.</summary>
    [JsonPropertyName("prompts")]
    public IReadOnlyList<McpPrompt> Prompts { get; init; } = [];

    /// <summary>Gets the opaque cursor of the next page, absent on the last one.</summary>
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }
}

/// <summary>Result of <c>prompts/get</c>.</summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpPromptGetResult
{
    /// <summary>Gets or sets what the prompt does.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets the messages of the prompt, its arguments filled in.</summary>
    [JsonPropertyName("messages")]
    public IReadOnlyList<McpPromptMessage> Messages { get; init; } = [];
}
