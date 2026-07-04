using System.Net;
using System.Text.Json;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// LLM Response Format — DeepSeek payload coverage.
/// Verifies that <see cref="LlmConfig.ResponseFormat"/> reaches the wire as
/// <c>response_format: { "type": "..." }</c> in both <c>GenerateAsync</c>
/// (plain prompt path) and <c>ChatAsync</c> (multi-turn with tool metadata).
/// Also verifies the anti-"stuck stream" warning fires when the prompt does
/// not mention "json".
/// </summary>
public class DeepSeekResponseFormatPayloadTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<DeepSeekLlmProvider> _logger = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    private static TestHttpMessageHandler CreateOkHandler() => TestHttpMessageHandler.CreateWithResponse(
        HttpStatusCode.OK,
        JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "{}" } } },
            usage = new { total_tokens = 1, prompt_tokens = 1, completion_tokens = 0 }
        }));

    private static async Task<JsonElement> ReadRequestBodyAsync(HttpRequestMessage request)
    {
        var raw = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task GenerateAsync_ShouldEmitResponseFormat_WhenJsonObjectRequested()
    {
        using var handler = CreateOkHandler();
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        var config = LlmConfig.Create("deepseek-chat", TestApiKey) with
        {
            ResponseFormat = LlmResponseFormat.JsonObject()
        };
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);

        await provider.GenerateAsync("Return the answer as a json object.", cancellationToken: TestContext.Current.CancellationToken);

        var body = await ReadRequestBodyAsync(handler.CapturedRequests.Single());
        Assert.True(body.TryGetProperty("response_format", out var rf),
            "Expected `response_format` block in DeepSeek payload.");
        Assert.Equal("json_object", rf.GetProperty("type").GetString());
    }

    [Fact]
    public async Task GenerateAsync_ShouldOmitResponseFormat_WhenTypeIsText()
    {
        using var handler = CreateOkHandler();
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        var config = LlmConfig.Create("deepseek-chat", TestApiKey) with
        {
            ResponseFormat = LlmResponseFormat.Text()
        };
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);

        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        var body = await ReadRequestBodyAsync(handler.CapturedRequests.Single());
        Assert.False(body.TryGetProperty("response_format", out _),
            "`response_format` must not be emitted when Type == \"text\" (provider default).");
    }

    [Fact]
    public async Task GenerateAsync_ShouldOmitResponseFormat_WhenConfigResponseFormatIsNull()
    {
        using var handler = CreateOkHandler();
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        var config = LlmConfig.Create("deepseek-chat", TestApiKey); // ResponseFormat null
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);

        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        var body = await ReadRequestBodyAsync(handler.CapturedRequests.Single());
        Assert.False(body.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task GenerateAsync_ShouldWarn_WhenJsonObjectAndPromptHasNoJsonKeyword()
    {
        using var handler = CreateOkHandler();
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        var config = LlmConfig.Create("deepseek-chat", TestApiKey) with
        {
            ResponseFormat = LlmResponseFormat.JsonObject()
        };
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);

        await provider.GenerateAsync("Tell me about cats.", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(_logger.HasLoggedWarning("json"),
            "Expected a warning mentioning the missing 'json' keyword when response_format=json_object.");
    }

    [Fact]
    public async Task GenerateAsync_ShouldNotWarn_WhenPromptContainsJsonKeyword()
    {
        using var handler = CreateOkHandler();
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        var config = LlmConfig.Create("deepseek-chat", TestApiKey) with
        {
            ResponseFormat = LlmResponseFormat.JsonObject()
        };
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);

        await provider.GenerateAsync("Return the answer as JSON.", cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(_logger.HasLoggedWarning("json keyword"),
            "Did not expect a warning when the prompt mentions json.");
    }

    [Fact]
    public async Task ChatAsync_ShouldEmitResponseFormat_OnMultiTurnToolCalls()
    {
        using var handler = CreateOkHandler();
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        var config = LlmConfig.Create("deepseek-chat", TestApiKey) with
        {
            ResponseFormat = LlmResponseFormat.JsonObject()
        };
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);

        var messages = new[]
        {
            new LlmMessage { Role = "system", Content = "Reply as a json object." },
            new LlmMessage { Role = "user",   Content = "List 2 colours." },
            new LlmMessage { Role = "assistant", Content = "calling tool", ToolCallId = "call_1" },
        };

        await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        var body = await ReadRequestBodyAsync(handler.CapturedRequests.Single());
        Assert.True(body.TryGetProperty("response_format", out var rf));
        Assert.Equal("json_object", rf.GetProperty("type").GetString());
    }

    [Fact]
    public async Task GenerateAsync_ShouldEmitBothThinkingAndResponseFormat()
    {
        using var handler = CreateOkHandler();
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        var config = LlmConfig.Create("deepseek-v4-flash", TestApiKey) with
        {
            Thinking = new LlmThinkingConfig { Enabled = true, Effort = "high" },
            ResponseFormat = LlmResponseFormat.JsonObject()
        };
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);

        await provider.GenerateAsync("Return a json answer.", cancellationToken: TestContext.Current.CancellationToken);

        var body = await ReadRequestBodyAsync(handler.CapturedRequests.Single());
        Assert.Equal("enabled", body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("high", body.GetProperty("reasoning_effort").GetString());
        Assert.Equal("json_object", body.GetProperty("response_format").GetProperty("type").GetString());
    }
}
