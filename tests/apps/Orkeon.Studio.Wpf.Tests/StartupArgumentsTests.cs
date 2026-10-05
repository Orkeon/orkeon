using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

public sealed class StartupArgumentsTests
{
    [Fact]
    public void Should_NotSmoke_When_NoArgument()
    {
        var arguments = StartupArguments.Parse([]);

        Assert.False(arguments.SmokeExit);
        Assert.Empty(arguments.Unrecognized);
    }

    [Fact]
    public void Should_NotSmoke_When_ArgumentsAreNull()
    {
        Assert.False(StartupArguments.Parse(null).SmokeExit);
    }

    [Fact]
    public void Should_Smoke_When_SwitchIsPresent()
    {
        Assert.True(StartupArguments.Parse(["--smoke-exit"]).SmokeExit);
    }

    [Fact]
    public void Should_Smoke_When_SwitchIsAmongOthers()
    {
        var arguments = StartupArguments.Parse(["--something", "--smoke-exit"]);

        Assert.True(arguments.SmokeExit);
        Assert.Equal(["--something"], arguments.Unrecognized);
    }

    [Fact]
    public void Should_NotSmoke_When_CaseDiffers()
    {
        // The switch is a CI contract, matched exactly rather than loosely: a near-miss must open the
        // window normally instead of silently exiting and reporting a green smoke.
        Assert.False(StartupArguments.Parse(["--SMOKE-EXIT"]).SmokeExit);
    }

    [Fact]
    public void Should_ExposeTheSwitch_So_TheCiAndTheAppCannotDrift()
    {
        Assert.Equal("--smoke-exit", StartupArguments.SmokeExitSwitch);
    }

    [Fact]
    public void Should_NameEveryUnknownArgument_In_TheMessageTheAppReports()
    {
        // The two terminal front-ends exit 2 on an argument they do not know; the WPF one now
        // does the same, and this is the text it writes to standard error before it does.
        var arguments = StartupArguments.Parse(["--typo", "extra"]);

        var message = StartupArguments.DescribeUnrecognized(arguments.Unrecognized);

        Assert.Contains("--typo", message, StringComparison.Ordinal);
        Assert.Contains("extra", message, StringComparison.Ordinal);
        Assert.Contains(StartupArguments.SmokeExitSwitch, message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_UseTheSameRefusalCode_As_TheTerminalFrontEnds()
    {
        Assert.Equal(2, StartupArguments.UnrecognizedArgumentExitCode);
    }
    [Fact]
    public void The_capture_switch_takes_its_directory_and_travels_with_the_smoke()
    {
        var arguments = StartupArguments.Parse(["--capture-screens", "C:/shots"]);

        Assert.Equal("C:/shots", arguments.CaptureScreensDirectory);
        Assert.Empty(arguments.Unrecognized);
    }

    [Fact]
    public void A_capture_switch_without_a_directory_is_refused_not_ignored()
    {
        var arguments = StartupArguments.Parse(["--capture-screens"]);

        Assert.Null(arguments.CaptureScreensDirectory);
        Assert.Contains("--capture-screens", arguments.Unrecognized);
    }

    [Fact]
    public void The_cli_dir_switch_takes_its_directory_and_refuses_to_come_bare()
    {
        Assert.Equal(@"C:/tools/orkeon", StartupArguments.Parse(["--cli-dir", "C:/tools/orkeon"]).CliDirectory);

        var bare = StartupArguments.Parse(["--cli-dir"]);
        Assert.Null(bare.CliDirectory);
        Assert.Contains("--cli-dir", bare.Unrecognized);
    }

    // ── STUDIO-61: the teams folder of this start ──

    [Fact]
    public void The_teams_root_switch_takes_its_directory()
    {
        var arguments = StartupArguments.Parse(["--teams-root", @"C:\ws\teams", "--smoke-exit"]);

        Assert.Equal(@"C:\ws\teams", arguments.TeamsRoot);
        Assert.True(arguments.SmokeExit);
        Assert.Empty(arguments.Unrecognized);
        Assert.Equal("--teams-root", StartupArguments.TeamsRootSwitch);
    }

    [Fact]
    public void A_teams_root_switch_without_a_directory_is_refused_and_the_message_names_the_switch()
    {
        var bare = StartupArguments.Parse(["--teams-root"]);

        Assert.Null(bare.TeamsRoot);
        Assert.Contains("--teams-root", bare.Unrecognized);
        Assert.Contains("--teams-root <directory>", StartupArguments.DescribeUnrecognized(bare.Unrecognized), StringComparison.Ordinal);
    }
}
