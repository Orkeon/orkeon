using Jint;
using Jint.Runtime;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// The engine's memory limit (<see cref="Configuration.ScriptingLimitsOptions.MemoryLimitBytes"/>), measured the
/// way Jint's own <c>LimitMemory</c> constraint measures it — the bytes the engine's thread allocated since the
/// window opened at the last <see cref="Reset"/> — except that the work of a host tool the script calls is left
/// out: the bytes a tool call allocates on the engine's thread, inside <see cref="ExcludeHostWork"/>, are not
/// the script's (#247).
/// </summary>
/// <remarks>
/// The tool still runs where it ran, on the engine's thread up to its first real await, so the order of its
/// effects against the script's next statements is unchanged. A scope opened on another thread excludes
/// nothing: those bytes were never counted. Nested scopes count once, from the outermost.
/// <para>
/// The counter is per thread. When a host continuation drives the engine from another thread, the window keeps
/// what the previous thread counted up to its last reading and starts a segment on the new thread's counter;
/// Jint's own constraint subtracted one thread's counter from another's, a figure that meant nothing.
/// </para>
/// </remarks>
internal sealed class HostWorkMemoryConstraint : Constraint
{
    private readonly long _limit;
    private long _counted;
    private long _segmentStart;
    private long _lastSeen;
    private long _excluded;
    private int _segment;
    private long _openStart;
    private int _depth;
    private volatile int _thread = -1;

    /// <summary>Creates the constraint; its window opens at the engine's first <see cref="Reset"/>.</summary>
    /// <param name="limit">The bytes the script may allocate; zero or less disables the limit.</param>
    internal HostWorkMemoryConstraint(long limit)
    {
        _limit = limit;
        Reset();
    }

    /// <inheritdoc />
    /// <exception cref="MemoryLimitExceededException">The script allocated more than the limit, host work excluded.</exception>
    public override void Check()
    {
        if (_limit <= 0)
            return;
        var now = GC.GetAllocatedBytesForCurrentThread();
        var thread = Environment.CurrentManagedThreadId;
        if (thread != _thread)
        {
            _counted += _lastSeen - _segmentStart - _excluded;
            _segmentStart = now;
            _excluded = 0;
            _depth = 0;
            _segment++;
            _thread = thread;
        }
        _lastSeen = now;
        // Inside an open window the bytes allocated since it opened are not counted yet.
        var usage = _counted + (_depth > 0 ? _openStart : now) - _segmentStart - _excluded;
        if (usage > _limit)
            throw new MemoryLimitExceededException($"Script has allocated {usage} but is limited to {_limit}");
    }

    /// <inheritdoc />
    public override void Reset()
    {
        var thread = Environment.CurrentManagedThreadId;
        // A Reset inside an open scope keeps the depth and the scope; a move to another thread drops them.
        if (thread != _thread)
        {
            _depth = 0;
            _segment++;
        }
        _thread = thread;
        _segmentStart = _lastSeen = GC.GetAllocatedBytesForCurrentThread();
        _counted = 0;
        _excluded = 0;
        _openStart = _segmentStart;
    }

    /// <summary>
    /// Leaves out of the count what the calling thread allocates until the returned scope is disposed, when
    /// that thread is the engine's.
    /// </summary>
    internal HostWork ExcludeHostWork()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            return default;
        if (_depth++ == 0)
            _openStart = GC.GetAllocatedBytesForCurrentThread();
        return new HostWork(new Scope(this, _segment));
    }

    /// <summary>The scope <paramref name="memory"/> opens, or an empty one for an engine without the constraint.</summary>
    internal static HostWork Exclude(HostWorkMemoryConstraint? memory) =>
        memory?.ExcludeHostWork() ?? default;

    private void End(int segment)
    {
        if (Environment.CurrentManagedThreadId != _thread || segment != _segment || _depth == 0)
            return;
        if (--_depth > 0)
            return;
        // The excluded bytes stay inside the segment's last reading, so a move to another thread folds them out.
        _lastSeen = GC.GetAllocatedBytesForCurrentThread();
        _excluded += _lastSeen - _openStart;
    }

    internal sealed class Scope(HostWorkMemoryConstraint owner, int segment)
    {
        private bool _ended;

        internal void End()
        {
            if (_ended)
                return;
            _ended = true;
            owner.End(segment);
        }
    }

    /// <summary>One <see cref="ExcludeHostWork"/> scope; <c>default</c> excludes nothing.</summary>
    internal readonly struct HostWork : IDisposable
    {
        private readonly Scope? _scope;

        internal HostWork(Scope scope) => _scope = scope;

        public void Dispose() => _scope?.End();
    }
}
