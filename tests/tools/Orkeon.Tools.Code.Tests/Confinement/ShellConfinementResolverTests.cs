using Orkeon.Tools.Code.Confinement;

namespace Orkeon.Tools.Code.Tests.Confinement;

/// <summary>
/// <see cref="ShellConfinementResolver"/>: the configured mode, the platform and the container give the
/// backend, or the reason none can run.
/// </summary>
public sealed class ShellConfinementResolverTests
{
    [Theory]
    [InlineData((int)ShellPlatform.Linux, (int)ShellConfinementBackend.Bubblewrap)]
    [InlineData((int)ShellPlatform.MacOS, (int)ShellConfinementBackend.Seatbelt)]
    [InlineData((int)ShellPlatform.Windows, (int)ShellConfinementBackend.AppContainer)]
    public void ShouldPickThePlatformBackend_WhenAutoRunsOnAHost(int platform, int expected)
    {
        var resolution = ShellConfinementResolver.Resolve(ShellConfinementMode.Auto, (ShellPlatform)platform, inContainer: false);

        Assert.Equal((ShellConfinementBackend)expected, resolution.Backend);
        Assert.Null(resolution.UnavailableReason);
        Assert.Null(resolution.Notice);
    }

    [Theory]
    [InlineData((int)ShellPlatform.Linux)]
    [InlineData((int)ShellPlatform.MacOS)]
    [InlineData((int)ShellPlatform.Windows)]
    [InlineData((int)ShellPlatform.Other)]
    public void ShouldTurnConfinementOffWithANotice_WhenAutoRunsInAContainer(int platform)
    {
        var resolution = ShellConfinementResolver.Resolve(ShellConfinementMode.Auto, (ShellPlatform)platform, inContainer: true);

        Assert.Equal(ShellConfinementBackend.None, resolution.Backend);
        Assert.Equal(
            "shell confinement off: running in a container; set Orkeon:Tools:Shell:Confinement:Mode to force it",
            resolution.Notice);
    }

    [Fact]
    public void ShouldFailClosed_WhenAutoRunsOnAPlatformWithoutBackend()
    {
        var resolution = ShellConfinementResolver.Resolve(ShellConfinementMode.Auto, ShellPlatform.Other, inContainer: false);

        Assert.Null(resolution.Backend);
        Assert.Contains("no confinement backend", resolution.UnavailableReason!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((int)ShellPlatform.Linux, false)]
    [InlineData((int)ShellPlatform.Windows, true)]
    public void ShouldRunUnconfined_WhenNoneIsExplicit(int platform, bool inContainer)
    {
        var resolution = ShellConfinementResolver.Resolve(ShellConfinementMode.None, (ShellPlatform)platform, inContainer);

        Assert.Equal(ShellConfinementBackend.None, resolution.Backend);
        Assert.Null(resolution.Notice);
    }

    [Theory]
    [InlineData(ShellConfinementMode.Bubblewrap, (int)ShellPlatform.Linux, (int)ShellConfinementBackend.Bubblewrap)]
    [InlineData(ShellConfinementMode.Seatbelt, (int)ShellPlatform.MacOS, (int)ShellConfinementBackend.Seatbelt)]
    [InlineData(ShellConfinementMode.AppContainer, (int)ShellPlatform.Windows, (int)ShellConfinementBackend.AppContainer)]
    public void ShouldKeepAnExplicitMode_WhenItMatchesThePlatformEvenInAContainer(
        ShellConfinementMode mode, int platform, int expected)
    {
        var resolution = ShellConfinementResolver.Resolve(mode, (ShellPlatform)platform, inContainer: true);

        Assert.Equal((ShellConfinementBackend)expected, resolution.Backend);
        Assert.Null(resolution.Notice);
    }

    [Theory]
    [InlineData(ShellConfinementMode.Bubblewrap, (int)ShellPlatform.Windows, "Linux only")]
    [InlineData(ShellConfinementMode.Bubblewrap, (int)ShellPlatform.MacOS, "Linux only")]
    [InlineData(ShellConfinementMode.Seatbelt, (int)ShellPlatform.Linux, "macOS only")]
    [InlineData(ShellConfinementMode.AppContainer, (int)ShellPlatform.MacOS, "Windows only")]
    public void ShouldBeUnavailableWithTheReason_WhenAnExplicitModeIsForAnotherPlatform(
        ShellConfinementMode mode, int platform, string reason)
    {
        var resolution = ShellConfinementResolver.Resolve(mode, (ShellPlatform)platform, inContainer: false);

        Assert.Null(resolution.Backend);
        Assert.Contains(reason, resolution.UnavailableReason!, StringComparison.Ordinal);
    }
}
