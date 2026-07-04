using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IBasicLlmProvider for testing.
/// </summary>
public sealed class MockBasicLlmProvider : IBasicLlmProvider
{
    private string _chatResult = "mock basic response";
    private bool _isAvailable = true;
    private Func<string, LlmConfig?, string>? _chatFunc;
    private Exception? _isAvailableException;
    private Func<Task<bool>>? _isAvailableFunc;

    public string Name { get; set; } = "MockBasicLlmProvider";

    // --- Tracking ---
    public int ChatCallCount { get; private set; }
    public int IsAvailableCallCount { get; private set; }
    public string? LastChatMessage { get; private set; }
    public LlmConfig? LastChatConfig { get; private set; }

    // --- Configuration ---
    public void SetChatResult(string result) => _chatResult = result;
    public void SetIsAvailable(bool available) => _isAvailable = available;
    public void SetChatFunc(Func<string, LlmConfig?, string> func) => _chatFunc = func;
    public void SetIsAvailableException(Exception ex) => _isAvailableException = ex;
    public void SetIsAvailableFunc(Func<Task<bool>> func) => _isAvailableFunc = func;

    public Task<string> ChatAsync(
        string message,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        ChatCallCount++;
        LastChatMessage = message;
        LastChatConfig = config;

        var result = _chatFunc != null ? _chatFunc(message, config) : _chatResult;
        return Task.FromResult(result);
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        IsAvailableCallCount++;

        if (_isAvailableException != null)
            return Task.FromException<bool>(_isAvailableException);

        if (_isAvailableFunc != null)
            return _isAvailableFunc();

        return Task.FromResult(_isAvailable);
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        ChatCallCount = 0;
        IsAvailableCallCount = 0;
        LastChatMessage = null;
        LastChatConfig = null;
    }
}
