using Orkeon.Infrastructure.LLMs.Embeddings;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>In-memory <see cref="IEmbeddingFingerprintStore"/> for the dimension guard tests.</summary>
internal sealed class MockEmbeddingFingerprintStore : IEmbeddingFingerprintStore
{
    public EmbeddingFingerprint? Stored { get; set; }
    public int SaveCallCount { get; private set; }
    public Exception? LoadFailure { get; set; }

    public Task<EmbeddingFingerprint?> LoadAsync(CancellationToken cancellationToken) =>
        LoadFailure is null ? Task.FromResult(Stored) : Task.FromException<EmbeddingFingerprint?>(LoadFailure);

    public Task SaveAsync(EmbeddingFingerprint fingerprint, CancellationToken cancellationToken)
    {
        SaveCallCount++;
        Stored = fingerprint;
        return Task.CompletedTask;
    }
}
