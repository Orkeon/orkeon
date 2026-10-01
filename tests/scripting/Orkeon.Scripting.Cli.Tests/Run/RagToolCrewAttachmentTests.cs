using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Hosting;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Evaluation;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Tools.Rag;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// GAP-01: a crew agent that lists <c>rag_search</c> receives it, under the runner's default
/// <c>StrictTools</c>. The tool implements <c>IBaseTool</c> alone; it used to be found in the
/// registry and then refused as an unknown tool. Since GAP-02 every runner host registers the
/// RAG tools itself — this host adds nothing but the doubles.
/// </summary>
public sealed class RagToolCrewAttachmentTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "orkeon-rag-attach-" + Guid.NewGuid().ToString("N"));

    public RagToolCrewAttachmentTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public async Task A_crew_agent_listing_rag_search_receives_it_under_strict_tools()
    {
        var settingsPath = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(settingsPath,
            "{ \"RaggableTree\": { \"Enabled\": false } }", TestContext.Current.CancellationToken);

        using var host = RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan { CliMounts = [$"{FileSystemMount.Quote(_root)}:/crew:ro"] },
            configureServices: (_, services) =>
            {
                // The last registration wins over the real pipelines the host registered.
                services.AddSingleton<IRagPipeline>(new FakeRagPipeline());
                services.AddSingleton<IIngestionPipeline>(new FakeIngestionPipeline());
                services.AddSingleton<IRagEvalHarness>(new FakeRagEvalHarness());
            });

        using var scope = host.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<ICrewFactory>();
        var agents = scope.ServiceProvider.GetRequiredService<IAgentRepository>();

        var crew = await factory.CreateFromConfigAsync(CrewListing("rag_search"), TestContext.Current.CancellationToken);

        var agent = await agents.GetByIdAsync(Assert.Single(crew.Agents), TestContext.Current.CancellationToken);
        Assert.NotNull(agent);
        Assert.IsType<RagSearchTool>(Assert.Single(agent!.Tools, t => t.Name == "rag_search"));
    }

    private static CrewConfiguration CrewListing(string toolName)
    {
        var agentId = AgentId.Create();
        return new CrewConfiguration
        {
            Name = "rag-crew",
            Goal = "answer from the knowledge base",
            Process = ProcessType.Sequential,
            Agents =
            [
                new AgentConfiguration
                {
                    Id = agentId,
                    Role = "analyst",
                    Goal = "answer",
                    Backstory = "b",
                    Tools = [toolName]
                }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "answer the question",
                    ExpectedOutput = "an answer",
                    AssignedAgentId = agentId
                }
            ]
        };
    }
}
