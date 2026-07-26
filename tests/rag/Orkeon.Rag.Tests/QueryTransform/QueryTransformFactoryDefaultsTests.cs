using Microsoft.Extensions.AI;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Factories;
using Orkeon.Rag.QueryTransform;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.QueryTransform;

public class QueryTransformFactoryDefaultsTests
{
    private static QueryTransformerFactory CreateDefaultFactory(IChatClient? chatClient = null) =>
        QueryTransformFactoryDefaults.CreateDefault(() => chatClient ?? new FakeChatClient());

    [Fact]
    public void Factory_KnowsBuiltInNamesAndAliases()
    {
        var factory = CreateDefaultFactory();

        Assert.True(factory.IsKnown("none"));
        Assert.True(factory.IsKnown("multi-query"));
        Assert.True(factory.IsKnown("multiquery"));
        Assert.True(factory.IsKnown("multi_query"));
        Assert.True(factory.IsKnown("rag-fusion"));
        Assert.True(factory.IsKnown("ragfusion"));
        Assert.True(factory.IsKnown("rag_fusion"));
        Assert.True(factory.IsKnown("hyde"));
        Assert.True(factory.IsKnown("HYDE")); // case-insensitive
        Assert.True(factory.IsKnown(" Multi-Query ")); // trimmed
    }

    [Fact]
    public void Create_None_ReturnsIdentityTransformer()
    {
        var factory = CreateDefaultFactory();

        Assert.IsType<IdentityQueryTransformer>(factory.Create("none"));
    }

    [Fact]
    public void Create_MultiQuery_ResolvesThroughAllAliases()
    {
        var factory = CreateDefaultFactory();

        Assert.IsType<MultiQueryTransformer>(factory.Create("multi-query"));
        Assert.IsType<MultiQueryTransformer>(factory.Create("multiquery"));
        Assert.IsType<MultiQueryTransformer>(factory.Create("multi_query"));
    }

    [Fact]
    public void Create_RagFusion_ResolvesThroughAllAliases()
    {
        var factory = CreateDefaultFactory();

        Assert.IsType<RagFusionTransformer>(factory.Create("rag-fusion"));
        Assert.IsType<RagFusionTransformer>(factory.Create("ragfusion"));
        Assert.IsType<RagFusionTransformer>(factory.Create("rag_fusion"));
    }

    [Fact]
    public void Create_Hyde_ResolvesToHydeTransformer()
    {
        var factory = CreateDefaultFactory();

        Assert.IsType<HydeTransformer>(factory.Create("hyde"));
    }

    [Fact]
    public void Create_UnknownName_FailsLoudlyListingKnownNames()
    {
        var factory = CreateDefaultFactory();

        var exception = Assert.Throws<RagComponentNotFoundException>(() => factory.Create("step-back"));
        Assert.Contains("none", exception.Message, StringComparison.Ordinal);
        Assert.Contains("multi-query", exception.Message, StringComparison.Ordinal);
        Assert.Contains("rag-fusion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("hyde", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_None_NeverTouchesTheChatClientAccessor()
    {
        // Mode "none" must work in hosts that registered no IChatClient at all.
        var factory = QueryTransformFactoryDefaults.CreateDefault(
            () => throw new InvalidOperationException("no chat client registered"));

        Assert.IsType<IdentityQueryTransformer>(factory.Create("none"));
    }

    [Fact]
    public void RegisterDefaultTransformers_AllowsThirdPartyRegistrationsAlongside()
    {
        var factory = CreateDefaultFactory();
        factory.Register("step-back", static () => new IdentityQueryTransformer());

        Assert.IsType<IdentityQueryTransformer>(factory.Create("step-back"));
        Assert.IsType<MultiQueryTransformer>(factory.Create("multi-query"));
    }

    [Fact]
    public async Task IdentityTransformer_ReturnsTheOriginalQueryOnly()
    {
        var transformer = new IdentityQueryTransformer();

        var result = await transformer.TransformAsync(
            "the question", new QueryTransformContext(), TestContext.Current.CancellationToken);

        Assert.Equal(["the question"], result);
        Assert.Equal("none", transformer.Name);
        Assert.Equal(QueryTransformKind.Union, transformer.Kind);
    }

    [Fact]
    public async Task IdentityTransformer_GuardsArguments()
    {
        var transformer = new IdentityQueryTransformer();

        await Assert.ThrowsAsync<ArgumentException>(
            () => transformer.TransformAsync(" ", new QueryTransformContext(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => transformer.TransformAsync("q", null!, TestContext.Current.CancellationToken));
    }
}
