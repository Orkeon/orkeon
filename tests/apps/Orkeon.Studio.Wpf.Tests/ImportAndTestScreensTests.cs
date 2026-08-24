using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The two remaining v3 work screens: "Importer" (detect, warn about pasted secrets, copy on
/// confirmation only) and the expert "Tester" (a dedicated launcher that never touches the
/// launch history).
/// </summary>
public sealed class ImportAndTestScreensTests
{
    [Fact]
    public void A_recognized_target_with_a_pasted_secret_is_warned_about_before_the_copy()
    {
        var probe = new FakeTargetProbe();
        probe.Files.Add("/shared/revue.yaml");
        string? imported = null;
        var vm = new ImportTeamViewModel(
            probe,
            teamsRoot: "/teams",
            scanSecrets: _ => ["revue.yaml"],
            import: (source, root) => { imported = $"{root}:{source}"; return "/teams/revue"; });

        vm.Target.SelectedPath = "/shared/revue.yaml";
        vm.Target.DetectCommand.Execute(null);

        Assert.True(vm.HasSecretWarnings);
        Assert.Equal(["revue.yaml"], vm.SecretWarnings);
        Assert.Null(imported);   // nothing is copied before the confirmation

        string? landed = null;
        vm.TeamImported += (_, e) => landed = e.Path;
        vm.ImportCommand.Execute(null);

        Assert.Equal("/teams:/shared/revue.yaml", imported);
        Assert.Equal("/teams/revue", landed);
        Assert.Equal("/teams/revue", vm.StatusMessage);
    }

    [Fact]
    public void Nothing_recognized_means_nothing_importable()
    {
        var vm = new ImportTeamViewModel(new FakeTargetProbe(), teamsRoot: "/teams");

        vm.Target.SelectedPath = "/nowhere/ghost.yaml";
        vm.Target.DetectCommand.Execute(null);

        Assert.False(vm.ImportCommand.CanExecute(null));
    }

    [Fact]
    public void Picking_a_team_aims_the_trial_launcher_at_its_folder()
    {
        var probe = new FakeTargetProbe();
        probe.Directories.Add("/teams/veille");
        var launcher = new LaunchTabViewModel(
            new OrkeonProcessRunner(new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            probe,
            new FakeDirectoryProbe(),
            new FakePathPicker(),
            historyStore: null,
            settingsStore: new FakeAppSettingsStore());
        var vm = new TestTeamViewModel(
            launcher,
            teamsRoot: "/teams",
            loadTeams: () =>
            [
                new TeamSummary { Name = "Veille", Slug = "veille", Path = "/teams/veille" },
            ]);

        vm.SelectedTeam = vm.TeamChoices[0];

        Assert.Equal("/teams/veille", launcher.Target.SelectedPath);
    }
}

/// <summary>The recognition report of the Importer screen (audit 04/12).</summary>
public sealed class ImportRecognitionReportTests
{
    [Fact]
    public void A_recognized_candidate_yields_a_three_line_report()
    {
        var probe = new FakeTargetProbe().WithFile("/incoming/veille.yaml");
        var import = new ImportTeamViewModel(probe, scanSecrets: _ => []);

        Assert.False(import.HasRecognitionReport);

        import.Target.Select("/incoming/veille.yaml");

        Assert.True(import.HasRecognitionReport);
        Assert.Equal(3, import.RecognitionReport.Count);
        Assert.Equal("ok", import.RecognitionReport[0].Tone);   // recognized shape
        Assert.Equal("ok", import.RecognitionReport[1].Tone);   // no secrets
        Assert.Equal("info", import.RecognitionReport[2].Tone); // tools checked later
        Assert.Contains("veille", import.RecognitionReport[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pasted_key_turns_the_secret_line_into_a_warning()
    {
        var probe = new FakeTargetProbe().WithFile("/incoming/veille.yaml");
        var import = new ImportTeamViewModel(probe, scanSecrets: _ => ["crew.yaml"]);

        import.Target.Select("/incoming/veille.yaml");

        Assert.Equal("warn", import.RecognitionReport[1].Tone);
        Assert.Contains("1", import.RecognitionReport[1].Detail, StringComparison.Ordinal);
    }
}
