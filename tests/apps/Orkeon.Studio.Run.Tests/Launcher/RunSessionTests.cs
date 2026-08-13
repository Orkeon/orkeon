using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Run.Launcher;

namespace Orkeon.Studio.Run.Tests.Launcher;

/// <summary>
/// The run lifecycle over a scripted child process: what gets spawned, output arriving while
/// it runs, cancellation coming back as exit code 130 with the UI still alive, and what the
/// recent-launch list ends up holding.
/// </summary>
public class RunSessionTests
{
    private static RunLaunchRequest Request(params string[] arguments) => new()
    {
        TargetPath = "/crews/demo/crew.yaml",
        Arguments = arguments.Length == 0 ? ["run", "/crews/demo/crew.yaml"] : arguments,
        WorkingDirectory = "/crews/demo",
    };

    [Fact]
    public async Task The_run_spawns_the_located_binary_with_the_given_arguments()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var session = fixture.Build().Session;

        await session.RunAsync(
            Request("run", "/crews/demo/crew.yaml", "--validate"),
            cancellationToken: TestContext.Current.CancellationToken);

        var spawned = Assert.Single(fixture.Processes.Requests);
        Assert.Equal(LauncherFixture.BinaryPath, spawned.FileName);
        Assert.Equal(["run", "/crews/demo/crew.yaml", "--validate"], spawned.Arguments);
        Assert.Equal("/crews/demo", spawned.WorkingDirectory);
    }

    [Fact]
    public async Task Output_reaches_the_caller_line_by_line_while_the_process_runs()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Processes.WithStandardOutput("crew loaded").WithStandardError("warning: no Llm section");
        var session = fixture.Build().Session;

        var lines = new List<ProcessOutputLine>();
        await session.RunAsync(Request(), lines.Add, TestContext.Current.CancellationToken);

        Assert.Equal(2, lines.Count);
        Assert.Equal(ProcessOutputChannel.StandardOutput, lines[0].Channel);
        Assert.Equal("crew loaded", lines[0].Text);
        Assert.Equal(ProcessOutputChannel.StandardError, lines[1].Channel);
    }

    [Fact]
    public async Task A_finished_run_reports_its_exit_code()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Processes.ExitCode = OrkeonExitCodes.RuntimeError;
        var session = fixture.Build().Session;

        var result = await session.RunAsync(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OrkeonExitCodes.RuntimeError, result.ExitCode);
        Assert.Equal(RunOutcome.RuntimeError, result.Outcome);
        Assert.False(session.IsRunning);
        Assert.Same(result, session.LastResult);
    }

    [Fact]
    public async Task Cancelling_a_run_ends_it_with_code_130_and_leaves_the_session_usable()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Processes.RunsUntilCancelled = true;
        fixture.Processes.WithStandardOutput("kickoff");
        var session = fixture.Build().Session;

        // The scripted line is the readiness marker: cancelling before the child is really
        // running would test the token, not the stop path.
        var running = new TaskCompletionSource();
        var run = session.RunAsync(Request(), _ => running.TrySetResult(), TestContext.Current.CancellationToken);
        await running.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(session.RequestCancellation());
        var result = await run;

        Assert.Equal(OrkeonExitCodes.Cancelled, result.ExitCode);
        Assert.Equal(RunOutcome.Cancelled, result.Outcome);
        Assert.True(result.WasCancelled);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task A_cancelled_run_is_still_recorded_with_its_130()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Processes.RunsUntilCancelled = true;
        fixture.Processes.WithStandardOutput("kickoff");
        var session = fixture.Build().Session;

        var running = new TaskCompletionSource();
        var run = session.RunAsync(Request(), _ => running.TrySetResult(), TestContext.Current.CancellationToken);
        await running.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        session.RequestCancellation();
        await run;

        var recorded = Assert.Single(fixture.History.Recorded);
        Assert.Equal(OrkeonExitCodes.Cancelled, recorded.ExitCode);
        Assert.Equal(RunOutcome.Cancelled, recorded.Outcome);
    }

    [Fact]
    public void Cancelling_when_nothing_runs_is_not_an_error()
    {
        var session = new LauncherFixture().WithInstalledCli().Build().Session;

        Assert.False(session.RequestCancellation());
    }

    [Fact]
    public async Task A_second_run_is_refused_while_one_is_in_flight()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.Processes.RunsUntilCancelled = true;
        fixture.Processes.WithStandardOutput("kickoff");
        var session = fixture.Build().Session;

        var running = new TaskCompletionSource();
        var run = session.RunAsync(Request(), _ => running.TrySetResult(), TestContext.Current.CancellationToken);
        await running.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.RunAsync(Request(), cancellationToken: TestContext.Current.CancellationToken));

        session.RequestCancellation();
        await run;
    }

    [Fact]
    public async Task A_run_marked_as_not_recorded_stays_out_of_the_history()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        var session = fixture.Build().Session;

        await session.RunAsync(
            Request() with { RecordInHistory = false },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(fixture.History.Recorded);
    }

    [Fact]
    public async Task A_missing_binary_is_reported_instead_of_spawning_anything()
    {
        // No WithInstalledCli(): the launcher must survive a machine where the CLI is absent.
        var fixture = new LauncherFixture();
        var session = fixture.Build().Session;

        var result = await session.RunAsync(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.NotStarted, result.Outcome);
        Assert.Empty(fixture.Processes.Requests);
        Assert.Contains("orkeon", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_located_binary_is_readable_without_running_anything()
    {
        var fixture = new LauncherFixture().WithInstalledCli();

        var location = fixture.Build().Session.LocateBinary();

        Assert.True(location.Found);
        Assert.Equal(LauncherFixture.BinaryPath, location.Path);
        Assert.Empty(fixture.Processes.Requests);
    }

    [Fact]
    public async Task The_history_is_loaded_from_the_store()
    {
        var fixture = new LauncherFixture().WithInstalledCli();
        fixture.History.WithEntry(Orkeon.Studio.Core.History.LaunchHistoryEntry.Starting(
            "/crews/demo/crew.yaml",
            ["run", "/crews/demo/crew.yaml"]));
        var session = fixture.Build().Session;

        var history = await session.LoadHistoryAsync(TestContext.Current.CancellationToken);

        Assert.Single(history.Entries);
        Assert.Same(history, session.History);
    }
}
