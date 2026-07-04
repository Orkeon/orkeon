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

    public int ChatStreamingCallCount { get; private set; }

    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatStreamingCallCount++;
        LastChatMessages = messages;
        LastChatConfig = config;

        foreach (var chunk in _streamingChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return LlmStreamEvent.Content(chunk);
        }

        yield return LlmStreamEvent.Complete(new LlmResponse
        {
            Content = string.Concat(_streamingChunks),
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        ChatCallCount = 0;
        GenerateCallCount = 0;
        GenerateStreamingCallCount = 0;
        LastChatMessages = null;
        LastChatConfig = null;
        LastGeneratePrompt = null;
        LastStreamingPrompt = null;
    }
}
