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

    public Task<ProcessRunResult> RunAsync(
        ProcessLaunchRequest request,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        request.OnInputReady?.Invoke(new RecordingInputWriter(this));
        WhileRunning?.Invoke();

        foreach (var line in OutputToEmit)
            onOutput?.Invoke(line);

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
            return true;
        }

        public void Close()
        {
        }
    }
}
