using Orkeon.Domain.SharedKernel.ValueObjects;
using Polly;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class ZaiLlmProviderTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<ZaiLlmProvider> _logger = new();
    private readonly LlmConfig _config = LlmConfig.Create("glm-5.2", TestApiKey);
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    [Fact]
    public void ShouldReturnZai_WhenName()
    {
        using var provider = new ZaiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        Assert.Equal("zai", provider.Name);
    }

    [Fact]
    public async Task ShouldReturnContent_WhenGenerateAsyncWithValidResponse()
    {
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Test response" } }
            },
            usage = new { total_tokens = 100 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        _httpClientFactory.RegisterClient("ZaiLlmProvider", new HttpClient(handler));

        using var provider = new ZaiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Test response", result.Content);
        Assert.Equal(100, result.TokensUsed);
        Assert.Equal("glm-5.2", result.Model);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithMissingApiKey()
    {
        var configWithoutKey = LlmConfig.Create("glm-5.2", null);
        using var provider = new ZaiLlmProvider(configWithoutKey, _httpClientFactory, _noOpPolicy, _logger);

        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Content);
        Assert.Contains("Z.AI API key is required", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldSurfaceReasoningContentInMetadata_WhenThinkingModelResponds()
    {
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Final answer", reasoning_content = "step 1... step 2..." } }
            },
            usage = new { total_tokens = 60, prompt_tokens = 20, completion_tokens = 40 }
        });
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        _httpClientFactory.RegisterClient("ZaiLlmProvider", new HttpClient(handler));

        using var provider = new ZaiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Final answer", result.Content);
        Assert.Equal("step 1... step 2...", result.Metadata["reasoning_content"]);
    }

    [Fact]
    public async Task ShouldSurfaceReasoningTokensInMetadata_WhenUsageReportsThem()
    {
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "ok" } } },
            usage = new
            {
                total_tokens = 130,
                prompt_tokens = 100,
                completion_tokens = 30,
                completion_tokens_details = new { reasoning_tokens = 22 }
            }
        });
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        _httpClientFactory.RegisterClient("ZaiLlmProvider", new HttpClient(handler));

        using var provider = new ZaiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(22, result.Metadata["reasoning_tokens"]);
    }
}
