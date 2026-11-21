using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Hosting;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Host.Tests;

/// <summary>
/// #429: a crew that requires System One, run by <c>orkeon-host</c> with the decision circuit off, is refused
/// before its first task and its <c>run.finished</c> carries exit 69, as <c>orkeon run</c> returns it.
/// </summary>
public sealed class HostDecisionUnavailableExitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-host-decision-{Guid.NewGuid():N}");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HostDecisionUnavailableExitTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "crews"));
        File.WriteAllText(CrewPath, """
            name: support
            goal: Triage one ticket
            process: sequential
            decision:
              mode: required
            agents:
              helper:
                role: Helper
                goal: Help
                backstory: A minimal test agent.
                maxIter: 1
                tools: [system_one_decide]
            tasks:
              answer:
                description: Triage the ticket.
                expected_output: A category.
                agent: helper
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string CrewPath => Path.Combine(_root, "crews", "support.yaml");

    private IHost Build(HeldLlmProvider llm)
    {
        var root = new Dictionary<string, object?>
        {
            ["RaggableTree"] = new { Enabled = false },
            ["Orkeon"] = new Dictionary<string, object?>
            {
                ["Host"] = new Dictionary<string, object?> { ["Crews"] = new object[] { new { Name = "support", Path = CrewPath } } },
                ["Decision"] = new { Enabled = false },
            },
        };
        var settings = Path.Combine(_root, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(settings, JsonSerializer.Serialize(root));
        var crewPlan = HostCrewMounts.For([new HostedCrewOptions { Name = "support", Path = CrewPath }]);

        return RunnerHost.Build(
            settings,
            new RunnerMountPlan
            {
                CliMounts = [.. crewPlan.Mounts],
                AllowExternalMounts = true,
                WorkingDirectory = _root,
                RunHistoryByDefault = true,
            },
            configureLogging: (_, logging) => logging.ClearProviders(),
            configureServices: (context, services) =>
            {
                services.AddHostServices(context.Configuration, crewPlan);
                services.AddOrkeonLlmProvider(_ => llm);
            });
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldFinishTheRunWithExit69_WhenTheHostRunsARequiredCrewWithTheCircuitOff()
    {
        // Arrange
        using var host = Build(new HeldLlmProvider("unused"));
        var runner = host.Services.GetRequiredService<ICrewRunner>();

        // Act
        var result = await runner.RunAsync("support", "A ticket.", "test");

        // Assert
        var reader = host.Services.GetRequiredService<IRunHistoryReader>();
        var summary = Assert.Single(await reader.ListAsync(new RunQuery(), Ct));
        var finished = Assert.Single(await reader.ReadEventsAsync(summary.RunId, 1, Ct), e => e.Kind == RunEventKinds.RunFinished);
        Assert.True(
            !result.Succeeded && finished.Payload?["exitCode"]?.GetValue<int>() == 69,
            $"outcome={result.Outcome} message={result.Message} finished={finished.Payload?.ToJsonString()}");
    }
}
