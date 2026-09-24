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
/// <para>
/// A verb scripted with <see cref="Converse"/> is a session rather than a run (STUDIO-39: the
/// use-case search): it speaks its opening, then answers every stdin line and stays open until
/// its stdin closes — beside any other run, since it keeps its own output channel instead of the
/// single live one <see cref="Emit"/> writes to.
/// </para>
/// </summary>
internal sealed class ScriptedOrkeonCli : IProcessLauncher
{
    private readonly Dictionary<string, CliAnswer> _answers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Conversation> _conversations = new(StringComparer.Ordinal);
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
    /// Scripts <paramref name="verb"/> as a session: it speaks <paramref name="opening"/>, then
    /// answers each stdin line with what <paramref name="reply"/> returns, until its stdin closes.
    /// </summary>
    public ScriptedOrkeonCli Converse(string verb, IReadOnlyList<string> opening, Func<string, IEnumerable<string>> reply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(reply);

        _conversations[verb] = new Conversation(opening, reply);
        return this;
    }

    /// <summary>
    /// The next run of <paramref name="verb"/> plays its lines and then parks, so a stop can
    /// photograph a run in flight and the one after it can photograph how it ends.
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
        if (_conversations.TryGetValue(verb, out var conversation))
            return await ConverseAsync(request, conversation, onOutput, cancellationToken);

        var answer = _answers.TryGetValue(verb, out var scripted)
            ? scripted
            : new CliAnswer(0, []);

        _live = onOutput;
        try
        {
            request.OnInputReady?.Invoke(new ScriptedInputWriter(this));

            foreach (var line in answer.Lines)
                onOutput?.Invoke(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, line));

            if (!_heldVerbs.Remove(verb))
                return ProcessRunResult.FromExitCode(answer.ExitCode, TimeSpan.Zero);

            _parked = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var abandon = cancellationToken.Register(() => Release(OrkeonExitCodes.Cancelled));
            var exitCode = await _parked.Task;

            return cancellationToken.IsCancellationRequested
                ? ProcessRunResult.FromCancellation(
                    rawExitCode: -1,
                    ProcessTerminationOutcome.Of(ProcessTerminationMode.StoppedBySignal),
                    TimeSpan.Zero)
                : ProcessRunResult.FromExitCode(exitCode, TimeSpan.Zero);
        }
        finally
        {
            _live = null;
        }
    }

    /// <summary>
    /// The verb a request names: its first two arguments when that pair is scripted (a sub-verb
    /// such as <c>forge reopen</c>), else its first. A bare flag — <c>--version</c> — is its own
    /// verb: the About overlay asks for it exactly that way.
    /// </summary>
    private string VerbOf(ProcessLaunchRequest request)
    {
        var arguments = request.Arguments;
        if (arguments.Count > 1
            && (_answers.ContainsKey($"{arguments[0]} {arguments[1]}") || _conversations.ContainsKey($"{arguments[0]} {arguments[1]}")))
        {
            return $"{arguments[0]} {arguments[1]}";
        }

        return arguments.Count > 0 ? arguments[0] : "";
    }

    /// <summary>A session run: its own output channel, open until its stdin closes or it is cancelled.</summary>
    private async Task<ProcessRunResult> ConverseAsync(
        ProcessLaunchRequest request,
        Conversation conversation,
        Action<ProcessOutputLine>? onOutput,
        CancellationToken cancellationToken)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        request.OnInputReady?.Invoke(new ConversingInputWriter(this, conversation, onOutput, closed));

        foreach (var line in conversation.Opening)
            onOutput?.Invoke(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, line));

        using var abandon = cancellationToken.Register(() => closed.TrySetResult());
        await closed.Task;

        return cancellationToken.IsCancellationRequested
            ? ProcessRunResult.FromCancellation(
                rawExitCode: -1,
                ProcessTerminationOutcome.Of(ProcessTerminationMode.StoppedBySignal),
                TimeSpan.Zero)
            : ProcessRunResult.FromExitCode(0, TimeSpan.Zero);
    }

    /// <summary>What a session says when it starts, and how it answers a line.</summary>
    private sealed record Conversation(IReadOnlyList<string> Opening, Func<string, IEnumerable<string>> Reply);

    /// <summary>A session's stdin: each line is recorded, then answered on the session's own channel.</summary>
    private sealed class ConversingInputWriter(
        ScriptedOrkeonCli owner,
        Conversation conversation,
        Action<ProcessOutputLine>? onOutput,
        TaskCompletionSource closed) : IProcessInputWriter
    {
        public bool TryWriteLine(string line)
        {
            if (closed.Task.IsCompleted)
                return false;

            owner.InputLines.Add(line);
            foreach (var answer in conversation.Reply(line))
                onOutput?.Invoke(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, answer));

            return true;
        }

        public void Close() => closed.TrySetResult();
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
