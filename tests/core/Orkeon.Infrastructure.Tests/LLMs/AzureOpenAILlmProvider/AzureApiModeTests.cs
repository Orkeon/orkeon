using System.Net;
using System.Text.Json;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Azure's two API shapes (LLM-07, gaps G-06 / G-25): the historical dated deployment URL and
/// the v1 GA surface introduced in August 2025.
/// </summary>
public class AzureApiModeTests
{
    private const string Endpoint = "https://my-resource.openai.azure.com";

    private static readonly string OkBody = JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = "ok" } } },
        usage = new { total_tokens = 1, prompt_tokens = 1, completion_tokens = 0 },
    });

    private static async Task<Uri> CaptureEndpointAsync(LlmConfig config)
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkBody);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("AzureOpenAILlmProvider", new HttpClient(handler));

        using var provider = new AzureOpenAILlmProvider(
            config, factory, Policy.NoOpAsync<HttpResponseMessage>(),
            new TestLogger<AzureOpenAILlmProvider>());

        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        return handler.CapturedRequests.Single().RequestUri!;
    }

    private static LlmConfig BaseConfig() =>
        LlmConfig.Create("my-deployment", TestApiKey) with { BaseUrl = new Uri(Endpoint) };

    /// <summary>
    /// The dated shape stays the default: switching it would silently change the URL of every
    /// existing deployment-based configuration.
    /// </summary>
    [Fact]
    public async Task ShouldUseTheDatedDeploymentUrl_ByDefault()
    {
        var uri = await CaptureEndpointAsync(BaseConfig());

        Assert.Equal("/openai/deployments/my-deployment/chat/completions", uri.AbsolutePath);
        Assert.Contains("api-version=", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldHonourAnExplicitDatedApiVersion()
    {
        var uri = await CaptureEndpointAsync(BaseConfig() with { ApiVersion = "2025-04-01-preview" });

        Assert.Equal("api-version=2025-04-01-preview", uri.Query.TrimStart('?'));
    }

    /// <summary>
    /// The v1 GA surface drops <c>api-version</c> entirely and is the only path to the
    /// Responses API and to the non-OpenAI models Azure resells.
    /// </summary>
    [Fact]
    public async Task ShouldUseTheV1Url_WhenTheV1ModeIsSelected()
    {
        var uri = await CaptureEndpointAsync(BaseConfig() with { ApiVersion = "v1" });

        Assert.Equal("/openai/v1/chat/completions", uri.AbsolutePath);
        Assert.Empty(uri.Query);
    }

    [Fact]
    public async Task ShouldAlsoSelectTheV1Mode_FromTheCustomParameterBag()
    {
        var config = BaseConfig() with
        {
            CustomParameters = new Dictionary<string, object> { ["api_version"] = "v1" },
        };

        var uri = await CaptureEndpointAsync(config);

        Assert.Equal("/openai/v1/chat/completions", uri.AbsolutePath);
    }
}
