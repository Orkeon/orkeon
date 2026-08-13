using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Config.Tests.Doubles;

/// <summary>
/// Scripted <see cref="IProcessLauncher"/>: it records what it was asked to spawn and
/// replays canned output, so the diagnostic path is exercised without a real
/// <c>orkeon</c> binary.
/// </summary>
public sealed class FakeProcessLauncher : IProcessLauncher
{
    /// <summary>Every request received, in call order.</summary>
    public List<ProcessLaunchRequest> Requests { get; } = new();

    /// <summary>Exit code the scripted run ends with.</summary>
    public int ExitCode { get; set; }

    /// <summary>Standard-output lines emitted before the run ends.</summary>
    public List<string> StandardOutput { get; } = new();

    /// <summary>Adds standard-output lines.</summary>
    public FakeProcessLauncher WithStandardOutput(params string[] lines)
    {
        StandardOutput.AddRange(lines);
        return this;
    }

    public Task<ProcessRunResult> RunAsync(
        ProcessLaunchRequest request,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        foreach (var line in StandardOutput)
            onOutput?.Invoke(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, line));

        return Task.FromResult(ProcessRunResult.FromExitCode(ExitCode, TimeSpan.FromMilliseconds(1)));
    }
}
