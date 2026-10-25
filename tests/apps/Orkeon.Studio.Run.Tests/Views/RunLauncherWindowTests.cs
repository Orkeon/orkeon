using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Run.Launcher;
using Orkeon.Studio.Run.Views;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Run.Tests.Views;

/// <summary>
/// The screen itself, built without a terminal (no <c>Application.Init</c>, no driver, no
/// modal loop): what the detected shape does to the option fields, and what the target line
/// and command-line preview say.
/// </summary>
public class RunLauncherWindowTests
{
    [Fact]
    public void A_yaml_target_leaves_the_yaml_fields_usable_and_greys_the_script_ones()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var crew = fixture.WithYamlCrew();
        using var window = new RunLauncherWindow(fixture.Build());

        window.SelectTargetForTest(crew);

        Assert.True(window.IsOptionFieldEnabled(RunOption.Variables));
        Assert.True(window.IsOptionFieldEnabled(RunOption.InitialContext));
        Assert.False(window.IsOptionFieldEnabled(RunOption.Inputs));
        Assert.False(window.IsOptionFieldEnabled(RunOption.InputsFile));
    }

    [Fact]
    public void A_script_target_leaves_the_script_fields_usable_and_greys_the_yaml_ones()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var directory = fixture.WithScriptDirectory();
        using var window = new RunLauncherWindow(fixture.Build());

        window.SelectTargetForTest(directory);

        Assert.True(window.IsOptionFieldEnabled(RunOption.Inputs));
        Assert.True(window.IsOptionFieldEnabled(RunOption.InputsFile));
        Assert.False(window.IsOptionFieldEnabled(RunOption.Variables));
        Assert.False(window.IsOptionFieldEnabled(RunOption.InitialContext));
    }

    [Fact]
    public void Shared_fields_stay_usable_whatever_the_target()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var crew = fixture.WithYamlCrew();
        using var window = new RunLauncherWindow(fixture.Build());

        window.SelectTargetForTest(crew);

        Assert.True(window.IsOptionFieldEnabled(RunOption.Verbose));
        Assert.True(window.IsOptionFieldEnabled(RunOption.LlmLog));
    }

    [Fact]
    public void The_detected_shape_and_the_command_line_are_shown()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var crew = fixture.WithYamlCrew();
        using var window = new RunLauncherWindow(fixture.Build());

        window.SelectTargetForTest(crew);

        Assert.Contains("YAML crew file", window.TargetStatusText, StringComparison.Ordinal);
        Assert.Contains("orkeon run", window.CommandLineText, StringComparison.Ordinal);
        Assert.Contains(crew, window.CommandLineText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_multi_file_directory_shows_the_version_it_needs_rather_than_an_error()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Targets.WithDirectories("/crews/multi", "/crews/multi/agents");
        using var window = new RunLauncherWindow(fixture.Build());

        window.SelectTargetForTest("/crews/multi");

        Assert.Contains(
            RunTargetRequirements.MinimumCliVersion,
            window.TargetStatusText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_directory_of_scripts_asks_the_user_to_choose_one()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Targets
            .WithDirectories("/crews/many")
            .WithFiles("/crews/many/alpha.ork.ts", "/crews/many/beta.ork.ts");
        using var window = new RunLauncherWindow(fixture.Build());

        window.SelectTargetForTest("/crews/many");

        Assert.Contains("2 definition file(s) found", window.TargetStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrunnable_path_shows_the_detector_message()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        using var window = new RunLauncherWindow(fixture.Build());

        window.SelectTargetForTest("/nowhere/crew.yaml");

        Assert.Contains("/nowhere/crew.yaml", window.TargetStatusText, StringComparison.Ordinal);
        Assert.Contains("Select a crew", window.CommandLineText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_cli_is_reported_on_the_status_line_at_startup()
    {
        // No WithInstalledCli(): opening the launcher on a machine with no `orkeon` must say so
        // rather than fail at the first click.
        var fixture = new LauncherFixture();
        using var window = new RunLauncherWindow(fixture.Build());

        Assert.Contains("was not located on this machine", window.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_located_cli_is_reported_on_the_status_line_at_startup()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        using var window = new RunLauncherWindow(fixture.Build());

        Assert.Contains(LauncherFixture.BinaryPath, window.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void Running_without_a_target_asks_for_a_crew_and_starts_nothing()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        using var window = new RunLauncherWindow(fixture.Build());

        ViewTree.Press(ViewTree.Button(window, "Run"));

        Assert.Equal("Select a crew first.", window.StatusText);
        Assert.Equal(0, fixture.Processes.StartCount);
    }

    [Fact]
    public void Ctrl_Q_quits_with_a_zero_exit_code()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        using var window = new RunLauncherWindow(fixture.Build());

        var handled = window.NewKeyDownEvent(Key.Q.WithCtrl);

        Assert.True(handled);
        Assert.Equal(0, window.ExitCode);
    }

    [Fact]
    public void Choose_and_Detect_on_an_unrunnable_path_report_it_and_open_no_list()
    {
        // The launcher's default button is Run: a pressed button keeps its Accept, so Run never
        // repeats that no crew is selected.
        var fixture = new LauncherFixture().WithInstalledCli();
        using var window = new RunLauncherWindow(fixture.Build());
        ViewTree.All<TextField>(window)[0].Text = "/nowhere/crew.yaml";

        ViewTree.Press(ViewTree.Button(window, "Choose…"));
        Assert.Contains("/nowhere/crew.yaml", window.TargetStatusText, StringComparison.Ordinal);

        ViewTree.Press(ViewTree.Button(window, "Detect"));
        Assert.Contains("/nowhere/crew.yaml", window.TargetStatusText, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Processes.StartCount);
        Assert.NotEqual("Select a crew first.", window.StatusText);
    }

    [Fact]
    public void Unchecking_automatic_settings_unlocks_the_settings_path()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        using var window = new RunLauncherWindow(fixture.Build());
        Assert.False(window.IsOptionFieldEnabled(RunOption.Settings));

        ViewTree.All<CheckBox>(window).First(c => c.Text.StartsWith("Settings", StringComparison.Ordinal)).Value = CheckState.UnChecked;

        Assert.True(window.IsOptionFieldEnabled(RunOption.Settings));
    }
}
