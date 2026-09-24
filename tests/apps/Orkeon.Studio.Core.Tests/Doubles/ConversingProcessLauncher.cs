using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Tests.Doubles;

/// <summary>
/// An <see cref="IProcessLauncher"/> whose child answers its stdin — the shape of a session-mode
/// CLI, which <see cref="FakeProcessLauncher"/> cannot play: that one replays a fixed script,
/// while a session is a conversation. The child speaks its <see cref="Opening"/>, then answers
/// each line written to it through <see cref="Reply"/>, and lives until its stdin is closed.
/// </summary>
public sealed class ConversingProcessLauncher : IProcessLauncher
{
    /// <summary>Every request received, in call order.</summary>
    public List<ProcessLaunchRequest> Requests { get; } = [];

    /// <summary>Every line written to a child's stdin, all children together.</summary>
    public List<string> InputLines { get; } = [];

    /// <summary>How many children had their stdin closed.</summary>
    public int ClosedInputs { get; private set; }

    /// <summary>Standard-output lines each child speaks as soon as it starts.</summary>
    public List<string> Opening { get; } = [];

    /// <summary>Standard-error lines each child speaks as soon as it starts.</summary>
    public List<string> OpeningErrors { get; } = [];

    /// <summary>The standard-output lines that answer one stdin line; none when null.</summary>
    public Func<string, IEnumerable<string>>? Reply { get; set; }

    /// <summary>
    /// False plays a child that ends right after its opening — a CLI that knows no session
    /// mode, or one that refused its command line.
    /// </summary>
    public bool StaysOpen { get; set; } = true;

    /// <summary>The exit code a child ends with.</summary>
    public int ExitCode { get; set; }

    /// <summary>Ends every live child as if it had crashed, with <paramref name="exitCode"/>.</summary>
    public void Crash(int exitCode)
    {
        ExitCode = exitCode;
        foreach (var child in _live.ToList())
            child.TrySetResult();
    }

    private readonly List<TaskCompletionSource> _live = [];

    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(
        ProcessLaunchRequest request,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        var ended = new TaskCompletionSource();
        _live.Add(ended);
        request.OnInputReady?.Invoke(new AnsweringWriter(this, onOutput, ended));

        foreach (var line in Opening)
            onOutput?.Invoke(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, line));
        foreach (var line in OpeningErrors)
            onOutput?.Invoke(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, line));

        try
        {
            if (StaysOpen)
                await ended.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ProcessRunResult.FromCancellation(
                OrkeonExitCodes.Cancelled,
                ProcessTerminationOutcome.Of(ProcessTerminationMode.StoppedBySignal),
                TimeSpan.FromMilliseconds(1));
        }
        finally
        {
            // A dead child reads nothing more: its writer refuses from here on.
            ended.TrySetResult();
            _live.Remove(ended);
        }

        return ProcessRunResult.FromExitCode(ExitCode, TimeSpan.FromMilliseconds(1));
    }

    /// <summary>Answers each line on the spot, on the writer's thread — the order a real child keeps.</summary>
    private sealed class AnsweringWriter(
        ConversingProcessLauncher owner,
        Action<ProcessOutputLine>? onOutput,
        TaskCompletionSource ended) : IProcessInputWriter
    {
        public bool TryWriteLine(string line)
        {
            ArgumentNullException.ThrowIfNull(line);

            if (ended.Task.IsCompleted)
                return false;

            owner.InputLines.Add(line);
            foreach (var answer in owner.Reply?.Invoke(line) ?? [])
                onOutput?.Invoke(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, answer));

            return true;
        }

        public void Close()
        {
            if (ended.TrySetResult())
                owner.ClosedInputs++;
        }
    }
}
