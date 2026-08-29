using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

public sealed class MainWindowViewModelTests
{
    private static MainWindowViewModel Build() =>
        new(new FakeAppSettingsStore(),
            new FakeDirectoryProbe(),
            new FakeTargetProbe(),
            new FakePathPicker(),
            new OrkeonProcessRunner(
                new FakeProcessLauncher(),
                new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            new FakeLaunchHistoryStore(),
            dispatcher: null,
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
            new FakeAppSettingsStore(),
            new FakeDirectoryProbe(),
            new FakeTargetProbe(),
            new FakePathPicker(),
            new OrkeonProcessRunner(launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            new FakeLaunchHistoryStore(),
            dispatcher: null,
            globalPathOverride: "/tmp/appsettings.json");

        window.Config.Diagnostic.RunCommand.Execute(null);

        Assert.Single(launcher.Requests);
    }

    // ── « Autoriser un dossier » : les écrans équipe associent, les Réglages déclarent ──

    private static MainWindowViewModel WithDeclaredFolders(FakeAppSettingsStore store, params string[] mounts)
    {
        var window = new MainWindowViewModel(
            store,
            new FakeDirectoryProbe("/data", "/data/docs", "/data/out"),
            new FakeTargetProbe(),
            new FakePathPicker(),
            new OrkeonProcessRunner(
                new FakeProcessLauncher(),
                new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            new FakeLaunchHistoryStore(),
            dispatcher: null,
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
        Assert.False(window.FolderPicker.IsOpen);
        Assert.Equal(["/docs"], window.AllowedFolders.Rows.Select(r => r.VirtualPath));
    }

    [Fact]
    public void Picking_a_declared_folder_lands_on_the_team_with_the_settings_rights()
    {
        var window = WithDeclaredFolders(new FakeAppSettingsStore(), "/data/out:/output:rw");

        window.CreateTeam.AllowFolderCommand.Execute(null);
        window.AllowedFolders.Rows.Single().IsChecked = true;
        window.AllowedFolders.ConfirmCommand.Execute(null);

        Assert.Equal(["/data/out:/output:rw"], window.CreateTeam.TeamMounts);
    }

    [Fact]
    public void An_adopted_teams_folders_modal_offers_the_same_choice()
    {
        var window = WithDeclaredFolders(new FakeAppSettingsStore(), "/data/docs:/docs:ro");

        window.TeamMounts.AddCommand.Execute(null);

        Assert.True(window.AllowedFolders.IsOpen);
        Assert.False(window.FolderPicker.IsOpen);
    }

    [Fact]
    public void The_settings_card_still_opens_the_disk_picker_it_is_the_declaration_screen()
    {
        var window = WithDeclaredFolders(new FakeAppSettingsStore());

        window.Config.Mounts.AllowFolderCommand.Execute(null);

        Assert.True(window.FolderPicker.IsOpen);
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
        Assert.False(window.FolderPicker.IsOpen);
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

        var chips = window.CreateTeam.TeamMountChips;
        Assert.False(chips.Single(c => c.MountString.StartsWith("/data/docs", StringComparison.Ordinal)).IsUndeclared);
        Assert.True(chips.Single(c => c.MountString.StartsWith("/elsewhere", StringComparison.Ordinal)).IsUndeclared);
    }

    [Fact]
    public void Should_NameItselfOrkeonStudio()
    {
        Assert.Equal("Orkeon Studio", MainWindowViewModel.Title);
    }
}
