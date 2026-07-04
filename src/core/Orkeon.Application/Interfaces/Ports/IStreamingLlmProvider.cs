using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Interface for LLM providers that support streaming responses.
/// Enables real-time token generation for better user experience and performance.
/// </summary>
public interface IStreamingLlmProvider
{
    /// <summary>
    /// Generates a streaming response from the LLM model.
    /// Returns tokens as they are generated for real-time display.
    /// </summary>
    /// <param name="prompt">The input prompt</param>
    /// <param name="config">Optional LLM configuration override</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of response tokens/chunks</returns>
    IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a multi-message chat completion (exp07 F5 L2). Yields content/reasoning
    /// deltas as they arrive and terminates with exactly one
    /// <see cref="LlmStreamEventKind.Completed"/> event whose
    /// <see cref="LlmStreamEvent.FinalResponse"/> is equivalent to the non-streaming
    /// <c>ChatAsync</c> result (content, usage, synthesized tool-calls body). Providers
    /// without a native SSE chat path fall back to a buffered single-delta stream.
    /// </summary>
    /// <param name="messages">The conversation messages.</param>
    /// <param name="config">Optional LLM configuration override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if streaming is supported and enabled for this provider.
    /// </summary>
    bool SupportsStreaming { get; }
}
