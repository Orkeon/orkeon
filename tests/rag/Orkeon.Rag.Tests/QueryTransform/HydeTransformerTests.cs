using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.QueryTransform;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Rag.Tests.QueryTransform;

public class HydeTransformerTests
{
    private static readonly QueryTransformContext DefaultContext = new();

    [Fact]
    public void NameAndKind_AreHydeReplacement()
    {
        using var chat = new FakeChatClient();
        var transformer = new HydeTransformer(chat);

        Assert.Equal("hyde", transformer.Name);
        // HyDE contract: the hypothetical document is embedded INSTEAD of the
        // question (document <-> document retrieval, Gao et al. 2022).
        Assert.Equal(QueryTransformKind.Replacement, transformer.Kind);
    }

    [Fact]
    public async Task Transform_ReturnsTheHypotheticalDocumentOnly_WithoutTheOriginalQuestion()
    {
        using var chat = new FakeChatClient
        {
            ResponseText = "  Refund requests must be submitted within 30 days with receipts.  ",
        };
        var transformer = new HydeTransformer(chat);

        var result = await transformer.TransformAsync(
            "what is the refund policy?", DefaultContext, TestContext.Current.CancellationToken);

        Assert.Equal(["Refund requests must be submitted within 30 days with receipts."], result);
        Assert.DoesNotContain("what is the refund policy?", result);
        Assert.Equal(1, chat.CallCount);
    }

    [Fact]
    public async Task Transform_SendsTheQuestionInsideAHydePrompt()
    {
        using var chat = new FakeChatClient { ResponseText = "a passage" };
        var transformer = new HydeTransformer(chat);

        await transformer.TransformAsync("the question", DefaultContext, TestContext.Current.CancellationToken);

        Assert.NotNull(chat.LastMessages);
        Assert.Equal(ChatRole.System, chat.LastMessages![0].Role);
        Assert.Equal(HydeTransformer.SystemPrompt, chat.LastMessages[0].Text);
        Assert.Contains(
            "Write a passage that would answer the question: the question",
            chat.LastMessages[1].Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transform_EmptyResponse_FallsBackToOriginalWithWarning()
    {
        var logger = new MockLogger<HydeTransformer>();
        using var chat = new FakeChatClient { ResponseText = "   " };
        var transformer = new HydeTransformer(chat, logger);

        var result = await transformer.TransformAsync(
            "original question", DefaultContext, TestContext.Current.CancellationToken);

        Assert.Equal(["original question"], result);
        Assert.Contains(logger.GetEntriesByLevel(LogLevel.Warning),
            e => e.Message.Contains("no usable hypothetical document", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Transform_LlmFailure_FallsBackToOriginalWithWarning_NeverThrows()
    {
        var logger = new MockLogger<HydeTransformer>();
        using var chat = new ThrowingChatClient();
        var transformer = new HydeTransformer(chat, logger);

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
        var transformer = new HydeTransformer(chat);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transformer.TransformAsync("q", DefaultContext, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Transform_GuardsArguments()
    {
        using var chat = new FakeChatClient();
        var transformer = new HydeTransformer(chat);

        await Assert.ThrowsAsync<ArgumentException>(
            () => transformer.TransformAsync("  ", DefaultContext, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => transformer.TransformAsync("q", null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_RequiresChatClient()
    {
        Assert.Throws<ArgumentNullException>(() => new HydeTransformer(null!));
    }
}
