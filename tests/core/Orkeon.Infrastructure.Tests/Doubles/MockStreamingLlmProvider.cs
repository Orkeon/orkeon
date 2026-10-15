using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using System.Net;
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

    /// <summary>Whether the double streams on the wire (the port's default is false).</summary>
    public bool StreamsNatively { get; set; }

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

    private Exception? _callFailure;
    private Exception? _failureAfterChunks;
    private int _failAfterChunkCount;

    /// <summary>Makes every streaming method throw <paramref name="failure"/> when it is called, before any enumerator exists.</summary>
    public void FailStreamingCallWith(Exception failure) => _callFailure = failure;

    /// <summary>Makes the text stream throw <paramref name="failure"/> once <paramref name="chunkCount"/> chunks were yielded.</summary>
    public void FailGenerateStreamingAfter(int chunkCount, Exception failure)
    {
        _failAfterChunkCount = chunkCount;
        _failureAfterChunks = failure;
    }

    private Exception? _moveNextFailure;
    private Exception? _disposeFailure;

    /// <summary>
    /// Makes every stream (text and chat) hand out an enumerator whose <c>MoveNextAsync</c> throws
    /// <paramref name="moveNext"/> and whose <c>DisposeAsync</c> then throws <paramref name="dispose"/>.
    /// </summary>
    public void BreakStreamsAndDisposalWith(Exception moveNext, Exception dispose)
    {
        _moveNextFailure = moveNext;
        _disposeFailure = dispose;
    }

    private sealed class BrokenStream<T>(Exception moveNext, Exception dispose) : IAsyncEnumerable<T>, IAsyncEnumerator<T>
    {
        public T Current => default!;

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync() => throw moveNext;

        public ValueTask DisposeAsync() => throw dispose;
    }

    public IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        if (_callFailure is not null)
            throw _callFailure;
        if (_moveNextFailure is not null)
            return new BrokenStream<string>(_moveNextFailure, _disposeFailure!);
        return GenerateStreamingCore(prompt, cancellationToken);
    }

    private async IAsyncEnumerable<string> GenerateStreamingCore(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        GenerateStreamingCallCount++;
        LastStreamingPrompt = prompt;

        var yielded = 0;
        foreach (var chunk in _streamingChunks)
        {
            if (_failureAfterChunks is not null && yielded == _failAfterChunkCount)
                throw _failureAfterChunks;
            cancellationToken.ThrowIfCancellationRequested();
            yield return chunk;
            yielded++;
        }

        if (_failureAfterChunks is not null && yielded == _failAfterChunkCount)
            throw _failureAfterChunks;

        await Task.CompletedTask;
    }

    private Exception? _streamingFailure;

    /// <summary>Makes every chat stream throw an HTTP refusal (status, body in the message) before any event.</summary>
    public void RefuseStreamingWith(HttpStatusCode status, string body = "") =>
        _streamingFailure = new HttpRequestException($"Streaming refused ({(int)status}): {body}", null, status);

    /// <summary>Makes every chat stream throw <paramref name="failure"/> before any event.</summary>
    public void FailStreamingWith(Exception failure) => _streamingFailure = failure;

    /// <summary>Lifts a refusal: chat streams answer again.</summary>
    public void LiftRefusal() => _streamingFailure = null;

    private int _chatStreamingCallCount;

    public int ChatStreamingCallCount => Volatile.Read(ref _chatStreamingCallCount);

    public IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        if (_callFailure is not null)
            throw _callFailure;
        if (_moveNextFailure is not null)
            return new BrokenStream<LlmStreamEvent>(_moveNextFailure, _disposeFailure!);
        return ChatStreamingCore(messages, config, cancellationToken);
    }

    private async IAsyncEnumerable<LlmStreamEvent> ChatStreamingCore(
        LlmMessage[] messages,
        LlmConfig? config,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _chatStreamingCallCount);
        lock (_chatStreamingMessages)
        {
            _chatStreamingMessages.Add(messages);
            LastChatMessages = messages;
            LastChatConfig = config;
        }

        if (_streamingFailure is not null)
            throw _streamingFailure;

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
