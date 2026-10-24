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

    /// <summary>When set, the handles of the jobs started expose it as their live tail.</summary>
    public ShellLiveTail? LiveTailForJobs { get; set; }

    /// <summary>The detached jobs started, in order.</summary>
    public List<FakeDetachedJob> Jobs { get; } = [];

    /// <summary>When set, a job started opens its log through the opener it was handed and writes this text.</summary>
    public string? PumpedLogText { get; set; }

    public async Task<ShellJobHandle> StartDetachedAsync(
        ShellInvocation invocation, string physicalLogPath, Func<CancellationToken, Task<Stream>> openLogAsync,
        CancellationToken cancellationToken)
    {
        var job = new FakeDetachedJob(invocation, physicalLogPath) { LiveTailText = LiveTailForJobs, LogOpener = openLogAsync };
        if (PumpedLogText is { } text)
        {
            var stream = await openLogAsync(cancellationToken);
            await using (stream)
            await using (var writer = new StreamWriter(stream, leaveOpen: true))
                await writer.WriteAsync(text);
        }

        lock (Jobs)
            Jobs.Add(job);
        return new ShellJobHandle(job.Id, job.ExitSource.Task, job.Kill)
        {
            LiveTail = LiveTailForJobs is null ? null : () => job.LiveTailText,
        };
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}

/// <summary>A job started by <see cref="FakeShellSession.StartDetachedAsync"/>: the test decides when it exits.</summary>
internal sealed class FakeDetachedJob(ShellInvocation invocation, string physicalLogPath)
{
    /// <summary>The id: the name of the log file the job was given.</summary>
    public string Id { get; } = Path.GetFileNameWithoutExtension(physicalLogPath);

    /// <summary>The command started.</summary>
    public ShellInvocation Invocation { get; } = invocation;

    /// <summary>The physical path the output was asked to go to.</summary>
    public string LogPath { get; } = physicalLogPath;

    /// <summary>Completed by <see cref="Exit"/>.</summary>
    public TaskCompletionSource<int> ExitSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The in-memory output the handle exposes as its live tail; null for a session that keeps none.</summary>
    public ShellLiveTail? LiveTailText { get; set; }

    /// <summary>The log opener the session was handed.</summary>
    public Func<CancellationToken, Task<Stream>>? LogOpener { get; init; }

    /// <summary>How many times the job was killed.</summary>
    public int KillCount { get; private set; }

    /// <summary>Ends the job with <paramref name="exitCode"/>.</summary>
    public void Exit(int exitCode) => ExitSource.TrySetResult(exitCode);

    /// <summary>Writes lines to the log file, as the detached process would.</summary>
    public void WriteLog(params string[] lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.WriteAllLines(LogPath, lines);
    }

    /// <summary>Records the kill and ends the job as a killed process does.</summary>
    public void Kill()
    {
        KillCount++;
        ExitSource.TrySetResult(137);
    }
}
