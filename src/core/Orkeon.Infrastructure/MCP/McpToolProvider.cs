using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using Orkeon.Domain.Tools;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// Manages connections to multiple MCP servers and registers their
/// tools in the Orkeon tool registry.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed partial class McpToolProvider : IAsyncDisposable, IDisposable
{
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ConcurrentDictionary<string, McpClientEntry> _clients = new();
    private bool _disposed;

    private sealed record McpClientEntry(
        McpClient Client,
        IMcpTransport Transport,
        List<string> RegisteredToolNames,
        List<string> RejectedToolNames,
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
        if (config.Transport == McpTransportType.Sse && config.Url is null)
            throw new ArgumentException("URL required for SSE transport.", nameof(config));
        if (config.Transport is not (McpTransportType.Stdio or McpTransportType.Sse))
            throw new ArgumentException($"Unknown transport type: {config.Transport}", nameof(config));
        return ConnectServerCoreAsync();

        async Task ConnectServerCoreAsync()
        {
            if (_clients.ContainsKey(serverId))
                throw new InvalidOperationException($"Server '{serverId}' is already connected.");

            // SSE URL presence and transport-type validity are checked eagerly in
            // ConnectServerAsync, before the async state machine starts.
            IMcpTransport transport = config.Transport switch
            {
                McpTransportType.Stdio => new StdioMcpTransport(config,
                    _loggerFactory?.CreateLogger<StdioMcpTransport>()),
                McpTransportType.Sse => new SseMcpTransport(config.Url!,
                    logger: _loggerFactory?.CreateLogger<SseMcpTransport>()),
                _ => throw new InvalidOperationException($"Unknown transport type: {config.Transport}")
            };

            var client = new McpClient(transport, _loggerFactory?.CreateLogger<McpClient>());

            try
            {
                await ConnectAndRegisterAsync(serverId, client, transport, ct).ConfigureAwait(false);
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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the client passes to _clients in ConnectAndRegisterAsync, disposed by DisconnectServerAsync; on failure the catch path here disposes it.")]
    public async Task ConnectServerAsync(
        string serverId, IMcpTransport transport, CancellationToken ct = default)
    {
        if (_clients.ContainsKey(serverId))
            throw new InvalidOperationException($"Server '{serverId}' is already connected.");

        var client = new McpClient(transport, _loggerFactory?.CreateLogger<McpClient>());

        try
        {
            await ConnectAndRegisterAsync(serverId, client, transport, ct).ConfigureAwait(false);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// The one path both <c>ConnectServerAsync</c> overloads share: handshake, discovery, and
    /// registration under each tool's own name. A name already held in the registry — by a
    /// built-in tool, a script tool or another server's tool — is refused by the registry; that
    /// tool is left out, named on the log, and never unregistered at disconnection, so the
    /// tool that held the name keeps it.
    /// </summary>
    private async Task ConnectAndRegisterAsync(
        string serverId, McpClient client, IMcpTransport transport, CancellationToken ct)
    {
        // Dual-era connection: server/discover probe, legacy initialize fallback.
        await client.ConnectAsync(ct).ConfigureAwait(false);
        var tools = await client.ListToolsAsync(ct).ConfigureAwait(false);

        var registeredNames = new List<string>();
        var rejectedNames = new List<string>();
        foreach (var toolDef in tools)
        {
            var adapter = new McpToolAdapter(toolDef, client);
            if (await _toolRegistry.RegisterToolAsync(adapter).ConfigureAwait(false))
            {
                registeredNames.Add(toolDef.Name);
                LogRegisteredMcpTool(toolDef.Name, serverId);
            }
            else
            {
                rejectedNames.Add(toolDef.Name);
                LogMcpToolNameCollision(toolDef.Name, serverId);
            }
        }

        _clients[serverId] = new McpClientEntry(
            client, transport, registeredNames, rejectedNames, client.Capabilities);

        LogConnectedToMcpServer(serverId, registeredNames.Count);
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
            Capabilities = kvp.Value.Capabilities,
            RejectedToolNames = kvp.Value.RejectedToolNames.AsReadOnly()
        }).ToList();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var serverId in _clients.Keys.ToList())
        {
            await DisconnectServerAsync(serverId).ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The synchronous bridge a DI container needs: a singleton that only implements
    /// <see cref="IAsyncDisposable"/> makes <c>ServiceProvider.Dispose()</c> throw, and the
    /// runners dispose their host synchronously (STUDIO-21). Blocking here is the end of a
    /// process: the stdio servers it spawned are killed with it, and there is nothing left to
    /// starve.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Registered MCP tool '{Name}' from server '{ServerId}'")]
    private partial void LogRegisteredMcpTool(string name, string serverId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "The tool '{Name}' of MCP server '{ServerId}' collides with an already-registered tool and was not registered; the registered tool keeps the name")]
    private partial void LogMcpToolNameCollision(string name, string serverId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Connected to MCP server '{ServerId}' with {ToolCount} tools")]
    private partial void LogConnectedToMcpServer(string serverId, int toolCount);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Disconnected from MCP server '{ServerId}'")]
    private partial void LogDisconnectedFromMcpServer(string serverId);
}
