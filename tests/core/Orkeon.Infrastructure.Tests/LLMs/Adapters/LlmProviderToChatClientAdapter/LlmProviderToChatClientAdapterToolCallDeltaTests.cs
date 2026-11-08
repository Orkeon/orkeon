using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

/// <summary>
/// FWK-20: the tool-call fragments a provider streams reach the chat client's reader as updates that carry the
/// fragment and no content, so the fold of the turn is the one it was before.
/// </summary>
public sealed class LlmProviderToChatClientAdapterToolCallDeltaTests
{
    private static readonly ChatMessage[] OneUserMessage = [new(ChatRole.User, "hi")];

    private static async IAsyncEnumerable<LlmStreamEvent> Events([EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return LlmStreamEvent.Content("Hel");
        yield return LlmStreamEvent.ToolCallFragment(new ToolCallDelta(0, "call_1", "tool_x", ""));
        yield return LlmStreamEvent.ToolCallFragment(new ToolCallDelta(0, null, null, "{\"a\":1}"));
        yield return LlmStreamEvent.Content("lo");
        yield return LlmStreamEvent.Complete(new LlmResponse { Content = "Hello" });
    }

    private static async Task<List<ChatResponseUpdate>> ReadAsync()
    {
        var provider = new MockStreamingLlmProvider { SupportsStreaming = true };
        provider.SetChatStreamingHandler((_, _, ct) => Events(ct));
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in adapter.GetStreamingResponseAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken))
            updates.Add(update);
        return updates;
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async Task ShouldYieldOneUpdatePerFragment_CarryingTheFragmentAndNoContent()
    {
        var updates = await ReadAsync();

        var fragments = updates.Where(u => u.RawRepresentation is ToolCallDelta).ToList();
        Assert.Equal(2, fragments.Count);
        Assert.All(fragments, u => Assert.Empty(u.Contents));
        Assert.Equal("call_1", ((ToolCallDelta)fragments[0].RawRepresentation!).Id);
        Assert.Equal("{\"a\":1}", ((ToolCallDelta)fragments[1].RawRepresentation!).ArgumentsDelta);
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async Task ShouldFoldToTheSameTurn_WhateverTheFragments()
    {
        var updates = await ReadAsync();

        var response = updates.ToChatResponse();

        Assert.Equal("Hello", response.Text);
        Assert.Single(response.Messages);
    }
}
