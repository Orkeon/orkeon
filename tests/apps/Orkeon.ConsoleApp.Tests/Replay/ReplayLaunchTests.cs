using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.ConsoleApp.Replay;
using Orkeon.ConsoleApp.Tests.Fakes;

namespace Orkeon.ConsoleApp.Tests.Replay;

/// <summary>
/// <c>orkeon-repl --replay</c>'s plumbing: exit 2 for a file that cannot be read, 0 or 1 under <c>--replay</c>
/// alone, the replay concluded when the REPL throws, and the interrupt keys.
/// </summary>
public sealed class ReplayLaunchTests : IDisposable
{
    private readonly FakeConsoleAdapter _inner = new();
    private readonly FakeFileSystemService _fileSystem = new();
    private readonly ReplayIdleGate _gate = new(() => new CommandInstanceRegistry(), () => null);
    private readonly ReplayConsoleAdapter _replay;
    private readonly StringWriter _stderr = new();

    public ReplayLaunchTests()
    {
        _replay = new ReplayConsoleAdapter(_inner, _gate);
        _fileSystem.AddFile("/workspace/demo.replay", "/help\n");
    }

    public void Dispose()
    {
        _gate.Dispose();
        _stderr.Dispose();
    }

    private static ScriptedCommandsCliOptions Options(params string[] args) => ScriptedCommandsCliOptions.Parse(args);

    private Task<int?> Start(ScriptedCommandsCliOptions options)
        => ReplayLaunch.StartAsync(_replay, _fileSystem, options, _stderr, TestContext.Current.CancellationToken);

    // The REPL's loop, reduced to reading every line (the fake runs no command).
    private async Task ReadAll()
    {
        while (await _replay.ReadLineAsync(TestContext.Current.CancellationToken) is not null)
        {
        }
    }

    [Fact]
    public async Task ShouldExitTwo_WhenTheFileCannotBeRead()
    {
        Assert.Equal(ReplayConsoleAdapter.UnreadableExitCode, await Start(Options("--replay", "/workspace/missing.replay")));

        Assert.Contains("orkeon-repl: replay file not found: /workspace/missing.replay", _stderr.ToString(), StringComparison.Ordinal);
        Assert.False(_replay.IsActive);
    }

    [Fact]
    public async Task ShouldStartTheReplay_WhenTheFileReads()
    {
        Assert.Null(await Start(Options("--replay", "/workspace/demo.replay")));

        Assert.True(_replay.IsActive);
    }

    [Fact]
    public async Task ShouldStartNothing_WithoutReplay()
    {
        Assert.Null(await Start(Options()));

        Assert.False(_replay.IsActive);
    }

    [Fact]
    public async Task ShouldExitZero_WhenEveryLinePassedUnderReplayAlone()
    {
        var options = Options("--replay", "/workspace/demo.replay");
        await Start(options);

        Assert.Equal(ReplayConsoleAdapter.PassedExitCode, await ReplayLaunch.RunAsync(_replay, options, ReadAll));
    }

    [Fact]
    public async Task ShouldExitOne_WhenTheReplayStoppedUnderReplayAlone()
    {
        var options = Options("--replay", "/workspace/demo.replay");
        await Start(options);

        var exitCode = await ReplayLaunch.RunAsync(_replay, options, async () =>
        {
            await _replay.ReadLineAsync(TestContext.Current.CancellationToken);
            _replay.OnCommandOutcome(new Orkeon.Cli.Abstractions.Runners.CommandOutcome(
                "/help", "help", Orkeon.Cli.Abstractions.Runners.CommandOutcomeKind.Failed, "Error: broken"));
            await ReadAll();
        });

        Assert.Equal(ReplayConsoleAdapter.FailedExitCode, exitCode);
    }

    [Fact]
    public async Task ShouldReturnNoExitCode_UnderInteractive()
    {
        var options = Options("--replay", "/workspace/demo.replay", "--interactive");
        await Start(options);

        Assert.Null(await ReplayLaunch.RunAsync(_replay, options, () => _replay.ReadLineAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ShouldConcludeTheReplayAndRethrow_WhenTheReplThrows()
    {
        var options = Options("--replay", "/workspace/demo.replay");
        await Start(options);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => ReplayLaunch.RunAsync(_replay, options, async () =>
        {
            await _replay.ReadLineAsync(TestContext.Current.CancellationToken);
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal("boom", thrown.Message);
        Assert.False(_replay.IsActive);
        Assert.Equal(ReplayConsoleAdapter.FailedExitCode, _replay.ExitCode);
        Assert.True(_inner.Contains("replay stopped at line 1: the REPL failed: boom"));
    }

    [Fact]
    public async Task ShouldKeepTheProcessAndCancelTheCommand_OnTheFirstCtrlC()
    {
        await Start(Options("--replay", "/workspace/demo.replay"));
        var cancelled = 0;

        Assert.True(ReplayLaunch.OnCancelKey(_replay, () => cancelled++));

        Assert.Equal(1, cancelled);
    }

    [Fact]
    public async Task ShouldLetTheProcessEnd_OnASecondCtrlCWhileTheReplayStillRuns()
    {
        await Start(Options("--replay", "/workspace/demo.replay"));
        var cancelled = 0;
        ReplayLaunch.OnCancelKey(_replay, () => cancelled++);

        Assert.False(ReplayLaunch.OnCancelKey(_replay, () => cancelled++));

        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void ShouldLetTheProcessEnd_OnCtrlCWithoutAReplay()
        => Assert.False(ReplayLaunch.OnCancelKey(_replay, () => { }));

    [Fact]
    public async Task ShouldStopTheReplay_OnTheTuiInterruptKey()
    {
        var integration = new TuiIntegration();
        var promptInterrupted = 0;
        integration.PromptTurnRunning = () => false;
        integration.InterruptCurrent = () => promptInterrupted++;
        ReplayLaunch.WireTuiInterrupt(integration, _replay);
        Assert.False(integration.PromptTurnRunning());
        await Start(Options("--replay", "/workspace/demo.replay"));

        Assert.True(integration.PromptTurnRunning());
        integration.InterruptCurrent();

        Assert.Equal(1, promptInterrupted);
        Assert.Equal(ReplayInterrupt.AlreadyStopping, _replay.Interrupt());
    }
}
