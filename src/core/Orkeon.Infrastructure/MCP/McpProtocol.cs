using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// Protocol-level constants and helpers shared by the MCP client and server:
/// supported revisions, the modern per-request <c>_meta</c> keys (2026-07-28),
/// and the legacy initialize-handshake versions kept for backward compatibility.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public static class McpProtocol
{
    /// <summary>The current, stateless protocol revision (per-request `_meta`, `server/discover`).</summary>
    public const string ModernVersion = "2026-07-28";

    /// <summary>
    /// Legacy (initialize-handshake) revisions this implementation can negotiate, newest first.
    /// 2025-03-26 is deliberately absent: it is the only revision that mandates JSON-RPC
    /// batching, which this implementation does not speak.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedLegacyVersions =
        ["2025-11-25", "2025-06-18", "2024-11-05"];

    /// <summary>Every protocol revision this implementation supports, newest first.</summary>
    public static readonly IReadOnlyList<string> SupportedVersions =
        [ModernVersion, "2025-11-25", "2025-06-18", "2024-11-05"];

    /// <summary>`_meta` key carrying the protocol version of a modern request.</summary>
    public const string MetaProtocolVersion = "io.modelcontextprotocol/protocolVersion";

    /// <summary>`_meta` key carrying the client identity of a modern request.</summary>
    public const string MetaClientInfo = "io.modelcontextprotocol/clientInfo";

    /// <summary>`_meta` key carrying the client capabilities of a modern request.</summary>
    public const string MetaClientCapabilities = "io.modelcontextprotocol/clientCapabilities";

    /// <summary>`_meta` key carrying the server identity in a modern result.</summary>
    public const string MetaServerInfo = "io.modelcontextprotocol/serverInfo";

    /// <summary>HTTP header carrying the protocol version on Streamable HTTP.</summary>
    public const string ProtocolVersionHeader = "MCP-Protocol-Version";

    /// <summary>HTTP header carrying the JSON-RPC method on Streamable HTTP POSTs.</summary>
    public const string MethodHeader = "Mcp-Method";

    /// <summary>HTTP header carrying the primary entity name (tool/prompt/resource) on Streamable HTTP POSTs.</summary>
    public const string NameHeader = "Mcp-Name";

    /// <summary>
    /// Reads the modern protocol version declared in <c>params._meta</c>, or null when the
    /// request carries no modern metadata (i.e. it belongs to the legacy lineage).
    /// </summary>
    public static string? TryReadRequestedVersion(JsonElement? parameters)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } p)
            return null;
        if (!p.TryGetProperty("_meta", out var meta) || meta.ValueKind != JsonValueKind.Object)
            return null;
        if (!meta.TryGetProperty(MetaProtocolVersion, out var v) || v.ValueKind != JsonValueKind.String)
            return null;
        return v.GetString();
    }

    /// <summary>
    /// Merges modern `_meta` (protocol version, client info, client capabilities) into a
    /// request's parameters, creating the params object when the request has none.
    /// </summary>
    public static JsonElement BuildParamsWithMeta(
        object? parameters, string protocolVersion, string clientName, string clientVersion)
    {
        var dict = new Dictionary<string, object?>();
        if (parameters != null)
        {
            var element = parameters is JsonElement je
                ? je
                : JsonSerializer.SerializeToElement(parameters);
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                    dict[prop.Name] = prop.Value;
            }
        }

        dict["_meta"] = new Dictionary<string, object?>
        {
            [MetaProtocolVersion] = protocolVersion,
            [MetaClientInfo] = new { name = clientName, version = clientVersion },
            [MetaClientCapabilities] = new { }
        };

        return JsonSerializer.SerializeToElement(dict);
    }

    /// <summary>
    /// Builds the standard <c>UnsupportedProtocolVersionError</c> payload
    /// (<c>{ supported, requested }</c>) for a rejected request.
    /// </summary>
    public static JsonRpcError CreateUnsupportedVersionError(string requested)
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            supported = SupportedVersions,
            requested
        });
        return new JsonRpcError(McpErrorCodes.UnsupportedProtocolVersion, "Unsupported protocol version")
        {
            Data = data
        };
    }

    /// <summary>
    /// Extracts the server's supported versions from an
    /// <c>UnsupportedProtocolVersionError</c>'s data, or an empty list.
    /// </summary>
    public static IReadOnlyList<string> ReadSupportedVersionsFromError(JsonRpcError error)
    {
        if (error?.Data is not { ValueKind: JsonValueKind.Object } data)
            return [];
        if (!data.TryGetProperty("supported", out var supported) || supported.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<string>();
        foreach (var item in supported.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                list.Add(item.GetString()!);
        }
        return list;
    }
}

/// <summary>
/// MCP-specification error codes (the `-32020`…`-32099` range reserved by the spec).
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public static class McpErrorCodes
{
    /// <summary>Streamable HTTP header does not match the request body (-32020).</summary>
    public const int HeaderMismatch = -32020;

    /// <summary>The request requires a client capability that was not declared (-32021).</summary>
    public const int MissingRequiredClientCapability = -32021;

    /// <summary>The requested protocol version is not supported by the server (-32022).</summary>
    public const int UnsupportedProtocolVersion = -32022;
}
