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
/// Regression coverage for Experiment 07 friction #4: a per-call <see cref="LlmThinkingConfig"/>
/// must reach the DeepSeek API as a <c>thinking</c> block and a <c>reasoning_effort</c> field.
/// Before the fix, the YAML keys were parsed into <see cref="LlmYamlConfig"/> but silently dropped
/// before the request hit the wire.
/// </summary>
public class DeepSeekThinkingPayloadTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<DeepSeekLlmProvider> _logger = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    private static TestHttpMessageHandler CreateOkHandler() => TestHttpMessageHandler.CreateWithResponse(
        HttpStatusCode.OK,
        JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "ok" } } },
            usage = new { total_tokens = 1, prompt_tokens = 1, completion_tokens = 0 }
        }));

    private static async Task<JsonElement> ReadRequestBodyAsync(HttpRequestMessage request)
    {
        var raw = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task GenerateAsync_ShouldEmitThinkingBlock_WhenThinkingEnabledIsTrue()
    {
        using var handler = CreateOkHandler();
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        var config = LlmConfig.Create("deepseek-v4-flash", TestApiKey) with
        {
            Thinking = new LlmThinkingConfig { Enabled = true, Effort = "max" }
        };
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);

        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        var body = await ReadRequestBodyAsync(handler.CapturedRequests.Single());
        Assert.True(body.TryGetProperty("thinking", out var thinking),
            "Expected `thinking` block to be present in the DeepSeek payload.");
        Assert.Equal("enabled", thinking.GetProperty("type").GetString());

        Assert.True(body.TryGetProperty("reasoning_effort", out var effort));
        Assert.Equal("max", effort.GetString());
    }

    [Fact]
    public async Task GenerateAsync_ShouldEmitDisabledThinking_WhenEnabledIsFalse()
    {
        using var handler = CreateOkHandler();
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        var config = LlmConfig.Create("deepseek-v4-flash", TestApiKey) with
        {
            Thinking = new LlmThinkingConfig { Enabled = false }
        };
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);

        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        var body = await ReadRequestBodyAsync(handler.CapturedRequests.Single());
        Assert.Equal("disabled", body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(body.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task GenerateAsync_ShouldOmitThinkingFields_WhenThinkingIsNull()
    {
        using var handler = CreateOkHandler();
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        var config = LlmConfig.Create("deepseek-v4-flash", TestApiKey);
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);

        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        var body = await ReadRequestBodyAsync(handler.CapturedRequests.Single());
        Assert.False(body.TryGetProperty("thinking", out _));
        Assert.False(body.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task GenerateAsync_ShouldEmitTopP_WhenConfigured()
    {
        using var handler = CreateOkHandler();
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        var config = LlmConfig.Create("deepseek-v4-flash", TestApiKey) with { TopP = 0.85 };
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);

        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        var body = await ReadRequestBodyAsync(handler.CapturedRequests.Single());
        Assert.True(body.TryGetProperty("top_p", out var topP));
        Assert.Equal(0.85, topP.GetDouble());
    }
}
