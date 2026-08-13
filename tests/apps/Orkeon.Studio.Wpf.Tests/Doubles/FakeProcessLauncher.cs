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

    public Task<ProcessRunResult> RunAsync(
        ProcessLaunchRequest request,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        foreach (var line in OutputToEmit)
            onOutput?.Invoke(line);

        if (HonourCancellation && cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(ProcessRunResult.FromCancellation(
                rawExitCode: -1,
                ProcessTerminationMode.StoppedBySignal,
                TimeSpan.Zero));
        }

        return Task.FromResult(ProcessRunResult.FromExitCode(ExitCode, TimeSpan.Zero));
    }
}
