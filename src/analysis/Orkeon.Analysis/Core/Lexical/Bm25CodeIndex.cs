using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core.Lexical;

/// <summary>
/// In-memory BM25 index over RaggableTree nodes — the lexical half of hybrid code
/// search. A NATIVE Analysis implementation, not a reference to
/// <c>Orkeon.Rag.Retrieval.Bm25Index</c>: Analysis must not reference Orkeon.Rag
/// (ADR-003/006 keep the dependency pointing the other way), and code needs the
/// code-aware tokenizer (<see cref="CodeTokenizer"/>), not the prose one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its tuning is its own.</b> <see cref="DefaultK1"/> and <see cref="DefaultB"/>
/// carry the textbook Okapi values, which is also where the RAG index got its own —
/// they coincide because both start from the literature, not because either tracks the
/// other. Prose chunks and code declarations have different length distributions, so
/// retuning one side is a legitimate change that must NOT be propagated to the other.
/// Nothing enforces equality, and nothing should: this is a tuning parameter of one
/// index, not a value two subsystems have to agree on (ADR-009).
/// </para>
/// <para>
/// Indexed text per node: <c>Name + Fqn + Signature + SemanticSummary + DocComment</c> —
/// the fields a developer's query words actually appear in. Deliberately NOT the source
/// snippet: indexing bodies would drown declarations under their own call sites, and the
/// vector half already captures body semantics through the embedding text.
/// Not thread-safe by itself — the owning store serializes access (its RW lock).
/// </para>
/// </remarks>
public sealed class Bm25CodeIndex
{
    /// <summary>Term-frequency saturation: the standard Okapi BM25 value.</summary>
    public const double DefaultK1 = 1.2;

    /// <summary>Length normalization: the standard Okapi BM25 value.</summary>
    public const double DefaultB = 0.75;

    private readonly double _k1;
    private readonly double _b;

    // term → (nodeId → term frequency)
    private readonly Dictionary<string, Dictionary<string, int>> _postings = new(StringComparer.Ordinal);
    // nodeId → token count (document length)
    private readonly Dictionary<string, int> _lengths = new(StringComparer.Ordinal);
    // nodeId → the terms it contributed (for cheap removal)
    private readonly Dictionary<string, string[]> _termsByNode = new(StringComparer.Ordinal);
    private long _totalTokenCount;

    /// <summary>Initializes an empty index.</summary>
    /// <param name="k1">Term-frequency saturation (must be non-negative).</param>
    /// <param name="b">Length normalization, in [0, 1].</param>
    public Bm25CodeIndex(double k1 = DefaultK1, double b = DefaultB)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k1);
        ArgumentOutOfRangeException.ThrowIfNegative(b);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(b, 1.0);
        _k1 = k1;
        _b = b;
    }

    /// <summary>Number of indexed nodes.</summary>
    public int Count => _lengths.Count;

    /// <summary>Adds or replaces one node's lexical document.</summary>
    public void Upsert(RaggableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        Remove(node.Id);

        var tokens = CodeTokenizer.Tokenize(ComposeDocument(node));
        if (tokens.Count == 0) return;

        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in tokens)
            frequencies[token] = frequencies.GetValueOrDefault(token) + 1;

        foreach (var (term, tf) in frequencies)
        {
            if (!_postings.TryGetValue(term, out var posting))
            {
                posting = new Dictionary<string, int>(StringComparer.Ordinal);
                _postings[term] = posting;
            }
            posting[node.Id] = tf;
        }

        _lengths[node.Id] = tokens.Count;
        _termsByNode[node.Id] = [.. frequencies.Keys];
        _totalTokenCount += tokens.Count;
    }

    /// <summary>Removes one node's document; unknown ids are a no-op.</summary>
    public void Remove(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        if (!_termsByNode.TryGetValue(nodeId, out var terms)) return;

        foreach (var term in terms)
        {
            if (_postings.TryGetValue(term, out var posting))
            {
                posting.Remove(nodeId);
                if (posting.Count == 0) _postings.Remove(term);
            }
        }
        _totalTokenCount -= _lengths.GetValueOrDefault(nodeId);
        _lengths.Remove(nodeId);
        _termsByNode.Remove(nodeId);
    }

    /// <summary>Drops every document (full re-index).</summary>
    public void Clear()
    {
        _postings.Clear();
        _lengths.Clear();
        _termsByNode.Clear();
        _totalTokenCount = 0;
    }

    /// <summary>
    /// BM25-scores the indexed nodes against <paramref name="query"/> and returns the
    /// best <paramref name="topK"/> as (nodeId, score), best first. A query with no
    /// indexable token — or an empty index — returns an empty list.
    /// </summary>
    public IReadOnlyList<(string NodeId, double Score)> Search(string query, int topK)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        var queryTerms = CodeTokenizer.Tokenize(query);
        if (queryTerms.Count == 0 || _lengths.Count == 0) return [];

        var documentCount = (double)_lengths.Count;
        var averageLength = _totalTokenCount / documentCount;
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var term in queryTerms.Distinct(StringComparer.Ordinal))
        {
            if (!_postings.TryGetValue(term, out var posting)) continue;

            var idf = Math.Log(1.0 + ((documentCount - posting.Count + 0.5) / (posting.Count + 0.5)));
            foreach (var (nodeId, tf) in posting)
            {
                var length = _lengths[nodeId];
                var normalized = tf * (_k1 + 1.0)
                    / (tf + (_k1 * (1.0 - _b + (_b * (length / averageLength)))));
                scores[nodeId] = scores.GetValueOrDefault(nodeId) + (idf * normalized);
            }
        }

        return [.. scores
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(topK)
            .Select(pair => (pair.Key, pair.Value))];
    }

    private static string ComposeDocument(RaggableNode node)
        => string.Join(
            ' ',
            new[] { node.Name, node.Fqn, node.Signature, node.SemanticSummary, node.DocComment }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
}
