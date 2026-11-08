using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Base;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.ToolCallDeltas;

/// <summary>
/// FWK-20: no provider of the sixteen stays mute on tool-call deltas, and a fallback never lets the partial
/// deltas of a failed provider reach the caller (FWK-05).
/// </summary>
public sealed class ToolCallDeltaCoverageTests
{
    private static readonly LlmMessage[] Hello = [new() { Role = "user", Content = "hi" }];

    [Fact]
    [Trait("BR", "FWK-20")]
    public void ShouldCoverEverySixteenProvider_ThroughOneOfTheThreeStreamingFamilies()
    {
        var providers = typeof(HttpLlmProviderBase).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && t.IsSubclassOf(typeof(HttpLlmProviderBase)))
            .ToList();

        Assert.Equal(16, providers.Count);
        foreach (var provider in providers)
        {
            var family = provider.IsSubclassOf(typeof(OpenAICompatibleProviderBase))
                || provider == typeof(AnthropicLlmProvider)
                || provider == typeof(OllamaLlmProvider);
            Assert.True(family, $"{provider.Name} streams through none of the families that emit tool-call deltas.");
        }
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async Task ShouldNotLetAFailedProvidersDeltasReachTheCaller_WhenItFailsBeforeAnyEvent()
    {
        var a = new FakeScriptedLlmProvider { StreamThrowsBefore = new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable) };
        var b = new FakeScriptedLlmProvider
        {
            StreamEvents =
            [
                LlmStreamEvent.ToolCallFragment(new ToolCallDelta(0, "call_b", "tool_b", "{}")),
                LlmStreamEvent.Complete(new LlmResponse { Content = "" }),
            ],
        };

        var events = await Drain(Chain(a, b));

        Assert.Equal("call_b", events[0].ToolCall!.Id);
        Assert.Single(events, e => e.Kind == LlmStreamEventKind.ToolCallDelta);
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async Task ShouldCommitToTheFirstProvider_WhenItsFirstEventIsAToolCallDelta()
    {
        var failure = FakeScriptedLlmProvider.Http(HttpStatusCode.InternalServerError);
        var a = new FakeScriptedLlmProvider
        {
            StreamEvents =
            [
                LlmStreamEvent.ToolCallFragment(new ToolCallDelta(0, "call_a", "tool_a", "{\"x\"")),
                LlmStreamEvent.Complete(failure),
            ],
        };
        var b = new FakeScriptedLlmProvider
        {
            StreamEvents = [LlmStreamEvent.ToolCallFragment(new ToolCallDelta(0, "call_b", "tool_b", "{}")), LlmStreamEvent.Complete(new LlmResponse())],
        };

        var events = await Drain(Chain(a, b));

        Assert.Equal(0, b.Calls);
        Assert.Equal("call_a", events[0].ToolCall!.Id);
        Assert.Same(failure, events[^1].FinalResponse);
    }

    private static FallbackLlmProvider Chain(FakeScriptedLlmProvider a, FakeScriptedLlmProvider b) =>
        new("main", [new FallbackLink("a", a), new FallbackLink("b", b)], new FakeLlmFallbackObserver(), NullLogger.Instance, null, TimeProvider.System);

    private static async Task<List<LlmStreamEvent>> Drain(FallbackLlmProvider chain)
    {
        var events = new List<LlmStreamEvent>();
        await foreach (var ev in chain.ChatStreamingAsync(Hello, cancellationToken: TestContext.Current.CancellationToken))
            events.Add(ev);
        return events;
    }
}
