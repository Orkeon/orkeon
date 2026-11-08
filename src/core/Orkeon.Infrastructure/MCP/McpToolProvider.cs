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
    private readonly Orkeon.Tools.Abstractions.Auth.IOAuthLoginInteraction? _loginInteraction;
    private readonly ConcurrentDictionary<string, McpClientEntry> _clients = new();
    private readonly TimeProvider _time;
    private bool _disposed;

    /// <summary>The method of the notification that tells the client a server's tool list changed.</summary>
    private const string ToolListChangedMethod = "notifications/tools/list_changed";

    /// <summary>The method of the notification that tells the client a subscribed resource changed.</summary>
    private const string ResourceUpdatedMethod = "notifications/resources/updated";

    /// <summary>How long a <c>subscriptions/listen</c> stream must stay open before its end resets the backoff.</summary>
    internal static readonly TimeSpan ListenHealthyAfter = TimeSpan.FromSeconds(5);

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
        private long _resourceUpdates;
        private readonly ConcurrentDictionary<string, Uri> _subscriptions = new();
        private readonly ConcurrentDictionary<string, McpResourceReadResult> _resourceCache = new();
        private readonly ConcurrentDictionary<string, long> _resourceVersions = new();
        private readonly object _cacheLock = new();
        private long _cacheEpoch;
        private int _reopenRequested;

        public McpClient Client { get; } = client;

        public IMcpTransport Transport { get; } = transport;

        public McpServerCapabilities? Capabilities { get; } = capabilities;

        /// <summary>The server's settings; null for a server connected without them.</summary>
        public McpServerConfig? Config { get; init; }

        public bool TrustAnnotations { get; init; }

        /// <summary>The server's authenticator when it can sign a person in, to do so outside the timeouts.</summary>
        public IMcpSignInAuthenticator? SignIn { get; init; }

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

        /// <summary>The <c>subscriptions/listen</c> request in flight: cancelled to open it again with another filter.</summary>
        public CancellationTokenSource? ListenRound { get; set; }

        public Task Listener { get; set; } = Task.CompletedTask;

        /// <summary>The handler of the transport's <c>Resumed</c> event, kept to be detached at disconnection.</summary>
        public Action? ResumedHandler { get; set; }

        public long ResourceUpdates => Interlocked.Read(ref _resourceUpdates);

        public Uri[] SubscribedUris() => [.. _subscriptions.Values];

        public bool IsSubscribed(Uri uri) => _subscriptions.ContainsKey(uri.ToString());

        public void Subscribe(Uri uri) => _subscriptions[uri.ToString()] = uri;

        /// <summary>Drops a subscription and its cached read; false when there was none.</summary>
        public bool Unsubscribe(Uri uri)
        {
            lock (_cacheLock)
            {
                _resourceCache.TryRemove(uri.ToString(), out _);
                _resourceVersions.AddOrUpdate(uri.ToString(), 1, (_, v) => v + 1);
                return _subscriptions.TryRemove(uri.ToString(), out _);
            }
        }

        /// <summary>A subscription changed on a modern server: its notification stream must be opened again with the new filter.</summary>
        public void RequestReopen() => Volatile.Write(ref _reopenRequested, 1);

        public bool TakeReopenRequest() => Interlocked.Exchange(ref _reopenRequested, 0) == 1;

        /// <summary>What a read must still match when it comes back to be cached: no update and no clearing since it began.</summary>
        public (long Epoch, long Version) Stamp(Uri uri) =>
            (Interlocked.Read(ref _cacheEpoch), _resourceVersions.GetValueOrDefault(uri.ToString()));

        /// <summary>Drops every cached read: what the server said while its stream was down or re-opened is lost.</summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                Interlocked.Increment(ref _cacheEpoch);
                _resourceCache.Clear();
            }
        }

        public bool TryGetCached(Uri uri, out McpResourceReadResult? result) => _resourceCache.TryGetValue(uri.ToString(), out result);

        /// <summary>Keeps a read, unless the resource was updated or the cache cleared since <paramref name="stamp"/> was taken.</summary>
        public void Cache(Uri uri, McpResourceReadResult result, (long Epoch, long Version) stamp)
        {
            lock (_cacheLock)
            {
                if (Stamp(uri) == stamp && IsSubscribed(uri))
                    _resourceCache[uri.ToString()] = result;
            }
        }

        /// <summary>A subscribed resource changed: its cached read is stale, and nothing else is touched.</summary>
        public void ResourceUpdated(string uri)
        {
            lock (_cacheLock)
            {
                _resourceVersions.AddOrUpdate(uri, 1, (_, v) => v + 1);
                _resourceCache.TryRemove(uri, out _);
            }

            Interlocked.Increment(ref _resourceUpdates);
        }

        /// <summary>A <c>tools/list_changed</c> was read: a listing is owed, whatever else arrives.</summary>
        public void MarkChanged()
        {
            Interlocked.Increment(ref _notifications);
            MarkDirty();
        }

        /// <summary>A listing is owed, without a notification having been read (a stream that was down).</summary>
        public void MarkDirty()
        {
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
    /// <param name="loginInteraction">
    /// Shows the sign-in code of an <c>oauth2-device</c> server to a person; absent in a host without a terminal,
    /// which then refuses such a server.
    /// </param>
    public McpToolProvider(
        IToolRegistry toolRegistry,
        ILoggerFactory? loggerFactory = null,
        IHttpClientFactory? httpClientFactory = null,
        TimeProvider? timeProvider = null,
        Orkeon.Tools.Abstractions.Auth.IOAuthLoginInteraction? loginInteraction = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _loginInteraction = loginInteraction;
        ArgumentNullException.ThrowIfNull(toolRegistry);
        _toolRegistry = toolRegistry;
        _loggerFactory = loggerFactory;
        _httpClientFactory = httpClientFactory;
        _logger = loggerFactory?.CreateLogger<McpToolProvider>() ?? NullLogger<McpToolProvider>.Instance;
    }

    private static Func<CancellationToken, ValueTask>? BeforeCallOf(IMcpSignInAuthenticator? signIn) =>
        signIn is null ? null : signIn.EnsureSignedInAsync;

    private McpOAuthServices OAuthServices() => new(_httpClientFactory, _loginInteraction, _time);

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
            var authenticator = McpRequestAuthenticators.Create(serverId, config, null, OAuthServices());
            IMcpTransport transport = config.Transport switch
            {
                McpTransportType.Stdio => new StdioMcpTransport(config,
                    _loggerFactory?.CreateLogger<StdioMcpTransport>(), serverId),
                McpTransportType.Sse => new StreamableHttpMcpTransport(config.Url!,
                    _httpClientFactory?.CreateClient(McpServiceExtensions.HttpClientName),
                    _loggerFactory?.CreateLogger<StreamableHttpMcpTransport>(),
                    authenticator, _time),
                _ => throw new InvalidOperationException($"Unknown transport type: {config.Transport}")
            };

            var client = new McpClient(transport, _loggerFactory?.CreateLogger<McpClient>());

            try
            {
                await ConnectAndRegisterAsync(serverId, client, transport, config.TrustAnnotations, config, ct, authenticator as IMcpSignInAuthenticator).ConfigureAwait(false);
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
        (McpRequestAuthenticators.Create(serverId, config, null, OAuthServices()) as IDisposable)?.Dispose();

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
        string serverId, McpClient client, IMcpTransport transport, bool trustAnnotations, McpServerConfig? config, CancellationToken ct,
        IMcpSignInAuthenticator? signIn = null)
    {
        // A person may owe a sign-in (the device flow): it waits for the code's lifetime, not for ConnectTimeout.
        if (signIn is not null)
            await signIn.EnsureSignedInAsync(ct).ConfigureAwait(false);

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
            SignIn = signIn,
        };
        clientEntry.SetUnmatched(unmatched);
        try
        {
            foreach (var toolDef in kept)
            {
                var adapter = new McpToolAdapter(toolDef, client, trustAnnotations, config?.CallTimeout) { BeforeCall = BeforeCallOf(signIn) };
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
        if (transport is StreamableHttpMcpTransport http)
        {
            void OnResumedHandler() => OnResumed(serverId, clientEntry);
            clientEntry.ResumedHandler = OnResumedHandler;
            http.Resumed += OnResumedHandler;
        }

        if (client.Era == McpProtocolEra.Modern)
            clientEntry.Listener = Task.Run(() => ListenLoopAsync(serverId, clientEntry), CancellationToken.None);

        var toolCount = clientEntry.ToolNames().Length;
        LogConnectedToMcpServer(serverId, toolCount);
    }

    /// <summary>Lists the prompts of a connected server.</summary>
    /// <param name="serverId">The server's identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="InvalidOperationException">The server is not connected.</exception>
    public Task<IReadOnlyList<McpPrompt>> ListPromptsAsync(string serverId, CancellationToken ct = default) =>
        ClientOf(serverId).ListPromptsAsync(ct);

    /// <summary>Lists the resources of a connected server, following its pages.</summary>
    /// <param name="serverId">The server's identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="InvalidOperationException">The server is not connected.</exception>
    public Task<IReadOnlyList<McpResource>> ListResourcesAsync(string serverId, CancellationToken ct = default) =>
        ClientOf(serverId).ListResourcesAsync(ct);

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
                else if (notification.Method == ResourceUpdatedMethod && ReadUpdatedUri(notification) is { } uri)
                    entry.ResourceUpdated(uri);
            }
        }
        catch (OperationCanceledException)
        {
            // The server was disconnected.
        }
    }

    /// <summary>
    /// Subscribes to the updates of a resource of a connected server whose capabilities announce
    /// <c>resources.subscribe</c>. From then on its reads (<see cref="ReadResourceAsync"/>) are cached until a
    /// <c>resources/updated</c> notification invalidates them; the notification touches nothing else.
    /// </summary>
    /// <param name="serverId">The server's identifier.</param>
    /// <param name="uri">The resource.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="InvalidOperationException">The server is not connected or does not announce resource subscriptions.</exception>
    public async Task SubscribeResourceAsync(string serverId, Uri uri, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var entry = EntryOf(serverId);
        if (entry.Capabilities?.Resources?.Subscribe != true)
            throw new InvalidOperationException($"MCP server '{serverId}' does not announce resources.subscribe.");

        if (entry.Client.Era == McpProtocolEra.Modern)
        {
            // A modern server takes the subscription in the filter of its notification stream: open it again.
            entry.Subscribe(uri);
            await ReopenListenAsync(entry).ConfigureAwait(false);
            return;
        }

        await entry.Client.SubscribeResourceAsync(uri, ct).ConfigureAwait(false);
        entry.Subscribe(uri);
    }

    /// <summary>
    /// Ends the subscription to a resource and drops its cached read. A legacy server is told
    /// (<c>resources/unsubscribe</c>); a modern one is heard on a stream opened again without the resource.
    /// </summary>
    /// <param name="serverId">The server's identifier.</param>
    /// <param name="uri">The resource.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="InvalidOperationException">The server is not connected.</exception>
    public async Task UnsubscribeResourceAsync(string serverId, Uri uri, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var entry = EntryOf(serverId);
        if (!entry.Unsubscribe(uri))
            return;

        if (entry.Client.Era == McpProtocolEra.Modern)
            await ReopenListenAsync(entry).ConfigureAwait(false);
        else
            await entry.Client.UnsubscribeResourceAsync(uri, ct).ConfigureAwait(false);
    }

    private static async Task ReopenListenAsync(McpClientEntry entry)
    {
        entry.RequestReopen();
        if (entry.ListenRound is { } round)
        {
            try
            {
                await round.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // The stream just ended and is being opened again: it reads the new filter.
            }
        }
    }

    /// <summary>
    /// Reads a resource of a connected server. The read of a resource subscribed to is kept until the server
    /// says it was updated; any other read goes to the server each time, as nothing would invalidate it.
    /// </summary>
    /// <param name="serverId">The server's identifier.</param>
    /// <param name="uri">The resource.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="InvalidOperationException">The server is not connected.</exception>
    public async Task<McpResourceReadResult> ReadResourceAsync(string serverId, Uri uri, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var entry = EntryOf(serverId);
        if (entry.IsSubscribed(uri) && entry.TryGetCached(uri, out var cached) && cached is not null)
            return cached;

        // An update that arrives while the read is in flight makes its value stale: it is not kept.
        var stamp = entry.Stamp(uri);
        var result = await entry.Client.ReadResourceAsync(uri, ct).ConfigureAwait(false);
        entry.Cache(uri, result, stamp);
        return result;
    }

    /// <summary>
    /// The transport may have missed what the server said (its stream was opened again, or its session
    /// initialised again): no cached read can be trusted, and a legacy server forgets its subscriptions with
    /// the session, so they are sent again.
    /// </summary>
    private void OnResumed(string serverId, McpClientEntry entry)
    {
        entry.ClearCache();
        if (entry.Client.Era != McpProtocolEra.Modern && entry.SubscribedUris().Length > 0)
            _ = Task.Run(() => ResubscribeAsync(serverId, entry), CancellationToken.None);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fault barrier of a background task: a subscription that cannot be sent again is logged and the others are tried.")]
    private async Task ResubscribeAsync(string serverId, McpClientEntry entry)
    {
        foreach (var uri in entry.SubscribedUris())
        {
            try
            {
                await entry.Client.SubscribeResourceAsync(uri, entry.Stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogMcpResubscribeFailed(ex, serverId, uri);
            }
        }
    }

    /// <summary>How many <c>resources/updated</c> notifications a server sent.</summary>
    internal long ResourceUpdates(string serverId) =>
        _clients.TryGetValue(serverId, out var entry) ? entry.ResourceUpdates : 0;

    private McpClientEntry EntryOf(string serverId) =>
        _clients.TryGetValue(serverId, out var entry)
            ? entry
            : throw new InvalidOperationException($"MCP server '{serverId}' is not connected.");

    private static string? ReadUpdatedUri(JsonRpcNotification notification) =>
        notification.Params is { ValueKind: System.Text.Json.JsonValueKind.Object } p
        && p.TryGetProperty("uri", out var uri) && uri.ValueKind == System.Text.Json.JsonValueKind.String
            ? uri.GetString()
            : null;

    /// <summary>
    /// Keeps a modern server's <c>subscriptions/listen</c> stream open: when it ends or fails it is opened again
    /// after a bounded backoff, and the tool list is marked stale, since what the server said while the stream
    /// was down is lost. A server that refuses the request is not asked again.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fault barrier of a background stream: any failure re-opens it after the backoff, and is logged.")]
    private async Task ListenLoopAsync(string serverId, McpClientEntry entry)
    {
        var ct = entry.Stop.Token;
        var attempt = 0;
        var first = true;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var round = CancellationTokenSource.CreateLinkedTokenSource(ct);
                entry.ListenRound = round;
                var failed = false;
                try
                {
                    if (!first)
                    {
                        entry.MarkDirty();
                        entry.ClearCache();
                    }

                    first = false;
                    var opened = _time.GetTimestamp();
                    await entry.Client.ListenAsync(entry.SubscribedUris(), round.Token).ConfigureAwait(false);

                    // A stream that ends at once is no healthier than one that fails: the attempts go on counting.
                    failed = _time.GetElapsedTime(opened) < ListenHealthyAfter;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException) when (round.IsCancellationRequested && entry.TakeReopenRequest())
                {
                    // A subscription changed: the stream opens again at once, with the new filter. Any other
                    // cancellation (an HTTP timeout, say) is a failure like the rest and waits out the backoff.
                    continue;
                }
                catch (McpListenRefusedException ex)
                {
                    LogMcpListenRefused(ex, serverId);
                    return;
                }
                catch (Exception ex)
                {
                    failed = true;
                    LogMcpListenFailed(ex, serverId);
                }

                attempt = failed ? attempt + 1 : 0;
                await Task.Delay(StreamableHttpMcpTransport.Backoff(attempt), _time, ct).ConfigureAwait(false);
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
        if (entry.SignIn is { } signIn)
            await signIn.EnsureSignedInAsync(ct).ConfigureAwait(false);

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
                    var adapter = new McpToolAdapter(definition, entry.Client, entry.TrustAnnotations, entry.Config?.CallTimeout) { BeforeCall = BeforeCallOf(entry.SignIn) };
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
                var adapter = new McpToolAdapter(definition, entry.Client, entry.TrustAnnotations, entry.Config?.CallTimeout) { BeforeCall = BeforeCallOf(entry.SignIn) };
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
        if (entry.Transport is StreamableHttpMcpTransport http && entry.ResumedHandler is { } handler)
            http.Resumed -= handler;
        await entry.Stop.CancelAsync().ConfigureAwait(false);
        await entry.Reader.ConfigureAwait(false);
        await entry.Watcher.ConfigureAwait(false);
        await entry.Listener.ConfigureAwait(false);

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

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "MCP server '{ServerId}' offers no subscriptions/listen stream: it is not asked again")]
    private partial void LogMcpListenRefused(Exception ex, string serverId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "The subscriptions/listen stream of MCP server '{ServerId}' failed: it is opened again after a delay")]
    private partial void LogMcpListenFailed(Exception ex, string serverId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "MCP server '{ServerId}' did not take the subscription to '{Uri}' again after its session was resumed")]
    private partial void LogMcpResubscribeFailed(Exception ex, string serverId, Uri uri);

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
