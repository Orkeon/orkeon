using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Domain.Common;

namespace Orkeon.Infrastructure.EventHub;

/// <summary>
/// In-process, lock-free <see cref="IEventHub"/> built on <see cref="System.Threading.Channels"/>
/// and <see cref="ConcurrentDictionary{TKey,TValue}"/>. Patterned after
/// <c>InMemoryAgentChannel</c>. Designed for single-process orchestration; swap for a
/// distributed implementation (e.g. Redis Streams) for multi-host deployments.
/// </summary>
public sealed partial class InMemoryEventHub : IEventHub, IDisposable
{

    // Synthetic topics stamped on Post/Send/Reply messages so they round-trip through Message envelopes.
    private const string PostTopic = "_mailbox.post";
    private const string SendTopic = "_mailbox.send";
    private const string ReplyTopic = "_mailbox.reply";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IEventHubCallerContext _callerContext;
    private readonly EventHubMiddlewarePipeline _pipeline;
    private readonly ILogger<InMemoryEventHub> _logger;

    // Subscribers: topic → (subscriptionId → SubscriberState).
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Subscriber>> _subscribers
        = new(StringComparer.Ordinal);

    // Mailboxes keyed by raw URI string. Each holds an unbounded MPSC channel.
    private readonly ConcurrentDictionary<string, MailboxEntry> _mailboxes
        = new(StringComparer.Ordinal);

    // Pending Send TCS keyed by correlation id value.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Message>> _pendingReplies
        = new(StringComparer.Ordinal);

    // OnReply waiters (WaitForAsync), keyed by correlation id value.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Message>> _replyWaiters
        = new(StringComparer.Ordinal);

    // LastValueCache: keyed by (scope:string, key:string). Scope is crewId.ToString() or "" for global.
    private readonly ConcurrentDictionary<(string Scope, string Key), Message> _lastValues = new();

    /// <summary>Initializes a new instance of <see cref="InMemoryEventHub"/>.</summary>
    public InMemoryEventHub(
        IEventHubCallerContext callerContext,
        ILogger<InMemoryEventHub> logger,
        IEnumerable<IEventHubMiddleware>? middlewares = null)
    {
        ArgumentNullException.ThrowIfNull(callerContext);
        ArgumentNullException.ThrowIfNull(logger);
        _callerContext = callerContext;
        _logger = logger;
        _pipeline = new EventHubMiddlewarePipeline(middlewares ?? []);
    }

    // ── Mailbox lifecycle ───────────────────────────────────────────────

    /// <summary>
    /// Registers a mailbox under <paramref name="address"/>. Posts to an unregistered mailbox raise
    /// <see cref="MailboxNotFoundException"/>. Returns an <see cref="IDisposable"/> that removes the
    /// mailbox when disposed.
    /// </summary>
    public IDisposable RegisterMailbox(MailboxAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var entry = _mailboxes.GetOrAdd(address.Raw, _ => new MailboxEntry(address));
        entry.IncrementRef();
        return new MailboxRegistration(this, address.Raw);
    }

    private void UnregisterMailbox(string rawAddress)
    {
        if (_mailboxes.TryGetValue(rawAddress, out var entry) && entry.DecrementRef() == 0)
        {
            _mailboxes.TryRemove(rawAddress, out _);
            entry.Channel.Writer.TryComplete();
        }
    }

    // ── PublishAsync ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async System.Threading.Tasks.Task PublishAsync(
        string topic,
        object payload,
        PublishOptions? options,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ct.ThrowIfCancellationRequested();

        var caller = _callerContext.Current;
        var message = BuildMessage(new MessageBuildContext
        {
            Topic = topic,
            SourceCrewId = caller.CrewId,
            SourceAgentId = caller.AgentId,
            TargetCrewId = options?.TargetCrewId,
            TargetMailbox = null,
            CorrelationId = null,
            Payload = payload,
            Metadata = options?.Metadata,
            SchemaId = options?.SchemaId ?? Message.NoDeclaredSchemaId
        });

        message = await _pipeline.OnPublishAsync(message, ct).ConfigureAwait(false);
        DispatchToTopic(topic, message);

        if (options?.RetainAsLastValue == true)
        {
            if (string.IsNullOrWhiteSpace(options.LastValueKey))
                throw new ArgumentException(
                    "PublishOptions.LastValueKey is required when RetainAsLastValue is true.",
                    nameof(options));

            var scopeKey = options.TargetCrewId is null ? "" : options.TargetCrewId.ToString()!;
            _lastValues[(scopeKey, options.LastValueKey)] = message;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var scope = options?.TargetCrewId?.ToString() ?? "<global>";
            LogPublished(topic, message.Id, scope);
        }
    }

    /// <summary>
    /// Runs the receive stages for one recipient about to consume <paramref name="message"/>,
    /// returning <see langword="null"/> when a stage says the recipient already had it. A
    /// duplicate is not an error the consumer should ever see, so it is swallowed here and the
    /// message is skipped.
    /// </summary>
    private async Task<Message?> DeliverAsync(Message message, CancellationToken ct)
    {
        try
        {
            return await _pipeline.OnReceiveAsync(message, ct).ConfigureAwait(false);
        }
        catch (DuplicateMessageException)
        {
            LogDuplicateDropped(message.Topic, message.Id);
            return null;
        }
    }

    private void DispatchToTopic(string topic, Message message)
    {
        if (!_subscribers.TryGetValue(topic, out var bucket))
            return;

        foreach (var subscriber in bucket.Values)
        {
            if (!ShouldDeliver(message, subscriber))
                continue;

            // Channels are unbounded → TryWrite never blocks and never returns false unless completed.
            subscriber.Channel.Writer.TryWrite(message);
        }
    }

    private static bool ShouldDeliver(Message message, Subscriber subscriber)
    {
        // Spec §10.1 — a subscriber in crew Y receives messages with TargetCrewId ∈ {null, Y}.
        if (message.TargetCrewId is null) return true;
        if (subscriber.SubscriberCrewId is null) return true; // unscoped subscriber sees everything
        return message.TargetCrewId.Equals(subscriber.SubscriberCrewId);
    }

    // ── PostAsync ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public System.Threading.Tasks.Task PostAsync(
        MailboxAddress recipient,
        object payload,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ct.ThrowIfCancellationRequested();
        return PostCoreAsync(recipient, payload, correlation: null, topic: PostTopic, ct);
    }

    private async System.Threading.Tasks.Task PostCoreAsync(
        MailboxAddress to,
        object payload,
        CorrelationId? correlation,
        string topic,
        CancellationToken ct)
    {
        if (!_mailboxes.TryGetValue(to.Raw, out var entry))
            throw new MailboxNotFoundException(to.Raw);

        var caller = _callerContext.Current;
        var message = BuildMessage(new MessageBuildContext
        {
            Topic = topic,
            SourceCrewId = caller.CrewId,
            SourceAgentId = caller.AgentId,
            TargetCrewId = to.CrewId,
            TargetMailbox = to,
            CorrelationId = correlation,
            Payload = payload,
            Metadata = null,
            SchemaId = Message.NoDeclaredSchemaId
        });

        // Mailbox traffic goes through the same stages as a publish. It has to: the ACL guards
        // who may reach a mailbox, and client:// — the one address that leaves the process —
        // is only ever reached this way.
        message = await _pipeline.OnPublishAsync(message, ct).ConfigureAwait(false);

        entry.Channel.Writer.TryWrite(message);
        if (_logger.IsEnabled(LogLevel.Debug))
            LogPosted(to.Raw, message.Id);
    }

    // ── SendAsync ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<TResponse> SendAsync<TRequest, TResponse>(
        MailboxAddress recipient,
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "SendAsync requires a positive timeout. ForeverWaitTimeout is not allowed for Send (spec §9.3).");

        return SendCoreAsync<TRequest, TResponse>(recipient, request, timeout, ct);
    }

    private async Task<TResponse> SendCoreAsync<TRequest, TResponse>(
        MailboxAddress to,
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (!_mailboxes.TryGetValue(to.Raw, out _))
            throw new MailboxNotFoundException(to.Raw);

        var correlation = CorrelationId.NewId();
        var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        var correlationKey = correlation.AsString();
        if (!_pendingReplies.TryAdd(correlationKey, tcs))
        {
            throw new InvalidOperationException(
                $"Internal error: correlation id collision for '{correlationKey}'.");
        }

        try
        {
            await PostCoreAsync(to, request!, correlation, SendTopic, ct).ConfigureAwait(false);
        }
        catch
        {
            _pendingReplies.TryRemove(correlationKey, out _);
            throw;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        await using var registration = timeoutCts.Token.Register(static state =>
        {
            var ctx = (TaskCompletionSource<Message>)state!;
            ctx.TrySetCanceled();
        }, tcs).ConfigureAwait(false);

        try
        {
            var replyMessage = await tcs.Task.ConfigureAwait(false);
            return DeserializePayload<TResponse>(replyMessage.Payload)!;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SendTimeoutException(correlationKey, timeout);
        }
        finally
        {
            _pendingReplies.TryRemove(correlationKey, out _);
        }
    }

    // ── ReplyAsync ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public System.Threading.Tasks.Task ReplyAsync(
        CorrelationId correlation,
        object payload,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(correlation);
        ct.ThrowIfCancellationRequested();

        var correlationKey = correlation.AsString();
        if (_pendingReplies.TryRemove(correlationKey, out var pendingTcs))
        {
            var caller = _callerContext.Current;
            var replyMessage = BuildMessage(new MessageBuildContext
            {
                Topic = ReplyTopic,
                SourceCrewId = caller.CrewId,
                SourceAgentId = caller.AgentId,
                TargetCrewId = null,
                TargetMailbox = null,
                CorrelationId = correlation,
                Payload = payload,
                Metadata = null,
                SchemaId = Message.NoDeclaredSchemaId
            });
            pendingTcs.TrySetResult(replyMessage);
            // Also fulfil any WaitForAsync(OnReply) waiter for the same id.
            if (_replyWaiters.TryRemove(correlationKey, out var waiterTcs))
                waiterTcs.TrySetResult(replyMessage);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        if (_replyWaiters.TryRemove(correlationKey, out var waiterOnly))
        {
            var caller = _callerContext.Current;
            var replyMessage = BuildMessage(new MessageBuildContext
            {
                Topic = ReplyTopic,
                SourceCrewId = caller.CrewId,
                SourceAgentId = caller.AgentId,
                TargetCrewId = null,
                TargetMailbox = null,
                CorrelationId = correlation,
                Payload = payload,
                Metadata = null,
                SchemaId = Message.NoDeclaredSchemaId
            });
            waiterOnly.TrySetResult(replyMessage);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        throw new UnknownCorrelationException(correlationKey);
    }

    // ── SubscribeAsync ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public IAsyncEnumerable<Message> SubscribeAsync(string topic, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        var subscriberCrewId = _callerContext.Current.CrewId;
        // Treat the system caller as "unscoped" so default-context subscribers see all messages,
        // which is the natural test ergonomic.
        var effectiveScope = CrewId.IsSystem(subscriberCrewId) ? null : subscriberCrewId;

        // Eagerly register the subscription so callers observing the bucket immediately after
        // SubscribeAsync returns will see it. Avoids a race with publishes interleaved with
        // first-MoveNextAsync().
        var channel = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        var subscriberId = Guid.NewGuid();
        var subscriber = new Subscriber(channel, effectiveScope);
        var bucket = _subscribers.GetOrAdd(topic, _ => new ConcurrentDictionary<Guid, Subscriber>());
        bucket[subscriberId] = subscriber;

        return ConsumeAsync(topic, subscriberId, channel, ct);
    }

    private async IAsyncEnumerable<Message> ConsumeAsync(
        string topic,
        Guid subscriberId,
        Channel<Message> channel,
        [EnumeratorCancellation] CancellationToken ct)
    {
        try
        {
            while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var msg))
                {
                    var delivered = await DeliverAsync(msg, ct).ConfigureAwait(false);
                    if (delivered is not null)
                        yield return delivered;
                }
            }
        }
        finally
        {
            if (_subscribers.TryGetValue(topic, out var bucket))
                bucket.TryRemove(subscriberId, out _);
            channel.Writer.TryComplete();
        }
    }

    /// <summary>Test hook: returns the live subscriber count for <paramref name="topic"/>.</summary>
    internal int GetSubscriberCount(string topic)
        => _subscribers.TryGetValue(topic, out var bucket) ? bucket.Count : 0;

    /// <summary>Test hook: returns the live mailbox count.</summary>
    internal int GetMailboxCount() => _mailboxes.Count;

    /// <summary>Test hook: returns the raw URIs of registered mailboxes.</summary>
    internal IReadOnlyCollection<string> GetMailboxAddresses() => _mailboxes.Keys.ToArray();

    // ── WaitForAsync ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<Message> WaitForAsync(
        WaitDescriptor descriptor,
        WaitTimeout timeout,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(timeout);

        return descriptor switch
        {
            WaitOnTopic onTopic => WaitOnTopicAsync(onTopic, timeout, ct),
            WaitOnMailbox onMailbox => WaitOnMailboxAsync(onMailbox, timeout, ct),
            WaitOnReply onReply => WaitOnReplyAsync(onReply, timeout, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor), descriptor.GetType().Name, "Unknown WaitDescriptor variant.")
        };
    }

    private async Task<Message> WaitOnTopicAsync(WaitOnTopic descriptor, WaitTimeout timeout, CancellationToken ct)
    {
        var subscriberCrewId = _callerContext.Current.CrewId;
        var effectiveScope = CrewId.IsSystem(subscriberCrewId) ? null : subscriberCrewId;
        var startedAt = DateTimeOffset.UtcNow;
        var waitId = Ulid.NewUlid().ToString();

        var channel = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        var subscriberId = Guid.NewGuid();
        var subscriber = new Subscriber(channel, effectiveScope);
        var bucket = _subscribers.GetOrAdd(descriptor.Topic, _ => new ConcurrentDictionary<Guid, Subscriber>());
        bucket[subscriberId] = subscriber;

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeout is FiniteWaitTimeout finite)
                linked.CancelAfter(finite.Duration);

            try
            {
                while (await channel.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out var msg))
                    {
                        if (!MetadataMatches(msg, descriptor.MetadataMatch))
                            continue;

                        var delivered = await DeliverAsync(msg, linked.Token).ConfigureAwait(false);
                        if (delivered is not null)
                            return delivered;
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout is FiniteWaitTimeout)
            {
                return WaitTimedOutMessageFactory.Create(
                    originalTopic: descriptor.Topic,
                    originalWaitId: waitId,
                    originalStartedAt: startedAt,
                    targetCrewId: effectiveScope);
            }

            throw new OperationCanceledException(ct);
        }
        finally
        {
            bucket.TryRemove(subscriberId, out _);
            channel.Writer.TryComplete();
        }
    }

    private async Task<Message> WaitOnMailboxAsync(WaitOnMailbox descriptor, WaitTimeout timeout, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var waitId = Ulid.NewUlid().ToString();

        // Auto-register the mailbox so the waiter can receive messages even if no one explicitly
        // pre-registered it. The waiter ref is released in `finally`.
        var entry = _mailboxes.GetOrAdd(descriptor.Address.Raw, _ => new MailboxEntry(descriptor.Address));
        entry.IncrementRef();

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeout is FiniteWaitTimeout finite)
                linked.CancelAfter(finite.Duration);

            try
            {
                while (await entry.Channel.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false))
                {
                    if (entry.Channel.Reader.TryRead(out var msg))
                    {
                        var delivered = await DeliverAsync(msg, linked.Token).ConfigureAwait(false);
                        if (delivered is not null)
                            return delivered;
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout is FiniteWaitTimeout)
            {
                return WaitTimedOutMessageFactory.Create(
                    originalTopic: descriptor.Address.Raw,
                    originalWaitId: waitId,
                    originalStartedAt: startedAt,
                    targetCrewId: descriptor.Address.CrewId);
            }

            throw new OperationCanceledException(ct);
        }
        finally
        {
            UnregisterMailbox(descriptor.Address.Raw);
        }
    }

    private async Task<Message> WaitOnReplyAsync(WaitOnReply descriptor, WaitTimeout timeout, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var waitId = Ulid.NewUlid().ToString();
        var correlationKey = descriptor.Correlation.AsString();

        var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_replyWaiters.TryAdd(correlationKey, tcs))
            throw new InvalidOperationException(
                $"A WaitForAsync(OnReply) is already pending for correlation '{correlationKey}'.");

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeout is FiniteWaitTimeout finite)
                linked.CancelAfter(finite.Duration);

            await using var registration = linked.Token.Register(static state =>
            {
                var captured = (TaskCompletionSource<Message>)state!;
                captured.TrySetCanceled();
            }, tcs).ConfigureAwait(false);

            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout is FiniteWaitTimeout)
            {
                return WaitTimedOutMessageFactory.Create(
                    originalTopic: $"_correlation/{correlationKey}",
                    originalWaitId: waitId,
                    originalStartedAt: startedAt,
                    targetCrewId: null);
            }
        }
        finally
        {
            _replyWaiters.TryRemove(correlationKey, out _);
        }
    }

    // ── GetLastValueAsync ───────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<Message?> GetLastValueAsync(string key, CrewId? crewScope, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        var scopeKey = crewScope is null ? "" : crewScope.ToString()!;
        return Task.FromResult(_lastValues.TryGetValue((scopeKey, key), out var msg) ? msg : null);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Groups the parameters required to construct a <see cref="Message"/>.
    /// </summary>
    private sealed record MessageBuildContext
    {
        public required string Topic { get; init; }
        public required CrewId SourceCrewId { get; init; }
        public AgentId? SourceAgentId { get; init; }
        public CrewId? TargetCrewId { get; init; }
        public MailboxAddress? TargetMailbox { get; init; }
        public CorrelationId? CorrelationId { get; init; }
        public required object Payload { get; init; }
        public ImmutableDictionary<string, string>? Metadata { get; init; }
        public required string SchemaId { get; init; }
    }

    private static Message BuildMessage(MessageBuildContext context)
    {
        return new Message
        {
            Id = MessageId.NewId(),
            Topic = context.Topic,
            SourceCrewId = context.SourceCrewId,
            SourceAgentId = context.SourceAgentId,
            TargetCrewId = context.TargetCrewId,
            TargetMailbox = context.TargetMailbox,
            CorrelationId = context.CorrelationId,
            PublishedAt = DateTimeOffset.UtcNow,
            Metadata = context.Metadata ?? ImmutableDictionary<string, string>.Empty,
            Payload = SerializePayload(context.Payload),
            SchemaId = context.SchemaId
        };
    }

    private static ReadOnlyMemory<byte> SerializePayload(object? payload)
    {
        if (payload is null) return ReadOnlyMemory<byte>.Empty;
        if (payload is ReadOnlyMemory<byte> rom) return rom;
        if (payload is byte[] bytes) return bytes;
        return JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), s_jsonOptions);
    }

    /// <summary>Deserializes the binary payload of a message into a typed value.</summary>
    public static T? DeserializePayload<T>(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty) return default;
        if (typeof(T) == typeof(ReadOnlyMemory<byte>)) return (T)(object)payload;
        if (typeof(T) == typeof(byte[])) return (T)(object)payload.ToArray();
        return JsonSerializer.Deserialize<T>(payload.Span, s_jsonOptions);
    }

    private static bool MetadataMatches(Message message, ImmutableDictionary<string, string>? required)
    {
        if (required is null || required.Count == 0) return true;
        foreach (var (k, v) in required)
        {
            if (!message.Metadata.TryGetValue(k, out var actual) || actual != v)
                return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var bucket in _subscribers.Values)
        {
            foreach (var sub in bucket.Values)
                sub.Channel.Writer.TryComplete();
        }
        _subscribers.Clear();

        foreach (var entry in _mailboxes.Values)
            entry.Channel.Writer.TryComplete();
        _mailboxes.Clear();

        foreach (var tcs in _pendingReplies.Values)
            tcs.TrySetCanceled();
        _pendingReplies.Clear();

        foreach (var tcs in _replyWaiters.Values)
            tcs.TrySetCanceled();
        _replyWaiters.Clear();
    }

    private sealed record Subscriber(Channel<Message> Channel, CrewId? SubscriberCrewId);

    private sealed class MailboxEntry
    {
        public MailboxEntry(MailboxAddress address) => Address = address;

        public MailboxAddress Address { get; }
        public Channel<Message> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<Message>(
            new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        private int _refCount;
        public void IncrementRef() => Interlocked.Increment(ref _refCount);
        public int DecrementRef() => Interlocked.Decrement(ref _refCount);
    }

    private sealed class MailboxRegistration(InMemoryEventHub hub, string rawAddress) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                hub.UnregisterMailbox(rawAddress);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "EventHub published topic={Topic} id={MessageId} scope={Scope}")]
    private partial void LogPublished(string topic, MessageId messageId, string scope);

    [LoggerMessage(Level = LogLevel.Debug, Message = "EventHub posted to mailbox={Mailbox} id={MessageId}")]
    private partial void LogPosted(string mailbox, MessageId messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "EventHub dropped an already-delivered message topic={Topic} id={MessageId}")]
    private partial void LogDuplicateDropped(string topic, MessageId messageId);
}
