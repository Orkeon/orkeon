using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// FWK-11 - <c>orkeon eval run --crew</c> with <c>--llm-record</c> and <c>--llm-replay</c>: the crew's calls
/// are written to a cassette, then replayed with no model and no network, on a profile that would call a
/// real endpoint. The recorded provider is a settings-level mock.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class EvalCrewRecordReplayTests
{
    private const string Crew = """
        name: eval-crew
        goal: Answer questions
        process: sequential

        agents:
          answerer:
            role: "Answerer"
            goal: "Answer"
            backstory: "A minimal agent."
            maxIter: 1

        tasks:
          answer:
            description: "Answer the question: {input}"
            expected_output: "An answer."
            agent: answerer
        """;

    private const string Responses =
        "{\"match\":\"capital of France\",\"content\":\"Final answer: Paris\"}\n"
        + "{\"match\":\"capital of Germany\",\"content\":\"Final answer: Berlin\"}\n";

    private const string Dataset = """
        [
          { "task_description": "What is the capital of France?", "expected_output": "Paris" },
          { "task_description": "What is the capital of Germany?", "expected_output": "Berlin" }
        ]
        """;

    private const string MockSettings = "{ \"Llm\": { \"Provider\": \"mock\", \"Responses\": \"/answers/answers.jsonl\" } }";

    // A profile that would reach a real endpoint if anything called it: a replay must never do.
    private const string UnreachableSettings = "{ \"Llm\": { \"Model\": \"llama3\", \"BaseUrl\": \"http://127.0.0.1:9\" } }";

    private sealed class Fixture : IDisposable
    {
        public ScriptScratch Scratch { get; } = new();

        public string Crew { get; }

        public string Dataset { get; }

        public string AnswersDir { get; }

        public string Cassette => Path.Combine(Scratch.Root, "cassettes", "eval.jsonl");

        public Fixture()
        {
            Crew = Scratch.WriteScript("crew.yaml", EvalCrewRecordReplayTests.Crew);
            Dataset = Scratch.WriteFile("qa.json", EvalCrewRecordReplayTests.Dataset);
            AnswersDir = Path.GetDirectoryName(Scratch.WriteFile(Path.Combine("out", "answers.jsonl"), Responses))!;
        }

        public EvalRunOptions Options(string settings, Action<EvalRunOptions> tune)
        {
            var options = new EvalRunOptions
            {
                Dataset = Dataset,
                Crew = Crew,
                Offline = true,
                AllowExternalMounts = true,
                Mounts = [$"{AnswersDir}:/answers:ro"],
                SettingsPath = Scratch.WriteFile("settings.json", settings),
                Evaluators = "similarity",
                WorkingDirectoryOverride = Scratch.Root,
            };
            tune(options);
            return options;
        }

        public async Task<string[]> OutputsAsync()
        {
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(Scratch.Root, ".orkeon", "eval", "qa-eval.json"), TestContext.Current.CancellationToken));
            return [.. report.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.GetProperty("output").GetString() ?? string.Empty)];
        }

        public void Dispose() => Scratch.Dispose();
    }

    [Fact]
    [Trait("BR", "FWK-11")]
    public async Task A_recorded_eval_replays_to_the_same_outputs_with_no_provider_behind_it()
    {
        using var fixture = new Fixture();
        using var recording = new TestConsole();
        var recordExit = await EvalCommand.ExecuteRunAsync(recording, fixture.Options(MockSettings, o => o.LlmRecord = fixture.Cassette));
        Assert.True(recordExit == Program.ExitOk, recording.Stderr);
        Assert.True(File.Exists(fixture.Cassette), "the cassette was not written");
        var recorded = await fixture.OutputsAsync();
        File.Delete(Path.Combine(fixture.AnswersDir, "answers.jsonl"));

        using var replaying = new TestConsole();
        var replayExit = await EvalCommand.ExecuteRunAsync(replaying, fixture.Options(UnreachableSettings, o => o.LlmReplay = fixture.Cassette));

        Assert.True(replayExit == Program.ExitOk, replaying.Stderr);
        var replayed = await fixture.OutputsAsync();
        Assert.Contains("Paris", replayed[0], StringComparison.Ordinal);
        Assert.Contains("Berlin", replayed[1], StringComparison.Ordinal);
        Assert.Equal(recorded, replayed);
    }

    [Fact]
    [Trait("BR", "FWK-11")]
    public async Task The_record_and_replay_switches_and_the_mock_are_exclusive_two_by_two()
    {
        using var fixture = new Fixture();
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, fixture.Options(MockSettings, o =>
        {
            o.LlmRecord = fixture.Cassette;
            o.LlmReplay = fixture.Cassette;
        }));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("exclusive", console.Stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Cassette));
    }

    [Fact]
    [Trait("BR", "FWK-11")]
    public async Task The_record_and_replay_switches_belong_to_a_crew()
    {
        using var fixture = new Fixture();
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, fixture.Options(MockSettings, o =>
        {
            o.Crew = null;
            o.LlmRecord = fixture.Cassette;
        }));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--llm-record", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("--crew", console.Stderr, StringComparison.Ordinal);
    }
}
