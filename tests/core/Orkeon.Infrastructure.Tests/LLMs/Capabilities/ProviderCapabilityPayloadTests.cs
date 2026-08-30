using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Cross-provider coverage of the capability foundation (LLM-02) and of the two options it
/// carries: structured outputs (LLM-03) and thinking (LLM-04).
/// </summary>
/// <remarks>
/// The point of the audit was not that the wiring was missing but that its absence was
/// invisible: a YAML crew could declare <c>response_format</c> or <c>thinking</c> and the
/// option would be dropped without a trace on ten providers out of twelve. So every test here
/// asserts one of two things — the field reaches the wire in the provider's own dialect, or an
/// actionable warning is emitted. Never silence.
/// </remarks>
public class ProviderCapabilityPayloadTests
{
    private const string SampleSchema =
        """{"type":"object","properties":{"answer":{"type":"string"}},"required":["answer"]}""";

    /// <summary>Every provider, by the type name its HTTP client is registered under.</summary>
    public static TheoryData<string> AllProviders() =>
    [
        nameof(OpenAIProvider), nameof(AzureOpenAILlmProvider),
        nameof(TogetherAiLlmProvider), nameof(MistralLlmProvider), nameof(KimiLlmProvider),
        nameof(QwenLlmProvider), nameof(HuggingFaceLlmProvider), nameof(ZaiLlmProvider),
        nameof(DeepSeekLlmProvider), nameof(AnthropicLlmProvider), nameof(OllamaLlmProvider),
        nameof(GrokLlmProvider), nameof(MiniMaxLlmProvider),
    ];

    // ── The declaration itself ──────────────────────────────────────────────

    /// <summary>
    /// The declaration is what the whole mechanism reads, so it is reviewed in diff rather
    /// than left implicit. Only DeepSeek replays reasoning content; only Anthropic needs
    /// explicit cache breakpoints — the two claims the audit found most often over-generalised.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllProviders))]
    public void ShouldDeclareCapabilities_ForEveryProvider(string providerTypeName)
    {
        var provider = ProviderProbe.Create(providerTypeName);
        try
        {
            var capabilities = provider.Capabilities;

            Assert.NotSame(LlmProviderCapabilities.Unknown, capabilities);
            Assert.Equal(providerTypeName == nameof(DeepSeekLlmProvider), capabilities.ReplaysReasoningContent);
            Assert.Equal(providerTypeName == nameof(AnthropicLlmProvider), capabilities.ExplicitPromptCaching);
            Assert.Equal(providerTypeName == nameof(DeepSeekLlmProvider), capabilities.RequiresJsonKeywordInPrompt);
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    // ── Structured outputs, in each dialect (G-15) ──────────────────────────

    [Theory]
    [InlineData(nameof(OpenAIProvider))]
    [InlineData(nameof(AzureOpenAILlmProvider))]
    [InlineData(nameof(TogetherAiLlmProvider))]
    [InlineData(nameof(MistralLlmProvider))]
    [InlineData(nameof(KimiLlmProvider))]
    [InlineData(nameof(QwenLlmProvider))]
    [InlineData(nameof(HuggingFaceLlmProvider))]
    [InlineData(nameof(ZaiLlmProvider))]
    [InlineData(nameof(DeepSeekLlmProvider))]
    [InlineData(nameof(GrokLlmProvider))]
    public async Task ShouldSendJsonObjectConstraint_OnTheOpenAiCompatibleFamily(string providerTypeName)
    {
        var body = await ProviderProbe.CapturePayloadAsync(providerTypeName, config => config with
        {
            // "json" in the prompt keeps DeepSeek's own guard quiet; irrelevant elsewhere.
            SystemMessage = "Answer in json.",
            ResponseFormat = LlmResponseFormat.JsonObject(),
        });

        Assert.True(body.TryGetProperty("response_format", out var value), "expected `response_format` on the wire");
        Assert.Equal("json_object", value.GetProperty("type").GetString());
    }

    /// <summary>
    /// Anthropic has **no schema-less JSON mode**: <c>output_config.format</c> takes exactly
    /// <c>type</c> (always <c>"json_schema"</c>) and <c>schema</c>. A bare <c>json_object</c>
    /// request cannot be expressed at the API level, so it is reported rather than sent in a
    /// shape the API would reject.
    /// </summary>
    [Fact]
    public async Task ShouldReportAJsonObjectRequest_OnAnthropicWhichHasNoSchemaLessMode()
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(AnthropicLlmProvider), config => config with
        {
            ResponseFormat = LlmResponseFormat.JsonObject(),
        });

        Assert.False(probe.Body.TryGetProperty("output_config", out _));
        Assert.Contains(probe.Warnings, w => w.Contains("response_format without a schema", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldSendTheSchema_AsAnthropicOutputConfigFormat()
    {
        var body = await ProviderProbe.CapturePayloadAsync(nameof(AnthropicLlmProvider), config => config with
        {
            ResponseFormat = LlmResponseFormat.JsonSchema("answer_shape", SampleSchema),
        });

        var format = body.GetProperty("output_config").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Object, format.GetProperty("schema").ValueKind);
        // The object takes exactly two keys — no name, no strict (strict lives on tools).
        Assert.Equal(2, format.EnumerateObject().Count());
    }

    /// <summary>Ollama's dialect is a bare <c>format</c> field, not a nested object.</summary>
    [Fact]
    public async Task ShouldSendJsonObjectConstraint_AsOllamaFormatField()
    {
        var body = await ProviderProbe.CapturePayloadAsync(nameof(OllamaLlmProvider), config => config with
        {
            ResponseFormat = LlmResponseFormat.JsonObject(),
        });

        Assert.Equal("json", body.GetProperty("format").GetString());
    }

    [Theory]
    [InlineData(nameof(OpenAIProvider))]
    [InlineData(nameof(AzureOpenAILlmProvider))]
    [InlineData(nameof(GrokLlmProvider))]
    [InlineData(nameof(TogetherAiLlmProvider))]
    [InlineData(nameof(MistralLlmProvider))]
    public async Task ShouldSendTheSchema_WhenTheProviderValidatesOne(string providerTypeName)
    {
        var body = await ProviderProbe.CapturePayloadAsync(providerTypeName, config => config with
        {
            ResponseFormat = LlmResponseFormat.JsonSchema("answer_shape", SampleSchema),
        });

        var format = body.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());

        var schema = format.GetProperty("json_schema");
        Assert.Equal("answer_shape", schema.GetProperty("name").GetString());
        Assert.True(schema.GetProperty("strict").GetBoolean());
        // The schema must travel as a JSON object, not as an escaped string.
        Assert.Equal(JsonValueKind.Object, schema.GetProperty("schema").ValueKind);
        Assert.Equal("string",
            schema.GetProperty("schema").GetProperty("properties").GetProperty("answer")
                .GetProperty("type").GetString());
    }

    /// <summary>
    /// A provider that only guarantees well-formed JSON must degrade explicitly, so the caller
    /// learns the schema was not enforced instead of trusting a validation that never ran.
    /// </summary>
    [Theory]
    [InlineData(nameof(KimiLlmProvider))]
    [InlineData(nameof(ZaiLlmProvider))]
    [InlineData(nameof(HuggingFaceLlmProvider))]
    public async Task ShouldDowngradeSchemaToJsonObject_AndSaySo_WhenTheProviderCannotValidateIt(
        string providerTypeName)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(providerTypeName, config => config with
        {
            ResponseFormat = LlmResponseFormat.JsonSchema("answer_shape", SampleSchema),
        });

        Assert.Equal("json_object", probe.Body.GetProperty("response_format").GetProperty("type").GetString());
        Assert.False(probe.Body.GetProperty("response_format").TryGetProperty("json_schema", out _));
        Assert.Contains(probe.Warnings, w => w.Contains("response_format.schema", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AllProviders))]
    public async Task ShouldWriteNothing_WhenTheFormatIsTextOrUnset(string providerTypeName)
    {
        var textual = await ProviderProbe.CapturePayloadAsync(providerTypeName, config => config with
        {
            ResponseFormat = LlmResponseFormat.Text(),
        });
        var unset = await ProviderProbe.CapturePayloadAsync(providerTypeName, config => config);

        foreach (var body in new[] { textual, unset })
        {
            Assert.False(body.TryGetProperty("response_format", out _));
            Assert.False(body.TryGetProperty("format", out _));
            Assert.False(body.TryGetProperty("output_config", out _));
        }
    }

    // ── Thinking, in each dialect (G-16 / G-19) ─────────────────────────────

    [Theory]
    [InlineData(nameof(OpenAIProvider))]
    [InlineData(nameof(AzureOpenAILlmProvider))]
    [InlineData(nameof(GrokLlmProvider))]
    [InlineData(nameof(MistralLlmProvider))]
    [InlineData(nameof(KimiLlmProvider))]
    [InlineData(nameof(DeepSeekLlmProvider))]
    [InlineData(nameof(ZaiLlmProvider))]
    public async Task ShouldSendReasoningEffort_OnTheOpenAiCompatibleFamily(string providerTypeName)
    {
        var body = await ProviderProbe.CapturePayloadAsync(providerTypeName, config => config with
        {
            Thinking = new LlmThinkingConfig { Effort = "high" },
        });

        Assert.Equal("high", body.GetProperty("reasoning_effort").GetString());
    }

    /// <summary>
    /// Providers whose API decides on its own whether to reason accept the effort hint but not
    /// an on/off switch — and say so rather than pretending the switch was honoured.
    /// </summary>
    [Theory]
    [InlineData(nameof(OpenAIProvider))]
    [InlineData(nameof(GrokLlmProvider))]
    public async Task ShouldReportTheToggle_WhenOnlyAnEffortHintIsAccepted(string providerTypeName)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(providerTypeName, config => config with
        {
            Thinking = new LlmThinkingConfig { Enabled = false },
        });

        Assert.False(probe.Body.TryGetProperty("thinking", out _));
        Assert.Contains(probe.Warnings, w => w.Contains("thinking.enabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldSendAdaptiveThinkingAndEffort_OnAnthropic()
    {
        var body = await ProviderProbe.CapturePayloadAsync(nameof(AnthropicLlmProvider), config => config with
        {
            Thinking = new LlmThinkingConfig { Enabled = true, Effort = "high" },
        });

        Assert.Equal("adaptive", body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("high", body.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task ShouldSendThinkField_OnOllama()
    {
        var enabled = await ProviderProbe.CapturePayloadAsync(nameof(OllamaLlmProvider), config => config with
        {
            Thinking = new LlmThinkingConfig { Enabled = true },
        });
        Assert.True(enabled.GetProperty("think").GetBoolean());

        var effort = await ProviderProbe.CapturePayloadAsync(nameof(OllamaLlmProvider), config => config with
        {
            Thinking = new LlmThinkingConfig { Effort = "medium" },
        });
        Assert.Equal("medium", effort.GetProperty("think").GetString());
    }

    [Fact]
    public async Task ShouldSendDashScopeThinkingFields_OnQwen()
    {
        var body = await ProviderProbe.CapturePayloadAsync(nameof(QwenLlmProvider), config => config with
        {
            Thinking = new LlmThinkingConfig { Enabled = true, BudgetTokens = 2048 },
        });

        Assert.True(body.GetProperty("enable_thinking").GetBoolean());
        Assert.Equal(2048, body.GetProperty("thinking_budget").GetInt32());
        // The OpenAI-shaped block must not leak: DashScope does not read it.
        Assert.False(body.TryGetProperty("thinking", out _));
    }

    /// <summary>
    /// Qwen is the only API that takes a reasoning budget. Everywhere else the option is
    /// reported — the exact failure mode the audit found for every option before LLM-02.
    /// </summary>
    [Theory]
    [InlineData(nameof(OpenAIProvider))]
    [InlineData(nameof(DeepSeekLlmProvider))]
    [InlineData(nameof(AnthropicLlmProvider))]
    [InlineData(nameof(OllamaLlmProvider))]
    public async Task ShouldReportTheBudget_WhereTheApiTakesNone(string providerTypeName)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(providerTypeName, config => config with
        {
            Thinking = new LlmThinkingConfig { BudgetTokens = 4096 },
        });

        Assert.False(probe.Body.TryGetProperty("thinking_budget", out _));
        Assert.Contains(probe.Warnings, w => w.Contains("budgetTokens", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Test harness: builds a provider, captures the request body and its warnings.</summary>
    private static class ProviderProbe
    {
        internal sealed record Capture(JsonElement Body, IReadOnlyList<string> Warnings);

        private static readonly string OkBody = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "ok" } } },
            content = new[] { new { type = "text", text = "ok" } },     // Anthropic shape
            response = "ok",                                            // Ollama shape
            usage = new { total_tokens = 1, prompt_tokens = 1, completion_tokens = 0, input_tokens = 1, output_tokens = 0 },
        });

        internal static ILlmProvider Create(
            string providerTypeName, IHttpClientFactory? factory = null, WarningSink? sink = null)
        {
            var type = typeof(OpenAIProvider).Assembly.GetType(
                $"Orkeon.Infrastructure.LLMs.{providerTypeName}", throwOnError: true)!;

            // Prefer the tool-calling overload, but fall back to the resilience-policy one:
            // Ollama deliberately ships without a native tool-calling strategy (R10.7).
            var candidates = Array.FindAll(
                type.GetConstructors(),
                c => c.GetParameters() is [var cfg, var http, var third, var fourth]
                     && cfg.ParameterType == typeof(LlmConfig)
                     && http.ParameterType == typeof(IHttpClientFactory)
                     && (third.ParameterType == typeof(IToolCallingStrategy)
                         || third.ParameterType == typeof(IAsyncPolicy<HttpResponseMessage>))
                     && typeof(ILogger).IsAssignableFrom(fourth.ParameterType));

            var ctor = Array.Find(candidates, c => c.GetParameters()[2].ParameterType == typeof(IToolCallingStrategy))
                       ?? candidates.FirstOrDefault();
            Assert.NotNull(ctor);

            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];
            args[0] = BaseConfig();
            args[1] = factory ?? new TestHttpClientFactory();
            // The logger parameter is ILogger<TProvider>, so the sink is wrapped in a
            // closed generic built from the provider type itself.
            if (sink is not null && parameters.Length > 3)
                args[3] = Activator.CreateInstance(typeof(CollectingLogger<>).MakeGenericType(type), sink);

            return (ILlmProvider)ctor.Invoke(args);
        }

        internal static async Task<JsonElement> CapturePayloadAsync(
            string providerTypeName, Func<LlmConfig, LlmConfig> configure) =>
            (await CapturePayloadWithLogAsync(providerTypeName, configure)).Body;

        internal static async Task<Capture> CapturePayloadWithLogAsync(
            string providerTypeName, Func<LlmConfig, LlmConfig> configure)
        {
            using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkBody);
            var factory = new TestHttpClientFactory();
            factory.RegisterClient(providerTypeName, new HttpClient(handler));

            var sink = new WarningSink();
            var provider = Create(providerTypeName, factory, sink);
            try
            {
                await provider.GenerateAsync(
                    TestPrompt, configure(BaseConfig()), TestContext.Current.CancellationToken);
            }
            finally
            {
                (provider as IDisposable)?.Dispose();
            }

            var request = Assert.Single(handler.CapturedRequests);
            var raw = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
            using var doc = JsonDocument.Parse(raw);
            return new Capture(doc.RootElement.Clone(), sink.Warnings);
        }

        /// <summary>
        /// A configuration valid for every provider at once: Azure needs an explicit resource
        /// URL, Ollama ignores the key, and the others need a key.
        /// </summary>
        private static LlmConfig BaseConfig() =>
            LlmConfig.Create("test-model", TestApiKey) with
            {
                BaseUrl = new Uri("https://provider.test/v1"),
            };
    }

    /// <summary>Collects the warnings emitted during one captured call.</summary>
    private sealed class WarningSink
    {
        public List<string> Warnings { get; } = [];
    }

    /// <summary>
    /// Captures warning-level messages so "was it reported?" can be asserted. Generic because
    /// providers take an <c>ILogger&lt;TProvider&gt;</c>; the sink is shared and untyped.
    /// </summary>
    private sealed class CollectingLogger<T>(WarningSink sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (logLevel >= LogLevel.Warning)
                sink.Warnings.Add(formatter(state, exception));
        }
    }
}
