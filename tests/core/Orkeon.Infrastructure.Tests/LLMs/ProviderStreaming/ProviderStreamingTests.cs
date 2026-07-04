using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using System.Net;
using System.Text;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Infrastructure.Tests.LLMs;

public sealed class ProviderStreamingTests : IDisposable
{
    private readonly MockHttpClientFactory _httpClientFactory = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy =
        Policy.NoOpAsync<HttpResponseMessage>();

    #region OpenAI Streaming

    [Fact]
    public async Task ShouldYieldTokens_WhenOpenAIGenerateStreamingAsync()
    {
        var sseData = BuildSseStream(
            """{"choices":[{"delta":{"content":"Hello"}}]}""",
            """{"choices":[{"delta":{"content":" world"}}]}""");

        using var provider = CreateOpenAIProvider(sseData, HttpStatusCode.OK);

        var tokens = await CollectTokens(provider, "test prompt");

        Assert.Equal(2, tokens.Count);
        Assert.Equal("Hello", tokens[0]);
        Assert.Equal(" world", tokens[1]);
    }

    [Fact]
    public async Task ShouldYieldNothing_WhenOpenAIGenerateStreamingAsyncEmptyApiKey()
    {
        using var provider = CreateOpenAIProvider("", HttpStatusCode.OK, apiKey: "");
        var tokens = await CollectTokens(provider, "test");
        Assert.Empty(tokens);
    }

    [Fact]
    public async Task ShouldYieldNothing_WhenOpenAIGenerateStreamingAsyncServerError()
    {
        using var provider = CreateOpenAIProvider("", HttpStatusCode.InternalServerError);
        var tokens = await CollectTokens(provider, "test");
        Assert.Empty(tokens);
    }

    [Fact]
    public void ShouldBeTrue_WhenOpenAISupportsStreaming()
    {
        using var provider = CreateOpenAIProvider("", HttpStatusCode.OK);
        Assert.True(provider.SupportsStreaming);
    }

    #endregion

    #region Anthropic Streaming

    [Fact]
    public async Task ShouldYieldTokens_WhenAnthropicGenerateStreamingAsync()
    {
        var sseData = BuildSseStream(
            """{"type":"content_block_delta","delta":{"text":"Hello"}}""",
            """{"type":"content_block_delta","delta":{"text":" Claude"}}""",
            """{"type":"message_stop"}""");

        using var provider = CreateAnthropicProvider(sseData, HttpStatusCode.OK);

        var tokens = await CollectTokens(provider, "test prompt");

        Assert.Equal(2, tokens.Count);
        Assert.Equal("Hello", tokens[0]);
        Assert.Equal(" Claude", tokens[1]);
    }

    [Fact]
    public async Task ShouldYieldNothing_WhenAnthropicGenerateStreamingAsyncEmptyApiKey()
    {
        using var provider = CreateAnthropicProvider("", HttpStatusCode.OK, apiKey: "");
        var tokens = await CollectTokens(provider, "test");
        Assert.Empty(tokens);
    }

    #endregion

    #region Ollama Streaming

    [Fact]
    public async Task ShouldYieldTokens_WhenOllamaGenerateStreamingAsync()
    {
        var ndjson = string.Join("\n",
            """{"response":"Hello","done":false}""",
            """{"response":" Ollama","done":false}""",
            """{"response":"","done":true}""");

        using var provider = CreateOllamaProvider(ndjson, HttpStatusCode.OK);

        var tokens = await CollectTokens(provider, "test prompt");

        Assert.Equal(2, tokens.Count);
        Assert.Equal("Hello", tokens[0]);
        Assert.Equal(" Ollama", tokens[1]);
    }

    [Fact]
    public async Task ShouldYieldNothing_WhenOllamaGenerateStreamingAsyncServerError()
    {
        using var provider = CreateOllamaProvider("", HttpStatusCode.InternalServerError);
        var tokens = await CollectTokens(provider, "test");
        Assert.Empty(tokens);
    }

    #endregion

    #region Groq Streaming

    [Fact]
    public async Task ShouldYieldTokens_WhenGroqGenerateStreamingAsync()
    {
        var sseData = BuildSseStream(
            """{"choices":[{"delta":{"content":"Fast"}}]}""",
            """{"choices":[{"delta":{"content":" response"}}]}""");

        using var provider = CreateGroqProvider(sseData, HttpStatusCode.OK);

        var tokens = await CollectTokens(provider, "test prompt");

        Assert.Equal(2, tokens.Count);
        Assert.Equal("Fast", tokens[0]);
        Assert.Equal(" response", tokens[1]);
    }

    #endregion

    #region Azure OpenAI Streaming

    [Fact]
    public async Task ShouldYieldTokens_WhenAzureOpenAIGenerateStreamingAsync()
    {
        var sseData = BuildSseStream(
            """{"choices":[{"delta":{"content":"Azure"}}]}""",
            """{"choices":[{"delta":{"content":" token"}}]}""");

        using var provider = CreateAzureOpenAIProvider(sseData, HttpStatusCode.OK);

        var tokens = await CollectTokens(provider, "test prompt");

        Assert.Equal(2, tokens.Count);
        Assert.Equal("Azure", tokens[0]);
        Assert.Equal(" token", tokens[1]);
    }

    #endregion

    #region SSE Termination

    [Fact]
    public async Task ShouldRespectDoneTerminator_WhenOpenAIGenerateStreamingAsync()
    {
        var sseData = "data: " + """{"choices":[{"delta":{"content":"before"}}]}""" + "\n\n" +
                       "data: [DONE]\n\n" +
                       "data: " + """{"choices":[{"delta":{"content":"after"}}]}""" + "\n\n";

        using var provider = CreateOpenAIProvider(sseData, HttpStatusCode.OK, rawSse: true);

        var tokens = await CollectTokens(provider, "test");

        Assert.Single(tokens);
        Assert.Equal("before", tokens[0]);
    }

    #endregion

    #region Helpers

    private static string BuildSseStream(params string[] jsonPayloads)
    {
        var sb = new StringBuilder();
        foreach (var payload in jsonPayloads)
        {
            sb.AppendLine($"data: {payload}");
            sb.AppendLine();
        }
        sb.AppendLine("data: [DONE]");
        sb.AppendLine();
        return sb.ToString();
    }

    private static async Task<List<string>> CollectTokens(
        IStreamingLlmProvider provider, string prompt)
    {
        var tokens = new List<string>();
        await foreach (var token in provider.GenerateStreamingAsync(prompt))
        {
            tokens.Add(token);
        }
        return tokens;
    }

    private void SetupHttpClient(string responseBody, HttpStatusCode statusCode)
    {
        var handler = _httpClientFactory.SetupDefaultHandler();
        handler.SetResponse(new HttpResponseMessage(statusCode)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(responseBody)))
        });
    }

    private OpenAIProvider CreateOpenAIProvider(
        string responseBody, HttpStatusCode statusCode, string apiKey = "sk-test", bool rawSse = false)
    {
        SetupHttpClient(responseBody, statusCode);
        var config = LlmConfig.Default() with { ApiKey = apiKey, Model = ModelGpt4 };
        return new OpenAIProvider(config, _httpClientFactory, _noOpPolicy,
            NullLogger<OpenAIProvider>.Instance);
    }

    private AnthropicLlmProvider CreateAnthropicProvider(
        string responseBody, HttpStatusCode statusCode, string apiKey = "sk-test")
    {
        SetupHttpClient(responseBody, statusCode);
        var config = LlmConfig.Default() with { ApiKey = apiKey, Model = ModelClaude3Opus };
        return new AnthropicLlmProvider(config, _httpClientFactory, _noOpPolicy,
            NullLogger<AnthropicLlmProvider>.Instance);
    }

    private OllamaLlmProvider CreateOllamaProvider(
        string responseBody, HttpStatusCode statusCode)
    {
        SetupHttpClient(responseBody, statusCode);
        var config = LlmConfig.Default() with { Model = ModelLlama2, BaseUrl = new Uri(EndpointOllamaDefault) };
        return new OllamaLlmProvider(config, _httpClientFactory, _noOpPolicy,
            NullLogger<OllamaLlmProvider>.Instance);
    }

    private GroqLlmProvider CreateGroqProvider(
        string responseBody, HttpStatusCode statusCode)
    {
        SetupHttpClient(responseBody, statusCode);
        var config = LlmConfig.Default() with { ApiKey = "gsk-test", Model = "llama-3.3-70b-versatile" };
        return new GroqLlmProvider(config, _httpClientFactory, _noOpPolicy,
            NullLogger<GroqLlmProvider>.Instance);
    }

    private AzureOpenAILlmProvider CreateAzureOpenAIProvider(
        string responseBody, HttpStatusCode statusCode)
    {
        SetupHttpClient(responseBody, statusCode);
        var config = LlmConfig.Default() with
        {
            ApiKey = "azure-key",
            Model = ModelGpt4,
            BaseUrl = new Uri("https://myendpoint.openai.azure.com")
        };
        return new AzureOpenAILlmProvider(config, _httpClientFactory, _noOpPolicy,
            NullLogger<AzureOpenAILlmProvider>.Instance);
    }

    #endregion

    public void Dispose()
    {
        _httpClientFactory.Dispose();
        GC.SuppressFinalize(this);
    }
}

#pragma warning restore CS0618
