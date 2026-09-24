using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

/// <summary>What the scripted CLI answers for one verb.</summary>
/// <param name="ExitCode">The exit code the run ends on.</param>
/// <param name="Lines">Standard-output lines, played in order.</param>
internal sealed record CliAnswer(int ExitCode, IReadOnlyList<string> Lines);

/// <summary>
/// The one double the capture campaign needs: a child process that never starts.
/// <para>
/// Everything else the seeded world offers travels the real code path over real files, because
/// every store that matters takes an explicit path. A process does not, and four families of
/// screens live behind one — the diagnostic verdict, the About version line, the Run screen in
/// flight, and the wizard's live states, which no artefact on disk can reproduce because they
/// only exist while a stream is open.
/// </para>
/// <para>
/// Routing is on argv[0], so one instance answers <c>doctor</c>, <c>--version</c>, <c>forge</c>
/// and <c>run</c> for the whole campaign — or on argv[0] and argv[1] together when that pair has
/// a script of its own: <c>forge reopen</c> is a conversation of its own, distinct from the
/// cycle <c>forge</c> answers.
/// </para>
/// </summary>
internal sealed class ScriptedOrkeonCli : IProcessLauncher
{
    private readonly Dictionary<string, CliAnswer> _answers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _heldVerbs = new(StringComparer.Ordinal);

    private Action<ProcessOutputLine>? _live;
    private TaskCompletionSource<int>? _parked;

    /// <summary>Every launch that was asked for — nothing was ever spawned.</summary>
    public List<ProcessLaunchRequest> Requests { get; } = [];

    /// <summary>What was written to the child's standard input.</summary>
    public List<string> InputLines { get; } = [];

    /// <summary>Called with each line written to stdin while a run is live.</summary>
    public Action<string>? OnInputLine { get; set; }

    /// <summary>Scripts the answer for one verb.</summary>
    public ScriptedOrkeonCli Answer(string verb, int exitCode, params string[] lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);
        ArgumentNullException.ThrowIfNull(lines);

        _answers[verb] = new CliAnswer(exitCode, lines);
        return this;
    }

    /// <summary>
    /// The next run of <paramref name="verb"/> plays its lines and then parks, so a stop can
    /// photograph a run in flight and the one after it can photograph how it ends. It parks
    /// before the line that reports its end, which the release plays: parked after it, the shot
    /// «in flight» was of a run that had already said it was over (STUDIO-34).
    /// </summary>
    public void Hold(string verb) => _heldVerbs.Add(verb);

    /// <summary>True while a run is parked waiting for <see cref="Release"/>.</summary>
    public bool IsParked => _parked is not null;

    /// <summary>
    /// Pushes one line into the live stream, mid-run. Outside a run there is no listener and the
    /// line falls on the floor — which is exactly what a dead child would do.
    /// </summary>
    public void Emit(string line) =>
        _live?.Invoke(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, line));

    /// <summary>Lets a parked run finish.</summary>
    public void Release(int exitCode = 0)
    {
        var parked = _parked;
        _parked = null;
        parked?.TrySetResult(exitCode);
    }

    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(
        ProcessLaunchRequest request,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Requests.Add(request);

        var verb = VerbOf(request);
        var answer = _answers.TryGetValue(verb, out var scripted)
            ? scripted
            : new CliAnswer(0, []);

        _live = onOutput;
        try
        {
            request.OnInputReady?.Invoke(new ScriptedInputWriter(this));

            var held = _heldVerbs.Remove(verb);
            var end = held ? EndOf(answer.Lines) : answer.Lines.Count;
            Play(answer.Lines, 0, end, onOutput);

            if (!held)
                return ProcessRunResult.FromExitCode(answer.ExitCode, TimeSpan.Zero);

            _parked = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var abandon = cancellationToken.Register(() => Release(OrkeonExitCodes.Cancelled));
            var exitCode = await _parked.Task;

            if (cancellationToken.IsCancellationRequested)
            {
                return ProcessRunResult.FromCancellation(
                    rawExitCode: -1,
                    ProcessTerminationOutcome.Of(ProcessTerminationMode.StoppedBySignal),
                    TimeSpan.Zero);
            }

            // Released: the run reports its end, then exits.
            Play(answer.Lines, end, answer.Lines.Count, onOutput);
            return ProcessRunResult.FromExitCode(exitCode, TimeSpan.Zero);
        }
        finally
        {
            _live = null;
        }
    }

    /// <summary>Plays the lines from <paramref name="from"/> up to, not including, <paramref name="to"/>.</summary>
    private static void Play(IReadOnlyList<string> lines, int from, int to, Action<ProcessOutputLine>? onOutput)
    {
        for (var index = from; index < to; index++)
            onOutput?.Invoke(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, lines[index]));
    }

    /// <summary>Where a script's end starts: its last <c>run.finished</c> line, or its length when it reports none.</summary>
    private static int EndOf(IReadOnlyList<string> lines)
    {
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            if (lines[index].Contains("\"kind\":\"run.finished\"", StringComparison.Ordinal))
                return index;
        }

        return lines.Count;
    }

    /// <summary>
    /// The verb a request names: its first two arguments when that pair is scripted (a sub-verb
    /// such as <c>forge reopen</c>), else its first. A bare flag — <c>--version</c> — is its own
    /// verb: the About overlay asks for it exactly that way.
    /// </summary>
    private string VerbOf(ProcessLaunchRequest request)
    {
        var arguments = request.Arguments;
        if (arguments.Count > 1 && _answers.ContainsKey($"{arguments[0]} {arguments[1]}"))
            return $"{arguments[0]} {arguments[1]}";

        return arguments.Count > 0 ? arguments[0] : "";
    }

    private sealed class ScriptedInputWriter(ScriptedOrkeonCli owner) : IProcessInputWriter
    {
        public bool TryWriteLine(string line)
        {
            owner.InputLines.Add(line);
            owner.OnInputLine?.Invoke(line);
            return true;
        }

        public void Close()
        {
            // Nothing to close: this writer only appends to the owner's in-memory line list,
            // so there is no stream, handle or child process to release.
        }
    }
}
