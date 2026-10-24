using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>Pins the event stream of a run whose host is refused while it is built (CH-07).</summary>
[Collection(CliCollection.Name)]
public sealed class HostRefusalStreamTests
{
    private const string Crew =
        """
        name: refusal-crew
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
    [Trait("BR", "CH-07")]
    public async Task ShouldCloseTheStreamOnRunFinished_WhenTheHostRefusesItsSettings()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        var settings = scratch.WriteFile(
            "appsettings.json",
            """{ "Llm": { "Provider": "OpenAI", "ApiKey": "${OPENAI_API_KEY}" } }""");
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            AllowExternalMounts = true,
            Events = "jsonl",
        });

        var events = console.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonElement.Parse(line))
            .ToList();
        var kinds = events.Select(e => e.GetProperty("kind").GetString()).ToList();

        Assert.Equal(1, exit);
        Assert.Equal(["run.started", "run.finished"], kinds);
        Assert.Equal(1, events[^1].GetProperty("exitCode").GetInt32());
        Assert.Contains("OPENAI_API_KEY", console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENAI_API_KEY", console.Stdout, StringComparison.Ordinal);
    }
}
