using System.Text.Json;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The « busy team » seam (STUDIO-28, D-02 — shared with STUDIO-31, D-09): what Studio is doing
/// with a team folder at the moment a gesture wants to move or hide it. Both launchers expose the
/// target of their run in flight — never the picker, which stays live during a run — and the
/// shell answers from them and from the team the wizard reopened.
/// </summary>
public sealed class TeamActivityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-activity-{Guid.NewGuid():N}");

    private string TeamsRoot => Path.Combine(_root, "teams");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static ProcessOutputLine Out(string json) => ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    // ── the rule ──

    /// <summary>
    /// A run target is the team's when it is the folder itself or anything under it — the launcher
    /// takes a crew file as readily as the folder — and a sibling sharing its prefix is another team.
    /// </summary>
    [Fact]
    public void A_target_inside_the_team_folder_makes_it_busy_and_a_sibling_does_not()
    {
        var team = Path.Combine(TeamsRoot, "veille");

        Assert.Equal(TeamActivity.Running, TeamActivities.Of(team, team, null, null));
        Assert.Equal(TeamActivity.Running, TeamActivities.Of(team, Path.Combine(team, "crew", "config.yaml"), null, null));
        Assert.Equal(TeamActivity.Running, TeamActivities.Of(team + Path.DirectorySeparatorChar, team, null, null));
        Assert.Equal(TeamActivity.None, TeamActivities.Of(team, Path.Combine(TeamsRoot, "veille-2"), null, null));
        Assert.Equal(TeamActivity.None, TeamActivities.Of(team, null, null, null));
    }

    /// <summary>The Test screen and the wizard answer in their own words, and a run answers first.</summary>
    [Fact]
    public void Each_holder_is_named_and_a_run_answers_first()
    {
        var team = Path.Combine(TeamsRoot, "veille");
        var other = Path.Combine(TeamsRoot, "autre");

        Assert.Equal(TeamActivity.Testing, TeamActivities.Of(team, other, team, null));
        Assert.Equal(TeamActivity.OpenInWizard, TeamActivities.Of(team, other, other, team));
        Assert.Equal(TeamActivity.Running, TeamActivities.Of(team, team, team, team));
        Assert.Equal(TeamActivity.Testing, TeamActivities.Of(team, null, team, team));
    }

    /// <summary>A screen wired without the hook answers that no team is ever busy.</summary>
    [Fact]
    public void An_unwired_screen_says_no_team_is_busy_and_a_wired_one_relays_the_hook()
    {
        var team = Path.Combine(TeamsRoot, "veille");

        Assert.Equal(TeamActivity.None, new TeamsViewModel(new TeamsDependencies { TeamsRoot = TeamsRoot, LoadSessions = () => [] }).ActivityOf(team));

        var wired = new TeamsViewModel(new TeamsDependencies
        {
            TeamsRoot = TeamsRoot,
            LoadSessions = () => [],
            ActivityOf = path => path == team ? TeamActivity.Testing : TeamActivity.None,
        });
        Assert.Equal(TeamActivity.Testing, wired.ActivityOf(team));
        Assert.Equal(TeamActivity.None, wired.ActivityOf(Path.Combine(TeamsRoot, "autre")));
    }

    // ── the launcher ──

    /// <summary>
    /// The launcher says what its run in flight targets — the path the run started on, even when
    /// the picker moves meanwhile — and nothing once it is over.
    /// </summary>
    [Fact]
    public async Task The_launcher_names_the_target_of_its_run_in_flight_and_nothing_after()
    {
        var launcher = new FakeProcessLauncher();
        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = new FakeTargetProbe().WithFile("/teams/veille/crew.yaml").WithFile("/teams/autre/crew.yaml"),
            Directories = new FakeDirectoryProbe(),
            SettingsStore = new FakeAppSettingsStore(),
        });
        tab.Target.Select("/teams/veille/crew.yaml");
        Assert.Null(tab.RunningTarget);

        string? whileRunning = null;
        string? afterThePickerMoved = null;
        launcher.WhileRunning = () =>
        {
            whileRunning = tab.RunningTarget;
            tab.Target.Select("/teams/autre/crew.yaml");
            afterThePickerMoved = tab.RunningTarget;
        };

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/teams/veille/crew.yaml", whileRunning);
        Assert.Equal("/teams/veille/crew.yaml", afterThePickerMoved);
        Assert.Null(tab.RunningTarget);
    }

    // ── the shell ──

    /// <summary>The shell's hook reads the Launch screen's run in flight, then the Test screen's.</summary>
    [Fact]
    public async Task The_shell_says_a_team_is_running_or_under_test_while_either_launcher_runs_it()
    {
        var team = Team("veille");
        var processes = new FakeProcessLauncher();
        var window = Shell(processes, new FakeTargetProbe().WithDirectory(team).WithDirectory(Path.Combine(team, "agents")));
        Assert.Equal(TeamActivity.None, window.Teams.ActivityOf(team));

        TeamActivity? duringTheLaunch = null;
        processes.WhileRunning = () => duringTheLaunch = window.Teams.ActivityOf(team);
        window.Launch.Target.Select(team);
        await window.Launch.RunAsync(TestContext.Current.CancellationToken);

        TeamActivity? duringTheTest = null;
        processes.WhileRunning = () => duringTheTest = window.Teams.ActivityOf(team);
        window.Test.Launcher.Target.Select(team);
        await window.Test.Launcher.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TeamActivity.Running, duringTheLaunch);
        Assert.Equal(TeamActivity.Testing, duringTheTest);
        Assert.Equal(TeamActivity.None, window.Teams.ActivityOf(team));
    }

    /// <summary>The shell's hook says a team the wizard reopened with « Modify » is open there, and only that one.</summary>
    [Fact]
    public async Task The_shell_says_a_team_reopened_in_the_wizard_is_open_there()
    {
        var team = Team("veille");
        var other = Team("autre");
        var sessionDir = Path.Combine(_root, ".orkeon", "forge", "veille");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "session.json"),
            """{"v":1,"slug":"veille","title":"Veille","format":"yaml","state":"Test","status":"Active"}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "blueprint.json"),
            """{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":[]}],"tasks":[{"key":"t","description":"d","agent":"a"}]}""", TestContext.Current.CancellationToken);
        var processes = new FakeProcessLauncher();
        processes.NextRuns.Enqueue(
        [
            Out($$"""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":{{JsonSerializer.Serialize(sessionDir)}},"format":"yaml","resumed":false}"""),
            Out($$"""{"v":2,"seq":2,"ts":"t","kind":"team.reopened","slug":"veille","dir":{{JsonSerializer.Serialize(sessionDir)}},"path":{{JsonSerializer.Serialize(team)}},"state":"test","rebuilt":true,"brief":"derived"}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);
        var window = Shell(processes, new FakeTargetProbe());

        await window.CreateTeam.ReopenTeamAsync(TeamCatalog.Describe(team));

        Assert.Equal(TeamActivity.OpenInWizard, window.Teams.ActivityOf(team));
        Assert.Equal(TeamActivity.None, window.Teams.ActivityOf(other));
    }

    private string Team(string slug)
    {
        var team = Path.Combine(TeamsRoot, slug);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = slug });
        Directory.CreateDirectory(Path.Combine(team, "crew"));
        File.WriteAllText(Path.Combine(team, "crew", "config.yaml"), "name: " + slug + "\n");
        return team;
    }

    private MainWindowViewModel Shell(FakeProcessLauncher processes, FakeTargetProbe targets) => new(
        new Orkeon.Studio.Wpf.ViewModels.Services.StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
            TargetProbe = targets,
            Picker = new FakePathPicker(),
            ProcessRunner = new OrkeonProcessRunner(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            HistoryStore = new FakeLaunchHistoryStore(),
        },
        globalPathOverride: Path.Combine(_root, "appsettings.json"),
        forgeWorkspace: _root,
        teamsRoot: TeamsRoot);
}
