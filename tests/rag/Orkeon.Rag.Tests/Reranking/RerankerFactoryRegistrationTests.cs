using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Reranking;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Reranking;

public class RerankerFactoryRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(_ => new FakeChatClient());
        services.AddOrkeonRagReranking();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Factory_KnowsBuiltInNamesAndAliases()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<RerankerFactory>();

        Assert.True(factory.IsKnown("none"));
        Assert.True(factory.IsKnown("noop"));
        Assert.True(factory.IsKnown("llm"));
        Assert.True(factory.IsKnown("listwise"));
        Assert.True(factory.IsKnown("NOOP")); // case-insensitive
    }

    [Fact]
    public void Create_None_ReturnsNoopReranker()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<RerankerFactory>();

        Assert.IsType<NoopReranker>(factory.Create("none"));
        Assert.IsType<NoopReranker>(factory.Create("noop"));
    }

    [Fact]
    public void Create_Llm_ReturnsListwiseReranker_ResolvingChatClient()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<RerankerFactory>();

        Assert.IsType<LlmListwiseReranker>(factory.Create("llm"));
        Assert.IsType<LlmListwiseReranker>(factory.Create("listwise"));
    }

    [Fact]
    public void Create_UnknownName_FailsLoudlyWithKnownNames()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<RerankerFactory>();

        var exception = Assert.Throws<RagComponentNotFoundException>(() => factory.Create("cohere"));
        Assert.Contains("none", exception.Message, StringComparison.Ordinal);
        Assert.Contains("llm", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registrars_ContributeAdditionalRerankers()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(_ => new FakeChatClient());
        services.AddSingleton<IRerankerRegistrar>(new StubRegistrar());
        services.AddOrkeonRagReranking();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<RerankerFactory>();

        Assert.True(factory.IsKnown("stub"));
        Assert.IsType<NoopReranker>(factory.Create("stub"));
    }

    [Fact]
    public void AddOrkeonRagReranking_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(_ => new FakeChatClient());
        services.AddOrkeonRagReranking();
        services.AddOrkeonRagReranking();

        using var provider = services.BuildServiceProvider();

        // A duplicate registration would throw "already registered" here.
        Assert.NotNull(provider.GetRequiredService<RerankerFactory>().Create("none"));
    }

    [Fact]
    public void RerankingOptions_DefaultsAreTheCascadeConstants()
    {
        var options = new RerankingOptions();

        Assert.Equal(50, RerankingOptions.DefaultCandidateK);
        Assert.Equal(5, RerankingOptions.DefaultTopN);
        Assert.Equal(RerankingOptions.DefaultCandidateK, options.CandidateK);
        Assert.Equal(RerankingOptions.DefaultTopN, options.TopN);
        Assert.Equal("none", options.Reranker);
    }

    private sealed class StubRegistrar : IRerankerRegistrar
    {
        public void Register(RerankerFactory factory, IServiceProvider serviceProvider) =>
            factory.Register("stub", static () => new NoopReranker());
    }
}
