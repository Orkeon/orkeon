using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// <c>--llm-record</c> and <c>--llm-replay</c> on a YAML crew, through the real entry point: a recorded
/// run replays to the same output with the provider's answers gone, a call the cassette lacks fails the
/// run, and the three LLM switches are exclusive. The recorded provider is a settings-level mock, so no
/// model and no network are involved. Joins <see cref="ConsoleSerialCollection"/>: the runner writes some
/// of its lines on the process-global console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerExecutionLlmCassetteTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private const string Config = """
        name: "cassette-crew"
        goal: "Write one report"
        process: "sequential"
        agents:
          scribe:
            role: "Scribe"
            goal: "Write the report"
            backstory: "A minimal test agent."
            tools:
              - "file_write"
            maxIter: 6
        tasks:
          write_it:
            description: "Write the report to the output folder."
            expectedOutput: "A confirmation."
            agent: "scribe"
        """;

    private readonly string _root;
    private readonly string _crewDir;
    private readonly string _outDir;
    private readonly string _answersDir;
    private readonly string _cassetteDir;

    public RunnerExecutionLlmCassetteTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-hosting-llm-cassette-" + Guid.NewGuid().ToString("N"));
        _crewDir = Path.Combine(_root, "crew");
        _outDir = Path.Combine(_root, "out");
        _answersDir = Path.Combine(_root, "answers");
        _cassetteDir = Path.Combine(_root, "cassettes");
        Directory.CreateDirectory(_crewDir);
        Directory.CreateDirectory(_outDir);
        Directory.CreateDirectory(_answersDir);
        File.WriteAllText(Path.Combine(_crewDir, "config.yaml"), Config);
        File.WriteAllText(Path.Combine(_crewDir, "appsettings.json"), """
            { "RaggableTree": { "Enabled": false }, "Llm": { "Provider": "mock", "Responses": "/answers/answers.jsonl" } }
            """);
        File.WriteAllText(Path.Combine(_answersDir, "answers.jsonl"), string.Join('\n',
            """{"match":"Write the report","toolCalls":[{"name":"file_write","arguments":{"path":"/output/report.txt","content":"written by the mock"}}]}""",
            """{"match":"Write the report","content":"The report is in the output folder."}""") + "\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private string Cassette => Path.Combine(_cassetteDir, "run.jsonl");

    private async Task<(int Exit, string Out, string Err)> RunAsync(Action<TestOptions> arrange, string? configText = null)
    {
        if (configText is not null)
            await File.WriteAllTextAsync(Path.Combine(_crewDir, "config.yaml"), configText, TestContext.Current.CancellationToken);
        var console = new FakeRunnerConsole();
        var opts = new TestOptions
        {
            ConfigPath = Path.Combine(_crewDir, "config.yaml"),
            SettingsPath = Path.Combine(_crewDir, "appsettings.json"),
            Mounts = [$"{_outDir}:/output:rw", $"{_answersDir}:/answers:ro"],
            AllowExternalMounts = true,
            RunnerConsole = console,
        };
        arrange(opts);
        var exit = await RunnerExecution.RunOneShotAsync(opts, "Orkeon.Hosting.Tests", null, TestContext.Current.CancellationToken);
        return (exit, console.Output.ToString(), console.ErrorOutput.ToString());
    }

    [Fact]
    public async Task A_recorded_run_replays_to_the_same_output_without_the_provider_answers()
    {
        var (recordExit, recordOut, recordErr) = await RunAsync(o => o.LlmRecord = Cassette);
        Assert.True(recordExit == 0, recordErr);
        Assert.True(File.Exists(Cassette), "the cassette was not written");
        File.Delete(Path.Combine(_outDir, "report.txt"));
        File.Delete(Path.Combine(_answersDir, "answers.jsonl"));
        var recorded = await File.ReadAllBytesAsync(Cassette, TestContext.Current.CancellationToken);

        var (replayExit, replayOut, replayErr) = await RunAsync(o => o.LlmReplay = Cassette);

        Assert.True(replayExit == 0, replayErr);
        Assert.Equal(recorded, await File.ReadAllBytesAsync(Cassette, TestContext.Current.CancellationToken));
        Assert.Equal(WithoutDuration(recordOut), WithoutDuration(replayOut));
        Assert.Contains("The report is in the output folder.", replayOut, StringComparison.Ordinal);
        Assert.Equal("written by the mock", await File.ReadAllTextAsync(Path.Combine(_outDir, "report.txt"), TestContext.Current.CancellationToken));
    }

    // The only line two runs of the same crew never share: how long this one took.
    private static string WithoutDuration(string output) =>
        System.Text.RegularExpressions.Regex.Replace(output, "Duration: [^\r\n]*", "Duration: -", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5));

    [Fact]
    public async Task A_call_the_cassette_does_not_hold_fails_the_run()
    {
        var (recordExit, _, recordErr) = await RunAsync(o => o.LlmRecord = Cassette);
        Assert.True(recordExit == 0, recordErr);

        var (exit, stdout, stderr) = await RunAsync(
            o => o.LlmReplay = Cassette,
            Config.Replace("Write the report to the output folder.", "Write a different report entirely.", StringComparison.Ordinal));

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("The report is in the output folder.", stdout, StringComparison.Ordinal);
        Assert.Contains("holds no answer", stdout + stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_record_outside_the_working_directory_is_refused_without_the_opt_in_and_writes_nothing()
    {
        var (exit, _, stderr) = await RunAsync(o =>
        {
            o.LlmRecord = Cassette;
            o.AllowExternalMounts = false;
        });

        Assert.Equal(1, exit);
        Assert.Contains("--llm-record points outside the current working directory", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_cassetteDir));
    }

    [Fact]
    public async Task A_record_that_escapes_the_working_directory_by_a_relative_path_is_refused()
    {
        var escape = Path.Combine("..", "..", "orkeon-escape-" + Guid.NewGuid().ToString("N"), "run.jsonl");
        try
        {
            var (exit, _, stderr) = await RunAsync(o =>
            {
                o.LlmRecord = escape;
                o.AllowExternalMounts = false;
            });

            Assert.Equal(1, exit);
            Assert.Contains("--llm-record points outside the current working directory", stderr, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(escape))));
        }
        finally
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(escape))!;
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("record", "replay", "--llm-record", "--llm-replay")]
    [InlineData("record", "mock", "--llm-record", "--llm-mock")]
    [InlineData("replay", "mock", "--llm-replay", "--llm-mock")]
    public async Task The_three_llm_switches_are_exclusive_two_by_two(string first, string second, string firstName, string secondName)
    {
        var mock = Path.Combine(_answersDir, "answers.jsonl");
        var (exit, _, stderr) = await RunAsync(o =>
        {
            Apply(o, first, Cassette, mock);
            Apply(o, second, Cassette, mock);
        });

        Assert.Equal(1, exit);
        Assert.Contains(firstName, stderr, StringComparison.Ordinal);
        Assert.Contains(secondName, stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(Cassette));
    }

    private static void Apply(TestOptions o, string which, string cassette, string mock)
    {
        switch (which)
        {
            case "record": o.LlmRecord = cassette; break;
            case "replay": o.LlmReplay = cassette; break;
            default: o.LlmMock = mock; break;
        }
    }

    [Fact]
    public async Task A_replay_of_a_cassette_that_does_not_exist_is_refused_naming_the_file()
    {
        var (exit, _, stderr) = await RunAsync(o => o.LlmReplay = Cassette);

        Assert.Equal(1, exit);
        Assert.Contains("--llm-replay", stderr, StringComparison.Ordinal);
        Assert.Contains(Cassette, stderr, StringComparison.Ordinal);
    }
}
