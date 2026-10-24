using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// <see cref="UnavailableShellSandbox"/>: a confinement that cannot run here says why and never opens
/// a session, so the call fails rather than run unconfined.
/// </summary>
public sealed class UnavailableShellSandboxTests
{
    [Fact]
    public async Task ShouldRefuseToOpenWithTheReason_WhenAskedForASession()
    {
        var sandbox = new UnavailableShellSandbox("seatbelt", "Seatbelt confines a process on macOS only");

        var ex = await Assert.ThrowsAsync<ShellSessionException>(
            () => sandbox.OpenAsync(ShellSessionView.Empty, TestContext.Current.CancellationToken));

        Assert.Equal("Confinement unavailable: Seatbelt confines a process on macOS only.", ex.Message);
        Assert.Equal("seatbelt", sandbox.Name);
        Assert.True(sandbox.UsesVirtualPaths);
        Assert.False(sandbox.Availability.IsAvailable);
        Assert.Equal("Seatbelt confines a process on macOS only", sandbox.Availability.Reason);
    }
}
