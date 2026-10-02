using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.ToolCalling;
using Polly;
using TestableOpenAICompatibleProvider = Orkeon.Infrastructure.Tests.LLMs.Base.TestableOpenAICompatibleProvider;
using TestHttpClientFactory = Orkeon.Infrastructure.Tests.TestDoubles.TestHttpClientFactory;
using TestHttpMessageHandler = Orkeon.Infrastructure.Tests.TestDoubles.TestHttpMessageHandler;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// GAP-18: a configuration that names no model (<see cref="LlmConfig.OnProfile"/>, an empty
/// <see cref="LlmConfig.Model"/>) runs on the model of the provider it is sent to — the model
/// that provider was configured with, else its own default — and never on an empty one. A call
/// configuration replaces the provider's, and the providers fell back with <c>??</c>, which an
/// empty string defeats: such a call went out with <c>"model": ""</c>.
/// </summary>
public sealed class UnsetModelResolutionTests
{
    private const string Key = "sk-test";

    private const string OpenAiShapedAnswer =
        """{"choices":[{"message":{"role":"assistant","content":"ok"}}],"usage":{"total_tokens":2,"prompt_tokens":1,"completion_tokens":1}}""";

    private const string AnthropicAnswer =
        """{"content":[{"type":"text","text":"ok"}],"usage":{"input_tokens":1,"output_tokens":1}}""";

    private static LlmConfig NoModel() => LlmConfig.OnProfile() with { ApiKey = Key };

    private static LlmMessage[] Hello => [LlmMessage.User("hello")];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (TestHttpMessageHandler Handler, TestHttpClientFactory Factory) Wire(string clientName, string answer)
    {
        var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, answer);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient(clientName, new HttpClient(handler));
        return (handler, factory);
    }

    private static async Task<string?> SentModelAsync(TestHttpMessageHandler handler)
    {
        var body = await handler.CapturedRequests.Single().Content!.ReadAsStringAsync(Ct);
        using var payload = JsonDocument.Parse(body);
        return payload.RootElement.GetProperty("model").GetString();
    }

    // ── The OpenAI-compatible family ────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_call_naming_no_model_runs_on_the_model_the_provider_is_configured_with(bool chat)
    {
        var (handler, factory) = Wire(nameof(TestableOpenAICompatibleProvider), OpenAiShapedAnswer);
        using (handler)
        using (var provider = new TestableOpenAICompatibleProvider(
            LlmConfig.Create("m-config", Key), factory, Policy.NoOpAsync<HttpResponseMessage>()))
        {
            var response = chat
                ? await provider.ChatAsync(Hello, NoModel(), Ct)
                : await provider.GenerateAsync("hello", NoModel(), Ct);

            Assert.Equal("m-config", await SentModelAsync(handler));
            // The model the meter records is the one the request named.
            Assert.Equal("m-config", response.Model);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_provider_configured_with_no_model_sends_its_own_default(bool chat)
    {
        var (handler, factory) = Wire(nameof(TestableOpenAICompatibleProvider), OpenAiShapedAnswer);
        using (handler)
        using (var provider = new TestableOpenAICompatibleProvider(NoModel(), factory, Policy.NoOpAsync<HttpResponseMessage>()))
        {
            var response = chat
                ? await provider.ChatAsync(Hello, null, Ct)
                : await provider.GenerateAsync("hello", NoModel() with { Model = "   " }, Ct);

            Assert.Equal(TestModelName, await SentModelAsync(handler));
            Assert.Equal(TestModelName, response.Model);
        }
    }

    [Fact]
    public async Task A_call_naming_a_model_keeps_it()
    {
        var (handler, factory) = Wire(nameof(TestableOpenAICompatibleProvider), OpenAiShapedAnswer);
        using (handler)
        using (var provider = new TestableOpenAICompatibleProvider(
            LlmConfig.Create("m-config", Key), factory, Policy.NoOpAsync<HttpResponseMessage>()))
        {
            await provider.ChatAsync(Hello, LlmConfig.Create("m-call", Key), Ct);

            Assert.Equal("m-call", await SentModelAsync(handler));
        }
    }

    [Fact]
    public async Task A_DeepSeek_provider_with_no_model_sends_DeepSeeks_default_never_OpenAIs()
    {
        var (handler, factory) = Wire(nameof(DeepSeekLlmProvider), OpenAiShapedAnswer);
        using (handler)
        using (var provider = new DeepSeekLlmProvider(NoModel(), factory, Policy.NoOpAsync<HttpResponseMessage>()))
        {
            await provider.ChatAsync(Hello, NoModel(), Ct);

            Assert.Equal(LlmProviderDefaultModels.DeepSeek, await SentModelAsync(handler));
        }
    }

    // ── The three providers outside that family ─────────────────────────────

    [Fact]
    public async Task Anthropic_runs_a_call_naming_no_model_on_its_configured_model_then_its_default()
    {
        var (configured, factory) = Wire(nameof(AnthropicLlmProvider), AnthropicAnswer);
        using (configured)
        using (var provider = new AnthropicLlmProvider(
            LlmConfig.Create("claude-opus-5", Key), factory, Policy.NoOpAsync<HttpResponseMessage>()))
        {
            var response = await provider.ChatAsync(Hello, NoModel(), Ct);

            Assert.Equal("claude-opus-5", await SentModelAsync(configured));
            Assert.Equal("claude-opus-5", response.Model);
        }

        var (bare, bareFactory) = Wire(nameof(AnthropicLlmProvider), AnthropicAnswer);
        using (bare)
        using (var provider = new AnthropicLlmProvider(NoModel(), bareFactory, Policy.NoOpAsync<HttpResponseMessage>()))
        {
            var response = await provider.GenerateAsync("hello", null, Ct);

            Assert.Equal(LlmProviderDefaultModels.Anthropic, await SentModelAsync(bare));
            Assert.Equal(LlmProviderDefaultModels.Anthropic, response.Model);
        }
    }

    [Fact]
    public async Task Azure_runs_a_call_naming_no_model_on_its_configured_deployment()
    {
        var endpoint = new Uri("https://my-resource.openai.azure.com");
        var (handler, factory) = Wire(nameof(AzureOpenAILlmProvider), OpenAiShapedAnswer);
        using (handler)
        using (var provider = new AzureOpenAILlmProvider(
            LlmConfig.Create("my-deployment", Key) with { BaseUrl = endpoint }, factory, Policy.NoOpAsync<HttpResponseMessage>()))
        {
            await provider.GenerateAsync("hello", NoModel() with { BaseUrl = endpoint }, Ct);

            Assert.Equal(
                "/openai/deployments/my-deployment/chat/completions",
                handler.CapturedRequests.Single().RequestUri!.AbsolutePath);
        }
    }

    [Fact]
    public async Task Ollama_runs_a_call_naming_no_model_on_its_configured_model_on_both_endpoints()
    {
        // A plain conversation takes /api/generate; one that declares tools takes /api/chat.
        var (generate, generateFactory) = Wire(nameof(OllamaLlmProvider), """{"response":"hi","done":true}""");
        using (generate)
        using (var provider = new OllamaLlmProvider(LlmConfig.Create("qwen3"), generateFactory))
        {
            var response = await provider.ChatAsync(Hello, LlmConfig.OnProfile(), Ct);

            Assert.EndsWith("/api/generate", generate.CapturedRequests.Single().RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("qwen3", await SentModelAsync(generate));
            Assert.Equal("qwen3", response.Model);
        }

        var (chat, chatFactory) = Wire(nameof(OllamaLlmProvider), """{"message":{"role":"assistant","content":"hi"},"done":true}""");
        using (chat)
        using (var provider = new OllamaLlmProvider(
            LlmConfig.Create("qwen3"), chatFactory,
            new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance)))
        {
            var tools = LlmConfig.OnProfile() with { Tools = [new ToolSchema("get_weather", "Get the weather", [])] };
            var response = await provider.ChatAsync(Hello, tools, Ct);

            Assert.EndsWith("/api/chat", chat.CapturedRequests.Single().RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("qwen3", await SentModelAsync(chat));
            Assert.Equal("qwen3", response.Model);
        }
    }

    [Fact]
    public async Task An_Ollama_provider_with_no_model_sends_the_documented_default_not_llama2()
    {
        var (handler, factory) = Wire(nameof(OllamaLlmProvider), """{"response":"hi","done":true}""");
        using (handler)
        using (var provider = new OllamaLlmProvider(LlmConfig.OnProfile(), factory))
        {
            var response = await provider.GenerateAsync("hello", null, Ct);

            Assert.Equal(LlmProviderDefaultModels.Ollama, await SentModelAsync(handler));
            Assert.Equal(LlmProviderDefaultModels.Ollama, response.Model);
        }
    }
}
