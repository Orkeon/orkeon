using System.Text.Json;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// « Rename » on a team card (STUDIO-28): an editor in place of the action row (D-07), refused
/// while Studio runs the team, tests it or has it open in the wizard (D-02) and when the new name's
/// folder is taken (D-03); otherwise <c>forge rename</c> in the workshop's workspace (D-01), then
/// the launch history follows the folder so the card keeps its last run (D-04), and an allowed
/// folder the settings declare inside the former folder is said, never rewritten (D-05). The engine
/// is a scripted child that moves the folder the way the real one does.
/// </summary>
public sealed class TeamRenameTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-rename-{Guid.NewGuid():N}");

    private string TeamsRoot => Path.Combine(_root, "teams");

    private string Team => Path.Combine(TeamsRoot, "ma-veille");

    private string Renamed => Path.Combine(TeamsRoot, "veille-du-matin");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static ProcessOutputLine Out(string json) => ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    private static string Text(string key) => EnglishStudioStrings.Instance[key];

    // ── the gesture ──

    /// <summary>
    /// D-07: « Rename » opens the editor on the team's name in place of the action row — one
    /// question at a time: a delete banner closes it, and it closes a delete banner.
    /// </summary>
    [Fact]
    public void Rename_opens_an_editor_in_place_of_the_row_one_question_at_a_time()
    {
        SeedTeam(Team, "Ma veille");
        SeedTeam(Path.Combine(TeamsRoot, "synthese"), "Synthèse");
        var teams = Teams(new FakeProcessLauncher());
        var card = teams.Teams.Single(c => c.Summary.Path == Team);
        var other = teams.Teams.Single(c => c.Summary.Path != Team);

        card.RenameCommand.Execute(null);
        Assert.True(card.IsRenaming);
        Assert.False(card.IsIdle);
        Assert.Equal("Ma veille", card.RenameText);

        other.AskDeleteCommand.Execute(null);
        Assert.False(card.IsRenaming);
        Assert.True(other.IsConfirmingDelete);

        card.RenameCommand.Execute(null);
        Assert.False(other.IsConfirmingDelete);
        card.RenameText = "  ";
        Assert.False(card.ConfirmRenameCommand.CanExecute(null));

        card.CancelRenameCommand.Execute(null);
        Assert.False(card.IsRenaming);
        Assert.True(card.IsIdle);
    }

    /// <summary>
    /// D-01: the engine renames — <c>forge rename</c> on the folder, with the name as typed, in the
    /// workshop's workspace where the linked session lives — and the card is the team where it is
    /// now, the line saying its new name and folder.
    /// </summary>
    [Fact]
    public async Task Rename_asks_the_engine_and_the_card_follows_the_team()
    {
        SeedTeam(Team, "Ma veille");
        var processes = EngineRenaming(Team, Renamed, "Veille du matin");
        var teams = Teams(processes);
        TeamRenamedEventArgs? announced = null;
        teams.TeamRenamed += (_, e) => announced = e;
        var card = Assert.Single(teams.Teams);

        card.RenameCommand.Execute(null);
        card.RenameText = "Veille du matin";
        await card.ConfirmRenameCommand.ExecuteAsync();

        var request = Assert.Single(processes.Requests);
        Assert.Equal(["forge", "rename", Team, "--name", "Veille du matin", "--events", "jsonl"], request.Arguments);
        Assert.Equal(_root, request.WorkingDirectory);
        var renamed = Assert.Single(teams.Teams);
        Assert.Equal(Renamed, renamed.Summary.Path);
        Assert.Equal("Veille du matin", renamed.Name);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, EnglishStudioStrings.Instance[StudioStringKeys.TeamsRenamed], "Veille du matin", "veille-du-matin"), teams.StatusMessage);
        Assert.Equal(Team, announced!.From);
        Assert.Equal(Renamed, announced.Path);
    }

    /// <summary>A name the team already has renames nothing: the editor closes, the engine is not asked.</summary>
    [Fact]
    public async Task The_name_the_team_already_has_renames_nothing()
    {
        SeedTeam(Team, "Ma veille");
        var processes = new FakeProcessLauncher();
        var card = Assert.Single(Teams(processes).Teams);

        card.RenameCommand.Execute(null);
        card.RenameText = " Ma veille ";
        await card.ConfirmRenameCommand.ExecuteAsync();

        Assert.False(card.IsRenaming);
        Assert.Empty(processes.Requests);
    }

    // ── what refuses it ──

    /// <summary>
    /// D-03: the new name's folder already holds another team: said on the card with that team's
    /// name — the wizard's own words for the same collision — and the engine is not asked.
    /// </summary>
    [Fact]
    public async Task A_taken_name_is_refused_on_the_card_before_the_engine_is_asked()
    {
        SeedTeam(Team, "Ma veille");
        SeedTeam(Renamed, "La veille du matin");
        var processes = new FakeProcessLauncher();
        var card = Teams(processes).Teams.Single(c => c.Summary.Path == Team);

        card.RenameCommand.Execute(null);
        card.RenameText = "Veille du matin";
        await card.ConfirmRenameCommand.ExecuteAsync();

        Assert.Equal(
            string.Format(System.Globalization.CultureInfo.CurrentCulture, EnglishStudioStrings.Instance[StudioStringKeys.WizardNameTakenTeam], "La veille du matin", "veille-du-matin"),
            card.RenameRefusal);
        Assert.True(card.IsRenaming);
        Assert.Empty(processes.Requests);
    }

    /// <summary>The engine refuses — whatever it did is undone on its side: the card says why, and stays where it is.</summary>
    [Fact]
    public async Task The_engines_refusal_is_said_on_the_card_and_nothing_moves()
    {
        SeedTeam(Team, "Ma veille");
        var processes = new FakeProcessLauncher { ExitCode = 1 };
        processes.OutputToEmit.Add(Out("""{"v":2,"seq":1,"ts":"t","kind":"error","code":"FORGE-RENAME-FAILED","message":"The team was not renamed: the disk refused. Everything is as it was.","recoverable":true}"""));
        var teams = Teams(processes);
        var card = Assert.Single(teams.Teams);

        card.RenameCommand.Execute(null);
        card.RenameText = "Veille du matin";
        await card.ConfirmRenameCommand.ExecuteAsync();

        Assert.Equal(
            string.Format(System.Globalization.CultureInfo.CurrentCulture, EnglishStudioStrings.Instance[StudioStringKeys.TeamsRenameFailed],
                "The team was not renamed: the disk refused. Everything is as it was."),
            card.RenameRefusal);
        Assert.True(card.IsRenaming);
        Assert.Same(card, Assert.Single(teams.Teams));
    }

    /// <summary>
    /// D-02, through the shell's own wiring: while the Run screen runs the team, while the Test
    /// screen does, and while « Modify » has it open in the wizard, the rename is refused with the
    /// reason — and the engine is never asked to move a folder something is using.
    /// </summary>
    [Fact]
    public async Task Rename_is_refused_while_a_launcher_runs_the_team_or_the_wizard_has_it_open()
    {
        SeedTeam(Team, "Ma veille");
        var processes = new FakeProcessLauncher();
        var window = Shell(processes, new FakeTargetProbe().WithDirectory(Team).WithDirectory(Path.Combine(Team, "agents")));
        var card = Assert.Single(window.Teams.Teams);
        card.RenameCommand.Execute(null);
        card.RenameText = "Veille du matin";

        processes.WhileRunning = () => _ = card.ConfirmRenameCommand.ExecuteAsync();
        window.Launch.Target.Select(Team);
        await window.Launch.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Text(StudioStringKeys.TeamsRenameBusyRunning), card.RenameRefusal);

        window.Test.Launcher.Target.Select(Team);
        await window.Test.Launcher.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Text(StudioStringKeys.TeamsRenameBusyTesting), card.RenameRefusal);

        processes.WhileRunning = null;
        processes.NextRuns.Enqueue(ReopenedAtTheDryPause(Team));
        await window.CreateTeam.ReopenTeamAsync(TeamCatalog.Describe(Team));
        await card.ConfirmRenameCommand.ExecuteAsync();
        Assert.Equal(Text(StudioStringKeys.TeamsRenameBusyWizard), card.RenameRefusal);

        Assert.DoesNotContain(processes.Requests, request => request.Arguments.Count > 1 && request.Arguments[1] == "rename");
        Assert.True(Directory.Exists(Team));
    }

    // ── what follows the folder ──

    /// <summary>
    /// D-04: the launch history is rewritten — the renamed card keeps its last run, and the entry
    /// replays where the team now is: its target, working directory and arguments name the new folder.
    /// </summary>
    [Fact]
    public async Task The_renamed_card_keeps_its_last_run_and_its_launches_replay_where_it_is()
    {
        SeedTeam(Team, "Ma veille");
        var startedAt = new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
        var history = new FakeLaunchHistoryStore
        {
            History = LaunchHistory.Empty.Add(LaunchHistoryEntry.Starting(
                    Team,
                    ["run", Team, "--mount", $"{Path.Combine(Team, "output")}:/output:rw"],
                    workingDirectory: Team,
                    startedAt: startedAt)
                .WithResult(ProcessRunResult.FromExitCode(0, TimeSpan.FromSeconds(3)))),
        };
        var teams = Teams(EngineRenaming(Team, Renamed, "Veille du matin"), history);
        await teams.LoadLastRunsAsync(TestContext.Current.CancellationToken);
        var card = Assert.Single(teams.Teams);
        Assert.Equal(startedAt, card.LastRun);

        card.RenameCommand.Execute(null);
        card.RenameText = "Veille du matin";
        await card.ConfirmRenameCommand.ExecuteAsync();

        Assert.Equal(startedAt, Assert.Single(teams.Teams).LastRun);
        var entry = Assert.Single(history.History.Entries);
        Assert.Equal(Renamed, entry.Target);
        Assert.Equal(Renamed, entry.WorkingDirectory);
        Assert.Equal(["run", Renamed, "--mount", $"{Path.Combine(Renamed, "output")}:/output:rw"], entry.Arguments);
    }

    /// <summary>
    /// D-05: a folder the settings allow by an absolute path inside the former folder pointed into
    /// the team and now points nowhere. It is said — never rewritten: the settings are the user's.
    /// A team-relative folder needs nothing, and a folder elsewhere is not the team's business.
    /// </summary>
    [Fact]
    public async Task An_absolute_allowed_folder_inside_the_team_is_said_never_rewritten()
    {
        SeedTeam(Team, "Ma veille");
        var inside = Path.Combine(Team, "docs");
        var elsewhere = Path.Combine(_root, "ailleurs");
        IReadOnlyList<string> declared = [$"{inside}:/docs:ro", $"{elsewhere}:/ailleurs:ro"];
        var teams = new TeamsViewModel(new TeamsDependencies
        {
            TeamsRoot = TeamsRoot,
            WorkspaceDirectory = _root,
            LoadSessions = () => [],
            Forge = Client(EngineRenaming(Team, Renamed, "Veille du matin")),
            DeclaredMounts = () => declared,
        });
        var card = Assert.Single(teams.Teams);

        card.RenameCommand.Execute(null);
        card.RenameText = "Veille du matin";
        await card.ConfirmRenameCommand.ExecuteAsync();

        var warning = string.Format(System.Globalization.CultureInfo.CurrentCulture, EnglishStudioStrings.Instance[StudioStringKeys.TeamsRenameStrandedFolders], inside);
        Assert.EndsWith(warning, teams.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(elsewhere, teams.StatusMessage, StringComparison.Ordinal);
        Assert.Equal([$"{inside}:/docs:ro", $"{elsewhere}:/ailleurs:ro"], declared);
    }

    /// <summary>
    /// The shell: once renamed, a launcher aimed at the former folder is aimed at the new one, and
    /// the history the Run screen lists is the rewritten one.
    /// </summary>
    [Fact]
    public async Task A_launcher_aimed_at_the_former_folder_follows_the_team()
    {
        SeedTeam(Team, "Ma veille");
        var history = new FakeLaunchHistoryStore
        {
            History = LaunchHistory.Empty.Add(LaunchHistoryEntry.Starting(Team, ["run", Team], workingDirectory: Team)),
        };
        var processes = EngineRenaming(Team, Renamed, "Veille du matin");
        var targets = new FakeTargetProbe()
            .WithDirectory(Team).WithDirectory(Path.Combine(Team, "agents"))
            .WithDirectory(Renamed).WithDirectory(Path.Combine(Renamed, "agents"));
        var window = Shell(processes, targets, history);
        window.Launch.Target.Select(Team);
        var card = Assert.Single(window.Teams.Teams);

        card.RenameCommand.Execute(null);
        card.RenameText = "Veille du matin";
        await card.ConfirmRenameCommand.ExecuteAsync();

        Assert.Equal(Renamed, window.Launch.Target.SelectedPath);
        Assert.Equal(Renamed, Assert.Single(window.Launch.History.Entries).Target);
    }

    // ── the wizard (D-06) ──

    /// <summary>
    /// D-06: in the wizard « Modify » reopened, the name stays the team's title — the re-adoption
    /// writes back into the same folder — and the step says the folder is renamed from My teams.
    /// A new team has no such line: its folder is its name's.
    /// </summary>
    [Fact]
    public async Task The_wizard_points_to_rename_only_for_a_team_it_reopened()
    {
        SeedTeam(Team, "Ma veille");
        var (vm, processes, _) = CreateTeamWizardTests.Build(teamsRoot: TeamsRoot, workspace: _root);
        Assert.False(vm.IsModifyingTeam);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        processes.NextRuns.Enqueue(ReopenedAtTheDryPause(Team));

        await vm.ReopenTeamAsync(TeamCatalog.Describe(Team));

        Assert.True(vm.IsModifyingTeam);
        Assert.Contains(nameof(CreateTeamViewModel.IsModifyingTeam), raised);
    }

    // ── helpers ──

    private static void SeedTeam(string directory, string name)
    {
        TeamCatalog.SaveMetadata(directory, new StudioTeamMetadata { Name = name });
        Directory.CreateDirectory(Path.Combine(directory, "crew"));
        File.WriteAllText(Path.Combine(directory, "crew", "config.yaml"), "name: veille\n");
    }

    private static ForgeClient Client(FakeProcessLauncher processes) =>
        new(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled()));

    private TeamsViewModel Teams(FakeProcessLauncher processes, FakeLaunchHistoryStore? history = null) =>
        new(new TeamsDependencies
        {
            TeamsRoot = TeamsRoot,
            WorkspaceDirectory = _root,
            LoadSessions = () => [],
            Forge = Client(processes),
            HistoryStore = history,
        });

    /// <summary>
    /// The engine, scripted the way <c>forge rename</c> behaves when it succeeds: the folder moves,
    /// the sidecar takes the name, and <c>team.renamed</c> says where the team is now.
    /// </summary>
    private static FakeProcessLauncher EngineRenaming(string from, string to, string name)
    {
        var processes = new FakeProcessLauncher();
        processes.WhileRunning = () =>
        {
            Directory.Move(from, to);
            TeamCatalog.SaveMetadata(to, TeamCatalog.Describe(to).Metadata! with { Name = name });
        };
        processes.OutputToEmit.Add(Out(
            $$"""{"v":2,"seq":1,"ts":"t","kind":"team.renamed","from":{{JsonSerializer.Serialize(from)}},"path":{{JsonSerializer.Serialize(to)}},"name":{{JsonSerializer.Serialize(name)}}}"""));
        return processes;
    }

    /// <summary>The engine's answer to <c>forge reopen</c> on a team with no session: one rebuilt, at the dry pause.</summary>
    private IReadOnlyList<ProcessOutputLine> ReopenedAtTheDryPause(string team)
    {
        var sessionDir = Path.Combine(_root, ".orkeon", "forge", "ma-veille");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "session.json"),
            """{"v":1,"slug":"ma-veille","title":"Ma veille","format":"yaml","state":"Test","status":"Active"}""");
        File.WriteAllText(Path.Combine(sessionDir, "blueprint.json"),
            """{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":[]}],"tasks":[{"key":"t","description":"d","agent":"a"}]}""");
        return
        [
            Out($$"""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"ma-veille","dir":{{JsonSerializer.Serialize(sessionDir)}},"format":"yaml","resumed":false}"""),
            Out($$"""{"v":2,"seq":2,"ts":"t","kind":"team.reopened","slug":"ma-veille","dir":{{JsonSerializer.Serialize(sessionDir)}},"path":{{JsonSerializer.Serialize(team)}},"state":"test","rebuilt":true,"brief":"derived"}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ];
    }

    private MainWindowViewModel Shell(FakeProcessLauncher processes, FakeTargetProbe targets, FakeLaunchHistoryStore? history = null) => new(
        new Orkeon.Studio.Wpf.ViewModels.Services.StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
            TargetProbe = targets,
            Picker = new FakePathPicker(),
            ProcessRunner = new OrkeonProcessRunner(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            HistoryStore = history ?? new FakeLaunchHistoryStore(),
        },
        globalPathOverride: Path.Combine(_root, "appsettings.json"),
        forgeWorkspace: _root,
        teamsRoot: TeamsRoot);
}
