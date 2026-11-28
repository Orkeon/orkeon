using Orkeon.Cli.Commands.Scripting.Shell;

namespace Orkeon.Cli.Commands.Scripting.Tests.Shell;

/// <summary>The operator's real shell (#491): pipes work, and a command that reads the keyboard never takes it.</summary>
public sealed class ProcessOperatorShellRunnerTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(120);

    private readonly ProcessOperatorShellRunner _runner = new();
    private readonly List<string> _lines = [];
    private readonly Lock _gate = new();

    private void OnLine(string line)
    {
        lock (_gate)
            _lines.Add(line);
    }

    [Fact]
    public async Task APipe_RunsInTheOperatorsShell()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The command is a POSIX pipe.");

        var run = await _runner.RunAsync("echo a | tr a b", OnLine, s_timeout, TestContext.Current.CancellationToken);

        Assert.Equal(new OperatorShellRun(0, TimedOut: false), run);
        Assert.Equal(["b"], _lines);
    }

    [Fact]
    public async Task ACommandThatReadsTheKeyboard_ReadsTheEndOfItsInput()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The command is POSIX.");

        var run = await _runner.RunAsync("cat; echo done", OnLine, s_timeout, TestContext.Current.CancellationToken);

        Assert.False(run.TimedOut);
        Assert.Equal(["done"], _lines);
    }

    [Fact]
    public async Task APager_EndsWithoutTakingTheConsole()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows() && File.Exists("/usr/bin/less"), "less is not installed.");
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "first line\nsecond line\n", TestContext.Current.CancellationToken);

            var run = await _runner.RunAsync($"less '{file}'", OnLine, s_timeout, TestContext.Current.CancellationToken);

            Assert.False(run.TimedOut);
            Assert.Contains("second line", _lines);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task TheStandardError_ReachesTheLines_AndTheExitCodeIsKept()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The command is POSIX.");

        var run = await _runner.RunAsync("echo oops >&2; exit 3", OnLine, s_timeout, TestContext.Current.CancellationToken);

        Assert.Equal(3, run.ExitCode);
        Assert.Equal(["oops"], _lines);
    }

    [Fact]
    public async Task ThePagers_AreCat()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The command is POSIX.");

        await _runner.RunAsync("echo \"$PAGER $GIT_PAGER\"", OnLine, s_timeout, TestContext.Current.CancellationToken);

        Assert.Equal(["cat cat"], _lines);
    }

    [Fact]
    public async Task ACancelledRun_StopsTheCommand()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The command is POSIX.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var run = _runner.RunAsync("echo started; sleep 600", line =>
        {
            OnLine(line);
            cts.Cancel();
        }, s_timeout, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }
}
