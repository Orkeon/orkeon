using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of ILlmProvider for testing.
/// </summary>
public sealed class MockLlmProvider : ILlmProvider
{
    private LlmResponse _generateResult = new() { Content = "mock response" };
    private LlmResponse _chatResult = new() { Content = "mock chat response" };
    private Func<string, LlmConfig?, LlmResponse>? _generateFunc;
    private Func<LlmMessage[], LlmConfig?, LlmResponse>? _chatFunc;
    private Exception? _chatException;

    public string Name { get; set; } = "MockLlmProvider";

    // --- Tracking ---
    public int GenerateCallCount { get; private set; }
    public int ChatCallCount { get; private set; }
    public string? LastGeneratePrompt { get; private set; }
    public LlmConfig? LastGenerateConfig { get; private set; }
    public LlmMessage[]? LastChatMessages { get; private set; }
    public LlmConfig? LastChatConfig { get; private set; }

    // --- Configuration ---
    public void SetGenerateResult(LlmResponse result) => _generateResult = result;
    public void SetGenerateResult(string content) => _generateResult = new LlmResponse { Content = content };
    public void SetChatResult(LlmResponse result) => _chatResult = result;
    public void SetChatResult(string content) => _chatResult = new LlmResponse { Content = content };
    public void SetGenerateFunc(Func<string, LlmConfig?, LlmResponse> func) => _generateFunc = func;
    public void SetChatFunc(Func<LlmMessage[], LlmConfig?, LlmResponse> func) => _chatFunc = func;
    public void SetChatException(Exception ex) => _chatException = ex;

    public Task<LlmResponse> GenerateAsync(
        string prompt,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        GenerateCallCount++;
        LastGeneratePrompt = prompt;
        LastGenerateConfig = config;

        var result = _generateFunc != null ? _generateFunc(prompt, config) : _generateResult;
        return Task.FromResult(result);
    }

    public Task<LlmResponse> ChatAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        ChatCallCount++;
        LastChatMessages = messages;
        LastChatConfig = config;

        if (_chatException != null)
            return Task.FromException<LlmResponse>(_chatException);

        var result = _chatFunc != null ? _chatFunc(messages, config) : _chatResult;
        return Task.FromResult(result);
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        GenerateCallCount = 0;
        ChatCallCount = 0;
        LastGeneratePrompt = null;
        LastGenerateConfig = null;
        LastChatMessages = null;
        LastChatConfig = null;
    }
}
