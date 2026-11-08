using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// Dual-era MCP protocol client. Detects whether the server speaks the modern,
/// stateless lineage (2026-07-28 — per-request `_meta`, `server/discover`) or a
/// legacy initialize-handshake revision (2025-11-25 and earlier), then exposes
/// tools/list, tools/call, resources/list, and resources/read RPCs on either.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class McpClient : IAsyncDisposable
{
    private const string ClientName = "Orkeon";
    private const string ClientVersion = "1.0.0";

    /// <summary>The most pages one listing follows: a server whose cursor never ends fails the listing.</summary>
    internal const int MaxPages = 100;

    /// <summary>The most items one listing gathers over its pages: past it the server is flooding the client, and the listing fails.</summary>
    internal const int MaxItems = 10_000;

    private readonly IMcpTransport _transport;
    private readonly ILogger _logger;
    private int _nextId;
    private McpServerCapabilities? _capabilities;
    private McpServerInfo? _serverInfo;
    private McpProtocolEra _era = McpProtocolEra.Unknown;
    private string _negotiatedVersion = McpProtocol.ModernVersion;

    /// <summary>
    /// Capabilities advertised by the server (from `server/discover` on modern
    /// servers, from the initialize handshake on legacy ones).
    /// </summary>
    public McpServerCapabilities? Capabilities => _capabilities;

    /// <summary>
    /// Server identity, when the server reported one.
    /// </summary>
    public McpServerInfo? ServerInfo => _serverInfo;

    /// <summary>
    /// The protocol era the connected server speaks (Unknown before <see cref="ConnectAsync"/>).
    /// </summary>
    public McpProtocolEra Era => _era;

    /// <summary>
    /// The protocol revision in force for this connection.
    /// </summary>
    public string NegotiatedVersion => _negotiatedVersion;

    /// <summary>
    /// Whether the client is ready to issue requests (era detected, and for
    /// legacy servers the initialize handshake completed).
    /// </summary>
    public bool IsInitialized => _era != McpProtocolEra.Unknown;

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
    /// Connects to the server and detects its protocol era: probes with
    /// `server/discover` (modern), and falls back to the legacy `initialize`
    /// handshake when the probe is answered with a non-modern error, as
    /// specified by the 2026-07-28 backward-compatibility rules.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_era != McpProtocolEra.Unknown) return;

        if (!_transport.IsConnected)
        {
            await _transport.ConnectAsync(ct).ConfigureAwait(false);
        }

        var request = CreateModernRequest("server/discover", null, McpProtocol.ModernVersion);
        var response = await _transport.SendRequestAsync(request, ct).ConfigureAwait(false);

        if (response.Error == null)
        {
            var result = DeserializeResult<McpDiscoverResult>(response);

            // A modern server MUST list its supported versions. A success response
            // without them is a legacy server answering an unknown method leniently:
            // fall back to the initialize handshake instead of failing.
            if (result.SupportedVersions.Count == 0)
            {
                LogModernProbeFellBack(0, "discover result carried no supportedVersions");
                await InitializeAsync(ct).ConfigureAwait(false);
                return;
            }

            AdoptModern(PickCommonVersion(result.SupportedVersions)
                ?? throw NoCommonVersion(result.SupportedVersions));
            _capabilities = result.Capabilities;
            _serverInfo = ReadServerInfoMeta(result.Meta);
            LogEraDetected("modern", _negotiatedVersion, _serverInfo?.Name);
            return;
        }

        if (response.Error.Code == McpErrorCodes.UnsupportedProtocolVersion)
        {
            // A modern server that rejects our preferred version still identifies
            // itself as modern; retry with a mutually supported revision.
            var supported = McpProtocol.ReadSupportedVersionsFromError(response.Error);
            var chosen = PickCommonVersion(supported) ?? throw NoCommonVersion(supported);
            AdoptModern(chosen);
            var retry = CreateModernRequest("server/discover", null, chosen);
            var retryResponse = await _transport.SendRequestAsync(retry, ct).ConfigureAwait(false);
            if (retryResponse.Error != null)
            {
                throw new InvalidOperationException(
                    $"server/discover failed after version renegotiation: [{retryResponse.Error.Code}] {retryResponse.Error.Message}");
            }
            var result = DeserializeResult<McpDiscoverResult>(retryResponse);
            _capabilities = result.Capabilities;
            _serverInfo = ReadServerInfoMeta(result.Meta);
            LogEraDetected("modern", _negotiatedVersion, _serverInfo?.Name);
            return;
        }

        // Any other error identifies a legacy server: fall back to initialize.
        LogModernProbeFellBack(response.Error.Code, response.Error.Message);
        await InitializeAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the legacy MCP initialize handshake (2025-11-25 and earlier),
    /// negotiating the newest legacy revision both sides support and sending
    /// the required `notifications/initialized`.
    /// </summary>
    public async Task<McpInitializeResult> InitializeAsync(CancellationToken ct = default)
    {
        if (!_transport.IsConnected)
        {
            await _transport.ConnectAsync(ct).ConfigureAwait(false);
        }

        var requestedVersion = McpProtocol.SupportedLegacyVersions[0];
        var initParams = new
        {
            protocolVersion = requestedVersion,
            capabilities = new { },
            clientInfo = new { name = ClientName, version = ClientVersion }
        };

        var request = CreateRequest("initialize", initParams);
        var response = await _transport.SendRequestAsync(request, ct).ConfigureAwait(false);

        if (response.Error != null)
        {
            throw new InvalidOperationException(
                $"MCP initialization failed: [{response.Error.Code}] {response.Error.Message}");
        }

        var result = DeserializeResult<McpInitializeResult>(response);

        // Legacy negotiation: the server answers with the revision it selects.
        // Accept it when we support it; otherwise the connection is unusable.
        if (!string.IsNullOrEmpty(result.ProtocolVersion) &&
            !McpProtocol.SupportedLegacyVersions.Contains(result.ProtocolVersion))
        {
            throw new InvalidOperationException(
                $"MCP server selected protocol version '{result.ProtocolVersion}', which this client does not support " +
                $"(supported legacy versions: {string.Join(", ", McpProtocol.SupportedLegacyVersions)}).");
        }

        _era = McpProtocolEra.Legacy;
        _negotiatedVersion = string.IsNullOrEmpty(result.ProtocolVersion)
            ? requestedVersion
            : result.ProtocolVersion;
        _capabilities = result.Capabilities;
        _serverInfo = result.ServerInfo;

        // The legacy lineage requires the initialized notification before normal traffic.
        await _transport.SendNotificationAsync(
            new JsonRpcNotification { Method = "notifications/initialized" }, ct).ConfigureAwait(false);

        LogMcpClientInitialized(_serverInfo?.Name, _serverInfo?.Version);

        return result;
    }

    /// <summary>
    /// Lists tools available on the MCP server.
    /// </summary>
    public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken ct = default)
    {
        EnsureReady();

        return await PageAllAsync("tools/list", (McpToolListResult r) => (r.Tools, r.NextCursor), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the prompts of the MCP server, following the cursor of every page.
    /// </summary>
    public async Task<IReadOnlyList<McpPrompt>> ListPromptsAsync(CancellationToken ct = default)
    {
        EnsureReady();
        return await PageAllAsync("prompts/list", (McpPromptListResult r) => (r.Prompts, r.NextCursor), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a prompt of the server, its <paramref name="arguments"/> filled in.
    /// </summary>
    public async Task<McpPromptGetResult> GetPromptAsync(
        string name, IReadOnlyDictionary<string, string>? arguments = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureReady();

        var response = await SendAsync("prompts/get", new { name, arguments = arguments ?? new Dictionary<string, string>() }, ct)
            .ConfigureAwait(false);

        if (response.Error != null)
        {
            throw new InvalidOperationException(
                $"prompts/get failed: [{response.Error.Code}] {response.Error.Message}");
        }

        return DeserializeResult<McpPromptGetResult>(response);
    }

    /// <summary>
    /// Calls a tool on the MCP server.
    /// </summary>
    public async Task<McpToolCallResult> CallToolAsync(
        string name, JsonElement? arguments = null, CancellationToken ct = default)
    {
        EnsureReady();

        var callParams = new McpToolCallParams { Name = name, Arguments = arguments };
        var response = await SendAsync("tools/call", callParams, ct).ConfigureAwait(false);

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

        var result = DeserializeResult<McpToolCallResult>(response);

        // A missing resultType means "complete" (pre-2026 server). Multi-round-trip
        // interim results are not supported by this client: surface them as errors
        // instead of silently returning partial data.
        if (result.ResultType == "input_required")
        {
            return new McpToolCallResult
            {
                IsError = true,
                Content =
                [
                    new()
                    {
                        Type = "text",
                        Text = "MCP server requested additional input (multi-round-trip request); this client does not support MRTR."
                    }
                ]
            };
        }

        return result;
    }

    /// <summary>
    /// Lists resources available on the MCP server.
    /// </summary>
    public async Task<IReadOnlyList<McpResource>> ListResourcesAsync(CancellationToken ct = default)
    {
        EnsureReady();

        return await PageAllAsync("resources/list", (McpResourceListResult r) => (r.Resources, r.NextCursor), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a resource by URI.
    /// </summary>
    public Task<McpResourceReadResult> ReadResourceAsync(Uri uri, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return ReadResourceCoreAsync();

        async Task<McpResourceReadResult> ReadResourceCoreAsync()
        {
            EnsureReady();

            var readParams = new { uri = uri.ToString() };
            var response = await SendAsync("resources/read", readParams, ct).ConfigureAwait(false);

            if (response.Error != null)
            {
                throw new InvalidOperationException(
                    $"resources/read failed: [{response.Error.Code}] {response.Error.Message}");
            }

            return DeserializeResult<McpResourceReadResult>(response);
        }
    }

    /// <summary>
    /// Opens the 2026-07-28 notification stream (<c>subscriptions/listen</c>) and returns when the server
    /// ends it. The notifications it carries reach the transport's <see cref="IMcpTransport.Notifications"/>
    /// channel as they arrive; the call itself lasts as long as the stream, and is not bounded by a call timeout.
    /// </summary>
    /// <param name="resourceSubscriptions">The resources whose updates the stream carries.</param>
    /// <param name="ct">Cancels the stream.</param>
    /// <exception cref="InvalidOperationException">The server is not a modern one, or refused the subscription.</exception>
    public async Task ListenAsync(IReadOnlyCollection<Uri> resourceSubscriptions, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resourceSubscriptions);
        EnsureReady();
        if (_era != McpProtocolEra.Modern)
        {
            throw new InvalidOperationException(
                "subscriptions/listen belongs to the 2026-07-28 revision: a legacy server is heard through the transport's own stream and resources/subscribe.");
        }

        var listenParams = new
        {
            notifications = new
            {
                toolsListChanged = true,
                promptsListChanged = true,
                resourcesListChanged = true,
                resourceSubscriptions = resourceSubscriptions.Select(u => u.ToString()).ToArray(),
            },
        };

        JsonRpcResponse response;
        try
        {
            response = await SendAsync("subscriptions/listen", listenParams, ct).ConfigureAwait(false);
        }
        catch (McpStreamEndedException)
        {
            // The server closed the stream: the subscription is over, the caller opens it again.
            return;
        }

        if (response.Error != null)
        {
            throw new McpListenRefusedException(
                $"subscriptions/listen failed: [{response.Error.Code}] {response.Error.Message}");
        }
    }

    /// <summary>Subscribes to the updates of a resource (legacy <c>resources/subscribe</c>); they arrive as <c>notifications/resources/updated</c>.</summary>
    /// <param name="uri">The resource.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task SubscribeResourceAsync(Uri uri, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        EnsureReady();
        var response = await SendAsync("resources/subscribe", new { uri = uri.ToString() }, ct).ConfigureAwait(false);
        if (response.Error != null)
        {
            throw new InvalidOperationException(
                $"resources/subscribe failed: [{response.Error.Code}] {response.Error.Message}");
        }
    }

    /// <summary>Ends the subscription to a resource (legacy <c>resources/unsubscribe</c>).</summary>
    /// <param name="uri">The resource.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task UnsubscribeResourceAsync(Uri uri, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        EnsureReady();
        var response = await SendAsync("resources/unsubscribe", new { uri = uri.ToString() }, ct).ConfigureAwait(false);
        if (response.Error != null)
        {
            throw new InvalidOperationException(
                $"resources/unsubscribe failed: [{response.Error.Code}] {response.Error.Message}");
        }
    }

    /// <summary>
    /// Sends a request in the era in force. On a modern connection the request
    /// carries per-request `_meta`; an <c>UnsupportedProtocolVersionError</c>
    /// triggers one renegotiation + retry with a mutually supported revision.
    /// </summary>
    private async Task<JsonRpcResponse> SendAsync(string method, object? parameters, CancellationToken ct)
    {
        var request = _era == McpProtocolEra.Modern
            ? CreateModernRequest(method, parameters, _negotiatedVersion)
            : CreateRequest(method, parameters);

        JsonRpcResponse response;
        try
        {
            response = await _transport.SendRequestAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller gave up or a timeout fired: the server is told, so it stops working for nobody.
            await NotifyCancelledAsync(request).ConfigureAwait(false);
            throw;
        }

        if (_era == McpProtocolEra.Modern &&
            response.Error is { Code: McpErrorCodes.UnsupportedProtocolVersion } error)
        {
            var supported = McpProtocol.ReadSupportedVersionsFromError(error);
            var chosen = PickCommonVersion(supported) ?? throw NoCommonVersion(supported);
            AdoptModern(chosen);
            var retry = CreateModernRequest(method, parameters, chosen);
            response = await _transport.SendRequestAsync(retry, ct).ConfigureAwait(false);
        }

        return response;
    }

    /// <summary>
    /// Best effort <c>notifications/cancelled</c> for a request that was abandoned: bounded, and a failure
    /// to send it is not worth reporting over the cancellation itself.
    /// </summary>
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort notification on the way out of a cancellation: nothing it throws may replace the cancellation.")]
    private async Task NotifyCancelledAsync(JsonRpcRequest request)
    {
        if (request.Id is null || request.Method is "initialize" or "server/discover")
            return;

        try
        {
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _transport.SendNotificationAsync(new JsonRpcNotification
            {
                Method = "notifications/cancelled",
                Params = JsonSerializer.SerializeToElement(new { requestId = request.Id.Value, reason = "The client stopped waiting for the answer." }),
            }, guard.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The connection is likely the very reason of the cancellation.
        }
    }

    /// <summary>
    /// Reads every page of a listing: sends <paramref name="method"/> with the cursor of the page before
    /// it, until a page names no next cursor. At most <see cref="MaxPages"/> pages — past it the server
    /// is not paging, it is looping, and the listing fails rather than grow without end.
    /// </summary>
    private async Task<IReadOnlyList<TItem>> PageAllAsync<TPage, TItem>(
        string method, Func<TPage, (IReadOnlyList<TItem> Items, string? NextCursor)> read, CancellationToken ct)
        where TPage : new()
    {
        var all = new List<TItem>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var response = await SendAsync(method, cursor is null ? null : new { cursor }, ct).ConfigureAwait(false);
            if (response.Error != null)
            {
                throw new InvalidOperationException(
                    $"{method} failed: [{response.Error.Code}] {response.Error.Message}");
            }

            var (items, next) = read(DeserializeResult<TPage>(response));
            all.AddRange(items);
            if (all.Count > MaxItems)
            {
                throw new InvalidOperationException(
                    $"{method} returned more than {MaxItems} items over {page + 1} pages: the listing is cut off rather than grow without end.");
            }

            if (string.IsNullOrEmpty(next))
                return all;
            cursor = next;
        }

        throw new InvalidOperationException(
            $"{method} still had a next page after {MaxPages} pages: the server's cursor never ends.");
    }

    private JsonRpcRequest CreateRequest(string method, object? parameters = null)
    {
        var request = new JsonRpcRequest
        {
            Method = method,
            Id = JsonSerializer.SerializeToElement(Interlocked.Increment(ref _nextId))
        };

        if (parameters != null)
        {
            var json = JsonSerializer.Serialize(parameters);
            request.Params = JsonElement.Parse(json);
        }

        return request;
    }

    private JsonRpcRequest CreateModernRequest(string method, object? parameters, string protocolVersion)
    {
        return new JsonRpcRequest
        {
            Method = method,
            Id = JsonSerializer.SerializeToElement(Interlocked.Increment(ref _nextId)),
            Params = McpProtocol.BuildParamsWithMeta(parameters, protocolVersion, ClientName, ClientVersion)
        };
    }

    private void AdoptModern(string version)
    {
        _era = McpProtocolEra.Modern;
        _negotiatedVersion = version;
    }

    private static string? PickCommonVersion(IReadOnlyList<string> serverVersions)
    {
        // Our SupportedVersions list is newest-first; take the newest both sides speak.
        return McpProtocol.SupportedVersions
            .FirstOrDefault(version => serverVersions.Contains(version));
    }

    private static InvalidOperationException NoCommonVersion(IReadOnlyList<string> serverVersions)
    {
        return new InvalidOperationException(
            $"No mutually supported MCP protocol version: server supports [{string.Join(", ", serverVersions)}], " +
            $"client supports [{string.Join(", ", McpProtocol.SupportedVersions)}].");
    }

    private static McpServerInfo? ReadServerInfoMeta(JsonElement? meta)
    {
        if (meta is not { ValueKind: JsonValueKind.Object } m)
            return null;
        if (!m.TryGetProperty(McpProtocol.MetaServerInfo, out var info) ||
            info.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return JsonSerializer.Deserialize<McpServerInfo>(info.GetRawText());
    }

    private static T DeserializeResult<T>(JsonRpcResponse response) where T : new()
    {
        if (response.Result == null)
            return new T();

        return JsonSerializer.Deserialize<T>(response.Result.Value.GetRawText()) ?? new T();
    }

    private void EnsureReady()
    {
        if (_era == McpProtocolEra.Unknown)
            throw new InvalidOperationException("MCP client is not connected. Call ConnectAsync (or InitializeAsync) first.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "MCP client initialized with server {Name} v{Version}")]
    private partial void LogMcpClientInitialized(string? name, string? version);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "MCP era detected: {Era}, protocol {Version}, server {Name}")]
    private partial void LogEraDetected(string era, string version, string? name);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "server/discover probe answered with a non-modern error [{Code}] {Message}; falling back to legacy initialize")]
    private partial void LogModernProbeFellBack(int code, string message);
}

/// <summary>The server refused <c>subscriptions/listen</c>: it does not offer the stream, and asking again would not change that.</summary>
internal sealed class McpListenRefusedException : InvalidOperationException
{
    public McpListenRefusedException() { }

    public McpListenRefusedException(string message) : base(message) { }

    public McpListenRefusedException(string message, Exception innerException) : base(message, innerException) { }
}
