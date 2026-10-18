using System.Diagnostics;
using Orkeon.Domain.Execution;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// The unconfined POSIX session for real: one <c>/bin/sh -s</c> serves every command of a run, each
/// argument reaches the command as one word, a timeout leaves the session unusable, and nothing
/// survives the session's end or the host's death. Skipped on Windows, which has no POSIX shell; the
/// checks of what survives read <c>/proc</c> and run on Linux.
/// </summary>
public sealed class PosixShellSessionTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("orkeon-shell-session-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PosixShellSessionTests()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no POSIX shell: each command is its own process there.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static ShellInvocation Sh(string script, string? workingDirectory = null, double timeoutSeconds = 30)
        => new("sh", ["-c", script], workingDirectory, TimeSpan.FromSeconds(timeoutSeconds));

    private static async Task<PosixShellSession> OpenAsync(UnconfinedShellSandbox? sandbox = null)
        => (PosixShellSession)await (sandbox ?? UnconfinedShellSandbox.Shared).OpenAsync(ShellSessionView.Empty, Ct);

    [Fact]
    public async Task ShouldRunEveryCommandInTheSameShell()
    {
        await using var session = await OpenAsync();

        // The command is exec'd from a subshell of the session's shell: its parent is that shell.
        var first = await session.RunAsync(Sh("echo $PPID"), Ct);
        var second = await session.RunAsync(Sh("echo $PPID"), Ct);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(first.Stdout, second.Stdout);
        Assert.Equal(session.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), first.Stdout.Trim());
    }

    [Fact]
    public async Task ShouldPassEachArgumentAsOneWord_WhateverItHolds()
    {
        string[] words = ["it's", "", "two words", "$HOME", "`id`", "$(id)", "line1\nline2", "*", ";", "&&"];
        await using var session = await OpenAsync();

        var result = await session.RunAsync(new ShellInvocation("printf", ["%s|", .. words], null, TimeSpan.FromSeconds(30)), Ct);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Concat(words.Select(w => w + "|")), result.Stdout);
    }

    [Fact]
    public async Task ShouldRunInTheWorkingDirectory_AndKeepTheSessionsOwnAfterwards()
    {
        await using var session = await OpenAsync();

        var inside = await session.RunAsync(new ShellInvocation("pwd", [], _tempDir, TimeSpan.FromSeconds(30)), Ct);
        var after = await session.RunAsync(new ShellInvocation("pwd", [], null, TimeSpan.FromSeconds(30)), Ct);

        Assert.Equal(Path.GetFullPath(_tempDir), inside.Stdout.Trim());
        Assert.NotEqual(inside.Stdout, after.Stdout);
    }

    [Fact]
    public async Task ShouldReturnTheExitCodeAndBothStreams()
    {
        await using var session = await OpenAsync();

        var result = await session.RunAsync(Sh("echo out; echo err >&2; exit 3"), Ct);

        Assert.Equal(new ShellRunResult(3, "out\n", "err\n"), result);
    }

    [Fact]
    public async Task ShouldReportACommandThatIsNotInstalled_AsAFailedCommand()
    {
        await using var session = await OpenAsync();

        var result = await session.RunAsync(new ShellInvocation("orkeon-no-such-command", [], null, TimeSpan.FromSeconds(30)), Ct);
        var next = await session.RunAsync(Sh("echo still here"), Ct);

        Assert.Equal(127, result.ExitCode);
        Assert.Contains("not found", result.Stderr, StringComparison.Ordinal);
        Assert.Equal("still here\n", next.Stdout);
    }

    [Fact]
    public async Task ShouldCapEachStream()
    {
        await using var session = await OpenAsync();

        var result = await session.RunAsync(Sh("yes | head -c 100000"), Ct);

        Assert.Equal(ShellScript.ByteCap, result.Stdout.Length);
    }

    [Fact]
    public async Task ShouldReportATimeout_AndRefuseFurtherCommands()
    {
        await using var session = await OpenAsync();

        var result = await session.RunAsync(new ShellInvocation("sleep", ["30"], null, TimeSpan.FromMilliseconds(200)), Ct);

        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
        await Assert.ThrowsAsync<ShellSessionException>(() => session.RunAsync(Sh("echo"), Ct));
    }

    [Fact]
    public async Task ShouldRemoveThePrivateDirectory_WhenTheSessionEnds()
    {
        var session = await OpenAsync();
        await session.RunAsync(Sh("echo"), Ct);
        var directory = session.PrivateDirectory;
        Assert.True(Directory.Exists(directory));

        await session.DisposeAsync();

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task ShouldRemoveThePrivateDirectory_WhenTheSessionHadToBeKilled()
    {
        var session = await OpenAsync();
        var directory = session.PrivateDirectory;
        await session.RunAsync(new ShellInvocation("sleep", ["30"], null, TimeSpan.FromMilliseconds(200)), Ct);

        await session.DisposeAsync();

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task ShouldFailTheCommand_WhenTheShellDies()
    {
        await using var session = await OpenAsync();

        // The command's parent is the session's shell.
        var error = await Assert.ThrowsAsync<ShellSessionException>(() => session.RunAsync(Sh("kill -KILL $PPID"), Ct));

        Assert.Contains("ended", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldLeaveNothingRunning_WhenTheRunEnds()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The test reads /proc.");
        var duration = LinuxProcesses.UniqueSleepDuration();
        int? sleep = null;
        try
        {
            var scope = AgentRunScope.Begin("agent-1");
            var manager = new ShellSessionManager();
            var shellPid = (await manager.RunAsync(UnconfinedShellSandbox.Shared, ShellSessionView.Empty, Sh("echo $PPID"), Ct)).Stdout.Trim();
            var pid = int.Parse(shellPid, System.Globalization.CultureInfo.InvariantCulture);
            var running = manager.RunAsync(UnconfinedShellSandbox.Shared, ShellSessionView.Empty,
                new ShellInvocation("sleep", [duration], null, TimeSpan.FromHours(1)), Ct);
            await Polling.WaitUntilAsync(() => (sleep = LinuxProcesses.Find("sleep", duration)) is not null);
            Assert.NotNull(LinuxProcesses.FindWatchdog(Environment.ProcessId, pid));

            await scope.DisposeAsync();

            await Assert.ThrowsAsync<ShellSessionException>(() => running);
            await Polling.WaitUntilAsync(() => !LinuxProcesses.IsAlive(sleep!.Value) && !LinuxProcesses.IsAlive(pid));
            Assert.Null(LinuxProcesses.FindWatchdog(Environment.ProcessId, pid));
        }
        finally
        {
            LinuxProcesses.KillIfAlive(sleep);
        }
    }

    /// <summary>
    /// A process stands in for the host: when it dies, the watchdog kills the session's shell and the
    /// command it runs within a few seconds, though nothing closed the session.
    /// </summary>
    [Fact]
    public async Task ShouldKillTheSessionAndItsCommand_WhenTheHostDies()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The test reads /proc.");
        var duration = LinuxProcesses.UniqueSleepDuration();
        using var host = Process.Start(new ProcessStartInfo("sleep", "300") { UseShellExecute = false })!;
        int? sleep = null;
        try
        {
            await using var session = await OpenAsync(new UnconfinedShellSandbox { WatchedHostPid = host.Id });
            var running = session.RunAsync(new ShellInvocation("sleep", [duration], null, TimeSpan.FromHours(1)), Ct);
            await Polling.WaitUntilAsync(() => (sleep = LinuxProcesses.Find("sleep", duration)) is not null);

            host.Kill();

            await Polling.WaitUntilAsync(() => !LinuxProcesses.IsAlive(sleep!.Value) && !LinuxProcesses.IsAlive(session.ProcessId));
            await Assert.ThrowsAsync<ShellSessionException>(() => running);
        }
        finally
        {
            LinuxProcesses.KillIfAlive(sleep);
            if (!host.HasExited) host.Kill();
        }
    }
}
