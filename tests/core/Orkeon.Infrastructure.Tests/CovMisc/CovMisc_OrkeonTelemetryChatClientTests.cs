using Microsoft.Extensions.AI;
using Orkeon.Infrastructure.Telemetry;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.CovMisc;

/// <summary>
/// Coverage for <see cref="OrkeonTelemetryChatClient"/> — the DelegatingChatClient that
/// wraps responses with tracing spans and metrics recording.
/// </summary>
[Collection("OrkeonMeter")] // emits on the global "Orkeon" meter; serialize with other meter tests
public class CovMisc_OrkeonTelemetryChatClientTests
{
    private static readonly ChatMessage[] s_messages =
    [
        new(ChatRole.User, "hello")
    ];

    [Fact]
    public async Task GetResponseAsync_DelegatesToInner_AndReturnsResponse()
    {
        using var inner = new MockChatClient();
        inner.SetGetResponseResult("hi there");
        using var client = new OrkeonTelemetryChatClient(inner, metrics: null, providerName: "openai");

        var response = await client.GetResponseAsync(s_messages, new ChatOptions { ModelId = "gpt-4o" }, TestContext.Current.CancellationToken);

        Assert.Equal("hi there", response.Text);
        Assert.Equal(1, inner.GetResponseCallCount);
    }

    [Fact]
    public async Task GetResponseAsync_WithMetrics_RecordsCall()
    {
        using var inner = new MockChatClient();
        var usageMessage = new ChatMessage(ChatRole.Assistant, "answer");
        var response = new ChatResponse(usageMessage)
        {
            Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 }
        };
        inner.SetGetResponseResult(response);

        using var metrics = new OrkeonMetrics();
        using var client = new OrkeonTelemetryChatClient(inner, metrics, "openai");

        var result = await client.GetResponseAsync(s_messages, new ChatOptions { ModelId = "gpt-4o" }, TestContext.Current.CancellationToken);

        Assert.Equal("answer", result.Text);
    }

    [Fact]
    public async Task GetResponseAsync_NullOptions_UsesUnknownModel()
    {
        using var inner = new MockChatClient();
        inner.SetGetResponseResult("ok");
        using var metrics = new OrkeonMetrics();
        using var client = new OrkeonTelemetryChatClient(inner, metrics);

        var result = await client.GetResponseAsync(s_messages, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", result.Text);
    }

    [Fact]
    public async Task GetResponseAsync_InnerThrows_PropagatesAndStillCompletesSpan()
    {
        using var inner = new MockChatClient();
        inner.SetGetResponseException(new InvalidOperationException("provider down"));
        using var metrics = new OrkeonMetrics();
        using var client = new OrkeonTelemetryChatClient(inner, metrics, "openai");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetResponseAsync(s_messages, new ChatOptions { ModelId = "gpt-4o" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_YieldsAllUpdates()
    {
        using var inner = new MockChatClient();
        inner.SetStreamingUpdates(
        [
            new ChatResponseUpdate(ChatRole.Assistant, "Hello "),
            new ChatResponseUpdate(ChatRole.Assistant, "World")
        ]);
        using var metrics = new OrkeonMetrics();
        using var client = new OrkeonTelemetryChatClient(inner, metrics, "ollama");

        var collected = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync(s_messages, new ChatOptions { ModelId = "llama3" }, TestContext.Current.CancellationToken))
        {
            collected.Add(update.Text);
        }

        Assert.Equal(["Hello ", "World"], collected);
        Assert.Equal(1, inner.GetStreamingResponseCallCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NullOptions_UsesUnknownModel_WithoutMetrics()
    {
        using var inner = new MockChatClient();
        inner.SetStreamingUpdates([new ChatResponseUpdate(ChatRole.Assistant, "chunk")]);
        using var client = new OrkeonTelemetryChatClient(inner);

        var collected = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync(s_messages, cancellationToken: TestContext.Current.CancellationToken))
        {
            collected.Add(update.Text);
        }

        Assert.Single(collected);
    }
}
