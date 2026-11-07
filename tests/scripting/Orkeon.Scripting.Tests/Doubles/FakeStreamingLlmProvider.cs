using System.Runtime.CompilerServices;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// An <see cref="ILlmProvider"/> that also streams, in the chunks it is given: what a configured
/// provider looks like behind <c>TestOverrideLlmProvider</c>.
/// </summary>
public sealed class FakeStreamingLlmProvider(params string[] chunks) : ILlmProvider, IStreamingLlmProvider
{
    /// <summary>Streamed calls received, text and chat together.</summary>
    public int StreamingCalls { get; private set; }

    /// <inheritdoc />
    public string Name => "fake-streaming";

    /// <inheritdoc />
    public bool SupportsStreaming => true;

    /// <inheritdoc />
    public bool StreamsNatively => true;

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new LlmResponse { Content = string.Concat(chunks) });

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new LlmResponse { Content = string.Concat(chunks) });

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StreamingCalls++;
        foreach (var chunk in chunks)
            yield return chunk;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StreamingCalls++;
        foreach (var chunk in chunks)
            yield return LlmStreamEvent.Content(chunk);
        yield return LlmStreamEvent.Complete(new LlmResponse { Content = string.Concat(chunks) });
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
