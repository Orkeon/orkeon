using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IEmbeddingProvider"/> with a meaning a test can predict: one
/// dimension per concept, and a text scores on every concept one of whose keywords it contains.
/// "inbox" and "digest" are therefore close in meaning while sharing no term — exactly the case
/// the hybrid search exists for. Records each batch so a test can count model work.
/// </summary>
internal sealed class FakeKeywordEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private static readonly string[][] Concepts =
    [
        ["mail", "inbox", "digest"],
        ["invoice", "purchase", "bill"],
        ["fraud", "suspicious", "scam"],
        ["hire", "onboarding", "newcomer"],
    ];

    /// <summary>Number of <see cref="EmbedBatchAsync"/> calls.</summary>
    public int Batches { get; private set; }

    /// <summary>Every text embedded, in order.</summary>
    public List<string> Texts { get; } = [];

    /// <summary>Whether <see cref="Dispose"/> ran.</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc />
    public int Dimensions => Concepts.Length + 1;

    /// <inheritdoc />
    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(texts);
        Batches++;
        Texts.AddRange(texts);
        return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>([.. texts.Select(Embed)]);
    }

    /// <summary>Marks the provider disposed: the search owns the model it loaded, and releases it.</summary>
    public void Dispose() => Disposed = true;

    private ReadOnlyMemory<float> Embed(string text)
    {
        var lower = text.ToLowerInvariant();
        var vector = new float[Dimensions];
        for (var i = 0; i < Concepts.Length; i++)
        {
            if (Concepts[i].Any(keyword => lower.Contains(keyword, StringComparison.Ordinal)))
                vector[i] = 1f;
        }

        // A small shared component: no vector is ever zero, so every cosine is defined.
        vector[^1] = 0.1f;
        return vector;
    }
}
