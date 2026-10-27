using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.ConsoleApp.Replay;

/// <summary>What a Ctrl-C (or the TUI's interrupt key) did to the replay.</summary>
internal enum ReplayInterrupt
{
    /// <summary>No replay runs: the key keeps its usual effect.</summary>
    NotRunning,

    /// <summary>The first interrupt of this replay: it stops at the line in flight.</summary>
    Stopping,

    /// <summary>The replay was already told to stop and still runs: the key keeps its usual effect (it kills the process).</summary>
    AlreadyStopping,
}

/// <summary>
/// The REPL's console while a replay runs (<c>orkeon-repl --replay</c>, <c>/replay</c>): a decorator of the
/// real <see cref="IConsoleAdapter"/> that answers the reads with the lines of the replay file, as if typed.
/// The read loop, the tool approval channel (its interceptor, and <c>runCrew</c>'s
/// <c>AnswerFromConsole</c>) and <c>human_input</c> all read through it, so a line answers whatever asks; one
/// tool approval consumes exactly one line, whoever reads it (<see cref="ReplayIdleGate"/>).
/// <list type="bullet">
///   <item><description>Before each line the loop reads, the <see cref="ReplayIdleGate"/> waits for the background work to end, or for a question the line answers.</description></item>
///   <item><description>Each line is echoed (<c>/cmd args</c> after the prompt, <c>&gt; /cmd args</c> in the TUI).</description></item>
///   <item><description>At the first error - a command that failed, was cancelled or is unknown, a background task launched by the replay that failed, a question left unanswered when the file ends, Ctrl-C - it writes <c>replay stopped at line N: &lt;cause&gt;</c> on a line of its own.</description></item>
///   <item><description>The replay ends at the loop's next read only - never inside a command's read - so the outcome of the command in flight and of the background work is known first. It then closes the input (the REPL exits, <see cref="ExitCode"/> set) or hands the keyboard back to the real console.</description></item>
/// </list>
/// With no replay running, every member delegates to the real console.
/// </summary>
internal sealed class ReplayConsoleAdapter : IConsoleAdapter, IThreadedConsole, ICommandOutcomeObserver
{
    /// <summary>The exit code of a replay whose every line passed.</summary>
    public const int PassedExitCode = 0;

    /// <summary>The exit code of a replay stopped on an error.</summary>
    public const int FailedExitCode = 1;

    /// <summary>The exit code of a replay file missing or unreadable.</summary>
    public const int UnreadableExitCode = 2;

    private readonly IConsoleAdapter _inner;
    private readonly ReplayIdleGate _gate;
    private readonly string _echoPrefix;
    private readonly Lock _lock = new();
    private ReplayRun? _run;
    private bool _inputClosed;

    /// <summary>Creates the decorator.</summary>
    /// <param name="inner">The real console.</param>
    /// <param name="gate">What a line waits for.</param>
    /// <param name="echoPrefix">Written before an echoed line: empty after a plain prompt, <c>"&gt; "</c> in the TUI.</param>
    public ReplayConsoleAdapter(IConsoleAdapter inner, ReplayIdleGate gate, string echoPrefix = "")
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _echoPrefix = echoPrefix ?? string.Empty;
    }

    /// <summary>Whether a replay runs.</summary>
    public bool IsActive
    {
        get
        {
            lock (_lock)
                return _run is not null;
        }
    }

    /// <summary>0 once a replay passed, 1 once one stopped on an error; null before a replay ended.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>
    /// Starts a replay; refused (false) while one runs.
    /// </summary>
    /// <param name="lines">The lines to feed.</param>
    /// <param name="closeInputAtEnd">
    /// True for <c>--replay</c> alone: at the end, or at the first error, the input ends and the REPL exits.
    /// False (<c>--interactive</c>, <c>/replay</c>): the keyboard is handed back.
    /// </param>
    public bool TryStart(IReadOnlyList<ReplayLine> lines, bool closeInputAtEnd)
    {
        ArgumentNullException.ThrowIfNull(lines);
        lock (_lock)
        {
            if (_run is not null)
                return false;
            _run = new ReplayRun(lines, closeInputAtEnd);
            ExitCode = null;
        }

        _gate.Attach();
        _gate.Reset();
        return true;
    }

    /// <summary>
    /// Ctrl-C, or the TUI's interrupt key: stops the replay at the line in flight; the caller cancels the running
    /// command. The first call of a replay answers <see cref="ReplayInterrupt.Stopping"/>; a later one, while the
    /// replay still runs (a command that ignores its cancellation), <see cref="ReplayInterrupt.AlreadyStopping"/>:
    /// the key then keeps its usual effect.
    /// </summary>
    public ReplayInterrupt Interrupt()
    {
        ReplayRun run;
        lock (_lock)
        {
            if (_run is null)
                return ReplayInterrupt.NotRunning;
            run = _run;
            if (run.Interrupted)
                return ReplayInterrupt.AlreadyStopping;
            run.Interrupted = true;
            run.Fail(run.Current?.Number ?? FirstLineNumber(run), "interrupted");
        }

        // Outside the lock: the cancellation runs the continuations of the waits, which take the lock.
        run.Interrupt.Cancel();
        return ReplayInterrupt.Stopping;
    }

    /// <inheritdoc />
    public void OnCommandOutcome(CommandOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Kind == CommandOutcomeKind.Succeeded)
            return;

        lock (_lock)
        {
            // A line the replay fed (not the /replay that started it, which no line fed).
            if (_run is { Current: { } line } run)
                run.Fail(line.Number, CauseOf(outcome));
        }
    }

    /// <inheritdoc />
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentRun() is not { } run)
            return _inputClosed ? null : await _inner.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        if (!run.HasFailed)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, run.Interrupt.Token);
            try
            {
                await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Ctrl-C while the background work runs: the replay stops at its line.
            }
        }

        TakeBackgroundFailure(run);
        ReplayLine? next = null;
        if (!run.HasFailed)
        {
            if (run.TryNext(out var taken))
                next = taken;
            else
                FailOnQuestionLeft(run); // The file ran out: a question still waiting is one the replay left unanswered.
        }

        if (run.HasFailed || next is null)
            return EndRun(run, afterPrompt: true) ? null : await _inner.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        // A command line is never an approval's answer (the channel leaves it to the loop): the claim is over.
        if (next.Text.TrimStart().StartsWith('/'))
            _gate.Release(ReplayReader.Loop);

        lock (_lock)
            run.Current = next;
        _gate.TrackingLine = next.Number;
        Echo(next.Text);
        return next.Text;
    }

    /// <summary>
    /// A read from inside a command (the approval answered by <c>runCrew</c>, a prompt of a menu command): the next
    /// line is served at once - the command holds the loop, a question is what reads it - unless the read loop
    /// already took the line for the approval waiting, in which case the read waits and, once that approval is
    /// answered, returns an empty line without taking one. When the file has run out, or the replay stopped, the
    /// read gets the end of the input (null), under <c>--interactive</c> too: the keyboard is never handed to a
    /// command before the stop line is written. The replay itself ends at the loop's next read, once the outcome
    /// of the command is known; it then writes the stop line and, under <c>--interactive</c>, hands the keyboard
    /// back to the loop.
    /// </summary>
    public string? ReadLine()
    {
        if (CurrentRun() is not { } run)
            return _inputClosed ? null : _inner.ReadLine();

        if (!run.HasFailed && !_gate.WaitForCommandRead(run.Interrupt.Token) && !run.HasFailed)
            return string.Empty;

        TakeBackgroundFailure(run);
        ReplayLine? next = null;
        if (!run.HasFailed)
        {
            if (run.TryNext(out var taken))
            {
                next = taken;
            }
            else
            {
                // A command asked for a line the file does not have.
                var cause = _gate.PendingApprovalTool is { } tool
                    ? $"replay ended with an approval waiting: {tool}"
                    : "replay ended while a command waited for a line";
                lock (_lock)
                    run.Fail(run.LastServedNumber, cause);
            }
        }

        if (run.HasFailed || next is null)
            return null;

        Echo(next.Text);
        return next.Text;
    }

    /// <inheritdoc />
    public void Write(string text) => _inner.Write(text);

    /// <inheritdoc />
    public void WriteLine(string text) => _inner.WriteLine(text);

    /// <inheritdoc />
    public bool SeparatesThreads => _inner is IThreadedConsole { SeparatesThreads: true };

    /// <inheritdoc />
    public void WriteLine(string? thread, string text)
    {
        if (_inner is IThreadedConsole threaded)
            threaded.WriteLine(thread, text);
        else
            _inner.WriteLine(text);
    }

    /// <inheritdoc />
    public void WriteAttention(string? thread, string text)
    {
        if (_inner is IThreadedConsole threaded)
            threaded.WriteAttention(thread, text);
        else
            _inner.WriteLine(text);
    }

    /// <inheritdoc />
    public ConsoleKeyInfo ReadKey(bool intercept = false) => _inner.ReadKey(intercept);

    /// <inheritdoc />
    public Task<ConsoleKeyInfo> ReadKeyAsync(bool intercept = false, CancellationToken cancellationToken = default)
        => _inner.ReadKeyAsync(intercept, cancellationToken);

    /// <inheritdoc />
    public void Clear() => _inner.Clear();

    /// <summary>
    /// The REPL ended (the input closed, <c>/exit</c> before the last line, or the loop threw): ends a replay still
    /// running, saying why when a line failed, and returns the exit code of <c>--replay</c>.
    /// </summary>
    /// <param name="crash">Why the REPL loop ended by throwing; null when it ended normally.</param>
    public int Conclude(string? crash = null)
    {
        if (CurrentRun() is { } run)
        {
            TakeBackgroundFailure(run);
            if (crash is not null)
                lock (_lock)
                    run.Fail(run.Current?.Number ?? run.LastServedNumber, crash);
            EndRun(run, afterPrompt: false);
        }

        return ExitCode ?? PassedExitCode;
    }

    private ReplayRun? CurrentRun()
    {
        lock (_lock)
            return _run;
    }

    private void TakeBackgroundFailure(ReplayRun run)
    {
        if (_gate.TakeFailure() is { } background)
            lock (_lock)
                run.Fail(background.LineNumber, background.Cause);
    }

    private void FailOnQuestionLeft(ReplayRun run)
    {
        string? cause = null;
        if (_gate.PendingApprovalTool is { } tool)
            cause = $"replay ended with an approval waiting: {tool}";
        else if (_gate.OtherQuestionPending)
            cause = "replay ended with a question waiting";
        if (cause is not null)
            lock (_lock)
                run.Fail(run.LastServedNumber, cause);
    }

    private void Echo(string text) => _inner.WriteLine(_echoPrefix + text);

    /// <summary>
    /// Ends the run, says why when it failed; true when the input is now closed. After a plain prompt, the message
    /// starts on a new line. The run's interrupt source is never disposed: a read may still look at its token.
    /// </summary>
    private bool EndRun(ReplayRun run, bool afterPrompt)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_run, run))
                return _inputClosed;
            _run = null;
            ExitCode = run.HasFailed ? FailedExitCode : PassedExitCode;
            if (run.CloseInputAtEnd)
                _inputClosed = true;
        }

        _gate.Reset();
        string? message = null;
        if (run.Failure is { } failure)
            message = $"replay stopped at line {failure.LineNumber}: {failure.Cause}";
        else if (!run.CloseInputAtEnd)
            message = $"replay finished: {run.Count} line(s)";
        if (message is not null)
        {
            // The plain prompt waits on its line; the TUI's prompt is a label, its transcript needs no break.
            if (afterPrompt && _echoPrefix.Length == 0)
                _inner.WriteLine(string.Empty);
            _inner.WriteLine(message);
        }

        return run.CloseInputAtEnd;
    }

    private static int FirstLineNumber(ReplayRun run) => run.Count > 0 ? run.First.Number : 0;

    private static string CauseOf(CommandOutcome outcome) => outcome.Kind switch
    {
        CommandOutcomeKind.Unknown => outcome.Message ?? $"unknown command '{outcome.Input.Trim()}'",
        CommandOutcomeKind.Cancelled => outcome.Message ?? $"command '{outcome.CommandName}' cancelled",
        _ => string.IsNullOrWhiteSpace(outcome.Message) ? $"command '{outcome.CommandName}' failed" : outcome.Message!,
    };

    /// <summary>One replay in progress.</summary>
    private sealed class ReplayRun(IReadOnlyList<ReplayLine> lines, bool closeInputAtEnd)
    {
        private int _next;

        public bool CloseInputAtEnd { get; } = closeInputAtEnd;

        /// <summary>Cancelled by Ctrl-C; never disposed (no timer), so its token can be read at any time.</summary>
        public CancellationTokenSource Interrupt { get; } = new();

        /// <summary>Whether Ctrl-C was already pressed for this replay.</summary>
        public bool Interrupted { get; set; }

        /// <summary>The last line the read loop was fed: the outcomes are charged to it.</summary>
        public ReplayLine? Current { get; set; }

        public ReplayFailure? Failure { get; private set; }

        public bool HasFailed => Failure is not null;

        public int Count => lines.Count;

        public ReplayLine First => lines[0];

        /// <summary>The number of the last line served, by the loop or a command; the first line's before any.</summary>
        public int LastServedNumber
        {
            get
            {
                var served = Math.Min(Volatile.Read(ref _next), lines.Count);
                return served > 0 ? lines[served - 1].Number : FirstLineNumber(this);
            }
        }

        /// <summary>Records the first failure; the later ones are ignored.</summary>
        public void Fail(int lineNumber, string cause) => Failure ??= new ReplayFailure(lineNumber, cause);

        public bool TryNext(out ReplayLine line)
        {
            var index = Interlocked.Increment(ref _next) - 1;
            if (index < lines.Count)
            {
                line = lines[index];
                return true;
            }

            line = null!;
            return false;
        }
    }
}
