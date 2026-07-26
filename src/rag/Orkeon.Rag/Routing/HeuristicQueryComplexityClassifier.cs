using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Rag.Routing;

/// <summary>
/// Deterministic, zero-LLM query-complexity classifier (Adaptive-RAG, guide
/// §8.4). The default classifier — used in tests/CI and on hosts without any
/// LLM — and the documented fallback when <c>Classifier = "llm"</c> is selected
/// but no <c>IChatClient</c> is registered.
/// </summary>
/// <remarks>
/// Rules, applied in order (pure function of the query — same input, same route):
/// <list type="number">
/// <item><description><b>Empty/whitespace query</b> → <see cref="QueryRoute.NoRetrieval"/>
/// (nothing to retrieve against).</description></item>
/// <item><description><b>Greeting / social / opinion opener</b> (hello, hi, thanks,
/// bonjour, merci, "how are you"…) → <see cref="QueryRoute.NoRetrieval"/>.</description></item>
/// <item><description><b>Multi-hop signals</b> — two or more interrogative words,
/// comparative markers (compare, versus, "difference between"…), two or more
/// question marks, or a long query (&gt; 24 words) →
/// <see cref="QueryRoute.Iterative"/>.</description></item>
/// <item><description><b>Short imperative form</b> — at most 4 words, no question
/// mark, no interrogative word (e.g. "summarize this", "translate that text") →
/// <see cref="QueryRoute.NoRetrieval"/>.</description></item>
/// <item><description>Everything else → <see cref="QueryRoute.SingleShot"/>
/// (the safe default: one retrieval pass).</description></item>
/// </list>
/// All comparisons are case-insensitive (ordinal).
/// </remarks>
public sealed class HeuristicQueryComplexityClassifier : IQueryComplexityClassifier
{
    /// <summary>Word count above which a query is considered multi-hop.</summary>
    private const int LongQueryWordCount = 25;

    /// <summary>Maximum word count of the short imperative rule (rule 4).</summary>
    private const int ShortImperativeWordCount = 4;

    // Rule 2 — greeting / social / opinion openers (query starts with one of
    // these, or is exactly one of these).
    private static readonly string[] s_socialOpeners =
    [
        "hello", "hi ", "hi!", "hey", "thanks", "thank you", "good morning",
        "good afternoon", "good evening", "how are you", "what's up", "nice to meet",
        "bonjour", "salut", "bonsoir", "merci", "coucou", "ça va", "ca va",
    ];

    // Rule 3 — interrogative words (counted as whole words, occurrences).
    private static readonly string[] s_interrogativeWords =
    [
        "who", "what", "when", "where", "why", "how", "which", "whom", "whose",
        "qui", "quoi", "quand", "pourquoi", "comment", "combien", "quel", "quelle", "quels", "quelles",
    ];

    // Rule 3 — comparative / multi-document markers (substring match).
    private static readonly string[] s_comparativeMarkers =
    [
        "compare", "comparison", "versus", " vs ", " vs.", "difference between",
        "differences between", "pros and cons", "trade-off", "tradeoff", "step by step",
        "comparer", "comparaison", "différence entre", "différences entre",
    ];

    /// <inheritdoc />
    public Task<QueryRoute> ClassifyAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Classify(query));
    }

    /// <summary>Synchronous core — deterministic rules, no I/O.</summary>
    internal static QueryRoute Classify(string query)
    {
        // Rule 1 — empty query.
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return QueryRoute.NoRetrieval;
        }

#pragma warning disable CA1308 // rule tables are lowercase by construction, not a comparison normalization
        var lowered = trimmed.ToLowerInvariant();
#pragma warning restore CA1308

        // Rule 2 — greeting / social opener.
        if (IsSocialOpener(lowered))
        {
            return QueryRoute.NoRetrieval;
        }

        var words = lowered.Split(
            [' ', '\t', '\r', '\n', ',', ';', ':', '.', '!', '?', '(', ')', '"', '\''],
            StringSplitOptions.RemoveEmptyEntries);
        var interrogativeCount = words.Count(word => s_interrogativeWords.Contains(word, StringComparer.Ordinal));

        // Rule 3 — multi-hop / comparative / long query.
        if (interrogativeCount >= 2
            || s_comparativeMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal))
            || lowered.Count(c => c == '?') >= 2
            || words.Length >= LongQueryWordCount)
        {
            return QueryRoute.Iterative;
        }

        // Rule 4 — short imperative form (no question at all).
        if (words.Length <= ShortImperativeWordCount
            && !lowered.Contains('?', StringComparison.Ordinal)
            && interrogativeCount == 0)
        {
            return QueryRoute.NoRetrieval;
        }

        // Rule 5 — safe default.
        return QueryRoute.SingleShot;
    }

    private static bool IsSocialOpener(string lowered) =>
        s_socialOpeners.Any(opener =>
            lowered.StartsWith(opener, StringComparison.Ordinal)
            || lowered.Equals(opener.TrimEnd(), StringComparison.Ordinal));
}
