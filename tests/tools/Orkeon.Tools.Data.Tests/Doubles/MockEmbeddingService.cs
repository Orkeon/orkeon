using DomainEmbeddingService = Orkeon.Domain.Memory.IEmbeddingService;

namespace Orkeon.Tools.Data.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IEmbeddingService for testing file search tools.
/// Implements the Domain embedding interface.
/// Supports deterministic embeddings, custom factory functions, and exception injection.
/// </summary>
public sealed class MockEmbeddingService : DomainEmbeddingService
{
    private float[] _defaultEmbedding = [0.1f, 0.2f, 0.3f];
    private Func<string, float[]>? _embeddingFactory;
    private Exception? _exceptionToThrow;

    // --- Tracking ---
    public int CallCount { get; private set; }
    public string? LastText { get; private set; }
    public List<string> AllTexts { get; } = [];

    // --- Configuration ---

    /// <summary>Sets a fixed embedding result returned for all inputs.</summary>
    public void SetEmbeddingResult(float[] result) => _defaultEmbedding = result;

    /// <summary>Sets a factory function that produces different embeddings per content.</summary>
    public void SetEmbeddingFactory(Func<string, float[]> factory) => _embeddingFactory = factory;

    /// <summary>Configures the mock to throw the given exception on next call.</summary>
    public void SetExceptionToThrow(Exception ex) => _exceptionToThrow = ex;

    /// <summary>Core implementation used by the Domain interface.</summary>
    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastText = text;
        AllTexts.Add(text);

        if (_exceptionToThrow is not null)
            return Task.FromException<float[]>(_exceptionToThrow);

        var result = _embeddingFactory is not null
            ? _embeddingFactory(text)
            : _defaultEmbedding;

        return Task.FromResult(result);
    }

    /// <summary>Resets all tracking state.</summary>
    public void Reset()
    {
        CallCount = 0;
        LastText = null;
        AllTexts.Clear();
    }
}
