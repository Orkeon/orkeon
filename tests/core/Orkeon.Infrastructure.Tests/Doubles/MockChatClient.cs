using Microsoft.Extensions.AI;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IChatClient for testing.
/// </summary>
public sealed class MockChatClient : IChatClient
{
    private ChatResponse _getResponseResult = new(new ChatMessage(ChatRole.Assistant, "mock response"));
    private Func<IEnumerable<ChatMessage>, ChatOptions?, ChatResponse>? _getResponseFunc;
    private Exception? _getResponseException;
    private IReadOnlyList<ChatResponseUpdate>? _streamingUpdates;
    private Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? _streamingFunc;

    // --- Tracking ---
    public int GetResponseCallCount { get; private set; }
    public int GetStreamingResponseCallCount { get; private set; }
    public int GetServiceCallCount { get; private set; }
    public IEnumerable<ChatMessage>? LastGetResponseMessages { get; private set; }
    public ChatOptions? LastGetResponseOptions { get; private set; }
    public IEnumerable<ChatMessage>? LastGetStreamingResponseMessages { get; private set; }
    public ChatOptions? LastGetStreamingResponseOptions { get; private set; }

    // --- Configuration ---
    public void SetGetResponseResult(ChatResponse result) => _getResponseResult = result;
    public void SetGetResponseResult(string content) =>
        _getResponseResult = new ChatResponse(new ChatMessage(ChatRole.Assistant, content));
    public void SetGetResponseFunc(Func<IEnumerable<ChatMessage>, ChatOptions?, ChatResponse> func) =>
        _getResponseFunc = func;

    /// <summary>
    /// Configures GetResponseAsync to throw the specified exception.
    /// </summary>
    public void SetGetResponseException(Exception exception) => _getResponseException = exception;

    public void SetStreamingUpdates(IReadOnlyList<ChatResponseUpdate> updates) =>
        _streamingUpdates = updates;
    public void SetStreamingFunc(Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> func) =>
        _streamingFunc = func;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GetResponseCallCount++;
        LastGetResponseMessages = chatMessages;
        LastGetResponseOptions = options;

        if (_getResponseException != null)
            throw _getResponseException;

        var result = _getResponseFunc != null
            ? _getResponseFunc(chatMessages, options)
            : _getResponseResult;
        return Task.FromResult(result);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        GetStreamingResponseCallCount++;
        LastGetStreamingResponseMessages = chatMessages;
        LastGetStreamingResponseOptions = options;

        if (_streamingFunc != null)
        {
            await foreach (var update in _streamingFunc(chatMessages, options, cancellationToken))
            {
                yield return update;
            }
            yield break;
        }

        var updates = _streamingUpdates ??
        [
            new ChatResponseUpdate(ChatRole.Assistant, "mock streaming response")
        ];

        foreach (var update in updates)
        {
            yield return update;
        }

        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        GetServiceCallCount++;

        if (serviceType == typeof(IChatClient))
            return this;

        return null;
    }

    public void Dispose()
    {
        // No resources to dispose in mock
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        GetResponseCallCount = 0;
        GetStreamingResponseCallCount = 0;
        GetServiceCallCount = 0;
        LastGetResponseMessages = null;
        LastGetResponseOptions = null;
        LastGetStreamingResponseMessages = null;
        LastGetStreamingResponseOptions = null;
    }
}
