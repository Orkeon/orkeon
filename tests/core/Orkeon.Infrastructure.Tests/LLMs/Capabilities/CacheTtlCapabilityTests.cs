using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>RT-12: a provider that cannot honour a cache TTL says so, naming the option.</summary>
public class CacheTtlCapabilityTests
{
    [Fact]
    [Trait("BR", "RT-12")]
    public async Task ShouldWarnStructured_WhenProviderCannotHonourCacheTtl()
    {
        const string body = """{"choices":[{"message":{"content":"ok"}}],"usage":{"total_tokens":1,"prompt_tokens":1,"completion_tokens":0}}""";
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient(nameof(OpenAIProvider), new HttpClient(handler));
        var warnings = new List<(int Id, string Message)>();
        var config = LlmConfig.Create("test-model", TestApiKey) with { BaseUrl = new Uri("https://provider.test/v1") };
        using var provider = new OpenAIProvider(
            config, factory, Policy.NoOpAsync<HttpResponseMessage>(), new CollectingLogger<OpenAIProvider>(warnings));
        using var adapter = new LlmProviderToChatClientAdapter(provider, config);

        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [LlmChatOptionsKeys.CacheTtl] = "1h" },
        };
        await adapter.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")], options, TestContext.Current.CancellationToken);

        var warning = Assert.Single(warnings, w => w.Id == 110);
        Assert.Contains("'cache_ttl'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-12")]
    public async Task ShouldWarnStructured_WhenOllamaCannotHonourCacheTtl()
    {
        const string body = """{"response":"ok","message":{"role":"assistant","content":"ok"},"done":true}""";
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient(nameof(OllamaLlmProvider), new HttpClient(handler));
        var warnings = new List<(int Id, string Message)>();
        var config = LlmConfig.Create("test-model", TestApiKey) with { BaseUrl = new Uri("https://provider.test/v1") };
        using var provider = new OllamaLlmProvider(
            config, factory, Policy.NoOpAsync<HttpResponseMessage>(), new CollectingLogger<OllamaLlmProvider>(warnings));
        using var adapter = new LlmProviderToChatClientAdapter(provider, config);

        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [LlmChatOptionsKeys.CacheTtl] = "1h" },
        };
        await adapter.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")], options, TestContext.Current.CancellationToken);

        var warning = Assert.Single(warnings, w => w.Id == 110);
        Assert.Contains("'cache_ttl'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public async Task ShouldIgnoreTheHostDefaultCache_WhenTheProviderCachesImplicitly()
    {
        // Llm:Cache applies to explicit-caching providers only: an implicit one neither carries
        // it to its calls nor warns that its lifetime was not sent.
        const string body = """{"choices":[{"message":{"content":"ok"}}],"usage":{"total_tokens":1,"prompt_tokens":1,"completion_tokens":0}}""";
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient(nameof(OpenAIProvider), new HttpClient(handler));
        var warnings = new List<(int Id, string Message)>();
        var config = LlmConfig.Create("test-model", TestApiKey) with
        {
            BaseUrl = new Uri("https://provider.test/v1"),
            Cache = new LlmCacheConfig { CacheSystemPrompt = true, Ttl = "1h" },
        };
        using var provider = new OpenAIProvider(
            config, factory, Policy.NoOpAsync<HttpResponseMessage>(), new CollectingLogger<OpenAIProvider>(warnings));
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        await adapter.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")], new ChatOptions(), TestContext.Current.CancellationToken);
        await provider.GenerateAsync("hello", null, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(warnings, w => w.Id == 110);
        Assert.Null(provider.BaseConfig?.Cache);
    }

    private sealed class CollectingLogger<T>(List<(int Id, string Message)> warnings) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (logLevel >= LogLevel.Warning)
                warnings.Add((eventId.Id, formatter(state, exception)));
        }
    }
}
