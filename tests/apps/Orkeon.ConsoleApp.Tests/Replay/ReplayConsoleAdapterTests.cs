using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.ConsoleApp.Replay;
using Orkeon.Cli.Abstractions.Tests.Doubles;
using Orkeon.ConsoleApp.Tests.Fakes;

namespace Orkeon.ConsoleApp.Tests.Replay;

/// <summary>
/// The replay's console: it feeds the lines once nothing runs, answers a question with the next line, and at
/// the end closes the input or hands the keyboard back.
/// </summary>
public sealed class ReplayConsoleAdapterTests : IDisposable
{
    // A hang guard, never reached when the test passes: no test bounds a duration.
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(2);

    private readonly FakeConsoleAdapter _inner = new();
    private readonly CommandInstanceRegistry _registry = new();
    private readonly FakeToolApprovalWatch _approvals = new();
    private readonly ReplayIdleGate _gate;
    private readonly ReplayConsoleAdapter _replay;

    public ReplayConsoleAdapterTests()
    {
        _gate = new ReplayIdleGate(() => _registry, () => _approvals);
        _replay = new ReplayConsoleAdapter(_inner, _gate);
    }

    public void Dispose() => _gate.Dispose();

    private static ReplayLine[] Lines(params string[] texts)
        => texts.Select((text, index) => new ReplayLine(index + 1, text)).ToArray();

    private CommandInstance StartBackgroundTask(string name = "dev-batches")
        => _registry.Register(name, CommandInstanceKind.Async, "dev", "run the batches", Guid.NewGuid());

    private static CommandResponse Done(string name)
        => new(name, name, success: true, payload: "ok", error: null);

    // A read from inside a command (runCrew's AnswerFromConsole): synchronous, as the production code does it.
    private string? CommandRead() => _replay.ReadLine();

    [Fact]
    public async Task ShouldFeedAndEchoEachLine_WhenNothingRuns()
    {
        _replay.TryStart(Lines("/help", "/tasks"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal("/help", await _replay.ReadLineAsync(ct));
        Assert.Equal("/tasks", await _replay.ReadLineAsync(ct));
        Assert.Equal(["/help", "/tasks"], _inner.Lines);
    }

    [Fact]
    public async Task ShouldAnswerAnApprovalWithTheNextLine_WhenABackgroundTaskWaitsForIt()
    {
        _replay.TryStart(Lines("/dev-batches plan.md", "y"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal("/dev-batches plan.md", await _replay.ReadLineAsync(ct));
        StartBackgroundTask();

        var answer = _replay.ReadLineAsync(ct);
        Assert.False(answer.IsCompleted);
        _approvals.Ask("write_file");

        Assert.Equal("y", await answer.WaitAsync(HangGuard, ct));
    }

    [Fact]
    public async Task ShouldWaitForTheRunningTask_BeforeFeedingTheNextLine()
    {
        _replay.TryStart(Lines("/dev-batches plan.md", "/tasks"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal("/dev-batches plan.md", await _replay.ReadLineAsync(ct));
        var task = StartBackgroundTask();

        var next = _replay.ReadLineAsync(ct);
        Assert.False(next.IsCompleted);
        task.Complete(Done("dev-batches"));

        Assert.Equal("/tasks", await next.WaitAsync(HangGuard, ct));
        Assert.Null(_replay.ExitCode);
    }

    [Fact]
    public async Task ShouldStopAtTheLineThatLaunchedIt_WhenABackgroundTaskFails()
    {
        _replay.TryStart(Lines("/dev-batches plan.md", "/tasks"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal("/dev-batches plan.md", await _replay.ReadLineAsync(ct));
        var task = StartBackgroundTask();

        var next = _replay.ReadLineAsync(ct);
        task.Fail("the build broke");

        Assert.Null(await next.WaitAsync(HangGuard, ct));
        Assert.Equal(ReplayConsoleAdapter.FailedExitCode, _replay.ExitCode);
        Assert.True(_inner.Contains("replay stopped at line 1: [t1] dev-batches failed: the build broke"));
    }

    [Fact]
    public async Task ShouldCloseTheInput_WhenTheLastLineWasFedWithoutInteractive()
    {
        _replay.TryStart(Lines("/help"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        _inner.ScriptedLines = new Queue<string?>(["typed by hand"]);

        Assert.Equal("/help", await _replay.ReadLineAsync(ct));
        Assert.Null(await _replay.ReadLineAsync(ct));
        Assert.Null(await _replay.ReadLineAsync(ct));
        Assert.Equal(ReplayConsoleAdapter.PassedExitCode, _replay.ExitCode);
        Assert.False(_replay.IsActive);
    }

    [Fact]
    public async Task ShouldHandTheKeyboardBack_WhenTheLastLineWasFedInInteractive()
    {
        _replay.TryStart(Lines("/help"), closeInputAtEnd: false);
        var ct = TestContext.Current.CancellationToken;
        _inner.ScriptedLines = new Queue<string?>(["typed by hand"]);

        Assert.Equal("/help", await _replay.ReadLineAsync(ct));
        Assert.Equal("typed by hand", await _replay.ReadLineAsync(ct));
        Assert.True(_inner.Contains("replay finished: 1 line(s)"));
        Assert.False(_replay.IsActive);
    }

    [Fact]
    public async Task ShouldExitOne_WhenTheLastLinesTaskFailsWhileTheLoopReadsAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        Task<string?>? readWhileSettling = null;

        // Subscribed before the replay's gate: the loop reads again once the state is Failed, before the gate saw it.
        _registry.InstanceTerminated += (_, _) => readWhileSettling = _replay.ReadLineAsync(ct);
        _replay.TryStart(Lines("/bgfail"), closeInputAtEnd: true);
        Assert.Equal("/bgfail", await _replay.ReadLineAsync(ct));
        var task = StartBackgroundTask("bgfail");

        task.Fail("no such crew");

        Assert.Null(await readWhileSettling!.WaitAsync(HangGuard, ct));
        Assert.Equal(ReplayConsoleAdapter.FailedExitCode, _replay.ExitCode);
        Assert.True(_inner.Contains("replay stopped at line 1: [t1] bgfail failed: no such crew"));
    }

    [Fact]
    public async Task ShouldStopAtTheLineThatLaunchedIt_WhenItsCompletedCallbackFails()
    {
        _replay.TryStart(Lines("/askbg-throws hello"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal("/askbg-throws hello", await _replay.ReadLineAsync(ct));
        var task = StartBackgroundTask("askbg-throws");

        // As the runner does: the drain is held from the settle until completed() ended, and reports its failure.
        var drain = _registry.BeginWork();
        task.Complete(Done("askbg-throws"));
        var next = _replay.ReadLineAsync(ct);
        Assert.False(next.IsCompleted);
        _registry.ReportCompletionFailure(task, "Error: render broke");
        drain.Dispose();

        Assert.Null(await next.WaitAsync(HangGuard, ct));
        Assert.Equal(ReplayConsoleAdapter.FailedExitCode, _replay.ExitCode);
        Assert.True(_inner.Contains("replay stopped at line 1: [t1] askbg-throws completed() failed: Error: render broke"));
    }

    [Fact]
    public async Task ShouldPass_WhenACompletedCallbackOnlyRunsLong()
    {
        _replay.TryStart(Lines("/bgslow"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal("/bgslow", await _replay.ReadLineAsync(ct));
        var task = StartBackgroundTask("bgslow");

        var drain = _registry.BeginWork();
        task.Complete(Done("bgslow"));
        var next = _replay.ReadLineAsync(ct);
        Assert.False(next.IsCompleted);
        drain.Dispose();

        Assert.Null(await next.WaitAsync(HangGuard, ct));
        Assert.Equal(ReplayConsoleAdapter.PassedExitCode, _replay.ExitCode);
    }

    [Fact]
    public async Task ShouldEndACommandsReadPastTheEnd_BeforeHandingTheKeyboardBackInInteractive()
    {
        _replay.TryStart(Lines("/ask"), closeInputAtEnd: false);
        var ct = TestContext.Current.CancellationToken;
        _inner.ScriptedLines = new Queue<string?>(["typed by hand"]);
        Assert.Equal("/ask", await _replay.ReadLineAsync(ct));

        Assert.Null(CommandRead());

        Assert.Equal("typed by hand", await _replay.ReadLineAsync(ct));
        Assert.True(_inner.Contains("replay stopped at line 1: replay ended while a command waited for a line"));
    }

    [Fact]
    public async Task ShouldStopAtTheFedLine_WhenItsCommandFails()
    {
        _replay.TryStart(Lines("/help", "/bogus", "/tasks"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        await _replay.ReadLineAsync(ct);
        _replay.OnCommandOutcome(new CommandOutcome("/help", "help", CommandOutcomeKind.Succeeded, null));
        await _replay.ReadLineAsync(ct);
        _replay.OnCommandOutcome(new CommandOutcome("/bogus", null, CommandOutcomeKind.Unknown, "unknown command '/bogus'"));

        Assert.Null(await _replay.ReadLineAsync(ct));
        Assert.Equal(1, _replay.Conclude());
        Assert.True(_inner.Contains("replay stopped at line 2: unknown command '/bogus'"));
    }

    [Fact]
    public void ShouldRefuseASecondReplay_WhileOneRuns()
    {
        Assert.True(_replay.TryStart(Lines("/help"), closeInputAtEnd: false));

        Assert.False(_replay.TryStart(Lines("/tasks"), closeInputAtEnd: false));
    }

    [Fact]
    public async Task ShouldStopAtTheLineInFlight_WhenInterrupted()
    {
        _replay.TryStart(Lines("/dev-batches plan.md", "/tasks"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        await _replay.ReadLineAsync(ct);
        StartBackgroundTask();
        var next = _replay.ReadLineAsync(ct);

        Assert.Equal(ReplayInterrupt.Stopping, _replay.Interrupt());

        Assert.Null(await next.WaitAsync(HangGuard, ct));
        Assert.True(_inner.Contains("replay stopped at line 1: interrupted"));
        Assert.Equal(ReplayInterrupt.NotRunning, _replay.Interrupt());
    }

    [Fact]
    public async Task ShouldAnswerAlreadyStopping_WhenInterruptedAgainWhileTheReplayStillRuns()
    {
        _replay.TryStart(Lines("/run-crew demo", "/tasks"), closeInputAtEnd: true);
        await _replay.ReadLineAsync(TestContext.Current.CancellationToken);

        // The command in flight ignores its cancellation: the replay still runs after the first Ctrl-C.
        Assert.Equal(ReplayInterrupt.Stopping, _replay.Interrupt());

        Assert.Equal(ReplayInterrupt.AlreadyStopping, _replay.Interrupt());
        Assert.True(_replay.IsActive);
    }

    [Fact]
    public async Task ShouldConcludeOnlyAtTheLoopsNextRead_WhenTheFileRunsOutInsideACommandsRead()
    {
        _replay.TryStart(Lines("/run-crew demo"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal("/run-crew demo", await _replay.ReadLineAsync(ct));

        // The command reads a line the file does not have: its input ends, the replay does not conclude yet.
        Assert.Null(CommandRead());
        Assert.True(_replay.IsActive);
        Assert.Null(_replay.ExitCode);
        _replay.OnCommandOutcome(new CommandOutcome("/run-crew demo", "run-crew", CommandOutcomeKind.Failed, "Error: the crew was denied"));

        Assert.Null(await _replay.ReadLineAsync(ct));
        Assert.Equal(ReplayConsoleAdapter.FailedExitCode, _replay.ExitCode);
        Assert.True(_inner.Contains("replay stopped at line 1: replay ended while a command waited for a line"));
    }

    [Fact]
    public async Task ShouldFail_WhenTheFileEndsWithAnApprovalWaiting()
    {
        _replay.TryStart(Lines("/dev-batches plan.md"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        await _replay.ReadLineAsync(ct);
        StartBackgroundTask();
        _approvals.Ask("write_file");

        Assert.Null(await _replay.ReadLineAsync(ct).WaitAsync(HangGuard, ct));

        Assert.Equal(ReplayConsoleAdapter.FailedExitCode, _replay.ExitCode);
        Assert.True(_inner.Contains("replay stopped at line 1: replay ended with an approval waiting: write_file"));
    }

    [Fact]
    public async Task ShouldFail_WhenACommandReadsForAnApprovalTheFileDoesNotAnswer()
    {
        _replay.TryStart(Lines("/run-crew demo"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        await _replay.ReadLineAsync(ct);
        _approvals.Ask("write_file");

        Assert.Null(CommandRead());
        _approvals.Pending.Clear();
        _approvals.NotifyPendingChanged();

        Assert.Null(await _replay.ReadLineAsync(ct).WaitAsync(HangGuard, ct));
        Assert.Equal(ReplayConsoleAdapter.FailedExitCode, _replay.ExitCode);
        Assert.True(_inner.Contains("replay stopped at line 1: replay ended with an approval waiting: write_file"));
    }

    [Fact]
    public async Task ShouldServeOneLinePerApproval_WhenACommandReadsAfterTheLoopTookTheLine()
    {
        _replay.TryStart(Lines("/dev-batches plan.md", "y", "/tasks"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        await _replay.ReadLineAsync(ct);
        var task = StartBackgroundTask();
        _approvals.Ask("write_file");
        // The loop takes the answer first: it holds the claim on the approval.
        Assert.Equal("y", await _replay.ReadLineAsync(ct).WaitAsync(HangGuard, ct));

        // runCrew's AnswerFromConsole, in the background task, reads for the same approval.
        var commandRead = Task.Run(_replay.ReadLine, ct);
        Assert.True(SpinWait.SpinUntil(() => _gate.CommandReadsWaiting == 1, HangGuard));
        // The loop's line answers it, as the approval channel's interceptor does.
        _approvals.Pending.Clear();
        _approvals.NotifyPendingChanged();

        Assert.Equal(string.Empty, await commandRead.WaitAsync(HangGuard, ct));
        task.Complete(Done("dev-batches"));
        Assert.Equal("/tasks", await _replay.ReadLineAsync(ct).WaitAsync(HangGuard, ct));
    }

    [Fact]
    public async Task ShouldKeepTheLoopWaiting_WhenACommandTookTheLineForTheApproval()
    {
        _replay.TryStart(Lines("/dev-batches plan.md", "y", "/tasks"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        await _replay.ReadLineAsync(ct);
        var task = StartBackgroundTask();
        _approvals.Ask("write_file");
        // runCrew's AnswerFromConsole takes the answer first: it holds the claim.
        Assert.Equal("y", CommandRead());

        var next = _replay.ReadLineAsync(ct);
        Assert.False(next.IsCompleted);
        _approvals.Pending.Clear();
        _approvals.NotifyPendingChanged();
        // The approval answered, the loop still waits for the background task: the next line is not taken.
        Assert.Null(_gate.Claim);
        Assert.False(next.IsCompleted);
        task.Complete(Done("dev-batches"));

        Assert.Equal("/tasks", await next.WaitAsync(HangGuard, ct));
    }

    [Fact]
    public async Task ShouldEndACommandsWaitForTheClaim_WhenInterrupted()
    {
        _replay.TryStart(Lines("/dev-batches plan.md", "y", "/tasks"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        await _replay.ReadLineAsync(ct);
        StartBackgroundTask();
        _approvals.Ask("write_file");
        Assert.Equal("y", await _replay.ReadLineAsync(ct).WaitAsync(HangGuard, ct));
        var commandRead = Task.Run(_replay.ReadLine, ct);
        Assert.True(SpinWait.SpinUntil(() => _gate.CommandReadsWaiting == 1, HangGuard));

        Assert.Equal(ReplayInterrupt.Stopping, _replay.Interrupt());

        Assert.Null(await commandRead.WaitAsync(HangGuard, ct));
        Assert.Null(await _replay.ReadLineAsync(ct).WaitAsync(HangGuard, ct));
        Assert.True(_inner.Contains("replay stopped at line 2: interrupted"));
    }

    [Fact]
    public async Task ShouldWriteTheStopLineOnALineOfItsOwn_AfterThePlainPrompt()
    {
        _replay.TryStart(Lines("/bogus"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        await _replay.ReadLineAsync(ct);
        _replay.OnCommandOutcome(new CommandOutcome("/bogus", null, CommandOutcomeKind.Unknown, "unknown command '/bogus'"));

        Assert.Null(await _replay.ReadLineAsync(ct));

        var stop = _inner.Lines.FindIndex(l => l.StartsWith("replay stopped at line 1", StringComparison.Ordinal));
        Assert.True(stop > 0);
        Assert.Equal(string.Empty, _inner.Lines[stop - 1]);
    }

    [Fact]
    public async Task ShouldWriteNoBlankLine_InTheTui()
    {
        using var gate = new ReplayIdleGate(() => _registry, () => _approvals);
        var inner = new FakeConsoleAdapter();
        var replay = new ReplayConsoleAdapter(inner, gate, "> ");
        replay.TryStart(Lines("/help"), closeInputAtEnd: false);
        var ct = TestContext.Current.CancellationToken;
        inner.ScriptedLines = new Queue<string?>(["typed by hand"]);
        await replay.ReadLineAsync(ct);

        await replay.ReadLineAsync(ct);

        Assert.Equal(["> /help", "replay finished: 1 line(s)"], inner.Lines);
    }

    [Fact]
    public async Task ShouldStopOnTheCrash_WhenTheReplThrew()
    {
        _replay.TryStart(Lines("/help", "/tasks"), closeInputAtEnd: true);
        await _replay.ReadLineAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReplayConsoleAdapter.FailedExitCode, _replay.Conclude("the REPL failed: boom"));

        Assert.True(_inner.Contains("replay stopped at line 1: the REPL failed: boom"));
        Assert.False(_replay.IsActive);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "BR-10")]
    public void ShouldForwardThreadedLines_WhenReplayDecoratesThreadedConsole()
    {
        var threaded = new FakeThreadedConsole { SeparatesThreads = true };
        var over = (object)new ReplayConsoleAdapter(threaded, _gate) as IThreadedConsole;
        var plain = (object)new ReplayConsoleAdapter(new FakeConsoleAdapter(), _gate) as IThreadedConsole;

        Assert.NotNull(over);
        Assert.True(over.SeparatesThreads);
        over.WriteLine("t1", "x");
        over.WriteAttention("t1", "y");
        Assert.Equal([("t1", "x")], threaded.Lines);
        Assert.Equal([("t1", "y")], threaded.Attentions);
        Assert.NotNull(plain);
        Assert.False(plain.SeparatesThreads);
    }

    [Fact]
    public async Task ShouldAnswerNotRunningAndKeepTheInputClosed_AfterTheRunEnded()
    {
        _replay.TryStart(Lines("/help"), closeInputAtEnd: true);
        var ct = TestContext.Current.CancellationToken;
        await _replay.ReadLineAsync(ct);
        Assert.Null(await _replay.ReadLineAsync(ct));

        Assert.Equal(ReplayInterrupt.NotRunning, _replay.Interrupt());
        Assert.Null(CommandRead());
    }
}
