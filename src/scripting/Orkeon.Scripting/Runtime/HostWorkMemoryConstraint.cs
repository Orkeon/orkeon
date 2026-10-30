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
/// </remarks>
internal sealed class HostWorkMemoryConstraint : Constraint
{
    private readonly long _limit;
    private long _windowStart;
    private long _excluded;
    private int _window;
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
        _thread = Environment.CurrentManagedThreadId;
        var usage = GC.GetAllocatedBytesForCurrentThread() - _windowStart - _excluded;
        if (usage > _limit)
            throw new MemoryLimitExceededException($"Script has allocated {usage} but is limited to {_limit}");
    }

    /// <inheritdoc />
    public override void Reset()
    {
        _thread = Environment.CurrentManagedThreadId;
        _windowStart = GC.GetAllocatedBytesForCurrentThread();
        _excluded = 0;
        _window++;
    }

    /// <summary>
    /// Leaves out of the count what the calling thread allocates until the returned scope is disposed, when
    /// that thread is the engine's.
    /// </summary>
    internal HostWork ExcludeHostWork()
    {
        if (Environment.CurrentManagedThreadId != _thread || _depth++ > 0)
            return new HostWork(this, -1, _window);
        return new HostWork(this, GC.GetAllocatedBytesForCurrentThread(), _window);
    }

    /// <summary>The scope <paramref name="memory"/> opens, or an empty one for an engine without the constraint.</summary>
    internal static HostWork Exclude(HostWorkMemoryConstraint? memory) =>
        memory?.ExcludeHostWork() ?? default;

    private void End(long start, int window)
    {
        if (Environment.CurrentManagedThreadId != _thread)
            return;
        if (start < 0)
        {
            if (_depth > 0) _depth--;
            return;
        }
        _depth = 0;
        if (window == _window)
            _excluded += GC.GetAllocatedBytesForCurrentThread() - start;
    }

    /// <summary>One <see cref="ExcludeHostWork"/> scope; <c>default</c> excludes nothing.</summary>
    internal readonly struct HostWork : IDisposable
    {
        private readonly HostWorkMemoryConstraint? _owner;
        private readonly long _start;
        private readonly int _window;

        internal HostWork(HostWorkMemoryConstraint owner, long start, int window)
        {
            _owner = owner;
            _start = start;
            _window = window;
        }

        public void Dispose() => _owner?.End(_start, _window);
    }
}
