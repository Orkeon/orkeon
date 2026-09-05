using System.Collections.Concurrent;
using System.Text.Json;
using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Domain.Common;
using Orkeon.Scripting.Cli.Events;

namespace Orkeon.Scripting.Cli.Commands.Run;

/// <summary>The inbound verbs the bridge understands. Each one maps onto a member of <see cref="IEventHub"/>.</summary>
internal static class HubCommandKinds
{
    public const string Post = "post";
    public const string Send = "send";
    public const string Publish = "publish";
    public const string Reply = "reply";
    public const string Subscribe = "subscribe";
    public const string Unsubscribe = "unsubscribe";

    /// <summary>The outbound kind carrying anything the external peer asked to hear.</summary>
    public const string HubMessage = Orkeon.Constants.Protocol.RunEventKinds.HubMessage;
}

/// <summary>
/// Gives a process outside the run a seat at the hub (BUS-05).
/// <para>
/// A **decorator**, never a replacement: <c>InMemoryEventHub</c> keeps carrying local traffic
/// untouched and untested-against. The bridge only handles what concerns the external peer —
/// it serves the peer's <c>client://{name}</c> mailbox so agents can write to it, projects the
/// peer's own commands onto the hub, and relays what the peer asked to listen to. A run without
/// <c>--events</c> never constructs one.
/// </para>
/// <para>
/// The peer speaks the agents' own grammar rather than a vocabulary invented for it:
/// <c>post</c>, <c>send</c>, <c>publish</c>, <c>reply</c>, <c>subscribe</c> map one for one.
/// Malformed or unknown lines are ignored — the outbound stream is the contract, the inbound
/// one is tolerant.
/// </para>
/// <para>
/// Who may reach the peer is not this class's decision: the ACL stage answers it, and the peer
/// is named in a crew's <c>links:</c> block as <c>client:{name}</c> like any other
/// correspondent. The bridge opens a door; HUB-03 put the guard in front of it.
/// </para>
/// </summary>
internal sealed class JsonLinesEventHubBridge : IEventHub, IAsyncDisposable
{
    private readonly IEventHub _inner;
    private readonly OrkeonEventWriter _events;
    private readonly string _clientName;
    private readonly MailboxAddress _clientMailbox;
    private readonly IEventHubCallerContext? _callerContext;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingFromPeer = new();
    private readonly ConcurrentDictionary<string, RelaySubscription> _relays = new(StringComparer.Ordinal);

    private sealed record RelaySubscription(CancellationTokenSource Cancellation)
    {
        public Task? Pump { get; set; }
    }

    /// <summary>Builds the bridge around the local hub and the outbound stream.</summary>
    /// <param name="inner">The hub carrying local traffic. Untouched for everything not aimed at the peer.</param>
    /// <param name="events">The outbound stream the peer reads.</param>
    /// <param name="clientName">The peer's name, as it appears in <c>client://{name}</c>.</param>
    /// <param name="callerContext">
    /// The ambient hub identity, when the host exposes one. It is what lets a relayed line say
    /// *who* posted — without it the peer knows only that somebody did.
    /// </param>
    public JsonLinesEventHubBridge(
        IEventHub inner, OrkeonEventWriter events, string clientName, IEventHubCallerContext? callerContext = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        _clientName = clientName;
        _clientMailbox = MailboxAddress.Parse(new Uri($"client://{clientName}"));
        _callerContext = callerContext;
    }

    /// <summary>The address agents use to reach the external peer.</summary>
    public MailboxAddress ClientMailbox => _clientMailbox;

    private bool IsPeer(MailboxAddress address) =>
        address.Kind == MailboxKind.Client
        && string.Equals(address.ClientName, _clientName, StringComparison.Ordinal);

    // ── Outbound: agents writing to the peer ────────────────────────────

    /// <inheritdoc />
    public System.Threading.Tasks.Task PostAsync(MailboxAddress recipient, object payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(recipient);

        if (!IsPeer(recipient))
            return _inner.PostAsync(recipient, payload, ct);

        Relay(topic: null, payload: payload, correlationId: null, from: DescribeAmbientCaller());
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TResponse> SendAsync<TRequest, TResponse>(
        MailboxAddress recipient, TRequest request, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(recipient);

        return IsPeer(recipient)
            ? AskPeerAsync<TResponse>(request, timeout, ct)
            : _inner.SendAsync<TRequest, TResponse>(recipient, request, timeout, ct);
    }

    private async Task<TResponse> AskPeerAsync<TResponse>(object? request, TimeSpan timeout, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var pending = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingFromPeer[correlationId] = pending;

        try
        {
            Relay(topic: null, payload: request, correlationId: correlationId, from: DescribeAmbientCaller(), expectsReply: true);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);

            await using var registration = linked.Token.Register(
                static state => ((TaskCompletionSource<JsonElement>)state!).TrySetCanceled(),
                pending).ConfigureAwait(false);

            JsonElement answer;
            try
            {
                answer = await pending.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The peer is a separate process that may simply not be listening. A timeout
                // says so in the hub's own vocabulary rather than inventing an error kind.
                throw new SendTimeoutException(correlationId, timeout);
            }

            return answer.Deserialize<TResponse>(SerializerOptions)!;
        }
        finally
        {
            _pendingFromPeer.TryRemove(correlationId, out _);
        }
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task ReplyAsync(CorrelationId correlation, object payload, CancellationToken ct) =>
        _inner.ReplyAsync(correlation, payload, ct);

    /// <inheritdoc />
    public System.Threading.Tasks.Task PublishAsync(
        string topic, object payload, PublishOptions? options, CancellationToken ct) =>
        _inner.PublishAsync(topic, payload, options, ct);

    /// <inheritdoc />
    public IAsyncEnumerable<Message> SubscribeAsync(string topic, CancellationToken ct) =>
        _inner.SubscribeAsync(topic, ct);

    /// <inheritdoc />
    public Task<Message> WaitForAsync(WaitDescriptor descriptor, WaitTimeout timeout, CancellationToken ct) =>
        _inner.WaitForAsync(descriptor, timeout, ct);

    /// <inheritdoc />
    public Task<Message?> GetLastValueAsync(string key, CrewId? crewScope, CancellationToken ct) =>
        _inner.GetLastValueAsync(key, crewScope, ct);

    // ── Inbound: the peer speaking to the agents ────────────────────────

    /// <summary>
    /// Projects one inbound line onto the hub. Unknown verbs and malformed payloads are
    /// ignored rather than raised: a client that sends nonsense should not stop a run.
    /// </summary>
    public async Task HandleCommandAsync(string line, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(line);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("kind", out var kindElement)
            || kindElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        switch (kindElement.GetString())
        {
            case HubCommandKinds.Post:
                await PostFromPeerAsync(root, ct).ConfigureAwait(false);
                break;
            case HubCommandKinds.Send:
                await SendFromPeerAsync(root, ct).ConfigureAwait(false);
                break;
            case HubCommandKinds.Publish:
                await PublishFromPeerAsync(root, ct).ConfigureAwait(false);
                break;
            case HubCommandKinds.Reply:
                ReplyFromPeer(root);
                break;
            case HubCommandKinds.Subscribe:
                StartRelay(root, ct);
                break;
            case HubCommandKinds.Unsubscribe:
                StopRelay(root);
                break;
            default:
                break;   // an unknown verb is not an error, it is a client we do not serve
        }
    }

    private async Task PostFromPeerAsync(JsonElement root, CancellationToken ct)
    {
        if (!TryReadAddress(root, out var to) || !root.TryGetProperty("payload", out var payload))
            return;

        await _inner.PostAsync(to!, payload.Clone(), ct).ConfigureAwait(false);
    }

    private async Task SendFromPeerAsync(JsonElement root, CancellationToken ct)
    {
        if (!TryReadAddress(root, out var to) || !root.TryGetProperty("payload", out var payload))
            return;

        var timeout = root.TryGetProperty("timeoutMs", out var ms) && ms.TryGetInt32(out var value) && value > 0
            ? TimeSpan.FromMilliseconds(value)
            : TimeSpan.FromSeconds(30);

        var correlationId = root.TryGetProperty("correlationId", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;

        // The answer comes back as a hub.message carrying the peer's own correlation id, so it
        // can pair the two without the bridge inventing an id the peer never saw.
        var answer = await _inner
            .SendAsync<JsonElement, JsonElement>(to!, payload.Clone(), timeout, ct)
            .ConfigureAwait(false);

        // The answer pairs with the peer's own correlation id; the responder's identity is
        // not carried by SendAsync's return, so `from` stays absent rather than guessed.
        Relay(topic: null, payload: answer, correlationId: correlationId, from: null);
    }

    private async Task PublishFromPeerAsync(JsonElement root, CancellationToken ct)
    {
        if (!root.TryGetProperty("topic", out var topicElement)
            || topicElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("payload", out var payload))
        {
            return;
        }

        var topic = topicElement.GetString();
        if (string.IsNullOrWhiteSpace(topic))
            return;

        var options = root.TryGetProperty("retainAs", out var retain) && retain.ValueKind == JsonValueKind.String
            ? new PublishOptions { RetainAsLastValue = true, LastValueKey = retain.GetString() }
            : null;

        await _inner.PublishAsync(topic, payload.Clone(), options, ct).ConfigureAwait(false);
    }

    private void ReplyFromPeer(JsonElement root)
    {
        if (!root.TryGetProperty("correlationId", out var id)
            || id.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("payload", out var payload))
        {
            return;
        }

        var correlationId = id.GetString();
        if (correlationId is not null && _pendingFromPeer.TryRemove(correlationId, out var pending))
            pending.TrySetResult(payload.Clone());
    }

    private void StartRelay(JsonElement root, CancellationToken ct)
    {
        if (!root.TryGetProperty("topic", out var topicElement)
            || topicElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var topic = topicElement.GetString();
        if (string.IsNullOrWhiteSpace(topic) || _relays.ContainsKey(topic))
            return;      // subscribing twice to one topic would double every message

#pragma warning disable CA2000 // Ownership transfers to _relays; disposed by StopRelay, the pump's finally, or DisposeAsync.
        var relay = new RelaySubscription(CancellationTokenSource.CreateLinkedTokenSource(ct));
#pragma warning restore CA2000
        if (!_relays.TryAdd(topic, relay))
        {
            relay.Cancellation.Dispose();
            return;
        }

        // Open the subscription here, not inside the background task: the hub registers a
        // subscriber the moment SubscribeAsync is called, so deferring that call would leave a
        // window where the peer has asked to listen and is not yet listening.
        var stream = _inner.SubscribeAsync(topic, relay.Cancellation.Token);
        relay.Pump = Task.Run(() => RelayTopicAsync(topic, relay, stream), CancellationToken.None);
    }

    private async Task RelayTopicAsync(string topic, RelaySubscription relay, IAsyncEnumerable<Message> stream)
    {
        try
        {
            await foreach (var message in stream.ConfigureAwait(false))
                Relay(topic, ReadPayload(message), correlationId: message.CorrelationId?.AsString(), from: DescribeSource(message));
        }
        catch (OperationCanceledException)
        {
            // Unsubscribed, or the run ended.
        }
        finally
        {
            // A stream that ends on its own (the hub completed it) must free the slot, or the
            // topic could never be subscribed again — StartRelay guards on ContainsKey.
            if (_relays.TryRemove(topic, out var current) && ReferenceEquals(current, relay))
                relay.Cancellation.Dispose();
        }
    }

    private void StopRelay(JsonElement root)
    {
        if (!root.TryGetProperty("topic", out var topicElement)
            || topicElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var topic = topicElement.GetString();
        if (topic is not null && _relays.TryRemove(topic, out var relay))
        {
            relay.Cancellation.Cancel();
            relay.Cancellation.Dispose();
        }
    }

    // ── Emission ────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private void Relay(string? topic, object? payload, string? correlationId, string? from, bool expectsReply = false)
    {
        var scope = correlationId is null
            ? OrkeonEventScope.None
            : new OrkeonEventScope { CorrelationId = correlationId };

        // Null entries are omitted by the writer — the contract's "absent key is omitted"
        // holds for hub.message like for every other kind.
        var line = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["from"] = from,
            ["topic"] = topic,
            ["payload"] = payload,
        };

        // expectsReply appears only on a send: a post and a topic relay carry nothing to
        // answer, and the peer must not have to guess which correlated lines are questions,
        // so the key is written only when there is one to answer.
        if (expectsReply)
            line["expectsReply"] = true;

        _events.Emit(HubCommandKinds.HubMessage, scope, line);
    }

    /// <summary>
    /// Who a relayed message came from, in the hub's own address grammar — the peer cannot
    /// answer, or even attribute, a line that does not say.
    /// </summary>
    private static string? DescribeSource(Message message)
    {
        if (CrewId.IsSystem(message.SourceCrewId))
            return message.SourceAgentId is null ? null : $"agent://{message.SourceCrewId}/{message.SourceAgentId}";

        return message.SourceAgentId is not null
            ? $"agent://{message.SourceCrewId}/{message.SourceAgentId}"
            : $"crew://{message.SourceCrewId}";
    }

    private string? DescribeAmbientCaller()
    {
        var caller = _callerContext?.Current;
        if (caller is null || (CrewId.IsSystem(caller.CrewId) && caller.AgentId is null))
            return null;

        return caller.AgentId is not null
            ? $"agent://{caller.CrewId}/{caller.AgentId}"
            : $"crew://{caller.CrewId}";
    }

    private static JsonElement? ReadPayload(Message message)
    {
        if (message.Payload.IsEmpty)
            return null;

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(message.Payload.Span);
        }
        catch (JsonException)
        {
            // A payload the peer cannot read as JSON is reported as absent rather than as a
            // broken line: the outbound stream must stay parsable.
            return null;
        }
    }

    private static bool TryReadAddress(JsonElement root, out MailboxAddress? address)
    {
        address = null;
        if (!root.TryGetProperty("to", out var to) || to.ValueKind != JsonValueKind.String)
            return false;

        var raw = to.GetString();
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return false;

        try
        {
            address = MailboxAddress.Parse(uri);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var relays = _relays.Values.ToArray();
        _relays.Clear();

        foreach (var relay in relays)
        {
            try
            {
                await relay.Cancellation.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // The relay's own pump won the race to clean up a naturally-ended stream.
            }
        }

        // Await the pumps: a fire-and-forget relay still writing after dispose would race the
        // final run.finished line on the shared stream.
        foreach (var relay in relays)
        {
            if (relay.Pump is { } pump)
            {
                try
                {
                    await pump.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected: that is how relays stop.
                }
            }

            relay.Cancellation.Dispose();
        }

        foreach (var pending in _pendingFromPeer.Values)
            pending.TrySetCanceled();

        _pendingFromPeer.Clear();
    }
}
