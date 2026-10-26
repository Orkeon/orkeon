using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.ConsoleApp.Replay;
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

        Assert.True(_replay.Interrupt());

        Assert.Null(await next.WaitAsync(HangGuard, ct));
        Assert.True(_inner.Contains("replay stopped at line 1: interrupted"));
        Assert.False(_replay.Interrupt());
    }
}
