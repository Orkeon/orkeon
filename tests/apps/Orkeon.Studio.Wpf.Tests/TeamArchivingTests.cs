using System.Text.Json;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-31 through the view models: an archived team leaves the active list and keeps every
/// link; nothing in Studio relaunches or tests it by mistake — each guard offers the restore
/// instead of refusing in silence — and the gestures follow the rules: a scheduled team stops its
/// schedule first, a team that runs is neither archived nor restored. The screen that shows all
/// this (search, sort, the Archives view, the undo banner) is STUDIO-32's; these pin the model.
/// </summary>
public sealed class TeamArchivingTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-archive-{Guid.NewGuid():N}");

    private string TeamsRoot => Path.Combine(_root, "teams");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string Text(string key) => EnglishStudioStrings.Instance[key];

    private static ProcessOutputLine Out(string json) => ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    private string Team(string slug, string? schedule = null, string? profile = null, params string[] mounts)
    {
        var team = Path.Combine(TeamsRoot, slug);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
        {
            Name = slug,
            Profile = profile,
            Schedule = schedule,
            Mounts = mounts.Length > 0 ? mounts : null,
        });
        return team;
    }

    private TeamsViewModel Teams(FakeProcessLauncher? processes = null, Func<string, TeamActivity>? activityOf = null) =>
        new(new TeamsDependencies
        {
            TeamsRoot = TeamsRoot,
            WorkspaceDirectory = _root,
            LoadSessions = () => [],
            Forge = new ForgeClient(
                processes ?? new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            ActivityOf = activityOf,
            Clock = new StubTimeProvider { Now = Now },
        });

    // ── My teams: out of the active list, back intact ──

    [Fact]
    public async Task An_archived_team_leaves_the_active_list_keeps_its_folder_and_comes_back_intact()
    {
        var veille = Team("veille", profile: "Local", mounts: "./output:/output:rw");
        Team("synthese");
        var teams = Teams();
        var changed = new List<string>();
        teams.ArchiveChanged += (_, e) => changed.AddRange(e.Paths);

        await teams.Teams.Single(card => card.Slug == "veille").ArchiveCommand.ExecuteAsync();

        Assert.Equal(["synthese"], teams.Teams.Select(card => card.Slug));
        Assert.Equal(1, teams.ActiveCount);
        var archived = Assert.Single(teams.ArchivedTeams);
        Assert.True(archived.IsArchived);
        Assert.Equal(Now, archived.Summary.ArchivedAt);
        Assert.Equal(1, teams.ArchivedCount);
        Assert.True(Directory.Exists(veille));
        Assert.Equal([veille], changed);

        archived.RestoreCommand.Execute(null);

        Assert.Empty(teams.ArchivedTeams);
        Assert.Equal(["synthese", "veille"], teams.Teams.Select(card => card.Slug));
        var restored = TeamCatalog.Describe(veille);
        Assert.False(restored.IsArchived);
        Assert.Equal("Local", restored.Profile);
        Assert.Equal(["./output:/output:rw"], restored.Metadata!.Mounts);
        Assert.Equal([veille, veille], changed);
    }

    /// <summary>
    /// D-06: archiving a scheduled team without stopping its schedule is refused — the system would
    /// keep running a team nobody sees — and the card offers « Stop the schedule and archive »,
    /// which runs <c>forge unschedule</c> first, then archives.
    /// </summary>
    [Fact]
    public async Task Archiving_a_scheduled_team_asks_to_stop_its_schedule_first()
    {
        var veille = Team("veille", schedule: "daily@07:30");
        var processes = new FakeProcessLauncher();
        var teams = Teams(processes);
        var card = Assert.Single(teams.Teams);

        await card.ArchiveCommand.ExecuteAsync();

        Assert.Empty(processes.Requests);
        Assert.False(TeamCatalog.Describe(veille).IsArchived);
        Assert.True(card.OffersScheduleStop);
        Assert.Equal(Text(StudioStringKeys.TeamsArchiveScheduled), card.ArchiveNotice);

        processes.OutputToEmit.Add(Out("""{"v":2,"seq":1,"ts":"t","kind":"schedule.state","path":"p","state":"absent","reason":"not-installed","removed":true}"""));
        await card.StopScheduleAndArchiveCommand.ExecuteAsync();

        Assert.Equal(["forge", "unschedule", veille, "--events", "jsonl"], Assert.Single(processes.Requests).Arguments);
        var archived = TeamCatalog.Describe(veille);
        Assert.True(archived.IsArchived);
        Assert.Null(archived.Schedule);
        Assert.Empty(teams.Teams);
        Assert.False(Assert.Single(teams.ArchivedTeams).IsScheduled);
    }

    /// <summary>A schedule the system refuses to stop keeps the team active, the card saying what to run by hand.</summary>
    [Fact]
    public async Task A_schedule_the_system_refuses_to_stop_keeps_the_team_active()
    {
        var veille = Team("veille", schedule: "daily@07:30");
        var processes = new FakeProcessLauncher { ExitCode = 1 };
        processes.OutputToEmit.Add(Out($$"""{"v":2,"seq":1,"ts":"t","kind":"error","code":"FORGE-SCHEDULE-REFUSED","message":"The windows scheduler refused: ERROR: Access is denied.","recoverable":true,"command":{{JsonSerializer.Serialize("schtasks /Delete /TN \"Orkeon veille\" /F")}}}"""));
        var teams = Teams(processes);
        var card = Assert.Single(teams.Teams);

        await card.StopScheduleAndArchiveCommand.ExecuteAsync();

        Assert.False(TeamCatalog.Describe(veille).IsArchived);
        Assert.Equal("daily@07:30", TeamCatalog.Describe(veille).Schedule);
        Assert.Contains("Access is denied", card.ArchiveNotice, StringComparison.Ordinal);
        Assert.Equal("schtasks /Delete /TN \"Orkeon veille\" /F", card.ArchiveManualCommand);
        Assert.Same(card, Assert.Single(teams.Teams));
    }

    /// <summary>D-09: a busy team — running, or open in the wizard — is neither archived nor restored, and the card says why.</summary>
    [Fact]
    public async Task A_busy_team_is_neither_archived_nor_restored()
    {
        var veille = Team("veille");
        var archivedTeam = Team("synthese");
        TeamCatalog.Archive(archivedTeam, Now);
        var busy = new HashSet<string>(StringComparer.Ordinal) { veille, archivedTeam };
        var teams = Teams(activityOf: path => busy.Contains(path) ? TeamActivity.Running : TeamActivity.None);

        var active = Assert.Single(teams.Teams);
        await active.ArchiveCommand.ExecuteAsync();

        Assert.False(TeamCatalog.Describe(veille).IsArchived);
        Assert.Equal(Text(StudioStringKeys.TeamsArchiveBusy), active.ArchiveNotice);

        var archived = Assert.Single(teams.ArchivedTeams);
        archived.RestoreCommand.Execute(null);

        Assert.True(TeamCatalog.Describe(archivedTeam).IsArchived);
        Assert.Equal(Text(StudioStringKeys.TeamsRestoreBusy), archived.ArchiveNotice);
        // One question on screen at a time (STUDIO-32): the second gesture closed the first one's notice.
        Assert.False(active.HasArchiveNotice);
        Assert.Equal(Text(StudioStringKeys.TeamsRestoreBusy), teams.RestoreTeam(archivedTeam));
    }

    /// <summary>D-07: the card's Test icon skips the trial screen's picker, so it asks itself — never a silent refusal.</summary>
    [Fact]
    public void The_test_icon_of_an_archived_team_offers_to_restore_it()
    {
        TeamCatalog.Archive(Team("veille"), Now);
        var teams = Teams();
        var asked = 0;
        teams.TestRequested += (_, _) => asked++;
        var card = Assert.Single(teams.ArchivedTeams);

        card.TestCommand.Execute(null);

        Assert.Equal(0, asked);
        Assert.True(card.OffersRestore);
        Assert.Equal(Text(StudioStringKeys.CommonArchivedTeamRestore), card.ArchiveNotice);

        card.DismissArchiveNoticeCommand.Execute(null);
        Assert.False(card.HasArchiveNotice);
        Assert.False(card.OffersRestore);
    }

    /// <summary>D-05: the card's last activity is the most recent of the sidecar's last run, the history and the promotion.</summary>
    [Fact]
    public async Task The_card_reads_its_last_activity_from_the_sidecar_and_the_history()
    {
        var veille = Team("veille");
        TeamCatalog.UpdateMetadata(veille, current => current with { LastRunAt = Now.AddDays(-40) });
        var store = new FakeLaunchHistoryStore
        {
            History = LaunchHistory.Empty.Add(new LaunchHistoryEntry { Target = veille, StartedAt = Now.AddDays(-2) }),
        };
        var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = TeamsRoot, LoadSessions = () => [], HistoryStore = store });
        Assert.Equal(Now.AddDays(-40), Assert.Single(teams.Teams).LastActivity);

        await teams.LoadLastRunsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Now.AddDays(-2), Assert.Single(teams.Teams).LastActivity);
    }

    // ── the launchers: an archived team is not relaunched by mistake ──

    private static LaunchTabViewModel Launcher(string target, FakeProcessLauncher processes, FakeLaunchHistoryStore? history = null) =>
        new(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = new FakeTargetProbe().WithDirectory(target).WithDirectory(Path.Combine(target, "agents")),
            Directories = new FakeDirectoryProbe(target),
            HistoryStore = history ?? new FakeLaunchHistoryStore(),
            SettingsStore = new FakeAppSettingsStore(),
        });

    /// <summary>D-07: an archived target leaves Run and the dry run off, and the banner offers the restore.</summary>
    [Fact]
    public void An_archived_target_is_not_launched_and_the_banner_restores_it()
    {
        var veille = Team("veille");
        TeamCatalog.Archive(veille, Now);
        var tab = Launcher(veille, new FakeProcessLauncher());

        tab.Target.Select(veille);

        Assert.True(tab.IsTargetArchived);
        Assert.Equal(Text(StudioStringKeys.CommonArchivedTeamRestore), tab.ArchivedTargetMessage);
        Assert.False(tab.RunCommand.CanExecute(null));
        Assert.False(tab.ValidateCommand.CanExecute(null));

        tab.RestoreTargetCommand.Execute(null);

        Assert.False(TeamCatalog.Describe(veille).IsArchived);
        Assert.False(tab.IsTargetArchived);
        Assert.Equal("", tab.ArchivedTargetMessage);
        Assert.True(tab.RunCommand.CanExecute(null));
        Assert.Equal(Text(StudioStringKeys.CommonTeamRestored), tab.StatusMessage);
    }

    /// <summary>
    /// D-07: « Replay » in the History runs the recorded argv in one click, without confirmation — on
    /// an archived team it runs nothing: the entry's card asks « Archived team — restore it? » in
    /// place, and once restored the next « Replay » runs it.
    /// </summary>
    [Fact]
    public async Task Replaying_an_archived_team_proposes_to_restore_it_and_runs_nothing()
    {
        var veille = Team("veille");
        TeamCatalog.Archive(veille, Now);
        var history = new FakeLaunchHistoryStore
        {
            History = LaunchHistory.Empty.Add(LaunchHistoryEntry.Starting(veille, ["run", veille], startedAt: Now.AddDays(-3))),
        };
        var processes = new FakeProcessLauncher();
        var tab = Launcher(veille, processes, history);
        await tab.InitializeAsync(TestContext.Current.CancellationToken);
        var card = Assert.Single(tab.History.Entries);

        card.ReplayCommand.Execute(null);

        Assert.Empty(processes.Requests);
        Assert.True(card.IsOfferingRestore);
        Assert.Equal(Text(StudioStringKeys.CommonArchivedTeamRestore), card.RestoreOffer);
        Assert.Equal(Text(StudioStringKeys.CommonArchivedTeamRestore), tab.StatusMessage);
        // The form was left alone: the offer is on the card, not a half-loaded launch.
        Assert.Null(tab.Target.Target);

        card.RestoreCommand.Execute(null);

        Assert.False(card.IsOfferingRestore);
        Assert.False(TeamCatalog.Describe(veille).IsArchived);
        await tab.ReplayAsync(card.Entry, TestContext.Current.CancellationToken);
        Assert.Equal(["run", veille], Assert.Single(processes.Requests).Arguments);
    }

    // ── the shell: the busy hook, the Test picker, « Used by », the last run ──

    private MainWindowViewModel Shell(FakeProcessLauncher processes, IModelProfileStore? profiles = null)
    {
        var probe = new FakeTargetProbe();
        foreach (var team in Directory.EnumerateDirectories(TeamsRoot))
            probe.WithDirectory(team).WithDirectory(Path.Combine(team, "agents"));

        return new MainWindowViewModel(
            new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe([.. Directory.EnumerateDirectories(TeamsRoot)]),
                TargetProbe = probe,
                Picker = new FakePathPicker(),
                ProcessRunner = new OrkeonProcessRunner(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
                ProfileStore = profiles,
                Clock = new StubTimeProvider { Now = Now },
            },
            globalPathOverride: Path.Combine(_root, "appsettings.json"),
            forgeWorkspace: _root,
            teamsRoot: TeamsRoot);
    }

    /// <summary>
    /// D-09 end to end: while Run runs a team, archiving it is refused — the Launch tab exposes its
    /// running target, the shell's busy hook reads it. Once the run is over the archive goes
    /// through, the real run having stamped the team's last run (D-05); the Test picker drops the
    /// team and the launcher refuses it (D-07, D-08) until its banner restores it.
    /// </summary>
    [Fact]
    public async Task Archiving_is_refused_during_a_run_and_the_screens_follow_the_archive_once_it_is_over()
    {
        var veille = Team("veille");
        Team("synthese");
        var processes = new FakeProcessLauncher();
        var window = Shell(processes);
        window.Launch.Target.Select(veille);
        string? refusal = null;
        processes.WhileRunning = () =>
        {
            Assert.Equal(veille, window.Launch.RunningTarget);
            var card = window.Teams.Teams.Single(c => c.Slug == "veille");
            card.ArchiveCommand.Execute(null);
            refusal = card.ArchiveNotice;
        };

        await window.Launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Text(StudioStringKeys.TeamsArchiveBusy), refusal);
        Assert.False(TeamCatalog.Describe(veille).IsArchived);
        Assert.Null(window.Launch.RunningTarget);
        Assert.NotNull(TeamCatalog.Describe(veille).LastRunAt);
        Assert.Equal(["synthese", "veille"], window.Test.TeamChoices.Select(team => team.Slug));

        processes.WhileRunning = null;
        await window.Teams.Teams.Single(c => c.Slug == "veille").ArchiveCommand.ExecuteAsync();

        Assert.True(TeamCatalog.Describe(veille).IsArchived);
        Assert.Equal(["synthese"], window.Test.TeamChoices.Select(team => team.Slug));
        Assert.True(window.Launch.IsTargetArchived);
        Assert.False(window.Launch.RunCommand.CanExecute(null));

        window.Launch.RestoreTargetCommand.Execute(null);

        Assert.False(TeamCatalog.Describe(veille).IsArchived);
        Assert.Equal(["synthese", "veille"], window.Test.TeamChoices.Select(team => team.Slug));
        Assert.Equal(["synthese", "veille"], window.Teams.Teams.Select(card => card.Slug));
        Assert.True(window.Launch.RunCommand.CanExecute(null));
    }

    /// <summary>D-09: the wizard open on a team makes it busy too.</summary>
    [Fact]
    public async Task A_team_open_in_the_wizard_is_not_archived()
    {
        var veille = Team("veille");
        Directory.CreateDirectory(Path.Combine(veille, "crew"));
        await File.WriteAllTextAsync(Path.Combine(veille, "crew", "config.yaml"), "name: veille\n", TestContext.Current.CancellationToken);
        var processes = new FakeProcessLauncher();
        var window = Shell(processes);
        var sessionDir = Path.Combine(_root, ".orkeon", "forge", "veille");
        processes.OutputToEmit.AddRange(
        [
            Out($$"""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","dir":{{JsonSerializer.Serialize(sessionDir)}},"format":"yaml","resumed":false}"""),
            Out($$"""{"v":2,"seq":2,"ts":"t","kind":"team.reopened","slug":"veille","dir":{{JsonSerializer.Serialize(sessionDir)}},"path":{{JsonSerializer.Serialize(veille)}},"state":"test","rebuilt":true,"brief":"derived"}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);
        await window.CreateTeam.ReopenTeamAsync(TeamCatalog.Describe(veille));
        Assert.Equal(veille, window.CreateTeam.ReopenedTeamPath);

        var card = Assert.Single(window.Teams.Teams);
        await card.ArchiveCommand.ExecuteAsync();

        Assert.False(TeamCatalog.Describe(veille).IsArchived);
        Assert.Equal(Text(StudioStringKeys.TeamsArchiveBusy), card.ArchiveNotice);
    }

    /// <summary>
    /// D-08: « Used by » counts every team — an archived one still names its profile and its
    /// folders, and removing either would break it the day it is restored — while the settings'
    /// team folders list it with its state.
    /// </summary>
    [Fact]
    public async Task Used_by_counts_the_archived_teams_and_the_settings_say_which_they_are()
    {
        var id = Orkeon.Domain.Common.MountId.Create();
        var veille = Team("veille", profile: "Local", mounts: [$"{id}|/data/docs:/docs:ro", "./output:/output:rw"]);
        TeamCatalog.Archive(veille, Now);
        var store = new InMemoryModelProfileStore();
        await store.SaveAsync(new ModelProfileSet
        {
            Profiles = [new ModelProfile { Name = "Local", Provider = "ollama", BaseUrl = "http://localhost:11434", Model = "phi3" }],
            DefaultProfile = "Local",
        }, TestContext.Current.CancellationToken);
        var window = Shell(new FakeProcessLauncher(), store);

        await window.Settings.Profiles.InitializeAsync(TestContext.Current.CancellationToken);
        window.Config.Mounts.Load([$"{id}|/data/docs:/docs:ro"]);

        Assert.Empty(window.Teams.Teams);
        Assert.Equal(["veille"], Assert.Single(window.Settings.Profiles.Profiles).UsedByTeams);
        Assert.Equal(["veille"], Assert.Single(window.Config.Mounts.Mounts).UsedByTeams);
        var row = window.Settings.TeamFolders.Rows.Single(r => r.VirtualPath == "/output");
        Assert.True(row.IsArchived);
        Assert.Equal("veille (archived) · /output → output", row.Label);
    }
}
