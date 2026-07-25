using System.Globalization;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Chunking;

/// <summary>
/// Embedding-driven chunking: splits text into sentences, embeds each sentence, and
/// closes a chunk whenever the next sentence drifts semantically from the running
/// centroid of the current chunk (cosine similarity below the threshold) or when the
/// chunk would exceed <see cref="ChunkingOptions.MaxChunkSize"/>.
/// </summary>
/// <remarks>
/// <para>
/// The embedding function is optional by design (RAG-02/C3): the historical codebase had
/// no true semantic chunker, and this strategy must stay constructible without any
/// embedding provider. <b>When no embedding function is supplied, the strategy falls back
/// to <see cref="StructuralChunkingStrategy"/> semantics</b> (heading-aware splitting) —
/// a documented, deterministic degradation rather than a silent no-op.
/// </para>
/// <para>
/// The similarity threshold defaults to <c>0.75</c> and can be overridden per call via
/// <c>options.Extensions["similarity_threshold"]</c> (invariant-culture float).
/// <see cref="ChunkingOptions.Overlap"/> is ignored in semantic mode: boundaries are
/// semantic, not positional. A single sentence longer than <c>MaxChunkSize</c> is emitted
/// whole (sentences are never split internally).
/// </para>
/// </remarks>
public sealed class SemanticChunkingStrategy : ChunkingStrategyBase
{
    /// <summary>Extensions key overriding the sentence-similarity threshold.</summary>
    public const string SimilarityThresholdKey = "similarity_threshold";

    private const float DefaultSimilarityThreshold = 0.75f;

    private readonly Func<string, float[]>? _embed;
    private readonly StructuralChunkingStrategy _structuralFallback = new();

    /// <summary>
    /// Initializes the strategy.
    /// </summary>
    /// <param name="embeddingFunction">
    /// Optional synchronous embedding function. When <c>null</c>, the strategy degrades to
    /// structural (heading-aware) chunking — see the class remarks.
    /// </param>
    public SemanticChunkingStrategy(Func<string, float[]>? embeddingFunction = null)
    {
        _embed = embeddingFunction;
    }

    /// <inheritdoc />
    public override string Name => "semantic";

    internal override List<ChunkSlice> ComputeSlices(
        string content, int maxChunkSize, int overlap, ChunkingOptions options)
    {
        if (_embed is null)
        {
            // Documented fallback: no embedding provider available → structural chunking.
            return _structuralFallback.ComputeSlices(content, maxChunkSize, overlap, options);
        }

        var sentences = SentenceChunkingStrategy.SplitIntoSentences(content);
        if (sentences.Count == 0)
            return [];

        var threshold = ReadThreshold(options);

        var embeddings = new float[sentences.Count][];
        for (int i = 0; i < sentences.Count; i++)
        {
            var (offset, length) = sentences[i];
            embeddings[i] = _embed(content.Substring(offset, length))
                ?? throw new InvalidOperationException(
                    "The semantic chunking embedding function returned null.");
        }

        var slices = new List<ChunkSlice>();

        int groupStart = sentences[0].Offset;
        int groupEnd = sentences[0].Offset + sentences[0].Length;
        var centroid = (float[])embeddings[0].Clone();
        int groupCount = 1;

        for (int i = 1; i < sentences.Count; i++)
        {
            var (offset, length) = sentences[i];
            var candidateEnd = offset + length;

            var sameTopic =
                embeddings[i].Length == centroid.Length &&
                VectorMath.CosineSimilarity(centroid, embeddings[i]) >= threshold;
            var fits = candidateEnd - groupStart <= maxChunkSize;

            if (sameTopic && fits)
            {
                // Extend the group and update the running centroid incrementally.
                for (int d = 0; d < centroid.Length; d++)
                    centroid[d] = ((centroid[d] * groupCount) + embeddings[i][d]) / (groupCount + 1);
                groupCount++;
                groupEnd = candidateEnd;
            }
            else
            {
                slices.Add(new ChunkSlice(groupStart, groupEnd));
                groupStart = offset;
                groupEnd = candidateEnd;
                centroid = (float[])embeddings[i].Clone();
                groupCount = 1;
            }
        }

        slices.Add(new ChunkSlice(groupStart, groupEnd));
        return slices;
    }

    private static float ReadThreshold(ChunkingOptions options)
    {
        if (options.Extensions.TryGetValue(SimilarityThresholdKey, out var raw) &&
            float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            parsed is >= -1f and <= 1f)
        {
            return parsed;
        }

        return DefaultSimilarityThreshold;
    }
}
