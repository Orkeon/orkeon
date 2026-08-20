using System.Collections.Concurrent;

namespace Orkeon.Host.Gateway;

/// <summary>
/// The routing strategy rc.2 ships: one conversation, one run (spec §3, decision G2).
/// <para>
/// A Discord thread already means "this topic, from here to the end" to the people using it, so
/// making it mean one run costs no explanation. Any richer mapping — a session spanning
/// threads, a run spanning conversations — needs a concept the user has to learn first.
/// </para>
/// </summary>
internal sealed class ThreadIsRunRouter : IConversationRouter
{
    private readonly ConcurrentDictionary<string, string> _runs = new(StringComparer.Ordinal);
    private readonly string _defaultCrew;

    /// <summary>Routes every conversation to <paramref name="defaultCrew"/>.</summary>
    /// <remarks>
    /// rc.2 hosts one crew, so the router does not need to choose. The seam is here rather than
    /// inlined so that hosting several later changes this class and nothing else.
    /// </remarks>
    public ThreadIsRunRouter(string defaultCrew)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCrew);
        _defaultCrew = defaultCrew;
    }

    /// <inheritdoc />
    public string ResolveCrew(InboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _defaultCrew;
    }

    /// <inheritdoc />
    public string? FindRun(string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        return _runs.TryGetValue(conversationId, out var runId) ? runId : null;
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
