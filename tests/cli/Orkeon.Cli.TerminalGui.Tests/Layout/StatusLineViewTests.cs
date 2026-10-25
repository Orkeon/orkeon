using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public class StatusLineViewTests
{
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeInteractiveRunner _runner = new();

    private StatusLineView CreateView(IReadOnlyList<string>? bootVerbs = null)
        => new(new TerminalGuiOptions { SpinnerVerbs = bootVerbs }, InlineDispatcher.Instance, _clock);

    [Fact]
    public void ShouldThrow_WhenOptionsOrRunnerAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new StatusLineView(null!, InlineDispatcher.Instance, _clock));
        using var view = CreateView();
        Assert.Throws<ArgumentNullException>(() => view.Bind(null!, null, null));
    }

    [Fact]
    public void ShouldStayBlank_WhenNothingRuns()
    {
        using var view = CreateView();

        view.Bind(_runner, new TuiIntegration(), null);

        Assert.Equal(string.Empty, view.CurrentText);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void ShouldShowTheWaitingApprovalOverTheTurn_WhenABackgroundTaskAsks()
    {
        var integration = new TuiIntegration
        {
            PromptTurnRunning = () => true,
            PendingApprovals = () =>
            [
                new Orkeon.Cli.Abstractions.Runners.PendingToolApproval("read", "t1", "dev-batches"),
                new Orkeon.Cli.Abstractions.Runners.PendingToolApproval("shell_command", "t2", "dev-batches"),
            ],
        };
        using var view = CreateView();
        view.Bind(_runner, integration, null);

        view.Refresh();

        Assert.StartsWith("? [t1] dev-batches asks to run read…", view.CurrentText, StringComparison.Ordinal);
        Assert.Contains("answer y once", view.CurrentText, StringComparison.Ordinal);
        Assert.Contains("+1 more", view.CurrentText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void ShouldFallBackToTheTurn_WhenNoApprovalWaits()
    {
        using var view = CreateView(["Pondering"]);
        view.Bind(_runner, new TuiIntegration { PromptTurnRunning = () => true, PendingApprovals = () => [] }, null);

        view.Refresh();

        Assert.Contains("Pondering", view.CurrentText, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldShowTurnDeltaOfTokensAndElapsedTime_WhenCommandRuns()
    {
        long tokens = 1_000;
        var integration = new TuiIntegration
        {
            SessionTokens = () => tokens,
            SpinnerVerbs = () => ["Pondering"],
        };
        using var view = CreateView();
        view.Bind(_runner, integration, null);

        _runner.Running = true;
        view.Refresh();
        _clock.Advance(TimeSpan.FromSeconds(5));
        tokens = 1_500;
        view.Refresh();

        Assert.Contains("Pondering", view.CurrentText);
        Assert.Contains("5s", view.CurrentText);
        Assert.Contains("500 tokens", view.CurrentText);
        Assert.Contains("working", view.CurrentText);
    }

    [Fact]
    public void ShouldReportStreaming_WhenDeltasFlow()
    {
        using var view = CreateView(["Booting"]);
        view.Bind(_runner, new TuiIntegration { PromptTurnRunning = () => true }, null);

        view.SetStreaming(true);
        view.Refresh();

        Assert.Contains("Booting", view.CurrentText);
        Assert.Contains("streaming", view.CurrentText);
        Assert.DoesNotContain("tokens", view.CurrentText);
    }

    [Fact]
    public void ShouldRaiseTurnCompletedAndClear_WhenRunningTurnEnds()
    {
        using var view = CreateView();
        var completed = 0;
        view.TurnCompleted += (_, _) => completed++;
        view.Bind(_runner, null, null);

        _runner.Running = true;
        view.SetStreaming(true);
        view.Refresh();
        Assert.NotEqual(string.Empty, view.CurrentText);

        _runner.Running = false;
        view.Refresh();
        view.Refresh();

        Assert.Equal(1, completed);
        Assert.Equal(string.Empty, view.CurrentText);

        _runner.Running = true;
        view.Refresh();
        Assert.Contains("working", view.CurrentText);
    }

    [Fact]
    public void ShouldShowProgressBar_WhenLiveProgressIsReportedWhileIdle()
    {
        var started = _clock.GetUtcNow();
        var integration = new TuiIntegration
        {
            Progress = () => new ProgressInfo
            {
                Label = "Compacting conversation",
                Ratio = 0.349,
                StartedAt = started,
                FromLiveInstance = true,
            },
        };
        using var view = CreateView();
        view.Bind(_runner, integration, null);
        _clock.Advance(TimeSpan.FromSeconds(12));
        view.Refresh();

        Assert.Contains("Compacting conversation", view.CurrentText);
        Assert.Contains("34%", view.CurrentText);
        Assert.Contains("12s", view.CurrentText);
    }

    [Fact]
    public void ShouldHideStaleProgress_WhenSnapshotIsNotLiveAndNothingRuns()
    {
        var integration = new TuiIntegration
        {
            Progress = () => new ProgressInfo { Label = "Indexing", Message = "files", StartedAt = _clock.GetUtcNow() },
        };
        using var view = CreateView();
        view.Bind(_runner, integration, null);
        Assert.Equal(string.Empty, view.CurrentText);

        _runner.Running = true;
        view.Refresh();

        Assert.Contains("Indexing", view.CurrentText);
        Assert.Contains("files", view.CurrentText);
    }

    [Fact]
    public void ShouldDegradeGracefully_WhenHostDelegatesThrow()
    {
        var integration = new TuiIntegration
        {
            PromptTurnRunning = () => true,
            Progress = () => throw new InvalidOperationException("progress"),
            SpinnerVerbs = () => throw new InvalidOperationException("verbs"),
            SessionTokens = () => throw new InvalidOperationException("tokens"),
        };
        using var view = CreateView(["Booting"]);

        view.Bind(_runner, integration, null);

        Assert.Contains("Booting", view.CurrentText);
        Assert.DoesNotContain("tokens", view.CurrentText);
    }

    [Fact]
    public void ShouldFallBackToBootVerbs_WhenLiveVerbsAreEmpty()
    {
        var integration = new TuiIntegration { PromptTurnRunning = () => true, SpinnerVerbs = () => [] };
        using var view = CreateView(["Booting"]);

        view.Bind(_runner, integration, null);

        Assert.Contains("Booting", view.CurrentText);
    }
}
