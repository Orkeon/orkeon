using Microsoft.Extensions.AI;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

/// <summary>
/// D5-03: the adapter's streaming fallback (taken whenever the provider does not stream, which
/// is what an unconfigured provider now declares) must not hand M.E.AI an empty
/// <see cref="ChatResponseUpdate"/>. The provider's refusal travels in the response metadata,
/// which an update does not carry, so the enumeration fails instead of ending on silence.
/// </summary>
public class LlmProviderToChatClientAdapterStreamingTests
{
    private const string MissingKeyError = "OpenAI API key is required";

    private static readonly ChatMessage[] OneUserMessage = [new(ChatRole.User, "hi")];

    [Fact]
    public async Task Streaming_fallback_fails_when_the_buffered_answer_is_a_provider_error()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(new LlmResponse
        {
            Content = "",
            Metadata = new Dictionary<string, object> { ["error"] = MissingKeyError },
        });

        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var updates = new List<ChatResponseUpdate>();
        var failure = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var update in adapter.GetStreamingResponseAsync(
                               OneUserMessage, cancellationToken: TestContext.Current.CancellationToken))
                updates.Add(update);
        });

        Assert.Contains(MissingKeyError, failure.Message, StringComparison.Ordinal);
        Assert.Empty(updates);
    }

    [Fact]
    public async Task Streaming_fallback_still_yields_an_update_when_the_model_simply_said_nothing()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(new LlmResponse { Content = "" });

        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in adapter.GetStreamingResponseAsync(
                           OneUserMessage, cancellationToken: TestContext.Current.CancellationToken))
            updates.Add(update);

        Assert.Single(updates);
        Assert.Equal("", updates[0].Text);
    }
}
