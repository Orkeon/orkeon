using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

public sealed class MainWindowViewModelTests
{
    private static MainWindowViewModel Build() =>
        new(new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                TargetProbe = new FakeTargetProbe(),
                Picker = new FakePathPicker(),
                ProcessRunner = new OrkeonProcessRunner(
                    new FakeProcessLauncher(),
                    new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
            },
            globalPathOverride: "/home/user/.config/Orkeon/appsettings.json");

    [Fact]
    public void Should_ExposeTheTwoTabs()
    {
        var window = Build();

        Assert.NotNull(window.Config);
        Assert.NotNull(window.Launch);
        Assert.Equal(0, window.SelectedTabIndex);
    }

    [Fact]
    public void Should_ConstructWithoutAWindow_So_TheSuiteRunsOnLinux()
    {
        // This is the whole point of the split: the ViewModels never touch a WPF type, so the entire
        // behaviour of both tabs is asserted on the Linux runner and only the XAML needs Windows.
        var window = Build();

        Assert.True(window.Config.Location.CanSave);
        Assert.False(window.Launch.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task Should_LocateTheCliAndLoadTheHistory_When_Initialized()
    {
        var window = Build();

        await window.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(window.Launch.IsBinaryAvailable);
    }

    [Fact]
    public void Should_ShareTheProcessRunner_Between_TheDiagnosticAndTheLauncher()
    {
        // Both tabs invoke the same co-installed CLI; wiring two runners would let them disagree
        // about which binary is in use.
        var launcher = new FakeProcessLauncher();
        var window = new MainWindowViewModel(
            new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                TargetProbe = new FakeTargetProbe(),
                Picker = new FakePathPicker(),
                ProcessRunner = new OrkeonProcessRunner(
                    launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
            },
            globalPathOverride: "/tmp/appsettings.json");

        window.Config.Diagnostic.RunCommand.Execute(null);

        Assert.Single(launcher.Requests);
    }

    // ── "allow a folder" : the team screens associate, the settings screen declares ──

    private static MainWindowViewModel WithDeclaredFolders(FakeAppSettingsStore store, params string[] mounts) =>
        WithDeclaredFolders(store, new FakePathPicker(), mounts);

    private static MainWindowViewModel WithDeclaredFolders(FakeAppSettingsStore store, FakePathPicker picker, params string[] mounts)
    {
        var window = new MainWindowViewModel(
            new StudioServices
            {
                SettingsStore = store,
                Directories = new FakeDirectoryProbe("/data", "/data/docs", "/data/out"),
                TargetProbe = new FakeTargetProbe(),
                Picker = picker,
                ProcessRunner = new OrkeonProcessRunner(
                    new FakeProcessLauncher(),
                    new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
            },
            globalPathOverride: "/home/user/.config/Orkeon/appsettings.json");

        window.Config.Mounts.Load(mounts);
        return window;
    }

    [Fact]
    public void The_wizards_allow_folder_button_offers_the_declared_folders_not_the_disk()
    {
        // The defect this pins: both team screens used to open the folder picker, which is the
        // DECLARATION screen — every team then re-declared its mounts from scratch.
        var window = WithDeclaredFolders(new FakeAppSettingsStore(), "/data/docs:/docs:ro");

        window.CreateTeam.AllowFolderCommand.Execute(null);

        Assert.True(window.AllowedFolders.IsOpen);
        Assert.Equal(["/docs"], window.AllowedFolders.Rows.Select(r => r.VirtualPath));
    }

    [Fact]
    public void Picking_a_declared_folder_lands_on_the_team_with_the_settings_rights()
    {
        var window = WithDeclaredFolders(new FakeAppSettingsStore(), "/data/out:/output:rw");

        window.CreateTeam.AllowFolderCommand.Execute(null);
        window.AllowedFolders.Rows.Single().IsChecked = true;
        window.AllowedFolders.ConfirmCommand.Execute(null);

        // Verbatim — the settings entry itself, id included (VFS-90).
        var bound = Assert.Single(window.CreateTeam.TeamMounts);
        Assert.Equal("/data/out:/output:rw", MountDefinition.Parse(bound).WithoutId().ToMountString());
        Assert.Equal(Assert.Single(window.Config.Mounts.CurrentMountStrings), bound);
    }

    [Fact]
    public void An_adopted_teams_folders_modal_offers_the_same_choice()
    {
        var window = WithDeclaredFolders(new FakeAppSettingsStore(), "/data/docs:/docs:ro");

        window.TeamMounts.AddCommand.Execute(null);

        Assert.True(window.AllowedFolders.IsOpen);
    }

    [Fact]
    public void The_settings_card_opens_the_os_folder_dialog_and_declares_the_pick_read_only()
    {
        // STUDIO-19: the settings ARE the declaration screen, and declaring is one OS dialog —
        // no in-app modal. The pick lands read-only under the folder's own name.
        var picker = new FakePathPicker { FolderToReturn = "/data/docs" };
        var window = WithDeclaredFolders(new FakeAppSettingsStore(), picker);

        window.Config.Mounts.AllowFolderCommand.Execute(null);

        Assert.Equal([EnglishStudioStrings.Instance[StudioStringKeys.DialogSelectMountFolder]], picker.Prompts);
        Assert.Equal(["/data/docs:/docs:ro"], window.Config.Mounts.CurrentMountStrings.Select(m => MountDefinition.Parse(m).WithoutId().ToMountString()));
        Assert.False(window.AllowedFolders.IsOpen);
    }

    [Fact]
    public void Declaring_a_new_folder_sends_the_user_to_the_settings_folders_tab()
    {
        var window = WithDeclaredFolders(new FakeAppSettingsStore(), "/data/docs:/docs:ro");

        window.CreateTeam.AllowFolderCommand.Execute(null);
        window.AllowedFolders.DeclareNewCommand.Execute(null);

        // The folders tab, not merely the settings screen: arriving on the model tab and having
        // to find the right one is how the gesture loses the user it was meant to help.
        Assert.False(window.AllowedFolders.IsOpen);
        Assert.True(window.Settings.IsFoldersTab);
    }

    [Fact]
    public void A_team_folder_the_settings_do_not_declare_reads_red()
    {
        var window = WithDeclaredFolders(new FakeAppSettingsStore(), "/data/docs:/docs:ro");

        window.CreateTeam.AllowFolderCommand.Execute(null);
        window.AllowedFolders.Rows.Single().IsChecked = true;
        window.AllowedFolders.ConfirmCommand.Execute(null);
        // An entry no settings folder backs — an imported sidecar, a settings entry since deleted.
        window.CreateTeam.TeamMounts.Add("/elsewhere/archives:/archives:ro");

        var chips = window.CreateTeam.MountRows;
        Assert.False(chips.Single(c => c.MountString.Contains("/data/docs", StringComparison.Ordinal)).IsUndeclared);
        Assert.True(chips.Single(c => c.MountString.Contains("/elsewhere", StringComparison.Ordinal)).IsUndeclared);
    }

    [Fact]
    public void Should_NameItselfOrkeonStudio()
    {
        Assert.Equal("Orkeon Studio", MainWindowViewModel.Title);
    }

    // ── STUDIO-18: the window opens on the per-user settings file ──

    private const string GlobalPath = "/home/user/.config/Orkeon/appsettings.json";

    private static MainWindowViewModel BuildOver(FakeAppSettingsStore store, string root, params string[] existingFolders) =>
        new(new StudioServices
            {
                SettingsStore = store,
                Directories = new FakeDirectoryProbe(existingFolders),
                TargetProbe = new FakeTargetProbe(),
                Picker = new FakePathPicker(),
                ProcessRunner = new OrkeonProcessRunner(
                    new FakeProcessLauncher(),
                    new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
            },
            globalPathOverride: GlobalPath,
            forgeWorkspace: Path.Combine(root, "forge"),
            teamsRoot: Path.Combine(root, "teams"));

    private static string TempRoot() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"orkeon-shell-{Guid.NewGuid():N}")).FullName;

    private static void SaveTeam(string root, string slug, params string[] mounts)
    {
        var team = Path.Combine(root, "teams", slug);
        Directory.CreateDirectory(team);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = slug, Mounts = mounts });
    }

    [Fact]
    public async Task Should_OpenOnThePerUserFile_So_TheFoldersItDeclaresVouchForTheTeams()
    {
        // The owner's file declared two folders; « Settings › Authorized folders » opened empty
        // and a team using one of them read red on its card, because nothing loaded the file.
        var root = TempRoot();
        try
        {
            SaveTeam(root, "factures", "/data/factures:/workspace:ro");
            var store = new FakeAppSettingsStore();
            store.Files[GlobalPath] = """
            { "Orkeon": { "FileSystem": { "Mounts": ["/data/factures:/workspace:ro", "/data/out:/output:rw"] } } }
            """;
            var window = BuildOver(store, root, "/data/factures", "/data/out");

            // Built, not yet initialised: the card was computed on an empty declared list.
            Assert.True(Assert.Single(Assert.Single(window.Teams.Teams).MountChips).IsUndeclared);

            await window.InitializeAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, window.Settings.Config.Mounts.Mounts.Count);
            Assert.True(window.Settings.Config.Location.IsGlobal);
            Assert.True(DeclaredMounts.IsDeclared("/data/factures:/docs:ro", window.Config.Mounts.CurrentMountStrings));
            Assert.False(Assert.Single(Assert.Single(window.Teams.Teams).MountChips).IsUndeclared);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Should_RefreshTheCardsAndTheLauncher_When_AFolderIsDeclaredInTheSettings()
    {
        // The verdicts are read live from the settings editor, but a card computes its chips
        // when it is built: declaring a folder used to leave the card red until a restart.
        var root = TempRoot();
        try
        {
            SaveTeam(root, "factures", "/data/factures:/workspace:ro");
            var window = BuildOver(new FakeAppSettingsStore(), root, "/data/factures");
            await window.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.True(Assert.Single(Assert.Single(window.Teams.Teams).MountChips).IsUndeclared);

            window.Config.Mounts.AddPickedMount(new MountDefinition
            {
                PhysicalPath = "/data/factures",
                VirtualPath = "/factures",
            });

            Assert.False(Assert.Single(Assert.Single(window.Teams.Teams).MountChips).IsUndeclared);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
