using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-55, decision 5: on the Launch tab in French, a refusal reads in French — an added folder
/// that starts with <c>-</c>, a target that does not exist —, and the detector's English line steps
/// back: the status line says the explanation of its code, and the line under the shape chooser
/// appears only when it names the scripts to move.
/// </summary>
public sealed class LaunchRefusalLanguageTests
{
    private static readonly FakeResxStudioStrings French = new("fr");

    private static LaunchTabViewModel Tab(FakeTargetProbe probe) => new(new LaunchTabDependencies
    {
        ProcessRunner = new OrkeonProcessRunner(new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
        TargetProbe = probe,
        Directories = new FakeDirectoryProbe("/data"),
        SettingsStore = new FakeAppSettingsStore(),
        Strings = French,
    });

    [Fact]
    public void An_added_folder_that_starts_with_a_dash_is_refused_in_french()
    {
        var tab = Tab(new FakeTargetProbe().WithFile("/crews/team.yaml"));
        tab.Target.Select("/crews/team.yaml");
        var mount = tab.Mounts.LaunchMounts.AddMount();
        mount.PhysicalPath = "-x";
        mount.VirtualPath = "/docs";

        tab.CheckOptions();

        var message = Assert.Single(tab.ValidationMessages, m => m.Code == LaunchCodes.MountStartsWithDash);
        Assert.Equal(French["Studio.Diagnostics.Code." + LaunchCodes.MountStartsWithDash], message.FriendlyText);
        Assert.DoesNotContain("starts with '-'", message.FriendlyText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_target_is_said_in_french_on_the_status_line_and_in_the_list()
    {
        var tab = Tab(new FakeTargetProbe());

        tab.Target.Select("/nowhere");

        var expected = French["Studio.Diagnostics.Code." + RunTargetCodes.PathNotFound];
        Assert.Equal(expected, tab.Target.StatusDisplay);
        Assert.Equal(expected, Assert.Single(tab.ValidationMessages, m => m.Code == RunTargetCodes.PathNotFound).FriendlyText);
    }

    [Fact]
    public void An_ambiguous_directory_shows_no_english_line_under_the_shape_chooser()
    {
        var selection = new TargetSelectionViewModel(
            new FakeTargetProbe().WithDirectory("/crews/both").WithDirectory("/crews/both/agents").WithFile("/crews/both/crew.ork.ts"),
            strings: French);

        selection.Select("/crews/both");

        Assert.True(selection.NeedsShapeChoice);
        Assert.Null(selection.YamlLayoutBlockedMessage);
        Assert.Equal(French["Studio.Diagnostics.Code." + RunTargetCodes.AmbiguousDirectory], selection.StatusDisplay);
    }

    [Fact]
    public void A_yaml_layout_blocked_by_a_script_still_names_the_scripts_under_the_chooser()
    {
        var selection = new TargetSelectionViewModel(
            new FakeTargetProbe().WithDirectory("/crews/both").WithDirectory("/crews/both/agents").WithFile("/crews/both/crew.ork.ts"),
            strings: French);
        selection.Select("/crews/both");

        selection.PreferredDirectoryKind = RunTargetKind.MultiFileCrewDirectory;

        Assert.Equal(RunTargetCodes.YamlLayoutBlockedByScript, selection.ErrorCode);
        Assert.Contains("crew.ork.ts", selection.YamlLayoutBlockedMessage, StringComparison.Ordinal);
        Assert.Equal(French["Studio.Diagnostics.Code." + RunTargetCodes.YamlLayoutBlockedByScript], selection.StatusDisplay);
    }
}
