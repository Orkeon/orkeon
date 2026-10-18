using Orkeon.Domain.Execution;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// The bubblewrap session for real: one confined shell per run, its private <c>/tmp</c> kept from one
/// command to the next, and nothing left once the run ends. Integration: skipped with the probe's
/// reason where bubblewrap cannot confine a process.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BubblewrapShellSessionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public BubblewrapShellSessionTests()
    {
        var bubblewrap = BubblewrapProbe.Current();
        Assert.SkipUnless(bubblewrap.IsAvailable, $"bubblewrap cannot confine a process here: {bubblewrap.Reason}.");
    }

    private static ShellInvocation Command(string executable, params string[] arguments)
        => new(executable, arguments, null, TimeSpan.FromSeconds(30));

    [Fact]
    public async Task ShouldKeepTheSameConfinedShell_FromOneCommandToTheNext()
    {
        var sandbox = new BubblewrapShellSandbox();

        await using (var session = await sandbox.OpenAsync(ShellSessionView.Empty, Ct))
        {
            Assert.Equal(0, (await session.RunAsync(Command("touch", "/tmp/orkeon-marker"), Ct)).ExitCode);
            Assert.Equal(0, (await session.RunAsync(Command("test", "-e", "/tmp/orkeon-marker"), Ct)).ExitCode);
        }

        // A new session has a /tmp of its own.
        await using var fresh = await sandbox.OpenAsync(ShellSessionView.Empty, Ct);
        Assert.NotEqual(0, (await fresh.RunAsync(Command("test", "-e", "/tmp/orkeon-marker"), Ct)).ExitCode);
    }

    [Fact]
    public async Task ShouldSeeThePathsOfTheView()
    {
        var sandbox = new BubblewrapShellSandbox();
        var root = Directory.CreateTempSubdirectory("orkeon-bwrap-session-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "confined", Ct);
            var view = new ShellSessionView([new ConfinedPath(root, "/workspace", ConfinedAccess.ReadOnly)], [], false);
            await using var session = await sandbox.OpenAsync(view, Ct);

            var result = await session.RunAsync(new ShellInvocation("cat", ["a.txt"], "/workspace", TimeSpan.FromSeconds(30)), Ct);

            Assert.Equal("confined", result.Stdout);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldKillTheConfinedTree_WhenTheRunEnds()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The test reads /proc.");
        var duration = LinuxProcesses.UniqueSleepDuration();
        int? sleep = null;
        try
        {
            var scope = AgentRunScope.Begin("agent-1");
            var running = new ShellSessionManager().RunAsync(new BubblewrapShellSandbox(), ShellSessionView.Empty,
                new ShellInvocation("sleep", [duration], null, TimeSpan.FromHours(1)), Ct);
            // The host's /proc shows the processes of the sandbox's PID namespace too.
            await Polling.WaitUntilAsync(() => (sleep = LinuxProcesses.Find("sleep", duration)) is not null);

            await scope.DisposeAsync();

            await Assert.ThrowsAsync<ShellSessionException>(() => running);
            await Polling.WaitUntilAsync(() => !LinuxProcesses.IsAlive(sleep!.Value));
        }
        finally
        {
            LinuxProcesses.KillIfAlive(sleep);
        }
    }
}
