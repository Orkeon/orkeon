using System.Collections.Concurrent;

namespace Orkeon.Host.Gateway;

/// <summary>
/// The routing strategy the host ships: one conversation, one run (spec §3, decision G2), and
/// the crew chosen by the room the conversation was opened in (GAP-11).
/// <para>
/// A Discord thread already means "this topic, from here to the end" to the people using it, so
/// making it mean one run costs no explanation. Any richer mapping — a session spanning
/// threads, a run spanning conversations — needs a concept the user has to learn first.
/// </para>
/// </summary>
internal sealed class ThreadIsRunRouter : IConversationRouter
{
    // A claimed-but-not-yet-started conversation holds this marker: the claim must exist
    // before the run id does, or the gap between them is exactly the race TryBegin closes.
    private const string PendingRun = "";

    private readonly ConcurrentDictionary<string, string> _runs = new(StringComparer.Ordinal);
    private readonly ChatRoutes _routes;

    /// <summary>Routes every conversation to <paramref name="defaultCrew"/>.</summary>
    public ThreadIsRunRouter(string defaultCrew)
        : this(new ChatRoutes(defaultCrew))
    {
    }

    /// <summary>
    /// Routes each conversation by the room it was opened in (<see cref="ChatRoutes"/>); the
    /// thread is still the run, whichever crew the room picks.
    /// </summary>
    public ThreadIsRunRouter(ChatRoutes routes)
        => _routes = routes ?? throw new ArgumentNullException(nameof(routes));

    /// <inheritdoc />
    public string ResolveCrew(InboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _routes.Resolve(message);
    }

    /// <inheritdoc />
    public string? FindRun(string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        return _runs.TryGetValue(conversationId, out var runId) && runId.Length > 0 ? runId : null;
    }

    /// <inheritdoc />
    public bool TryBegin(string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        return _runs.TryAdd(conversationId, PendingRun);
    }

    /// <inheritdoc />
    public void Attach(string conversationId, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        _runs[conversationId] = runId;
    }

    /// <inheritdoc />
    public void Detach(string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        _runs.TryRemove(conversationId, out _);
    }
}
