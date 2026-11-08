using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Memory;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.SharedKernel;
using Orkeon.Infrastructure.Tools;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Infrastructure.Tests.Doubles;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Infrastructure.Tests.Memory.Cognitive;

/// <summary>
/// FWK-04 — the cognitive memory as a binary wires it: a singleton resolvable from the root provider,
/// its three LLM services on the named profile (metered), a recall tool for the crew, and the order
/// of a run's recall by composite score.
/// </summary>
public sealed class CognitiveMemoryWiringTests
{
    private const string AnalysisJson =
        """{"importance": 0.9, "category": "fact", "key_entities": [], "summary": "s", "suggested_tags": [], "reasoning": "r"}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Harness : IDisposable
    {
        public MockLlmProvider Root { get; } = new();
        public MockLlmProvider Named { get; } = new();
        public MockLlmUsageSink Sink { get; } = new();
        public MockEmbeddingProvider Embedder { get; } = new();
        public InMemoryProvider Store { get; } = new();
        public CrewMemoryProviderRegistry Registry { get; } = new();
        public ServiceProvider Services { get; }
        public CrewId Crew { get; } = CrewId.Create();

        public Harness(string? profile)
        {
            Named.SetChatResult(AnalysisJson);
            Root.SetChatResult(AnalysisJson);
            Embedder.SetEmbeddingResult([1f, 0f, 0f]);
            var settings = new Dictionary<string, string?> { ["Orkeon:CognitiveMemory:Enabled"] = "true" };
            if (profile is not null)
                settings["Orkeon:CognitiveMemory:Profile"] = profile;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ILlmUsageSink>(Sink);
            services.AddOrkeonLlmProvider(_ => Root);
            services.AddSingleton<IEmbeddingProvider>(Embedder);
            services.AddSingleton(Registry);
            services.AddSingleton<IMemoryService>(sp => new MemoryService(
                new StubMemoryProviderFactory(Store), NullLogger<MemoryService>.Instance, Registry, Store));
            services.AddSingleton<ILlmProfileRegistry>(sp => new LlmProfileRegistry(
                sp, [new LlmProfileRegistration("cheap", _ => LlmProviderEntrance.Enter(Named, Sink, null))]));
            services.AddOrkeonCognitiveMemory(configuration);
            Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
            Registry.Record(Crew, providerType: null, "news-desk", memoryEnabled: true);
        }

        public void Dispose() => Services.Dispose();
    }

    [Fact]
    public void The_cognitive_service_resolves_from_the_root_provider_as_a_singleton()
    {
        using var harness = new Harness(profile: null);

        var first = harness.Services.GetRequiredService<ICognitiveMemoryService>();
        var second = harness.Services.GetRequiredService<ICognitiveMemoryService>();

        Assert.Same(first, second);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_analysis_runs_on_the_named_profile_and_is_metered()
    {
        using var harness = new Harness(profile: "cheap");
        var service = harness.Services.GetRequiredService<ICognitiveMemoryService>();

        await service.RememberAsync(harness.Crew, "Orkeon is a framework", cancellationToken: Ct);

        Assert.True(harness.Named.ChatCallCount > 0);
        Assert.Equal(0, harness.Root.ChatCallCount);
        Assert.NotEmpty(harness.Sink.Recorded);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_detector_and_the_consolidator_run_on_the_named_profile_too()
    {
        using var harness = new Harness(profile: "cheap");
        harness.Named.SetChatResult("""{"has_contradiction": false, "conflicting_ids": [], "description": "", "resolution": "", "action": "keep_both"}""");
        var detector = harness.Services.GetRequiredService<ContradictionDetector>();
        var candidate = MemoryItem.Create("an older fact", embedding: [1f, 0f, 0f]);

        await detector.CheckAsync("a new fact", [candidate], Ct);

        Assert.True(harness.Named.ChatCallCount > 0);
        Assert.Equal(0, harness.Root.ChatCallCount);

        var before = harness.Named.ChatCallCount;
        harness.Named.SetChatResult("Consolidated: one fact");
        var memory = harness.Services.GetRequiredService<IMemoryService>().GetMemorySystem(harness.Crew).LongTerm;
        for (var i = 0; i < 5; i++)
            await memory.AddAsync(MemoryItem.Create($"Redundant fact version {i}", embedding: [1f, 0f, 0f], importance: 0.5f));
        await harness.Services.GetRequiredService<ICognitiveMemoryService>().ConsolidateAsync(harness.Crew, Ct);

        Assert.True(harness.Named.ChatCallCount > before);
        Assert.Equal(0, harness.Root.ChatCallCount);
    }

    private static MemoryCoordinator CoordinatorOver(Harness harness) => new(
        NullLogger<MemoryCoordinator>.Instance,
        harness.Services.GetRequiredService<IMemoryService>(),
        harness.Registry,
        harness.Embedder,
        Options.Create(new CrewMemoryOptions()),
        harness.Services.GetRequiredService<ICognitiveMemoryService>());

    private static async System.Threading.Tasks.Task AddRelatedAndUnrelatedAsync(Harness harness)
    {
        // The query embeds to [1, 0, 0]. The unrelated memory is fresh and rated 0.6: on recency and
        // importance alone it reaches the 0.4 composite floor.
        var memory = harness.Services.GetRequiredService<IMemoryService>().GetMemorySystem(harness.Crew).LongTerm;
        await memory.AddAsync(MemoryItem.Create("related", embedding: [1f, 0f, 0f], importance: 0.6f));
        await memory.AddAsync(MemoryItem.Create("unrelated", embedding: [0f, 1f, 0f], importance: 0.6f));
    }

    [Fact]
    public async System.Threading.Tasks.Task A_fresh_unrelated_memory_never_reaches_the_prompt_of_the_next_task()
    {
        using var harness = new Harness(profile: null);
        await AddRelatedAndUnrelatedAsync(harness);
        var agent = DomainAgent.Create(AgentRole.From("A"), AgentGoal.From("g"), AgentBackstory.From("b"));
        var task = DomainTask.Create(TaskDescription.From("d"), ExpectedOutput.From("e"));

        var recalled = await CoordinatorOver(harness).RecallAsync(
            agent, task, new Orkeon.Application.Context.SimpleExecutionContext(harness.Crew, [], Orkeon.Application.Context.NullMemoryScope.Instance, []), Ct);

        Assert.Equal(["related"], recalled.Select(m => m.Content));
    }

    [Fact]
    public async System.Threading.Tasks.Task A_fresh_unrelated_memory_is_not_returned_by_memory_recall_either()
    {
        using var harness = new Harness(profile: null);
        await AddRelatedAndUnrelatedAsync(harness);
        using var tool = new MemoryRecallTool(harness.Services.GetRequiredService<ICognitiveMemoryService>());

        using var scope = LlmUsageScope.Begin(crewId: harness.Crew.ToString());
        var response = await tool.CallAsync(new ToolCallRequest("memory_recall", new Dictionary<string, object?>
        {
            ["query"] = "anything",
        }), Ct);

        var result = Assert.IsType<Dictionary<string, object?>>(response.Result);
        var memories = Assert.IsType<System.Collections.IEnumerable>(result["memories"], exactMatch: false).Cast<Dictionary<string, object?>>();
        Assert.Equal(["related"], memories.Select(m => m["content"]));
    }

    [Fact]
    public async System.Threading.Tasks.Task The_recall_tool_of_another_crew_never_reads_the_memories_of_this_one()
    {
        using var harness = new Harness(profile: null);
        await AddRelatedAndUnrelatedAsync(harness);
        var other = CrewId.Create();
        harness.Registry.Record(other, providerType: null, "other-desk", memoryEnabled: true);
        using var tool = new MemoryRecallTool(harness.Services.GetRequiredService<ICognitiveMemoryService>());

        using var scope = LlmUsageScope.Begin(crewId: other.ToString());
        var response = await tool.CallAsync(new ToolCallRequest("memory_recall", new Dictionary<string, object?>
        {
            ["query"] = "related",
        }), Ct);

        var result = Assert.IsType<Dictionary<string, object?>>(response.Result);
        Assert.Empty(Assert.IsType<System.Collections.IEnumerable>(result["memories"], exactMatch: false).Cast<object>());
    }

    [Fact]
    public async System.Threading.Tasks.Task Without_a_profile_the_analysis_runs_on_the_default_provider()
    {
        using var harness = new Harness(profile: null);
        var service = harness.Services.GetRequiredService<ICognitiveMemoryService>();

        await service.RememberAsync(harness.Crew, "Orkeon is a framework", cancellationToken: Ct);

        Assert.True(harness.Root.ChatCallCount > 0);
        Assert.Equal(0, harness.Named.ChatCallCount);
        Assert.NotEmpty(harness.Sink.Recorded);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_failing_analysis_stores_without_importance_and_warns_memory_analysis_degraded()
    {
        var logger = new MockLogger<MemoryAnalyzer>();
        var llm = new MockLlmProvider();
        llm.SetChatException(new InvalidOperationException("model down"));
        var analyzer = new MemoryAnalyzer(llm, Options.Create(new CognitiveMemoryOptions()), logger);

        var analysis = await analyzer.AnalyzeAsync("a fact", null, Ct);

        Assert.Equal(Orkeon.Domain.Constants.Memory.MemoryDefaults.DefaultImportance, analysis.Importance);
        Assert.Contains(logger.LogEntries, e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("memory.analysis.degraded", StringComparison.Ordinal));
    }

    [Fact]
    public async System.Threading.Tasks.Task The_recall_tool_answers_with_the_memories_of_the_crew_of_the_call()
    {
        using var harness = new Harness(profile: null);
        var service = harness.Services.GetRequiredService<ICognitiveMemoryService>();
        await service.RememberAsync(harness.Crew, "Orkeon is a framework", cancellationToken: Ct);
        using var tool = new MemoryRecallTool(service);

        using var scope = LlmUsageScope.Begin(crewId: harness.Crew.ToString());
        var response = await tool.CallAsync(new ToolCallRequest("memory_recall", new Dictionary<string, object?>
        {
            ["query"] = "framework",
            ["limit"] = 3,
        }), Ct);

        Assert.True(response.Success, response.Error);
        var result = Assert.IsType<Dictionary<string, object?>>(response.Result);
        var memories = Assert.IsType<System.Collections.IEnumerable>(result["memories"], exactMatch: false).Cast<object>().ToList();
        Assert.Single(memories);
    }

    // SEC-9: a script's act that lists memory_recall under a crew with memory: false reads nothing.
    [Fact]
    public async System.Threading.Tasks.Task The_recall_tool_of_a_crew_without_memory_reads_nothing()
    {
        using var harness = new Harness(profile: null);
        await AddRelatedAndUnrelatedAsync(harness);
        harness.Registry.Record(harness.Crew, providerType: null, "news-desk", memoryEnabled: false);
        using var tool = new MemoryRecallTool(harness.Services.GetRequiredService<ICognitiveMemoryService>(), registry: harness.Registry);

        using var scope = LlmUsageScope.Begin(crewId: harness.Crew.ToString());
        var response = await tool.CallAsync(new ToolCallRequest("memory_recall", new Dictionary<string, object?>
        {
            ["query"] = "related",
        }), Ct);

        Assert.False(response.Success);
        Assert.Contains("memory: true", response.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_recall_tool_built_by_the_container_reads_the_crew_declarations()
    {
        using var harness = new Harness(profile: null);
        await AddRelatedAndUnrelatedAsync(harness);
        harness.Registry.Record(harness.Crew, providerType: null, "news-desk", memoryEnabled: false);
        var tool = harness.Services.GetServices<IBaseTool>().Single(t => t.Name == "memory_recall");

        using var scope = LlmUsageScope.Begin(crewId: harness.Crew.ToString());
        var response = await tool.CallAsync(new ToolCallRequest("memory_recall", new Dictionary<string, object?>
        {
            ["query"] = "related",
        }), Ct);

        Assert.False(response.Success);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_recall_tool_outside_a_crew_fails_cleanly()
    {
        using var harness = new Harness(profile: null);
        using var tool = new MemoryRecallTool(harness.Services.GetRequiredService<ICognitiveMemoryService>());

        var response = await tool.CallAsync(new ToolCallRequest("memory_recall", new Dictionary<string, object?>
        {
            ["query"] = "framework",
        }), Ct);

        Assert.False(response.Success);
    }

    [Fact]
    public void The_toolbelt_holds_the_recall_tool_only_for_a_crew_with_memory()
    {
        using var harness = new Harness(profile: null);
        var agent = DomainAgent.Create(AgentRole.From("A"), AgentGoal.From("g"), AgentBackstory.From("b"));
        var task = DomainTask.Create(TaskDescription.From("d"), ExpectedOutput.From("e"));
        using var recall = new MemoryRecallTool(harness.Services.GetRequiredService<ICognitiveMemoryService>());
        IBaseTool[] registered = [recall];

        var without = Orkeon.Application.Crew.Execution.TaskToolbelt.Compose(agent, task, registered, memoryEnabled: false);
        var with = Orkeon.Application.Crew.Execution.TaskToolbelt.Compose(agent, task, registered, memoryEnabled: true);

        Assert.DoesNotContain(without, t => t.Name == "memory_recall");
        Assert.Contains(with, t => t.Name == "memory_recall");
    }

    [Fact]
    public async System.Threading.Tasks.Task A_recent_important_memory_comes_before_a_more_similar_old_one()
    {
        using var harness = new Harness(profile: null);
        var service = harness.Services.GetRequiredService<ICognitiveMemoryService>();
        var memory = harness.Services.GetRequiredService<IMemoryService>().GetMemorySystem(harness.Crew).LongTerm;
        var fresh = MemoryItem.Create("old and similar", embedding: [1f, 0f, 0f], importance: 0.1f);
        var old = MemoryItem.Restore(fresh.Id, fresh.Content, fresh.Embedding, fresh.Importance,
            fresh.Metadata with { Access = fresh.Metadata.Access with { CreatedAt = DateTime.UtcNow.AddDays(-365) } });
        await memory.AddAsync(old);
        await memory.AddAsync(MemoryItem.Create("recent and important", embedding: [0.8f, 0.6f, 0f], importance: 1f));
        harness.Embedder.SetEmbeddingResult([1f, 0f, 0f]);

        var recalled = await service.RecallAsync(harness.Crew, "query", new RecallOptions { MinScore = 0f }, Ct);

        Assert.Equal("recent and important", recalled[0].Item.Content);
    }
}
