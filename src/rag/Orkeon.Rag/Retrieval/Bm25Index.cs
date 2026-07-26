using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Retrieval;

/// <summary>
/// In-process, in-memory BM25 lexical index over the chunks of a single collection
/// (RAG-04/C2, plan §5). Feeds the lexical side of the emulated hybrid path of
/// <see cref="HybridSearchDocumentStore"/> on providers without native hybrid search.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scoring</b> — classic Okapi BM25 with the standard parameters
/// <c>k1 = 1.2</c>, <c>b = 0.75</c>:
/// <c>score(q, d) = Σ idf(t) · tf(t, d)·(k1+1) / (tf(t, d) + k1·(1 − b + b·|d|/avgdl))</c>
/// with <c>idf(t) = ln(1 + (N − df(t) + 0.5) / (df(t) + 0.5))</c> (always positive).
/// Tokenization is deliberately simple and robust: contiguous Unicode letter/digit runs,
/// lowercased with the invariant culture — no stemming, no stop-word list.
/// </para>
/// <para>
/// <b>Memory limit (documented by design)</b> — the whole index (terms, postings and
/// chunk texts' statistics) lives in process memory and is rebuilt per process: it only
/// covers chunks that were upserted through the owning decorator in the current process.
/// This is acceptable at demo/small-corpus scale (thousands of chunks); for large corpora
/// or multi-process deployments, the scale path is the <b>native</b> hybrid search of the
/// backing store (LanceDB server-side BM25/FTS + vector, exposed through
/// <c>IHybridSearchCapable</c>), which the decorator prefers automatically.
/// </para>
/// <para>Thread-safe: all members take an internal lock (searches are short and CPU-bound).</para>
/// </remarks>
public sealed class Bm25Index
{
    /// <summary><see cref="ScoredChunk.ScoreOrigin"/> of scores produced by this index.</summary>
    public const string Bm25ScoreOrigin = "bm25";

    /// <summary>Standard BM25 term-frequency saturation parameter.</summary>
    public const double DefaultK1 = 1.2;

    /// <summary>Standard BM25 length-normalization parameter.</summary>
    public const double DefaultB = 0.75;

    private readonly double _k1;
    private readonly double _b;
    private readonly object _gate = new();

    /// <summary>Indexed chunks by chunk id.</summary>
    private readonly Dictionary<string, IndexedChunk> _chunks = new(StringComparer.Ordinal);

    /// <summary>Postings: term → (chunk id → term frequency).</summary>
    private readonly Dictionary<string, Dictionary<string, int>> _postings = new(StringComparer.Ordinal);

    private long _totalTokenCount;

    /// <summary>Initializes an empty index.</summary>
    /// <param name="k1">BM25 term-frequency saturation (must be non-negative).</param>
    /// <param name="b">BM25 length normalization, in [0, 1].</param>
    public Bm25Index(double k1 = DefaultK1, double b = DefaultB)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k1);
        ArgumentOutOfRangeException.ThrowIfNegative(b);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(b, 1.0);
        _k1 = k1;
        _b = b;
    }

    /// <summary>Gets the number of indexed chunks.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _chunks.Count;
            }
        }
    }

    /// <summary>Inserts or replaces (by <see cref="Chunk.Id"/>) every chunk of <paramref name="chunks"/>.</summary>
    public void UpsertRange(IEnumerable<Chunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        lock (_gate)
        {
            foreach (var chunk in chunks)
            {
                ArgumentNullException.ThrowIfNull(chunk);
                RemoveUnlocked(chunk.Id);
                AddUnlocked(chunk);
            }
        }
    }

    /// <summary>Removes every indexed chunk originating from <paramref name="sourceId"/>.</summary>
    public void RemoveSource(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        lock (_gate)
        {
            var ids = _chunks.Values
                .Where(entry => string.Equals(entry.Chunk.SourceId, sourceId, StringComparison.Ordinal))
                .Select(entry => entry.Chunk.Id)
                .ToList();

            foreach (var id in ids)
                RemoveUnlocked(id);
        }
    }

    /// <summary>
    /// Scores every indexed chunk against <paramref name="query"/> and returns the best
    /// <paramref name="topK"/> matches, best first, with
    /// <see cref="ScoredChunk.ScoreOrigin"/> = <c>bm25</c>. A query without any indexable
    /// token returns an empty list.
    /// </summary>
    public IReadOnlyList<ScoredChunk> Search(string query, int topK)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var queryTerms = Tokenize(query);
        if (queryTerms.Count == 0)
            return [];

        lock (_gate)
        {
            if (_chunks.Count == 0)
                return [];

            var documentCount = (double)_chunks.Count;
            var averageLength = _totalTokenCount / documentCount;
            var scores = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (var term in queryTerms.Distinct(StringComparer.Ordinal))
            {
                if (!_postings.TryGetValue(term, out var posting))
                    continue;

                var idf = Math.Log(1.0 + ((documentCount - posting.Count + 0.5) / (posting.Count + 0.5)));

                foreach (var (chunkId, termFrequency) in posting)
                {
                    var length = _chunks[chunkId].TokenCount;
                    var normalized = termFrequency * (_k1 + 1.0)
                        / (termFrequency + (_k1 * (1.0 - _b + (_b * (length / averageLength)))));

                    scores[chunkId] = scores.GetValueOrDefault(chunkId) + (idf * normalized);
                }
            }

            return scores
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(topK)
                .Select(pair => new ScoredChunk
                {
                    Chunk = _chunks[pair.Key].Chunk,
                    Score = pair.Value,
                    ScoreOrigin = Bm25ScoreOrigin,
                })
                .ToList();
        }
    }

    /// <summary>
    /// Splits <paramref name="text"/> into lowercase (invariant culture) tokens: contiguous
    /// runs of Unicode letters or digits; everything else separates.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308",
        Justification = "Lowercasing is the linguistic normalization of the index terms (queries and documents must fold identically), not a security-sensitive comparison normalization.")]
    internal static List<string> Tokenize(string? text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(text))
            return tokens;

        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isTokenChar = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (isTokenChar)
            {
                if (start < 0)
                    start = i;
            }
            else if (start >= 0)
            {
                tokens.Add(text[start..i].ToLowerInvariant());
                start = -1;
            }
        }

        return tokens;
    }

    private void AddUnlocked(Chunk chunk)
    {
        var tokens = Tokenize(chunk.Content);
        _chunks[chunk.Id] = new IndexedChunk(chunk, tokens.Count);
        _totalTokenCount += tokens.Count;

        foreach (var group in tokens.GroupBy(token => token, StringComparer.Ordinal))
        {
            if (!_postings.TryGetValue(group.Key, out var posting))
            {
                posting = new Dictionary<string, int>(StringComparer.Ordinal);
                _postings[group.Key] = posting;
            }

            posting[chunk.Id] = group.Count();
        }
    }

    private void RemoveUnlocked(string chunkId)
    {
        if (!_chunks.Remove(chunkId, out var removed))
            return;

        _totalTokenCount -= removed.TokenCount;

        // Full postings sweep: acceptable at the in-process scale documented above.
        foreach (var term in _postings.Keys.ToList())
        {
            var posting = _postings[term];
            if (posting.Remove(chunkId) && posting.Count == 0)
                _postings.Remove(term);
        }
    }

    private sealed record IndexedChunk(Chunk Chunk, int TokenCount);
}
