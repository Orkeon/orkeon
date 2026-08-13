using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Tests.Process;

/// <summary>
/// The real launcher, exercised against neutral system processes (<c>/bin/sh</c>) — never
/// against a real <c>orkeon</c> binary, which the suite must not require. These are the only
/// tests here that spawn anything, hence the integration trait; the platform guard skips
/// them where <c>/bin/sh</c> does not exist.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SystemProcessLauncherTests
{
    private const string Shell = "/bin/sh";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    private static bool ShellAvailable => !OperatingSystem.IsWindows() && File.Exists(Shell);

    /// <summary>
    /// Reported by <see cref="Assert.SkipUnless"/> so a Windows run shows these as skipped —
    /// an early `return` would have reported them as passing without asserting anything.
    /// </summary>
    private const string ShellRequired = "Requires a POSIX shell at " + Shell + " (not available on this platform).";

    private static ProcessLaunchRequest Script(string script, TimeSpan? gracePeriod = null) => new()
    {
        FileName = Shell,
        Arguments = ["-c", script],
        GracePeriod = gracePeriod ?? TimeSpan.FromSeconds(5),
    };

    [Fact]
    public async Task Output_arrives_line_by_line_while_the_process_runs()
    {
        Assert.SkipUnless(ShellAvailable, ShellRequired);

        var firstLine = new TaskCompletionSource<ProcessOutputLine>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lines = new List<string>();
        var gate = new Lock();

        var run = SystemProcessLauncher.Instance.RunAsync(
            Script("echo first; sleep 2; echo second"),
            line =>
            {
                lock (gate)
                    lines.Add(line.Text);
                firstLine.TrySetResult(line);
            },
            TestContext.Current.CancellationToken);

        var observed = await firstLine.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        // The point of the streaming contract: "first" is in hand while the child is still
        // sleeping, not after it exited two seconds later.
        Assert.Equal("first", observed.Text);
        Assert.False(run.IsCompleted);

        var result = await run.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Success, result.Outcome);
        lock (gate)
            Assert.Equal(["first", "second"], lines);
    }

    [Fact]
    public async Task Standard_error_is_reported_on_its_own_channel()
    {
        Assert.SkipUnless(ShellAvailable, ShellRequired);

        var lines = new List<ProcessOutputLine>();

        var result = await SystemProcessLauncher.Instance
            .RunAsync(Script("echo out; echo boom 1>&2"), lines.Add, TestContext.Current.CancellationToken)
            .WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Success, result.Outcome);
        Assert.Contains(lines, l => l.Channel == ProcessOutputChannel.StandardOutput && l.Text == "out");
        Assert.Contains(lines, l => l.Channel == ProcessOutputChannel.StandardError && l.Text == "boom");
    }

    [Fact]
    public async Task Arguments_are_passed_as_a_list_so_spaces_and_quotes_survive()
    {
        Assert.SkipUnless(ShellAvailable, ShellRequired);

        var lines = new List<string>();
        var request = new ProcessLaunchRequest
        {
            FileName = Shell,
            Arguments = ["-c", """for a in "$@"; do echo "[$a]"; done""", "sh", "a b", "c\"d", "e'f"],
        };

        var result = await SystemProcessLauncher.Instance
            .RunAsync(request, line => lines.Add(line.Text), TestContext.Current.CancellationToken)
            .WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Success, result.Outcome);
        Assert.Equal(["[a b]", "[c\"d]", "[e'f]"], lines);
    }

    [Fact]
    public async Task The_exit_code_of_the_child_is_reported()
    {
        Assert.SkipUnless(ShellAvailable, ShellRequired);

        var result = await SystemProcessLauncher.Instance.RunAsync(Script("exit 2"), cancellationToken: TestContext.Current.CancellationToken).WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(RunOutcome.RuntimeError, result.Outcome);
        Assert.Equal(ProcessTerminationMode.Exited, result.Termination);
    }

    [Fact]
    public async Task A_cancelled_process_is_signalled_first_and_gets_to_exit_by_itself()
    {
        Assert.SkipUnless(ShellAvailable, ShellRequired);

        // The shell installs a SIGINT handler that exits 130 — exactly what the CLI does.
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        var run = SystemProcessLauncher.Instance.RunAsync(
            Script("trap 'exit 130' INT; echo ready; while :; do sleep 0.1; done"),
            line =>
            {
                if (line.Text == "ready")
                    ready.TrySetResult();
            },
            cts.Token);

        await ready.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        var result = await run.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(ProcessTerminationMode.StoppedBySignal, result.Termination);
        Assert.Equal(OrkeonExitCodes.Cancelled, result.ExitCode);
        Assert.Equal(RunOutcome.Cancelled, result.Outcome);
        Assert.True(result.WasCancelled);

        // The child really ran its own handler: the raw code is the one the script chose,
        // not a signal death. This is what the CLI's 130 contract depends on.
        Assert.Equal(OrkeonExitCodes.Cancelled, result.RawExitCode);
    }

    [Fact]
    public async Task A_process_that_ignores_the_signal_is_killed_after_the_grace_period()
    {
        Assert.SkipUnless(ShellAvailable, ShellRequired);

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        var run = SystemProcessLauncher.Instance.RunAsync(
            Script("trap '' INT; echo ready; while :; do sleep 0.1; done", TimeSpan.FromMilliseconds(300)),
            line =>
            {
                if (line.Text == "ready")
                    ready.TrySetResult();
            },
            cts.Token);

        await ready.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        var result = await run.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(ProcessTerminationMode.Killed, result.Termination);
        Assert.Equal(OrkeonExitCodes.Cancelled, result.ExitCode);
        Assert.True(result.WasCancelled);
    }

    [Fact]
    public async Task A_binary_that_does_not_exist_is_reported_not_thrown()
    {
        var request = new ProcessLaunchRequest
        {
            FileName = Path.Combine(Path.GetTempPath(), "orkeon-does-not-exist-" + Guid.NewGuid().ToString("N")),
        };

        var result = await SystemProcessLauncher.Instance.RunAsync(request, cancellationToken: TestContext.Current.CancellationToken).WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.NotStarted, result.Outcome);
        Assert.Contains("Cannot start", result.Description, StringComparison.Ordinal);
    }
}
