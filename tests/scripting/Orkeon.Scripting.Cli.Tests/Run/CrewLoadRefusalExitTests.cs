using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// CH-02 - characterisation: a crew that cannot be loaded (<c>llm: gpt-4o</c>, a plain string where
/// a block is expected) ends <c>run</c> with exit 2 and <c>run --validate</c> with exit 1, as of today.
/// <para>In the CLI collection: the host it builds logs through the console logger provider, which writes to the process streams.</para>
/// </summary>
[Collection(CliCollection.Name)]
public sealed class CrewLoadRefusalExitTests
{
    private const string UnloadableCrew =
        """
        name: unloadable-crew
        goal: Fail at load
        process: sequential

        agents:
          writer:
            role: "Writer"
            goal: "Write"
            backstory: "An agent whose llm is a plain string."
            llm: gpt-4o

        tasks:
          draft:
            description: "Draft it."
            expected_output: "A draft."
            agent: writer
        """;

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldExitWithTwo_WhenRunCannotLoadTheCrew()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", UnloadableCrew);
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions { ScriptPath = crew, AllowExternalMounts = true });

        Assert.True(exit == 2, $"exit {exit}: {console.Stderr}");
        Assert.Contains("llm", console.Stderr + console.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldExitWithOne_WhenValidateCannotLoadTheCrew()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", UnloadableCrew);
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions { ScriptPath = crew, AllowExternalMounts = true, Validate = true });

        Assert.True(exit == 1, $"exit {exit}: {console.Stderr}");
        Assert.Contains("llm", console.Stderr + console.Stdout, StringComparison.OrdinalIgnoreCase);
    }
}
