using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.ConsoleApp.Replay;

/// <summary>
/// The REPL's console while a replay runs (<c>orkeon-repl --replay</c>, <c>/replay</c>): a decorator of the
/// real <see cref="IConsoleAdapter"/> that answers the reads with the lines of the replay file, as if typed.
/// The read loop, the tool approval channel (its interceptor, and <c>runCrew</c>'s
/// <c>AnswerFromConsole</c>) and <c>human_input</c> all read through it, so a line answers whatever asks.
/// <list type="bullet">
///   <item><description>Before each line the loop reads, the <see cref="ReplayIdleGate"/> waits for the background work to end, or for a question the line answers.</description></item>
///   <item><description>Each line is echoed (<c>/cmd args</c> after the prompt, <c>&gt; /cmd args</c> in the TUI).</description></item>
///   <item><description>At the first error — a command that failed, was cancelled or is unknown, a background task launched by the replay that failed, Ctrl-C — it writes <c>replay stopped at line N: &lt;cause&gt;</c>.</description></item>
///   <item><description>At the end or on an error, it closes the input (the REPL exits, <see cref="ExitCode"/> set) or hands the keyboard back to the real console.</description></item>
/// </list>
/// With no replay running, every member delegates to the real console.
/// </summary>
internal sealed class ReplayConsoleAdapter : IConsoleAdapter, ICommandOutcomeObserver
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
    /// Ctrl-C: stops the replay at the line in flight; the caller cancels the running command. False when no
    /// replay runs, the key then keeping its usual effect.
    /// </summary>
    public bool Interrupt()
    {
        lock (_lock)
        {
            if (_run is not { } run)
                return false;
            run.Fail(run.Current?.Number ?? FirstLineNumber(run), "interrupted");
            run.Interrupt.Cancel();
            return true;
        }
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

        if (_gate.TakeFailure() is { } background)
            lock (_lock) run.Fail(background.LineNumber, background.Cause);

        if (run.HasFailed || !run.TryNext(out var next))
            return EndRun(run) ? null : await _inner.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        lock (_lock)
            run.Current = next;
        _gate.TrackingLine = next.Number;
        Echo(next.Text);
        return next.Text;
    }

    /// <summary>
    /// A read from inside a command (the approval answered by <c>runCrew</c>, a prompt of a menu command): the next
    /// line is served at once — the command holds the loop, a question is what reads it.
    /// </summary>
    public string? ReadLine()
    {
        if (CurrentRun() is not { } run)
            return _inputClosed ? null : _inner.ReadLine();

        if (_gate.TakeFailure() is { } background)
            lock (_lock) run.Fail(background.LineNumber, background.Cause);

        if (run.HasFailed || !run.TryNext(out var next))
            return EndRun(run) ? null : _inner.ReadLine();

        Echo(next.Text);
        return next.Text;
    }

    /// <inheritdoc />
    public void Write(string text) => _inner.Write(text);

    /// <inheritdoc />
    public void WriteLine(string text) => _inner.WriteLine(text);

    /// <inheritdoc />
    public ConsoleKeyInfo ReadKey(bool intercept = false) => _inner.ReadKey(intercept);

    /// <inheritdoc />
    public Task<ConsoleKeyInfo> ReadKeyAsync(bool intercept = false, CancellationToken cancellationToken = default)
        => _inner.ReadKeyAsync(intercept, cancellationToken);

    /// <inheritdoc />
    public void Clear() => _inner.Clear();

    /// <summary>
    /// The REPL ended (the input closed, or <c>/exit</c> before the last line): ends a replay still running,
    /// saying why when a line failed, and returns the exit code of <c>--replay</c>.
    /// </summary>
    public int Conclude()
    {
        if (CurrentRun() is { } run)
        {
            if (_gate.TakeFailure() is { } background)
                lock (_lock) run.Fail(background.LineNumber, background.Cause);
            EndRun(run);
        }

        return ExitCode ?? PassedExitCode;
    }

    private ReplayRun? CurrentRun()
    {
        lock (_lock)
            return _run;
    }

    private void Echo(string text) => _inner.WriteLine(_echoPrefix + text);

    /// <summary>Ends the run, says why when it failed; true when the input is now closed.</summary>
    private bool EndRun(ReplayRun run)
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
        run.Interrupt.Dispose();
        if (run.Failure is { } failure)
            _inner.WriteLine($"replay stopped at line {failure.LineNumber}: {failure.Cause}");
        else if (!run.CloseInputAtEnd)
            _inner.WriteLine($"replay finished: {run.Count} line(s)");
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

        public CancellationTokenSource Interrupt { get; } = new();

        /// <summary>The last line the read loop was fed: the outcomes are charged to it.</summary>
        public ReplayLine? Current { get; set; }

        public ReplayFailure? Failure { get; private set; }

        public bool HasFailed => Failure is not null;

        public int Count => lines.Count;

        public ReplayLine First => lines[0];

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
