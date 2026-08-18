using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// Dual-era MCP server that exposes Orkeon tools to external MCP clients:
/// it serves modern, stateless requests (2026-07-28 — per-request `_meta`,
/// `server/discover`) and legacy initialize-handshake clients (2025-11-25
/// and earlier) on the same endpoint. Supports running over stdio or
/// processing individual requests.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class McpServer
{
    /// <summary>Freshness hint returned on tools/list (the DI tool set is stable for a process).</summary>
    private const long ToolListTtlMs = 60_000;

    private readonly IToolRegistry _toolRegistry;
    private readonly McpServerOptions _options;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of <see cref="McpServer"/>.</summary>
    /// <param name="toolRegistry">The tool registry exposing Orkeon tools.</param>
    /// <param name="options">The MCP server options.</param>
    /// <param name="logger">Optional logger.</param>
    public McpServer(
        IToolRegistry toolRegistry,
        IOptions<McpServerOptions> options,
        ILogger<McpServer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        _toolRegistry = toolRegistry;
        _options = options?.Value ?? new McpServerOptions();
        _logger = logger ?? NullLogger<McpServer>.Instance;
    }

    /// <summary>Initializes a new instance of <see cref="McpServer"/> with direct options (useful for testing).</summary>
    /// <param name="toolRegistry">The tool registry exposing Orkeon tools.</param>
    /// <param name="options">The MCP server options.</param>
    /// <param name="logger">Optional logger.</param>
    public McpServer(IToolRegistry toolRegistry, McpServerOptions options, ILogger<McpServer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        _toolRegistry = toolRegistry;
        _options = options ?? new McpServerOptions();
        _logger = logger ?? NullLogger<McpServer>.Instance;
    }

    /// <summary>
    /// Processes a single JSON-RPC request and returns the response, or null when the
    /// message is a notification (which must not be answered).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "JSON-RPC boundary fault barrier: any handler failure is converted into a JSON-RPC InternalError response so one bad request cannot crash the MCP server.")]
    public Task<JsonRpcResponse?> ProcessRequestAsync(
        JsonRpcRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProcessRequestCoreAsync();

        async Task<JsonRpcResponse?> ProcessRequestCoreAsync()
        {
            try
            {
                // Notifications (no id) are processed silently and never answered.
                // A JSON `"id": null` deserializes as a Null-kind element, not a C# null.
                if (request.Id is null ||
                    request.Id.Value.ValueKind == JsonValueKind.Null ||
                    request.Method.StartsWith("notifications/", StringComparison.Ordinal))
                {
                    LogNotificationReceived(request.Method);
                    return null;
                }

                // Modern requests declare their protocol version per request; a declared
                // version we do not support is rejected independently of the method.
                var requestedVersion = McpProtocol.TryReadRequestedVersion(request.Params);
                if (requestedVersion != null &&
                    !McpProtocol.SupportedVersions.Contains(requestedVersion))
                {
                    return new JsonRpcResponse
                    {
                        Id = request.Id,
                        Error = McpProtocol.CreateUnsupportedVersionError(requestedVersion)
                    };
                }

                var isModern = requestedVersion != null;

                return request.Method switch
                {
                    "server/discover" => HandleDiscover(request),
                    "initialize" => HandleInitialize(request),
                    // ping was removed from the modern lineage; it stays served for legacy sessions.
                    "ping" when !isModern => CreateSuccessResponse(request.Id, new { }),
                    "tools/list" => await HandleToolsList(request, isModern).ConfigureAwait(false),
                    "tools/call" => await HandleToolsCall(request, isModern, ct).ConfigureAwait(false),
                    _ => CreateErrorResponse(request.Id, JsonRpcErrorCodes.MethodNotFound,
                        $"Method not found: {request.Method}")
                };
            }
            catch (Exception ex)
            {
                LogErrorProcessingRequest(ex, request.Method);
                return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InternalError, ex.Message);
            }
        }
    }

    /// <summary>
    /// Runs the MCP server on stdin/stdout, reading JSON-RPC messages
    /// line by line and writing responses (notifications get none).
    /// </summary>
    public async Task RunStdioAsync(CancellationToken ct = default)
    {
        LogMcpServerStarting();

        using var reader = Console.In;
        using var writer = Console.Out;

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var request = JsonSerializer.Deserialize<JsonRpcRequest>(line);
                if (request == null) continue;

                var response = await ProcessRequestAsync(request, ct).ConfigureAwait(false);
                if (response == null) continue;

                var responseJson = JsonSerializer.Serialize(response);
                await writer.WriteLineAsync(responseJson.AsMemory(), ct).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                LogFailedToParseRequest(ex, line);
                var errorResponse = CreateErrorResponse(null, JsonRpcErrorCodes.ParseError,
                    "Failed to parse JSON-RPC request");
                var errorJson = JsonSerializer.Serialize(errorResponse);
                await writer.WriteLineAsync(errorJson.AsMemory(), ct).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private JsonRpcResponse HandleDiscover(JsonRpcRequest request)
    {
        var result = new McpDiscoverResult
        {
            SupportedVersions = McpProtocol.SupportedVersions,
            Capabilities = new McpServerCapabilities
            {
                Tools = new McpCapabilityInfo { ListChanged = true }
            },
            TtlMs = ToolListTtlMs,
            CacheScope = "private",
            Meta = BuildServerInfoMeta()
        };

        return CreateSuccessResponse(request.Id, result);
    }

    private JsonRpcResponse HandleInitialize(JsonRpcRequest request)
    {
        // Legacy negotiation: echo the requested revision when we support it,
        // otherwise answer with our newest legacy revision (the legacy spec then
        // leaves the decision to the client).
        string? requested = null;
        if (request.Params is { ValueKind: JsonValueKind.Object } p &&
            p.TryGetProperty("protocolVersion", out var v) &&
            v.ValueKind == JsonValueKind.String)
        {
            requested = v.GetString();
        }

        var negotiated = requested != null && McpProtocol.SupportedLegacyVersions.Contains(requested)
            ? requested
            : McpProtocol.SupportedLegacyVersions[0];

        LogLegacyInitialize(requested, negotiated);

        var result = new McpInitializeResult
        {
            ProtocolVersion = negotiated,
            Capabilities = new McpServerCapabilities
            {
                Tools = new McpCapabilityInfo { ListChanged = true }
            },
            ServerInfo = new McpServerInfo
            {
                Name = _options.Name,
                Version = _options.Version
            }
        };

        return CreateSuccessResponse(request.Id, result);
    }

    private async Task<JsonRpcResponse> HandleToolsList(JsonRpcRequest request, bool isModern)
    {
        var tools = await _toolRegistry.GetAllToolsAsync().ConfigureAwait(false);

        // Deterministic order (2026-07-28 SHOULD): stable client caches, better prompt caching.
        var mcpTools = tools
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new McpToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = ConvertToolSchemaToJsonSchema(t.Schema)
            }).ToList();

        var result = new McpToolListResult
        {
            Tools = mcpTools,
            // Additive fields: legacy clients ignore unknown members; modern clients require them.
            ResultType = "complete",
            TtlMs = ToolListTtlMs,
            CacheScope = "private",
            Meta = isModern ? BuildServerInfoMeta() : null
        };
        return CreateSuccessResponse(request.Id, result);
    }

    private async Task<JsonRpcResponse> HandleToolsCall(JsonRpcRequest request, bool isModern, CancellationToken ct)
    {
        McpToolCallParams? callParams = null;
        if (request.Params.HasValue)
        {
            callParams = JsonSerializer.Deserialize<McpToolCallParams>(
                request.Params.Value.GetRawText());
        }

        if (callParams == null || string.IsNullOrEmpty(callParams.Name))
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams,
                "Missing tool name in parameters");
        }

        var tool = await _toolRegistry.GetToolByNameAsync(callParams.Name).ConfigureAwait(false);
        if (tool == null)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams,
                $"Tool not found: {callParams.Name}");
        }

        var parameters = new Dictionary<string, object?>();
        if (callParams.Arguments.HasValue &&
            callParams.Arguments.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in callParams.Arguments.Value.EnumerateObject())
            {
                parameters[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString()!,
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => prop.Value.GetRawText()
                };
            }
        }

        var toolRequest = new ToolCallRequest(callParams.Name, parameters);
        var response = await tool.CallAsync(toolRequest, ct).ConfigureAwait(false);

        var mcpResult = new McpToolCallResult
        {
            IsError = !response.Success,
            ResultType = "complete",
            Content =
            [
                new()
                {
                    Type = "text",
                    Text = response.Success
                        ? response.Result?.ToString() ?? ""
                        : response.Error ?? "Unknown error"
                }
            ]
        };
        _ = isModern; // both eras share the same result shape for tools/call

        return CreateSuccessResponse(request.Id, mcpResult);
    }

    private JsonElement BuildServerInfoMeta()
    {
        return JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            [McpProtocol.MetaServerInfo] = new { name = _options.Name, version = _options.Version }
        });
    }

    /// <summary>
    /// Converts a Orkeon ToolSchema to a JSON Schema element for MCP.
    /// </summary>
    public static JsonElement? ConvertToolSchemaToJsonSchema(ToolSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var jsonSchema = new Dictionary<string, object>
        {
            ["type"] = "object"
        };

        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var kvp in schema.Parameters)
        {
            var paramSchema = new Dictionary<string, object>
            {
                ["type"] = kvp.Value.Type,
                ["description"] = kvp.Value.Description
            };

            if (kvp.Value.Default != null)
                paramSchema["default"] = kvp.Value.Default;

            if (kvp.Value.Enum != null && kvp.Value.Enum.Count > 0)
                paramSchema["enum"] = kvp.Value.Enum;

            properties[kvp.Key] = paramSchema;

            if (kvp.Value.Required)
                required.Add(kvp.Key);
        }

        jsonSchema["properties"] = properties;
        if (required.Count > 0)
            jsonSchema["required"] = required;

        var json = JsonSerializer.Serialize(jsonSchema);
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonRpcResponse CreateSuccessResponse(JsonElement? id, object result)
    {
        var json = JsonSerializer.Serialize(result);
        return new JsonRpcResponse
        {
            Id = id,
            Result = JsonDocument.Parse(json).RootElement
        };
    }

    private static JsonRpcResponse CreateErrorResponse(JsonElement? id, int code, string message)
    {
        return new JsonRpcResponse
        {
            Id = id,
            Error = new JsonRpcError(code, message)
        };
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Error processing request: {Method}")]
    private partial void LogErrorProcessingRequest(Exception ex, string method);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "MCP server starting on stdio (dual-era: {Modern} + legacy initialize)", SkipEnabledCheck = false)]
    private partial void LogMcpServerStartingCore(string modern);

    private void LogMcpServerStarting() => LogMcpServerStartingCore(McpProtocol.ModernVersion);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Failed to parse request: {Line}")]
    private partial void LogFailedToParseRequest(Exception ex, string line);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "MCP notification received: {Method}")]
    private partial void LogNotificationReceived(string method);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Legacy MCP initialize: requested {Requested}, negotiated {Negotiated}")]
    private partial void LogLegacyInitialize(string? requested, string negotiated);
}
