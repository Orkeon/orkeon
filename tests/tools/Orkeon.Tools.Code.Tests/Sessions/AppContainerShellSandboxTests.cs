using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// <see cref="AppContainerShellSandbox"/> where no AppContainer can run: what it reports and what it
/// refuses. Its sessions for real are in <c>Windows/AppContainerShellSessionTests</c> (Integration).
/// </summary>
public sealed class AppContainerShellSandboxTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void ShouldSeeThePhysicalPaths()
    {
        var sandbox = new AppContainerShellSandbox(() => ShellSandboxAvailability.Available);

        Assert.Equal("appcontainer", sandbox.Name);
        Assert.False(sandbox.UsesVirtualPaths);
    }

    [Fact]
    public void ShouldReportWindowsOnly_OnAnotherPlatform()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "this host is Windows.");

        var availability = AppContainerShellSandbox.Shared.Availability;

        Assert.False(availability.IsAvailable);
        Assert.Equal("AppContainer confines a process on Windows only", availability.Reason);
    }

    [Fact]
    public async Task ShouldNeverOpenASession_WhenUnavailable()
    {
        var sandbox = new AppContainerShellSandbox(() => new ShellSandboxAvailability("no AppContainer here"));

        var error = await Assert.ThrowsAsync<ShellSessionException>(() => sandbox.OpenAsync(ShellSessionView.Empty, Ct));

        Assert.Contains("no AppContainer here", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNeverOpenASession_OffWindows_WhateverTheProbeSays()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "this host is Windows.");
        var sandbox = new AppContainerShellSandbox(() => ShellSandboxAvailability.Available);

        var error = await Assert.ThrowsAsync<ShellSessionException>(() => sandbox.OpenAsync(ShellSessionView.Empty, Ct));

        Assert.Contains("Windows 10 and later", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldNameTheFirstExtraPathThatIsNotFullyQualified()
    {
        Assert.Equal(@"tools\bin", AppContainerShellSandbox.FirstUnqualifiedPath([Path.GetTempPath(), @"tools\bin", "other"]));
        Assert.Null(AppContainerShellSandbox.FirstUnqualifiedPath([Path.GetTempPath()]));
    }
}
