using System.Net;
using System.Text.Json;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Polly;
using TestHttpClientFactory = Orkeon.Infrastructure.Tests.TestDoubles.TestHttpClientFactory;
using TestHttpMessageHandler = Orkeon.Infrastructure.Tests.TestDoubles.TestHttpMessageHandler;

namespace Orkeon.Infrastructure.Tests.LLMs.CallConfiguration;

/// <summary>
/// GAP-29: a call's <see cref="LlmConfig"/> completes the configuration its provider was built
/// with, instead of replacing it. Every caller outside the chat client — the planner, the
/// cognitive memory, the context-window summarizer, the RaggableTree summarizer, the agent loops —
/// builds a configuration of its own that names a temperature and nothing about the connection;
/// the providers took it whole (<c>config ?? Config</c>), so on a real vendor such a call lost the
/// key (and answered "API key is required"), the base URL (and left for the vendor's default), the
/// timeout (back to 30 s) and the provider's own settings.
/// </summary>
/// <remarks>
/// The key, the URL and the timeout are on the <b>provider only</b> in every test here: a test that
/// puts them on the call proves nothing, which is how this stayed invisible. The assertions read
/// what reached the wire — the request the handler captured and the timeout of the client it was
/// sent with.
/// </remarks>
public sealed class ProviderCallConfigurationTests
{
    private const string Key = "sk-provider-only";
    private const int TimeoutSeconds = 600;
    private static readonly Uri Endpoint = new("https://llm.example.test/v1");

    private const string OpenAiShapedAnswer =
        """{"choices":[{"message":{"role":"assistant","content":"ok"}}],"usage":{"total_tokens":2,"prompt_tokens":1,"completion_tokens":1}}""";

    private const string OpenAiShapedStream =
        "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n";

    private const string AnthropicAnswer =
        """{"content":[{"type":"text","text":"ok"}],"usage":{"input_tokens":1,"output_tokens":1}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LlmMessage[] Hello => [LlmMessage.User("hello")];

    /// <summary>What a caller outside the chat client sends: its own sampling, nothing about the connection.</summary>
    private static LlmConfig CallerConfig() => LlmConfig.OnProfile() with { Temperature = 0.3 };

    private static LlmConfig Configured(string model) =>
        LlmConfig.Create(model, Key) with { BaseUrl = Endpoint, TimeoutSeconds = TimeoutSeconds };

    /// <summary>One provider's wire: the handler that captures, the client it was sent with.</summary>
    private sealed class Wire : IDisposable
    {
        private readonly TestHttpMessageHandler _handler;

        public Wire(string clientName, string answer)
        {
            _handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, answer);
            Client = new HttpClient(_handler);
            Factory.RegisterClient(clientName, Client);
        }

        public HttpClient Client { get; }

        public TestHttpClientFactory Factory { get; } = new();

        public HttpRequestMessage Sent => Assert.Single(_handler.CapturedRequests);

        public async Task<JsonElement> SentPayloadAsync()
        {
            var body = await Sent.Content!.ReadAsStringAsync(Ct);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }

        public void Dispose()
        {
            Client.Dispose();
            _handler.Dispose();
        }
    }

    private static Wire WireFor<TProvider>(string answer) => new(typeof(TProvider).Name, answer);

    private static void AssertBearer(HttpRequestMessage sent, string key)
    {
        Assert.NotNull(sent.Headers.Authorization);
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.Equal(key, sent.Headers.Authorization.Parameter);
    }

    // ── The OpenAI-compatible family ─────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_call_without_a_connection_runs_on_the_providers_key_url_and_timeout(bool chat)
    {
        using var wire = WireFor<DeepSeekLlmProvider>(OpenAiShapedAnswer);
        using var provider = new DeepSeekLlmProvider(Configured("deepseek-chat"), wire.Factory, Policy.NoOpAsync<HttpResponseMessage>());

        var response = chat
            ? await provider.ChatAsync(Hello, CallerConfig(), Ct)
            : await provider.GenerateAsync("hello", CallerConfig(), Ct);

        Assert.Null(response.Error);
        AssertBearer(wire.Sent, Key);
        Assert.Equal(new Uri("https://llm.example.test/v1/chat/completions"), wire.Sent.RequestUri);
        Assert.Equal(TimeSpan.FromSeconds(TimeoutSeconds), wire.Client.Timeout);
        // The call's own setting still wins: its temperature, on the provider's model.
        var payload = await wire.SentPayloadAsync();
        Assert.Equal(0.3, payload.GetProperty("temperature").GetDouble(), precision: 3);
        Assert.Equal("deepseek-chat", payload.GetProperty("model").GetString());
    }

    [Fact]
    public async Task A_streamed_call_without_a_connection_runs_on_the_providers_key_and_url()
    {
        using var wire = WireFor<DeepSeekLlmProvider>(OpenAiShapedStream);
        using var provider = new DeepSeekLlmProvider(Configured("deepseek-chat"), wire.Factory, Policy.NoOpAsync<HttpResponseMessage>());

        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(Hello, CallerConfig(), Ct))
            events.Add(ev);

        Assert.Null(Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!.Error);
        AssertBearer(wire.Sent, Key);
        Assert.Equal(new Uri("https://llm.example.test/v1/chat/completions"), wire.Sent.RequestUri);
        Assert.Equal(TimeSpan.FromSeconds(TimeoutSeconds), wire.Client.Timeout);
    }

    [Fact]
    public async Task What_the_call_sets_wins_over_the_provider()
    {
        using var wire = WireFor<DeepSeekLlmProvider>(OpenAiShapedAnswer);
        using var provider = new DeepSeekLlmProvider(Configured("deepseek-chat"), wire.Factory, Policy.NoOpAsync<HttpResponseMessage>());

        var call = CallerConfig() with
        {
            ApiKey = "sk-of-the-call",
            BaseUrl = new Uri("https://other.example.test/v2"),
            TimeoutSeconds = 45,
            Model = "deepseek-reasoner",
        };
        await provider.ChatAsync(Hello, call, Ct);

        AssertBearer(wire.Sent, "sk-of-the-call");
        Assert.Equal(new Uri("https://other.example.test/v2/chat/completions"), wire.Sent.RequestUri);
        Assert.Equal(TimeSpan.FromSeconds(45), wire.Client.Timeout);
        Assert.Equal("deepseek-reasoner", (await wire.SentPayloadAsync()).GetProperty("model").GetString());
    }

    [Fact]
    public async Task The_providers_own_settings_reach_a_call_that_names_none()
    {
        // Llm:Thinking:Enabled = false is the host's answer to a reasoning model that times out;
        // a call that does not mention thinking must not switch it back on.
        using var wire = WireFor<DeepSeekLlmProvider>(OpenAiShapedAnswer);
        var configured = Configured("deepseek-chat") with { Thinking = new LlmThinkingConfig { Enabled = false } };
        using var provider = new DeepSeekLlmProvider(configured, wire.Factory, Policy.NoOpAsync<HttpResponseMessage>());

        await provider.ChatAsync(Hello, CallerConfig(), Ct);

        var payload = await wire.SentPayloadAsync();
        Assert.Equal("disabled", payload.GetProperty("thinking").GetProperty("type").GetString());
    }

    // ── The three providers outside that family ─────────────────────────────

    [Fact]
    public async Task Anthropic_sends_the_providers_key_workspace_url_and_timeout()
    {
        using var wire = WireFor<AnthropicLlmProvider>(AnthropicAnswer);
        var configured = LlmConfig.Create("claude-sonnet-5", Key) with
        {
            BaseUrl = new Uri("https://anthropic.example.test"),
            WorkspaceId = "wrkspc_provider",
            TimeoutSeconds = TimeoutSeconds,
        };
        using var provider = new AnthropicLlmProvider(configured, wire.Factory, Policy.NoOpAsync<HttpResponseMessage>());

        var response = await provider.ChatAsync(Hello, CallerConfig(), Ct);

        Assert.Null(response.Error);
        Assert.Equal(Key, Assert.Single(wire.Sent.Headers.GetValues("x-api-key")));
        Assert.Equal("wrkspc_provider", Assert.Single(wire.Sent.Headers.GetValues("anthropic-workspace-id")));
        Assert.Equal(new Uri("https://anthropic.example.test/v1/messages"), wire.Sent.RequestUri);
        Assert.Equal(TimeSpan.FromSeconds(TimeoutSeconds), wire.Client.Timeout);
    }

    [Fact]
    public async Task Azure_sends_the_providers_key_endpoint_api_version_and_timeout()
    {
        using var wire = WireFor<AzureOpenAILlmProvider>(OpenAiShapedAnswer);
        var configured = LlmConfig.Create("my-deployment", Key) with
        {
            BaseUrl = new Uri("https://my-resource.openai.azure.com"),
            ApiVersion = "2025-04-01-preview",
            TimeoutSeconds = TimeoutSeconds,
        };
        using var provider = new AzureOpenAILlmProvider(configured, wire.Factory, Policy.NoOpAsync<HttpResponseMessage>());

        var response = await provider.GenerateAsync("hello", CallerConfig(), Ct);

        Assert.Null(response.Error);
        Assert.Equal(Key, Assert.Single(wire.Sent.Headers.GetValues("api-key")));
        Assert.Equal(
            new Uri("https://my-resource.openai.azure.com/openai/deployments/my-deployment/chat/completions?api-version=2025-04-01-preview"),
            wire.Sent.RequestUri);
        Assert.Equal(TimeSpan.FromSeconds(TimeoutSeconds), wire.Client.Timeout);
    }

    [Fact]
    public async Task Azure_keeps_an_api_version_the_provider_carries_as_a_custom_parameter()
    {
        using var wire = WireFor<AzureOpenAILlmProvider>(OpenAiShapedAnswer);
        var configured = LlmConfig.Create("my-deployment", Key) with
        {
            BaseUrl = new Uri("https://my-resource.openai.azure.com"),
            CustomParameters = new Dictionary<string, object> { ["api_version"] = "v1" },
        };
        using var provider = new AzureOpenAILlmProvider(configured, wire.Factory, Policy.NoOpAsync<HttpResponseMessage>());

        await provider.ChatAsync(Hello, CallerConfig(), Ct);

        Assert.Equal(new Uri("https://my-resource.openai.azure.com/openai/v1/chat/completions"), wire.Sent.RequestUri);
    }

    [Fact]
    public async Task Ollama_keeps_the_providers_timeout()
    {
        // Ollama's URL is fixed when the provider is built and it needs no key: the timeout was
        // the one thing a call's configuration took away.
        using var wire = WireFor<OllamaLlmProvider>("""{"response":"hi","done":true}""");
        var configured = LlmConfig.Create("qwen3") with
        {
            BaseUrl = new Uri("http://ollama.example.test:11434"),
            TimeoutSeconds = TimeoutSeconds,
        };
        using var provider = new OllamaLlmProvider(configured, wire.Factory);

        await provider.GenerateAsync("hello", CallerConfig(), Ct);

        Assert.Equal(new Uri("http://ollama.example.test:11434/api/generate"), wire.Sent.RequestUri);
        Assert.Equal(TimeSpan.FromSeconds(TimeoutSeconds), wire.Client.Timeout);
    }

    [Fact]
    public async Task A_timeout_nobody_sets_is_thirty_seconds()
    {
        using var wire = WireFor<DeepSeekLlmProvider>(OpenAiShapedAnswer);
        using var provider = new DeepSeekLlmProvider(
            LlmConfig.Create("deepseek-chat", Key), wire.Factory, Policy.NoOpAsync<HttpResponseMessage>());

        await provider.ChatAsync(Hello, CallerConfig(), Ct);

        Assert.Equal(TimeSpan.FromSeconds(30), wire.Client.Timeout);
    }
}
