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
    public async Task Lines_written_to_stdin_reach_the_child_in_utf8()
    {
        Assert.SkipUnless(ShellAvailable, ShellRequired);

        var written = new List<bool>();
        var lines = new List<string>();

        var result = await SystemProcessLauncher.Instance
            .RunAsync(
                Script("""read a; read b; echo "got:$a:$b" """) with
                {
                    // The writer is handed over synchronously right after the start, so the
                    // dialogue can begin before the first await resolves.
                    OnInputReady = writer =>
                    {
                        written.Add(writer.TryWriteLine("première"));
                        written.Add(writer.TryWriteLine("deuxième"));
                    },
                },
                line => lines.Add(line.Text),
                TestContext.Current.CancellationToken)
            .WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal([true, true], written);
        Assert.Equal(RunOutcome.Success, result.Outcome);
        Assert.Equal(["got:première:deuxième"], lines);
    }

    [Fact]
    public async Task Writing_to_a_finished_child_reports_false_instead_of_throwing()
    {
        Assert.SkipUnless(ShellAvailable, ShellRequired);

        IProcessInputWriter? input = null;

        var result = await SystemProcessLauncher.Instance
            .RunAsync(
                Script("exit 0") with { OnInputReady = writer => input = writer },
                cancellationToken: TestContext.Current.CancellationToken)
            .WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Success, result.Outcome);
        Assert.NotNull(input);

        // The launcher closed stdin when the run ended: the dialogue is over, not broken.
        Assert.False(input!.TryWriteLine("too late"));
        input.Close();   // idempotent, never throws
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

    /// <summary>
    /// Which of the two termination paths wins — the child's own SIGINT handler, or the kill
    /// that follows the grace period — is a real race against a real shell, and a saturated
    /// machine loses it: the child can be starved past the grace period and come back
    /// <see cref="ProcessTerminationMode.Killed"/>. So this test asserts the contract the
    /// caller actually depends on (the run ended as a cancellation, the child is gone, the
    /// user-facing code is 130) and leaves the choice between the two paths to
    /// <c>ProcessTerminatorTests</c>, which decides it deterministically on a fake handle.
    /// </summary>
    [Fact]
    public async Task A_cancelled_process_is_stopped_and_reported_as_cancelled()
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

        // The run is over: an endless loop only ends here because the launcher stopped it.
        Assert.True(run.IsCompleted);
        Assert.True(result.WasCancelled);
        Assert.Equal(RunOutcome.Cancelled, result.Outcome);
        Assert.Equal(OrkeonExitCodes.Cancelled, result.ExitCode);

        // It did not end on its own, and it did start: both are excluded whichever path won.
        Assert.NotEqual(ProcessTerminationMode.Exited, result.Termination);
        Assert.NotEqual(ProcessTerminationMode.NotStarted, result.Termination);

        // A graceful stop was delivered — this platform has one — so the launcher has no
        // failure to report about it.
        Assert.Null(result.GracefulStopFailureReason);
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
