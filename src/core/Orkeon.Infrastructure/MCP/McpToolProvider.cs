using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using Orkeon.Domain.Tools;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// Manages connections to multiple MCP servers and registers their
/// tools in the Orkeon tool registry.
/// </summary>
public partial class McpToolProvider : IAsyncDisposable
{
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ConcurrentDictionary<string, McpClientEntry> _clients = new();

    private sealed record McpClientEntry(
        McpClient Client,
        IMcpTransport Transport,
        List<string> RegisteredToolNames,
        McpServerCapabilities? Capabilities);

    /// <summary>Initializes a new instance of <see cref="McpToolProvider"/>.</summary>
    /// <param name="toolRegistry">The tool registry to register MCP tools into.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public McpToolProvider(
        IToolRegistry toolRegistry,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        _toolRegistry = toolRegistry;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<McpToolProvider>() ?? NullLogger<McpToolProvider>.Instance;
    }

    /// <summary>
    /// Connects to an MCP server, discovers its tools, and registers them in the tool registry.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the created transport is transferred to the McpClient (which disposes it in DisposeAsync); the client is stored in _clients and disposed by DisconnectServerAsync, or disposed on the catch path here on failure.")]
    public Task ConnectServerAsync(
        string serverId, McpServerConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        return ConnectServerCoreAsync();

        async Task ConnectServerCoreAsync()
        {
            if (_clients.ContainsKey(serverId))
                throw new InvalidOperationException($"Server '{serverId}' is already connected.");

            IMcpTransport transport = config.Transport switch
            {
                McpTransportType.Stdio => new StdioMcpTransport(config,
                    _loggerFactory?.CreateLogger<StdioMcpTransport>()),
                McpTransportType.Sse => new SseMcpTransport(
                    config.Url ?? throw new ArgumentException("URL required for SSE transport.", nameof(config)),
                    logger: _loggerFactory?.CreateLogger<SseMcpTransport>()),
                _ => throw new ArgumentException($"Unknown transport type: {config.Transport}", nameof(config))
            };

            var client = new McpClient(transport, _loggerFactory?.CreateLogger<McpClient>());

            try
            {
                var initResult = await client.InitializeAsync(ct).ConfigureAwait(false);
                var tools = await client.ListToolsAsync(ct).ConfigureAwait(false);

                var registeredNames = new List<string>();
                foreach (var toolDef in tools)
                {
                    var adapter = new McpToolAdapter(toolDef, client);
                    var registered = await _toolRegistry.RegisterToolAsync(adapter).ConfigureAwait(false);
                    if (registered)
                    {
                        registeredNames.Add(toolDef.Name);
                        LogRegisteredMcpTool(toolDef.Name, serverId);
                    }
                }

                _clients[serverId] = new McpClientEntry(
                    client, transport, registeredNames, initResult.Capabilities);

                LogConnectedToMcpServer(serverId, registeredNames.Count);
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    /// <summary>
    /// Connects to an MCP server using an externally-provided transport
    /// (useful for testing or custom transports).
    /// </summary>
    public async Task ConnectServerAsync(
        string serverId, IMcpTransport transport, CancellationToken ct = default)
    {
        if (_clients.ContainsKey(serverId))
            throw new InvalidOperationException($"Server '{serverId}' is already connected.");

        var client = new McpClient(transport, _loggerFactory?.CreateLogger<McpClient>());

        try
        {
            var initResult = await client.InitializeAsync(ct).ConfigureAwait(false);
            var tools = await client.ListToolsAsync(ct).ConfigureAwait(false);

            var registeredNames = new List<string>();
            foreach (var toolDef in tools)
            {
                var adapter = new McpToolAdapter(toolDef, client);
                var registered = await _toolRegistry.RegisterToolAsync(adapter).ConfigureAwait(false);
                if (registered)
                {
                    registeredNames.Add(toolDef.Name);
                }
            }

            _clients[serverId] = new McpClientEntry(
                client, transport, registeredNames, initResult.Capabilities);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Disconnects from an MCP server and unregisters its tools.
    /// </summary>
    public async Task DisconnectServerAsync(string serverId)
    {
        if (!_clients.TryRemove(serverId, out var entry))
            return;

        foreach (var toolName in entry.RegisteredToolNames)
        {
            await _toolRegistry.UnregisterToolAsync(toolName).ConfigureAwait(false);
        }

        await entry.Client.DisposeAsync().ConfigureAwait(false);

        LogDisconnectedFromMcpServer(serverId);
    }

    /// <summary>
    /// Returns the status of all connected MCP servers.
    /// </summary>
    public IReadOnlyList<McpServerStatus> GetServerStatuses()
    {
        return _clients.Select(kvp => new McpServerStatus
        {
            ServerId = kvp.Key,
            IsConnected = kvp.Value.Transport.IsConnected,
            Capabilities = kvp.Value.Capabilities
        }).ToList();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var serverId in _clients.Keys.ToList())
        {
            await DisconnectServerAsync(serverId).ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Registered MCP tool '{Name}' from server '{ServerId}'")]
    private partial void LogRegisteredMcpTool(string name, string serverId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Connected to MCP server '{ServerId}' with {ToolCount} tools")]
    private partial void LogConnectedToMcpServer(string serverId, int toolCount);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Disconnected from MCP server '{ServerId}'")]
    private partial void LogDisconnectedFromMcpServer(string serverId);
}
