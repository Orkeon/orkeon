using Microsoft.Extensions.AI;
using Orkeon.Rag.Evaluation;

namespace Orkeon.Rag.Tests.Evaluation;

/// <summary>
/// The deterministic offline generation stub used by <c>orkeon rag eval --offline</c>
/// and the CI runs: it must return the linear pipeline's context block verbatim.
/// </summary>
public sealed class ExtractiveOfflineChatClientTests
{
    [Fact]
    public async Task Response_ReturnsTheContextBlockOfTheLinearPipelinePrompt()
    {
        using var client = new ExtractiveOfflineChatClient();
        var user = "Context:\n[1] (source: faq.md)\nRefunds within 30 days.\n\nQuestion: refund policy?";

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, "sys"), new ChatMessage(ChatRole.User, user)],
            options: null,
            TestContext.Current.CancellationToken);

        Assert.StartsWith(ExtractiveOfflineChatClient.AnswerPrefix, response.Text, StringComparison.Ordinal);
        Assert.Contains("Refunds within 30 days.", response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Question: refund policy?", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractContext_WithoutTheMarkers_ReturnsTheWholeMessage()
    {
        Assert.Equal("free-form prompt", ExtractiveOfflineChatClient.ExtractContext("free-form prompt"));
    }
}
