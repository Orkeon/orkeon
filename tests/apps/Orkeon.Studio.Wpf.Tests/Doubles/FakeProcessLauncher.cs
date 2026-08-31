using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>
/// An <see cref="IProcessLauncher"/> that replays a scripted result instead of spawning anything,
/// recording what it was asked to run.
/// </summary>
public sealed class FakeProcessLauncher : IProcessLauncher
{
    public List<ProcessLaunchRequest> Requests { get; } = [];

    public List<ProcessOutputLine> OutputToEmit { get; } = [];

    public int ExitCode { get; set; }

    public bool HonourCancellation { get; set; }

    public ProcessLaunchRequest? LastRequest => Requests.Count > 0 ? Requests[^1] : null;

    /// <summary>What the caller wrote to stdin, when the request wired <c>OnInputReady</c>.</summary>
    public List<string> InputLines { get; } = [];

    /// <summary>
    /// Called after the input writer was handed over and before the scripted output plays —
    /// the window a test uses to write stdin while the "child" is still alive.
    /// </summary>
    public Action? WhileRunning { get; set; }

    /// <summary>
    /// Called with each line the caller writes to stdin, while the "child" is alive.
    /// <para>
    /// The forge brief stage is a conversation, not a script: ForgeStages emits an
    /// assistant.message, BLOCKS on stdin, and only then emits the next one. Replaying a
    /// fixed list of output cannot express that — a test needs to answer the answer. Pair
    /// this with <see cref="Emit"/> to script the real loop.
    /// </para>
    /// </summary>
    public Action<string>? OnInputLine { get; set; }

    /// <summary>
    /// Pushes one line into the live output stream, mid-run. Only meaningful from inside
    /// <see cref="WhileRunning"/> or <see cref="OnInputLine"/>; outside a run there is no
    /// listener and the line is dropped, which is exactly what a dead child would do.
    /// </summary>
    public void Emit(ProcessOutputLine line) => _onOutput?.Invoke(line);

    private Action<ProcessOutputLine>? _onOutput;

    public Task<ProcessRunResult> RunAsync(
        ProcessLaunchRequest request,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        _onOutput = onOutput;
        try
        {
            request.OnInputReady?.Invoke(new RecordingInputWriter(this));
            WhileRunning?.Invoke();

            foreach (var line in OutputToEmit)
                onOutput?.Invoke(line);
        }
        finally
        {
            // The child is gone: a later Emit must fall on the floor rather than reach a
            // conversation that has moved on.
            _onOutput = null;
        }

        if (HonourCancellation && cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(ProcessRunResult.FromCancellation(
                rawExitCode: -1,
                ProcessTerminationOutcome.Of(ProcessTerminationMode.StoppedBySignal),
                TimeSpan.Zero));
        }

        return Task.FromResult(ProcessRunResult.FromExitCode(ExitCode, TimeSpan.Zero));
    }

    private sealed class RecordingInputWriter(FakeProcessLauncher owner) : IProcessInputWriter
    {
        public bool TryWriteLine(string line)
        {
            owner.InputLines.Add(line);
            owner.OnInputLine?.Invoke(line);
            return true;
        }

        public void Close()
        {
        }
    }
}
