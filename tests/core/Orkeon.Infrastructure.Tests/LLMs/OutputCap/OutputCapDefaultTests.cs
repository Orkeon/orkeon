using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Orkeon.Constants.Llm;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// What reaches the wire when nothing pins the output cap (LLM-10): the model's documented
/// maximum, the 4096 fallback for an unknown model, no field at all for a vendor that
/// documents no cap — and a pinned value untouched, whatever the model.
/// </summary>
public sealed class OutputCapDefaultTests : IDisposable
{
    private const string MinimalSuccessResponse =
        """{"choices":[{"message":{"content":"ok"}}],"usage":{"total_tokens":2}}""";

    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly MockHttpMessageHandler _handler;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;

    public OutputCapDefaultTests()
    {
        _httpClientFactory = new MockHttpClientFactory();
        _handler = _httpClientFactory.SetupDefaultHandler();
        _handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(MinimalSuccessResponse, System.Text.Encoding.UTF8, "application/json")
        });
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
    }

    private async Task<JsonDocument> ReadSentPayloadAsync()
    {
        Assert.NotNull(_handler.LastRequest);
        Assert.NotNull(_handler.LastRequest!.Content);
        var body = await _handler.LastRequest.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(body);
    }

    [Fact]
    public async Task OpenAI_sends_the_documented_maximum_when_nothing_pins_the_cap()
    {
        using var provider = new OpenAIProvider(LlmConfig.Create("gpt-5.6-sol", TestApiKey), _httpClientFactory, _noOpPolicy);

        await provider.ChatAsync([LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        using var payload = await ReadSentPayloadAsync();
        Assert.Equal(128_000, payload.RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public async Task DeepSeek_sends_384K_on_its_default_where_the_engine_used_to_send_4096()
    {
        using var provider = new DeepSeekLlmProvider(LlmConfig.Create(LlmProviderDefaultModels.DeepSeek, TestApiKey), _httpClientFactory, _noOpPolicy);

        await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        using var payload = await ReadSentPayloadAsync();
        Assert.Equal(393_216, payload.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task An_unknown_model_keeps_the_4096_fallback_exactly_as_before()
    {
        using var provider = new DeepSeekLlmProvider(LlmConfig.Create("deepseek-experimental-nobody-documented", TestApiKey), _httpClientFactory, _noOpPolicy);

        await provider.ChatAsync([LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        using var payload = await ReadSentPayloadAsync();
        Assert.Equal(LlmDefaults.FallbackMaxOutputTokens, payload.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task A_pinned_cap_reaches_the_wire_untouched_whatever_the_model()
    {
        var config = LlmConfig.Create(LlmProviderDefaultModels.DeepSeek, TestApiKey) with { MaxTokens = 4096 };
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy);

        await provider.ChatAsync([LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        using var payload = await ReadSentPayloadAsync();
        Assert.Equal(4096, payload.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task Mistral_leaves_the_field_out_because_the_vendor_documents_no_cap()
    {
        // Prompt + max_tokens may not exceed the window: a fixed cap fails on a real prompt,
        // and no cap at all lets the model write to its window — the actual maximum.
        using var provider = new MistralLlmProvider(LlmConfig.Create(LlmProviderDefaultModels.Mistral, TestApiKey), _httpClientFactory, _noOpPolicy);

        await provider.ChatAsync([LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        using var payload = await ReadSentPayloadAsync();
        Assert.False(payload.RootElement.TryGetProperty("max_tokens", out _));
        Assert.False(payload.RootElement.TryGetProperty("max_completion_tokens", out _));
    }

    [Fact]
    public async Task Mistral_still_writes_a_pinned_cap()
    {
        var config = LlmConfig.Create(LlmProviderDefaultModels.Mistral, TestApiKey) with { MaxTokens = 900 };
        using var provider = new MistralLlmProvider(config, _httpClientFactory, _noOpPolicy);

        await provider.ChatAsync([LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        using var payload = await ReadSentPayloadAsync();
        Assert.Equal(900, payload.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task Together_names_its_window_as_the_cap_and_asks_the_endpoint_to_clamp_rather_than_refuse()
    {
        using var provider = new TogetherAiLlmProvider(LlmConfig.Create(LlmProviderDefaultModels.Together, TestApiKey), _httpClientFactory, _noOpPolicy);

        await provider.ChatAsync([LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        using var payload = await ReadSentPayloadAsync();
        Assert.Equal(131_072, payload.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("truncate", payload.RootElement.GetProperty("context_length_exceeded_behavior").GetString());
    }

    [Fact]
    public async Task Anthropic_sends_the_documented_maximum_and_keeps_4096_for_an_unknown_claude()
    {
        var anthropicResponse = """{"content":[{"type":"text","text":"ok"}],"usage":{"input_tokens":1,"output_tokens":1}}""";
        _handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(anthropicResponse, System.Text.Encoding.UTF8, "application/json")
        });

        using var known = new AnthropicLlmProvider(LlmConfig.Create(LlmProviderDefaultModels.Anthropic, TestApiKey), _httpClientFactory, _noOpPolicy, NullLogger<AnthropicLlmProvider>.Instance);
        await known.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        using (var payload = await ReadSentPayloadAsync())
            Assert.Equal(128_000, payload.RootElement.GetProperty("max_tokens").GetInt32());

        using var unknown = new AnthropicLlmProvider(LlmConfig.Create("claude-experimental-nobody-documented", TestApiKey), _httpClientFactory, _noOpPolicy, NullLogger<AnthropicLlmProvider>.Instance);
        await unknown.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        using (var payload = await ReadSentPayloadAsync())
            Assert.Equal(LlmDefaults.FallbackMaxOutputTokens, payload.RootElement.GetProperty("max_tokens").GetInt32());
    }

    public void Dispose()
    {
        _handler.Dispose();
        _httpClientFactory.Dispose();
    }
}

/// <summary>
/// A catalogue cap the endpoint refuses is dropped once — the vendor's own default applies —
/// and said so; a cap the user pinned is theirs, and its rejection surfaces unchanged.
/// </summary>
public sealed class CatalogueOutputCapRetryTests
{
    private const string QwenRejection =
        """{"error":{"code":"InvalidParameter","message":"Range of max_tokens should be [1, 8192]","type":"invalid_request_error"}}""";

    private static string Success() => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = "ok" } } },
        usage = new { total_tokens = 7 },
    });

    private static (QwenLlmProvider Provider, TestHttpMessageHandler Handler) Build(LlmConfig config, Func<HttpRequestMessage, HttpResponseMessage> responses)
    {
        var handler = new TestHttpMessageHandler(responses);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("QwenLlmProvider", new HttpClient(handler));
        var provider = new QwenLlmProvider(config, factory, resiliencePolicy: null, new TestLogger<QwenLlmProvider>());
        return (provider, handler);
    }

    [Fact]
    public async Task A_refused_catalogue_cap_is_retried_once_without_the_field()
    {
        var calls = 0;
        var (provider, handler) = Build(LlmConfig.Create(LlmProviderDefaultModels.Qwen, TestApiKey), _ => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(QwenRejection) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Success()) });
        using var owned = provider;

        var result = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", result.Content);
        Assert.Equal(2, handler.CapturedRequests.Count);

        var first = await handler.CapturedRequests[0].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using (var doc = JsonDocument.Parse(first))
            Assert.Equal(131_072, doc.RootElement.GetProperty("max_tokens").GetInt32());

        var retried = await handler.CapturedRequests[1].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using (var doc = JsonDocument.Parse(retried))
            Assert.False(doc.RootElement.TryGetProperty("max_tokens", out _));
    }

    [Fact]
    public async Task A_refused_pinned_cap_is_not_retried()
    {
        var (provider, handler) = Build(
            LlmConfig.Create(LlmProviderDefaultModels.Qwen, TestApiKey) with { MaxTokens = 200_000 },
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(QwenRejection) });
        using var owned = provider;

        var result = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(handler.CapturedRequests);
        Assert.NotNull(result.Metadata);
        Assert.True(result.Metadata!.ContainsKey("error"));
    }

    [Fact]
    public async Task A_rejection_about_something_else_is_not_mistaken_for_the_cap()
    {
        var (provider, handler) = Build(
            LlmConfig.Create(LlmProviderDefaultModels.Qwen, TestApiKey),
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":{"message":"invalid api key"}}""") });
        using var owned = provider;

        await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(handler.CapturedRequests);
    }
}
