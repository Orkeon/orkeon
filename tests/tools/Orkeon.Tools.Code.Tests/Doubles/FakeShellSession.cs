using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests.Doubles;

/// <summary>
/// A hand-written <see cref="IShellSession"/>: records the commands it runs and whether it was
/// disposed, and answers with <see cref="Results"/> (then <see cref="DefaultResult"/>).
/// </summary>
internal sealed class FakeShellSession : IShellSession
{
    private int _running;

    /// <summary>The order in which the sandbox opened this session (1 for the first).</summary>
    public int Number { get; init; }

    /// <summary>The answers of the next commands, in order.</summary>
    public Queue<ShellRunResult> Results { get; } = new();

    /// <summary>The answer once <see cref="Results"/> is empty.</summary>
    public ShellRunResult DefaultResult { get; set; } = new(0, "ok", "");

    /// <summary>Thrown by the next command instead of answering, when set.</summary>
    public Exception? ThrowOnRun { get; set; }

    /// <summary>When set, each command waits for it (or for its cancellation) before answering.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Completed when a command starts running.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The commands run, in order.</summary>
    public List<ShellInvocation> Invocations { get; } = [];

    /// <summary>The most commands seen running at once.</summary>
    public int MaxConcurrency { get; private set; }

    /// <summary>How many times the session was disposed.</summary>
    public int DisposeCount { get; private set; }

    /// <summary>Whether the session was disposed.</summary>
    public bool Disposed => DisposeCount > 0;

    public async Task<ShellRunResult> RunAsync(ShellInvocation invocation, CancellationToken cancellationToken)
    {
        var running = Interlocked.Increment(ref _running);
        try
        {
            lock (Invocations)
            {
                MaxConcurrency = Math.Max(MaxConcurrency, running);
                Invocations.Add(invocation);
            }

            Started.TrySetResult();
            if (Gate is { } gate)
                await gate.Task.WaitAsync(cancellationToken);

            if (ThrowOnRun is { } exception)
                throw exception;

            lock (Invocations)
                return Results.Count > 0 ? Results.Dequeue() : DefaultResult;
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}
