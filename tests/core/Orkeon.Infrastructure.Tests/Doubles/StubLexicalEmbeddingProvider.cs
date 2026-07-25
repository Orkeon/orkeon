using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Deterministic hand-written embedding double whose cosine similarity mirrors term overlap.
/// Each distinct token is assigned its own dimension (bag-of-words, binary presence), so
/// texts sharing words score high and disjoint texts score exactly zero — collision-free
/// as long as the test vocabulary stays below <see cref="Dimensions"/>.
/// </summary>
public sealed class StubLexicalEmbeddingProvider : IEmbeddingProvider
{
    private readonly ConcurrentDictionary<string, int> _vocabulary = new(StringComparer.Ordinal);
    private int _nextIndex;

    public string Name => "StubLexicalEmbeddingProvider";
    public string Model => "stub-lexical";
    public int Dimensions { get; }

    // --- Tracking ---
    public int GetEmbeddingCallCount { get; private set; }
    public int GetEmbeddingsCallCount { get; private set; }
    public string? LastGetEmbeddingText { get; private set; }
    public IList<string>? LastGetEmbeddingsTexts { get; private set; }

    public StubLexicalEmbeddingProvider(int dimensions = 512)
    {
        Dimensions = dimensions;
    }

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        GetEmbeddingCallCount++;
        LastGetEmbeddingText = text;
        return Task.FromResult(Embed(text));
    }

    public Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default)
    {
        GetEmbeddingsCallCount++;
        LastGetEmbeddingsTexts = texts;
        IList<float[]> results = texts.Select(Embed).ToList();
        return Task.FromResult(results);
    }

    private float[] Embed(string text)
    {
        var vector = new float[Dimensions];

        foreach (var token in Tokenize(text))
        {
            var index = _vocabulary.GetOrAdd(token, _ => Interlocked.Increment(ref _nextIndex) - 1);
            vector[index % Dimensions] = 1f;
        }

        return vector;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var current = new System.Text.StringBuilder();

        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(char.ToLowerInvariant(c));
            }
            else if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
        }

        if (current.Length > 0)
            yield return current.ToString();
    }
}
