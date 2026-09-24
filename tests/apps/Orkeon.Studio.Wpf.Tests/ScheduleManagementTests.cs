using System.Text.Json;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-27 through the view models: the adoption asks before the schedule is installed, and the
/// card shows what the engine answered — never what the sidecar says — and offers install and
/// stop. The engine is a scripted child: no scheduler is ever touched.
/// </summary>
public sealed class ScheduleManagementTests : IDisposable
{
    private const string OriginalId = "6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-schedule-{Guid.NewGuid():N}");

    private string TeamsRoot => Path.Combine(_root, "teams");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static ProcessOutputLine Out(string json) => ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    private static ProcessOutputLine State(string state, string? reason = null, bool? removed = null)
    {
        var reasonPart = reason is null ? "" : $",\"reason\":\"{reason}\"";
        var removedPart = removed is null ? "" : $",\"removed\":{(removed.Value ? "true" : "false")}";
        return Out($$"""{"v":2,"seq":1,"ts":"t","kind":"schedule.state","path":"p","state":"{{state}}"{{reasonPart}},"expression":"daily@07:30","family":"windows","names":["Orkeon veille"]{{removedPart}}}""");
    }

    private static ProcessOutputLine Refused(string message, string command) =>
        Out($$"""{"v":2,"seq":1,"ts":"t","kind":"error","code":"FORGE-SCHEDULE-REFUSED","message":{{JsonSerializer.Serialize(message)}},"recoverable":true,"command":{{JsonSerializer.Serialize(command)}}}""");

    private static ForgeClient Client(FakeProcessLauncher processes) =>
        new(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled()));

    private string Team(string slug, string? schedule = "daily@07:30", string? id = null)
    {
        var team = Path.Combine(TeamsRoot, slug);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = slug, Profile = "Local", Schedule = schedule });
        if (id is not null)
        {
            File.WriteAllText(Path.Combine(team, ForgeSessionCatalog.TeamRecordFileName), $$"""{"v":1,"id":"{{id}}","slug":"{{slug}}"}""");
        }

        return team;
    }

    private TeamsViewModel Teams(FakeProcessLauncher processes) =>
        new(new TeamsDependencies { TeamsRoot = TeamsRoot, WorkspaceDirectory = _root, Forge = Client(processes) });

    // ── the adoption asks (D-05) ──

    /// <summary>
    /// A scheduled team is adopted, and nothing is installed yet: the wizard asks « Install the
    /// schedule (every day at 07:30)? ». « Install » runs <c>forge schedule</c> on the new team,
    /// and the answer reaches My teams.
    /// </summary>
    [Fact]
    public async Task Adopting_a_scheduled_team_asks_first_and_install_runs_forge_schedule()
    {
        var (vm, processes) = await ReadyToAdoptAsync();
        var promoted = Path.Combine(TeamsRoot, "ma-veille");
        vm.TeamName = "Ma veille";
        vm.ScheduleChoice = 1;
        vm.ScheduleTime = "07:30";
        processes.OutputToEmit.Clear();
        processes.OutputToEmit.Add(Out($$"""{"v":2,"seq":1,"ts":"t","kind":"promoted","path":{{JsonSerializer.Serialize(promoted)}},"launcher":"run.cmd","schedule":"schedule","install":"schtasks …"}"""));

        await vm.SaveTeamCommand.ExecuteAsync();

        // Asked — nothing ran but the promotion.
        Assert.True(vm.ScheduleOffer.IsOpen);
        Assert.Equal("Install the schedule (every day at 07:30)?", vm.ScheduleOffer.Question);
        Assert.Equal("promote", processes.LastRequest!.Arguments[1]);

        TeamScheduleChangedEventArgs? changed = null;
        vm.ScheduleOffer.ScheduleChanged += (_, e) => changed = e;
        processes.OutputToEmit.Clear();
        processes.OutputToEmit.Add(State("installed"));
        await vm.ScheduleOffer.InstallCommand.ExecuteAsync();

        Assert.Equal(["forge", "schedule", promoted, "--events", "jsonl"], processes.LastRequest!.Arguments);
        Assert.False(vm.ScheduleOffer.IsOpen);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.WizardScheduleInstalled], vm.ScheduleOffer.Outcome);
        Assert.Equal(promoted, changed!.Path);
        Assert.Equal(TeamScheduleState.Installed, changed.State);
    }

    /// <summary>« Later » installs nothing, says the team stays installable, and asks the engine nothing.</summary>
    [Fact]
    public async Task Later_installs_nothing_and_says_the_team_stays_installable()
    {
        var (vm, processes) = await AdoptScheduledAsync();
        var launches = processes.Requests.Count;

        vm.ScheduleOffer.LaterCommand.Execute(null);

        Assert.False(vm.ScheduleOffer.IsOpen);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.WizardScheduleDeclined], vm.ScheduleOffer.Outcome);
        Assert.Equal(launches, processes.Requests.Count);
    }

    /// <summary>An install the system refuses says so, with the command a person can run by hand.</summary>
    [Fact]
    public async Task A_refused_install_from_the_wizard_says_what_to_run_by_hand()
    {
        var (vm, processes) = await AdoptScheduledAsync();
        processes.OutputToEmit.Clear();
        processes.OutputToEmit.Add(Refused("The windows scheduler refused: ERROR: Access is denied.", "schtasks /Create /TN \"Orkeon ma-veille\" /XML \"x.xml\" /F"));
        processes.ExitCode = 1;

        await vm.ScheduleOffer.InstallCommand.ExecuteAsync();

        Assert.Contains("Access is denied", vm.ScheduleOffer.Outcome, StringComparison.Ordinal);
        Assert.True(vm.ScheduleOffer.HasManualCommand);
        Assert.Equal("schtasks /Create /TN \"Orkeon ma-veille\" /XML \"x.xml\" /F", vm.ScheduleOffer.ManualCommand);
    }

    /// <summary>A re-adoption whose schedule still stands as declared asks nothing: the line says it stays.</summary>
    [Fact]
    public async Task A_readoption_whose_schedule_stands_asks_nothing()
    {
        var (vm, processes, teamDir) = await ReopenScheduledTeamAsync();
        processes.NextRuns.Enqueue([Out(PromotedLine(teamDir))]);
        processes.NextRuns.Enqueue([State("installed")]);

        await vm.SaveTeamCommand.ExecuteAsync();

        Assert.Equal(["forge", "schedule", teamDir, "--check", "--events", "jsonl"], processes.LastRequest!.Arguments);
        Assert.False(vm.ScheduleOffer.IsOpen);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.WizardScheduleKept], vm.ScheduleOffer.Outcome);
    }

    /// <summary>A re-adoption whose schedule is not installed as declared asks again.</summary>
    [Fact]
    public async Task A_readoption_whose_schedule_is_stale_asks_again()
    {
        var (vm, processes, teamDir) = await ReopenScheduledTeamAsync();
        processes.NextRuns.Enqueue([Out(PromotedLine(teamDir))]);
        processes.NextRuns.Enqueue([State("stale", "changed")]);

        await vm.SaveTeamCommand.ExecuteAsync();

        Assert.True(vm.ScheduleOffer.IsOpen);
    }

    /// <summary>
    /// A re-adoption « on demand » of a scheduled team stops the schedule first — choosing on demand
    /// is the consent — then promotes, and the adopted line says the schedule is stopped.
    /// </summary>
    [Fact]
    public async Task A_readoption_on_demand_stops_the_schedule_before_promoting()
    {
        var (vm, processes, teamDir) = await ReopenScheduledTeamAsync();
        vm.ScheduleChoice = 0;
        processes.NextRuns.Enqueue([State("absent", "not-installed", removed: true)]);
        processes.NextRuns.Enqueue([Out(PromotedLine(teamDir))]);

        await vm.SaveTeamCommand.ExecuteAsync();

        var verbs = processes.Requests.Select(r => r.Arguments[1]).ToList();
        Assert.Equal(["reopen", "resume", "unschedule", "promote"], verbs);
        Assert.Equal(["forge", "unschedule", teamDir, "--events", "jsonl"], processes.Requests[2].Arguments);
        Assert.EndsWith(EnglishStudioStrings.Instance[StudioStringKeys.WizardScheduleStopped], vm.StatusMessage, StringComparison.Ordinal);
        Assert.Null(TeamCatalog.Describe(teamDir).Schedule);
        Assert.False(vm.ScheduleOffer.IsOpen);
    }

    /// <summary>When that schedule cannot be stopped, nothing is re-adopted: the line says why and what to run by hand.</summary>
    [Fact]
    public async Task A_readoption_on_demand_whose_schedule_cannot_be_stopped_is_not_saved()
    {
        var (vm, processes, teamDir) = await ReopenScheduledTeamAsync();
        vm.ScheduleChoice = 0;
        processes.NextRuns.Enqueue([Refused("The windows scheduler refused: ERROR: Access is denied.", "schtasks /Delete /TN \"Orkeon veille-docs\" /F")]);
        processes.ExitCode = 1;

        await vm.SaveTeamCommand.ExecuteAsync();

        Assert.DoesNotContain(processes.Requests, request => request.Arguments[1] == "promote");
        Assert.Contains("Access is denied", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("schtasks /Delete", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Equal("daily@07:30", TeamCatalog.Describe(teamDir).Schedule);
        Assert.Equal(4, vm.MaxStep);
    }

    // ── the card (D-05) ──

    /// <summary>
    /// The card says what the engine answered, never what the sidecar says: nothing claimed before
    /// the check, green once installed, amber when not installed or to reinstall. A refresh lays
    /// the answers back on the rebuilt cards without asking again.
    /// </summary>
    [Fact]
    public async Task The_badge_follows_the_check_and_a_refresh_asks_nothing()
    {
        var installed = Team("installee");
        var absent = Team("absente");
        var stale = Team("perimee");
        Team("a-la-demande", schedule: null);
        var processes = new FakeProcessLauncher();
        var teams = Teams(processes);

        var byName = teams.Teams.ToDictionary(card => card.Slug, StringComparer.Ordinal);
        Assert.Equal("accent", byName["installee"].BadgeTone);
        Assert.False(byName["installee"].HasScheduleStateLine);
        Assert.False(byName["a-la-demande"].HasScheduleRow);

        // Asked in the cards' order: absente, installee, perimee — the on-demand team is not asked.
        processes.NextRuns.Enqueue([State("absent", "not-installed")]);
        processes.NextRuns.Enqueue([State("installed")]);
        processes.NextRuns.Enqueue([State("stale", "moved")]);
        await teams.CheckSchedulesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [absent, installed, stale],
            processes.Requests.Select(request => request.Arguments[2]));
        Assert.All(processes.Requests, request => Assert.Equal("--check", request.Arguments[3]));
        byName = teams.Teams.ToDictionary(card => card.Slug, StringComparer.Ordinal);
        Assert.Equal("ok", byName["installee"].BadgeTone);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.TeamsScheduleInstalled], byName["installee"].ScheduleStateLine);
        Assert.False(byName["installee"].ShowsInstallSchedule);
        Assert.True(byName["installee"].ShowsStopSchedule);
        Assert.Equal("warn", byName["absente"].BadgeTone);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.TeamsScheduleAbsent], byName["absente"].ScheduleStateLine);
        Assert.True(byName["absente"].ShowsInstallSchedule);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.TeamsScheduleStale], byName["perimee"].ScheduleStateLine);
        Assert.True(byName["perimee"].ShowsInstallSchedule);

        teams.Refresh();

        Assert.Equal(3, processes.Requests.Count);
        Assert.Equal("ok", teams.Teams.Single(card => card.Slug == "installee").BadgeTone);
    }

    /// <summary>« Install the schedule » runs <c>forge schedule</c> and shows what the engine answered.</summary>
    [Fact]
    public async Task Install_from_the_card_runs_forge_schedule_and_shows_the_answer()
    {
        var team = Team("veille");
        var processes = new FakeProcessLauncher();
        var teams = Teams(processes);
        processes.NextRuns.Enqueue([State("absent", "not-installed")]);
        await teams.CheckSchedulesAsync(TestContext.Current.CancellationToken);
        var card = Assert.Single(teams.Teams);

        processes.NextRuns.Enqueue([State("installed")]);
        await card.InstallScheduleCommand.ExecuteAsync();

        Assert.Equal(["forge", "schedule", team, "--events", "jsonl"], processes.LastRequest!.Arguments);
        Assert.Equal(TeamScheduleState.Installed, card.ScheduleState);
        Assert.False(card.HasScheduleMessage);
    }

    /// <summary>« Stop the schedule » runs <c>forge unschedule</c>, then the sidecar forgets the schedule: the team is on demand.</summary>
    [Fact]
    public async Task Stop_runs_forge_unschedule_and_removes_the_schedule_from_the_sidecar()
    {
        var team = Team("veille");
        var processes = new FakeProcessLauncher();
        var teams = Teams(processes);
        processes.NextRuns.Enqueue([State("installed")]);
        await teams.CheckSchedulesAsync(TestContext.Current.CancellationToken);

        processes.NextRuns.Enqueue([State("absent", "not-installed", removed: true)]);
        await Assert.Single(teams.Teams).StopScheduleCommand.ExecuteAsync();

        Assert.Equal(["forge", "unschedule", team, "--events", "jsonl"], processes.LastRequest!.Arguments);
        Assert.Null(TeamCatalog.Describe(team).Schedule);
        Assert.Equal("Local", TeamCatalog.Describe(team).Profile);
        var card = Assert.Single(teams.Teams);
        Assert.False(card.IsScheduled);
        Assert.False(card.HasScheduleRow);
        Assert.Equal(TeamScheduleState.Unknown, card.ScheduleState);
    }

    /// <summary>A stop the system refuses changes nothing — the sidecar keeps the schedule — and says what to run by hand.</summary>
    [Fact]
    public async Task A_refused_stop_changes_nothing_and_shows_the_manual_command()
    {
        var team = Team("veille");
        var processes = new FakeProcessLauncher();
        var teams = Teams(processes);
        processes.OutputToEmit.Add(Refused("The linux scheduler refused: Failed to connect to bus", "systemctl --user disable --now orkeon-veille.timer"));
        processes.ExitCode = 1;
        var card = Assert.Single(teams.Teams);

        await card.StopScheduleCommand.ExecuteAsync();

        Assert.Equal("daily@07:30", TeamCatalog.Describe(team).Schedule);
        Assert.Contains("Failed to connect to bus", card.ScheduleMessage, StringComparison.Ordinal);
        Assert.Equal("systemctl --user disable --now orkeon-veille.timer", card.ScheduleManualCommand);
    }

    // ── the shell ──

    /// <summary>
    /// The shell asks the engine at startup where each scheduled team's schedule stands — through
    /// the same binary as the doctor and the launcher — and the card shows the answer.
    /// </summary>
    [Fact]
    public async Task The_shell_checks_every_scheduled_team_at_startup()
    {
        var team = Team("veille");
        var processes = new FakeProcessLauncher();
        processes.OutputToEmit.Add(State("installed"));
        var window = new Orkeon.Studio.Wpf.ViewModels.Shell.MainWindowViewModel(
            new Orkeon.Studio.Wpf.ViewModels.Services.StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                TargetProbe = new FakeTargetProbe(),
                Picker = new FakePathPicker(),
                ProcessRunner = new OrkeonProcessRunner(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
            },
            globalPathOverride: Path.Combine(_root, "appsettings.json"),
            forgeWorkspace: _root,
            teamsRoot: TeamsRoot);

        await window.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Contains(processes.Requests, request =>
            request.Arguments.SequenceEqual(["forge", "schedule", team, "--check", "--events", "jsonl"]));
        Assert.Equal(TeamScheduleState.Installed, Assert.Single(window.Teams.Teams).ScheduleState);
    }

    // ── helpers ──

    private async Task<(CreateTeamViewModel Vm, FakeProcessLauncher Processes)> ReadyToAdoptAsync()
    {
        var (vm, processes, _) = CreateTeamWizardTests.Build(teamsRoot: TeamsRoot, workspace: _root);
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
        ]);
        vm.Need = "une veille documentaire";
        vm.FrequencyChoices[1].SelectCommand.Execute(null);
        vm.SourceChoices[0].SelectCommand.Execute(null);
        vm.OutputChoices[0].SelectCommand.Execute(null);
        await vm.ComposeCommand.ExecuteAsync();
        Assert.Equal(4, vm.Step);
        return (vm, processes);
    }

    private async Task<(CreateTeamViewModel Vm, FakeProcessLauncher Processes)> AdoptScheduledAsync()
    {
        var (vm, processes) = await ReadyToAdoptAsync();
        vm.TeamName = "Ma veille";
        vm.ScheduleChoice = 1;
        vm.ScheduleTime = "07:30";
        processes.OutputToEmit.Clear();
        processes.OutputToEmit.Add(Out(PromotedLine(Path.Combine(TeamsRoot, "ma-veille"))));
        await vm.SaveTeamCommand.ExecuteAsync();
        Assert.True(vm.ScheduleOffer.IsOpen);
        return (vm, processes);
    }

    private static string PromotedLine(string path) =>
        $$"""{"v":2,"seq":1,"ts":"t","kind":"promoted","path":{{JsonSerializer.Serialize(path)}},"launcher":"run.cmd","updated":true}""";

    /// <summary>
    /// A team adopted with daily@07:30, reopened through « Modify »: the engine found its session
    /// (<c>forge reopen</c>), and the wizard resumed it at the Ready pause, seeded from the sidecar.
    /// </summary>
    private async Task<(CreateTeamViewModel Vm, FakeProcessLauncher Processes, string TeamDir)> ReopenScheduledTeamAsync()
    {
        var teamDir = Path.Combine(TeamsRoot, "veille-docs");
        var sessionDir = Path.Combine(_root, ".orkeon", "forge", "veille");
        Directory.CreateDirectory(sessionDir);
        Directory.CreateDirectory(teamDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "session.json"),
            $$"""{"v":1,"id":"{{OriginalId}}","slug":"veille","title":"Veille","format":"yaml","state":"Promoted","status":"Promoted","promotedTo":{{JsonSerializer.Serialize(teamDir)}}}""",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "blueprint.json"),
            """{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_read"]}],"tasks":[{"key":"t","description":"d","agent":"a"}]}""",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(teamDir, "studio-team.json"),
            """{"name":"Veille docs","description":"le besoin d'origine","profile":"Local","schedule":"daily@07:30"}""",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(teamDir, "forge.json"),
            $$"""{"v":1,"id":"{{OriginalId}}","slug":"veille"}""", TestContext.Current.CancellationToken);

        var (vm, processes, _) = CreateTeamWizardTests.Build(teamsRoot: TeamsRoot, workspace: _root);
        processes.NextRuns.Enqueue(
        [
            Out($$"""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","id":"{{OriginalId}}","dir":{{JsonSerializer.Serialize(sessionDir)}},"format":"yaml","resumed":true}"""),
            Out($$"""{"v":2,"seq":2,"ts":"t","kind":"team.reopened","slug":"veille","dir":{{JsonSerializer.Serialize(sessionDir)}},"path":{{JsonSerializer.Serialize(teamDir)}},"state":"promoted","rebuilt":false}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"promoted","exitCode":0}"""),
        ]);
        processes.NextRuns.Enqueue(
        [
            Out($$"""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":{{JsonSerializer.Serialize(sessionDir)}},"format":"yaml","resumed":true}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"stage.entered","stage":"ready","iteration":1}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
        ]);

        await vm.ReopenTeamAsync(TeamCatalog.Describe(teamDir));
        Assert.Equal(1, vm.ScheduleChoice);
        Assert.True(vm.SaveTeamCommand.CanExecute(null));
        return (vm, processes, teamDir);
    }
}
