using System.Security.Cryptography;
using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// FWK-09 - <c>orkeon eval run --crew</c>: the crew runs on every case over the mock provider, its
/// final output replaces the case's output in the report, and the dataset file is left as it was.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class EvalCrewTests
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

    private sealed class Fixture : IDisposable
    {
        public ScriptScratch Scratch { get; } = new();

        public string Crew { get; }

        public string Mock { get; }

        public string Settings { get; }

        public Fixture()
        {
            Crew = Scratch.WriteScript("crew.yaml", EvalCrewTests.Crew);
            Directory.CreateDirectory(Path.Combine(Scratch.Root, "mock"));
            Mock = Scratch.WriteFile(Path.Combine("mock", "answers.jsonl"), Responses);
            Settings = Scratch.WriteFile("settings.json", "{}");
        }

        public EvalRunOptions Options(string dataset, Action<EvalRunOptions>? tune = null)
        {
            var options = new EvalRunOptions
            {
                Dataset = dataset,
                Crew = Crew,
                LlmMock = Mock,
                Offline = true,
                AllowExternalMounts = true,
                SettingsPath = Settings,
                Evaluators = "similarity",
                WorkingDirectoryOverride = Scratch.Root,
            };
            tune?.Invoke(options);
            return options;
        }

        public void Dispose() => Scratch.Dispose();
    }

    private const string Dataset = """
        [
          { "task_description": "What is the capital of France?", "expected_output": "Paris" },
          { "task_description": "What is the capital of Germany?", "expected_output": "Berlin" }
        ]
        """;

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldReplaceTheOutputWithTheCrews_AndExitOk_OnTheMockProfile()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Scratch.Root, "mock"));
        var dataset = fixture.Scratch.WriteFile("qa.json", Dataset);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(dataset, TestContext.Current.CancellationToken));
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, fixture.Options(dataset));

        Assert.True(exit == Program.ExitOk, console.Stderr);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Scratch.Root, ".orkeon", "eval", "qa-eval.json"), TestContext.Current.CancellationToken));
        var cases = report.RootElement.GetProperty("cases");
        Assert.Contains("Paris", cases[0].GetProperty("output").GetString(), StringComparison.Ordinal);
        Assert.Contains("Berlin", cases[1].GetProperty("output").GetString(), StringComparison.Ordinal);
        Assert.Equal("What is the capital of France?", cases[0].GetProperty("task_description").GetString());
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(dataset, TestContext.Current.CancellationToken)));
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldExitOne_WhenTheCrewsOutputScoresUnderMinScore()
    {
        using var fixture = new Fixture();
        var dataset = fixture.Scratch.WriteFile("wrong.json", """[ { "task_description": "What is the capital of France?", "expected_output": "Tokyo is far away" } ]""");
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, fixture.Options(dataset, o => o.MinScore = 0.99));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.True(console.Stderr.Contains("BELOW THRESHOLD", StringComparison.Ordinal), console.Stderr);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldRefuseOffline_WhenTheCrewIsNotOnTheMockProvider()
    {
        using var fixture = new Fixture();
        var dataset = fixture.Scratch.WriteFile("qa.json", Dataset);
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, fixture.Options(dataset, o => o.LlmMock = null));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.True(console.Stderr.Contains("mock", StringComparison.Ordinal), console.Stderr);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldRefuseACaseWithoutTaskDescription_WhenACrewIsGiven()
    {
        using var fixture = new Fixture();
        var dataset = fixture.Scratch.WriteFile("nodesc.json", """[ { "expected_output": "Paris" } ]""");
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, fixture.Options(dataset));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.True(console.Stderr.Contains("task_description", StringComparison.Ordinal), console.Stderr);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldStopWithExitTwoAndWriteNoReport_WhenTheCrewFailsMidRun()
    {
        using var fixture = new Fixture();
        // The second question has no line in the responses file: the mock fails the crew on it.
        var dataset = fixture.Scratch.WriteFile("partial.json", """
            [
              { "task_description": "What is the capital of France?", "expected_output": "Paris" },
              { "task_description": "What is the capital of Spain?", "expected_output": "Madrid" }
            ]
            """);
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, fixture.Options(dataset));

        Assert.Equal(Program.ExitRuntimeError, exit);
        Assert.True(console.Stderr.Contains("case 1", StringComparison.Ordinal), console.Stderr);
        Assert.True(console.Stderr.Contains("no report", StringComparison.Ordinal), console.Stderr);
        Assert.False(File.Exists(Path.Combine(fixture.Scratch.Root, ".orkeon", "eval", "partial-eval.json")), "a report was written for a run that did not finish");
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldRefuseOffline_NamingTheAgentAndItsProfile_WhenAnAgentHasARealProfile()
    {
        using var fixture = new Fixture();
        var script = fixture.Scratch.ScriptDir;
        await File.WriteAllTextAsync(Path.Combine(script, "answers.jsonl"), "{\"match\":\"\",\"content\":\"ok\"}\n", TestContext.Current.CancellationToken);
        var settings = fixture.Scratch.WriteFile("real.json", """
            { "RaggableTree": { "Enabled": false },
              "Llm": { "Provider": "mock", "Responses": "/crew/answers.jsonl",
                       "Profiles": { "real": { "BaseUrl": "https://api.anthropic.com/v1", "Model": "claude-sonnet-5" } } } }
            """);
        var crew = fixture.Scratch.WriteScript("real-crew.yaml", Crew.Replace("maxIter: 1", "maxIter: 1\n    llm:\n      profile: real", StringComparison.Ordinal));
        var dataset = fixture.Scratch.WriteFile("qa.json", Dataset);
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, fixture.Options(dataset, o =>
        {
            o.Crew = crew;
            o.LlmMock = null;
            o.SettingsPath = settings;
        }));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.True(console.Stderr.Contains("Answerer", StringComparison.Ordinal) && console.Stderr.Contains("real", StringComparison.Ordinal), console.Stderr);
        Assert.False(File.Exists(Path.Combine(fixture.Scratch.Root, ".orkeon", "eval", "qa-eval.json")));
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldRefuseAMissingCrew_WithTheRunGuards()
    {
        using var fixture = new Fixture();
        var dataset = fixture.Scratch.WriteFile("qa.json", Dataset);
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, fixture.Options(dataset, o => o.Crew = Path.Combine(fixture.Scratch.Root, "absent.yaml")));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.True(console.Stderr.Contains("absent.yaml", StringComparison.Ordinal), console.Stderr);
    }
}
