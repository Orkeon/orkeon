using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Launch;

/// <summary>
/// Builds a session whose every collaborator is a hand-written double: a declared install
/// layout, a scripted child process, an in-memory history. No test here ever needs a real
/// <c>orkeon</c> binary on the machine.
/// </summary>
internal sealed class SessionFixture
{
    /// <summary>Directory the fake install layout puts the CLI in.</summary>
    public static string InstallDirectory { get; } = Path.Combine("/", "opt", "orkeon");

    /// <summary>Path the binary locator will resolve to.</summary>
    public static string BinaryPath { get; } = Path.Combine(InstallDirectory, "orkeon");

    public SessionFixture() => Executables.BaseDirectory = InstallDirectory;

    /// <summary>The declared install layout binary resolution sees.</summary>
    public FakeExecutableProbe Executables { get; } = new();

    /// <summary>The scripted child process.</summary>
    public FakeProcessLauncher Processes { get; } = new();

    /// <summary>The in-memory recent-launch list.</summary>
    public FakeLaunchHistoryStore History { get; } = new();

    /// <summary>Declares the co-installed CLI as present.</summary>
    public SessionFixture WithInstalledCli()
    {
        Executables.WithFile(BinaryPath);
        return this;
    }

    /// <summary>Builds the session over the declared doubles.</summary>
    public RunSession Build() =>
        new(new OrkeonProcessRunner(Processes, new OrkeonBinaryLocator(Executables)), History);
}

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
        var fixture = new SessionFixture().WithInstalledCli();
        var session = fixture.Build();

        await session.RunAsync(
            Request("run", "/crews/demo/crew.yaml", "--validate"),
            cancellationToken: TestContext.Current.CancellationToken);

        var spawned = Assert.Single(fixture.Processes.Requests);
        Assert.Equal(SessionFixture.BinaryPath, spawned.FileName);
        Assert.Equal(["run", "/crews/demo/crew.yaml", "--validate"], spawned.Arguments);
        Assert.Equal("/crews/demo", spawned.WorkingDirectory);
    }

    [Fact]
    public async Task Output_reaches_the_caller_line_by_line_while_the_process_runs()
    {
        var fixture = new SessionFixture().WithInstalledCli();
        fixture.Processes.WithStandardOutput("crew loaded").WithStandardError("warning: no Llm section");
        var session = fixture.Build();

        var lines = new List<ProcessOutputLine>();
        await session.RunAsync(Request(), lines.Add, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, lines.Count);
        Assert.Equal(ProcessOutputChannel.StandardOutput, lines[0].Channel);
        Assert.Equal("crew loaded", lines[0].Text);
        Assert.Equal(ProcessOutputChannel.StandardError, lines[1].Channel);
    }

    [Fact]
    public async Task A_finished_run_reports_its_exit_code()
    {
        var fixture = new SessionFixture().WithInstalledCli();
        fixture.Processes.ExitCode = OrkeonExitCodes.RuntimeError;
        var session = fixture.Build();

        var result = await session.RunAsync(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OrkeonExitCodes.RuntimeError, result.ExitCode);
        Assert.Equal(RunOutcome.RuntimeError, result.Outcome);
        Assert.False(session.IsRunning);
        Assert.Same(result, session.LastResult);
    }

    [Fact]
    public async Task Cancelling_a_run_ends_it_with_code_130_and_leaves_the_session_usable()
    {
        var fixture = new SessionFixture().WithInstalledCli();
        fixture.Processes.RunsUntilCancelled = true;
        fixture.Processes.WithStandardOutput("kickoff");
        var session = fixture.Build();

        // The scripted line is the readiness marker: cancelling before the child is really
        // running would test the token, not the stop path.
        var running = new TaskCompletionSource();
        var run = session.RunAsync(Request(), _ => running.TrySetResult(), cancellationToken: TestContext.Current.CancellationToken);
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
        var fixture = new SessionFixture().WithInstalledCli();
        fixture.Processes.RunsUntilCancelled = true;
        fixture.Processes.WithStandardOutput("kickoff");
        var session = fixture.Build();

        var running = new TaskCompletionSource();
        var run = session.RunAsync(Request(), _ => running.TrySetResult(), cancellationToken: TestContext.Current.CancellationToken);
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
        var session = new SessionFixture().WithInstalledCli().Build();

        Assert.False(session.RequestCancellation());
    }

    [Fact]
    public async Task A_second_run_is_refused_while_one_is_in_flight()
    {
        var fixture = new SessionFixture().WithInstalledCli();
        fixture.Processes.RunsUntilCancelled = true;
        fixture.Processes.WithStandardOutput("kickoff");
        var session = fixture.Build();

        var running = new TaskCompletionSource();
        var run = session.RunAsync(Request(), _ => running.TrySetResult(), cancellationToken: TestContext.Current.CancellationToken);
        await running.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.RunAsync(Request(), cancellationToken: TestContext.Current.CancellationToken));

        session.RequestCancellation();
        await run;
    }

    [Fact]
    public async Task A_run_marked_as_not_recorded_stays_out_of_the_history()
    {
        var fixture = new SessionFixture().WithInstalledCli();
        var session = fixture.Build();

        await session.RunAsync(
            Request() with { RecordInHistory = false },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(fixture.History.Recorded);
    }

    [Fact]
    public async Task A_session_without_a_store_still_keeps_its_recent_list_in_memory()
    {
        var fixture = new SessionFixture().WithInstalledCli();
        var session = new RunSession(
            new OrkeonProcessRunner(fixture.Processes, new OrkeonBinaryLocator(fixture.Executables)),
            history: null);

        await session.RunAsync(Request(), cancellationToken: TestContext.Current.CancellationToken);

        // Nothing persisted, but the session's own list shows the run: a store-less
        // front-end still gets a replayable recent list for the current session.
        var entry = Assert.Single(session.History.Entries);
        Assert.Equal("/crews/demo/crew.yaml", entry.Target);
    }

    [Fact]
    public async Task A_missing_binary_is_reported_instead_of_spawning_anything()
    {
        // No WithInstalledCli(): the launcher must survive a machine where the CLI is absent.
        var fixture = new SessionFixture();
        var session = fixture.Build();

        var result = await session.RunAsync(Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.NotStarted, result.Outcome);
        Assert.Empty(fixture.Processes.Requests);
        Assert.Contains("orkeon", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_located_binary_is_readable_without_running_anything()
    {
        var fixture = new SessionFixture().WithInstalledCli();

        var location = fixture.Build().LocateBinary();

        Assert.True(location.Found);
        Assert.Equal(SessionFixture.BinaryPath, location.Path);
        Assert.Empty(fixture.Processes.Requests);
    }

    [Fact]
    public async Task The_history_is_loaded_from_the_store()
    {
        var fixture = new SessionFixture().WithInstalledCli();
        fixture.History.WithEntry(Orkeon.Studio.Core.History.LaunchHistoryEntry.Starting(
            "/crews/demo/crew.yaml",
            ["run", "/crews/demo/crew.yaml"]));
        var session = fixture.Build();

        var history = await session.LoadHistoryAsync(TestContext.Current.CancellationToken);

        Assert.Single(history.Entries);
        Assert.Same(history, session.History);
    }
}
