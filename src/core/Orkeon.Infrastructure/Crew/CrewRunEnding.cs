namespace Orkeon.Infrastructure.Crew;

/// <summary>
/// Whether the crew run in progress has told its execution hook how it ended (GAP-32, decision 4.3).
/// The orchestrator opens one per run; <see cref="CrewHookDispatcher"/> marks it when it reports the
/// crew completed or failed. A run whose strategy never reported its end — one that failed before
/// it, on a memory that cannot work, a plan whose provider failed, a crew the repository does not
/// hold — is then reported by the orchestrator, once: the hook hears every failed run exactly once.
/// </summary>
/// <remarks>
/// Backed by <see cref="AsyncLocal{T}"/> holding a mutable mark, so a report made from a task's own
/// flow (a parallel wave, an <c>asyncExecution</c> task) reaches the run that opened it; a crew run
/// from inside a task opens its own, and its end is never taken for its caller's.
/// </remarks>
internal static class CrewRunEnding
{
    private static readonly AsyncLocal<Mark?> Ambient = new();

    /// <summary>What a run has reported of its end so far.</summary>
    internal sealed class Mark
    {
        private int _reported;
        private int _failureReported;

        /// <summary>Whether the hook heard the crew complete or fail.</summary>
        public bool Reported => Volatile.Read(ref _reported) != 0;

        /// <summary>Whether the hook heard the crew fail — and a streamed run's <c>error</c> went out.</summary>
        public bool FailureReported => Volatile.Read(ref _failureReported) != 0;

        internal void Set(bool failed)
        {
            Interlocked.Exchange(ref _reported, 1);
            if (failed)
                Interlocked.Exchange(ref _failureReported, 1);
        }
    }

    /// <summary>Opens the end-of-run mark of a run; disposing the handle restores the enclosing one.</summary>
    internal static IDisposable Begin(out Mark mark)
    {
        var enclosing = Ambient.Value;
        mark = new Mark();
        Ambient.Value = mark;
        return new Scope(enclosing);
    }

    /// <summary>Records that the run in progress reported its end; nothing outside a run.</summary>
    internal static void Record(bool failed) => Ambient.Value?.Set(failed);

    private sealed class Scope(Mark? enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}
