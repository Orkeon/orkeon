using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// FWK-23, the end-to-end scenario of the plan on the mock profile: a task asks a human through the <c>human_input</c> tool
/// (the mock model calls it), <c>orkeon run</c> stops with exit 75, and <c>--resume latest --input</c> finishes the run with
/// exit 0 without running the finished task again. Through <see cref="RunCommand"/>, with checkpointing on a folder of its own.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandHumanInputE2ETests
{
    private const string Crew =
        """
        name: ask-the-human
        goal: Write a report the human colours
        process: sequential

        agents:
          worker:
            role: "Worker"
            goal: "Work"
            backstory: "A minimal agent."
            maxIter: 4

        tasks:
          collect:
            description: "Collect the facts."
            expected_output: "Facts."
            agent: worker
          ask:
            description: "Ask the human which colour to use."
            expected_output: "The colour."
            agent: worker
            humanInput: true
            dependencies: [collect]
        """;

    private const string Settings =
        """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Checkpointing": { "Enabled": true, "Store": "json" } } }""";

    private const string CollectLine =
        """{"match":"^Task:\\s+Collect the facts","regex":true,"content":"facts collected"}""";

    // The mock model calls the tool, then answers once the tool result is in: two lines for one prompt.
    private const string AskLines =
        """
        {"match":"^Task:\\s+Ask the human","regex":true,"toolCalls":[{"name":"human_input","arguments":{"prompt":"Which colour should the report use?","input_type":"text"}}],"content":""}
        {"match":"^Task:\\s+Ask the human","regex":true,"content":"the human chose a colour"}
        """;

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldStopWithExit75ThenResumeWithTheAnswer_WhenTheMockModelAsksTheHumanInputTool()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        var settings = scratch.WriteFile("appsettings.json", Settings);
        var firstPass = scratch.WriteFile(Path.Combine("out", "first.jsonl"), CollectLine + "\n" + AskLines + "\n");

        using var stopped = new TestConsole();
        var firstExit = await RunCommand.ExecuteAsync(stopped, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            AllowExternalMounts = true,
            LlmMock = firstPass,
            WorkingDirectory = scratch.Root,
        });

        Assert.True(firstExit == Program.ExitWaitingForInput, $"exit={firstExit} stderr={stopped.Stderr}");
        Assert.Contains("Which colour should the report use?", stopped.Stderr, StringComparison.Ordinal);
        Assert.Contains("--resume", stopped.Stderr, StringComparison.Ordinal);
        Assert.Contains("--input", stopped.Stderr, StringComparison.Ordinal);

        // The second pass's model knows nothing of the first task: had the run played it again, the call would fail.
        var secondPass = scratch.WriteFile(Path.Combine("out", "second.jsonl"), AskLines + "\n");
        using var resumed = new TestConsole();
        var secondExit = await RunCommand.ExecuteAsync(resumed, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            AllowExternalMounts = true,
            LlmMock = secondPass,
            WorkingDirectory = scratch.Root,
            Resume = "latest",
            Input = "green",
        });

        Assert.True(secondExit == Program.ExitOk, $"exit={secondExit} stderr={resumed.Stderr}");
        Assert.Contains("the human chose a colour", resumed.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldStopAgainWithExit75_WhenResumedWithoutAnAnswer()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        var settings = scratch.WriteFile("appsettings.json", Settings);
        var pass = scratch.WriteFile(Path.Combine("out", "first.jsonl"), CollectLine + "\n" + AskLines + "\n");
        using var stopped = new TestConsole();
        Assert.Equal(Program.ExitWaitingForInput, await RunCommand.ExecuteAsync(stopped, new RunCommandOptions
        {
            ScriptPath = crew, SettingsPath = settings, AllowExternalMounts = true, LlmMock = pass, WorkingDirectory = scratch.Root,
        }));

        using var again = new TestConsole();
        var exit = await RunCommand.ExecuteAsync(again, new RunCommandOptions
        {
            ScriptPath = crew, SettingsPath = settings, AllowExternalMounts = true, LlmMock = pass, WorkingDirectory = scratch.Root, Resume = "latest",
        });

        Assert.Equal(Program.ExitWaitingForInput, exit);
        Assert.Contains("Which colour should the report use?", again.Stderr, StringComparison.Ordinal);
    }
}
