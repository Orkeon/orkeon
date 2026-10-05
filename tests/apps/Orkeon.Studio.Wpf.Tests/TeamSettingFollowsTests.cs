using Orkeon.Domain.FileSystem;
using System.Text.Json;
using Orkeon.Domain.Common;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;
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
/// STUDIO-52, on the window over a test disk: a team keeps its model setting and its folders — a
/// renamed setting carries its teams, a setting absent from this machine or offered to no crew is
/// said where the team's setting is shown, an imported team's launchers are written for this
/// machine, and a gesture that makes a team or a run depend on the settings file saves what the
/// file as saved lacks. Never a key, never a folder of the disk on a line.
/// </summary>
public sealed class TeamSettingFollowsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-setting-follows-" + Guid.NewGuid().ToString("N"));
    private readonly string _teamsRoot;
    private readonly string _settingsPath;
    private readonly string _docs;
    private readonly FakeAppSettingsStore _store = new();
    private readonly FakeProcessLauncher _processes = new();
    private readonly FakeTargetProbe _targets = new();

    private static readonly ModelProfile DeepSeek = new()
    {
        Name = "DeepSeek", Provider = "DeepSeek", BaseUrl = "https://api.deepseek.com",
        Model = "deepseek-v4-flash", KeyEnvName = "DEEPSEEK_API_KEY", TimeoutSeconds = 600,
    };

    private static readonly ModelProfile Zai = new()
    {
        Name = "Z.AI", Provider = "Z.AI", BaseUrl = "https://api.z.ai/api/paas/v4",
        Model = "glm-5.2", KeyEnvName = "ZAI_API_KEY",
    };

    /// <summary>A setting that names no provider — « None » in the editor: from Studio, an echo.</summary>
    private static readonly ModelProfile NoModel = new() { Name = "Sans modèle", Provider = "None" };

    public TeamSettingFollowsTests()
    {
        _teamsRoot = Path.Combine(_root, "teams");
        _settingsPath = Path.Combine(_root, "config", "appsettings.json");
        _docs = Path.Combine(_root, "data", "docs");
        Directory.CreateDirectory(_docs);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    // ── the disk ──

    /// <summary>A promoted team: its crew, the launchers `forge promote` wrote, its companion file.</summary>
    private string PromotedTeam(string slug, StudioTeamMetadata metadata, string? parent = null)
    {
        var team = Path.Combine(parent ?? _teamsRoot, slug);
        Directory.CreateDirectory(Path.Combine(team, "crew"));
        File.WriteAllText(Path.Combine(team, "crew", "config.yaml"), "name: veille\n");
        File.WriteAllText(Path.Combine(team, TeamLaunchers.PosixLauncherName), "#!/usr/bin/env sh\n# " + TeamLauncherScript.Header(slug) + "\nexec orkeon run \"$DIR/crew\"\n");
        File.WriteAllText(Path.Combine(team, TeamLaunchers.WindowsLauncherName), "@echo off\r\nrem " + TeamLauncherScript.Header(slug) + "\r\norkeon run \"%~dp0crew\"\r\n");
        TeamCatalog.SaveMetadata(team, metadata);
        _targets.WithDirectory(team).WithDirectory(Path.Combine(team, "agents"));
        return team;
    }

    /// <summary>The settings file as saved, in the store the window reads and on the disk the launchers look at.</summary>
    private void SettingsFile(string json)
    {
        _store.Files[_settingsPath] = json;
        File.WriteAllText(_settingsPath, json);
    }

    /// <summary>A settings file declaring <c>/docs</c> without an id — written by hand, or before VFS-90.</summary>
    private void SettingsWithDocsWithoutId() =>
        SettingsFile(JsonSerializer.Serialize(new { Orkeon = new { FileSystem = new { Mounts = new[] { $"{_docs}:/docs:ro" } } } }));

    private MainWindowViewModel Window(FakeAppSettingsStore? store = null) =>
        new(new StudioServices
            {
                SettingsStore = store ?? _store,
                Directories = new FakeDirectoryProbe(_docs),
                TargetProbe = _targets,
                Picker = new FakePathPicker(),
                ProcessRunner = new OrkeonProcessRunner(_processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
                KeyStore = new FakeApiKeyStore(),
                LlmProbe = new FakeLlmEndpointProbe(),
            },
            globalPathOverride: _settingsPath,
            forgeWorkspace: Path.Combine(_root, "forge"),
            teamsRoot: _teamsRoot);

    private static string Posix(string team) => File.ReadAllText(Path.Combine(team, TeamLaunchers.PosixLauncherName));

    private static string Windows(string team) => File.ReadAllText(Path.Combine(team, TeamLaunchers.WindowsLauncherName));

    private static TeamCardViewModel Card(MainWindowViewModel window, string team) =>
        window.Teams.Teams.Concat(window.Teams.ArchivedTeams).Single(card => card.Summary.Path == team);

    private static ProcessOutputLine Installed() => ProcessOutputLine.Now(
        ProcessOutputChannel.StandardOutput,
        """{"v":2,"seq":1,"ts":"t","kind":"schedule.state","path":"p","state":"installed","expression":"daily@07:30","family":"systemd","names":["orkeon-veille"]}""");

    // ── decision 1: a renamed setting carries its teams ──

    [Fact]
    public async Task Renaming_a_setting_carries_its_teams_their_launchers_their_launches_and_the_wizard()
    {
        var veille = PromotedTeam("veille", new StudioTeamMetadata { Name = "Veille", Profile = "DeepSeek" });
        var archived = PromotedTeam("archive", new StudioTeamMetadata { Name = "Archive", Profile = "DeepSeek", ArchivedAt = DateTimeOffset.UnixEpoch, Archived = true });
        var other = PromotedTeam("autre", new StudioTeamMetadata { Name = "Autre", Profile = "Z.AI" });
        var window = Window();
        window.Settings.Profiles.CommitEdit(DeepSeek, previousName: null);
        window.Settings.Profiles.CommitEdit(Zai, previousName: null);
        window.CreateTeam.AdoptProfileName = "DeepSeek";

        window.Settings.Profiles.CommitEdit(DeepSeek with { Name = "DeepSeek V4" }, previousName: "DeepSeek");

        // The companion files, the archived team's included, and nothing else.
        Assert.Equal("DeepSeek V4", TeamCatalog.ProfileFor(veille));
        Assert.Equal("DeepSeek V4", TeamCatalog.ProfileFor(archived));
        Assert.Equal("Z.AI", TeamCatalog.ProfileFor(other));
        Assert.NotNull(TeamCatalog.Describe(archived).ArchivedAt);
        // The launchers name the new host profile; the other team's do not move.
        Assert.Contains("--llm-profile='deepseek-v4'", Posix(veille), StringComparison.Ordinal);
        Assert.Contains("--llm-profile=\"deepseek-v4\"", Windows(veille), StringComparison.Ordinal);
        Assert.Contains("--llm-profile='deepseek-v4'", Posix(archived), StringComparison.Ordinal);
        Assert.Contains("--llm-profile='z-ai'", Posix(other), StringComparison.Ordinal);
        // « Used by » lists them under the new name; the wizard's step 4 follows.
        var item = window.Settings.Profiles.Profiles.Single(profile => profile.Name == "DeepSeek V4");
        Assert.Equal(["Archive", "Veille"], item.UsedByTeams.Order(StringComparer.Ordinal));
        Assert.Equal("DeepSeek V4", window.CreateTeam.AdoptProfileName);
        Assert.Equal("DeepSeek V4", Card(window, veille).Summary.Profile);

        // A launch from the Run screen runs on the renamed setting, not on the default.
        window.Launch.Target.Select(veille);
        await window.Launch.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal("deepseek-v4-flash", _processes.LastRequest!.Environment["ORKEON_Llm__Model"]);

        foreach (var launcher in new[] { Posix(veille), Windows(veille) })
            Assert.DoesNotContain("API_KEY", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void A_setting_with_no_model_renamed_carries_its_teams_too()
    {
        var team = PromotedTeam("echo", new StudioTeamMetadata { Name = "Écho", Profile = "Sans modèle" });
        var window = Window();
        window.Settings.Profiles.CommitEdit(NoModel, previousName: null);

        window.Settings.Profiles.CommitEdit(NoModel with { Name = "Écho seul" }, previousName: "Sans modèle");

        Assert.Equal("Écho seul", TeamCatalog.ProfileFor(team));
        Assert.Equal("Écho seul", Card(window, team).Summary.Profile);
        Assert.DoesNotContain("--llm-profile", Posix(team), StringComparison.Ordinal);
    }

    // ── decision 2: what no run outside Studio can take, or what this machine does not have ──

    [Fact]
    public void A_scheduled_team_on_a_setting_offered_to_no_crew_says_its_scheduled_run_takes_the_default()
    {
        var chinese = PromotedTeam("modele", new StudioTeamMetadata { Name = "Modèle", Profile = "模型", Schedule = "daily@07:30" });
        var echo = PromotedTeam("echo", new StudioTeamMetadata { Name = "Écho", Profile = "Sans modèle", Schedule = "daily@07:30" });
        var unscheduled = PromotedTeam("libre", new StudioTeamMetadata { Name = "Libre", Profile = "模型" });
        var window = Window();
        window.Settings.Profiles.CommitEdit(DeepSeek with { Name = "模型" }, previousName: null);
        window.Settings.Profiles.CommitEdit(NoModel, previousName: null);

        Assert.Contains("Scheduled, this team runs on the default setting, not on “模型”.", Card(window, chinese).ScheduledRunLine, StringComparison.Ordinal);
        Assert.Contains("“Sans modèle” has no model: from Studio, the team answers as an echo.", Card(window, echo).ScheduledRunLine, StringComparison.Ordinal);
        Assert.False(Card(window, unscheduled).HasScheduledRunLine);
        Assert.DoesNotContain("--llm-profile", Posix(chinese), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_schedule_offer_says_the_default_before_anything_is_installed()
    {
        var team = PromotedTeam("modele", new StudioTeamMetadata { Name = "Modèle", Profile = "模型", Schedule = "daily@07:30" });
        var window = Window();
        window.Settings.Profiles.CommitEdit(DeepSeek with { Name = "模型" }, previousName: null);
        _processes.OutputToEmit.Clear();

        await window.CreateTeam.ScheduleOffer.OfferAsync(team, "daily@07:30", checkFirst: false);

        Assert.True(window.CreateTeam.ScheduleOffer.HasNotice);
        Assert.Contains("not on “模型”", window.CreateTeam.ScheduleOffer.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void A_setting_absent_from_this_machine_is_said_with_the_same_words_on_the_card_and_the_run_screens()
    {
        var team = PromotedTeam("veille", new StudioTeamMetadata { Name = "Veille", Profile = "DeepSeek" });
        var window = Window();
        window.Settings.Profiles.CommitEdit(DeepSeek, previousName: null);
        window.Settings.Profiles.CommitEdit(Zai, previousName: null);
        window.Launch.Target.Select(team);
        window.Test.Launcher.Target.Select(team);
        Assert.DoesNotContain("absent", Card(window, team).ProfileDisplay!, StringComparison.Ordinal);

        window.Settings.Profiles.Delete("DeepSeek");

        const string Said = "DeepSeek — absent from this machine: the default setting runs in its place";
        Assert.Contains(Said, Card(window, team).ProfileDisplay!, StringComparison.Ordinal);
        Assert.Contains(Said, window.Launch.TeamMetaLine!, StringComparison.Ordinal);
        Assert.Contains(Said, window.Test.Launcher.TeamMetaLine!, StringComparison.Ordinal);
    }

    // ── decision 3: a team that changes machine ──

    [Fact]
    public void An_imported_teams_launchers_are_written_for_this_machine_at_once()
    {
        var source = PromotedTeam("veille", new StudioTeamMetadata { Name = "Veille", Profile = "Claude" }, Path.Combine(_root, "shared"));
        File.WriteAllText(Path.Combine(source, TeamLaunchers.PosixLauncherName),
            "#!/usr/bin/env sh\n# " + TeamLauncherScript.Header("veille") + "\nexec orkeon run \"$DIR/crew\" --settings='/autre/machine/appsettings.json' --llm-profile='claude'\n");
        SettingsFile("{}");
        var window = Window();
        window.Settings.Profiles.CommitEdit(DeepSeek, previousName: null);

        window.Import.Target.Select(source);
        window.Import.ImportCommand.Execute(null);

        var imported = Path.Combine(_teamsRoot, "veille");
        Assert.True(Directory.Exists(imported), window.Import.StatusMessage);
        var posix = Posix(imported);
        Assert.Contains($"--settings='{_settingsPath}'", posix, StringComparison.Ordinal);
        Assert.DoesNotContain("/autre/machine", posix, StringComparison.Ordinal);
        Assert.DoesNotContain("--llm-profile", posix, StringComparison.Ordinal);
        Assert.Contains("absent from this machine", Card(window, imported).ProfileDisplay!, StringComparison.Ordinal);

        // « Claude » here: the next write of the launchers names it.
        window.Settings.Profiles.CommitEdit(DeepSeek with { Name = "Claude" }, previousName: null);
        Assert.Contains("--llm-profile='claude'", Posix(imported), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Installing_a_folder_copied_by_hand_writes_its_launchers_for_this_machine_first()
    {
        var team = PromotedTeam("veille", new StudioTeamMetadata { Name = "Veille", Profile = "DeepSeek", Schedule = "daily@07:30" });
        var window = Window();
        window.Settings.Profiles.CommitEdit(DeepSeek, previousName: null);
        // The folder arrives by hand, with the launchers another machine wrote: nothing rewrites them
        // until the gesture that schedules the team here.
        await File.WriteAllTextAsync(Path.Combine(team, TeamLaunchers.PosixLauncherName),
            "#!/usr/bin/env sh\n# " + TeamLauncherScript.Header("veille") + "\nexec orkeon run \"$DIR/crew\" --settings='/autre/machine/appsettings.json'\n",
            TestContext.Current.CancellationToken);
        window.Teams.Refresh();
        string? atInstall = null;
        _processes.WhileRunning = () => atInstall ??= Posix(team);
        _processes.OutputToEmit.Add(Installed());

        await Card(window, team).InstallScheduleCommand.ExecuteAsync();

        Assert.NotNull(atInstall);
        Assert.DoesNotContain("/autre/machine", atInstall, StringComparison.Ordinal);
        Assert.Contains("--llm-profile='deepseek'", atInstall, StringComparison.Ordinal);
        Assert.Equal(["forge", "schedule", team, "--events", "jsonl"], _processes.LastRequest!.Arguments);
    }

    // ── decision 4: what the settings file as saved does not hold yet ──

    /// <summary>A file the save validation accepts: one allowed folder.</summary>
    private void SettingsWithAFolder() =>
        SettingsFile(JsonSerializer.Serialize(new { Orkeon = new { FileSystem = new { Mounts = new[] { $"{_docs}:/docs:ro" } } } }));

    [Fact]
    public async Task An_expert_setting_not_saved_yet_says_the_scheduled_run_will_be_refused_until_the_settings_are_saved()
    {
        SettingsWithAFolder();
        var team = PromotedTeam("veille", new StudioTeamMetadata { Name = "Veille", Profile = "GLM", Schedule = "daily@07:30" });
        var window = Window();
        window.Mode.SetExpertCommand.Execute(null);
        await window.Config.InitializeAsync(TestContext.Current.CancellationToken);

        window.Settings.Profiles.CommitEdit(Zai with { Name = "GLM" }, previousName: null);

        Assert.Equal(
            "The next scheduled run will be refused: the saved settings file does not define “GLM” yet. Save the settings.",
            Card(window, team).ScheduledRunLine);

        Assert.True(await window.Config.SaveAsync(TestContext.Current.CancellationToken));

        Assert.False(Card(window, team).HasScheduledRunLine);
    }

    [Fact]
    public async Task Install_saves_what_the_file_lacks_before_forge_schedule()
    {
        SettingsWithAFolder();
        var team = PromotedTeam("veille", new StudioTeamMetadata { Name = "Veille", Profile = "GLM", Schedule = "daily@07:30" });
        var window = Window();
        window.Mode.SetExpertCommand.Execute(null);
        await window.Config.InitializeAsync(TestContext.Current.CancellationToken);
        window.Settings.Profiles.CommitEdit(Zai with { Name = "GLM" }, previousName: null);
        var savedBefore = false;
        _processes.WhileRunning = () => savedBefore = _store.SavedPaths.Contains(_settingsPath);
        _processes.OutputToEmit.Add(Installed());

        await Card(window, team).InstallScheduleCommand.ExecuteAsync();

        Assert.True(savedBefore);
        Assert.Contains("\"glm\"", _store.Files[_settingsPath], StringComparison.Ordinal);
        Assert.False(Card(window, team).HasScheduledRunLine);
        Assert.Contains("--llm-profile='glm'", Posix(team), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_save_still_installs_and_the_card_says_what_the_scheduled_run_lacks()
    {
        SettingsWithAFolder();
        var team = PromotedTeam("veille", new StudioTeamMetadata { Name = "Veille", Profile = "GLM", Schedule = "daily@07:30" });
        var window = Window();
        window.Mode.SetExpertCommand.Execute(null);
        await window.Config.InitializeAsync(TestContext.Current.CancellationToken);
        window.Settings.Profiles.CommitEdit(Zai with { Name = "GLM" }, previousName: null);
        _store.SaveFault = new IOException("locked");
        _processes.OutputToEmit.Add(Installed());

        await Card(window, team).InstallScheduleCommand.ExecuteAsync();

        Assert.Equal(["forge", "schedule", team, "--events", "jsonl"], _processes.LastRequest!.Arguments);
        Assert.Contains("will be refused", Card(window, team).ScheduledRunLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_novice_has_no_line_the_settings_save_themselves()
    {
        SettingsWithAFolder();
        var team = PromotedTeam("veille", new StudioTeamMetadata { Name = "Veille", Profile = "GLM", Schedule = "daily@07:30" });
        var window = Window();
        await window.Config.InitializeAsync(TestContext.Current.CancellationToken);

        window.Settings.Profiles.CommitEdit(Zai with { Name = "GLM" }, previousName: null);

        Assert.Contains(_settingsPath, _store.SavedPaths);
        Assert.False(Card(window, team).HasScheduledRunLine);
    }

    [Fact]
    public async Task A_folder_a_team_links_by_an_id_given_at_load_is_saved_and_stays_linked_after_a_restart()
    {
        SettingsWithDocsWithoutId();
        var team = PromotedTeam("veille", new StudioTeamMetadata { Name = "Veille", Mounts = ["./output:/output:rw"] });
        var window = Window();
        await window.Config.InitializeAsync(TestContext.Current.CancellationToken);
        var declared = window.Config.Mounts.CurrentMountStrings.Single();
        Assert.True(MountDefinition.TryParse(declared, out var docs, out _));
        Assert.NotNull(docs.Id);

        // « Change the folders » on the team's card, /docs ticked, saved.
        Card(window, team).ChangeMountsCommand.Execute(null);
        window.TeamMounts.AddMount(docs);
        window.TeamMounts.SaveCommand.Execute(null);

        // The file as saved now carries the id the companion file names.
        Assert.Contains(docs.Id!.ToString(), _store.Files[_settingsPath], StringComparison.Ordinal);
        Assert.Contains(TeamCatalog.Describe(team).Mounts, mount => mount.Contains(docs.Id.ToString(), StringComparison.Ordinal));

        // A restart: the same files, read again — the team resolves the declaration.
        var again = Window();
        await again.Config.InitializeAsync(TestContext.Current.CancellationToken);
        var resolved = TeamCatalog.Describe(team, again.Config.Mounts.CurrentMountStrings).ResolvedMounts
            .Single(mount => mount.Id is not null || mount.Source == TeamMountSource.Settings);
        Assert.Equal(TeamMountSource.Settings, resolved.Source);
    }

    [Fact]
    public async Task A_launch_naming_an_id_the_file_lacks_saves_the_settings_before_orkeon_run()
    {
        SettingsWithDocsWithoutId();
        var team = PromotedTeam("veille", new StudioTeamMetadata { Name = "Veille", Mounts = [$"{_docs}:/docs:ro"] });
        var window = Window();
        await window.Config.InitializeAsync(TestContext.Current.CancellationToken);
        var savedBefore = false;
        _processes.WhileRunning = () => savedBefore = _store.SavedPaths.Contains(_settingsPath);

        window.Launch.Target.Select(team);
        await window.Launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(savedBefore);
        var arguments = _processes.LastRequest!.Arguments;
        var id = arguments[arguments.ToList().IndexOf("--mount-id") + 1];
        Assert.Contains(id, _store.Files[_settingsPath], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_launch_whose_save_is_refused_does_not_start_and_says_why()
    {
        SettingsWithDocsWithoutId();
        var team = PromotedTeam("veille", new StudioTeamMetadata { Name = "Veille", Mounts = [$"{_docs}:/docs:ro"] });
        var window = Window();
        await window.Config.InitializeAsync(TestContext.Current.CancellationToken);
        _store.SaveFault = new IOException("locked");

        window.Launch.Target.Select(team);
        var result = await window.Launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(_processes.Requests);
        Assert.StartsWith("Not launched: the settings this run depends on could not be saved — ", window.Launch.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("locked", window.Launch.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_launch_that_pins_another_settings_file_saves_nothing()
    {
        SettingsWithDocsWithoutId();
        var team = PromotedTeam("veille", new StudioTeamMetadata { Name = "Veille", Mounts = [$"{_docs}:/docs:ro"] });
        var window = Window();
        await window.Config.InitializeAsync(TestContext.Current.CancellationToken);

        window.Launch.Target.Select(team);
        window.Launch.Options.SettingsMode = SettingsSelectionMode.ExplicitPath;
        window.Launch.Options.SettingsPath = Path.Combine(_root, "other.json");
        await window.Launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_store.SavedPaths);
    }
}
