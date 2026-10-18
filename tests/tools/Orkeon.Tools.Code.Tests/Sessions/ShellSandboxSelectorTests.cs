using Microsoft.Extensions.Logging;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.Sessions;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// <see cref="ShellSandboxSelector"/>: the sandbox of each mode, the container notice logged once, and
/// the backends not built yet reported unavailable.
/// </summary>
public sealed class ShellSandboxSelectorTests
{
    private readonly UnconfinedShellSandbox _unconfined = new();
    private readonly BubblewrapShellSandbox _bubblewrap = new(() => BubblewrapAvailability.Available("/usr/bin/bwrap"));
    private readonly FakeContainerDetector _containers = new();
    private readonly RecordingLogger _logger = new();

    private ShellSandboxSelector Selector(ShellPlatform platform)
        => new(_unconfined, _bubblewrap, _containers, platform, _logger);

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

    [Theory]
    [InlineData((int)ShellPlatform.MacOS, "Seatbelt confinement is not implemented yet")]
    [InlineData((int)ShellPlatform.Windows, "AppContainer confinement is not implemented yet")]
    public void ShouldReportNotImplemented_WhenAutoPicksABackendNotBuiltYet(int platform, string reason)
    {
        var sandbox = Selector((ShellPlatform)platform).Select(ShellConfinementMode.Auto);

        Assert.False(sandbox.Availability.IsAvailable);
        Assert.Contains(reason, sandbox.Availability.Reason!, StringComparison.Ordinal);
    }

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
