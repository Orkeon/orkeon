using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.QueryTransform;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Rag.Tests.QueryTransform;

public class MultiQueryTransformerTests
{
    private static readonly QueryTransformContext DefaultContext = new();

    [Fact]
    public void NameAndKind_AreMultiQueryUnion()
    {
        using var chat = new FakeChatClient();
        var transformer = new MultiQueryTransformer(chat);

        Assert.Equal("multi-query", transformer.Name);
        Assert.Equal(QueryTransformKind.Union, transformer.Kind);
    }

    [Fact]
    public async Task Transform_NumberedLines_ReturnsOriginalFirstThenVariants()
    {
        using var chat = new FakeChatClient
        {
            ResponseText = "1. What is the refund policy?\n2) How do refunds work?\n3: Refund rules",
        };
        var transformer = new MultiQueryTransformer(chat);

        var result = await transformer.TransformAsync(
            "refund policy?", DefaultContext, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["refund policy?", "What is the refund policy?", "How do refunds work?", "Refund rules"],
            result);
        Assert.Equal(1, chat.CallCount); // one single LLM call for all variants
    }

    [Fact]
    public async Task Transform_BulletedLines_AreParsed()
    {
        using var chat = new FakeChatClient { ResponseText = "- variant one\n* variant two\n• variant three" };
        var transformer = new MultiQueryTransformer(chat);

        var result = await transformer.TransformAsync(
            "q", DefaultContext, TestContext.Current.CancellationToken);

        Assert.Equal(["q", "variant one", "variant two", "variant three"], result);
    }

    [Fact]
    public async Task Transform_JsonArray_IsParsed_EvenInsideCodeFence()
    {
        using var chat = new FakeChatClient
        {
            ResponseText = "```json\n[\"alpha variant\", \"beta variant\"]\n```",
        };
        var transformer = new MultiQueryTransformer(chat);

        var result = await transformer.TransformAsync(
            "q", DefaultContext, TestContext.Current.CancellationToken);

        Assert.Equal(["q", "alpha variant", "beta variant"], result);
    }

    [Fact]
    public async Task Transform_PreambleAndQuotes_AreStripped()
    {
        using var chat = new FakeChatClient
        {
            ResponseText = "Here are 3 variants:\n1. \"quoted variant\"\n2. plain variant",
        };
        var transformer = new MultiQueryTransformer(chat);

        var result = await transformer.TransformAsync(
            "q", DefaultContext, TestContext.Current.CancellationToken);

        Assert.Equal(["q", "quoted variant", "plain variant"], result);
    }

    [Fact]
    public async Task Transform_DeduplicatesCaseInsensitively_IncludingTheOriginal()
    {
        using var chat = new FakeChatClient
        {
            ResponseText = "1. Refund Policy\n2. REFUND POLICY\n3. My Question\n4. other variant",
        };
        var transformer = new MultiQueryTransformer(chat);

        var result = await transformer.TransformAsync(
            "my question", DefaultContext, TestContext.Current.CancellationToken);

        // "REFUND POLICY" duplicates variant 1; "My Question" duplicates the original.
        Assert.Equal(["my question", "Refund Policy", "other variant"], result);
    }

    [Fact]
    public async Task Transform_RespectsMaxVariants()
    {
        using var chat = new FakeChatClient { ResponseText = "1. a1\n2. b2\n3. c3\n4. d4\n5. e5" };
        var transformer = new MultiQueryTransformer(chat);

        var result = await transformer.TransformAsync(
            "q", new QueryTransformContext { MaxVariants = 2 }, TestContext.Current.CancellationToken);

        Assert.Equal(["q", "a1", "b2"], result);
    }

    [Fact]
    public async Task Transform_UnusableResponse_FallsBackToOriginalWithWarning()
    {
        var logger = new MockLogger<MultiQueryTransformer>();
        using var chat = new FakeChatClient { ResponseText = "   \n```\n```\nHere are the variants:" };
        var transformer = new MultiQueryTransformer(chat, logger);

        var result = await transformer.TransformAsync(
            "original question", DefaultContext, TestContext.Current.CancellationToken);

        Assert.Equal(["original question"], result);
        Assert.Contains(logger.GetEntriesByLevel(LogLevel.Warning),
            e => e.Message.Contains("no usable variant", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Transform_LlmFailure_FallsBackToOriginalWithWarning_NeverThrows()
    {
        var logger = new MockLogger<MultiQueryTransformer>();
        using var chat = new ThrowingChatClient();
        var transformer = new MultiQueryTransformer(chat, logger);

        var result = await transformer.TransformAsync(
            "original question", DefaultContext, TestContext.Current.CancellationToken);

        Assert.Equal(["original question"], result);
        Assert.Contains(logger.GetEntriesByLevel(LogLevel.Warning),
            e => e.Message.Contains("generation failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Transform_Cancellation_Propagates()
    {
        using var chat = new ThrowingChatClient { ExceptionToThrow = new OperationCanceledException() };
        var transformer = new MultiQueryTransformer(chat);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transformer.TransformAsync("q", DefaultContext, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Transform_SendsSystemPromptAndVariantCountToTheLlm()
    {
        using var chat = new FakeChatClient { ResponseText = "1. v" };
        var transformer = new MultiQueryTransformer(chat);

        await transformer.TransformAsync(
            "the question", new QueryTransformContext { MaxVariants = 4 }, TestContext.Current.CancellationToken);

        Assert.NotNull(chat.LastMessages);
        Assert.Equal(ChatRole.System, chat.LastMessages![0].Role);
        Assert.Equal(LlmQueryVariantTransformerBase.SystemPrompt, chat.LastMessages[0].Text);
        Assert.Contains("the question", chat.LastMessages[1].Text, StringComparison.Ordinal);
        Assert.Contains("4", chat.LastMessages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transform_GuardsArguments()
    {
        using var chat = new FakeChatClient();
        var transformer = new MultiQueryTransformer(chat);

        await Assert.ThrowsAsync<ArgumentException>(
            () => transformer.TransformAsync("   ", DefaultContext, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => transformer.TransformAsync("q", null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_RequiresChatClient()
    {
        Assert.Throws<ArgumentNullException>(() => new MultiQueryTransformer(null!));
    }
}
