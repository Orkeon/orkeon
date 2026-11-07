using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Telemetry;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Telemetry;

namespace Orkeon.Infrastructure.Tests.LLMs.Fallback;

/// <summary>FWK-05: the meter and the trace name the provider that answered, not the profile the crew asked for.</summary>
public sealed class FallbackCostAttributionTests
{
    private static ServiceProvider Host(MockLlmUsageSink sink)
    {
        var a = new FakeScriptedLlmProvider { Name = "vendor-a", Response = FakeScriptedLlmProvider.Http(System.Net.HttpStatusCode.ServiceUnavailable) };
        var b = new FakeScriptedLlmProvider
        {
            Name = "vendor-b",
            Response = new LlmResponse { Content = "from-b", PromptTokens = 7, CompletionTokens = 3, TokensUsed = 10, Model = "model-b" },
        };
        var services = new ServiceCollection();
        services.AddSingleton<ILlmUsageSink>(sink);
        services.AddOrkeonLlmProfile("a", _ => a, LlmConfig.Create("model-a"), ["b"]);
        services.AddOrkeonLlmProfile("b", _ => b, LlmConfig.Create("model-b"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task The_meter_records_the_tokens_of_the_provider_that_answered()
    {
        var sink = new MockLlmUsageSink();
        await using var container = Host(sink);
        var profile = container.GetRequiredService<ILlmProfileRegistry>().Resolve("a");

        var answer = await profile.BasicProvider.ChatAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("from-b", answer);
        var usage = Assert.Single(sink.Recorded, e => e.PromptTokens + e.CompletionTokens > 0);
        Assert.Equal("vendor-b", usage.Provider);
        Assert.Equal(10, sink.TotalTokens);
    }

    [Fact]
    public async Task The_trace_holds_one_chat_span_per_provider_reached_each_named_for_its_own_provider()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = new Activity("fwk05.root");
        root.Start();
        await using var container = Host(new MockLlmUsageSink());
        var profile = container.GetRequiredService<ILlmProfileRegistry>().Resolve("a");

        await profile.BasicProvider.ChatAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        var chats = recorder.Snapshot(root.TraceId).Where(s => s.GetTagItem("gen_ai.operation.name") is "chat").ToList();
        Assert.Equal(2, chats.Count);
        var providers = chats.Select(c => (string)c.GetTagItem("gen_ai.provider.name")!).Order().ToList();
        Assert.Equal(["vendor-a", "vendor-b"], providers);
    }
}
