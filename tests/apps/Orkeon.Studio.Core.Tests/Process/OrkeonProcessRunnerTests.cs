using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Process;

/// <summary>
/// The runner as the front-ends see it: locate, spawn, stream, interpret.
/// Everything here runs on <see cref="FakeProcessLauncher"/> — no real <c>orkeon</c>.
/// </summary>
public sealed class OrkeonProcessRunnerTests
{
    private static readonly string InstallDirectory = Path.Combine("/", "opt", "orkeon");
    private static readonly string BinaryPath = Path.Combine(InstallDirectory, "orkeon");

    private static OrkeonProcessRunner CreateRunner(FakeProcessLauncher launcher, bool binaryPresent = true)
    {
        var probe = new FakeExecutableProbe { BaseDirectory = InstallDirectory };
        if (binaryPresent)
            probe.WithFile(BinaryPath);

        return new OrkeonProcessRunner(launcher, new OrkeonBinaryLocator(probe, ["orkeon"]));
    }

    [Fact]
    public async Task Arguments_reach_the_child_verbatim()
    {
        var launcher = new FakeProcessLauncher();
        var runner = CreateRunner(launcher);

        // A path with a space is the case a joined command line would silently split.
        await runner.RunAsync(
            ["run", "/home/me/my crews/crew.yaml", "--validate"],
            workingDirectory: "/home/me",
            cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(launcher.Requests);
        Assert.Equal(BinaryPath, request.FileName);
        Assert.Equal(["run", "/home/me/my crews/crew.yaml", "--validate"], request.Arguments);
        Assert.Equal("/home/me", request.WorkingDirectory);
        Assert.Equal(ProcessLaunchRequest.DefaultGracePeriod, request.GracePeriod);
    }

    [Theory]
    [InlineData(0, RunOutcome.Success)]
    [InlineData(1, RunOutcome.ScriptError)]
    [InlineData(2, RunOutcome.RuntimeError)]
    [InlineData(130, RunOutcome.Cancelled)]
    [InlineData(42, RunOutcome.Unknown)]
    public async Task The_exit_code_of_the_child_is_interpreted(int exitCode, RunOutcome expected)
    {
        var launcher = new FakeProcessLauncher { ExitCode = exitCode };

        var result = await CreateRunner(launcher).RunAsync(["run", "crew.yaml"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
    }

    [Fact]
    public async Task A_missing_binary_returns_an_actionable_result_and_spawns_nothing()
    {
        var launcher = new FakeProcessLauncher();

        var result = await CreateRunner(launcher, binaryPresent: false).RunAsync(["run", "crew.yaml"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.NotStarted, result.Outcome);
        Assert.Contains("orkeon", result.Description, StringComparison.Ordinal);
        Assert.Contains("PATH", result.Description, StringComparison.Ordinal);
        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task Output_is_delivered_while_the_process_is_still_running()
    {
        // A long-running fake: the lines must reach the sink one at a time, not in a burst
        // when the run finishes. The assertion is that the run task is still pending when the
        // first lines have already been observed.
        var launcher = new FakeProcessLauncher
        {
            LineDelay = TimeSpan.FromMilliseconds(30),
            RunsUntilCancelled = true,
        }.WithStandardOutput("step 1", "step 2").WithStandardError("warning: no Llm section");

        var received = new List<ProcessOutputLine>();
        var thirdLineSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cts = new CancellationTokenSource();
        var run = CreateRunner(launcher).RunAsync(
            ["run", "crew.yaml"],
            onOutput: line =>
            {
                received.Add(line);
                if (received.Count == 3)
                    thirdLineSeen.TrySetResult();
            },
            cancellationToken: cts.Token);

        await thirdLineSeen.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(run.IsCompleted);
        Assert.Equal(["step 1", "step 2", "warning: no Llm section"], received.Select(l => l.Text));
        Assert.Equal(ProcessOutputChannel.StandardError, received[2].Channel);

        await cts.CancelAsync();
        var result = await run;
        Assert.Equal(RunOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task Cancelling_a_run_yields_the_interrupted_result()
    {
        var launcher = new FakeProcessLauncher { RunsUntilCancelled = true };
        using var cts = new CancellationTokenSource();

        var run = CreateRunner(launcher).RunAsync(["run", "crew.yaml"], cancellationToken: cts.Token);
        await cts.CancelAsync();
        var result = await run;

        Assert.Equal(RunOutcome.Cancelled, result.Outcome);
        Assert.Equal(OrkeonExitCodes.Cancelled, result.ExitCode);
        Assert.True(result.WasCancelled);
    }

    [Fact]
    public async Task The_doctor_runs_the_json_form_and_reads_the_checks()
    {
        var launcher = new FakeProcessLauncher().WithStandardOutput(
            """[{"check":"appsettings","status":"ok","detail":"/home/me/.config/Orkeon/appsettings.json"},""" +
            """{"check":"llm-reachability","status":"fail","detail":"endpoint refused the connection"}]""");

        var report = await CreateRunner(launcher).RunDoctorAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["doctor", "--json"], Assert.Single(launcher.Requests).Arguments);
        Assert.Null(report.ParseError);
        Assert.Equal(2, report.Checks.Count);
        Assert.Equal("appsettings", report.Checks[0].Check);
        Assert.Equal(DoctorStatus.Ok, report.Checks[0].Status);
        Assert.True(report.HasFailures);
        Assert.False(report.HasWarnings);
    }

    [Fact]
    public async Task A_doctor_run_with_unreadable_output_reports_the_parse_error()
    {
        var launcher = new FakeProcessLauncher().WithStandardOutput("✅ appsettings  everything is fine");

        var report = await CreateRunner(launcher).RunDoctorAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(report.Checks);
        Assert.NotNull(report.ParseError);
        Assert.Contains("--json", report.ParseError, StringComparison.Ordinal);
        Assert.Contains("appsettings", report.RawOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_doctor_run_without_a_binary_reports_the_missing_binary()
    {
        var report = await CreateRunner(new FakeProcessLauncher(), binaryPresent: false).RunDoctorAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.NotStarted, report.Run.Outcome);
        Assert.Empty(report.Checks);
        Assert.Contains("orkeon", report.ParseError!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_binary_location_is_exposed_for_a_status_field()
    {
        var location = CreateRunner(new FakeProcessLauncher()).LocateBinary();

        Assert.True(location.Found);
        Assert.Equal(BinaryPath, location.Path);
    }
}
