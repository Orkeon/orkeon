using Orkeon.Studio.Core.Localization;
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
        var vm = new ImportTeamViewModel(new ImportTeamDependencies
        {
            TargetProbe = probe,
            TeamsRoot = "/teams",
            ScanSecrets = _ => ["revue.yaml"],
            Import = (string source, string root, out string? refusal) =>
            {
                imported = $"{root}:{source}";
                refusal = null;
                return "/teams/revue";
            },
        });

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

    /// <summary>
    /// STUDIO-12 C1: a source the catalogue refuses — a folder the detector cannot resolve
    /// to a crew definition — is said in the status line with the detector's own words,
    /// instead of the generic "check access to the source" line.
    /// </summary>
    [Fact]
    public void A_refused_source_shows_the_refusal_in_the_status_line()
    {
        var probe = new FakeTargetProbe();
        probe.Files.Add("/shared/revue.yaml");
        var vm = new ImportTeamViewModel(new ImportTeamDependencies
        {
            TargetProbe = probe,
            TeamsRoot = "/teams",
            ScanSecrets = _ => [],
            Import = (string _, string _, out string? refusal) =>
            {
                refusal = "'/shared/revue.yaml' holds no crew definition.";
                return null;
            },
        });
        var landed = false;
        vm.TeamImported += (_, _) => landed = true;

        vm.Target.Select("/shared/revue.yaml");
        vm.ImportCommand.Execute(null);

        Assert.False(landed);
        Assert.Equal("Not imported — '/shared/revue.yaml' holds no crew definition.", vm.StatusMessage);
    }

    [Fact]
    public void A_disk_that_refused_keeps_the_generic_failure_line()
    {
        var probe = new FakeTargetProbe();
        probe.Files.Add("/shared/revue.yaml");
        var vm = new ImportTeamViewModel(new ImportTeamDependencies
        {
            TargetProbe = probe,
            TeamsRoot = "/teams",
            ScanSecrets = _ => [],
            Import = (string _, string _, out string? refusal) =>
            {
                refusal = null;
                return null;
            },
        });

        vm.Target.Select("/shared/revue.yaml");
        vm.ImportCommand.Execute(null);

        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.ImportFailed], vm.StatusMessage);
    }

    [Fact]
    public void Nothing_recognized_means_nothing_importable()
    {
        var vm = new ImportTeamViewModel(new ImportTeamDependencies { TargetProbe = new FakeTargetProbe(), TeamsRoot = "/teams" });

        vm.Target.SelectedPath = "/nowhere/ghost.yaml";
        vm.Target.DetectCommand.Execute(null);

        Assert.False(vm.ImportCommand.CanExecute(null));
    }

    [Fact]
    public void Picking_a_team_aims_the_trial_launcher_at_its_folder()
    {
        var probe = new FakeTargetProbe();
        probe.Directories.Add("/teams/veille");
        var launcher = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = probe,
            Directories = new FakeDirectoryProbe(),
            Picker = new FakePathPicker(),
            SettingsStore = new FakeAppSettingsStore(),
        });
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
    public void A_recognized_candidate_yields_a_four_line_report()
    {
        var probe = new FakeTargetProbe().WithFile("/incoming/veille.yaml");
        var import = new ImportTeamViewModel(new ImportTeamDependencies { TargetProbe = probe, ScanSecrets = _ => [] });

        Assert.False(import.HasRecognitionReport);

        import.Target.Select("/incoming/veille.yaml");

        Assert.True(import.HasRecognitionReport);
        Assert.Equal(4, import.RecognitionReport.Count);
        Assert.Equal("ok", import.RecognitionReport[0].Tone);   // recognized shape
        Assert.Equal("ok", import.RecognitionReport[1].Tone);   // no secrets
        Assert.Equal("info", import.RecognitionReport[2].Tone); // no declared folder (F-08)
        Assert.Equal("info", import.RecognitionReport[3].Tone); // tools checked later
        Assert.Contains("veille", import.RecognitionReport[0].Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// VFS-90 D-06: a candidate naming a declaration this machine does not have gets a warning
    /// line and an action that authorizes the folder as recorded — under the same id — so the
    /// team names a declaration this machine has once imported.
    /// </summary>
    [Fact]
    public async Task A_candidate_naming_a_missing_declaration_is_warned_and_can_be_authorized_as_recorded()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-import-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "veille");
        Directory.CreateDirectory(Path.Combine(source, "agents"));
        var id = Orkeon.Domain.Common.MountId.Create();
        try
        {
            TeamCatalog.SaveMetadata(source, new StudioTeamMetadata { Name = "Veille", Mounts = [$"{id}|/home/them/docs:/docs:ro", "./output:/output:rw"] });
            var declared = new List<string>();
            var saved = 0;
            var probe = new FakeTargetProbe().WithDirectory(source).WithDirectory(Path.Combine(source, "agents"));
            var import = new ImportTeamViewModel(new ImportTeamDependencies
            {
                TargetProbe = probe,
                ScanSecrets = _ => [],
                DeclaredMounts = () => declared,
                DeclareMount = mount => declared.Add(mount.ToMountString()),
                SaveSettings = () => { saved++; return Task.FromResult(true); },
            });

            import.Target.Select(source);

            Assert.True(import.HasUnknownMounts);
            Assert.Contains(import.RecognitionReport, c => c.Tone == "warn" && c.Title.StartsWith("1 folder", StringComparison.Ordinal));
            Assert.True(import.DeclareCopiesCommand.CanExecute(null));

            await import.DeclareCopiesCommand.ExecuteAsync(null);

            Assert.Equal([$"{id}|/home/them/docs:/docs:ro"], declared);
            Assert.Equal(1, saved);
            Assert.False(import.HasUnknownMounts);
            Assert.DoesNotContain(import.RecognitionReport, c => c.Tone == "warn");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_pasted_key_turns_the_secret_line_into_a_warning()
    {
        var probe = new FakeTargetProbe().WithFile("/incoming/veille.yaml");
        var import = new ImportTeamViewModel(new ImportTeamDependencies { TargetProbe = probe, ScanSecrets = _ => ["crew.yaml"] });

        import.Target.Select("/incoming/veille.yaml");

        Assert.Equal("warn", import.RecognitionReport[1].Tone);
        Assert.Contains("1", import.RecognitionReport[1].Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The report is read by whoever RECEIVED the team, so the sidecar's mounts are named the
    /// way the agents address them — the exporting machine's folder layout has no business
    /// here (ADR-008). This line used to join the raw mount strings.
    /// </summary>
    [Fact]
    public void The_declared_folders_line_names_mounts_not_the_exporter_s_disk()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"orkeon-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "agents"));
        try
        {
            File.WriteAllText(
                Path.Combine(directory, StudioTeamMetadata.FileName),
                """{"name":"veille","mounts":["C:\\Users\\demo\\Factures:/factures:ro"]}""");

            var probe = new FakeTargetProbe()
                .WithDirectory(directory)
                .WithDirectory(Path.Combine(directory, "agents"));
            var import = new ImportTeamViewModel(new ImportTeamDependencies { TargetProbe = probe, ScanSecrets = _ => [] });

            import.Target.Select(directory);

            var line = import.RecognitionReport.Single(c => c.Detail.Contains("/factures", StringComparison.Ordinal));
            Assert.DoesNotContain(@"C:\", line.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("Factures:", line.Detail, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
