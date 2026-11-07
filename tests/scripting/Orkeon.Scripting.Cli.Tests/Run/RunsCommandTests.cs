using System.Text.Json;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Hosting.Runs;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// FWK-08: <c>orkeon runs</c> reads back what <c>orkeon run</c> left in the history. The wiring tests go through the real
/// commands: a real run (on the <c>--llm-mock</c> profile) writes its history under <c>./.orkeon/runs</c>, the mount the host
/// plans itself, then the verb's own command class reads it back.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunsCommandTests
{
    private const string Crew =
        """
        name: history-crew
        goal: Run once and leave a history
        process: sequential

        agents:
          writer:
            role: "Writer"
            goal: "Write"
            backstory: "An agent."

        tasks:
          draft:
            description: "Draft it."
            expected_output: "A draft."
            agent: writer
        """;

    private const string EnabledSettings =
        """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "RunHistory": { "Enabled": true } } }""";

    private static string WorkingDirectory => Directory.GetCurrentDirectory();

    private sealed record Recorded(List<string> Stream, string RunId, string Settings, string Crew);

    /// <summary>Runs the crew with the history on; <paramref name="events"/> says whether the stream is asked for too.</summary>
    private static async Task<Recorded> RunWithHistoryAsync(
        ScriptScratch scratch, bool events = true, Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configure = null)
    {
        var crew = scratch.WriteScript("crew.yaml", Crew);
        var settings = scratch.WriteScript("settings.json", EnabledSettings);
        var answers = scratch.WriteFile(Path.Combine("out", "answers.jsonl"), "{\"match\":\"draft\",\"content\":\"A draft.\"}\n");
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            LlmMock = answers,
            AllowExternalMounts = true,
            Events = events ? "jsonl" : null,
            ConfigureTestServices = (_, services) => configure?.Invoke(services),
        });

        Assert.True(exit == 0, console.Stderr);
        var stream = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

        // The run's id: the stream carries it; without the stream the history's own list names it, by the crew it ran.
        var runId = events
            ? JsonElement.Parse(stream[0]).GetProperty("correlationId").GetString()!
            : await OnlyRunOfAsync(settings, crew);
        return new Recorded(stream, runId, settings, crew);
    }

    private static async Task<string> OnlyRunOfAsync(string settings, string crew)
    {
        var (_, list, stderr) = await RunsAsync(settings, "list", "--crew", crew);
        var line = Assert.Single(list.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.True(stderr.Length == 0, stderr);
        return JsonElement.Parse(line).GetProperty("run_id").GetString()!;
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunsAsync(string settings, params string[] args) =>
        await RunsInAsync(WorkingDirectory, settings, args);

    private static async Task<(int Exit, string Stdout, string Stderr)> RunsInAsync(string workingDirectory, string settings, params string[] args)
    {
        using var console = new TestConsole(stdin: string.Empty);
        var exit = await RunsCommand.DispatchAsync(console, [.. args, "--settings", settings], workingDirectory);
        return (exit, console.Stdout, console.Stderr);
    }

    private static List<JsonElement> Parse(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonElement.Parse(line))];

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShowRestoresEveryEventOfARun_FromRunStartedToRunFinished_AsTheStreamWroteThem()
    {
        using var scratch = new ScriptScratch();
        var recorded = await RunWithHistoryAsync(scratch);

        var (exit, stdout, stderr) = await RunsAsync(recorded.Settings, "show", recorded.RunId);

        Assert.True(exit == 0, stderr);
        var shown = Parse(stdout);
        Assert.Equal("run.started", shown[0].GetProperty("kind").GetString());
        Assert.Equal(1, shown[0].GetProperty("seq").GetInt32());
        Assert.Equal("run.finished", shown[^1].GetProperty("kind").GetString());
        // The very lines of the --events stream: one reader serves both.
        Assert.Equal(recorded.Stream, stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList());
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task WithoutEvents_AnEnabledHistoryStillRecordsTheRun_AndNothingGoesToStdout()
    {
        using var scratch = new ScriptScratch();
        var recorded = await RunWithHistoryAsync(scratch, events: false);

        var (exit, stdout, stderr) = await RunsAsync(recorded.Settings, "show", recorded.RunId);

        Assert.DoesNotContain(recorded.Stream, line => line.StartsWith("{\"v\":", StringComparison.Ordinal));
        Assert.True(exit == 0, stderr);
        var shown = Parse(stdout);
        Assert.Equal(1, shown[0].GetProperty("seq").GetInt32());
        Assert.Equal("run.started", shown[0].GetProperty("kind").GetString());
        Assert.Contains(shown, e => e.GetProperty("kind").GetString() == "task.completed");
        Assert.Equal("run.finished", shown[^1].GetProperty("kind").GetString());
        Assert.True(shown[^1].GetProperty("success").GetBoolean());
        Assert.Equal(Enumerable.Range(1, shown.Count), shown.Select(e => e.GetProperty("seq").GetInt32()));
        Assert.All(shown, e => Assert.Equal(recorded.RunId, e.GetProperty("correlationId").GetString()));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task MoreEarlyEventsThanTheBuffer_LoseNothingOnTheStream_AndTheHistoryKeepsTheFirstOnesAndTheEnd()
    {
        using var scratch = new ScriptScratch();

        // 200 events written while the host is still being built, before the history can join: the buffer keeps the first 128.
        var recorded = await RunWithHistoryAsync(scratch, configure: services =>
        {
            var publisher = (RunEventPublisher)services.Single(d => d.ServiceType == typeof(RunEventPublisher)).ImplementationInstance!;
            for (var i = 0; i < 200; i++)
                publisher.Emit(RunEventKinds.CostUpdated, new { tokens = i });
        });

        var (_, stdout, _) = await RunsAsync(recorded.Settings, "show", recorded.RunId);
        var shown = Parse(stdout);
        var seqs = shown.Select(e => e.GetProperty("seq").GetInt32()).ToList();

        Assert.True(recorded.Stream.Count >= 200 + 2, "the stream carries every event");
        Assert.Equal(Enumerable.Range(1, 128), seqs.Take(128));
        Assert.Equal("run.started", shown[0].GetProperty("kind").GetString());
        Assert.Equal("run.finished", shown[^1].GetProperty("kind").GetString());
        Assert.True(seqs.Count < recorded.Stream.Count, "the history holds the buffered events, not the overflow");
        Assert.Equal(seqs.Order(), seqs);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ListShowsTheRunWithItsCrewAndStatus_AndTailStartsFromASeq()
    {
        using var scratch = new ScriptScratch();
        var recorded = await RunWithHistoryAsync(scratch);

        var (listExit, list, _) = await RunsAsync(recorded.Settings, "list", "--limit", "500");
        var (tailExit, tail, _) = await RunsAsync(recorded.Settings, "tail", recorded.RunId, "--from", "3");

        Assert.Equal(0, listExit);
        var summary = Parse(list).Single(run => run.GetProperty("run_id").GetString() == recorded.RunId);
        Assert.Equal("succeeded", summary.GetProperty("status").GetString());
        Assert.Equal(recorded.Crew, summary.GetProperty("crew_key").GetString());
        Assert.Equal(0, tailExit);
        Assert.Equal(recorded.Stream.Skip(2), tail.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task TailFollowStopsAtTheEndOfARunThatFinished()
    {
        using var scratch = new ScriptScratch();
        var recorded = await RunWithHistoryAsync(scratch);

        var (exit, stdout, _) = await RunsAsync(recorded.Settings, "tail", recorded.RunId, "--follow");

        Assert.Equal(0, exit);
        Assert.Equal(recorded.Stream, stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // ---- a killed run, and the reading side

    private static string NewHistoryFolder(ScriptScratch scratch, out string settings, out DiskBackedFileSystemService fs)
    {
        var root = Path.Combine(scratch.Root, "work");
        Directory.CreateDirectory(Path.Combine(root, ".orkeon", "runs"));
        settings = scratch.WriteScript("settings.json", EnabledSettings);
        fs = new DiskBackedFileSystemService(Path.Combine(root, ".orkeon", "runs"), "/runs");
        return root;
    }

    private static void Record(IRunEventSink sink, TimeProvider time, string id, bool finish)
    {
        var run = new RunEventPublisher([sink], time, runId: id);
        run.Emit(RunEventKinds.RunStarted, new { target = "crews/killed.yaml" });
        if (finish)
            run.Emit(RunEventKinds.RunFinished, new { success = true });
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ARunKilledBeforeItFinished_IsOrphaned_AndTailFollowEndsInsteadOfWaitingForEver()
    {
        using var scratch = new ScriptScratch();
        var work = NewHistoryFolder(scratch, out var settings, out var fs);
        // A process id no machine hands out, on this host: the process is gone.
        var sink = new JsonLinesFileRunEventSink(fs, new RunHistoryOptions { Enabled = true }, new ManualTimeProvider(DateTimeOffset.UtcNow), new RunProcessIdentity(int.MaxValue - 7, Environment.MachineName, null));
        Record(sink, TimeProvider.System, "01KILLED0000000000000000AA", finish: false);

        var (listExit, list, _) = await RunsInAsync(work, settings, "list");

        using var console = new TestConsole(stdin: string.Empty);
        var follow = RunsCommand.DispatchAsync(console, ["tail", "01KILLED0000000000000000AA", "--follow", "--settings", settings], work);
        // A hang guard (two minutes, like every wait of the suite), never an assertion on duration: the verb ends by itself, or
        // the test says it went on waiting.
        var ended = await Task.WhenAny(follow, Task.Delay(Orkeon.Tests.Shared.Timing.Polling.DefaultTimeout, TestContext.Current.CancellationToken)) == follow;
        if (!ended)
            console.Interrupt();
        var tailExit = await follow;

        Assert.Equal(0, listExit);
        Assert.Equal("orphaned", Parse(list).Single().GetProperty("status").GetString());
        Assert.True(ended, "tail --follow went on waiting for a run whose process is gone");
        Assert.Equal(0, tailExit);
        Assert.Equal("run.started", Parse(console.Stdout).Single().GetProperty("kind").GetString());
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task TheReadingSideNeverPurges_EvenWhatExpired()
    {
        using var scratch = new ScriptScratch();
        var work = NewHistoryFolder(scratch, out var settings, out var fs);
        var ancient = new ManualTimeProvider(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sink = new JsonLinesFileRunEventSink(fs, new RunHistoryOptions { Enabled = true, RetentionDays = 30 }, ancient, new RunProcessIdentity(1, "elsewhere", null));
        Record(sink, ancient, "01ANCIENT00000000000000AAAA", finish: true);

        var (exit, list, _) = await RunsInAsync(work, settings, "list");
        var (showExit, _, _) = await RunsInAsync(work, settings, "show", "01ANCIENT00000000000000AAAA");

        Assert.Equal(0, exit);
        Assert.Equal(0, showExit);
        Assert.Single(Parse(list));
        Assert.True(File.Exists(Path.Combine(work, ".orkeon", "runs", "01ANCIENT00000000000000AAAA.jsonl")));
    }

    // ---- the verb itself

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShowAnUnknownRun_ExitsOneAndNamesIt()
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteScript("settings.json", EnabledSettings);

        var (exit, stdout, stderr) = await RunsAsync(settings, "show", "01NOSUCHRUN");

        Assert.Equal(1, exit);
        Assert.Empty(stdout);
        Assert.Contains("no run '01NOSUCHRUN'", stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task WithoutTheHistoryEnabled_ExitsOneAndSaysHowToTurnItOn()
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteScript("settings.json", """{ "RaggableTree": { "Enabled": false } }""");

        var (exit, _, stderr) = await RunsAsync(settings, "list");

        Assert.Equal(1, exit);
        Assert.Contains("Orkeon:RunHistory:Enabled", stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("show")]
    [InlineData("list", "--limit", "zero")]
    [InlineData("tail", "r1", "--from", "0")]
    [InlineData("list", "extra")]
    [Trait("BR", "FWK-08")]
    public async Task AnUnknownSubcommandOrAMalformedArgument_ExitsOne(params string[] args)
    {
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await RunsCommand.DispatchAsync(console, args, WorkingDirectory);

        Assert.Equal(1, exit);
        Assert.StartsWith("orkeon runs: ", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public void TheVerbIsDispatchedAndListedInTheUsage()
    {
        Assert.Contains("runs", Program.KnownVerbs);
        Assert.Contains(CliUsage.Verbs, verb => verb.Name == "runs");
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task TheProgramDispatchesTheVerb_AndAnUnknownVerbStillExitsOne()
    {
        using var console = new TestConsole(stdin: string.Empty);
        var help = await Program.DispatchAsync(console, ["runs", "--help"]);
        using var other = new TestConsole(stdin: string.Empty);
        var unknown = await Program.DispatchAsync(other, ["runz", "list"]);

        Assert.Equal(0, help);
        Assert.Contains("orkeon runs list", console.Stdout, StringComparison.Ordinal);
        Assert.Equal(1, unknown);
    }
}
