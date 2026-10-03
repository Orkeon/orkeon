using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Base;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// GAP-36: what a configuration sets reaches the wire, whatever its value, and what it leaves
/// unset does not — decision 1 for the temperature and <c>top_p</c>, on every payload builder of
/// every dialect. And what a dialect cannot write is said, with the structured warning
/// (EventId 110) — decision 8, for the penalties and the seed; Ollama now writes <c>top_p</c>,
/// <c>seed</c> and <c>stop</c> in its <c>options</c>.
/// </summary>
/// <remarks>
/// The engine took 0.7 and 1.0 for "not set": a temperature of 0.7 or a <c>top_p</c> of 1.0 never
/// reached the model, and a temperature nobody set went out as 0.7 — which the default models of
/// OpenAI (<c>gpt-5.6-sol</c>) and Anthropic (<c>claude-sonnet-5</c>) refuse.
/// </remarks>
public class SamplingPayloadTests
{
    /// <summary>The ways a request leaves a provider; each one builds its payload somewhere.</summary>
    public enum WirePath
    {
        /// <summary><c>GenerateAsync</c>: one prompt.</summary>
        Generate,
        /// <summary><c>ChatAsync</c> with a conversation.</summary>
        Chat,
        /// <summary><c>ChatAsync</c> with a tool declared: Ollama's <c>/api/chat</c>.</summary>
        ToolChat,
        /// <summary><c>GenerateStreamingAsync</c>.</summary>
        GenerateStream,
        /// <summary><c>ChatStreamingAsync</c>.</summary>
        ChatStream,
    }

    /// <summary>Every provider, by the type name its HTTP client is registered under.</summary>
    public static TheoryData<string> AllProviders() =>
    [
        nameof(OpenAIProvider), nameof(AzureOpenAILlmProvider),
        nameof(TogetherAiLlmProvider), nameof(MistralLlmProvider), nameof(KimiLlmProvider),
        nameof(QwenLlmProvider), nameof(HuggingFaceLlmProvider), nameof(ZaiLlmProvider),
        nameof(DeepSeekLlmProvider), nameof(AnthropicLlmProvider), nameof(OllamaLlmProvider),
        nameof(GrokLlmProvider), nameof(MiniMaxLlmProvider), nameof(GeminiLlmProvider),
        nameof(OpenRouterLlmProvider), nameof(MammouthLlmProvider),
    ];

    /// <summary>Every payload builder: the two of the OpenAI dialect, Anthropic's one, Ollama's three, Azure's stream.</summary>
    public static TheoryData<string, WirePath> EveryBuilder() => new()
    {
        { nameof(OpenAIProvider), WirePath.Generate },
        { nameof(OpenAIProvider), WirePath.Chat },
        { nameof(OpenAIProvider), WirePath.GenerateStream },
        { nameof(OpenAIProvider), WirePath.ChatStream },
        { nameof(DeepSeekLlmProvider), WirePath.Chat },
        { nameof(AnthropicLlmProvider), WirePath.Generate },
        { nameof(AnthropicLlmProvider), WirePath.Chat },
        { nameof(OllamaLlmProvider), WirePath.Generate },
        { nameof(OllamaLlmProvider), WirePath.ToolChat },
        { nameof(OllamaLlmProvider), WirePath.GenerateStream },
        { nameof(AzureOpenAILlmProvider), WirePath.GenerateStream },
    };

    // ── Decision 1: unset is not sent, set is sent ──────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllProviders))]
    public async Task A_temperature_nothing_sets_is_not_sent(string providerTypeName)
    {
        var probe = await Probe.CaptureAsync(providerTypeName, WirePath.Generate, config => config);

        Assert.False(probe.Sampling.TryGetProperty("temperature", out _), "no temperature may reach the wire");
    }

    [Theory]
    [MemberData(nameof(EveryBuilder))]
    public async Task Neither_a_temperature_nor_a_top_p_nothing_sets_is_sent_on_any_builder(string providerTypeName, WirePath path)
    {
        var probe = await Probe.CaptureAsync(providerTypeName, path, config => config);

        Assert.False(probe.Sampling.TryGetProperty("temperature", out _), "no temperature may reach the wire");
        Assert.False(probe.Sampling.TryGetProperty("top_p", out _), "no top_p may reach the wire");
    }

    [Theory]
    [MemberData(nameof(EveryBuilder))]
    public async Task A_temperature_and_a_top_p_that_are_set_are_sent_whatever_their_value(string providerTypeName, WirePath path)
    {
        var probe = await Probe.CaptureAsync(providerTypeName, path, config => config with { Temperature = 0.7, TopP = 1.0 });

        Assert.Equal(0.7, probe.Sampling.GetProperty("temperature").GetDouble(), precision: 3);
        Assert.Equal(1.0, probe.Sampling.GetProperty("top_p").GetDouble(), precision: 3);
    }

    /// <summary>
    /// The one exception, measured: Mistral validates greedy sampling against an explicit
    /// <c>top_p</c> (<c>"top_p must be 1 when using greedy sampling."</c>, 2026-08-30), so its
    /// dialect writes <c>top_p: 1</c> when nothing sets one.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllProviders))]
    public async Task A_top_p_nothing_sets_is_not_sent_but_Mistral_writes_one(string providerTypeName)
    {
        var probe = await Probe.CaptureAsync(providerTypeName, WirePath.Generate, config => config);

        if (providerTypeName == nameof(MistralLlmProvider))
            Assert.Equal(1.0, probe.Sampling.GetProperty("top_p").GetDouble(), precision: 3);
        else
            Assert.False(probe.Sampling.TryGetProperty("top_p", out _), "no top_p may reach the wire");
    }

    // ── Decision 8: what a dialect can write is written, the rest is said ──────────────────────

    [Theory]
    [InlineData(WirePath.Generate)]
    [InlineData(WirePath.ToolChat)]
    [InlineData(WirePath.GenerateStream)]
    public async Task Ollama_writes_top_p_seed_and_stop_in_its_options(WirePath path)
    {
        var probe = await Probe.CaptureAsync(nameof(OllamaLlmProvider), path, config => config with
        {
            TopP = 0.9,
            Seed = 7,
            StopSequences = ["END"],
        });

        var options = probe.Sampling;
        Assert.Equal(0.9, options.GetProperty("top_p").GetDouble(), precision: 3);
        Assert.Equal(7, options.GetProperty("seed").GetInt32());
        Assert.Equal(["END"], options.GetProperty("stop").EnumerateArray().Select(s => s.GetString()));
        Assert.DoesNotContain(probe.Warnings, w => w.EventId == 110);
    }

    [Theory]
    [InlineData(nameof(OpenAIProvider), "frequency_penalty")]
    [InlineData(nameof(OpenAIProvider), "presence_penalty")]
    [InlineData(nameof(OpenAIProvider), "seed")]
    [InlineData(nameof(DeepSeekLlmProvider), "seed")]
    [InlineData(nameof(AnthropicLlmProvider), "frequency_penalty")]
    [InlineData(nameof(AnthropicLlmProvider), "presence_penalty")]
    [InlineData(nameof(AnthropicLlmProvider), "seed")]
    [InlineData(nameof(OllamaLlmProvider), "frequency_penalty")]
    [InlineData(nameof(OllamaLlmProvider), "presence_penalty")]
    public async Task An_option_the_dialect_cannot_write_is_a_structured_warning_naming_it(string providerTypeName, string option)
    {
        var probe = await Probe.CaptureAsync(providerTypeName, WirePath.Chat, config => option switch
        {
            "frequency_penalty" => config with { FrequencyPenalty = 0.5 },
            "presence_penalty" => config with { PresencePenalty = 0.5 },
            _ => config with { Seed = 7 },
        });

        Assert.False(probe.Sampling.TryGetProperty(option, out _), $"`{option}` is not part of this dialect");
        var warning = Assert.Single(probe.Warnings, w => w.EventId == 110);
        Assert.Contains($"'{option}'", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(OpenAIProvider))]
    [InlineData(nameof(AnthropicLlmProvider))]
    [InlineData(nameof(OllamaLlmProvider))]
    public async Task Penalties_and_a_seed_nothing_sets_say_nothing(string providerTypeName)
    {
        var probe = await Probe.CaptureAsync(providerTypeName, WirePath.Chat, config => config);

        Assert.DoesNotContain(probe.Warnings, w => w.EventId == 110);
    }

    /// <summary>Test harness: builds a provider, sends one request on a path, captures its body and warnings.</summary>
    private static class Probe
    {
        internal sealed record Warning(int EventId, string Message);

        /// <summary>The request body, the object its sampling settings sit in, and the warnings.</summary>
        internal sealed record Capture(JsonElement Body, JsonElement Sampling, IReadOnlyList<Warning> Warnings);

        private static readonly string OkBody = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "ok" } } },
            content = new[] { new { type = "text", text = "ok" } },     // Anthropic shape
            response = "ok",                                            // Ollama /api/generate shape
            message = new { role = "assistant", content = "ok" },       // Ollama /api/chat shape
            done = true,
            usage = new { total_tokens = 1, prompt_tokens = 1, completion_tokens = 0, input_tokens = 1, output_tokens = 0 },
        });

        private const string SseBody =
            "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n";

        private const string NdjsonBody =
            "{\"response\":\"ok\",\"done\":false}\n{\"response\":\"\",\"done\":true}\n";

        internal static async Task<Capture> CaptureAsync(
            string providerTypeName, WirePath path, Func<LlmConfig, LlmConfig> configure)
        {
            var streamed = path is WirePath.GenerateStream or WirePath.ChatStream;
            var body = !streamed ? OkBody : providerTypeName == nameof(OllamaLlmProvider) ? NdjsonBody : SseBody;
            using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
            using var client = new HttpClient(handler);
            var factory = new TestHttpClientFactory();
            factory.RegisterClient(providerTypeName, client);

            var warnings = new List<Warning>();
            var provider = Create(providerTypeName, factory, warnings);
            try
            {
                await SendAsync(provider, path, configure(BaseConfig()));
            }
            finally
            {
                (provider as IDisposable)?.Dispose();
            }

            var request = Assert.Single(handler.CapturedRequests);
            var raw = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement.Clone();
            // Ollama writes its sampling settings in `options`, the others at the root.
            var sampling = providerTypeName == nameof(OllamaLlmProvider) ? root.GetProperty("options") : root;
            return new Capture(root, sampling, warnings);
        }

        private static async Task SendAsync(HttpLlmProviderBase provider, WirePath path, LlmConfig config)
        {
            var ct = TestContext.Current.CancellationToken;
            LlmMessage[] conversation = [LlmMessage.System("You answer briefly."), LlmMessage.User(TestPrompt)];
            switch (path)
            {
                case WirePath.Generate:
                    await provider.GenerateAsync(TestPrompt, config, ct);
                    break;
                case WirePath.Chat:
                    await provider.ChatAsync(conversation, config, ct);
                    break;
                case WirePath.ToolChat:
                    await provider.ChatAsync(conversation, config with
                    {
                        Tools = [new ToolSchema("lookup", "Looks a word up", [])],
                    }, ct);
                    break;
                case WirePath.GenerateStream:
                    await foreach (var _ in provider.GenerateStreamingAsync(TestPrompt, config, ct))
                    {
                        // Drained: the request is what the test reads.
                    }
                    break;
                case WirePath.ChatStream:
                    await foreach (var _ in provider.ChatStreamingAsync(conversation, config, ct))
                    {
                        // Drained: the request is what the test reads.
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(path), path, null);
            }
        }

        private static HttpLlmProviderBase Create(string providerTypeName, IHttpClientFactory factory, List<Warning> warnings)
        {
            var type = typeof(OpenAIProvider).Assembly.GetType(
                $"Orkeon.Infrastructure.LLMs.{providerTypeName}", throwOnError: true)!;

            // The resilience-policy overload: every provider has one, with no retry.
            var ctor = Array.Find(
                type.GetConstructors(),
                c => c.GetParameters() is [var cfg, var http, var policy, var logger]
                     && cfg.ParameterType == typeof(LlmConfig)
                     && http.ParameterType == typeof(IHttpClientFactory)
                     && policy.ParameterType == typeof(IAsyncPolicy<HttpResponseMessage>)
                     && typeof(ILogger).IsAssignableFrom(logger.ParameterType));
            Assert.NotNull(ctor);

            var logger = Activator.CreateInstance(typeof(CollectingLogger<>).MakeGenericType(type), warnings);
            return (HttpLlmProviderBase)ctor.Invoke([BaseConfig(), factory, Policy.NoOpAsync<HttpResponseMessage>(), logger]);
        }

        /// <summary>
        /// A configuration valid for every provider that sets no sampling at all: Azure needs an
        /// explicit resource URL, Ollama ignores the key, and the others need a key.
        /// </summary>
        private static LlmConfig BaseConfig() =>
            LlmConfig.Create("test-model", TestApiKey) with
            {
                BaseUrl = new Uri("https://provider.test/v1"),
            };
    }

    /// <summary>Records the warnings of one call, with their event ids.</summary>
    private sealed class CollectingLogger<T>(List<Probe.Warning> warnings) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (logLevel >= LogLevel.Warning)
                warnings.Add(new Probe.Warning(eventId.Id, formatter(state, exception)));
        }
    }
}
