using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

/// <summary>
/// The host default <c>Llm:Cache</c> reaches the chat-client adapter as its base configuration
/// (RunnerHost builds it with the raw settings); a provider that caches implicitly is neither
/// warned about a lifetime nor sent one.
/// </summary>
public sealed class LlmProviderToChatClientAdapterCacheTests
{
    [Fact]
    [Trait("BR", "R-44")]
    public async Task ShouldNotWarnCacheTtl_WhenRunnerHostAppliesLlmCacheToOpenAi()
    {
        using var factory = new MockHttpClientFactory();
        var handler = factory.SetupDefaultHandler();
        handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"},\"finish_reason\":null}]}\n\n"
                + "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"total_tokens\":2}}\n\n"
                + "data: [DONE]\n\n",
                System.Text.Encoding.UTF8,
                "text/event-stream")
        });
        var logger = new MockLogger<object>();
        var hostConfig = LlmConfig.Create("gpt-5.6-sol", TestApiKey)
            with { Cache = LlmCacheConfig.SystemPrompt() with { Ttl = "1h" } };
        using var loggers = new SharedLoggerFactory(logger);
        var providerFactory = new LlmProviderFactory(factory, loggers);
        var basic = providerFactory.Create(hostConfig);
        var wrapped = Assert.IsType<LlmProviderAdapter>(basic);
        using var adapter = new LlmProviderToChatClientAdapter(wrapped.UnderlyingProvider, hostConfig);

        await adapter.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { Temperature = 0.2f },
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain(logger.LogEntries, e => e.Message.Contains("cache_ttl", StringComparison.Ordinal));
        var body = await handler.LastRequest!.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("cache_ttl", body, StringComparison.Ordinal);
    }

    private sealed class SharedLoggerFactory(ILogger logger) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }
}
