namespace Orkeon.Rag.Evaluation;

/// <summary>
/// Deterministic retrieval metrics (plan §9.2): recall@k, precision@k, and
/// reciprocal rank, computed from the dataset's relevant refs against the ranked
/// retrieved items. A retrieved item is described by the set of identifiers it
/// exposes (chunk id, document id, source id); matching is
/// <see cref="Matches"/> — exact or path-suffix, case-insensitive.
/// </summary>
public static class RetrievalEvalMetrics
{
    /// <summary>
    /// True when <paramref name="identifier"/> satisfies <paramref name="relevantRef"/>:
    /// exact match (chunk/document ids), or path-suffix match after stripping an
    /// optional <c>#fragment</c> from the ref (so <c>corpus/faq.md#refunds</c>
    /// matches the source id <c>/workspace/examples/rag/eval/corpus/faq.md</c>).
    /// Comparison is ordinal case-insensitive; <c>\</c> is normalized to <c>/</c>
    /// and a leading <c>./</c> is ignored.
    /// </summary>
    public static bool Matches(string identifier, string relevantRef)
    {
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(relevantRef))
            return false;

        var id = Normalize(identifier);
        var reference = Normalize(relevantRef);

        if (id.Equals(reference, StringComparison.OrdinalIgnoreCase))
            return true;

        var hash = reference.IndexOf('#', StringComparison.Ordinal);
        var path = hash >= 0 ? reference[..hash] : reference;
        if (path.Length == 0)
            return false;

        return id.Equals(path, StringComparison.OrdinalIgnoreCase)
            || id.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Recall@k: fraction of <paramref name="relevant"/> refs matched by at least
    /// one of the first <paramref name="k"/> retrieved items.
    /// </summary>
    /// <param name="relevant">Relevant refs (must be non-empty).</param>
    /// <param name="rankedRetrievedKeys">Per retrieved item (rank order), the identifiers it exposes.</param>
    /// <param name="k">Cutoff (must be positive).</param>
    public static double RecallAtK(
        IReadOnlyList<string> relevant,
        IReadOnlyList<IReadOnlyList<string>> rankedRetrievedKeys,
        int k)
    {
        ArgumentNullException.ThrowIfNull(relevant);
        GuardArguments(relevant, rankedRetrievedKeys, k);

        var found = relevant.Count(reference =>
            TopK(rankedRetrievedKeys, k).Any(keys => keys.Any(key => Matches(key, reference))));

        return (double)found / relevant.Count;
    }

    /// <summary>
    /// Precision@k with denominator <paramref name="k"/> (missing ranks count as
    /// misses): fraction of the first k slots holding an item that matches at
    /// least one relevant ref.
    /// </summary>
    public static double PrecisionAtK(
        IReadOnlyList<string> relevant,
        IReadOnlyList<IReadOnlyList<string>> rankedRetrievedKeys,
        int k)
    {
        ArgumentNullException.ThrowIfNull(relevant);
        GuardArguments(relevant, rankedRetrievedKeys, k);

        var hits = TopK(rankedRetrievedKeys, k)
            .Count(keys => relevant.Any(reference => keys.Any(key => Matches(key, reference))));

        return (double)hits / k;
    }

    /// <summary>
    /// Reciprocal rank of the first retrieved item matching any relevant ref
    /// (1 for rank 1, 0.5 for rank 2, …; 0 when no retrieved item matches).
    /// </summary>
    public static double ReciprocalRank(
        IReadOnlyList<string> relevant,
        IReadOnlyList<IReadOnlyList<string>> rankedRetrievedKeys)
    {
        ArgumentNullException.ThrowIfNull(relevant);
        ArgumentNullException.ThrowIfNull(rankedRetrievedKeys);
        GuardRelevant(relevant);

        for (var rank = 0; rank < rankedRetrievedKeys.Count; rank++)
        {
            var keys = rankedRetrievedKeys[rank];
            if (relevant.Any(reference => keys.Any(key => Matches(key, reference))))
                return 1.0 / (rank + 1);
        }

        return 0.0;
    }

    private static IEnumerable<IReadOnlyList<string>> TopK(
        IReadOnlyList<IReadOnlyList<string>> ranked, int k)
        => ranked.Take(k);

    private static void GuardArguments(
        IReadOnlyList<string> relevant,
        IReadOnlyList<IReadOnlyList<string>> rankedRetrievedKeys,
        int k)
    {
        ArgumentNullException.ThrowIfNull(rankedRetrievedKeys);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        GuardRelevant(relevant);
    }

    private static void GuardRelevant(IReadOnlyList<string> relevant)
    {
        ArgumentNullException.ThrowIfNull(relevant);
        if (relevant.Count == 0)
        {
            throw new ArgumentException(
                "Retrieval metrics need at least one relevant ref — the evaluator skips " +
                "(null metrics) cases without ground truth instead of calling this.",
                nameof(relevant));
        }
    }

    private static string Normalize(string value)
    {
        var normalized = value.Replace('\\', '/').Trim();
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }
}
