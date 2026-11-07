using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// FWK-07, through <c>orkeon run --events jsonl --resume &lt;id&gt;</c>: the resumed run is the run it
/// resumes (its id is the <c>correlationId</c> of every line), the tasks it took from the checkpoint say
/// so, and it ends with the <c>run.finished</c> an uninterrupted run writes.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandResumeEventsTests : IDisposable
{
    private readonly string _state = Path.Combine(Path.GetTempPath(), "orkeon-resume-events-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_state, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private static string Crew(string name) => $"""
        name: {name}
        goal: Three steps
        process: sequential

        agents:
          worker:
            role: "Worker"
            goal: "Work"
            backstory: "A minimal agent."

        tasks:
          first:
            description: "First."
            expected_output: "One."
            agent: worker
          second:
            description: "Second."
            expected_output: "Two."
            agent: worker
            dependencies: [first]
          third:
            description: "Third."
            expected_output: "Three."
            agent: worker
            dependencies: [second]
        """;

    private async Task<(int Exit, List<JsonElement> Events)> RunAsync(string crew, string settings, FakeAgentExecutionService service, string? resume)
    {
        using var console = new TestConsole();
        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            AllowExternalMounts = true,
            Events = "jsonl",
            Resume = resume,
            WorkingDirectory = _state,
            ConfigureTestServices = (_, services) => services.AddScoped<IAgentExecutionService>(_ => service),
        });
        var events = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line)).ToList();
        return (exit, events);
    }

    private static JsonElement Finished(List<JsonElement> events) =>
        events.Single(e => e.GetProperty("kind").GetString() == "run.finished");

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldEndAResumedStreamAsAnUninterruptedRunDoes_UnderTheIdOfTheRunItResumes()
    {
        Directory.CreateDirectory(_state);
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew("resume-events-" + Guid.NewGuid().ToString("N")[..8]));
        var settings = scratch.WriteFile(
            "appsettings.json",
            """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Checkpointing": { "Enabled": true, "Store": "json" } } }""");

        var clean = await RunAsync(crew, settings, new FakeAgentExecutionService(), resume: null);
        var failing = new FakeAgentExecutionService();
        failing.FailOn.Add("second");
        var killed = await RunAsync(crew, settings, failing, resume: null);
        var runId = killed.Events[0].GetProperty("correlationId").GetString()!;
        var service = new FakeAgentExecutionService();
        var resumed = await RunAsync(crew, settings, service, resume: runId);

        Assert.Equal(Program.ExitRuntimeError, killed.Exit);
        Assert.Equal(Program.ExitOk, resumed.Exit);
        Assert.Equal(["second", "third"], service.Calls.ToArray());
        Assert.All(resumed.Events, e => Assert.Equal(runId, e.GetProperty("correlationId").GetString()));
        var completed = resumed.Events.Where(e => e.GetProperty("kind").GetString() == "task.completed").ToList();
        Assert.Equal([true, false, false], completed.Select(e => e.GetProperty("resumed").GetBoolean()).ToArray());
        Assert.Equal(Finished(clean.Events).GetProperty("success").GetBoolean(), Finished(resumed.Events).GetProperty("success").GetBoolean());
        Assert.Equal(Finished(clean.Events).GetProperty("exitCode").GetInt32(), Finished(resumed.Events).GetProperty("exitCode").GetInt32());
    }
}
