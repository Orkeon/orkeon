using Orkeon.Domain.Execution;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// The Seatbelt session for real (macOS): the mounts read or written after their rights, nothing else
/// of the user's files, no network, one shell per run and nothing left once the run ends. Integration:
/// skipped with the probe's reason where Seatbelt cannot confine a process.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SeatbeltShellSessionTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _root = "";
    private readonly string _docs = "";
    private readonly string _workspace = "";

    public SeatbeltShellSessionTests()
    {
        var seatbelt = SeatbeltProbe.Current();
        Assert.SkipUnless(seatbelt.IsAvailable, $"Seatbelt cannot confine a process here: {seatbelt.Reason}.");
        _root = Directory.CreateTempSubdirectory("orkeon-seatbelt-session-").FullName;
        _docs = Directory.CreateDirectory(Path.Combine(_root, "docs")).FullName;
        _workspace = Directory.CreateDirectory(Path.Combine(_root, "ws")).FullName;
        File.WriteAllText(Path.Combine(_docs, "a.txt"), "confined");
        Directory.CreateDirectory(Path.Combine(_workspace, "secrets"));
        File.WriteAllText(Path.Combine(_workspace, "secrets", "key.txt"), "hidden");
    }

    public void Dispose()
    {
        if (_root.Length == 0) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private ShellSessionView View() => new(
    [
        new ConfinedPath(_docs, "/docs", ConfinedAccess.ReadOnly),
        new ConfinedPath(_workspace, "/workspace", ConfinedAccess.ReadWrite),
        new ConfinedPath(Path.Combine(_workspace, "secrets"), "/workspace/secrets", ConfinedAccess.HiddenDirectory),
    ], [], false);

    private static ShellInvocation Command(string executable, params string[] arguments)
        => new(executable, arguments, null, TimeSpan.FromSeconds(30));

    [Fact]
    public async Task ShouldReadAReadOnlyMountAtItsPhysicalPath()
    {
        await using var session = await new SeatbeltShellSandbox().OpenAsync(View(), Ct);

        var result = await session.RunAsync(new ShellInvocation("cat", ["a.txt"], _docs, TimeSpan.FromSeconds(30)), Ct);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("confined", result.Stdout);
    }

    [Fact]
    public async Task ShouldRefuseWritingAReadOnlyMount_AndAllowWritingAReadWriteOne()
    {
        await using var session = await new SeatbeltShellSandbox().OpenAsync(View(), Ct);

        var readOnly = await session.RunAsync(Command("touch", Path.Combine(_docs, "new.txt")), Ct);
        var readWrite = await session.RunAsync(Command("touch", Path.Combine(_workspace, "new.txt")), Ct);

        Assert.NotEqual(0, readOnly.ExitCode);
        Assert.False(File.Exists(Path.Combine(_docs, "new.txt")));
        Assert.Equal(0, readWrite.ExitCode);
        Assert.True(File.Exists(Path.Combine(_workspace, "new.txt")));
    }

    [Fact]
    public async Task ShouldRefuseReadingAHiddenSubPathOfAMount()
    {
        await using var session = await new SeatbeltShellSandbox().OpenAsync(View(), Ct);

        var result = await session.RunAsync(Command("cat", Path.Combine(_workspace, "secrets", "key.txt")), Ct);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("hidden", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseReadingAFileOutsideTheMounts()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var outside = Path.Combine(home, $"orkeon-seatbelt-outside-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outside, "outside", Ct);
        try
        {
            await using var session = await new SeatbeltShellSandbox().OpenAsync(View(), Ct);

            var result = await session.RunAsync(Command("cat", outside), Ct);

            Assert.NotEqual(0, result.ExitCode);
            Assert.DoesNotContain("outside", result.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task ShouldRefuseTheNetwork_WhenItIsNotAllowed()
    {
        Assert.SkipUnless(File.Exists("/usr/bin/curl"), "curl is not installed.");
        await using var session = await new SeatbeltShellSandbox().OpenAsync(View(), Ct);

        var result = await session.RunAsync(Command("/usr/bin/curl", "-sS", "--max-time", "10", "-o", "/dev/null", "http://1.1.1.1/"), Ct);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task ShouldKeepTheSameConfinedShell_FromOneCommandToTheNext()
    {
        var sandbox = new SeatbeltShellSandbox();

        await using (var session = await sandbox.OpenAsync(View(), Ct))
        {
            // HOME is the session's own scratch directory.
            Assert.Equal(0, (await session.RunAsync(Command("sh", "-c", "touch \"$HOME/orkeon-marker\""), Ct)).ExitCode);
            Assert.Equal(0, (await session.RunAsync(Command("sh", "-c", "test -e \"$HOME/orkeon-marker\""), Ct)).ExitCode);
        }

        // A new session has a scratch directory of its own.
        await using var fresh = await sandbox.OpenAsync(View(), Ct);
        Assert.NotEqual(0, (await fresh.RunAsync(Command("sh", "-c", "test -e \"$HOME/orkeon-marker\""), Ct)).ExitCode);
    }

    [Fact]
    public async Task ShouldKillTheConfinedTree_WhenTheRunEnds()
    {
        var duration = LinuxProcesses.UniqueSleepDuration();
        int? sleep = null;
        try
        {
            var scope = AgentRunScope.Begin("agent-1");
            var running = new ShellSessionManager().RunAsync(new SeatbeltShellSandbox(), View(),
                new ShellInvocation("sleep", [duration], null, TimeSpan.FromHours(1)), Ct);
            await Polling.WaitUntilAsync(() => (sleep = PsProcesses.Find("sleep", duration)) is not null);

            await scope.DisposeAsync();

            await Assert.ThrowsAsync<ShellSessionException>(() => running);
            await Polling.WaitUntilAsync(() => !PsProcesses.IsAlive(sleep!.Value));
        }
        finally
        {
            PsProcesses.KillIfAlive(sleep);
        }
    }
}
