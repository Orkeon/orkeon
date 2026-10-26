using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;

namespace Orkeon.Studio.Wpf.Tests.Launch;

/// <summary>
/// Refonte lot 12: the Launch screen as a classeur — Team, Options, Command (Expert), Run — each
/// tab with its state, the action bar's readiness, and the run's columns read before the launch.
/// </summary>
public sealed class LaunchTabsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-launch-tabs-{Guid.NewGuid():N}");

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

    [Fact]
    public void Without_a_team_the_team_tab_waits_and_the_launch_says_why_it_is_greyed()
    {
        var (tab, _) = Build(new FakeTargetProbe());

        Assert.Equal(LaunchTabViewModel.TeamTab, tab.ActiveTab);
        Assert.Equal("Expected", tab.TeamTabState);
        Assert.Equal("danger", tab.LaunchReadinessTone);
        Assert.NotNull(tab.LaunchBlockedReason);
        Assert.Equal(tab.LaunchBlockedReason, tab.LaunchReadinessText);
        Assert.Equal("Empty", tab.RunTabState);
    }

    [Fact]
    public void A_chosen_team_checks_its_tab_and_readies_the_launch()
    {
        var (tab, _) = Build(new FakeTargetProbe().WithFile("/crews/team.yaml"));

        tab.Target.Select("/crews/team.yaml");

        Assert.Equal("Done", tab.TeamTabState);
        Assert.Equal("ok", tab.LaunchReadinessTone);
        Assert.Null(tab.LaunchBlockedReason);
        Assert.NotNull(tab.Target.KindDisplay);
    }

    [Fact]
    public void The_command_tab_is_the_experts_and_a_novice_lands_on_the_options()
    {
        var (tab, _) = Build(new FakeTargetProbe());

        tab.ActiveTab = LaunchTabViewModel.CommandTab;
        Assert.Equal(LaunchTabViewModel.OptionsTab, tab.ActiveTab);

        tab.IsExpertMode = true;
        tab.OptionsNextCommand.Execute(null);
        Assert.Equal(LaunchTabViewModel.CommandTab, tab.ActiveTab);

        tab.IsExpertMode = false;
        Assert.Equal(LaunchTabViewModel.OptionsTab, tab.ActiveTab);
        tab.OptionsNextCommand.Execute(null);
        Assert.Equal(LaunchTabViewModel.RunTab, tab.ActiveTab);
    }

    [Fact]
    public async Task The_launch_opens_the_run_tab_and_its_end_marks_it()
    {
        var (tab, launcher) = Build(new FakeTargetProbe().WithFile("/crews/team.yaml"));
        tab.Target.Select("/crews/team.yaml");

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LaunchTabViewModel.RunTab, tab.ActiveTab);
        Assert.Equal("Done", tab.RunTabState);
        Assert.True(tab.IsEditable);
        Assert.Equal("Finished", tab.RunPaneTitle);

        launcher.ExitCode = 1;
        await tab.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Warning", tab.RunTabState);
        Assert.Equal("fail", tab.RunBadgeTone);

        // A Stop reads as cancelled, never as a failure of the team.
        launcher.ExitCode = 130;
        await tab.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Warning", tab.RunTabState);
        Assert.Equal("warn", tab.RunBadgeTone);
        Assert.Equal("Cancelled", tab.RunBadgeText);
        Assert.Equal("Cancelled", tab.RunPaneTitle);
    }

    [Fact]
    public void The_crews_agents_have_their_columns_before_the_launch()
    {
        Directory.CreateDirectory(_root);
        var crew = Path.Combine(_root, "team.yaml");
        File.WriteAllText(crew, "agents:\n  - role: Researcher\n  - role: Writer\n");
        var (tab, _) = Build(new FakeTargetProbe().WithFile(crew));

        tab.Target.Select(crew);

        Assert.Equal(["Researcher", "Writer"], tab.AgentLanes.Select(lane => lane.Name));
        Assert.All(tab.AgentLanes, lane => Assert.Equal("waiting", lane.State));
        Assert.Equal("Team ready: 2 agents, 0 declared folders.", tab.TeamFooter);
        Assert.Equal("Ready to work", tab.RunPaneTitle);
    }

    [Fact]
    public void The_options_tab_lists_the_teams_folders_with_their_rights()
    {
        var team = Path.Combine(_root, "veille");
        Directory.CreateDirectory(team);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
        {
            Name = "Veille",
            Mounts = ["C:/docs:/docs:ro", "C:/out:/output:rw"],
        });
        var (tab, _) = Build(new FakeTargetProbe().WithDirectory(team));

        tab.Target.Select(team);

        Assert.Equal(["/docs", "/output"], tab.FolderRows.Select(row => row.VirtualPath));
        Assert.Equal([false, true], tab.FolderRows.Select(row => row.IsWritable));
        Assert.Equal("/output", tab.ResultVirtualPath);
    }

    [Fact]
    public void Without_model_settings_the_run_follows_the_default_and_names_no_setting()
    {
        var (tab, _) = Build(new FakeTargetProbe().WithFile("/crews/team.yaml"));
        tab.Target.Select("/crews/team.yaml");

        Assert.True(tab.RunUsesDefaultLlm);
        Assert.False(tab.HasLlmProfiles);
        Assert.Empty(tab.LlmProfiles);
    }

    [Fact]
    public void The_journal_enlarges_and_reduces()
    {
        var (tab, _) = Build(new FakeTargetProbe());

        tab.ToggleJournalSizeCommand.Execute(null);
        Assert.True(tab.IsJournalTall);

        tab.ToggleJournalSizeCommand.Execute(null);
        Assert.False(tab.IsJournalTall);
    }
}
