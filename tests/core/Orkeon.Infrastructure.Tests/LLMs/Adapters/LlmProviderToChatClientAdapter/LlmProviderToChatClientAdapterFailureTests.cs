using Microsoft.Extensions.AI;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

/// <summary>
/// LLM-11: the buffered path fails the way the streaming path already did when the provider
/// answered with a refusal. Mapping the refusal to an empty <see cref="ChatResponse"/> told the
/// agent loop "the model said nothing", which it diagnosed as a reasoning model out of budget
/// and retried once more without tools — for a second full timeout on the run of 2026-09-20.
/// </summary>
public class LlmProviderToChatClientAdapterFailureTests
{
    private const string TimeoutError = "Kimi did not answer within Llm:TimeoutSeconds = 180 s: the HTTP timeout elapsed";

    private static readonly ChatMessage[] OneUserMessage = [new(ChatRole.User, "hi")];

    [Fact]
    public async Task The_buffered_call_throws_the_providers_own_sentence_when_the_call_failed()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(new LlmResponse
        {
            Content = "",
            Metadata = new Dictionary<string, object>
            {
                [LlmResponseMetadataKeys.Error] = TimeoutError,
                [LlmResponseMetadataKeys.ErrorType] = nameof(TaskCanceledException),
            },
        });
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() =>
            adapter.GetResponseAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(TimeoutError, failure.Message);
        Assert.Equal(1, provider.ChatCallCount);
    }

    [Fact]
    public async Task An_empty_answer_without_a_refusal_is_still_an_empty_response()
    {
        // A model with nothing to say is not a failed call: the loop owns that case (the
        // tool-free retry), the adapter must not turn it into an exception.
        var provider = new MockLlmProvider();
        provider.SetChatResult(new LlmResponse { Content = "" });
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var response = await adapter.GetResponseAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("", response.Text);
    }

    [Fact]
    public async Task A_refusal_that_still_carries_text_is_returned_as_that_text()
    {
        // Some endpoints answer a partial text with an error note in the metadata: the text
        // is what the caller asked for, the note stays informational.
        var provider = new MockLlmProvider();
        provider.SetChatResult(new LlmResponse
        {
            Content = "partial answer",
            Metadata = new Dictionary<string, object> { [LlmResponseMetadataKeys.Error] = "truncated" },
        });
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var response = await adapter.GetResponseAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("partial answer", response.Text);
    }
}
