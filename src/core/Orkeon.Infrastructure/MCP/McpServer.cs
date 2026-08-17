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
/// MCP server that exposes Orkeon tools to external MCP clients.
/// Supports running over stdio or processing individual requests.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class McpServer
{
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
    /// Processes a single JSON-RPC request and returns the response.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "JSON-RPC boundary fault barrier: any handler failure is converted into a JSON-RPC InternalError response so one bad request cannot crash the MCP server.")]
    public Task<JsonRpcResponse> ProcessRequestAsync(
        JsonRpcRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProcessRequestCoreAsync();

        async Task<JsonRpcResponse> ProcessRequestCoreAsync()
        {
            try
            {
                return request.Method switch
                {
                    "initialize" => HandleInitialize(request),
                    "tools/list" => await HandleToolsList(request).ConfigureAwait(false),
                    "tools/call" => await HandleToolsCall(request, ct).ConfigureAwait(false),
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
    /// line by line and writing responses.
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

    private JsonRpcResponse HandleInitialize(JsonRpcRequest request)
    {
        var result = new McpInitializeResult
        {
            ProtocolVersion = "2024-11-05",
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

    private async Task<JsonRpcResponse> HandleToolsList(JsonRpcRequest request)
    {
        var tools = await _toolRegistry.GetAllToolsAsync().ConfigureAwait(false);

        var mcpTools = tools.Select(t => new McpToolDefinition
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = ConvertToolSchemaToJsonSchema(t.Schema)
        }).ToList();

        var result = new McpToolListResult { Tools = mcpTools };
        return CreateSuccessResponse(request.Id, result);
    }

    private async Task<JsonRpcResponse> HandleToolsCall(JsonRpcRequest request, CancellationToken ct)
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

        return CreateSuccessResponse(request.Id, mcpResult);
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

    private static JsonRpcResponse CreateSuccessResponse(int? id, object result)
    {
        var json = JsonSerializer.Serialize(result);
        return new JsonRpcResponse
        {
            Id = id,
            Result = JsonDocument.Parse(json).RootElement
        };
    }

    private static JsonRpcResponse CreateErrorResponse(int? id, int code, string message)
    {
        return new JsonRpcResponse
        {
            Id = id,
            Error = new JsonRpcError(code, message)
        };
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Error processing request: {Method}")]
    private partial void LogErrorProcessingRequest(Exception ex, string method);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "MCP server starting on stdio")]
    private partial void LogMcpServerStarting();

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Failed to parse request: {Line}")]
    private partial void LogFailedToParseRequest(Exception ex, string line);
}
