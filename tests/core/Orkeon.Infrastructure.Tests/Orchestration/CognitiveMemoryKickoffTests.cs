using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// FWK-04 — a YAML crew with <c>memory: true</c> through the host's composition with the cognitive
/// memory wired: composite scores are logged at its second run, and a run ends with a consolidation
/// under <c>ConsolidateOnRunEnd</c> — success, failure before the first call, or a consolidation that
/// itself fails.
/// </summary>
public sealed class CognitiveMemoryKickoffTests
{
    private const string Crew = """
        name: news-desk
        goal: Publish the AI news summary
        process: sequential
        memory: true
        agents:
          analyst:
            role: Analyst
            goal: Summarize the news
        tasks:
          summary:
            description: Summarize this week's AI news for the newsletter
            expected_output: Five bullet points about the AI news
            agent: analyst
        """;

    private const string AnalysisJson =
        """{"importance": 0.9, "category": "fact", "key_entities": [], "summary": "s", "suggested_tags": [], "reasoning": "r"}""";

    private const string NoContradictionJson =
        """{"has_contradiction": false, "conflicting_ids": [], "description": "", "resolution": "", "action": "keep_both"}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MockLlmProvider Vendor()
    {
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((messages, _) =>
        {
            var system = messages[0].Content ?? string.Empty;
            var content = system.Contains("memory analysis assistant", StringComparison.Ordinal) ? AnalysisJson
                : system.Contains("contradiction detection assistant", StringComparison.Ordinal) ? NoContradictionJson
                : "Week 40 AI news: agents everywhere.";
            return new LlmResponse { Content = content, PromptTokens = 3, CompletionTokens = 1, TokensUsed = 4 };
        });
        return vendor;
    }

    private static ServiceProvider Host(
        IEmbeddingProvider? embedder,
        Dictionary<string, string?> settings,
        ICognitiveMemoryService? cognitive = null,
        ILoggerFactory? loggers = null)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        services.AddSingleton<IConfiguration>(configuration);
        if (loggers is not null)
            services.AddSingleton(loggers);
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        if (embedder is not null)
            services.AddSingleton(embedder);
        services.AddOrkeonLlmProvider(_ => Vendor(), LlmConfig.Create("host-model"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        if (cognitive is not null)
            services.AddSingleton(cognitive);
        services.AddOrkeonCognitiveMemory(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task<CrewOutput> RunAsync(ServiceProvider container)
    {
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(Crew, Ct);
        var crew = await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
        return await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("news", new Dictionary<string, object>()), Ct);
    }

    private static MockEmbeddingProvider LexicalEmbedder()
    {
        var embedder = new MockEmbeddingProvider();
        embedder.SetEmbeddingFunc(text =>
        {
            var vector = new float[256];
            foreach (var word in text.ToLowerInvariant().Split([' ', '.', ',', ':', '\n', '\r', '-', '\''], StringSplitOptions.RemoveEmptyEntries))
            {
                var slot = 0;
                foreach (var c in word)
                    slot = (slot * 31 + c) & 0xFF;
                vector[slot] += 1f;
            }

            var norm = MathF.Sqrt(vector.Sum(v => v * v));
            return norm > 0 ? [.. vector.Select(v => v / norm)] : vector;
        });
        return embedder;
    }

    private static Dictionary<string, string?> Settings(bool consolidate) => new()
    {
        ["Orkeon:CognitiveMemory:Enabled"] = "true",
        ["Orkeon:CrewMemory:ConsolidateOnRunEnd"] = consolidate ? "true" : "false",
        ["Orkeon:CrewMemory:MinSemanticScore"] = "0.3",
    };

    [Fact]
    public async Task A_second_run_logs_the_composite_scores_of_what_it_recalls()
    {
        using var loggers = new RecordingLoggerFactory();
        await using var container = Host(LexicalEmbedder(), Settings(consolidate: false), loggers: loggers);

        var first = await RunAsync(container);
        var second = await RunAsync(container);

        Assert.True(first.Succeeded, first.Error);
        Assert.True(second.Succeeded, second.Error);
        Assert.Contains(loggers.Logger.Entries, e =>
            e.Message.Contains("composite", StringComparison.Ordinal) && e.Message.Contains("recalled a memory", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_run_consolidates_the_memory_at_its_end_under_the_setting()
    {
        var cognitive = new FakeCognitiveMemoryService();
        await using var container = Host(LexicalEmbedder(), Settings(consolidate: true), cognitive);

        var output = await RunAsync(container);

        Assert.True(output.Succeeded, output.Error);
        Assert.Single(cognitive.Consolidated);
    }

    [Fact]
    public async Task A_run_does_not_consolidate_without_the_setting()
    {
        var cognitive = new FakeCognitiveMemoryService();
        await using var container = Host(LexicalEmbedder(), Settings(consolidate: false), cognitive);

        await RunAsync(container);

        Assert.Empty(cognitive.Consolidated);
    }

    [Fact]
    public async Task A_run_that_throws_before_its_first_call_still_consolidates()
    {
        var cognitive = new FakeCognitiveMemoryService();
        await using var container = Host(embedder: null, Settings(consolidate: true), cognitive);

        var output = await RunAsync(container);

        Assert.False(output.Succeeded);
        Assert.Contains("no semantic embedding provider is configured", output.Error, StringComparison.Ordinal);
        Assert.Single(cognitive.Consolidated);
    }

    [Fact]
    public async Task A_consolidation_that_fails_never_masks_the_outcome_of_the_run()
    {
        var cognitive = new FakeCognitiveMemoryService { ConsolidateFails = new InvalidOperationException("consolidation down") };
        await using var container = Host(LexicalEmbedder(), Settings(consolidate: true), cognitive);

        var output = await RunAsync(container);

        Assert.True(output.Succeeded, output.Error);
        Assert.Contains("agents everywhere", output.FinalOutput, StringComparison.Ordinal);
        Assert.Single(cognitive.Consolidated);
    }

    [Fact]
    public async Task A_consolidation_that_fails_never_masks_the_failure_of_the_run()
    {
        var cognitive = new FakeCognitiveMemoryService { ConsolidateFails = new InvalidOperationException("consolidation down") };
        await using var container = Host(embedder: null, Settings(consolidate: true), cognitive);

        var output = await RunAsync(container);

        Assert.False(output.Succeeded);
        Assert.Contains("no semantic embedding provider is configured", output.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("consolidation down", output.Error, StringComparison.Ordinal);
    }
}
