namespace Orkeon.Analysis.Core.Lexical;

/// <summary>
/// Code-aware tokenizer for the lexical half of hybrid code search. This is the reason
/// the lexical index lives in Analysis instead of reusing the RAG's prose
/// <c>Bm25Index</c> verbatim: a prose tokenizer treats <c>getUserById</c> as one opaque
/// token, so the query "user id" never matches it. Identifiers are split on case and
/// separator boundaries and BOTH the sub-tokens and the whole identifier are emitted —
/// exact-identifier queries keep their strong signal while concept queries gain recall.
/// </summary>
public static class CodeTokenizer
{
    /// <summary>
    /// Tokenizes <paramref name="text"/>: split on non-alphanumerics (including
    /// <c>/</c>, <c>::</c>, <c>.</c> — path and FQN separators), then split each word on
    /// camelCase / digit boundaries. Emits the lowercased sub-tokens plus the lowercased
    /// whole word when it was composite. Single characters are dropped (pure noise in
    /// code: loop variables, operators' neighbours).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "Search-term normalization, not display: BM25 postings and queries must agree on ONE case, and lowercase is the conventional form for inverted-index terms (matches the RAG Bm25Index).")]
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var tokens = new List<string>();
        foreach (var word in SplitWords(text))
        {
            var parts = SplitIdentifier(word);
            foreach (var part in parts)
            {
                if (part.Length > 1) tokens.Add(part.ToLowerInvariant());
            }
            // The whole identifier keeps exact matches strong — but only when it was
            // actually composite, otherwise it would double-count every plain word.
            if (parts.Count > 1 && word.Length > 1) tokens.Add(word.ToLowerInvariant());
        }
        return tokens;
    }

    private static IEnumerable<string> SplitWords(string text)
    {
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsLetterOrDigit(text[i]))
            {
                if (start < 0) start = i;
                continue;
            }
            if (start >= 0)
            {
                yield return text[start..i];
                start = -1;
            }
        }
        if (start >= 0) yield return text[start..];
    }

    /// <summary>
    /// Splits one word on camelCase humps, ALLCAPS→Camel transitions and letter/digit
    /// boundaries: <c>getUserById</c> → [get, User, By, Id]; <c>HTTPClient2</c> →
    /// [HTTP, Client, 2].
    /// </summary>
    private static List<string> SplitIdentifier(string word)
    {
        var parts = new List<string>();
        if (word.Length == 0) return parts;

        var start = 0;
        for (var i = 1; i < word.Length; i++)
        {
            var prev = word[i - 1];
            var cur = word[i];
            var boundary =
                (char.IsLower(prev) && char.IsUpper(cur)) ||
                (char.IsLetter(prev) && char.IsDigit(cur)) ||
                (char.IsDigit(prev) && char.IsLetter(cur)) ||
                // ALLCAPS run ending before a Camel hump: "HTTPClient" → HTTP | Client.
                (char.IsUpper(prev) && char.IsUpper(cur) && i + 1 < word.Length && char.IsLower(word[i + 1]));
            if (boundary)
            {
                parts.Add(word[start..i]);
                start = i;
            }
        }
        parts.Add(word[start..]);
        return parts;
    }
}
