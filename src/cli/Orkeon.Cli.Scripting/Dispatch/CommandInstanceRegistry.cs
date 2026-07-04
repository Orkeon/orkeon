using System.Collections.Concurrent;

namespace Orkeon.Cli.Scripting.Dispatch;

/// <summary>
/// Host-side store of in-flight and recently-completed command instances (design §6, §8.2).
/// Thread-safe: background completions and engine-thread reads run concurrently.
/// </summary>
/// <remarks>
/// Retention (§6, §10 open point): <see cref="CommandInstanceState.Running"/> entries are
/// kept until they complete; terminal entries are kept up to <see cref="RetentionLimit"/>,
/// after which the oldest-completed ones are evicted so <c>result</c> can still read a
/// recent ticket without the store growing unbounded.
/// </remarks>
public sealed class CommandInstanceRegistry
{
    private readonly ConcurrentDictionary<string, CommandInstance> _instances = new(StringComparer.Ordinal);
    private long _ticketSeq;

    /// <summary>Maximum number of terminal entries kept before the oldest are evicted.</summary>
    public int RetentionLimit { get; init; } = 200;

    /// <summary>Number of instances currently tracked (running + retained terminal).</summary>
    public int Count => _instances.Count;

    /// <summary>Registers a new running instance and returns it. Allocates a fresh ticket.</summary>
    public CommandInstance Register(
        string name,
        CommandInstanceKind kind,
        string targetAgent,
        string intent,
        Guid correlationId)
    {
        var ticket = "t" + Interlocked.Increment(ref _ticketSeq);
        var instance = new CommandInstance(
            ticket, name ?? string.Empty, kind, targetAgent ?? string.Empty,
            intent ?? string.Empty, correlationId, DateTimeOffset.UtcNow)
        {
            // Evict once an instance reaches a terminal state — otherwise the most recently
            // completed entry would never trigger the budget check.
            TerminalObserver = _ => EvictIfNeeded(),
        };
        _instances[ticket] = instance;
        EvictIfNeeded();
        return instance;
    }

    /// <summary>Returns the instance for <paramref name="ticket"/>, or <see langword="null"/>.</summary>
    public CommandInstance? Get(string ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return null;
        return _instances.TryGetValue(ticket, out var instance) ? instance : null;
    }

    /// <summary>Snapshots of all instances matching <paramref name="filter"/>, newest first.</summary>
    public IReadOnlyList<CommandInstanceView> List(CommandInstanceFilter? filter = null)
    {
        filter ??= CommandInstanceFilter.All;
        return _instances.Values
            .Where(filter.Matches)
            .OrderByDescending(i => i.StartedAt)
            .Select(i => i.Snapshot())
            .ToList();
    }

    /// <summary>All tracked instances (used by the runner's completion drain).</summary>
    internal IReadOnlyCollection<CommandInstance> All() => _instances.Values.ToList();

    private void EvictIfNeeded()
    {
        // Cheap fast-path: only walk the store when it could plausibly be over budget.
        if (_instances.Count <= RetentionLimit) return;

        var terminal = _instances.Values
            .Where(i => i.State.IsTerminal())
            .Select(i => (i.Ticket, View: i.Snapshot()))
            .ToList();

        var overflow = terminal.Count - RetentionLimit;
        if (overflow <= 0) return;

        foreach (var (ticket, _) in terminal
                     .OrderBy(t => t.View.completedAt ?? t.View.startedAt, StringComparer.Ordinal)
                     .Take(overflow))
        {
            _instances.TryRemove(ticket, out _);
        }
    }
}
