using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Text.Json;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.MCP.Auth;
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
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ConcurrentDictionary<string, McpClientEntry> _clients = new();
    private readonly TimeProvider _time;
    private bool _disposed;

    /// <summary>The method of the notification that tells the client a server's tool list changed.</summary>
    private const string ToolListChangedMethod = "notifications/tools/list_changed";

    /// <summary>
    /// How long the client waits after a <c>tools/list_changed</c> before listing again: the notifications
    /// that arrive in the meantime are answered by that one listing.
    /// </summary>
    internal static readonly TimeSpan ToolListDebounce = TimeSpan.FromMilliseconds(500);

    private sealed record RegisteredTool(IBaseTool Adapter, string Fingerprint);

    /// <summary>One connected server: its client, the tools it registered, and the watch of its notifications.</summary>
    private sealed class McpClientEntry(McpClient client, IMcpTransport transport, McpServerCapabilities? capabilities)
    {
        private readonly object _state = new();
        private readonly Dictionary<string, RegisteredTool> _tools = [];
        private readonly List<string> _rejected = [];
        private List<string> _unmatched = [];
        private long _notifications;
        private long _resyncs;
        private int _dirty;

        public McpClient Client { get; } = client;

        public IMcpTransport Transport { get; } = transport;

        public McpServerCapabilities? Capabilities { get; } = capabilities;

        /// <summary>The server's settings; null for a server connected without them.</summary>
        public McpServerConfig? Config { get; init; }

        public bool TrustAnnotations { get; init; }

        /// <summary>One re-synchronisation at a time per server.</summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);

        /// <summary>Cancelled when the server is disconnected: ends the watch of its notifications.</summary>
        public CancellationTokenSource Stop { get; } = new();

        /// <summary>Woken by the reader each time the server says its tool list changed.</summary>
        public SemaphoreSlim Wake { get; } = new(0, 1);

        public Task Reader { get; set; } = Task.CompletedTask;

        public Task Watcher { get; set; } = Task.CompletedTask;

        public long NotificationsSeen => Interlocked.Read(ref _notifications);

        public long Resyncs => Interlocked.Read(ref _resyncs);

        public bool IsDirty => Volatile.Read(ref _dirty) == 1;

        /// <summary>A <c>tools/list_changed</c> was read: a listing is owed, whatever else arrives.</summary>
        public void MarkChanged()
        {
            Interlocked.Increment(ref _notifications);
            Volatile.Write(ref _dirty, 1);
            try
            {
                Wake.Release();
            }
            catch (SemaphoreFullException)
            {
                // A wake-up is already pending: the watcher will see the flag.
            }
        }

        /// <summary>The listing about to be made answers every notification read so far.</summary>
        public void ClearDirty() => Volatile.Write(ref _dirty, 0);

        public void CountResync() => Interlocked.Increment(ref _resyncs);

        public List<KeyValuePair<string, RegisteredTool>> SnapshotTools()
        {
            lock (_state)
                return [.. _tools];
        }

        public bool Holds(string name)
        {
            lock (_state)
                return _tools.ContainsKey(name);
        }

        public void Remember(string name, IBaseTool adapter, string fingerprint)
        {
            lock (_state)
            {
                _tools[name] = new RegisteredTool(adapter, fingerprint);
                _rejected.Remove(name);
            }
        }

        public void Forget(string name)
        {
            lock (_state)
                _tools.Remove(name);
        }

        /// <summary>Notes a name another tool holds; true when it was not noted yet.</summary>
        public bool Reject(string name)
        {
            lock (_state)
            {
                if (_rejected.Contains(name))
                    return false;
                _rejected.Add(name);
                return true;
            }
        }

        /// <summary>Forgets the rejected names the server no longer lists.</summary>
        public void KeepRejected(IReadOnlyCollection<string> listed)
        {
            lock (_state)
                _rejected.RemoveAll(name => !listed.Contains(name));
        }

        public void SetUnmatched(IEnumerable<string> unmatched)
        {
            lock (_state)
                _unmatched = [.. unmatched];
        }

        public string[] ToolNames()
        {
            lock (_state)
                return [.. _tools.Keys];
        }

        public (string[] Rejected, string[] Unmatched) Snapshot()
        {
            lock (_state)
                return ([.. _rejected], [.. _unmatched]);
        }
    }

    /// <summary>Initializes a new instance of <see cref="McpToolProvider"/>.</summary>
    /// <param name="toolRegistry">The tool registry to register MCP tools into.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="httpClientFactory">
    /// Optional factory of the shared <see cref="McpServiceExtensions.HttpClientName"/> client the SSE
    /// transports use; without it each transport owns a client.
    /// </param>
    /// <param name="timeProvider">The clock of the <c>tools/list_changed</c> debounce; the system clock by default.</param>
    public McpToolProvider(
        IToolRegistry toolRegistry,
        ILoggerFactory? loggerFactory = null,
        IHttpClientFactory? httpClientFactory = null,
        TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        ArgumentNullException.ThrowIfNull(toolRegistry);
        _toolRegistry = toolRegistry;
        _loggerFactory = loggerFactory;
        _httpClientFactory = httpClientFactory;
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
            // Validated before anything starts: a refused configuration names its key and spawns nothing.
            var authenticator = McpRequestAuthenticators.Create(serverId, config);
            IMcpTransport transport = config.Transport switch
            {
                McpTransportType.Stdio => new StdioMcpTransport(config,
                    _loggerFactory?.CreateLogger<StdioMcpTransport>(), serverId),
                McpTransportType.Sse => new SseMcpTransport(config.Url!,
                    _httpClientFactory?.CreateClient(McpServiceExtensions.HttpClientName),
                    _loggerFactory?.CreateLogger<SseMcpTransport>(),
                    authenticator),
                _ => throw new InvalidOperationException($"Unknown transport type: {config.Transport}")
            };

            var client = new McpClient(transport, _loggerFactory?.CreateLogger<McpClient>());

            try
            {
                await ConnectAndRegisterAsync(serverId, client, transport, config.TrustAnnotations, config, ct).ConfigureAwait(false);
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    /// <summary>
    /// Connects to an MCP server over an externally-provided transport, applying the rest of
    /// <paramref name="config"/>: <c>Tools</c> filter, <c>CallTimeout</c> and <c>TrustAnnotations</c>.
    /// </summary>
    /// <param name="serverId">The server's identifier.</param>
    /// <param name="transport">The transport to the server.</param>
    /// <param name="config">The server's settings; its transport fields are not used.</param>
    /// <param name="ct">The cancellation token.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the client passes to _clients in ConnectAndRegisterAsync, disposed by DisconnectServerAsync; on failure the catch path here disposes it.")]
    public async Task ConnectServerAsync(
        string serverId, IMcpTransport transport, McpServerConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(config);
        if (_clients.ContainsKey(serverId))
            throw new InvalidOperationException($"Server '{serverId}' is already connected.");
        _ = McpRequestAuthenticators.Create(serverId, config);

        var client = new McpClient(transport, _loggerFactory?.CreateLogger<McpClient>());
        try
        {
            await ConnectAndRegisterAsync(serverId, client, transport, config.TrustAnnotations, config, ct).ConfigureAwait(false);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Connects to an MCP server using an externally-provided transport
    /// (useful for testing or custom transports).
    /// </summary>
    /// <param name="serverId">The server's identifier.</param>
    /// <param name="transport">The transport to the server.</param>
    /// <param name="trustAnnotations">
    /// Whether the server's tool annotations decide each tool's access class
    /// (<see cref="McpServerConfig.TrustAnnotations"/>); <see langword="false"/> by default.
    /// </param>
    /// <param name="ct">The cancellation token.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the client passes to _clients in ConnectAndRegisterAsync, disposed by DisconnectServerAsync; on failure the catch path here disposes it.")]
    public async Task ConnectServerAsync(
        string serverId, IMcpTransport transport, bool trustAnnotations = false, CancellationToken ct = default)
    {
        if (_clients.ContainsKey(serverId))
            throw new InvalidOperationException($"Server '{serverId}' is already connected.");

        var client = new McpClient(transport, _loggerFactory?.CreateLogger<McpClient>());

        try
        {
            await ConnectAndRegisterAsync(serverId, client, transport, trustAnnotations, null, ct).ConfigureAwait(false);
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
        string serverId, McpClient client, IMcpTransport transport, bool trustAnnotations, McpServerConfig? config, CancellationToken ct)
    {
        // Dual-era connection: server/discover probe, legacy initialize fallback. A configured server
        // answers within its ConnectTimeout, whoever connects it: the shared HTTP client has no timeout.
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (config is not null)
            guard.CancelAfter(config.ConnectTimeout);

        IReadOnlyList<McpToolDefinition> tools;
        try
        {
            await client.ConnectAsync(guard.Token).ConfigureAwait(false);
            tools = await client.ListToolsAsync(guard.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && config is not null)
        {
            throw new TimeoutException(
                $"MCP server '{serverId}' gave no answer within {config.ConnectTimeout.TotalSeconds:0.###} s (MCP:Servers:{serverId}:ConnectTimeout).");
        }

        // The filter runs before registration: a tool it removes never reaches the registry.
        var (kept, unmatched) = McpToolFilter.Apply(tools, config?.Tools);
        foreach (var entry in unmatched)
            LogMcpToolFilterUnmatched(entry, serverId);

        var clientEntry = new McpClientEntry(client, transport, client.Capabilities)
        {
            Config = config,
            TrustAnnotations = trustAnnotations,
        };
        clientEntry.SetUnmatched(unmatched);
        try
        {
            foreach (var toolDef in kept)
            {
                var adapter = new McpToolAdapter(toolDef, client, trustAnnotations, config?.CallTimeout);
                if (await _toolRegistry.RegisterToolAsync(adapter).ConfigureAwait(false))
                {
                    clientEntry.Remember(toolDef.Name, adapter, Fingerprint(toolDef));
                    LogRegisteredMcpTool(toolDef.Name, serverId);
                }
                else
                {
                    clientEntry.Reject(toolDef.Name);
                    LogMcpToolNameCollision(toolDef.Name, serverId);
                }
            }
        }
        catch
        {
            // The connection fails: the tools it registered so far leave with it.
            foreach (var name in clientEntry.ToolNames())
                await _toolRegistry.UnregisterToolAsync(name).ConfigureAwait(false);
            throw;
        }

        _clients[serverId] = clientEntry;
        clientEntry.Reader = Task.Run(() => ReadNotificationsAsync(clientEntry), CancellationToken.None);
        clientEntry.Watcher = Task.Run(() => WatchToolListAsync(serverId, clientEntry), CancellationToken.None);

        var toolCount = clientEntry.ToolNames().Length;
        LogConnectedToMcpServer(serverId, toolCount);
    }

    /// <summary>Lists the prompts of a connected server.</summary>
    /// <param name="serverId">The server's identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="InvalidOperationException">The server is not connected.</exception>
    public Task<IReadOnlyList<McpPrompt>> ListPromptsAsync(string serverId, CancellationToken ct = default) =>
        ClientOf(serverId).ListPromptsAsync(ct);

    /// <summary>Gets a prompt of a connected server, its arguments filled in.</summary>
    /// <param name="serverId">The server's identifier.</param>
    /// <param name="name">The prompt's name.</param>
    /// <param name="arguments">The prompt's arguments.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="InvalidOperationException">The server is not connected.</exception>
    public Task<McpPromptGetResult> GetPromptAsync(
        string serverId, string name, IReadOnlyDictionary<string, string>? arguments = null, CancellationToken ct = default) =>
        ClientOf(serverId).GetPromptAsync(name, arguments, ct);

    /// <summary>
    /// What the watch of a server's tool list has done: the <c>tools/list_changed</c> notifications read,
    /// and the re-listings finished (failed ones included). For the tests, which wait on it instead of a clock.
    /// </summary>
    internal (long Notifications, long Resyncs) ToolListActivity(string serverId) =>
        _clients.TryGetValue(serverId, out var entry) ? (entry.NotificationsSeen, entry.Resyncs) : (0, 0);

    private McpClient ClientOf(string serverId) =>
        _clients.TryGetValue(serverId, out var entry)
            ? entry.Client
            : throw new InvalidOperationException($"MCP server '{serverId}' is not connected.");

    private static string Fingerprint(McpToolDefinition definition) => JsonSerializer.Serialize(definition);

    /// <summary>
    /// Reads the notifications of one server as they arrive, and nothing else: a <c>tools/list_changed</c>
    /// marks the server dirty. The channel they come through is bounded and drops its oldest entry, so a
    /// reader that waited out a debounce or a listing could find the one that mattered gone; this one
    /// never waits.
    /// </summary>
    private static async Task ReadNotificationsAsync(McpClientEntry entry)
    {
        try
        {
            await foreach (var notification in entry.Transport.Notifications.ReadAllAsync(entry.Stop.Token).ConfigureAwait(false))
            {
                if (notification.Method == ToolListChangedMethod)
                    entry.MarkChanged();
            }
        }
        catch (OperationCanceledException)
        {
            // The server was disconnected.
        }
    }

    /// <summary>
    /// Lists a server's tools again whenever it is dirty: after <see cref="ToolListDebounce"/> — the
    /// notifications that arrive during the wait are answered by the one listing — and again if the server
    /// said it changed while the listing was running, until no notification is left unanswered.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fault barrier of a background watch: a failed re-synchronisation is logged and the server keeps the tools it had.")]
    private async Task WatchToolListAsync(string serverId, McpClientEntry entry)
    {
        var ct = entry.Stop.Token;
        try
        {
            while (true)
            {
                await entry.Wake.WaitAsync(ct).ConfigureAwait(false);
                while (entry.IsDirty)
                {
                    await Task.Delay(ToolListDebounce, _time, ct).ConfigureAwait(false);
                    entry.ClearDirty();
                    try
                    {
                        await ResyncToolsAsync(serverId, entry, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogMcpToolResyncFailed(ex, serverId);
                    }
                    finally
                    {
                        entry.CountResync();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The server was disconnected.
        }
    }

    /// <summary>
    /// Lists the server's tools again and brings the registry to what it lists, one tool at a time, the
    /// server's entry following each registry step: a tool it dropped is unregistered; a tool whose
    /// definition changed is swapped for a new adapter in one registry step, so its name never stops
    /// resolving; a new tool is registered; a name another tool holds is left to that tool and warned
    /// about as <c>mcp.tool.rejected</c>. The tool instances a task in progress already holds are not
    /// touched: only the next task to resolve its tools sees the change.
    /// </summary>
    private async Task ResyncToolsAsync(string serverId, McpClientEntry entry, CancellationToken ct)
    {
        await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (entry.Config is not null)
                guard.CancelAfter(entry.Config.ConnectTimeout);
            var listed = await entry.Client.ListToolsAsync(guard.Token).ConfigureAwait(false);
            var (kept, unmatched) = McpToolFilter.Apply(listed, entry.Config?.Tools);

            var current = new Dictionary<string, McpToolDefinition>();
            foreach (var definition in kept)
                current.TryAdd(definition.Name, definition);

            foreach (var (name, held) in entry.SnapshotTools())
            {
                if (!current.TryGetValue(name, out var definition))
                {
                    await _toolRegistry.UnregisterToolAsync(name).ConfigureAwait(false);
                    entry.Forget(name);
                }
                else if (held.Fingerprint != Fingerprint(definition))
                {
                    var adapter = new McpToolAdapter(definition, entry.Client, entry.TrustAnnotations, entry.Config?.CallTimeout);
                    if (await _toolRegistry.ReplaceToolAsync(held.Adapter, adapter).ConfigureAwait(false))
                        entry.Remember(name, adapter, Fingerprint(definition));
                    else
                        entry.Forget(name); // the registry no longer holds our instance: the name is registered afresh below
                }
            }

            entry.KeepRejected(current.Keys);
            foreach (var (name, definition) in current)
            {
                if (entry.Holds(name))
                    continue;
                var adapter = new McpToolAdapter(definition, entry.Client, entry.TrustAnnotations, entry.Config?.CallTimeout);
                if (await _toolRegistry.RegisterToolAsync(adapter).ConfigureAwait(false))
                {
                    entry.Remember(name, adapter, Fingerprint(definition));
                    LogRegisteredMcpTool(name, serverId);
                }
                else if (entry.Reject(name))
                {
                    LogMcpToolRejected(name, serverId);
                }
            }

            entry.SetUnmatched(unmatched);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    /// <summary>
    /// Disconnects from an MCP server and unregisters its tools.
    /// </summary>
    public async Task DisconnectServerAsync(string serverId)
    {
        if (!_clients.TryRemove(serverId, out var entry))
            return;

        // The watch ends first: a re-synchronisation must not register tools of a server being left.
        await entry.Stop.CancelAsync().ConfigureAwait(false);
        await entry.Reader.ConfigureAwait(false);
        await entry.Watcher.ConfigureAwait(false);

        foreach (var toolName in entry.ToolNames())
        {
            await _toolRegistry.UnregisterToolAsync(toolName).ConfigureAwait(false);
        }

        await entry.Client.DisposeAsync().ConfigureAwait(false);
        entry.Stop.Dispose();
        entry.Gate.Dispose();
        entry.Wake.Dispose();

        LogDisconnectedFromMcpServer(serverId);
    }

    /// <summary>
    /// Returns the status of all connected MCP servers.
    /// </summary>
    public IReadOnlyList<McpServerStatus> GetServerStatuses()
    {
        return _clients.Select(kvp =>
        {
            var (rejected, unmatched) = kvp.Value.Snapshot();
            return new McpServerStatus
            {
                ServerId = kvp.Key,
                IsConnected = kvp.Value.Transport.IsConnected,
                Capabilities = kvp.Value.Capabilities,
                RejectedToolNames = rejected,
                UnmatchedToolFilters = unmatched,
            };
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

    [LoggerMessage(EventName = "mcp.tool.rejected", Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "The tool '{Name}' of MCP server '{ServerId}' now collides with an already-registered tool and is ignored; the registered tool keeps the name")]
    private partial void LogMcpToolRejected(string name, string serverId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "MCP server '{ServerId}' changed its tool list and the client could not list it again; the tools registered so far stay")]
    private partial void LogMcpToolResyncFailed(Exception exception, string serverId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "The Tools filter entry '{Entry}' of MCP server '{ServerId}' matches no tool the server lists")]
    private partial void LogMcpToolFilterUnmatched(string entry, string serverId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Connected to MCP server '{ServerId}' with {ToolCount} tools")]
    private partial void LogConnectedToMcpServer(string serverId, int toolCount);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Disconnected from MCP server '{ServerId}'")]
    private partial void LogDisconnectedFromMcpServer(string serverId);
}
