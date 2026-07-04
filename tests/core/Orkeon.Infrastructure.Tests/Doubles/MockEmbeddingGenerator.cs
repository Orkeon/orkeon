using Microsoft.Extensions.AI;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt; for testing.
/// </summary>
public sealed class MockEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private float[] _defaultVector = [0.1f, 0.2f, 0.3f];
    private Func<IEnumerable<string>, GeneratedEmbeddings<Embedding<float>>>? _generateFunc;

    // --- Tracking ---
    public int GenerateCallCount { get; private set; }
    public int GetServiceCallCount { get; private set; }
    public IEnumerable<string>? LastGenerateValues { get; private set; }
    public EmbeddingGenerationOptions? LastGenerateOptions { get; private set; }

    // --- Configuration ---
    public void SetDefaultVector(float[] vector) => _defaultVector = vector;
    public void SetGenerateFunc(Func<IEnumerable<string>, GeneratedEmbeddings<Embedding<float>>> func) =>
        _generateFunc = func;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GenerateCallCount++;
        LastGenerateValues = values;
        LastGenerateOptions = options;

        if (_generateFunc != null)
            return Task.FromResult(_generateFunc(values));

        var embeddings = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var _ in values)
        {
            embeddings.Add(new Embedding<float>(_defaultVector));
        }
        return Task.FromResult(embeddings);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        GetServiceCallCount++;

        if (serviceType == typeof(IEmbeddingGenerator<string, Embedding<float>>))
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
        GenerateCallCount = 0;
        GetServiceCallCount = 0;
        LastGenerateValues = null;
        LastGenerateOptions = null;
    }
}
