namespace Orkeon.Host.Gateway;

/// <summary>One message arriving from a channel.</summary>
internal sealed record InboundMessage
{
    /// <summary>The channel it came from — <c>discord</c>, <c>console</c>, a test.</summary>
    public required string Channel { get; init; }

    /// <summary>
    /// The conversation it belongs to. On Discord this is a thread; the routing strategy makes
    /// one conversation one run, which is the only mapping a person can predict.
    /// </summary>
    public required string ConversationId { get; init; }

    /// <summary>Who sent it, as the channel identifies them.</summary>
    public required string SenderId { get; init; }

    /// <summary>What they said.</summary>
    public required string Text { get; init; }

    /// <summary>When the channel received it, in UTC.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }
}

/// <summary>
/// Where a channel sends things back. Separated from the channel itself because the throttling
/// lives here: a progress update per agent thought would saturate any chat platform's rate
/// limit, and that is a property of *replying*, not of any one platform.
/// </summary>
internal interface IChatResponder
{
    /// <summary>
    /// Acknowledges a message immediately, before any work starts. Mandatory, not polite: a
    /// crew can take minutes and every chat platform has a response window measured in seconds.
    /// </summary>
    Task AcknowledgeAsync(InboundMessage message, string text, CancellationToken ct);

    /// <summary>
    /// Reports progress. Implementations are expected to coalesce — the caller may send far
    /// more of these than a channel should transmit.
    /// </summary>
    Task ProgressAsync(InboundMessage message, string text, CancellationToken ct);

    /// <summary>Delivers the final answer, always sent even when the run failed or was stopped.</summary>
    Task CompleteAsync(InboundMessage message, string text, CancellationToken ct);
}

/// <summary>
/// A place people talk to Orkeon from. Discord is the first; the JSONL protocol of the run
/// event bus is a legitimate implementation of the same shape, which is why this is not named
/// after chat platforms.
/// </summary>
internal interface IChatChannel
{
    /// <summary>The channel's name, as it appears in configuration and in a run's origin.</summary>
    string Name { get; }

    /// <summary>Starts listening. Returns when the channel stops.</summary>
    Task RunAsync(Func<InboundMessage, CancellationToken, Task> onMessage, CancellationToken ct);

    /// <summary>The responder replies go through.</summary>
    IChatResponder Responder { get; }
}

/// <summary>
/// Decides whether a sender may talk to Orkeon at all.
/// <para>
/// Checked **before routing**, not after: a message from an unknown sender must not reach a
/// crew, cost a token, or appear in a log as an accepted request.
/// </para>
/// </summary>
internal interface IChatAuthorizer
{
    /// <summary>Whether <paramref name="message"/>'s sender is allowed.</summary>
    bool IsAuthorized(InboundMessage message);
}

/// <summary>
/// The allow-list authorizer, and the only one rc.2 ships.
/// <para>
/// **An empty list denies everyone.** The opposite default — empty means open — is how a bot
/// invited to a public server ends up spending someone's API budget on strangers. Refusing to
/// start is louder than refusing every message, so the configuration says which it is.
/// </para>
/// </summary>
internal sealed class AllowListChatAuthorizer : IChatAuthorizer
{
    private readonly HashSet<string> _allowed;

    /// <summary>Builds the authorizer over the sender identifiers a deployment trusts.</summary>
    public AllowListChatAuthorizer(IEnumerable<string> allowedSenderIds)
    {
        ArgumentNullException.ThrowIfNull(allowedSenderIds);
        _allowed = new HashSet<string>(allowedSenderIds, StringComparer.Ordinal);
    }

    /// <summary>Whether the list names anyone at all.</summary>
    public bool IsEmpty => _allowed.Count == 0;

    /// <inheritdoc />
    public bool IsAuthorized(InboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _allowed.Contains(message.SenderId);
    }
}

/// <summary>
/// Maps a conversation onto a run.
/// <para>
/// rc.2 ships one strategy — **thread equals run** — because it is the only mapping a user can
/// predict without being told: what happens in this thread is one job. It also gives
/// parallelism for free, without inventing a notion of session nobody asked for.
/// </para>
/// </summary>
internal interface IConversationRouter
{
    /// <summary>The crew a conversation's messages go to.</summary>
    string ResolveCrew(InboundMessage message);

    /// <summary>The run identifier already serving this conversation, or null when none is.</summary>
    string? FindRun(string conversationId);

    /// <summary>Remembers that <paramref name="runId"/> is serving <paramref name="conversationId"/>.</summary>
    void Attach(string conversationId, string runId);

    /// <summary>Forgets a conversation's run, once it has finished.</summary>
    void Detach(string conversationId);
}
