using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>
/// <c>orkeon email</c>, scripted (STUDIO-69): <c>accounts</c> prints <see cref="AccountsOutput"/>,
/// <c>check</c> prints <see cref="CheckOutput"/>, each with its own exit code and standard-error
/// line. A held verb parks until <see cref="Release"/> or its token — a connection check is a
/// state on screen while it runs, which the ordinary <see cref="FakeProcessLauncher"/> cannot
/// hold. Nothing is ever spawned.
/// </summary>
public sealed class FakeEmailCli : IProcessLauncher
{
    private readonly Lock _gate = new();
    private readonly List<TaskCompletionSource<bool>> _held = [];
    private readonly List<ProcessLaunchRequest> _requests = [];
    private int _cancelled;
    private int _live;
    private TaskCompletionSource _parked = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Every launch asked for, in order.</summary>
    public IReadOnlyList<ProcessLaunchRequest> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    /// <summary>What <c>email accounts</c> writes on standard output.</summary>
    public string AccountsOutput { get; set; } = "[]";

    /// <summary>The exit code <c>email accounts</c> ends on.</summary>
    public int AccountsExitCode { get; set; }

    /// <summary>What <c>email accounts</c> writes on standard error; nothing when null.</summary>
    public string? AccountsError { get; set; }

    /// <summary>What <c>email check</c> writes on standard output.</summary>
    public string CheckOutput { get; set; } = "";

    /// <summary>The exit code <c>email check</c> ends on.</summary>
    public int CheckExitCode { get; set; }

    /// <summary>What <c>email check</c> writes on standard error; nothing when null.</summary>
    public string? CheckError { get; set; }

    /// <summary>When true a check parks instead of ending, until <see cref="Release"/> or its token.</summary>
    public bool HoldChecks { get; set; }

    /// <summary>When true a listing parks instead of ending, until <see cref="Release"/> or its token.</summary>
    public bool HoldListings { get; set; }

    /// <summary>How many <c>email accounts</c> ran.</summary>
    public int ListRuns => Requests.Count(request => request.Arguments is ["email", "accounts", ..]);

    /// <summary>How many <c>email check</c> ran.</summary>
    public int CheckRuns => Requests.Count(request => request.Arguments is ["email", "check", ..]);

    /// <summary>How many children were stopped by their token.</summary>
    public int Cancelled => Volatile.Read(ref _cancelled);

    /// <summary>How many children are still alive: started, and neither ended nor stopped.</summary>
    public int Live => Volatile.Read(ref _live);

    /// <summary>The rendezvous of a held verb: completes once a child is parked.</summary>
    public Task Parked
    {
        get
        {
            lock (_gate)
                return _parked.Task;
        }
    }

    /// <summary>Lets every parked child end on its script, and arms the next rendezvous.</summary>
    public void Release()
    {
        TaskCompletionSource<bool>[] held;
        lock (_gate)
        {
            _parked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            held = [.. _held];
        }

        foreach (var child in held)
            child.TrySetResult(true);
    }

    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(
        ProcessLaunchRequest request,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
            _requests.Add(request);

        var listing = request.Arguments is ["email", "accounts", ..];
        Interlocked.Increment(ref _live);
        try
        {
            if (listing ? HoldListings : HoldChecks)
            {
                var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_gate)
                {
                    _held.Add(gate);
                    _parked.TrySetResult();
                }

                using var stop = cancellationToken.Register(() => gate.TrySetResult(false));
                var released = await gate.Task;
                lock (_gate)
                    _held.Remove(gate);

                if (!released)
                {
                    Interlocked.Increment(ref _cancelled);
                    return ProcessRunResult.FromCancellation(
                        rawExitCode: -1,
                        ProcessTerminationOutcome.Of(ProcessTerminationMode.StoppedBySignal),
                        TimeSpan.Zero);
                }
            }

            var (output, error, exitCode) = listing
                ? (AccountsOutput, AccountsError, AccountsExitCode)
                : (CheckOutput, CheckError, CheckExitCode);

            // The verb says which file it read before anything else, on standard error.
            onOutput?.Invoke(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "Using settings: " + request.Arguments[^1]));
            if (output.Length > 0)
                onOutput?.Invoke(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, output));
            if (error is { Length: > 0 })
                onOutput?.Invoke(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, error));

            return ProcessRunResult.FromExitCode(exitCode, TimeSpan.Zero);
        }
        finally
        {
            Interlocked.Decrement(ref _live);
        }
    }
}
