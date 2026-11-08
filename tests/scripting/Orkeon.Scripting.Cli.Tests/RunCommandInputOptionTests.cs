using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// FWK-23: <c>orkeon run --resume &lt;id&gt; --input "..."</c>, through the command itself. <c>--input</c> answers the question of a
/// run that stopped, so it is refused without <c>--resume</c> (exit 1, nothing runs), and the exit code of such a stop is 75.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandInputOptionTests
{
    private const string Crew =
        """
        name: input-smoke
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

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldRefuseInput_WhenThereIsNoResume()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            AllowExternalMounts = true,
            Input = "oui",
        });

        // The command's own refusal, ahead of the runner's (which says the same thing with `ERROR:`).
        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("orkeon run: --input", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("--resume", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldNotRefuseInput_WhenItComesWithAResume()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            AllowExternalMounts = true,
            Resume = "latest",
            Input = "oui",
        });

        // The refusal that follows is the resume's own (checkpointing is off), not the input's.
        Assert.Equal(Program.ExitScriptError, exit);
        Assert.DoesNotContain("needs --resume", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Checkpointing:Enabled", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public void ShouldExitWith75_WhenTheRunWaitsForInput()
    {
        Assert.Equal(75, Program.ExitWaitingForInput);
    }
}
