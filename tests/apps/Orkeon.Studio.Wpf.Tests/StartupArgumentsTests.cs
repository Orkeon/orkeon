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
}
