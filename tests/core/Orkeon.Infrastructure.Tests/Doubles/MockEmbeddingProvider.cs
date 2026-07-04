using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IEmbeddingProvider for testing.
/// </summary>
public sealed class MockEmbeddingProvider : IEmbeddingProvider
{
    private float[] _embeddingResult = [0.1f, 0.2f, 0.3f];
    private IList<float[]>? _embeddingsResult;
    private Func<string, float[]>? _embeddingFunc;

    public string Name { get; set; } = "MockEmbeddingProvider";
    public string Model { get; set; } = "mock-embedding-model";
    public int Dimensions { get; set; } = 3;

    // --- Tracking ---
    public int GetEmbeddingCallCount { get; private set; }
    public int GetEmbeddingsCallCount { get; private set; }
    public string? LastGetEmbeddingText { get; private set; }
    public IList<string>? LastGetEmbeddingsTexts { get; private set; }

    // --- Configuration ---
    public void SetEmbeddingResult(float[] result)
    {
        _embeddingResult = result;
        Dimensions = result.Length;
    }

    public void SetEmbeddingsResult(IList<float[]> result) => _embeddingsResult = result;
    public void SetEmbeddingFunc(Func<string, float[]> func) => _embeddingFunc = func;

    public Task<float[]> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        GetEmbeddingCallCount++;
        LastGetEmbeddingText = text;

        var result = _embeddingFunc != null ? _embeddingFunc(text) : _embeddingResult;
        return Task.FromResult(result);
    }

    public Task<IList<float[]>> GetEmbeddingsAsync(
        IList<string> texts,
        CancellationToken cancellationToken = default)
    {
        GetEmbeddingsCallCount++;
        LastGetEmbeddingsTexts = texts;

        if (_embeddingsResult != null)
            return Task.FromResult(_embeddingsResult);

        // Generate individual embeddings for each text
        IList<float[]> results = texts
            .Select(t => _embeddingFunc != null ? _embeddingFunc(t) : _embeddingResult)
            .ToList();
        return Task.FromResult(results);
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        GetEmbeddingCallCount = 0;
        GetEmbeddingsCallCount = 0;
        LastGetEmbeddingText = null;
        LastGetEmbeddingsTexts = null;
    }
}
