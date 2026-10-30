using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>RT-12: the per-call cache TTL reaches Anthropic's <c>cache_control</c> block.</summary>
public class AnthropicLlmProviderCacheTests
{
    private const string OkBody = """{"content":[{"type":"text","text":"ok"}],"usage":{"input_tokens":1,"output_tokens":1}}""";

    private static async Task<JsonElement> SystemBlockAsync(string? callTtl, string? configTtl)
        => (await SystemAsync(callTtl, new LlmCacheConfig { CacheSystemPrompt = true, Ttl = configTtl }))
            .EnumerateArray().Single().GetProperty("cache_control").Clone();

    private static async Task<JsonElement> SystemAsync(string? callTtl, LlmCacheConfig? cache)
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkBody);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("AnthropicLlmProvider", new HttpClient(handler));
        var config = LlmConfig.Create("claude-sonnet-5", TestApiKey) with
        {
            SystemMessage = "You are a careful assistant.",
            Cache = cache,
        };
        using var provider = new AnthropicLlmProvider(
            config, factory, Policy.NoOpAsync<HttpResponseMessage>(), new TestLogger<AnthropicLlmProvider>());
        using var adapter = new LlmProviderToChatClientAdapter(provider, config);

        var options = new ChatOptions { AdditionalProperties = new AdditionalPropertiesDictionary() };
        if (callTtl is not null)
            options.AdditionalProperties[LlmChatOptionsKeys.CacheTtl] = callTtl;
        await adapter.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")], options, TestContext.Current.CancellationToken);

        var raw = await handler.CapturedRequests.Single().Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.GetProperty("system").Clone();
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public async Task ShouldMarkTheSystemPromptWithTheCallTtl_WhenNoCacheIsConfigured()
    {
        var perCallOnly = await SystemAsync(callTtl: "1h", cache: null);
        var neither = await SystemAsync(callTtl: null, cache: null);

        var block = Assert.Single(perCallOnly.EnumerateArray());
        Assert.Equal("1h", block.GetProperty("cache_control").GetProperty("ttl").GetString());
        // No breakpoint asked for: the system prompt stays the plain string, nothing is cached.
        Assert.Equal(JsonValueKind.String, neither.ValueKind);
    }

    [Fact]
    [Trait("BR", "RT-12")]
    public async Task ShouldEmitOneHourTtl_WhenCallOptionSelectsLongTtl()
    {
        var perCall = await SystemBlockAsync(callTtl: "1h", configTtl: null);
        var overConfig = await SystemBlockAsync(callTtl: "1h", configTtl: "5m");
        var none = await SystemBlockAsync(callTtl: null, configTtl: null);
        var fromConfig = await SystemBlockAsync(callTtl: null, configTtl: "5m");

        Assert.Equal("1h", perCall.GetProperty("ttl").GetString());
        Assert.Equal("1h", overConfig.GetProperty("ttl").GetString());
        Assert.False(none.TryGetProperty("ttl", out _));
        Assert.Equal("5m", fromConfig.GetProperty("ttl").GetString());
    }
}
