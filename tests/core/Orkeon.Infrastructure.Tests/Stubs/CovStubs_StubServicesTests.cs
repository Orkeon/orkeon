using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Common;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Stubs;
using Orkeon.Tests.Shared.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainAgentRole = Orkeon.Domain.Agent.ValueObjects.AgentRole;
using DomainAgentGoal = Orkeon.Domain.Agent.ValueObjects.AgentGoal;
using DomainCrewTask = Orkeon.Domain.Task.CrewTask;
using DomainTaskDescription = Orkeon.Domain.Task.ValueObjects.TaskDescription;
using DomainExpectedOutput = Orkeon.Domain.Task.ValueObjects.ExpectedOutput;

namespace Orkeon.Infrastructure.Tests.CovStubs;

/// <summary>
/// Coverage tests for the Infrastructure.Stubs fallback/null services.
/// </summary>
public sealed class CovStubs_InMemoryToolRegistryTests
{
    private static InMemoryToolRegistry CreateSut() => new(NullLogger<InMemoryToolRegistry>.Instance);

    [Fact]
    public void Constructor_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() => new InMemoryToolRegistry(null!));

    [Fact]
    public async Task RegisterTool_NullTool_Throws()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.RegisterToolAsync(null!));
    }

    [Fact]
    public async Task RegisterTool_AddsAndRetrievesByNameAndId()
    {
        var sut = CreateSut();
        var tool = new StubBaseTool("alpha") { Description = "does alpha things" };

        var ok = await sut.RegisterToolAsync(tool);

        Assert.True(ok);
        Assert.Same(tool, await sut.GetToolAsync("alpha"));
        Assert.Same(tool, await sut.GetToolByNameAsync("alpha"));
        Assert.True(await sut.IsRegisteredAsync("alpha"));
    }

    [Fact]
    public async Task RegisterTool_SameName_UpdatesExisting()
    {
        var sut = CreateSut();
        var first = new StubBaseTool("dup") { Description = "first" };
        var second = new StubBaseTool("dup") { Description = "second" };

        await sut.RegisterToolAsync(first);
        await sut.RegisterToolAsync(second);

        Assert.Same(second, await sut.GetToolAsync("dup"));
        var all = await sut.GetAllToolsAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task GetTool_Unknown_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(await sut.GetToolAsync("nope"));
        Assert.Null(await sut.GetToolByNameAsync("nope"));
        Assert.False(await sut.IsRegisteredAsync("nope"));
    }

    [Fact]
    public async Task Unregister_RemovesTool()
    {
        var sut = CreateSut();
        await sut.RegisterToolAsync(new StubBaseTool("temp"));

        Assert.True(await sut.UnregisterToolAsync("temp"));
        Assert.False(await sut.UnregisterToolAsync("temp"));
        Assert.False(await sut.IsRegisteredAsync("temp"));
    }

    [Fact]
    public async Task GetToolsByTags_AlwaysEmpty()
    {
        var sut = CreateSut();
        await sut.RegisterToolAsync(new StubBaseTool("t1"));

        var result = await sut.GetToolsByTagsAsync("any", "tag");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllTools_ReturnsAllRegistered()
    {
        var sut = CreateSut();
        await sut.RegisterToolAsync(new StubBaseTool("a"));
        await sut.RegisterToolAsync(new StubBaseTool("b"));

        var all = await sut.GetAllToolsAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task GetToolsByCapability_MatchesDescription()
    {
        var sut = CreateSut();
        await sut.RegisterToolAsync(new StubBaseTool("search") { Description = "Web SEARCH capability" });
        await sut.RegisterToolAsync(new StubBaseTool("calc") { Description = "math only" });

        var matched = await sut.GetToolsByCapabilityAsync("search");

        Assert.Single(matched);
        Assert.Equal("search", matched[0].Name);
    }

    [Fact]
    public async Task GetToolsByCapability_NoMatch_ReturnsEmpty()
    {
        var sut = CreateSut();
        await sut.RegisterToolAsync(new StubBaseTool("calc") { Description = "math only" });

        var matched = await sut.GetToolsByCapabilityAsync("zzz-not-found");

        Assert.Empty(matched);
    }

    [Fact]
    public async Task GetToolsAsync_FiltersByNamesFromTools()
    {
        var sut = CreateSut();
        await sut.RegisterToolAsync(new StubBaseTool("keep"));
        await sut.RegisterToolAsync(new StubBaseTool("drop"));

        var requested = new[] { new Orkeon.Infrastructure.Tests.Doubles.MockTool("keep") };
        var result = await sut.GetToolsAsync(requested);

        Assert.Single(result);
        Assert.Equal("keep", result[0].Name);
    }

    [Fact]
    public async Task Clear_RemovesEverything()
    {
        var sut = CreateSut();
        await sut.RegisterToolAsync(new StubBaseTool("x"));

        await sut.ClearAsync();

        Assert.Empty(await sut.GetAllToolsAsync());
    }
}

public sealed class CovStubs_NullAgentExecutionServiceTests
{
    private static NullAgentExecutionService CreateSut() => new(NullLogger<NullAgentExecutionService>.Instance);

    private static DomainAgent CreateAgent()
        => DomainAgent.Create(DomainAgentRole.From("worker"), DomainAgentGoal.From("do work"));

    private static DomainCrewTask CreateTask()
        => DomainCrewTask.Create(DomainTaskDescription.From("desc"), DomainExpectedOutput.From("out"));

    private static SimpleExecutionContext CreateContext()
        => new(CrewId.Create(), [], NullMemoryScope.Instance, []);

    [Fact]
    public void Constructor_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() => new NullAgentExecutionService(null!));

    [Fact]
    public async Task ExecuteTask_ReturnsFailureWithError()
    {
        var sut = CreateSut();

        var result = await sut.ExecuteTaskAsync(CreateAgent(), CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("No IAgentExecutionService registered", result.Error);
        Assert.Empty(result.ToolsUsed);
        Assert.Equal(TimeSpan.Zero, result.ExecutionTime);
    }

    [Fact]
    public async Task ExecuteTaskGeneric_ReturnsFailureWithError()
    {
        var sut = CreateSut();

        var result = await sut.ExecuteTaskAsync<string>(CreateAgent(), CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Null(result.StructuredOutput);
        Assert.Equal("No IAgentExecutionService registered", result.Error);
    }

    [Fact]
    public async Task PlanTaskExecution_ReturnsEmptyPlanForAgent()
    {
        var sut = CreateSut();
        var agent = CreateAgent();

        var plan = await sut.PlanTaskExecutionAsync(agent, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        Assert.Equal(agent.Id, plan.AssignedAgent);
        Assert.Empty(plan.Steps);
        Assert.Equal(0.0, plan.ConfidenceScore);
    }

    [Fact]
    public async Task CanExecuteTask_AlwaysFalse()
    {
        var sut = CreateSut();

        Assert.False(await sut.CanExecuteTaskAsync(CreateAgent(), CreateTask(), TestContext.Current.CancellationToken));
    }
}

public sealed class CovStubs_InMemoryMemorySystemTests
{
    private static InMemoryMemorySystem CreateSut() => new(NullLogger<InMemoryMemorySystem>.Instance);

    [Fact]
    public void Constructor_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() => new InMemoryMemorySystem(null!));

    [Fact]
    public async Task GetAgentMemoryStore_Unknown_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(await sut.GetAgentMemoryStoreAsync(AgentId.Create()));
    }

    [Fact]
    public async Task CreateAgentMemoryStore_IsIdempotentAndRetrievable()
    {
        var sut = CreateSut();
        var agentId = AgentId.Create();

        var created = await sut.CreateAgentMemoryStoreAsync(agentId);
        var again = await sut.CreateAgentMemoryStoreAsync(agentId);
        var fetched = await sut.GetAgentMemoryStoreAsync(agentId);

        Assert.Same(created, again);
        Assert.Same(created, fetched);
    }

    [Fact]
    public async Task StoreAndRetrieveMemories_RespectsLimitAndTakesLast()
    {
        var sut = CreateSut();
        var agentId = AgentId.Create();

        await sut.StoreMemoryAsync(agentId, MemoryItem.Create("first"));
        await sut.StoreMemoryAsync(agentId, MemoryItem.Create("second"));
        await sut.StoreMemoryAsync(agentId, MemoryItem.Create("third"));

        var retrieved = (await sut.RetrieveMemoriesAsync(agentId, "q", limit: 2)).ToList();

        Assert.Equal(2, retrieved.Count);
        Assert.Equal("second", retrieved[0].Content);
        Assert.Equal("third", retrieved[1].Content);
    }

    [Fact]
    public async Task RetrieveMemories_Unknown_ReturnsEmpty()
    {
        var sut = CreateSut();
        Assert.Empty(await sut.RetrieveMemoriesAsync(AgentId.Create(), "q"));
    }
}

public sealed class CovStubs_NullLlmCacheTests
{
    private static NullLlmCache CreateSut() => new(NullLogger<NullLlmCache>.Instance);

    [Fact]
    public void Constructor_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() => new NullLlmCache(null!));

    [Fact]
    public async Task Get_AlwaysNull_AndCountsMisses()
    {
        var sut = CreateSut();

        Assert.Null(await sut.GetAsync("k1", TestContext.Current.CancellationToken));
        Assert.Null(await sut.GetAsync("k2", TestContext.Current.CancellationToken));

        var stats = await sut.GetStatisticsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, stats.TotalMisses);
        Assert.Equal(0, stats.TotalHits);
        Assert.Equal(0.0, stats.HitRate);
    }

    [Fact]
    public async Task Set_IsNoOp_ValueNotCached()
    {
        var sut = CreateSut();

        await sut.SetAsync("k", "v", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        Assert.Null(await sut.GetAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Clear_ResetsMisses()
    {
        var sut = CreateSut();
        await sut.GetAsync("k", TestContext.Current.CancellationToken);

        await sut.ClearAsync(TestContext.Current.CancellationToken);

        var stats = await sut.GetStatisticsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, stats.TotalMisses);
    }

    [Fact]
    public void GenerateCacheKey_IsDeterministicHex()
    {
        var sut = CreateSut();

        var a = sut.GenerateCacheKey("gpt", "hello", new { temp = 0.7 });
        var b = sut.GenerateCacheKey("gpt", "hello", new { temp = 0.7 }.ToString());
        var c = sut.GenerateCacheKey("gpt", "world");

        Assert.Equal(64, a.Length);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Matches("^[0-9a-f]+$", a);
    }
}

public sealed class CovStubs_InMemoryKnowledgeStoreTests
{
    private static readonly string[] AlphaBeta = ["alpha", "beta"];

    private static InMemoryKnowledgeStore CreateSut() => new(NullLogger<InMemoryKnowledgeStore>.Instance);

    [Fact]
    public void Constructor_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() => new InMemoryKnowledgeStore(null!));

    [Fact]
    public async Task StoreAndRetrieve_RoundTrips()
    {
        var sut = CreateSut();

        Assert.True(await sut.StoreAsync("k", "value", TestContext.Current.CancellationToken));
        Assert.Equal("value", await sut.RetrieveAsync<string>("k", TestContext.Current.CancellationToken));
        Assert.True(await sut.ExistsAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Retrieve_WrongType_ReturnsNull()
    {
        var sut = CreateSut();
        await sut.StoreAsync("k", "string-value", TestContext.Current.CancellationToken);

        Assert.Null(await sut.RetrieveAsync<List<int>>("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Retrieve_Missing_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(await sut.RetrieveAsync<string>("absent", TestContext.Current.CancellationToken));
        Assert.False(await sut.ExistsAsync("absent", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Search_FiltersByTypeAndLimit()
    {
        var sut = CreateSut();
        await sut.StoreAsync("a", "alpha", TestContext.Current.CancellationToken);
        await sut.StoreAsync("b", "beta", TestContext.Current.CancellationToken);
        await sut.StoreAsync("c", 123, TestContext.Current.CancellationToken); // not a string

        var results = await sut.SearchAsync<string>("ignored", maxResults: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Contains(results[0], AlphaBeta);
    }

    [Fact]
    public async Task Delete_RemovesKnowledge()
    {
        var sut = CreateSut();
        await sut.StoreAsync("k", "v", TestContext.Current.CancellationToken);

        Assert.True(await sut.DeleteAsync("k", TestContext.Current.CancellationToken));
        Assert.False(await sut.DeleteAsync("k", TestContext.Current.CancellationToken));
        Assert.False(await sut.ExistsAsync("k", TestContext.Current.CancellationToken));
    }
}

public sealed class CovStubs_HashBasedEmbeddingProviderTests
{
    private static HashBasedEmbeddingProvider CreateSut(int dims = 16)
        => new(NullLogger<HashBasedEmbeddingProvider>.Instance, dims);

    [Fact]
    public void Constructor_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() => new HashBasedEmbeddingProvider(null!));

    [Fact]
    public void Metadata_IsExposed()
    {
        var sut = CreateSut(32);
        Assert.Equal("hash-based-fallback", sut.Name);
        Assert.Equal("sha256-hash", sut.Model);
        Assert.Equal(32, sut.Dimensions);
    }

    [Fact]
    public async Task GetEmbedding_IsDeterministicAndSized()
    {
        var sut = CreateSut(16);

        var a = await sut.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken);
        var b = await sut.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken);
        var c = await sut.GetEmbeddingAsync("different", TestContext.Current.CancellationToken);

        Assert.Equal(16, a.Length);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.All(a, v => Assert.InRange(v, -1f, 1f));
    }

    [Fact]
    public async Task GetEmbeddings_Batch_ReturnsOnePerInput()
    {
        var sut = CreateSut(8);

        var results = await sut.GetEmbeddingsAsync(["x", "y", "z"], TestContext.Current.CancellationToken);

        Assert.Equal(3, results.Count);
        Assert.All(results, e => Assert.Equal(8, e.Length));
    }

    [Fact]
    public async Task GetEmbeddings_EmptyList_ReturnsEmpty()
    {
        var sut = CreateSut();
        Assert.Empty(await sut.GetEmbeddingsAsync([], TestContext.Current.CancellationToken));
    }
}

public sealed class CovStubs_SimpleAgentSelectionServiceTests
{
    private static SimpleAgentSelectionService CreateSut() => new(NullLogger<SimpleAgentSelectionService>.Instance);

    private static DomainAgent CreateAgent(string role = "analyst")
        => DomainAgent.Create(DomainAgentRole.From(role), DomainAgentGoal.From("goal of " + role));

    private static DomainCrewTask CreateTask()
        => DomainCrewTask.Create(DomainTaskDescription.From("task desc"), DomainExpectedOutput.From("task out"));

    [Fact]
    public void Constructor_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() => new SimpleAgentSelectionService(null!));

    [Fact]
    public async Task SelectBestAgent_NoAgents_ReturnsFailure()
    {
        var sut = CreateSut();

        var result = await sut.SelectBestAgentAsync([], CreateTask(), embedder: null!, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task SelectBestAgent_ReturnsFirstAgent()
    {
        var sut = CreateSut();
        var first = CreateAgent("first");
        var second = CreateAgent("second");

        var result = await sut.SelectBestAgentAsync([first, second], CreateTask(), embedder: null!, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(first.Id, result.SelectedAgentId);
        Assert.Equal(1.0, result.ConfidenceScore);
    }

    [Fact]
    public async Task GetAgentCapability_MapsRoleAndGoal()
    {
        var sut = CreateSut();
        var agent = CreateAgent("mapper");

        var cap = await sut.GetAgentCapabilityAsync(agent, embedder: null!, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(agent.Role.ToString(), cap.Name);
        Assert.Equal(1.0, cap.ConfidenceLevel);
    }

    [Fact]
    public async Task GetTaskRequirement_MapsDescriptionAndExpectedOutput()
    {
        var sut = CreateSut();
        var task = CreateTask();

        var req = await sut.GetTaskRequirementAsync(task, embedder: null!, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(task.Description.Value, req.Name);
        Assert.Equal(RequirementType.Capability, req.Type);
        Assert.Equal(task.ExpectedOutput.Value, req.Description);
    }
}

public sealed class CovStubs_NullTemplateInstantiatorTests
{
    private static NullTemplateInstantiator CreateSut() => new(NullLogger<NullTemplateInstantiator>.Instance);

    [Fact]
    public void Constructor_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() => new NullTemplateInstantiator(null!));

    [Fact]
    public async Task InstantiateAgent_ByName_Throws()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.InstantiateAgentAsync("agent-template", TemplateInstantiationParameters.Empty, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InstantiateTask_ByName_Throws()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.InstantiateTaskAsync("task-template", TemplateInstantiationParameters.Empty, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ComposeCrew_ByParts_Throws()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.ComposeCrewAsync(
                "crew",
                agents: [],
                tasks: [], cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ValidateInstantiation_ReturnsInvalid()
    {
        var sut = CreateSut();

        var validation = await sut.ValidateInstantiationAsync(
            "tpl", TemplateType.Agent, TemplateInstantiationParameters.Empty, TestContext.Current.CancellationToken);

        Assert.False(validation.IsValid);
        Assert.NotEmpty(validation.Errors);
    }

    [Fact]
    public async Task PreviewInstantiation_ReturnsWarningPreview()
    {
        var sut = CreateSut();

        var preview = await sut.PreviewInstantiationAsync(
            "tpl-name", TemplateType.Task, TemplateInstantiationParameters.Empty, TestContext.Current.CancellationToken);

        Assert.Equal("tpl-name", preview.TemplateName);
        Assert.Equal(string.Empty, preview.RenderedContent);
        Assert.NotEmpty(preview.Warnings);
    }
}
