using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Launch;

/// <summary>What one launch spawns.</summary>
public sealed record RunLaunchRequest
{
    /// <summary>The crew the launch is about, as the history records it.</summary>
    public required string TargetPath { get; init; }

    /// <summary>Arguments for the <c>orkeon</c> process, one element per argv slot.</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>The <c>--settings</c> file, when one was pinned.</summary>
    public string? SettingsPath { get; init; }

    /// <summary>Working directory of the child process.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Whether the launch joins the recent list. False for a dry run: <c>--validate</c>
    /// kicks nothing off, so replaying it from the history would replay a check, not a run.
    /// </summary>
    public bool RecordInHistory { get; init; } = true;

    /// <summary>
    /// Environment variables laid over the child's inherited environment — an adopted
    /// team's model profile travels here as <c>ORKEON_Llm__*</c>, never inside a file.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentOverrides { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// One launcher's run lifecycle: at most one <c>orkeon</c> child at a time, its output
/// handed to the caller line by line, its cancellation reachable from the UI thread, and
/// its outcome appended to the recent list.
/// <para>
/// Every front-end shares this class rather than re-implementing the lifecycle: the
/// terminal launcher, the WPF Launch tab, and any future flow that spawns the CLI.
/// </para>
/// </summary>
public sealed class RunSession
{
    private readonly OrkeonProcessRunner _runner;
    private readonly ILaunchHistoryStore? _history;
    private CancellationTokenSource? _cancellation;

    /// <summary>Creates a session over <paramref name="runner"/>, optionally recording launches.</summary>
    public RunSession(OrkeonProcessRunner runner, ILaunchHistoryStore? history = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _history = history;
    }

    /// <summary>True between the spawn and the child's exit.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Outcome of the last finished run, null before the first one.</summary>
    public ProcessRunResult? LastResult { get; private set; }

    /// <summary>The recent-launch list as last read or written.</summary>
    public LaunchHistory History { get; private set; } = LaunchHistory.Empty;

    /// <summary>Where the CLI binary was found — a status field, no process is spawned.</summary>
    public BinaryLocation LocateBinary() => _runner.LocateBinary();

    /// <summary>Reads the recent-launch list; a session with no store starts from an empty one.</summary>
    public async Task<LaunchHistory> LoadHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (_history is not null)
            History = await _history.LoadAsync(cancellationToken).ConfigureAwait(false);

        return History;
    }

    /// <summary>
    /// Runs <paramref name="request"/> to completion. Cancellation — the token, or
    /// <see cref="RequestCancellation"/> — stops the child gracefully then kills it, and
    /// comes back as a <see cref="RunOutcome.Cancelled"/> result rather than an exception,
    /// so the launcher stays alive and shows exit code 130.
    /// </summary>
    /// <exception cref="InvalidOperationException">A run is already in flight.</exception>
    /// <param name="request">What to launch, and whether to record it.</param>
    /// <param name="onOutput">Receives each output line as it arrives.</param>
    /// <param name="onInputReady">
    /// Receives the child's stdin, for a screen that answers the run's questions rather than
    /// only watching it.
    /// </param>
    /// <param name="cancellationToken">Stops the run.</param>
    public async Task<ProcessRunResult> RunAsync(
        RunLaunchRequest request,
        Action<ProcessOutputLine>? onOutput = null,
        Action<IProcessInputWriter>? onInputReady = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (IsRunning)
            throw new InvalidOperationException("A run is already in flight: cancel it before starting another.");

        var entry = LaunchHistoryEntry.Starting(
            request.TargetPath,
            request.Arguments,
            request.SettingsPath,
            request.WorkingDirectory);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;
        IsRunning = true;

        ProcessRunResult result;
        try
        {
            result = await _runner.RunAsync(
                request.Arguments,
                request.WorkingDirectory,
                onOutput,
                gracePeriod: null,
                onInputReady,
                environment: request.EnvironmentOverrides,
                cancellationToken: cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            _cancellation = null;
            IsRunning = false;
        }

        LastResult = result;

        if (request.RecordInHistory)
        {
            var completed = entry.WithResult(result);

            // Not the run's token: a cancelled run must still be recorded with its 130.
            // Without a store the list still accumulates in memory, so a session-only
            // front-end shows the same recent list as a persisted one.
            History = _history is not null
                ? await _history.RecordAsync(completed, CancellationToken.None).ConfigureAwait(false)
                : History.Add(completed);
        }

        return result;
    }

    /// <summary>
    /// Asks the running child to stop. Returns false when nothing is running or the stop was
    /// already requested — pressing Escape twice is not an error.
    /// </summary>
    public bool RequestCancellation()
    {
        var cancellation = _cancellation;
        if (cancellation is null)
            return false;

        try
        {
            if (cancellation.IsCancellationRequested)
                return false;

            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // The run finished between the read and the cancel; nothing left to stop.
            return false;
        }
    }
}
