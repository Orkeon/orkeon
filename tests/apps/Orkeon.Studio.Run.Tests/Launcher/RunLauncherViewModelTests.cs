using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Run.Launcher;

namespace Orkeon.Studio.Run.Tests.Launcher;

/// <summary>
/// The launcher as a whole: form to command line, dry run, real run with streamed output and
/// cancellation, the mounts the pinned appsettings already declares, and replaying a past
/// launch. Every collaborator is a double — no test needs an installed <c>orkeon</c>.
/// </summary>
public class RunLauncherViewModelTests
{
    [Fact]
    public void The_command_line_is_orkeon_run_plus_the_form()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var crew = fixture.WithYamlCrew();
        var launcher = fixture.Build();
        launcher.Target.Select(crew);
        launcher.Options.AddVariable("TOPIC", "quantum computing");
        launcher.Options.Verbosity = 2;

        var arguments = launcher.BuildArguments();

        Assert.Equal(["run", crew, "-V", "TOPIC=quantum computing", "--verbose", "2"], arguments);
        Assert.Contains("orkeon run", launcher.DescribeCommandLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void Verbosity_zero_emits_no_argument()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var crew = fixture.WithYamlCrew();
        var launcher = fixture.Build();
        launcher.Target.Select(crew);

        Assert.Equal(["run", crew], launcher.BuildArguments());
    }

    [Fact]
    public void A_dry_run_adds_validate()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var crew = fixture.WithYamlCrew();
        var launcher = fixture.Build();
        launcher.Target.Select(crew);

        Assert.Contains("--validate", launcher.BuildArguments(dryRun: true));
        Assert.DoesNotContain("--validate", launcher.BuildArguments(dryRun: false));
    }

    [Fact]
    public void A_script_target_emits_the_script_options_only()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var directory = fixture.WithScriptDirectory();
        var launcher = fixture.Build();
        launcher.Target.Select(directory);
        launcher.Options.InputsJson = """{"topic":"x"}""";
        launcher.Options.InitialContext = "ignored on a script";

        var arguments = launcher.BuildArguments();

        Assert.Contains("--inputs", arguments);
        Assert.DoesNotContain("--initial-context", arguments);
        Assert.False(launcher.IsOptionAvailable(RunOption.Variables));
        Assert.True(launcher.IsOptionAvailable(RunOption.Inputs));
    }

    [Fact]
    public void With_no_target_the_command_line_says_so_instead_of_throwing()
    {
        var launcher = new LauncherFixture().WithInstalledCli().Build();

        Assert.Contains("Select a crew", launcher.DescribeCommandLine(), StringComparison.Ordinal);
        Assert.False(launcher.CanLaunch);
        Assert.Throws<InvalidOperationException>(() => launcher.BuildArguments());
    }

    [Fact]
    public void A_multi_file_directory_validates_with_the_prerequisite_as_advice()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Targets.WithDirectories("/crews/multi", "/crews/multi/agents");
        var launcher = fixture.Build();
        launcher.Target.Select("/crews/multi");

        var messages = launcher.Validate();

        Assert.False(launcher.HasBlockingErrors());
        Assert.Contains(messages, message =>
            message.Code == LaunchCodes.DirectoryRunNotice
            && message.Severity == ValidationSeverity.Information);
        Assert.Equal(RunTargetRequirements.DirectoryRunNotice, launcher.Target.FrameworkRequirement);
    }

    [Fact]
    public void An_out_of_range_verbosity_blocks_the_launch()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var crew = fixture.WithYamlCrew();
        var launcher = fixture.Build();
        launcher.Target.Select(crew);
        launcher.Options.Verbosity = 7;

        Assert.True(launcher.HasBlockingErrors());
        Assert.Contains("out of range", launcher.DescribeCommandLine(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Launching_spawns_the_command_line_and_streams_its_output()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Processes.WithStandardOutput("crew loaded", "done");
        var crew = fixture.WithYamlCrew();
        var launcher = fixture.Build();
        launcher.Target.Select(crew);

        var lines = new List<string>();
        var result = await launcher.LaunchAsync(
            onOutput: line => lines.Add(line.Text),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OrkeonExitCodes.Success, result.ExitCode);
        Assert.Equal(["crew loaded", "done"], lines);
        Assert.Equal(["run", crew], Assert.Single(fixture.Processes.Requests).Arguments);
    }

    [Fact]
    public async Task The_working_directory_follows_the_crew()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var crew = fixture.WithYamlCrew("/crews/demo/crew.yaml");
        var launcher = fixture.Build();
        launcher.Target.Select(crew);

        await launcher.LaunchAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("/crews/demo", Assert.Single(fixture.Processes.Requests).WorkingDirectory);
    }

    [Fact]
    public async Task A_dry_run_is_not_recorded_in_the_recent_list()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var crew = fixture.WithYamlCrew();
        var launcher = fixture.Build();
        launcher.Target.Select(crew);

        await launcher.LaunchAsync(dryRun: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("--validate", Assert.Single(fixture.Processes.Requests).Arguments);
        Assert.Empty(fixture.History.Recorded);
    }

    [Fact]
    public async Task A_real_run_is_recorded_with_its_arguments()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var crew = fixture.WithYamlCrew();
        var launcher = fixture.Build();
        launcher.Target.Select(crew);
        launcher.Options.UseAutomaticSettings = false;
        launcher.Options.ExplicitSettingsPath = "/etc/orkeon/appsettings.json";

        await launcher.LaunchAsync(cancellationToken: TestContext.Current.CancellationToken);

        var recorded = Assert.Single(fixture.History.Recorded);
        Assert.Equal(crew, recorded.Target);
        Assert.Equal("/etc/orkeon/appsettings.json", recorded.SettingsPath);
        Assert.Equal(["run", crew, "--settings", "/etc/orkeon/appsettings.json"], recorded.Arguments);
        Assert.Equal(OrkeonExitCodes.Success, recorded.ExitCode);
    }

    [Fact]
    public async Task Cancelling_a_launch_shows_exit_code_130()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Processes.RunsUntilCancelled = true;
        fixture.Processes.WithStandardOutput("kickoff");
        var crew = fixture.WithYamlCrew();
        var launcher = fixture.Build();
        launcher.Target.Select(crew);

        var running = new TaskCompletionSource();
        var run = launcher.LaunchAsync(
            onOutput: _ => running.TrySetResult(),
            cancellationToken: TestContext.Current.CancellationToken);
        await running.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        launcher.Session.RequestCancellation();
        var result = await run;

        Assert.Equal(OrkeonExitCodes.Cancelled, result.ExitCode);
        Assert.Contains("Exit code 130", LaunchOutcomeFormatter.DescribeRun(result), StringComparison.Ordinal);
        Assert.True(launcher.CanLaunch);
    }

    [Fact]
    public void Automatic_settings_explain_why_the_declared_mounts_are_unknown()
    {
        var launcher = new LauncherFixture().WithInstalledCli().Build();

        launcher.RefreshSettingsMounts();

        Assert.Empty(launcher.SettingsMounts);
        Assert.Contains("--settings", launcher.SettingsMountsNotice!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pinned_settings_file_reveals_the_mounts_it_declares()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Settings.WithFile("/etc/orkeon/appsettings.json", """
        {
          "Orkeon": { "FileSystem": { "Mounts": [ "/old:/workspace:ro", "/out:/output:rw" ] } }
        }
        """);
        var launcher = fixture.Build();
        launcher.Options.UseAutomaticSettings = false;
        launcher.Options.ExplicitSettingsPath = "/etc/orkeon/appsettings.json";

        launcher.RefreshSettingsMounts();

        Assert.Equal(["/old:/workspace:ro", "/out:/output:rw"], launcher.SettingsMounts);
        Assert.Null(launcher.SettingsMountsNotice);
    }

    [Fact]
    public void A_launch_mount_is_shown_as_replacing_the_settings_mount_of_the_same_index()
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
        launcher.Options.AddMount(new MountDefinition { PhysicalPath = "/data", VirtualPath = "/workspace" });
        launcher.RefreshSettingsMounts();

        var effective = launcher.EffectiveMounts;

        // Index 0 belongs to the mount the runner injects for the crew directory; the user's
        // first --mount therefore lands on index 1 and masks the settings entry there.
        Assert.Equal("Orkeon:FileSystem:Mounts:0", effective[0].ConfigurationKey);
        Assert.Equal(MountOrigin.AutoInjected, effective[0].Origin);
        Assert.Equal("/old:/config:ro", effective[0].ReplacedSettingsMount);

        Assert.Equal("Orkeon:FileSystem:Mounts:1", effective[1].ConfigurationKey);
        Assert.Equal(MountOrigin.CommandLine, effective[1].Origin);
        Assert.True(effective[1].OverridesSettings);
        Assert.Equal("/out:/output:rw", effective[1].ReplacedSettingsMount);
    }

    [Fact]
    public void The_effective_mount_table_stays_empty_until_a_crew_is_selected()
    {
        // The runner's own mounts — and so every index — depend on the crew, so guessing here
        // would show the user a layout that is wrong by one or two entries.
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Directories.Create("/data");
        var launcher = fixture.Build();
        launcher.Options.AddMount(new MountDefinition { PhysicalPath = "/data", VirtualPath = "/workspace" });

        Assert.Empty(launcher.EffectiveMounts);
        Assert.Contains("Select a crew first", RunLauncherViewModel.EffectiveMountsUnknownNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreadable_settings_file_becomes_a_notice_not_an_exception()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var launcher = fixture.Build();
        launcher.Options.UseAutomaticSettings = false;
        launcher.Options.ExplicitSettingsPath = "/etc/orkeon/absent.json";

        launcher.RefreshSettingsMounts();

        Assert.Empty(launcher.SettingsMounts);
        Assert.Contains("absent.json", launcher.SettingsMountsNotice!, StringComparison.Ordinal);
    }

    [Fact]
    public void Launch_mounts_reach_the_command_line_with_the_external_switch()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Directories.Create("/data");
        var crew = fixture.WithYamlCrew();
        var launcher = fixture.Build();
        launcher.Target.Select(crew);
        launcher.Options.AddMount(new MountDefinition
        {
            PhysicalPath = "/data",
            VirtualPath = "/workspace",
            Rights = MountRights.ReadWrite,
        });
        launcher.Options.AllowExternalMounts = true;

        var arguments = launcher.BuildArguments();

        Assert.Equal(["run", crew, "--mount", "/data:/workspace:rw", "--allow-external-mounts"], arguments);
    }

    [Fact]
    public async Task Replaying_a_past_launch_reuses_its_recorded_arguments()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var crew = fixture.WithYamlCrew();
        var entry = LaunchHistoryEntry.Starting(
            crew,
            ["run", crew, "-V", "TOPIC=old", "--verbose", "1"],
            settingsPath: "/etc/orkeon/appsettings.json",
            workingDirectory: "/crews/demo");
        fixture.History.WithEntry(entry);
        var launcher = fixture.Build();
        await launcher.Session.LoadHistoryAsync(TestContext.Current.CancellationToken);

        var result = await launcher.ReplayAsync(
            launcher.Session.History.Entries[0],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OrkeonExitCodes.Success, result.ExitCode);
        var spawned = Assert.Single(fixture.Processes.Requests);
        Assert.Equal(["run", crew, "-V", "TOPIC=old", "--verbose", "1"], spawned.Arguments);
        Assert.Equal("/crews/demo", spawned.WorkingDirectory);
    }

    [Fact]
    public void Loading_a_past_launch_back_into_the_form_restores_its_target_and_settings()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var crew = fixture.WithYamlCrew();
        var entry = LaunchHistoryEntry.Starting(
            crew,
            ["run", crew],
            settingsPath: "/etc/orkeon/appsettings.json",
            workingDirectory: "/crews/demo");
        var launcher = fixture.Build();

        Assert.True(launcher.ApplyHistoryEntry(entry));

        Assert.Equal(crew, launcher.Target.SelectedPath);
        Assert.False(launcher.Options.UseAutomaticSettings);
        Assert.Equal("/etc/orkeon/appsettings.json", launcher.Options.ExplicitSettingsPath);
        Assert.Equal("/crews/demo", launcher.EffectiveWorkingDirectory);
    }

    [Fact]
    public void A_past_launch_whose_crew_moved_no_longer_resolves()
    {
        var launcher = new LauncherFixture().WithInstalledCli().Build();

        var applied = launcher.ApplyHistoryEntry(
            LaunchHistoryEntry.Starting("/crews/gone/crew.yaml", ["run", "/crews/gone/crew.yaml"]));

        Assert.False(applied);
        Assert.False(launcher.CanLaunch);
    }

    [Fact]
    public async Task A_history_entry_with_no_arguments_cannot_be_replayed()
    {
        var launcher = new LauncherFixture().WithInstalledCli().Build();
        var entry = LaunchHistoryEntry.Starting("/crews/demo/crew.yaml", []);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => launcher.ReplayAsync(entry, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_recent_list_is_described_newest_first()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.History
            .WithEntry(LaunchHistoryEntry.Starting("/crews/a.yaml", ["run", "/crews/a.yaml"]))
            .WithEntry(LaunchHistoryEntry.Starting("/crews/b.yaml", ["run", "/crews/b.yaml"]));
        var launcher = fixture.Build();
        await launcher.Session.LoadHistoryAsync(TestContext.Current.CancellationToken);

        var lines = launcher.DescribeHistory();

        Assert.Equal(2, lines.Count);
        Assert.Contains("/crews/b.yaml", lines[0], StringComparison.Ordinal);
        Assert.Contains("/crews/a.yaml", lines[1], StringComparison.Ordinal);
    }
}
