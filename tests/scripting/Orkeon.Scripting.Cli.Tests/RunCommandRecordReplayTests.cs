using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// <c>orkeon run --llm-record</c> then <c>--llm-replay</c> on a script: the second run gives the same
/// output from the cassette alone, and the switches are exclusive with <c>--llm-mock</c>. The recorded
/// provider is a settings-level mock, so no model and no network are involved.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandRecordReplayTests
{
    private const string AskScript = """
        /// <reference orkeon-script="1.0" />
        let answer = "";
        const worker = agentBuilder()
            .name("worker").role("Worker").goal("Ask the model")
            .body(async (input, ctx) => {
                answer = await ctx.llm.complete("ping");
                return answer;
            })
            .build();
        const crew = crewBuilder().name("cassette-crew").withAgent(worker).build();
        await crew.run();
        result = { answer: answer };
        """;

    private static (string Script, string Settings, string AnswersDir, string Cassette) Arrange(ScriptScratch scratch)
    {
        var script = scratch.WriteScript("ask.ork.ts", AskScript);
        var answers = scratch.WriteFile(Path.Combine("out", "answers.jsonl"), "{\"match\":\"ping\",\"content\":\"pong from the file\"}\n");
        var settings = scratch.WriteFile("appsettings.json", "{ \"Llm\": { \"Provider\": \"mock\", \"Responses\": \"/answers/answers.jsonl\" } }");
        return (script, settings, Path.GetDirectoryName(answers)!, Path.Combine(scratch.Root, "cassettes", "run.jsonl"));
    }

    private static RunCommandOptions Options(string script, string settings, string answersDir) => new()
    {
        ScriptPath = script,
        SettingsPath = settings,
        Mounts = [$"{answersDir}:/answers:ro"],
        AllowExternalMounts = true,
    };

    [Fact]
    public async Task A_recorded_script_replays_to_the_same_output_without_the_provider_answers()
    {
        using var scratch = new ScriptScratch();
        var (script, settings, answersDir, cassette) = Arrange(scratch);

        using var recording = new TestConsole();
        var recordOptions = Options(script, settings, answersDir);
        recordOptions.LlmRecord = cassette;
        var recordExit = await RunCommand.ExecuteAsync(recording, recordOptions);
        Assert.True(recordExit == Program.ExitOk, recording.Stderr);
        Assert.True(File.Exists(cassette), "the cassette was not written");
        File.Delete(Path.Combine(answersDir, "answers.jsonl"));

        using var replaying = new TestConsole();
        var replayOptions = Options(script, settings, answersDir);
        replayOptions.LlmReplay = cassette;
        var replayExit = await RunCommand.ExecuteAsync(replaying, replayOptions);

        Assert.True(replayExit == Program.ExitOk, replaying.Stderr);
        Assert.Contains("pong from the file", replaying.Stdout, StringComparison.Ordinal);
        Assert.Equal(recording.Stdout, replaying.Stdout);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public async Task The_three_llm_switches_are_exclusive_two_by_two(bool record, bool replay, bool mock)
    {
        using var scratch = new ScriptScratch();
        var (script, settings, answersDir, cassette) = Arrange(scratch);
        var options = Options(script, settings, answersDir);
        options.LlmRecord = record ? cassette : null;
        options.LlmReplay = replay ? cassette : null;
        options.LlmMock = mock ? Path.Combine(answersDir, "answers.jsonl") : null;

        using var console = new TestConsole();
        var exit = await RunCommand.ExecuteAsync(console, options);

        Assert.NotEqual(Program.ExitOk, exit);
        Assert.Contains("exclusive", console.Stderr, StringComparison.Ordinal);
    }
}
