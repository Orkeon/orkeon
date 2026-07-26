using Microsoft.Extensions.AI;

namespace Orkeon.Rag.Tests.QueryTransform;

/// <summary>
/// Hand-written <see cref="IChatClient"/> double that throws
/// <see cref="ExceptionToThrow"/> on every call — simulates a failing LLM
/// provider for the transformer fallback tests.
/// </summary>
internal sealed class ThrowingChatClient : IChatClient
{
    /// <summary>Exception thrown by every call (default: <see cref="HttpRequestException"/>).</summary>
    public Exception ExceptionToThrow { get; set; } = new HttpRequestException("provider down");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromException<ChatResponse>(ExceptionToThrow);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw ExceptionToThrow;

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // Nothing to dispose.
    }
}
