using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using System.Runtime.CompilerServices;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation that implements both ILlmProvider and IStreamingLlmProvider for testing.
/// </summary>
public sealed class MockStreamingLlmProvider : ILlmProvider, IStreamingLlmProvider
{
    private LlmResponse _chatResult = new() { Content = "mock chat response" };
    private LlmResponse _generateResult = new() { Content = "mock response" };
    private Func<LlmMessage[], LlmConfig?, LlmResponse>? _chatFunc;
    private IReadOnlyList<string> _streamingChunks = ["mock chunk"];
    private bool _supportsStreaming = true;

    public string Name { get; set; } = "MockStreamingLlmProvider";

    /// <summary>The configuration the provider declares (<see cref="ILlmProvider.BaseConfig"/>).</summary>
    public LlmConfig? BaseConfig { get; set; }

    // --- Tracking ---
    public int ChatCallCount { get; private set; }
    public int GenerateCallCount { get; private set; }
    public int GenerateStreamingCallCount { get; private set; }
    public LlmMessage[]? LastChatMessages { get; private set; }
    public LlmConfig? LastChatConfig { get; private set; }
    public string? LastGeneratePrompt { get; private set; }
    public string? LastStreamingPrompt { get; private set; }

    // --- Configuration ---
    public void SetChatResult(LlmResponse result) => _chatResult = result;
    public void SetChatResult(string content) => _chatResult = new LlmResponse { Content = content };
    public void SetGenerateResult(LlmResponse result) => _generateResult = result;
    public void SetChatFunc(Func<LlmMessage[], LlmConfig?, LlmResponse> func) => _chatFunc = func;
    public void SetStreamingChunks(IReadOnlyList<string> chunks) => _streamingChunks = chunks;

    /// <summary>
    /// The final response the chat stream completes with; unset, it is the chunks' text with
    /// no usage, the way a provider that counts nothing ends its stream.
    /// </summary>
    public void SetCompletedResponse(LlmResponse response) => _completedResponse = response;

    private LlmResponse? _completedResponse;
    private Func<LlmMessage[], LlmConfig?, CancellationToken, IAsyncEnumerable<LlmStreamEvent>>? _chatStreamingHandler;
    private Func<LlmMessage[], LlmConfig?, StreamedTurn>? _chatStreamingFunc;
    private readonly List<LlmMessage[]> _chatStreamingMessages = [];

    /// <summary>One streamed answer: the content chunks, in order, then the final response.</summary>
    /// <param name="Chunks">The content deltas, in order.</param>
    /// <param name="Final">The response the stream completes with.</param>
    public sealed record StreamedTurn(IReadOnlyList<string> Chunks, LlmResponse Final);

    /// <summary>
    /// Answers each chat stream from the conversation it is asked: its chunks, then its final
    /// response — for a crew whose tasks must each get their own answer.
    /// </summary>
    public void SetChatStreamingFunc(Func<LlmMessage[], LlmConfig?, StreamedTurn> func) => _chatStreamingFunc = func;

    /// <summary>
    /// Takes over each chat stream entirely, with the call's token — for a test that needs a call
    /// to wait (a cancellation, a rendezvous between concurrent tasks).
    /// </summary>
    public void SetChatStreamingHandler(Func<LlmMessage[], LlmConfig?, CancellationToken, IAsyncEnumerable<LlmStreamEvent>> handler) =>
        _chatStreamingHandler = handler;

    /// <summary>The conversation of every chat stream, in call order (thread-safe snapshot).</summary>
    public IReadOnlyList<LlmMessage[]> ChatStreamingMessages
    {
        get
        {
            lock (_chatStreamingMessages)
                return [.. _chatStreamingMessages];
        }
    }

    public bool SupportsStreaming
    {
        get => _supportsStreaming;
        set => _supportsStreaming = value;
    }

    public Task<LlmResponse> GenerateAsync(
        string prompt,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        GenerateCallCount++;
        LastGeneratePrompt = prompt;
        return Task.FromResult(_generateResult);
    }

    public Task<LlmResponse> ChatAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        ChatCallCount++;
        LastChatMessages = messages;
        LastChatConfig = config;

        var result = _chatFunc != null ? _chatFunc(messages, config) : _chatResult;
        return Task.FromResult(result);
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        GenerateStreamingCallCount++;
        LastStreamingPrompt = prompt;

        foreach (var chunk in _streamingChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return chunk;
        }

        await Task.CompletedTask;
    }

    private int _chatStreamingCallCount;

    public int ChatStreamingCallCount => Volatile.Read(ref _chatStreamingCallCount);

    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _chatStreamingCallCount);
        lock (_chatStreamingMessages)
        {
            _chatStreamingMessages.Add(messages);
            LastChatMessages = messages;
            LastChatConfig = config;
        }

        if (_chatStreamingHandler is not null)
        {
            await foreach (var streamed in _chatStreamingHandler(messages, config, cancellationToken).WithCancellation(cancellationToken))
                yield return streamed;
            yield break;
        }

        var turn = _chatStreamingFunc?.Invoke(messages, config)
            ?? new StreamedTurn(_streamingChunks, _completedResponse ?? new LlmResponse { Content = string.Concat(_streamingChunks) });

        foreach (var chunk in turn.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return LlmStreamEvent.Content(chunk);
            await Task.Yield();
        }

        yield return LlmStreamEvent.Complete(turn.Final);
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _chatStreamingCallCount, 0);
        ChatCallCount = 0;
        GenerateCallCount = 0;
        GenerateStreamingCallCount = 0;
        LastChatMessages = null;
        LastChatConfig = null;
        LastGeneratePrompt = null;
        LastStreamingPrompt = null;
    }
}
