using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Task;
using Orkeon.Hosting;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Rag.Pipeline;
using Orkeon.Scripting.Cli.Commands.Run;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Scripting.Cli.Tests.Forge;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Rag;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// STUDIO-42's acceptance criterion: for a hierarchical crew with planning and a RAG tool,
/// the total the run displays is the sum of what the provider reported — the plan, the
/// manager's assignment and review, the agent's turns and the RAG pipeline's answer, every
/// one of them. The meter used to see only the agent's turns, and <c>run.finished</c> read
/// the same short count.
/// <para>
/// In the CLI collection: an observed run reads the process-global console.
/// </para>
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunMeterCoverageTests : IDisposable
{
    private readonly StringWriter _output = new();

    public void Dispose() => _output.Dispose();

    private IReadOnlyList<JsonElement> Events() =>
    [
        .. _output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line)),
    ];

    [Fact]
    public async Task A_hierarchical_crew_with_planning_and_a_rag_tool_shows_every_token_its_provider_reported()
    {
        var ct = TestContext.Current.CancellationToken;
        var vendor = new ScriptedCrewVendor();
        using var console = new TestConsole(stdin: string.Empty);
        await using var observed = new ObservedRunContext(
            new OrkeonEventWriter(_output, new FakeOrkeonClock()), stream: false, clientName: "studio");

        // The real runner host, observed the way `orkeon run --events jsonl` observes it; the
        // vendor enters it like any provider the factory does not build.
        using var host = RunnerHost.Build(
            settingsPath: null,
            mounts: new RunnerMountPlan(),
            configureLogging: (_, logging) => logging.SetMinimumLevel(LogLevel.None),
            configureServices: (_, services) =>
            {
                services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
                services.AddOrkeonLlmProvider(_ => vendor);
                observed.WireServices(services);
            });
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        // The researcher's knowledge base: a real staged pipeline, answering through the
        // host's chat client.
        var pipeline = new StagedRagPipeline(
            new OneChunkDocumentStore("The warranty lasts two years."),
            new StubEmbeddingProvider(),
            services.GetRequiredService<IChatClient>());

        var manager = new AgentBuilder().Role("Manager").Goal("Hand out the work").Build();
        var researcher = new AgentBuilder().Role("Researcher").Goal("Answer from the knowledge base")
            .WithTool(new AgentToolAdapter(new RagSearchTool(pipeline)))
            .Build();
        var task = new CrewTaskBuilder().Description("How long is the warranty?").ExpectedOutput("A duration").Build();
        var crew = DomainCrew.Create(new CrewCreateOptions
        {
            Goal = "Answer from the knowledge base",
            ProcessType = ProcessType.Hierarchical,
            ManagerAgentId = manager.Id,
            Planning = true,
            PlanningLlm = services.GetRequiredService<ILlmProvider>(),
        });
        crew.AddAgent(manager.Id);
        crew.AddAgent(researcher.Id);
        crew.AddTask(task.Id);
        vendor.TaskId = task.Id.ToString();
        vendor.WorkerId = researcher.Id.ToString();

        await services.GetRequiredService<IAgentRepository>().AddAsync(manager, ct);
        await services.GetRequiredService<IAgentRepository>().AddAsync(researcher, ct);
        await services.GetRequiredService<ITaskRepository>().AddAsync(task, ct);
        await services.GetRequiredService<ICrewRepository>().AddAsync(crew, ct);

        var output = await services.GetRequiredService<ICrewOrchestrationService>()
            .KickoffAsync(crew.Id, new Orkeon.Application.Interfaces.Services.CrewInput("warranty question", new Dictionary<string, object>()), ct);
        await observed.FinishAsync(output.Succeeded ? 0 : 2);

        Assert.True(output.Succeeded, output.Error);

        // Every family made its call: the plan, the manager twice, the agent's two turns
        // around its tool, and the RAG pipeline's grounded answer inside that tool.
        Assert.Equal(["planning", "assign", "agent", "rag", "agent", "review"], vendor.Answered);

        var events = Events();
        var meter = events.Where(e => e.GetProperty("kind").GetString() == "cost.updated").ToList();
        var finished = Assert.Single(events, e => e.GetProperty("kind").GetString() == "run.finished");

        // One reading per call, no duplicate; the last reading and the closing total are the
        // provider's own sum.
        Assert.Equal(vendor.Calls, meter.Count);
        Assert.Equal(vendor.TotalTokens, meter[^1].GetProperty("tokens").GetInt64());
        Assert.Equal(vendor.TotalTokens, finished.GetProperty("tokens").GetInt64());
        Assert.Equal(
            finished.GetProperty("tokens").GetInt64(),
            finished.GetProperty("promptTokens").GetInt64() + finished.GetProperty("completionTokens").GetInt64());

        // The provider counted every call: nothing on the meter is the runtime's estimate.
        Assert.False(finished.TryGetProperty("estimatedTokens", out _));

        // Whose calls they were travels too: the researcher's turns and its tool's answer
        // under its role, the manager's under the manager's, the plan under the crew alone.
        var agents = meter.Select(e => e.TryGetProperty("agentId", out var id) ? id.GetString() : null).ToList();
        Assert.Equal([null, "Manager", "Researcher", "Researcher", "Researcher", "Manager"], agents);
        Assert.All(meter, e => Assert.Equal(crew.Id.ToString(), e.GetProperty("crewId").GetString()));

        // And what each call was for: a watcher naming the model the agents work on reads the
        // agent's two turns, not the plan, the manager or the RAG answer inside the tool.
        var operations = meter.Select(e => e.TryGetProperty("operation", out var op) ? op.GetString() : null).ToList();
        Assert.Equal(["planning", "manager", "agent", "rag", "agent", "manager"], operations);
    }
}
