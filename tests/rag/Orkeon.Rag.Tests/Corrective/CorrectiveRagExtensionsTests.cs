using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Corrective;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Corrective;

/// <summary>
/// Tests for <see cref="CorrectiveRagExtensions.AddOrkeonCorrectiveRag"/>: the
/// LLM-backed components when an <see cref="IChatClient"/> is registered, the
/// deterministic heuristic fallbacks without one, the TryAdd semantics (a host
/// registration wins), and the <c>Orkeon:Rag:Corrective</c> binding.
/// </summary>
public class CorrectiveRagExtensionsTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, string? (p) => p.Value))
            .Build();

    private static ServiceCollection BaseServices(IChatClient? chatClient = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentStore>(new StubDocumentStore());
        services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider());
        if (chatClient is not null)
            services.AddSingleton(chatClient);
        return services;
    }

    [Fact]
    public void WithChatClient_RegistersTheLlmEvaluatorCheckerAndPipeline()
    {
        using var chat = new FakeChatClient();
        var services = BaseServices(chat);
        services.AddOrkeonCorrectiveRag(Configuration());
        using var provider = services.BuildServiceProvider();

        Assert.IsType<LlmRetrievalEvaluator>(provider.GetRequiredService<IRetrievalEvaluator>());
        Assert.IsType<LlmGroundednessChecker>(provider.GetRequiredService<IGroundednessChecker>());
        Assert.NotNull(provider.GetRequiredService<CorrectiveRagPipeline>());
    }

    [Fact]
    public void WithoutChatClient_FallsBackToTheDeterministicHeuristics()
    {
        var services = BaseServices();
        services.AddOrkeonCorrectiveRag(Configuration());
        using var provider = services.BuildServiceProvider();

        Assert.IsType<HeuristicRetrievalEvaluator>(provider.GetRequiredService<IRetrievalEvaluator>());
        Assert.IsType<HeuristicGroundednessChecker>(provider.GetRequiredService<IGroundednessChecker>());
    }

    [Fact]
    public void HostRegistrations_Win_TryAddSemantics()
    {
        using var chat = new FakeChatClient();
        var services = BaseServices(chat);
        var hostEvaluator = new StubRetrievalEvaluator();
        var hostChecker = new StubGroundednessChecker();
        services.AddSingleton<IRetrievalEvaluator>(hostEvaluator);
        services.AddSingleton<IGroundednessChecker>(hostChecker);

        services.AddOrkeonCorrectiveRag(Configuration());
        using var provider = services.BuildServiceProvider();

        Assert.Same(hostEvaluator, provider.GetRequiredService<IRetrievalEvaluator>());
        Assert.Same(hostChecker, provider.GetRequiredService<IGroundednessChecker>());
    }

    [Fact]
    public void CorrectiveOptions_AreBoundFromTheOrkeonRagSection()
    {
        using var chat = new FakeChatClient();
        var services = BaseServices(chat);
        services.AddOrkeonCorrectiveRag(Configuration(
            ("Orkeon:Rag:Corrective:MaxIterations", "5"),
            ("Orkeon:Rag:Corrective:WebFallback:Enabled", "true"),
            ("Orkeon:Rag:Corrective:WebFallback:MaxResults", "7")));
        using var provider = services.BuildServiceProvider();

        var pipeline = provider.GetRequiredService<CorrectiveRagPipeline>();
        Assert.Equal(5, pipeline.Options.Corrective.MaxIterations);
        Assert.True(pipeline.Options.Corrective.WebFallback.Enabled);
        Assert.Equal(7, pipeline.Options.Corrective.WebFallback.MaxResults);
    }

    [Fact]
    public void CorrectiveOptionsDefaults_AreSafe_MaxIterations3AndWebFallbackOff()
    {
        using var chat = new FakeChatClient();
        var services = BaseServices(chat);
        services.AddOrkeonCorrectiveRag(Configuration());
        using var provider = services.BuildServiceProvider();

        var pipeline = provider.GetRequiredService<CorrectiveRagPipeline>();
        Assert.Equal(3, pipeline.Options.Corrective.MaxIterations);
        Assert.False(pipeline.Options.Corrective.WebFallback.Enabled);
    }

    [Fact]
    public async Task RegisteredWebRetriever_ReachesThePipeline()
    {
        using var chat = new FakeChatClient();
        var services = BaseServices(chat);
        var web = new StubWebDocumentRetriever
        {
            Results = { new RagDocument { Id = "w1", SourceId = "web", Content = "web content" } },
        };
        services.AddSingleton<IWebDocumentRetriever>(web);
        // Force the fallback path: no rewrite budget, fallback enabled, an
        // evaluator that always grades Incorrect.
        services.AddSingleton<IRetrievalEvaluator>(new StubRetrievalEvaluator
        {
            DefaultVerdict = new RetrievalVerdict { Grade = RetrievalGrade.Incorrect },
        });
        services.AddOrkeonCorrectiveRag(Configuration(
            ("Orkeon:Rag:Corrective:MaxIterations", "0"),
            ("Orkeon:Rag:Corrective:WebFallback:Enabled", "true")));
        using var provider = services.BuildServiceProvider();

        var pipeline = provider.GetRequiredService<CorrectiveRagPipeline>();
        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "q?", Collection = "kb" },
            TestContext.Current.CancellationToken);

        var webCall = Assert.Single(web.Calls);
        Assert.Equal("q?", webCall.Query);
        Assert.Contains(answer.Trace.Steps, s => s.Name == "corrective:web_fallback");
    }
}
