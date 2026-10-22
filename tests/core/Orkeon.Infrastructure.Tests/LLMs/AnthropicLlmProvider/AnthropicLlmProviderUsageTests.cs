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
}
