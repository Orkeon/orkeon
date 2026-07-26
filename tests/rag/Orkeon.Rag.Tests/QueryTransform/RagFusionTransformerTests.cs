using Microsoft.Extensions.Logging;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.QueryTransform;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Rag.Tests.QueryTransform;

public class RagFusionTransformerTests
{
    private static readonly QueryTransformContext DefaultContext = new();

    [Fact]
    public void NameAndKind_AreRagFusion_WithRrfFusionSemantics()
    {
        using var chat = new FakeChatClient();
        var transformer = new RagFusionTransformer(chat);

        Assert.Equal("rag-fusion", transformer.Name);
        // The distinguishing contract vs multi-query: per-variant result lists
        // must be fused by RRF at the retrieve stage (wired by the RAG-05
        // integration lot), not merged by plain union.
        Assert.Equal(QueryTransformKind.Fusion, transformer.Kind);
    }

    [Fact]
    public void SharesTheVariantGenerationImplementationWithMultiQuery()
    {
        Assert.True(typeof(LlmQueryVariantTransformerBase).IsAssignableFrom(typeof(RagFusionTransformer)));
        Assert.True(typeof(LlmQueryVariantTransformerBase).IsAssignableFrom(typeof(MultiQueryTransformer)));
    }

    [Fact]
    public async Task Transform_ReturnsOriginalFirstThenVariants_LikeMultiQuery()
    {
        using var chat = new FakeChatClient { ResponseText = "1. variant a\n2. variant b\n3. variant c" };
        var transformer = new RagFusionTransformer(chat);

        var result = await transformer.TransformAsync(
            "the question", DefaultContext, TestContext.Current.CancellationToken);

        Assert.Equal(["the question", "variant a", "variant b", "variant c"], result);
        Assert.Equal(1, chat.CallCount);
    }

    [Fact]
    public async Task Transform_RespectsMaxVariants()
    {
        using var chat = new FakeChatClient { ResponseText = "[\"v1\", \"v2\", \"v3\", \"v4\"]" };
        var transformer = new RagFusionTransformer(chat);

        var result = await transformer.TransformAsync(
            "q", new QueryTransformContext { MaxVariants = 2 }, TestContext.Current.CancellationToken);

        Assert.Equal(["q", "v1", "v2"], result);
    }

    [Fact]
    public async Task Transform_LlmFailure_FallsBackToOriginalWithWarning_NeverThrows()
    {
        var logger = new MockLogger<RagFusionTransformer>();
        using var chat = new ThrowingChatClient();
        var transformer = new RagFusionTransformer(chat, logger);

        var result = await transformer.TransformAsync(
            "original question", DefaultContext, TestContext.Current.CancellationToken);

        Assert.Equal(["original question"], result);
        Assert.Contains(logger.GetEntriesByLevel(LogLevel.Warning),
            e => e.Message.Contains("rag-fusion", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Transform_UnusableResponse_FallsBackToOriginalWithWarning()
    {
        var logger = new MockLogger<RagFusionTransformer>();
        using var chat = new FakeChatClient { ResponseText = "" };
        var transformer = new RagFusionTransformer(chat, logger);

        var result = await transformer.TransformAsync(
            "original question", DefaultContext, TestContext.Current.CancellationToken);

        Assert.Equal(["original question"], result);
        Assert.Single(logger.GetEntriesByLevel(LogLevel.Warning));
    }
}
