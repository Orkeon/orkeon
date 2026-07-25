using Microsoft.Extensions.AI;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IChatClient"/>: returns
/// <see cref="ResponseText"/> and records the messages and options received.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    /// <summary>Text of the assistant response returned by <see cref="GetResponseAsync"/>.</summary>
    public string ResponseText { get; set; } = "fake answer";

    /// <summary>Optional model id stamped on the response.</summary>
    public string? ResponseModelId { get; set; }

    /// <summary>Number of <see cref="GetResponseAsync"/> calls.</summary>
    public int CallCount { get; private set; }

    /// <summary>Messages received by the last call.</summary>
    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    /// <summary>Options received by the last call.</summary>
    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastMessages = messages.ToList();
        LastOptions = options;

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, ResponseText))
        {
            ModelId = ResponseModelId,
        };
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(IChatClient) ? this : null;

    public void Dispose()
    {
        // Nothing to dispose.
    }
}
