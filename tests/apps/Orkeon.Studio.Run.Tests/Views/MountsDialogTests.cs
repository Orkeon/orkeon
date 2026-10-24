// CS0618: the dialogs under test build a TextView, which Terminal.Gui marks obsolete; the
// production project silences it the same way (Orkeon.Studio.Run.csproj NoWarn).
#pragma warning disable CS0618

using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Run.Launcher;
using Orkeon.Studio.Run.Views;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Run.Tests.Views;

/// <summary>
/// The launch mounts panel, built without a terminal: what it lists, what it says the runtime
/// will see, and what Remove and the external-mounts switch do to the launch options. Add and
/// Edit open a modal form and are left to the form's own tests.
/// </summary>
public class MountsDialogTests
{
    [Fact]
    public void ShouldRejectNullLauncher_WhenBuilt()
        => Assert.Throws<ArgumentNullException>(() => new MountsDialog(null!));

    [Fact]
    public void ShouldSayNoLaunchMountAndAskForACrew_WhenNothingIsSelected()
    {
        var launcher = new LauncherFixture().WithInstalledCli().Build();

        using var dialog = new MountsDialog(launcher);

        var rows = ViewTree.Rows(ViewTree.One<ListView>(dialog));
        Assert.Contains("(none", Assert.Single(rows), StringComparison.Ordinal);
        Assert.Contains(RunLauncherViewModel.EffectiveMountsUnknownNotice, ViewTree.One<TextView>(dialog).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldDescribeEachEffectiveMountWithItsOrigin_WhenACrewIsSelected()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Directories.Create("/data");
        fixture.Settings.WithFile("/etc/orkeon/appsettings.json", """
        {
          "Orkeon": { "FileSystem": { "Mounts": [ "/old:/config:ro", "/out:/output:rw" ] } }
        }
        """);
        var crew = fixture.WithYamlCrew();
        var launcher = fixture.Build();
        launcher.Target.Select(crew);
        launcher.Options.UseAutomaticSettings = false;
        launcher.Options.ExplicitSettingsPath = "/etc/orkeon/appsettings.json";
        launcher.Options.AddMount(new MountDefinition { PhysicalPath = "/data", VirtualPath = "/output" });
        launcher.RefreshSettingsMounts();

        using var dialog = new MountsDialog(launcher);

        var effective = ViewTree.One<TextView>(dialog).Text;
        Assert.Contains("Orkeon:FileSystem:Mounts:0 = /old:/config:ro   [appsettings]", effective, StringComparison.Ordinal);
        Assert.Contains("[--mount]  (replaces '/out:/output:rw')", effective, StringComparison.Ordinal);
        Assert.Contains("[auto]", effective, StringComparison.Ordinal);
        Assert.Single(ViewTree.Rows(ViewTree.One<ListView>(dialog)), r => r.Contains("/data", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldSayWhichSharedRootEntryIsKept_WhenAMountIdSelectsOne()
    {
        var (launcher, _, b) = SharedRootLauncher();
        launcher.Options.MountIds = [b];

        using var dialog = new MountsDialog(launcher);

        var effective = ViewTree.One<TextView>(dialog).Text;
        Assert.Contains("(selected by --mount-id among 2)", effective, StringComparison.Ordinal);
        Assert.Contains("(not mounted for this run)", effective, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldFlagAConflict_WhenNothingSelectsASharedRootEntry()
    {
        var (launcher, _, _) = SharedRootLauncher();

        using var dialog = new MountsDialog(launcher);

        Assert.Contains("(one of 2 entries - nothing selects it)", ViewTree.One<TextView>(dialog).Text, StringComparison.Ordinal);
    }

    private static (RunLauncherViewModel Launcher, string A, string B) SharedRootLauncher()
    {
        var a = Orkeon.Domain.Common.MountId.Create().ToString();
        var b = Orkeon.Domain.Common.MountId.Create().ToString();
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Settings.WithFile("/etc/orkeon/appsettings.json",
            $$"""{ "Orkeon": { "FileSystem": { "Mounts": [ "{{a}}|/srv/a:/output:rw", "{{b}}|/srv/b:/output:rw" ] } } }""");
        var crew = fixture.WithYamlCrew();
        var launcher = fixture.Build();
        launcher.Target.Select(crew);
        launcher.Options.UseAutomaticSettings = false;
        launcher.Options.ExplicitSettingsPath = "/etc/orkeon/appsettings.json";
        launcher.RefreshSettingsMounts();
        return (launcher, a, b);
    }

    [Fact]
    public void ShouldStateTheSettingsNotice_WhenTheSettingsFileCannotBeRead()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var launcher = fixture.Build();
        launcher.Options.UseAutomaticSettings = false;
        launcher.Options.ExplicitSettingsPath = "/etc/orkeon/absent.json";
        launcher.RefreshSettingsMounts();

        using var dialog = new MountsDialog(launcher);

        Assert.Contains("absent.json", ViewTree.One<TextView>(dialog).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRemoveTheSelectedLaunchMount_WhenRemoveIsPressed()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Directories.Create("/data");
        var launcher = fixture.Build();
        launcher.Options.AddMount(new MountDefinition { PhysicalPath = "/data", VirtualPath = "/workspace" });
        using var dialog = new MountsDialog(launcher);
        ViewTree.One<ListView>(dialog).SelectedItem = 0;

        ViewTree.Press(ViewTree.Button(dialog, "Remove"));

        Assert.Empty(launcher.Options.Mounts);
        Assert.Contains("(none", Assert.Single(ViewTree.Rows(ViewTree.One<ListView>(dialog))), StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldIgnoreRemoveAndEdit_WhenNoLaunchMountIsSelected()
    {
        var launcher = new LauncherFixture().WithInstalledCli().Build();
        using var dialog = new MountsDialog(launcher);
        ViewTree.One<ListView>(dialog).SelectedItem = null;

        ViewTree.Press(ViewTree.Button(dialog, "Remove"));
        ViewTree.Press(ViewTree.Button(dialog, "Edit…"));

        Assert.Empty(launcher.Options.Mounts);
    }

    [Fact]
    public void ShouldToggleExternalMounts_WhenTheSwitchChanges()
    {
        var launcher = new LauncherFixture().WithInstalledCli().Build();
        launcher.Options.AllowExternalMounts = true;
        using var dialog = new MountsDialog(launcher);
        var toggle = ViewTree.One<CheckBox>(dialog);
        Assert.Equal(Terminal.Gui.Views.CheckState.Checked, toggle.Value);

        toggle.Value = Terminal.Gui.Views.CheckState.UnChecked;

        Assert.False(launcher.Options.AllowExternalMounts);
    }
}
