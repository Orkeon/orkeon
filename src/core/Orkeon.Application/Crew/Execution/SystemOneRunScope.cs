using System.Collections.Concurrent;
using Orkeon.Domain.Crew;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// What a run of a crew holds of System One beside its picks (#429, #434): the crew's decision mode, which the
/// agent loops read where they build a task's compaction settings, and the warnings said once per run (the
/// decision circuit off for an agent's skills, its route or the pruning of its read-tool results, an empty skills
/// library, a route without a ladder).
/// </summary>
/// <remarks>
/// Ambient, like <see cref="TaskPickScope"/>: backed by <see cref="AsyncLocal{T}"/>, opened by each kickoff, it
/// follows the run into its parallel waves and ballots, in every host; a crew run from inside a task opens its
/// own. Outside a run (a task executed on its own), no mode applies and each consumer keeps its own warnings.
/// </remarks>
public static class SystemOneRunScope
{
    private static readonly AsyncLocal<SystemOneRun?> Ambient = new();

    /// <summary>Opens the scope of a run; disposing the handle restores the enclosing one.</summary>
    /// <param name="mode">The crew's decision mode.</param>
    /// <returns>The handle that restores the enclosing scope.</returns>
    public static IDisposable Begin(CrewDecisionMode mode)
    {
        var enclosing = Ambient.Value;
        Ambient.Value = new SystemOneRun(mode);
        return new Scope(enclosing);
    }

    /// <summary>The run in progress; null outside a run.</summary>
    internal static SystemOneRun? Current => Ambient.Value;

    private sealed class Scope(SystemOneRun? enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}

/// <summary>One run of a crew, as System One's consumers read it.</summary>
internal sealed class SystemOneRun(CrewDecisionMode mode)
{
    /// <summary>The crew's decision mode.</summary>
    internal CrewDecisionMode Mode { get; } = mode;

    /// <summary>The warnings said in this run.</summary>
    internal RunWarnings Warnings { get; } = new();
}

/// <summary>Warnings said once: in a run, or by one consumer outside a run.</summary>
internal sealed class RunWarnings
{
    private readonly ConcurrentDictionary<string, byte> _said = new(StringComparer.Ordinal);

    /// <summary>The warnings of the run in progress, else <paramref name="own"/>, those of a consumer outside a run.</summary>
    /// <param name="own">The consumer's own warnings.</param>
    internal static RunWarnings Of(RunWarnings own) => SystemOneRunScope.Current?.Warnings ?? own;

    /// <summary>True the first time <paramref name="warning"/> is asked for here, false after.</summary>
    /// <param name="warning">The warning's name.</param>
    internal bool First(string warning) => _said.TryAdd(warning, 0);
}
