using System.Text;

namespace Orkeon.Studio.Core.Process;

/// <summary>
/// Runs the co-installed <c>orkeon</c> CLI: locates the binary, spawns it, streams its
/// output and interprets its exit code. This is the single entry point the three Studio
/// front-ends use — none of them touches <see cref="System.Diagnostics.Process"/>.
/// </summary>
public sealed class OrkeonProcessRunner
{
    private readonly IProcessLauncher _launcher;
    private readonly OrkeonBinaryLocator _locator;

    /// <summary>Creates a runner over an explicit launcher and locator (the test seam).</summary>
    public OrkeonProcessRunner(IProcessLauncher launcher, OrkeonBinaryLocator locator)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
    }

    /// <summary>Creates a runner over the real machine and real processes.</summary>
    public static OrkeonProcessRunner ForCurrentMachine() =>
        new(SystemProcessLauncher.Instance, OrkeonBinaryLocator.ForCurrentMachine());

    /// <summary>Resolves the binary without running anything — for a status field in the UI.</summary>
    public BinaryLocation LocateBinary() => _locator.Locate();

    /// <summary>
    /// Runs <c>orkeon</c> with <paramref name="arguments"/>.
    /// A missing binary is returned as a <see cref="RunOutcome.NotStarted"/> result carrying
    /// the actionable message, not as an exception.
    /// </summary>
    /// <param name="arguments">The argv, after the executable name.</param>
    /// <param name="workingDirectory">Directory to run in, or null for the current one.</param>
    /// <param name="onOutput">Receives each output line as it arrives.</param>
    /// <param name="gracePeriod">How long a cancelled child gets to exit on its own.</param>
    /// <param name="onInputReady">
    /// Receives the child's stdin the moment it exists. A run that only reads output leaves it
    /// null; a screen that has to answer a question needs it, and a question asked after the
    /// writer would have been offered is a question nobody can answer.
    /// </param>
    /// <param name="cancellationToken">Stops the child.</param>
    public async Task<ProcessRunResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        Action<ProcessOutputLine>? onOutput = null,
        TimeSpan? gracePeriod = null,
        Action<IProcessInputWriter>? onInputReady = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var location = _locator.Locate();
        if (!location.Found)
            return ProcessRunResult.NotStarted(location.Error ?? $"`{OrkeonBinaryLocator.ExecutableBaseName}` was not found.");

        var request = new ProcessLaunchRequest
        {
            FileName = location.Path!,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            GracePeriod = gracePeriod ?? ProcessLaunchRequest.DefaultGracePeriod,
            OnInputReady = onInputReady,
        };

        return await _launcher.RunAsync(request, onOutput, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <c>orkeon doctor --json</c> and reads the diagnostic table out of its output.
    /// Note the CLI's own contract: doctor exits 1 when a check fails, so a non-zero exit
    /// code here is a diagnosis, not a Studio failure — read <see cref="DoctorReport.Checks"/>.
    /// </summary>
    public async Task<DoctorReport> RunDoctorAsync(
        string? workingDirectory = null,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var stdout = new StringBuilder();
        var run = await RunAsync(
            ["doctor", "--json"],
            workingDirectory,
            line =>
            {
                if (line.Channel == ProcessOutputChannel.StandardOutput)
                    stdout.AppendLine(line.Text);
                onOutput?.Invoke(line);
            },
            gracePeriod: null,
            onInputReady: null,
            cancellationToken).ConfigureAwait(false);

        var raw = stdout.ToString();
        if (run.Outcome == RunOutcome.NotStarted)
            return new DoctorReport { Run = run, RawOutput = raw, ParseError = run.Description };

        return DoctorReportParser.TryParse(raw, out var checks, out var error)
            ? new DoctorReport { Run = run, RawOutput = raw, Checks = checks }
            : new DoctorReport { Run = run, RawOutput = raw, ParseError = error };
    }
}
