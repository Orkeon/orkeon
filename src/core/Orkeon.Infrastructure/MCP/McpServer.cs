using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// Dual-era MCP server that exposes Orkeon tools to external MCP clients:
/// it serves modern, stateless requests (2026-07-28 — per-request `_meta`,
/// `server/discover`) and legacy initialize-handshake clients (2025-11-25
/// and earlier) on the same endpoint. Supports running over stdio or
/// processing individual requests.
/// <para>
/// Every <c>tools/call</c> goes through the <see cref="IToolInvocationPipeline"/> every agent
/// turn calls its tools through (GAP-24), under the caller <c>mcp</c>: the guardian's tool
/// phase, the call, the one truncation rule, the result sanitizer and a <c>ToolExecution</c>
/// audit event. The client reads what a crew agent's model reads. <c>AddOrkeonMcpServer</c>
/// registers the server; <c>orkeon mcp serve</c> runs it on stdio.
/// </para>
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class McpServer
{
    /// <summary>Freshness hint returned on tools/list (the DI tool set is stable for a process).</summary>
    private const long ToolListTtlMs = 60_000;

    private const string CancelledMethod = "notifications/cancelled";

    /// <summary>The virtual folder served as resources, and the only one.</summary>
    private const string OutputRoot = "/output";

    private const string OutputUriPrefix = $"orkeon://{OutputRoot}/";

    /// <summary>MCP's "resource not found" error code.</summary>
    private const int ResourceNotFound = -32002;

    /// <summary>
    /// Who makes a call that arrives over MCP — the guardian's agent, the audit trail's role. The
    /// client is no crew agent, and the name it gives itself vouches for nothing.
    /// </summary>
    private static readonly ToolInvocationCaller McpCaller = new("mcp", "mcp") { GateExempt = true };

    private readonly IToolRegistry _toolRegistry;
    private readonly IToolInvocationPipeline _toolInvocation;
    private readonly McpServerOptions _options;
    private readonly ILogger _logger;
    private readonly IFileSystemService _fileSystem;
    private readonly IMcpServerExtension? _extension;
    private readonly HashSet<string>? _allowed;

    /// <summary>Initializes a new instance of <see cref="McpServer"/>.</summary>
    /// <param name="toolRegistry">The tool registry exposing Orkeon tools.</param>
    /// <param name="toolInvocation">The invocation point every <c>tools/call</c> goes through.</param>
    /// <param name="fileSystem">The virtual file system <c>/output</c> is served from, when <see cref="McpServerOptions.ExposeOutput"/> is on; its rights apply.</param>
    /// <param name="options">The MCP server options.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="extension">Optional extra tools and prompts the host serves (FWK-24).</param>
    public McpServer(
        IToolRegistry toolRegistry,
        IToolInvocationPipeline toolInvocation,
        IFileSystemService fileSystem,
        IOptions<McpServerOptions> options,
        ILogger<McpServer>? logger = null,
        IMcpServerExtension? extension = null)
        : this(toolRegistry, toolInvocation, fileSystem, options?.Value ?? new McpServerOptions(), logger, extension)
    {
    }

    /// <summary>Initializes a new instance of <see cref="McpServer"/> with direct options (useful for testing).</summary>
    /// <param name="toolRegistry">The tool registry exposing Orkeon tools.</param>
    /// <param name="toolInvocation">The invocation point every <c>tools/call</c> goes through.</param>
    /// <param name="fileSystem">The virtual file system <c>/output</c> is served from, when <see cref="McpServerOptions.ExposeOutput"/> is on; its rights apply.</param>
    /// <param name="options">The MCP server options.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="extension">Optional extra tools and prompts the host serves (FWK-24).</param>
    public McpServer(
        IToolRegistry toolRegistry,
        IToolInvocationPipeline toolInvocation,
        IFileSystemService fileSystem,
        McpServerOptions options,
        ILogger<McpServer>? logger = null,
        IMcpServerExtension? extension = null)
    {
        _extension = extension;
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(toolInvocation);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _toolRegistry = toolRegistry;
        _toolInvocation = toolInvocation;
        _options = options ?? new McpServerOptions();
        if (_options.MaxConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(options), _options.MaxConcurrency, "McpServerOptions.MaxConcurrency must be at least 1.");
        if (_options.ResourcesPageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(options), _options.ResourcesPageSize, "McpServerOptions.ResourcesPageSize must be at least 1.");
        if (_options.MaxResourceBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(options), _options.MaxResourceBytes, "McpServerOptions.MaxResourceBytes must be at least 1.");
        _fileSystem = fileSystem;
        _logger = logger ?? NullLogger<McpServer>.Instance;
        _allowed = _options.AllowedTools.Count == 0
            ? null
            : new HashSet<string>(_options.AllowedTools, StringComparer.OrdinalIgnoreCase);
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
        return ProcessRequestWithNotificationsAsync(request, null, ct);
    }

    /// <summary>
    /// The request's processing. <paramref name="notify"/> carries what the call sends the client before its answer
    /// (<c>notifications/progress</c>); null when there is no channel to send on.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "JSON-RPC boundary fault barrier: any handler failure is converted into a JSON-RPC InternalError response so one bad request cannot crash the MCP server.")]
    private Task<JsonRpcResponse?> ProcessRequestWithNotificationsAsync(JsonRpcRequest request, Action<JsonRpcNotification>? notify, CancellationToken ct)
    {
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
                    "tools/call" => await HandleToolsCall(request, isModern, notify, ct).ConfigureAwait(false),
                    "prompts/list" when HasPrompts => HandlePromptsList(request),
                    "prompts/get" when HasPrompts => await HandlePromptsGet(request, ct).ConfigureAwait(false),
                    "resources/list" when _options.ExposeOutput => await HandleResourcesList(request, ct).ConfigureAwait(false),
                    "resources/read" when _options.ExposeOutput => await HandleResourcesRead(request, ct).ConfigureAwait(false),
                    _ => CreateErrorResponse(request.Id, JsonRpcErrorCodes.MethodNotFound,
                        $"Method not found: {request.Method}")
                };
            }
            catch (McpRequestException ex)
            {
                return CreateErrorResponse(request.Id, ex.Code, ex.Message);
            }
            catch (Exception ex)
            {
                // The details stay in the log: a message may carry a path, a connection string, a stack of the host.
                LogErrorProcessingRequest(ex, request.Method);
                return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InternalError, "Internal error while processing the request.");
            }
        }
    }

    /// <summary>
    /// Runs the MCP server over a stdio-style channel, reading JSON-RPC messages
    /// line by line from <paramref name="reader"/> and writing responses to
    /// <paramref name="writer"/> (notifications get none), until the reader ends.
    /// The streams are the caller's: read and written, never disposed. The writer
    /// is the protocol channel: nothing else may write to it.
    /// </summary>
    /// <param name="reader">The channel requests are read from (the process's stdin for <c>orkeon mcp serve</c>).</param>
    /// <param name="writer">The channel responses are written to (the process's stdout for <c>orkeon mcp serve</c>).</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task RunStdioAsync(TextReader reader, TextWriter writer, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        LogMcpServerStarting();

        // One Task per request, at most MaxConcurrency of them running (the others wait their turn,
        // and a notifications/cancelled reaches them there too); the answers go through one channel
        // to one writer, so two answers never interleave on the protocol channel.
        using var gate = new SemaphoreSlim(_options.MaxConcurrency);
        var outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var inFlight = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        var running = new ConcurrentDictionary<Task, byte>();

        // A writer that fails ends the serving: nothing could be answered any more, so the loop
        // stops reading and the fault surfaces from this method.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = PumpAsync(outbox.Reader, new LineSink(writer), ct);
        _ = pump.ContinueWith(_ => stop.Cancel(), CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        var token = stop.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var request = ParseRequest(line, outbox.Writer);
                if (request == null) continue;

                if (string.Equals(request.Method, CancelledMethod, StringComparison.Ordinal))
                {
                    Cancel(inFlight, request);
                    continue;
                }

                if (IsNotification(request))
                {
                    await ProcessRequestAsync(request, ct).ConfigureAwait(false);
                    continue;
                }

                // Registered before the task starts: a cancel read right after this line always finds it.
                var key = request.Id!.Value.GetRawText();
                var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                if (!inFlight.TryAdd(key, cts))
                {
                    cts.Dispose();
                    outbox.Writer.TryWrite(JsonSerializer.Serialize(CreateErrorResponse(request.Id,
                        JsonRpcErrorCodes.InvalidRequest, "A request with this id is already in flight")));
                    continue;
                }

                var run = new RequestRun(key, cts, gate, inFlight, outbox.Writer);
                var task = ServeRequestAsync(request, run);
                running[task] = 0;
                _ = task.ContinueWith(done => running.TryRemove(done, out _), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        finally
        {
            // The client closed stdin (or the server is stopping): every request read is answered
            // first, unless the stop cancelled it. The outbox is completed whatever happens, so
            // the pump always ends. A call still waiting for its crew is let go rather than run a
            // paid crew in a queue nobody adds to.
            _extension?.StopAdmitting();
            try
            {
                await Task.WhenAll(running.Keys).ConfigureAwait(false);
            }
            finally
            {
                outbox.Writer.TryComplete();
            }

            try
            {
                await pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Stopped: the answers still queued are dropped with the connection.
            }
        }
    }

    /// <summary>Parses one line; a line that is not JSON-RPC is answered with a parse error and yields <see langword="null"/>.</summary>
    private JsonRpcRequest? ParseRequest(string line, ChannelWriter<string> outbox)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonRpcRequest>(line);
        }
        catch (JsonException ex)
        {
            LogFailedToParseRequest(ex, line);
            outbox.TryWrite(JsonSerializer.Serialize(CreateErrorResponse(null, JsonRpcErrorCodes.ParseError,
                "Failed to parse JSON-RPC request")));
            return null;
        }
    }

    /// <summary>What one request task needs: its key, its cancellation source, and the loop's shared state.</summary>
    private sealed record RequestRun(
        string Key,
        CancellationTokenSource Cts,
        SemaphoreSlim Gate,
        ConcurrentDictionary<string, CancellationTokenSource> InFlight,
        ChannelWriter<string> Outbox);

    private async Task ServeRequestAsync(JsonRpcRequest request, RequestRun run)
    {
        var key = run.Key;
        var cts = run.Cts;
        var gate = run.Gate;
        var inFlight = run.InFlight;
        var outbox = run.Outbox;
        // Off the read loop: a handler that blocks before its first await must not stop the reading.
        await Task.Yield();
        JsonRpcResponse? response = null;
        var cancelled = false;
        try
        {
            // A call of a tool the extension runs (a crew) is bounded by the extension itself, one run per crew: it
            // holds no slot of the server, or its callers waiting for a crew would starve tools/list and ping.
            var slotted = !CallsExtensionTool(request);
            if (slotted)
                await gate.WaitAsync(cts.Token).ConfigureAwait(false);
            try
            {
                response = await ProcessRequestWithNotificationsAsync(
                    request,
                    notification => outbox.TryWrite(JsonSerializer.Serialize(notification)),
                    cts.Token).ConfigureAwait(false);
            }
            finally
            {
                if (slotted)
                    gate.Release();
            }

            // A cancelled request is not answered (MCP cancellation).
            cancelled = cts.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Cancelled while waiting for its turn: it never started and is not answered.
            cancelled = true;
        }
        finally
        {
            inFlight.TryRemove(key, out _);
            cts.Dispose();
        }

        // Queued once the id is free again: a client that reuses an id the moment it reads the
        // answer is never refused as a duplicate.
        if (response != null && !cancelled)
            outbox.TryWrite(JsonSerializer.Serialize(response));
    }

    private bool CallsExtensionTool(JsonRpcRequest request) =>
        _extension is { } extension
        && request.Method == "tools/call"
        && request.Params is { ValueKind: JsonValueKind.Object } p
        && p.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
        && extension.OwnsTool(name.GetString()!);

    private sealed record LineSink(TextWriter Writer);

    private static async Task PumpAsync(ChannelReader<string> outbox, LineSink sink, CancellationToken ct)
    {
        var writer = sink.Writer;
        await foreach (var line in outbox.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            await writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
            await writer.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    private static bool IsNotification(JsonRpcRequest request) =>
        request.Id is null ||
        request.Id.Value.ValueKind == JsonValueKind.Null ||
        request.Method.StartsWith("notifications/", StringComparison.Ordinal);

    /// <summary>
    /// <c>notifications/cancelled</c>: cancels the in-flight request <c>requestId</c> names. An id the
    /// server does not know (finished, never sent) is ignored, as the specification asks.
    /// </summary>
    private static void Cancel(ConcurrentDictionary<string, CancellationTokenSource> inFlight, JsonRpcRequest notification)
    {
        if (notification.Params is not { ValueKind: JsonValueKind.Object } p ||
            !p.TryGetProperty("requestId", out var id) ||
            id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
            return;

        if (!inFlight.TryGetValue(id.GetRawText(), out var cts))
            return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The request finished between the lookup and the cancel.
        }
    }

    private JsonRpcResponse HandleDiscover(JsonRpcRequest request)
    {
        var result = new McpDiscoverResult
        {
            SupportedVersions = McpProtocol.SupportedVersions,
            Capabilities = new McpServerCapabilities
            {
                Tools = new McpCapabilityInfo { ListChanged = true },
                Resources = ResourcesCapability(),
                Prompts = PromptsCapability()
            },
            TtlMs = ToolListTtlMs,
            CacheScope = "private",
            Meta = BuildServerInfoMeta()
        };

        return CreateSuccessResponse(request.Id, result);
    }

    private bool HasPrompts => _extension is { Prompts.Count: > 0 };

    private McpCapabilityInfo? PromptsCapability() => HasPrompts ? new McpCapabilityInfo { ListChanged = false } : null;

    private JsonRpcResponse HandlePromptsList(JsonRpcRequest request) =>
        CreateSuccessResponse(request.Id, new McpPromptListResult { Prompts = _extension!.Prompts });

    private async Task<JsonRpcResponse> HandlePromptsGet(JsonRpcRequest request, CancellationToken ct)
    {
        string? name = null;
        JsonElement? arguments = null;
        if (request.Params is { ValueKind: JsonValueKind.Object } p)
        {
            if (p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                name = n.GetString();
            if (p.TryGetProperty("arguments", out var a))
                arguments = a;
        }

        if (string.IsNullOrEmpty(name))
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, "Missing prompt name in parameters");

        var result = await _extension!.GetPromptAsync(name, arguments, ct).ConfigureAwait(false);
        return CreateSuccessResponse(request.Id, result);
    }

    /// <summary>The progress channel of a call: notifications when the client gave a <c>progressToken</c> and there is a channel to send on.</summary>
    private static IMcpProgress ProgressFor(JsonRpcRequest request, Action<JsonRpcNotification>? notify)
    {
        if (notify is null
            || request.Params is not { ValueKind: JsonValueKind.Object } p
            || !p.TryGetProperty("_meta", out var meta)
            || meta.ValueKind != JsonValueKind.Object
            || !meta.TryGetProperty("progressToken", out var token)
            || token.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
        {
            return NoProgress.Instance;
        }

        return new NotifyingProgress(token.Clone(), notify);
    }

    private sealed class NoProgress : IMcpProgress
    {
        public static readonly NoProgress Instance = new();

        public void Report(double progress, double? total = null, string? message = null)
        {
            // Nothing to report: the caller sent no progress token.
        }
    }

    /// <summary>Turns each report into a <c>notifications/progress</c>; a value that does not exceed the last one is dropped, as the spec requires.</summary>
    private sealed class NotifyingProgress : IMcpProgress
    {
        private readonly JsonElement _token;
        private readonly Action<JsonRpcNotification> _notify;
        private readonly Lock _gate;
        private double _last;

        public NotifyingProgress(JsonElement token, Action<JsonRpcNotification> notify)
        {
            _token = token;
            _notify = notify;
            _gate = new Lock();
            _last = double.NegativeInfinity;
        }

        public void Report(double progress, double? total = null, string? message = null)
        {
            lock (_gate)
            {
                if (progress <= _last)
                    return;
                _last = progress;
                var parameters = new Dictionary<string, object?> { ["progressToken"] = _token, ["progress"] = progress };
                if (total is { } t)
                    parameters["total"] = t;
                if (message is not null)
                    parameters["message"] = message;
                _notify(new JsonRpcNotification
                {
                    Method = "notifications/progress",
                    Params = JsonSerializer.SerializeToElement(parameters),
                });
            }
        }
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
                Tools = new McpCapabilityInfo { ListChanged = true },
                Resources = ResourcesCapability(),
                Prompts = PromptsCapability()
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
            .Where(t => IsAllowed(t.Name))
            .Select(t => new McpToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = ConvertToolSchemaToJsonSchema(t.Schema)
            })
            .Concat(_extension?.Tools ?? [])
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

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

    private async Task<JsonRpcResponse> HandleToolsCall(JsonRpcRequest request, bool isModern, Action<JsonRpcNotification>? notify, CancellationToken ct)
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

        // A tool the host runs itself (a crew): it answers, with the progress the client asked for.
        if (_extension is { } extension && extension.OwnsTool(callParams.Name))
        {
            var progress = ProgressFor(request, notify);
            var extended = await extension.CallToolAsync(callParams.Name, callParams.Arguments, progress, ct).ConfigureAwait(false);
            extended.ResultType ??= "complete";
            return CreateSuccessResponse(request.Id, extended);
        }

        var tool = IsAllowed(callParams.Name)
            ? await _toolRegistry.GetToolByNameAsync(callParams.Name).ConfigureAwait(false)
            : null;
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

        // The invocation point of every agent turn (GAP-24): a blocked call never reaches the
        // tool, a result is truncated and tagged as data, each call is audited — and the client
        // reads the text a crew agent's model reads, an error included.
        var invocation = await _toolInvocation
            .InvokeAsync(new ToolInvocation(tool, parameters, McpCaller), ct)
            .ConfigureAwait(false);

        var mcpResult = new McpToolCallResult
        {
            IsError = !invocation.Success,
            ResultType = "complete",
            Content =
            [
                new()
                {
                    Type = "text",
                    Text = invocation.ConversationText
                }
            ]
        };
        _ = isModern; // both eras share the same result shape for tools/call

        return CreateSuccessResponse(request.Id, mcpResult);
    }

    /// <summary>Whether a registry tool is served: <see cref="McpServerOptions.AllowedTools"/> is empty or names it.</summary>
    private bool IsAllowed(string name) =>
        _allowed is null || _allowed.Contains(name);

    private McpCapabilityInfo? ResourcesCapability() => _options.ExposeOutput ? new McpCapabilityInfo() : null;

    /// <summary>
    /// <c>resources/list</c>: the files under <c>/output</c>, in path order, <see cref="McpServerOptions.ResourcesPageSize"/>
    /// per page. The cursor is the last path served, opaque to the client. Read through the virtual
    /// file system, which checks the mount and its rights; nothing else is listed.
    /// </summary>
    private async Task<JsonRpcResponse> HandleResourcesList(JsonRpcRequest request, CancellationToken ct)
    {
        string? after = null;
        if (request.Params is { ValueKind: JsonValueKind.Object } p &&
            p.TryGetProperty("cursor", out var cursor) && cursor.ValueKind == JsonValueKind.String)
        {
            after = DecodeCursor(cursor.GetString());
            if (after == null)
                return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, "Invalid cursor");
        }

        if (!_fileSystem.ResolveAndValidate(OutputRoot, FileAccessRights.Read).IsAllowed)
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, "The output folder is not readable");

        var files = new List<VirtualFileEntry>();
        try
        {
            await foreach (var entry in _fileSystem
                .EnumerateFilesAsync(OutputRoot, new VirtualEnumerationOptions(Recursive: true), ct)
                .ConfigureAwait(false))
            {
                if (entry.Kind == VirtualEntryKind.File &&
                    entry.VirtualPath.StartsWith(OutputRoot + "/", StringComparison.Ordinal) &&
                    (after == null || string.CompareOrdinal(entry.VirtualPath, after) > 0))
                    files.Add(entry);
            }
        }
        catch (FileAccessDeniedException)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, "The output folder is not readable");
        }

        var ordered = files.OrderBy(f => f.VirtualPath, StringComparer.Ordinal).ToList();
        var page = ordered.Take(_options.ResourcesPageSize).ToList();
        var result = new Dictionary<string, object>
        {
            ["resources"] = page.Select(f => new
            {
                uri = ToUri(f.VirtualPath),
                name = f.VirtualPath[(f.VirtualPath.LastIndexOf('/') + 1)..],
                mimeType = MimeTypeOf(f.VirtualPath, text: null),
                size = f.SizeBytes
            }).ToList()
        };
        if (ordered.Count > page.Count)
            result["nextCursor"] = EncodeCursor(page[^1].VirtualPath);
        return CreateSuccessResponse(request.Id, result);
    }

    /// <summary>
    /// <c>resources/read</c>: one file under <c>/output</c>, as text when it is UTF-8 text, as a
    /// base64 blob otherwise. The URI is parsed strictly — scheme, the <c>/output/</c> prefix, no
    /// <c>.</c>, <c>..</c>, empty or separator-bearing segment once unescaped, no query or fragment —
    /// then the virtual file system checks the mount and its Read right. Errors name the URI the
    /// client sent, never a physical path.
    /// </summary>
    private async Task<JsonRpcResponse> HandleResourcesRead(JsonRpcRequest request, CancellationToken ct)
    {
        if (request.Params is not { ValueKind: JsonValueKind.Object } p ||
            !p.TryGetProperty("uri", out var uriElement) || uriElement.ValueKind != JsonValueKind.String)
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, "Missing resource uri in parameters");

        var uri = uriElement.GetString()!;
        var path = ToOutputPath(uri);
        if (path == null)
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, $"Not a resource of this server: {uri}");

        if (!_fileSystem.ResolveAndValidate(path, FileAccessRights.Read).IsAllowed)
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, $"Resource not readable: {uri}");

        byte[]? bytes;
        try
        {
            // No link is followed: every folder on the way must be a plain directory (a symlinked
            // one may lead out of /output, where the lexical path check cannot see), and the file
            // a plain file (a link is reported as a SymLink entry, never served).
            if (!await FoldersArePlainAsync(path, ct).ConfigureAwait(false))
                return CreateErrorResponse(request.Id, ResourceNotFound, $"Resource not found: {uri}");

            var entry = await _fileSystem.TryGetEntryAsync(path, ct).ConfigureAwait(false);
            if (entry == null || entry.Kind != VirtualEntryKind.File || entry.LinkTarget != null)
                return CreateErrorResponse(request.Id, ResourceNotFound, $"Resource not found: {uri}");
            if (entry.SizeBytes > _options.MaxResourceBytes)
                return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams,
                    $"Resource too large (limit {_options.MaxResourceBytes} bytes): {uri}");

            // Bounded read: a file that grew since the size check is cut at the limit and refused.
            bytes = await ReadBoundedAsync(path, ct).ConfigureAwait(false);
        }
        catch (FileAccessDeniedException)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, $"Resource not readable: {uri}");
        }
        catch (FileNotFoundException)
        {
            return CreateErrorResponse(request.Id, ResourceNotFound, $"Resource not found: {uri}");
        }

        if (bytes == null)
            return CreateErrorResponse(request.Id, ResourceNotFound, $"Resource not found: {uri}");
        if (bytes.Length > _options.MaxResourceBytes)
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams,
                $"Resource too large (limit {_options.MaxResourceBytes} bytes): {uri}");

        var text = TryDecodeText(bytes);
        var content = text != null
            ? new Dictionary<string, object> { ["uri"] = uri, ["mimeType"] = MimeTypeOf(path, text: true), ["text"] = text }
            : new Dictionary<string, object> { ["uri"] = uri, ["mimeType"] = MimeTypeOf(path, text: false), ["blob"] = Convert.ToBase64String(bytes) };
        return CreateSuccessResponse(request.Id, new { contents = new[] { content } });
    }

    /// <summary>Whether every folder on the way to <paramref name="path"/> is a plain directory (no link).</summary>
    private async Task<bool> FoldersArePlainAsync(string path, CancellationToken ct)
    {
        var folder = new System.Text.StringBuilder(OutputRoot);
        foreach (var segment in path[(OutputRoot.Length + 1)..].Split('/')[..^1])
        {
            folder.Append('/').Append(segment);
            var parent = await _fileSystem.TryGetEntryAsync(folder.ToString(), ct).ConfigureAwait(false);
            if (parent == null || parent.Kind != VirtualEntryKind.Directory || parent.LinkTarget != null)
                return false;
        }

        return true;
    }

    private async Task<byte[]> ReadBoundedAsync(string path, CancellationToken ct)
    {
        using var stream = await _fileSystem.OpenReadStreamAsync(path, ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while (buffer.Length <= _options.MaxResourceBytes &&
               (read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, _options.MaxResourceBytes + 1L - buffer.Length)), ct).ConfigureAwait(false)) > 0)
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static string ToUri(string virtualPath) =>
        OutputUriPrefix + string.Join('/', virtualPath[(OutputRoot.Length + 1)..].Split('/').Select(Uri.EscapeDataString));

    /// <summary>The virtual path a resource URI names under <c>/output</c>, or null when it names anything else.</summary>
    private static string? ToOutputPath(string uri)
    {
        if (!uri.StartsWith(OutputUriPrefix, StringComparison.Ordinal) ||
            uri.Contains('?', StringComparison.Ordinal) || uri.Contains('#', StringComparison.Ordinal))
            return null;

        var segments = new List<string>();
        foreach (var raw in uri[OutputUriPrefix.Length..].Split('/'))
        {
            string segment;
            try
            {
                segment = Uri.UnescapeDataString(raw);
            }
            catch (UriFormatException)
            {
                return null;
            }

            if (segment.Length == 0 || segment is "." or ".." ||
                segment.AsSpan().IndexOfAny('/', '\\', '\0') >= 0)
                return null;
            segments.Add(segment);
        }

        return OutputRoot + "/" + string.Join('/', segments);
    }

    private static string EncodeCursor(string lastPath) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(lastPath)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            return null;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
            var path = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(padded));
            return path.StartsWith(OutputRoot + "/", StringComparison.Ordinal) ? path : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? TryDecodeText(byte[] bytes)
    {
        if (Array.IndexOf(bytes, (byte)0) >= 0)
            return null;
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string MimeTypeOf(string path, bool? text) =>
        Path.GetExtension(path).ToUpperInvariant() switch
        {
            ".MD" => "text/markdown",
            ".TXT" or ".LOG" => "text/plain",
            ".CSV" => "text/csv",
            ".JSON" => "application/json",
            ".HTML" or ".HTM" => "text/html",
            ".XML" => "application/xml",
            ".YAML" or ".YML" => "application/yaml",
            ".PNG" => "image/png",
            ".JPG" or ".JPEG" => "image/jpeg",
            ".GIF" => "image/gif",
            ".SVG" => "image/svg+xml",
            ".PDF" => "application/pdf",
            _ => text == true ? "text/plain" : "application/octet-stream"
        };

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
            var paramSchema = new Dictionary<string, object>();

            if (kvp.Value.Type != null)
                paramSchema["type"] = kvp.Value.Type;

            paramSchema["description"] = kvp.Value.Description;

            if (kvp.Value.Default != null)
                paramSchema["default"] = kvp.Value.Default;

            if (kvp.Value.Enum != null && kvp.Value.Enum.Count > 0)
                paramSchema["enum"] = kvp.Value.Enum;

            if (kvp.Value.Values != null)
                paramSchema["additionalProperties"] = kvp.Value.Values.ToValueDocument();

            properties[kvp.Key] = paramSchema;

            if (kvp.Value.Required)
                required.Add(kvp.Key);
        }

        jsonSchema["properties"] = properties;
        if (required.Count > 0)
            jsonSchema["required"] = required;

        var json = JsonSerializer.Serialize(jsonSchema);
        return JsonElement.Parse(json);
    }

    private static JsonRpcResponse CreateSuccessResponse(JsonElement? id, object result)
    {
        var json = JsonSerializer.Serialize(result);
        return new JsonRpcResponse
        {
            Id = id,
            Result = JsonElement.Parse(json)
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
