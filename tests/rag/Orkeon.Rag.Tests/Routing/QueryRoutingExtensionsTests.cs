using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Routing;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.Routing;

/// <summary>
/// Tests of the query-routing registration (RAG-05/C3):
/// <c>Orkeon:Rag:QueryRouting:Classifier</c> selects the implementation
/// (default <c>heuristic</c>), <c>llm</c> without a chat client falls back to
/// the heuristic, unknown names fail loudly, and a host-registered classifier
/// wins (TryAdd).
/// </summary>
public class QueryRoutingExtensionsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    private static ServiceProvider BuildProvider(
        IConfiguration configuration,
        IChatClient? chatClient = null,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        if (chatClient is not null)
        {
            services.AddSingleton(chatClient);
        }

        configure?.Invoke(services);
        services.AddOrkeonQueryRouting(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Default_NoConfiguration_RegistersTheHeuristicClassifier()
    {
        using var provider = BuildProvider(Config());

        Assert.IsType<HeuristicQueryComplexityClassifier>(
            provider.GetRequiredService<IQueryComplexityClassifier>());
    }

    [Fact]
    public void HeuristicExplicit_CaseInsensitive_RegistersTheHeuristicClassifier()
    {
        using var provider = BuildProvider(
            Config(("Orkeon:Rag:QueryRouting:Classifier", "Heuristic")));

        Assert.IsType<HeuristicQueryComplexityClassifier>(
            provider.GetRequiredService<IQueryComplexityClassifier>());
    }

    [Fact]
    public void Llm_WithChatClient_RegistersTheLlmClassifier()
    {
        using var chatClient = new FakeChatClient();
        using var provider = BuildProvider(
            Config(("Orkeon:Rag:QueryRouting:Classifier", "llm")), chatClient);

        Assert.IsType<LlmQueryComplexityClassifier>(
            provider.GetRequiredService<IQueryComplexityClassifier>());
    }

    [Fact]
    public void Llm_WithoutChatClient_FallsBackToTheHeuristicClassifier()
    {
        using var provider = BuildProvider(
            Config(("Orkeon:Rag:QueryRouting:Classifier", "llm")));

        Assert.IsType<HeuristicQueryComplexityClassifier>(
            provider.GetRequiredService<IQueryComplexityClassifier>());
    }

    [Fact]
    public void UnknownClassifier_FailsLoudly_ListingSupportedNames()
    {
        using var provider = BuildProvider(
            Config(("Orkeon:Rag:QueryRouting:Classifier", "quantum")));

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IQueryComplexityClassifier>());

        Assert.Contains("quantum", exception.Message, StringComparison.Ordinal);
        Assert.Contains("heuristic", exception.Message, StringComparison.Ordinal);
        Assert.Contains("llm", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HostRegisteredClassifier_Wins_TryAdd()
    {
        var host = new StubClassifier();
        using var provider = BuildProvider(
            Config(("Orkeon:Rag:QueryRouting:Classifier", "llm")),
            configure: services => services.AddSingleton<IQueryComplexityClassifier>(host));

        Assert.Same(host, provider.GetRequiredService<IQueryComplexityClassifier>());
    }

    [Fact]
    public void AddOrkeonRag_WiresTheClassifier_WithTheHeuristicDefault()
    {
        using var chatClient = new FakeChatClient();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/kb"));
        services.AddSingleton<IDocumentStore>(new FakeDocumentStore());
        services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider());
        services.AddSingleton<IChatClient>(chatClient);
        services.AddOrkeonRag(Config());
        using var provider = services.BuildServiceProvider();

        Assert.IsType<HeuristicQueryComplexityClassifier>(
            provider.GetRequiredService<IQueryComplexityClassifier>());
    }

    /// <summary>Hand-written stand-in for a host-registered classifier.</summary>
    private sealed class StubClassifier : IQueryComplexityClassifier
    {
        public Task<QueryRoute> ClassifyAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult(QueryRoute.SingleShot);
    }
}
