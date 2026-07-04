using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IEmbeddingService for testing.
/// </summary>
public sealed class MockEmbeddingService : IEmbeddingService
{
    private float[] _embeddingResult = [0.1f, 0.2f, 0.3f];
    private Func<string, float[]>? _embeddingFunc;
    private Exception? _exceptionToThrow;

    // --- Tracking ---
    public int GetEmbeddingCallCount { get; private set; }
    public string? LastGetEmbeddingText { get; private set; }

    // --- Configuration ---
    public void SetEmbeddingResult(float[] result) => _embeddingResult = result;
    public void SetEmbeddingFunc(Func<string, float[]> func) => _embeddingFunc = func;
    public void SetExceptionToThrow(Exception ex) => _exceptionToThrow = ex;

    public Task<float[]> GetEmbeddingAsync(string text)
    {
        GetEmbeddingCallCount++;
        LastGetEmbeddingText = text;

        if (_exceptionToThrow != null)
            return Task.FromException<float[]>(_exceptionToThrow);

        var result = _embeddingFunc != null ? _embeddingFunc(text) : _embeddingResult;
        return Task.FromResult(result);
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        GetEmbeddingCallCount = 0;
        LastGetEmbeddingText = null;
    }
}
