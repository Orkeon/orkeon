using Microsoft.Extensions.Logging;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.Sessions;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// <see cref="ShellSandboxSelector"/>: the sandbox of each mode, the container notice logged once, and
/// a mode for another platform reported unavailable.
/// </summary>
public sealed class ShellSandboxSelectorTests
{
    private readonly UnconfinedShellSandbox _unconfined = new();
    private readonly BubblewrapShellSandbox _bubblewrap = new(() => BubblewrapAvailability.Available("/usr/bin/bwrap"));
    private readonly SeatbeltShellSandbox _seatbelt = new(() => ShellSandboxAvailability.Available);
    private readonly AppContainerShellSandbox _appContainer = new(() => ShellSandboxAvailability.Available);
    private readonly FakeContainerDetector _containers = new();
    private readonly RecordingLogger _logger = new();

    private ShellSandboxSelector Selector(ShellPlatform platform)
        => new(_unconfined, _bubblewrap, _seatbelt, _containers, platform, _logger, _appContainer);

    [Fact]
    public void ShouldPickBubblewrap_WhenAutoRunsOnALinuxHost()
        => Assert.Same(_bubblewrap, Selector(ShellPlatform.Linux).Select(ShellConfinementMode.Auto));

    [Fact]
    public void ShouldRunUnconfinedAndLogOnce_WhenAutoRunsInAContainer()
    {
        _containers.InContainer = true;
        var selector = Selector(ShellPlatform.Linux);

        Assert.Same(_unconfined, selector.Select(ShellConfinementMode.Auto));
        Assert.Same(_unconfined, selector.Select(ShellConfinementMode.Auto));

        var notice = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, notice.Level);
        Assert.Equal(ShellConfinementResolver.ContainerNotice, notice.Message);
    }

    [Fact]
    public void ShouldNotAskForTheContainer_WhenTheModeIsExplicit()
    {
        _containers.InContainer = true;

        Assert.Same(_bubblewrap, Selector(ShellPlatform.Linux).Select(ShellConfinementMode.Bubblewrap));
        Assert.Same(_unconfined, Selector(ShellPlatform.Linux).Select(ShellConfinementMode.None));
        Assert.Equal(0, _containers.Calls);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void ShouldPickSeatbelt_WhenAutoOrSeatbeltRunsOnAMacOSHost()
    {
        Assert.Same(_seatbelt, Selector(ShellPlatform.MacOS).Select(ShellConfinementMode.Auto));
        Assert.Same(_seatbelt, Selector(ShellPlatform.MacOS).Select(ShellConfinementMode.Seatbelt));
    }

    [Fact]
    public async Task ShouldFailClosed_WhenSeatbeltIsUnavailable()
    {
        var unavailable = new SeatbeltShellSandbox(() => new ShellSandboxAvailability("sandbox-exec was not found"));
        var sandbox = new ShellSandboxSelector(_unconfined, _bubblewrap, unavailable, _containers, ShellPlatform.MacOS, _logger, _appContainer)
            .Select(ShellConfinementMode.Auto);

        Assert.False(sandbox.Availability.IsAvailable);
        var error = await Assert.ThrowsAsync<ShellSessionException>(() => sandbox.OpenAsync(
            ShellSessionView.Empty, TestContext.Current.CancellationToken));
        Assert.Contains("sandbox-exec was not found", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ShellConfinementMode.Auto)]
    [InlineData(ShellConfinementMode.AppContainer)]
    public void ShouldPickAppContainer_OnAWindowsHost(ShellConfinementMode mode)
        => Assert.Same(_appContainer, Selector(ShellPlatform.Windows).Select(mode));

    [Fact]
    public void ShouldUseTheSharedAppContainerSandbox_WhenNoneIsGiven()
        => Assert.Same(
            AppContainerShellSandbox.Shared,
            new ShellSandboxSelector(_unconfined, _bubblewrap, _seatbelt, _containers, ShellPlatform.Windows).Select(ShellConfinementMode.Auto));

    [Fact]
    public async Task ShouldNeverOpenASession_WhenTheModeIsForAnotherPlatform()
    {
        var sandbox = Selector(ShellPlatform.Windows).Select(ShellConfinementMode.Bubblewrap);

        Assert.False(sandbox.Availability.IsAvailable);
        Assert.Contains("Linux only", sandbox.Availability.Reason!, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ShellSessionException>(() => sandbox.OpenAsync(
            ShellSessionView.Empty, TestContext.Current.CancellationToken));
    }
}
