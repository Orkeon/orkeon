using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Forge;

/// <summary>
/// <c>forge rename</c> from Studio's side (STUDIO-28): the argv, run in the workshop's workspace,
/// and the report read back from the CLI's own golden lines — the new folder and name, the
/// session's new folder, a reinstalled schedule, or the refusal after which nothing changed.
/// </summary>
public sealed class ForgeRenameClientTests
{
    private static readonly string InstallDirectory = Path.Combine("/", "opt", "orkeon");
    private static readonly string BinaryPath = Path.Combine(InstallDirectory, "orkeon");

    // Verbatim from the CLI's golden test (ForgeEventWriterTests.The_rename_lines_are_the_pinned_golden_form).
    private const string SessionRenamedLine =
        """{"v":2,"seq":1,"ts":"2026-08-19T12:00:00Z","kind":"session.renamed","from":"ma-veille","to":"veille-du-matin","dir":"/home/u/.config/Orkeon/.orkeon/forge/veille-du-matin","suffixed":false}""";

    private const string TeamRenamedLine =
        """{"v":2,"seq":2,"ts":"2026-08-19T12:00:01Z","kind":"team.renamed","from":"/home/u/Orkeon/teams/ma-veille","path":"/home/u/Orkeon/teams/veille-du-matin","name":"Veille du matin"}""";

    private const string TakenLine =
        """{"v":2,"seq":3,"ts":"2026-08-19T12:00:02Z","kind":"error","code":"FORGE-RENAME-TAKEN","message":"The name's folder '/home/u/Orkeon/teams/veille-du-matin' is taken: another team is already there. Choose another name.","recoverable":true}""";

    // Verbatim from the CLI's golden test (ForgeEventWriterTests.The_schedule_lines_are_the_pinned_golden_form).
    private const string InstalledLine =
        """{"v":2,"seq":1,"ts":"2026-08-19T12:00:00Z","kind":"schedule.state","path":"/home/u/Orkeon/teams/ma-veille","state":"installed","expression":"daily@08:00","family":"windows","names":["Orkeon ma-veille"]}""";

    private static (ForgeClient Client, FakeProcessLauncher Processes) Build(bool installed = true)
    {
        var executables = new FakeExecutableProbe { BaseDirectory = InstallDirectory };
        if (installed)
            executables.WithFile(BinaryPath);
        var processes = new FakeProcessLauncher();
        return (new ForgeClient(processes, new OrkeonBinaryLocator(executables)), processes);
    }

    [Fact]
    public void The_rename_verb_follows_the_cli_grammar_and_takes_the_name_as_written()
    {
        Assert.Equal(
            ["forge", "rename", "/teams/ma veille", "--name", "-Veille du matin-", "--events", "jsonl"],
            ForgeArgumentsBuilder.BuildRename("/teams/ma veille", "-Veille du matin-"));
    }

    /// <summary>A rename that stands: where the team is now, its name, the session's folder and the schedule's state.</summary>
    [Fact]
    public async Task A_rename_reads_the_new_folder_the_session_and_the_schedule_the_engine_answered()
    {
        var (client, processes) = Build();
        processes.WithStandardOutput(SessionRenamedLine, InstalledLine, TeamRenamedLine);

        var report = await client.RenameAsync("/home/u/Orkeon/teams/ma-veille", "Veille du matin", "/home/u/.config/Orkeon", TestContext.Current.CancellationToken);

        Assert.True(report.Succeeded);
        Assert.Equal("/home/u/Orkeon/teams/veille-du-matin", report.Path);
        Assert.Equal("Veille du matin", report.Name);
        Assert.Equal("/home/u/.config/Orkeon/.orkeon/forge/veille-du-matin", report.SessionDirectory);
        Assert.Equal(TeamScheduleState.Installed, report.ScheduleState);
        var request = Assert.Single(processes.Requests);
        Assert.Equal(BinaryPath, request.FileName);
        Assert.Equal("/home/u/.config/Orkeon", request.WorkingDirectory);
        Assert.Equal(["forge", "rename", "/home/u/Orkeon/teams/ma-veille", "--name", "Veille du matin", "--events", "jsonl"], request.Arguments);
    }

    /// <summary>A team with no session and no schedule: the rename alone, nothing else claimed.</summary>
    [Fact]
    public async Task A_rename_alone_claims_no_session_and_no_schedule()
    {
        var (client, processes) = Build();
        processes.WithStandardOutput(TeamRenamedLine);

        var report = await client.RenameAsync("/home/u/Orkeon/teams/ma-veille", "Veille du matin", null, TestContext.Current.CancellationToken);

        Assert.True(report.Succeeded);
        Assert.Null(report.SessionDirectory);
        Assert.Equal(TeamScheduleState.Unknown, report.ScheduleState);
    }

    /// <summary>D-03: a refusal carries the engine's words, and no new folder.</summary>
    [Fact]
    public async Task A_refusal_carries_the_engines_words_and_no_folder()
    {
        var (client, processes) = Build();
        processes.WithStandardOutput(TakenLine);
        processes.ExitCode = 1;

        var report = await client.RenameAsync("/t", "Veille du matin", null, TestContext.Current.CancellationToken);

        Assert.False(report.Succeeded);
        Assert.Null(report.Path);
        Assert.Equal("FORGE-RENAME-TAKEN", report.ErrorCode);
        Assert.StartsWith("The name's folder '/home/u/Orkeon/teams/veille-du-matin' is taken", report.FailureReason, StringComparison.Ordinal);
    }

    /// <summary>An engine too old to know the verb, or one that crashed, says it on stderr: that is the reason.</summary>
    [Fact]
    public async Task A_crash_is_said_with_what_the_engine_printed()
    {
        var (client, processes) = Build();
        processes.WithStandardError("orkeon forge: rename needs the team folder (the one `forge promote --to` wrote).");
        processes.ExitCode = 1;

        var report = await client.RenameAsync("/t", "X", null, TestContext.Current.CancellationToken);

        Assert.False(report.Succeeded);
        Assert.Equal("orkeon forge: rename needs the team folder (the one `forge promote --to` wrote).", report.FailureReason);
    }

    [Fact]
    public async Task A_missing_binary_is_a_run_that_never_started()
    {
        var (client, processes) = Build(installed: false);

        var report = await client.RenameAsync("/t", "X", null, TestContext.Current.CancellationToken);

        Assert.False(report.Succeeded);
        Assert.Equal(RunOutcome.NotStarted, report.Run.Outcome);
        Assert.Empty(processes.Requests);
    }
}
