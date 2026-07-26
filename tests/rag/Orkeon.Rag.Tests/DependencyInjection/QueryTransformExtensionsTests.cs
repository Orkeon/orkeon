using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Factories;
using Orkeon.Rag.QueryTransform;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.DependencyInjection;

public class QueryTransformExtensionsTests
{
    private static ServiceProvider BuildProvider(bool withChatClient = true)
    {
        var services = new ServiceCollection();
        if (withChatClient)
        {
            services.AddSingleton<IChatClient>(_ => new FakeChatClient());
        }

        services.AddOrkeonQueryTransforms();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void RegistersAPopulatedSingletonFactory()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<QueryTransformerFactory>();

        Assert.True(factory.IsKnown("none"));
        Assert.True(factory.IsKnown("multi-query"));
        Assert.True(factory.IsKnown("rag-fusion"));
        Assert.True(factory.IsKnown("hyde"));
        Assert.Same(factory, provider.GetRequiredService<QueryTransformerFactory>());
    }

    [Fact]
    public void Create_LlmBackedTransformer_ResolvesTheHostChatClientLazily()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<QueryTransformerFactory>();

        Assert.IsType<MultiQueryTransformer>(factory.Create("multi-query"));
        Assert.IsType<RagFusionTransformer>(factory.Create("rag-fusion"));
        Assert.IsType<HydeTransformer>(factory.Create("hyde"));
    }

    [Fact]
    public void WithoutChatClient_ModeNoneStillWorks()
    {
        using var provider = BuildProvider(withChatClient: false);
        var factory = provider.GetRequiredService<QueryTransformerFactory>();

        Assert.IsType<IdentityQueryTransformer>(factory.Create("none"));
        // LLM-backed modes fail loudly only when actually created.
        Assert.ThrowsAny<InvalidOperationException>(() => factory.Create("hyde"));
    }

    [Fact]
    public void AddOrkeonQueryTransforms_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(_ => new FakeChatClient());
        services.AddOrkeonQueryTransforms();
        services.AddOrkeonQueryTransforms();

        using var provider = services.BuildServiceProvider();

        // A duplicate registration would throw "already registered" here.
        Assert.NotNull(provider.GetRequiredService<QueryTransformerFactory>().Create("none"));
    }

    [Fact]
    public void HostRegisteredFactory_Wins()
    {
        var custom = new QueryTransformerFactory();
        custom.Register("custom-only", static () => new IdentityQueryTransformer());

        var services = new ServiceCollection();
        services.AddSingleton(custom);
        services.AddOrkeonQueryTransforms();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<QueryTransformerFactory>();

        Assert.Same(custom, factory);
        Assert.True(factory.IsKnown("custom-only"));
        Assert.False(factory.IsKnown("multi-query"));
    }
}
