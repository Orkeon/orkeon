using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Forge;

/// <summary>
/// The schedule verbs from Studio's side (STUDIO-27): the argv of <c>forge schedule</c>,
/// <c>--check</c> and <c>forge unschedule</c>, and the report read back from the CLI's own golden
/// lines — the state, the refusal and its manual command, the warnings. Studio never runs the
/// operating system's scheduler itself; a scripted launcher stands for the engine.
/// </summary>
public sealed class ForgeScheduleClientTests
{
    private static readonly string InstallDirectory = Path.Combine("/", "opt", "orkeon");
    private static readonly string BinaryPath = Path.Combine(InstallDirectory, "orkeon");

    // Verbatim from the CLI's golden test (ForgeEventWriterTests.The_schedule_lines_are_the_pinned_golden_form).
    private const string InstalledLine =
        """{"v":2,"seq":1,"ts":"2026-08-19T12:00:00Z","kind":"schedule.state","path":"/home/u/Orkeon/teams/ma-veille","state":"installed","expression":"daily@08:00","family":"windows","names":["Orkeon ma-veille"]}""";

    private const string StaleLine =
        """{"v":2,"seq":2,"ts":"2026-08-19T12:00:01Z","kind":"schedule.state","path":"/home/u/Orkeon/teams/ma-veille","state":"stale","reason":"moved","expression":"daily@08:00","family":"linux","names":["orkeon-ma-veille.timer","orkeon-ma-veille.service"]}""";

    private const string RemovedLine =
        """{"v":2,"seq":3,"ts":"2026-08-19T12:00:02Z","kind":"schedule.state","path":"/home/u/Orkeon/teams/ma-veille","state":"absent","reason":"not-installed","family":"other","names":["orkeon:ma-veille"],"removed":true}""";

    private const string RefusedLine =
        """{"v":2,"seq":4,"ts":"2026-08-19T12:00:03Z","kind":"error","code":"FORGE-SCHEDULE-REFUSED","message":"The other scheduler refused: crontab: permission denied","recoverable":true,"command":"( crontab -l 2>/dev/null; cat \"/home/u/Orkeon/teams/ma-veille/schedule/cron.txt\" ) | crontab -"}""";

    private static (ForgeClient Client, FakeProcessLauncher Processes) Build(bool installed = true)
    {
        var executables = new FakeExecutableProbe { BaseDirectory = InstallDirectory };
        if (installed)
            executables.WithFile(BinaryPath);
        var processes = new FakeProcessLauncher();
        return (new ForgeClient(processes, new OrkeonBinaryLocator(executables)), processes);
    }

    [Fact]
    public void The_three_schedule_verbs_follow_the_cli_grammar()
    {
        Assert.Equal(
            ["forge", "schedule", "/teams/ma veille", "--events", "jsonl"],
            ForgeArgumentsBuilder.BuildSchedule("/teams/ma veille", ForgeScheduleVerb.Install));
        Assert.Equal(
            ["forge", "schedule", "/teams/ma veille", "--check", "--events", "jsonl"],
            ForgeArgumentsBuilder.BuildSchedule("/teams/ma veille", ForgeScheduleVerb.Check));
        Assert.Equal(
            ["forge", "unschedule", "/teams/ma veille", "--events", "jsonl"],
            ForgeArgumentsBuilder.BuildSchedule("/teams/ma veille", ForgeScheduleVerb.Remove));
    }

    [Fact]
    public async Task An_install_reads_the_state_the_engine_answered()
    {
        var (client, processes) = Build();
        processes.WithStandardOutput(InstalledLine);

        var report = await client.ScheduleAsync("/home/u/Orkeon/teams/ma-veille", ForgeScheduleVerb.Install, TestContext.Current.CancellationToken);

        Assert.True(report.Succeeded);
        Assert.Equal(TeamScheduleState.Installed, report.State);
        Assert.Equal("daily@08:00", report.Expression);
        Assert.Equal(["Orkeon ma-veille"], report.Names);
        var request = Assert.Single(processes.Requests);
        Assert.Equal(BinaryPath, request.FileName);
        Assert.Equal(["forge", "schedule", "/home/u/Orkeon/teams/ma-veille", "--events", "jsonl"], request.Arguments);
    }

    /// <summary>A check that finds the registration no longer matching the team: stale, with the reason.</summary>
    [Fact]
    public async Task A_check_reads_a_stale_schedule_and_why()
    {
        var (client, processes) = Build();
        processes.WithStandardOutput(StaleLine);

        var report = await client.ScheduleAsync("/t", ForgeScheduleVerb.Check, TestContext.Current.CancellationToken);

        Assert.True(report.Succeeded);
        Assert.Equal(TeamScheduleState.Stale, report.State);
        Assert.Equal("moved", report.Reason);
        Assert.Equal(["orkeon-ma-veille.timer", "orkeon-ma-veille.service"], report.Names);
    }

    [Fact]
    public async Task A_removal_says_whether_something_was_removed_and_keeps_the_warnings()
    {
        var (client, processes) = Build();
        processes.WithStandardOutput(
            """{"v":2,"seq":1,"ts":"t","kind":"warning","code":"FORGE-SCHEDULE-NOT-OWNED","message":"'Orkeon ma-veille' is the schedule of '/teams/ma-veille'."}""",
            RemovedLine);

        var report = await client.ScheduleAsync("/t", ForgeScheduleVerb.Remove, TestContext.Current.CancellationToken);

        Assert.True(report.Succeeded);
        Assert.Equal(TeamScheduleState.Absent, report.State);
        Assert.True(report.Removed);
        Assert.Equal(["'Orkeon ma-veille' is the schedule of '/teams/ma-veille'."], report.Warnings);
    }

    /// <summary>D-04: a refusal carries the engine's words and the command a person can run by hand.</summary>
    [Fact]
    public async Task A_refusal_carries_the_engines_words_and_the_manual_command()
    {
        var (client, processes) = Build();
        processes.WithStandardOutput(RefusedLine);
        processes.ExitCode = 1;

        var report = await client.ScheduleAsync("/t", ForgeScheduleVerb.Install, TestContext.Current.CancellationToken);

        Assert.False(report.Succeeded);
        Assert.Equal(TeamScheduleState.Unknown, report.State);
        Assert.Equal("FORGE-SCHEDULE-REFUSED", report.ErrorCode);
        Assert.Equal("The other scheduler refused: crontab: permission denied", report.FailureReason);
        Assert.Equal("( crontab -l 2>/dev/null; cat \"/home/u/Orkeon/teams/ma-veille/schedule/cron.txt\" ) | crontab -", report.ManualCommand);
    }

    /// <summary>An engine that crashed says nothing on the stream: what it printed on stderr is the reason.</summary>
    [Fact]
    public async Task A_crash_is_said_with_what_the_engine_printed()
    {
        var (client, processes) = Build();
        processes.WithStandardError("orkeon forge: the disk is full");
        processes.ExitCode = 2;

        var report = await client.ScheduleAsync("/t", ForgeScheduleVerb.Check, TestContext.Current.CancellationToken);

        Assert.False(report.Succeeded);
        Assert.Equal("orkeon forge: the disk is full", report.FailureReason);
        Assert.Null(report.ManualCommand);
    }

    [Fact]
    public async Task A_missing_binary_is_a_run_that_never_started()
    {
        var (client, processes) = Build(installed: false);

        var report = await client.ScheduleAsync("/t", ForgeScheduleVerb.Install, TestContext.Current.CancellationToken);

        Assert.False(report.Succeeded);
        Assert.Equal(RunOutcome.NotStarted, report.Run.Outcome);
        Assert.Empty(processes.Requests);
    }

    /// <summary>
    /// A schedule verb is a child of its own: it runs while the client drives a session, and
    /// never takes that session's place — the wizard can compose while a card installs.
    /// </summary>
    [Fact]
    public async Task A_schedule_verb_runs_beside_a_live_session()
    {
        var (client, processes) = Build();
        processes.RunsUntilCancelled = true;
        using var cancellation = new CancellationTokenSource();
        var session = client.RunAsync(new ForgeStartRequest { Need = "une veille" }, _ => { }, cancellationToken: cancellation.Token);
        Assert.True(client.IsRunning);

        processes.RunsUntilCancelled = false;
        processes.WithStandardOutput(InstalledLine);
        var report = await client.ScheduleAsync("/t", ForgeScheduleVerb.Check, TestContext.Current.CancellationToken);

        Assert.True(report.Succeeded);
        Assert.True(client.IsRunning);
        await cancellation.CancelAsync();
        await session;
    }
}
