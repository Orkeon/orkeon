using Orkeon.Hosting;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// <c>orkeon run --llm-mock &lt;file&gt;</c>: every LLM call is answered from a JSONL file, offline, the
/// same way every time; the file's folder is mounted read-only. <c>--test</c> binds the global
/// <c>test</c>, and a script without it has none.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandLlmMockTests
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
        const crew = crewBuilder().name("mock-crew").withAgent(worker).build();
        await crew.run();
        result = { answer: answer };
        """;

    private static string Responses(ScriptScratch scratch) =>
        scratch.WriteFile(Path.Combine("out", "answers.jsonl"), "{\"match\":\"ping\",\"content\":\"pong from the file\"}\n");

    [Fact]
    public async Task Llm_mock_answers_every_call_from_the_file_with_exit_0_and_no_network()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("ask.ork.ts", AskScript);
        var responses = Responses(scratch);

        string[] outputs = new string[2];
        for (var run = 0; run < outputs.Length; run++)
        {
            using var console = new TestConsole();
            var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions { ScriptPath = script, LlmMock = responses });

            Assert.True(exit == Program.ExitOk, console.Stderr);
            outputs[run] = console.Stdout;
        }

        Assert.Contains("pong from the file", outputs[0], StringComparison.Ordinal);
        Assert.Equal(outputs[0], outputs[1]);
    }

    [Fact]
    public async Task Llm_mock_naming_a_missing_file_is_refused()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("ask.ork.ts", AskScript);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = script,
            LlmMock = Path.Combine(scratch.Root, "absent.jsonl"),
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--llm-mock", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Llm_mock_in_a_folder_that_holds_the_script_is_refused_with_the_reason()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("ask.ork.ts", AskScript);
        var responses = scratch.WriteFile("answers.jsonl", "{\"match\":\"ping\",\"content\":\"pong\"}\n");
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions { ScriptPath = script, LlmMock = responses });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("holds the folder of the crew or script", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Llm_mock_mounts_the_folder_of_the_file_read_only_under_llm_mock()
    {
        using var scratch = new ScriptScratch();
        var physical = RunnerLlmMock.ResolvePath(Responses(scratch));

        var mount = Assert.Single(RunnerLlmMock.CreateMounts(physical));

        Assert.EndsWith(":/llm-mock:ro", mount, StringComparison.Ordinal);
        Assert.Equal("/llm-mock/answers.jsonl", RunnerLlmMock.VirtualPath(physical));
        Assert.Empty(RunnerLlmMock.CreateMounts(null));
    }

    [Fact]
    public async Task Test_switch_binds_the_test_global_and_without_it_there_is_none()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript(
            "probe.ork.ts",
            "/// <reference orkeon-script=\"1.0\" />\nresult = { kind: typeof test };\n");

        using var without = new TestConsole();
        var exitWithout = await RunCommand.ExecuteAsync(without, new RunCommandOptions { ScriptPath = script });
        using var with = new TestConsole();
        var exitWith = await RunCommand.ExecuteAsync(with, new RunCommandOptions { ScriptPath = script, Test = true });

        Assert.Equal(Program.ExitOk, exitWithout);
        Assert.Contains("undefined", without.Stdout, StringComparison.Ordinal);
        Assert.Equal(Program.ExitOk, exitWith);
        Assert.Contains("object", with.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_mock_expectation_is_served_before_the_llm_mock_profile()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript(
            "both.ork.ts",
            "test.mockLlm.when(\"ping\").respond(\"from-test\");\n" + AskScript.Replace("/// <reference orkeon-script=\"1.0\" />\n", string.Empty, StringComparison.Ordinal));
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = script,
            LlmMock = Responses(scratch),
            Test = true,
        });

        Assert.True(exit == Program.ExitOk, console.Stderr);
        Assert.Contains("from-test", console.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("pong from the file", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_switch_on_a_yaml_crew_is_refused_with_a_non_zero_exit()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", "name: c\ngoal: g\nagents: {}\ntasks: {}\n");
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions { ScriptPath = crew, Test = true });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--test", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("procedural", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_switch_on_a_declarative_script_is_refused_with_a_non_zero_exit()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript(
            "declared.ork.ts",
            "/// <reference orkeon-script=\"1.0\" />\nconst a = agentBuilder().name('a').role('R').goal('g').build();\n"
            + "globalThis.crew = crewBuilder().name('c').withAgent(a).build();\n");
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions { ScriptPath = script, Test = true });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("procedural", console.Stderr, StringComparison.Ordinal);
    }
}
