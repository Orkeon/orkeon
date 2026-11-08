using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// FWK-07: <c>orkeon run --resume &lt;id|latest&gt;</c>, through the command itself. It is refused where
/// it cannot mean anything (<c>--validate</c>, a checkpointing that is off, a script that runs its crew
/// itself, a stream that has to name the run before the host starts), and reaches the runner where it can.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandResumeTests
{
    private const string Crew =
        """
        name: resume-smoke
        goal: Say hello
        process: sequential

        agents:
          greeter:
            role: "Greeter"
            goal: "Greet"
            backstory: "A minimal agent."

        tasks:
          greet:
            description: "Say hello."
            expected_output: "A greeting."
            agent: greeter
        """;

    private const string CheckpointedSettings =
        """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Checkpointing": { "Enabled": true, "Store": "memory" } } }""";

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldRefuseResume_WhenTheRunIsAValidation()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            AllowExternalMounts = true,
            Validate = true,
            Resume = "latest",
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--resume cannot be combined with --validate", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldRefuseResume_WhenCheckpointingIsOff()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            AllowExternalMounts = true,
            Resume = "latest",
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("Orkeon:Checkpointing:Enabled", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldReachTheRunner_AndSayThereIsNothingToResume_WhenNoRunWasKept()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        var settings = scratch.WriteFile("appsettings.json", CheckpointedSettings);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            AllowExternalMounts = true,
            Resume = "latest",
        });

        Assert.Equal(Program.ExitRuntimeError, exit);
        Assert.Contains("Nothing to resume", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldRefuseResumeOfAProceduralScript()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("ok.ork.ts", """
            /// <reference orkeon-script="1.0" />
            """);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions { ScriptPath = script, Resume = "latest" });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("cannot be resumed", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldRefuseResumeLatest_WhenTheRunIsStreamedAsEvents()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            AllowExternalMounts = true,
            Events = "jsonl",
            Resume = "latest",
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("needs the run id", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldCarryTheResumedRunIdOnEveryLineOfTheStream()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        var settings = scratch.WriteFile("appsettings.json", CheckpointedSettings);
        var runId = Orkeon.Application.Interfaces.Services.ExecutionId.New().ToString();
        using var console = new TestConsole();

        await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            AllowExternalMounts = true,
            Events = "jsonl",
            Resume = runId,
        });

        var lines = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Contains(runId, line, StringComparison.Ordinal));
    }
}
