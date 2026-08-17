using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// MCP protocol client. Manages the initialize handshake and exposes
/// tools/list, tools/call, resources/list, and resources/read RPCs.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class McpClient : IAsyncDisposable
{
    private readonly IMcpTransport _transport;
    private readonly ILogger _logger;
    private int _nextId;
    private McpServerCapabilities? _capabilities;
    private McpServerInfo? _serverInfo;
    private bool _initialized;

    /// <summary>
    /// Capabilities advertised by the server after initialization.
    /// </summary>
    public McpServerCapabilities? Capabilities => _capabilities;

    /// <summary>
    /// Server info returned during initialization.
    /// </summary>
    public McpServerInfo? ServerInfo => _serverInfo;

    /// <summary>
    /// Whether the client has completed the initialize handshake.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>Initializes a new instance of <see cref="McpClient"/>.</summary>
    /// <param name="transport">The MCP transport to communicate over.</param>
    /// <param name="logger">Optional logger.</param>
    public McpClient(IMcpTransport transport, ILogger<McpClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _logger = logger ?? NullLogger<McpClient>.Instance;
    }

    /// <summary>
    /// Performs the MCP initialize handshake.
    /// </summary>
    public async Task<McpInitializeResult> InitializeAsync(CancellationToken ct = default)
    {
        if (!_transport.IsConnected)
        {
            await _transport.ConnectAsync(ct).ConfigureAwait(false);
        }

        var initParams = new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "Orkeon", version = "1.0.0" }
        };

        var request = CreateRequest("initialize", initParams);
        var response = await _transport.SendRequestAsync(request, ct).ConfigureAwait(false);

        if (response.Error != null)
        {
            throw new InvalidOperationException(
                $"MCP initialization failed: [{response.Error.Code}] {response.Error.Message}");
        }

        var result = DeserializeResult<McpInitializeResult>(response);
        _capabilities = result.Capabilities;
        _serverInfo = result.ServerInfo;
        _initialized = true;

        // Send initialized notification (no response expected, so we send as a request that we ignore)
        LogMcpClientInitialized(_serverInfo?.Name, _serverInfo?.Version);

        return result;
    }

    /// <summary>
    /// Lists tools available on the MCP server.
    /// </summary>
    public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken ct = default)
    {
        EnsureInitialized();

        var request = CreateRequest("tools/list");
        var response = await _transport.SendRequestAsync(request, ct).ConfigureAwait(false);

        if (response.Error != null)
        {
            throw new InvalidOperationException(
                $"tools/list failed: [{response.Error.Code}] {response.Error.Message}");
        }

        var result = DeserializeResult<McpToolListResult>(response);
        return result.Tools;
    }

    /// <summary>
    /// Calls a tool on the MCP server.
    /// </summary>
    public async Task<McpToolCallResult> CallToolAsync(
        string name, JsonElement? arguments = null, CancellationToken ct = default)
    {
        EnsureInitialized();

        var callParams = new McpToolCallParams { Name = name, Arguments = arguments };
        var request = CreateRequest("tools/call", callParams);
        var response = await _transport.SendRequestAsync(request, ct).ConfigureAwait(false);

        if (response.Error != null)
        {
            return new McpToolCallResult
            {
                IsError = true,
                Content =
                [
                    new() { Type = "text", Text = $"[{response.Error.Code}] {response.Error.Message}" }
                ]
            };
        }

        return DeserializeResult<McpToolCallResult>(response);
    }

    /// <summary>
    /// Lists resources available on the MCP server.
    /// </summary>
    public async Task<IReadOnlyList<McpResource>> ListResourcesAsync(CancellationToken ct = default)
    {
        EnsureInitialized();

        var request = CreateRequest("resources/list");
        var response = await _transport.SendRequestAsync(request, ct).ConfigureAwait(false);

        if (response.Error != null)
        {
            throw new InvalidOperationException(
                $"resources/list failed: [{response.Error.Code}] {response.Error.Message}");
        }

        var result = DeserializeResult<McpResourceListResult>(response);
        return result.Resources;
    }

    /// <summary>
    /// Reads a resource by URI.
    /// </summary>
    public Task<McpToolCallResult> ReadResourceAsync(Uri uri, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return ReadResourceCoreAsync();

        async Task<McpToolCallResult> ReadResourceCoreAsync()
        {
            EnsureInitialized();

            var readParams = new { uri = uri.ToString() };
            var request = CreateRequest("resources/read", readParams);
            var response = await _transport.SendRequestAsync(request, ct).ConfigureAwait(false);

            if (response.Error != null)
            {
                throw new InvalidOperationException(
                    $"resources/read failed: [{response.Error.Code}] {response.Error.Message}");
            }

            return DeserializeResult<McpToolCallResult>(response);
        }
    }

    private JsonRpcRequest CreateRequest(string method, object? parameters = null)
    {
        var request = new JsonRpcRequest
        {
            Method = method,
            Id = Interlocked.Increment(ref _nextId)
        };

        if (parameters != null)
        {
            var json = JsonSerializer.Serialize(parameters);
            request.Params = JsonDocument.Parse(json).RootElement;
        }

        return request;
    }

    private static T DeserializeResult<T>(JsonRpcResponse response) where T : new()
    {
        if (response.Result == null)
            return new T();

        return JsonSerializer.Deserialize<T>(response.Result.Value.GetRawText()) ?? new T();
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("MCP client has not been initialized. Call InitializeAsync first.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "MCP client initialized with server {Name} v{Version}")]
    private partial void LogMcpClientInitialized(string? name, string? version);
}
