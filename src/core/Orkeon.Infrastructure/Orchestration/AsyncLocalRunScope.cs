using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Infrastructure.Orchestration;

/// <summary>
/// Default <see cref="IRunScope"/>, backed by <see cref="AsyncLocal{T}"/>: a run sees its own
/// identity, never the identity of a run on another control flow. The slot is static so that every
/// instance (the orchestrator's and an observer's, built apart) reads the same ambient run.
/// <para>
/// A run started inside another (a crew kicked off from a task, a nested kickoff) enters its own
/// scope: for the length of the inner run <see cref="Current"/> is the inner one, and the outer one
/// is back when it ends. A reader inside the inner run never sees the outer run's identifier.
/// </para>
/// </summary>
public sealed class AsyncLocalRunScope : IRunScope
{
    private static readonly AsyncLocal<RunScope?> Slot = new();

    /// <inheritdoc />
    public RunScope? Current => Slot.Value;

    /// <inheritdoc />
    public IDisposable Enter(RunScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var previous = Slot.Value;
        Slot.Value = scope;
        return new Pop(previous);
    }

    private sealed class Pop(RunScope? previous) : IDisposable
    {
        public void Dispose() => Slot.Value = previous;
    }
}
