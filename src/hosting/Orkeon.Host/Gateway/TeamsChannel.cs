using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orkeon.Host.Gateway;

/// <summary>
/// The Microsoft Teams adapter (#446): the Bot Framework protocol over HTTP, on a listener of its own.
/// <para>
/// Unlike Discord, Teams calls the bot. What arrives is therefore anyone's until proven the Bot Connector's: the bearer token
/// is checked before the body is read (<see cref="TeamsTokenValidator"/>), then the body is held to what the token says. A
/// reply goes to the service the token named, and nowhere else.
/// </para>
/// <para>
/// A thread is a run, as everywhere in the gateway. Authorization, routing and throttling are the gateway's; this class
/// translates and guards the door.
/// </para>
/// </summary>
internal sealed partial class TeamsChannel : IChatChannel, IAsyncDisposable, IDisposable
{
    /// <summary>The largest body read: Teams caps a message at about 100 KB.</summary>
    internal const int MaxBodyBytes = 256 * 1024;

    /// <summary>The requests handled at once; one more is asked to come back.</summary>
    internal const int MaxRequests = 64;

    private const string TeamsChannelId = "msteams";

    /// <summary>How long a body may take to arrive.</summary>
    internal static readonly TimeSpan BodyDeadline = TimeSpan.FromSeconds(10);

    /// <summary>The least time between two warnings about refused tokens: their senders are not authenticated.</summary>
    internal static readonly TimeSpan RefusalLogInterval = TimeSpan.FromMinutes(1);

    private const int MaxAcceptFailures = 10;
    private static readonly TimeSpan s_acceptPause = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan s_teardown = TimeSpan.FromSeconds(5);

    private readonly TeamsChannelOptions _options;
    private readonly ILogger<TeamsChannel> _logger;
    private readonly TimeProvider _time;
    private readonly HttpClient _http;
    private readonly BotConnectorKeys _keys;
    private readonly TeamsBotToken _token;
    private readonly TeamsTokenValidator _validator;
    private readonly RecentActivities _recent;
    private readonly SemaphoreSlim _slots = new(MaxRequests, MaxRequests);
    private readonly ConcurrentDictionary<long, Task> _work = new();
    [SuppressMessage("Usage", "CA2213", Justification = "Aborted: Close() after Stop() binds the port again for an instant.")]
    private HttpListener? _listener;
    private readonly ConcurrentDictionary<string, byte> _refusing = new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;
    private long _refusalLoggedAt;

    /// <summary>The channel's own outbound transport.</summary>
    internal static SocketsHttpHandler CreateHandler() =>
        new() { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(10) };

    /// <summary>Builds the channel over its configuration.</summary>
    /// <param name="options">The configuration, validated by the caller.</param>
    /// <param name="logger">Where the channel logs.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="handler">The outbound transport, a double in the tests; the channel's own follows no redirect.</param>
    [SuppressMessage("Reliability", "CA2000", Justification = "The HttpClient owns its handler and disposes it.")]
    public TeamsChannel(
        IOptions<TeamsChannelOptions> options,
        ILogger<TeamsChannel> logger,
        TimeProvider? timeProvider = null,
        HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;

        // No redirect: the bot's token and its secret go to the address they were meant for, or nowhere.
        _http = new HttpClient(handler ?? CreateHandler())
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 1 << 20,
        };
        _keys = new BotConnectorKeys(_http, _time, logger);
        _token = new TeamsBotToken(_options, _http, _time);
        _validator = new TeamsTokenValidator(_options, _keys, _time);
        _recent = new RecentActivities(_time);
        Responder = new ThrottledResponder(new TeamsResponder(_http, _token, _time, logger), _options.ProgressInterval, _time);
    }

    /// <inheritdoc />
    public string Name => "teams";

    /// <inheritdoc />
    public IChatResponder Responder { get; }

    /// <inheritdoc />
    public event Func<CommandInvocation, Task<CommandReply>>? CommandInvoked;

    /// <summary>Takes the port, so that a port already held refuses the start of the host; <see cref="RunAsync"/> answers on it.</summary>
    /// <exception cref="HostConfigurationException">The prefix could not be bound.</exception>
    public void Bind()
    {
        if (_listener is not null)
            return;

        var listener = new HttpListener();
        try
        {
            listener.Prefixes.Add(_options.Prefix);
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            listener.Abort();
            throw new HostConfigurationException(
                $"The Teams channel could not listen on '{_options.Prefix}': {ex.Message}. Another process holds the port, or, under "
                + "Windows, the URL is not reserved for the service's account (netsh http add urlacl).", ex);
        }

        _listener = listener;
    }

    /// <inheritdoc />
    public async Task RunAsync(Func<InboundMessage, CancellationToken, Task> onMessage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onMessage);

        Bind();
        var listener = _listener ?? throw new InvalidOperationException("The Teams listener was not bound.");
        LogListening(_options.Prefix);
        if (_options.ListensInClearBeyondLoopback)
            LogClearListener(_options.Prefix);

        try
        {
            await using (ct.Register(listener.Stop).ConfigureAwait(false))
                await ListenAsync(listener, onMessage, ct).ConfigureAwait(false);
        }
        finally
        {
            listener.Abort();

            // A moment for the answers being sent; a run that ignores its cancellation does not hold the stop.
            await Task.WhenAny(Task.WhenAll(_work.Values), Task.Delay(s_teardown, CancellationToken.None)).ConfigureAwait(false);
        }
    }

    private async Task ListenAsync(HttpListener listener, Func<InboundMessage, CancellationToken, Task> onMessage, CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
                failures = 0;
            }
            catch (Exception ex) when (ct.IsCancellationRequested && (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException))
            {
                return;
            }
            catch (HttpListenerException ex) when (listener.IsListening && ++failures < MaxAcceptFailures)
            {
                // One client's connection, not the listener: a caller must not be able to stop the host.
                LogAcceptFailed(ex);
                await Task.Delay(s_acceptPause, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            // Refused at once rather than queued: the callers of this port are not authenticated yet.
            if (!_slots.Wait(0, CancellationToken.None))
            {
                Answer(context, HttpStatusCode.ServiceUnavailable);
                continue;
            }

            Track(() => HandleAsync(context, onMessage, ct));
        }
    }

    private async Task HandleAsync(HttpListenerContext context, Func<InboundMessage, CancellationToken, Task> onMessage, CancellationToken ct)
    {
        var status = HttpStatusCode.InternalServerError;
        try
        {
            status = await ReceiveAsync(context.Request, onMessage, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException or OperationCanceledException)
        {
            // The client left, or the host is stopping.
            status = HttpStatusCode.ServiceUnavailable;
        }
        finally
        {
            try
            {
                Answer(context, status);
            }
            finally
            {
                _slots.Release();
            }
        }
    }

    /// <summary>One request, from the door to the dispatch. Nothing of the body is read before the token was accepted.</summary>
    private async Task<HttpStatusCode> ReceiveAsync(
        HttpListenerRequest request, Func<InboundMessage, CancellationToken, Task> onMessage, CancellationToken ct)
    {
        if (!string.Equals(request.Url?.AbsolutePath, TeamsChannelOptions.MessagesPath, StringComparison.OrdinalIgnoreCase))
            return HttpStatusCode.NotFound;
        if (request.HttpMethod != HttpMethod.Post.Method)
            return HttpStatusCode.MethodNotAllowed;

        var token = await _validator.ValidateAsync(request.Headers["Authorization"], ct).ConfigureAwait(false);
        if (token is not { Outcome: TeamsTokenOutcome.Accepted, ServiceUrl: { } serviceUrl })
        {
            if (MayLogRefusal())
                LogTokenRefused(token.Reason);
            return token.Outcome == TeamsTokenOutcome.KeysUnavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Unauthorized;
        }

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type)
            || !string.Equals(type.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            return HttpStatusCode.UnsupportedMediaType;
        }

        if (await ReadBodyAsync(request, ct).ConfigureAwait(false) is not { } body)
            return HttpStatusCode.RequestEntityTooLarge;
        if (TeamsActivity.Parse(body) is not { } activity)
            return HttpStatusCode.BadRequest;

        if (RefusalOf(activity, token) is { } refusal)
        {
            LogActivityRefused(refusal);
            return HttpStatusCode.Forbidden;
        }

        return Accept(activity, serviceUrl, onMessage, ct);
    }

    /// <summary>Why the body is not the one the token vouches for, null when it is.</summary>
    private string? RefusalOf(TeamsActivity activity, TeamsTokenResult token)
    {
        if (!string.Equals(activity.ServiceUrl, token.ServiceUrlClaim, StringComparison.OrdinalIgnoreCase))
            return "its serviceUrl is not the token's";
        if (activity.ChannelId != TeamsChannelId)
            return "it is not a Teams activity";
        if (token.Endorsements?.Contains(TeamsChannelId, StringComparer.Ordinal) != true)
            return "the signing key is not endorsed for Teams";
        return activity.IsOfTenant(_options.TenantId) ? null : "it is not of the configured tenant";
    }

    private HttpStatusCode Accept(
        TeamsActivity activity, Uri serviceUrl, Func<InboundMessage, CancellationToken, Task> onMessage, CancellationToken ct)
    {
        // A join, a reaction, an edit, a card's invoke, another bot: acknowledged, and nothing more.
        if (activity.Type != "message" || activity.IsFromBot)
            return HttpStatusCode.OK;
        if (!activity.HasUsableIds
            || activity is not { Id: { } id, RecipientId: { } bot, FromId: { } user })
            return HttpStatusCode.BadRequest;

        if (activity.SenderId is not { } sender)
        {
            LogNoObjectId();
            return HttpStatusCode.OK;
        }

        // A lone mention has nothing to run on. A lone file goes on: the gateway says it was not taken.
        if (activity.Text.Length == 0 && activity.Attachments.Count == 0)
            return HttpStatusCode.OK;

        // A delivery already seen was already acted on. Someone off the list is told so once at a time, and fills no memory.
        var listed = _options.AllowedUserIds.Contains(sender, StringComparer.OrdinalIgnoreCase);
        if (listed ? !_recent.TryAdd($"{activity.ConversationId}\n{id}") : !_refusing.TryAdd(sender, 0))
            return HttpStatusCode.OK;

        var message = new TeamsInboundMessage
        {
            Channel = Name,
            ConversationId = activity.ThreadId,
            RoomId = activity.RoomId,
            SenderId = sender,
            Text = activity.Text,
            Attachments = activity.Attachments,
            ReceivedAt = _time.GetUtcNow(),
            ReplyTo = new TeamsReplyTarget(serviceUrl, activity.ThreadId, id, bot, user),
        };

        // Off the request: the service waits seconds for its 200, a crew works for minutes.
        Track(async () =>
        {
            try
            {
                await (CommandOf(message.Text) is { } command ? RunCommandAsync(command, message, ct) : onMessage(message, ct)).ConfigureAwait(false);
            }
            finally
            {
                if (!listed)
                    _refusing.TryRemove(sender, out _);
            }
        });
        return HttpStatusCode.OK;
    }

    private bool MayLogRefusal()
    {
        var now = _time.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _refusalLoggedAt);
        return now - last >= RefusalLogInterval.Ticks && Interlocked.CompareExchange(ref _refusalLoggedAt, now, last) == last;
    }

    /// <summary>The command a message is, whole: the bot has no slash command of its own in Teams.</summary>
    internal static string? CommandOf(string text) => text.ToUpperInvariant() switch
    {
        "/STOP" => ChatCommands.Stop,
        "/STATUS" => ChatCommands.Status,
        _ => null,
    };

    private async Task RunCommandAsync(string command, TeamsInboundMessage message, CancellationToken ct)
    {
        var answering = CommandInvoked?.Invoke(
            new CommandInvocation(command, message.ConversationId, message.SenderId) { RoomId = message.RoomId });
        var reply = answering is null ? new CommandReply("The host is not listening to commands.") : await answering.ConfigureAwait(false);

        var text = reply.Files.Count == 0 ? reply.Text : $"{reply.Text}\nThis channel does not send files.";
        await Responder.CompleteAsync(message, text, ct).ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1031", Justification = "Fault barrier for detached handlers: an exception here has no caller left to reach, so it is logged instead of lost.")]
    private void Track(Func<Task> work)
    {
        var id = Interlocked.Increment(ref _sequence);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work[id] = done.Task;
        _ = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                LogCancelled();
            }
            catch (Exception ex)
            {
                LogHandlerFailed(ex);
            }
            finally
            {
                _work.TryRemove(id, out _);
                done.SetResult();
            }
        });
    }

    private async Task<byte[]?> ReadBodyAsync(HttpListenerRequest request, CancellationToken ct)
    {
        if (request.ContentLength64 > MaxBodyBytes)
            return null;

        using var deadline = new CancellationTokenSource(BodyDeadline, _time);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.InputStream.ReadAsync(chunk, either.Token).AsTask().WaitAsync(either.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
                return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    [SuppressMessage("Design", "CA1031", Justification = "The client is gone: there is nobody to answer.")]
    private static void Answer(HttpListenerContext context, HttpStatusCode status)
    {
        try
        {
            context.Response.StatusCode = (int)status;

            // A body left unread is not waited for: on a kept connection, Close() would read it to its end, as slowly as it comes.
            if (status != HttpStatusCode.OK)
                context.Response.KeepAlive = false;
            context.Response.ContentLength64 = 0;
            context.Response.Close();
        }
        catch (Exception)
        {
            try
            {
                context.Response.Abort();
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _listener?.Abort();
        _http.Dispose();
        _keys.Dispose();
        _token.Dispose();
        _slots.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The Teams channel listens on {Prefix}")]
    private partial void LogListening(string prefix);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Teams:Prefix '{Prefix}' is plain http beyond the loopback: only a reverse proxy that terminates TLS may reach it, or the bearer tokens cross the network in clear")]
    private partial void LogClearListener(string prefix);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a request to the Teams endpoint: {Reason}")]
    private partial void LogTokenRefused(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Teams listener could not take a connection")]
    private partial void LogAcceptFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused an activity to the Teams endpoint: {Reason}")]
    private partial void LogActivityRefused(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored a Teams message without an Entra object id: its sender cannot be matched with Teams:AllowedUserIds")]
    private partial void LogNoObjectId();

    [LoggerMessage(Level = LogLevel.Debug, Message = "A Teams handler was cancelled")]
    private partial void LogCancelled();

    [LoggerMessage(Level = LogLevel.Error, Message = "A Teams handler failed")]
    private partial void LogHandlerFailed(Exception ex);

    /// <summary>The activities of the last minutes: the service delivers one again when its 200 was late or lost.</summary>
    internal sealed class RecentActivities(TimeProvider time)
    {
        /// <summary>The activities remembered at most.</summary>
        internal const int Capacity = 4096;

        /// <summary>How long one is remembered.</summary>
        internal static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

        private readonly Lock _lock = new();
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
        private readonly Queue<(string Key, DateTimeOffset At)> _order = new();

        /// <summary>Remembers <paramref name="key"/>; false when it was already.</summary>
        public bool TryAdd(string key)
        {
            lock (_lock)
            {
                var now = time.GetUtcNow();
                while (_order.TryPeek(out var oldest) && (now - oldest.At >= Window || _order.Count >= Capacity))
                {
                    _order.Dequeue();
                    _keys.Remove(oldest.Key);
                }

                if (!_keys.Add(key))
                    return false;

                _order.Enqueue((key, now));
                return true;
            }
        }
    }
}
