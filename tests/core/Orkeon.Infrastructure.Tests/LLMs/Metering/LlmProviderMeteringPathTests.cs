using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// The decorated path (STUDIO-42 D-01): a provider reaches the runtime either through the
/// factory or through <see cref="LlmProviderRegistrationExtensions.AddOrkeonLlmProvider"/>,
/// and either way every surface the runtime consumes — <see cref="ILlmProvider"/>,
/// <see cref="IBasicLlmProvider"/>, <see cref="IChatClient"/> — calls through one meter.
/// </summary>
public sealed class LlmProviderMeteringPathTests
{
    private const string OpenAiAnswer =
        """{"id":"chatcmpl-1","object":"chat.completion","model":"gpt-4o-mini","choices":[{"index":0,"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":3,"total_tokens":15}}""";

#pragma warning disable CS0618 // ApiKey is the direct-from-config credential path the providers still read.
    private static LlmConfig OpenAiConfig() => LlmConfig.Create("gpt-4o-mini") with
    {
        ApiKey = "sk-test",
        BaseUrl = new Uri("https://api.openai.com/v1"),
    };
#pragma warning restore CS0618

    private static MockHttpClientFactory VendorAnswering(out MockHttpMessageHandler handler)
    {
        var http = new MockHttpClientFactory();
        handler = http.SetupDefaultHandler();
        handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(OpenAiAnswer, Encoding.UTF8, "application/json"),
        });
        return http;
    }

    [Fact]
    public async Task Every_surface_of_a_factory_built_provider_is_metered_for_a_listening_host()
    {
        using var http = VendorAnswering(out var handler);
        var sink = new MockLlmUsageSink();
        var factory = new LlmProviderFactory(http, NullLoggerFactory.Instance, sink);
        var ct = TestContext.Current.CancellationToken;

        var basic = Assert.IsType<LlmProviderAdapter>(factory.Create(OpenAiConfig()));
        await basic.ChatAsync("hello", cancellationToken: ct);
        await basic.UnderlyingProvider.GenerateAsync("hello", cancellationToken: ct);
        using var chat = new LlmProviderToChatClientAdapter(basic.UnderlyingProvider);
        await chat.GetResponseAsync("hello", cancellationToken: ct);

        Assert.Equal(3, handler.SendCallCount);
        Assert.Equal(handler.SendCallCount, sink.Recorded.Count);
        Assert.All(sink.Recorded, usage =>
        {
            Assert.Equal(12, usage.PromptTokens);
            Assert.Equal(3, usage.CompletionTokens);
            Assert.False(usage.Estimated);
        });
        Assert.IsType<OpenAIProvider>(MeteredLlmProvider.Unwrap(basic.UnderlyingProvider));
    }

    [Fact]
    public void A_factory_no_host_listens_to_builds_its_providers_bare()
    {
        using var http = VendorAnswering(out _);
        var factory = new LlmProviderFactory(http, NullLoggerFactory.Instance);

        var basic = Assert.IsType<LlmProviderAdapter>(factory.Create(OpenAiConfig()));

        Assert.IsType<OpenAIProvider>(basic.UnderlyingProvider);
    }

    [Fact]
    public void The_container_hands_the_factory_the_sink_the_host_registered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<ILlmUsageSink>(new MockLlmUsageSink());
        services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();
        using var container = services.BuildServiceProvider();

        var basic = Assert.IsType<LlmProviderAdapter>(
            container.GetRequiredService<ILlmProviderFactory>().Create(OpenAiConfig()));

        Assert.IsType<MeteredLlmProvider>(basic.UnderlyingProvider);
    }

    [Fact]
    public async Task A_provider_registered_by_hand_is_metered_on_all_three_surfaces()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(new LlmResponse { Content = "hi", PromptTokens = 7, CompletionTokens = 2, TokensUsed = 9 });
        var sink = new MockLlmUsageSink();
        var services = new ServiceCollection();
        services.AddSingleton<ILlmUsageSink>(sink);
        services.AddOrkeonLlmProvider(_ => provider);
        using var container = services.BuildServiceProvider();
        var ct = TestContext.Current.CancellationToken;

        await container.GetRequiredService<ILlmProvider>().ChatAsync([LlmMessage.User("hi")], cancellationToken: ct);
        await container.GetRequiredService<IBasicLlmProvider>().ChatAsync("hi", cancellationToken: ct);
        await container.GetRequiredService<IChatClient>().GetResponseAsync("hi", cancellationToken: ct);

        Assert.Equal(3, provider.ChatCallCount);
        Assert.Equal(provider.ChatCallCount, sink.Recorded.Count);
        Assert.Equal(3 * 9, sink.TotalTokens);
    }
}
