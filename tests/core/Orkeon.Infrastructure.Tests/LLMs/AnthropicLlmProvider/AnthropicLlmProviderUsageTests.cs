using System.Net;
using Polly;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>What the Anthropic provider reads of the usage block (RT-12).</summary>
public sealed class AnthropicLlmProviderUsageTests
{
    private const string Body =
        """{"content":[{"type":"text","text":"ok"}],"usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":30,"cache_creation_input_tokens":200}}""";

    [Fact]
    [Trait("BR", "RT-12")]
    public async Task ShouldMapCacheCreationTokensToCacheWrite_WhenAnthropicUsageReportsThem()
    {
        using var factory = new MockHttpClientFactory();
        var handler = factory.SetupDefaultHandler();
        handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Body, System.Text.Encoding.UTF8, "application/json")
        });
        using var provider = new AnthropicLlmProvider(
            Orkeon.Domain.SharedKernel.ValueObjects.LlmConfig.Create(ModelClaude3Opus, TestApiKey),
            factory, Policy.NoOpAsync<HttpResponseMessage>());

        var response = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(200, response.CacheWriteTokens);
        Assert.Equal(30, response.CacheHitTokens);
        // 10 fresh + 200 written + 30 read, each counted once.
        Assert.Equal(240, response.PromptTokens);
    }

    private const string CacheBody =
        """{"content":[{"type":"text","text":"ok"}],"usage":{"input_tokens":10,"output_tokens":5,"cache_creation_input_tokens":10000,"cache_creation":{"ephemeral_5m_input_tokens":6000,"ephemeral_1h_input_tokens":4000}}}""";

    private static AnthropicLlmProvider NewProvider(MockHttpClientFactory factory, string body, string contentType)
    {
        var handler = factory.SetupDefaultHandler();
        handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, contentType)
        });
        return new AnthropicLlmProvider(
            Orkeon.Domain.SharedKernel.ValueObjects.LlmConfig.Create(ModelClaude3Opus, TestApiKey),
            factory, Policy.NoOpAsync<HttpResponseMessage>());
    }

    [Fact]
    [Trait("BR", "R-47")]
    public async Task ShouldReadEphemeral1hTokens_WhenAnthropicUsageHasCacheCreation()
    {
        using var factory = new MockHttpClientFactory();
        using var provider = NewProvider(factory, CacheBody, "application/json");

        var response = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(10000, response.CacheWriteTokens);
        Assert.Equal(4000, response.CacheWrite1hTokens);
    }

    [Fact]
    [Trait("BR", "R-47")]
    public async Task ShouldReadEphemeral1hTokens_WhenAnthropicUsageIsStreamed()
    {
        const string sse =
            "event: message_start\n" +
            "data: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":10,\"output_tokens\":1,\"cache_creation_input_tokens\":10000,\"cache_creation\":{\"ephemeral_5m_input_tokens\":6000,\"ephemeral_1h_input_tokens\":4000}}}}\n\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"ok\"}}\n\n" +
            "event: message_delta\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":5}}\n\n" +
            "event: message_stop\n" +
            "data: {\"type\":\"message_stop\"}\n\n";
        using var factory = new MockHttpClientFactory();
        using var provider = NewProvider(factory, sse, "text/event-stream");

        Orkeon.Domain.SharedKernel.ValueObjects.LlmResponse? final = null;
        await foreach (var ev in provider.ChatStreamingAsync(
            [Orkeon.Domain.SharedKernel.ValueObjects.LlmMessage.User("hello")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            if (ev.FinalResponse is not null)
                final = ev.FinalResponse;
        }

        Assert.NotNull(final);
        Assert.Equal(10000, final.CacheWriteTokens);
        Assert.Equal(4000, final.CacheWrite1hTokens);
    }
}
