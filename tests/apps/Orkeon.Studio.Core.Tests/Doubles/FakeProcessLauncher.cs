using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Tests.Doubles;

/// <summary>
/// Scripted <see cref="IProcessLauncher"/>: it records what it was asked to spawn and
/// replays a canned run. No test in this project ever needs a real <c>orkeon</c> binary —
/// that is the point of the interface.
/// </summary>
public sealed class FakeProcessLauncher : IProcessLauncher
{
    /// <summary>Every request received, in call order.</summary>
    public List<ProcessLaunchRequest> Requests { get; } = new();

    /// <summary>Exit code returned when the run is not cancelled.</summary>
    public int ExitCode { get; set; }

    /// <summary>Lines emitted before the run ends, in order.</summary>
    public List<ProcessOutputLine> ScriptedOutput { get; } = new();

    /// <summary>Pause inserted before each scripted line, to model a slow process.</summary>
    public TimeSpan LineDelay { get; set; } = TimeSpan.Zero;

    /// <summary>When true the run never ends on its own; only cancellation stops it.</summary>
    public bool RunsUntilCancelled { get; set; }

    /// <summary>How a cancelled run is reported to have ended.</summary>
    public ProcessTerminationMode CancellationTermination { get; set; } = ProcessTerminationMode.StoppedBySignal;

    /// <summary>Raw exit code reported for a cancelled run.</summary>
    public int CancellationRawExitCode { get; set; } = OrkeonExitCodes.Cancelled;

    /// <summary>Number of runs started.</summary>
    public int StartCount => Requests.Count;

    /// <summary>Adds a scripted standard-output line.</summary>
    public FakeProcessLauncher WithStandardOutput(params string[] lines)
    {
        foreach (var line in lines)
            ScriptedOutput.Add(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, line));
        return this;
    }

    /// <summary>Adds a scripted standard-error line.</summary>
    public FakeProcessLauncher WithStandardError(params string[] lines)
    {
        foreach (var line in lines)
            ScriptedOutput.Add(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, line));
        return this;
    }

    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(
        ProcessLaunchRequest request,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        try
        {
            foreach (var line in ScriptedOutput)
            {
                if (LineDelay > TimeSpan.Zero)
                    await Task.Delay(LineDelay, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                onOutput?.Invoke(line with { TimestampUtc = DateTimeOffset.UtcNow });
            }

            if (RunsUntilCancelled)
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ProcessRunResult.FromCancellation(
                CancellationRawExitCode,
                ProcessTerminationOutcome.Of(CancellationTermination),
                TimeSpan.FromMilliseconds(1));
        }

        return ProcessRunResult.FromExitCode(ExitCode, TimeSpan.FromMilliseconds(1));
    }
}
