using Microsoft.Extensions.AI;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IChatClient"/>: records the text of every message it is sent and
/// answers each call with <see cref="Answer"/>, so a runner test can read the prompt an agent
/// received without any LLM.
/// </summary>
public sealed class CapturingChatClient : IChatClient
{
    private readonly List<string> _prompts = [];
    private readonly Lock _lock = new();

    /// <summary>The fixed answer returned to every call.</summary>
    public string Answer { get; init; } = "Final answer.";

    /// <summary>Every message text received, in order.</summary>
    public IReadOnlyList<string> Prompts
    {
        get { lock (_lock) { return [.. _prompts]; } }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
            _prompts.AddRange(messages.Select(m => m.Text));
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(IChatClient) ? this : null;

    public void Dispose()
    {
        // Nothing to dispose.
    }
}
