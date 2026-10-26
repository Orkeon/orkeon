using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;

namespace Orkeon.Studio.Wpf.Tests.Launch;

/// <summary>
/// rc.5 harmonisation, lot L08: how the Command and Run tabs show what the launcher already knows —
/// the command split at its options (B09), the agent cards (D03, D05), the finished subtitle
/// (D01), the stale sentences that go once the run was launched (D03) and the journal fold (R16).
/// </summary>
public sealed class RunPanePresentationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-run-pane-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static (LaunchTabViewModel Tab, FakeProcessLauncher Launcher) Build(FakeTargetProbe probe)
    {
        var launcher = new FakeProcessLauncher();
        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = probe,
            Directories = new FakeDirectoryProbe(),
            HistoryStore = new FakeLaunchHistoryStore(),
            SettingsStore = new FakeAppSettingsStore(),
        });
        return (tab, launcher);
    }

    private string WriteCrew(params string[] roles)
    {
        Directory.CreateDirectory(_root);
        var crew = Path.Combine(_root, "team.yaml");
        File.WriteAllText(crew, "agents:\n" + string.Concat(roles.Select(role => $"  - role: {role}\n")));
        return crew;
    }

    private static ProcessOutputLine Completed(string role) => ProcessOutputLine.Now(
        ProcessOutputChannel.StandardOutput,
        $$"""{"v":2,"seq":1,"ts":"t","kind":"task.completed","taskId":"t-{{role}}","agentRole":"{{role}}","success":true,"durationMs":900}""");

    [Fact]
    public void The_command_breaks_only_between_two_options()
    {
        var segments = LaunchTabViewModel.SplitAtOptions(
            @"orkeon run C:\teams\tri --mount C:\data:/factures:rw --allow-external-mounts --events jsonl");

        Assert.Equal(
            [@"orkeon run C:\teams\tri", @"--mount C:\data:/factures:rw", "--allow-external-mounts", "--events jsonl"],
            segments);
    }

    [Fact]
    public void A_quoted_value_is_never_split_and_no_command_is_no_segment()
    {
        Assert.Equal(
            ["orkeon run \"C:\\my --team\"", "--events jsonl"],
            LaunchTabViewModel.SplitAtOptions("orkeon run \"C:\\my --team\" --events jsonl"));
        Assert.Empty(LaunchTabViewModel.SplitAtOptions(null));
        Assert.Empty(LaunchTabViewModel.SplitAtOptions("  "));
    }

    [Fact]
    public void The_segments_follow_the_command_and_lose_nothing_of_it()
    {
        var (tab, _) = Build(new FakeTargetProbe().WithFile("/crews/team.yaml"));

        tab.Target.Select("/crews/team.yaml");

        Assert.NotNull(tab.CommandLinePreview);
        Assert.Equal(tab.CommandLinePreview, string.Join(" ", tab.CommandLineSegments));
        Assert.All(tab.CommandLineSegments.Skip(1), segment => Assert.StartsWith("--", segment, StringComparison.Ordinal));
    }

    [Fact]
    public void Before_a_launch_the_cards_name_the_agents_and_keep_their_identifier()
    {
        var crew = WriteCrew("analyste", "Writer");
        var (tab, _) = Build(new FakeTargetProbe().WithFile(crew));

        tab.Target.Select(crew);

        Assert.Equal(["Analyste", "Writer"], tab.AgentCards.Select(card => card.DisplayName));
        Assert.Equal(["analyste", "Writer"], tab.AgentCards.Select(card => card.Identifier));
        Assert.True(tab.AgentCards[0].HasIdentifier);
        Assert.False(tab.AgentCards[1].HasIdentifier);
        Assert.All(tab.AgentCards, card => Assert.Equal("waiting", card.State));
    }

    [Fact]
    public async Task The_runs_spelling_of_an_agent_is_one_card_and_an_agent_never_started_reads_not_run()
    {
        var crew = WriteCrew("analyste", "redacteur", "relecteur");
        var (tab, launcher) = Build(new FakeTargetProbe().WithFile(crew));
        launcher.OutputToEmit.Add(Completed("Analyste"));
        launcher.OutputToEmit.Add(Completed("Rédacteur"));
        tab.Target.Select(crew);

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Analyste", "Rédacteur", "Relecteur"], tab.AgentCards.Select(card => card.DisplayName));
        Assert.Equal(["analyste", "redacteur", "relecteur"], tab.AgentCards.Select(card => card.Identifier));
        Assert.Equal(["done", "done", "notrun"], tab.AgentCards.Select(card => card.State));
        // The lanes themselves are untouched: the cards are a reading of them.
        Assert.Equal(4, tab.AgentLanes.Count);
    }

    [Fact]
    public async Task While_the_run_lives_its_announcement_and_the_ready_verdict_are_not_said()
    {
        var (tab, launcher) = Build(new FakeTargetProbe().WithFile("/crews/team.yaml"));
        tab.Target.Select("/crews/team.yaml");
        Assert.True(tab.ShowsValidationSummary);
        Assert.False(tab.ShowsFinishedSubtitle);

        bool? statusWhileRunning = null, verdictWhileRunning = null, subtitleWhileRunning = null;
        string? messageWhileRunning = null;
        launcher.WhileRunning = () =>
        {
            messageWhileRunning = tab.StatusMessage;
            statusWhileRunning = tab.ShowsStatusMessage;
            verdictWhileRunning = tab.ShowsValidationSummary;
            subtitleWhileRunning = tab.ShowsFinishedSubtitle;
        };

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(messageWhileRunning));
        Assert.False(statusWhileRunning);
        Assert.False(verdictWhileRunning);
        Assert.False(subtitleWhileRunning);
        Assert.False(tab.ShowsValidationSummary);
        Assert.True(tab.ShowsFinishedSubtitle);
        Assert.Equal(0, tab.ExitCode);
    }

    [Fact]
    public void The_journal_is_open_until_its_fold_closes_it()
    {
        var (tab, _) = Build(new FakeTargetProbe());
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.True(tab.IsJournalOpen);
        tab.IsJournalOpen = false;

        Assert.False(tab.IsJournalOpen);
        Assert.Contains(nameof(LaunchTabViewModel.IsJournalOpen), raised);
    }
}
