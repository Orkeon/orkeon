using System.Text.Json;
using Orkeon.Constants.Protocol;
using Orkeon.Studio.Core.Events;

namespace Orkeon.Studio.Core.UseCases;

/// <summary>Why a use case is in an answer, as the CLI says it.</summary>
public enum UseCaseMatchReason
{
    /// <summary>It shares terms with the query (<c>terms</c>).</summary>
    Terms,

    /// <summary>It shares no term; the local model finds it close in meaning (<c>meaning</c>).</summary>
    Meaning,

    /// <summary>Both (<c>terms+meaning</c>).</summary>
    TermsAndMeaning,
}

/// <summary>One use case of an answer, best first.</summary>
public sealed record UseCaseMatch
{
    /// <summary>1 for the best.</summary>
    public required int Rank { get; init; }

    /// <summary>The use case's id.</summary>
    public required string Id { get; init; }

    /// <summary>The CLI's score: comparable within one answer only, never across two.</summary>
    public double Score { get; init; }

    /// <summary>Terms, meaning, or both.</summary>
    public UseCaseMatchReason Reason { get; init; }

    /// <summary>The query's terms the sheet contains, normalized; empty for a match by meaning alone.</summary>
    public IReadOnlyList<string> Terms { get; init; } = [];

    /// <summary>
    /// In hybrid mode, the cosine between the query and the sheet — NOT a usable cutoff: a
    /// nonsense query still gets matches by meaning alone, with similarities in the same range.
    /// </summary>
    public double? Similarity { get; init; }

    /// <summary>The title in the query's language, when the answer carried one.</summary>
    public string? Title { get; init; }
}

/// <summary>The answer to one search (<c>usecases.results</c>).</summary>
public sealed record UseCaseAnswer
{
    /// <summary>The query as asked.</summary>
    public required string Query { get; init; }

    /// <summary>The language the CLI took the query for.</summary>
    public string Language { get; init; } = "";

    /// <summary><c>bm25</c> or <c>hybrid</c>.</summary>
    public string Mode { get; init; } = "";

    /// <summary>Why meaning was given up for this answer, when it was; null otherwise.</summary>
    public string? Degraded { get; init; }

    /// <summary>The use cases, best first.</summary>
    public required IReadOnlyList<UseCaseMatch> Matches { get; init; }

    /// <summary>
    /// Reads a <c>usecases.results</c> line. A result without an id is skipped; an unknown reason
    /// reads as <see cref="UseCaseMatchReason.Meaning"/> — the reading that never makes a
    /// suggestion. False when the line is not an answer.
    /// </summary>
    public static bool TryRead(OrkeonEvent resultsEvent, out UseCaseAnswer? answer)
    {
        ArgumentNullException.ThrowIfNull(resultsEvent);

        answer = null;
        if (!string.Equals(resultsEvent.Kind, UseCaseEventKinds.Results, StringComparison.Ordinal))
            return false;

        var matches = new List<UseCaseMatch>();
        if (resultsEvent.Root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var result in results.EnumerateArray())
            {
                if (ReadMatch(result, matches.Count + 1) is { } match)
                    matches.Add(match);
            }
        }

        answer = new UseCaseAnswer
        {
            Query = resultsEvent.GetString("query") ?? "",
            Language = resultsEvent.GetString("lang") ?? "",
            Mode = resultsEvent.GetString("mode") ?? "",
            Degraded = resultsEvent.GetString("degraded"),
            Matches = matches,
        };
        return true;
    }

    private static UseCaseMatch? ReadMatch(JsonElement result, int position)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
            || id.GetString() is not { Length: > 0 } identifier)
        {
            return null;
        }

        return new UseCaseMatch
        {
            Rank = Int(result, "rank") ?? position,
            Id = identifier,
            Score = Double(result, "score") ?? 0,
            Reason = Text(result, "reason") switch
            {
                "terms" => UseCaseMatchReason.Terms,
                "terms+meaning" => UseCaseMatchReason.TermsAndMeaning,
                _ => UseCaseMatchReason.Meaning,
            },
            Terms = Strings(result, "terms"),
            Similarity = Double(result, "similarity"),
            Title = Text(result, "title"),
        };
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static double? Double(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;

    private static List<string> Strings(JsonElement element, string name)
    {
        var items = new List<string>();
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
                items.Add(text);
        }

        return items;
    }
}

/// <summary>
/// Which use cases of an answer are close enough to a need to be suggested (STUDIO-39, D-03) —
/// the rule behind « N close use cases » under the need box.
/// <para>
/// A match counts when it shares with the need at least one DISTINCTIVE term: a rare one
/// (<see cref="UseCaseCatalog.IsRare"/>: at most three use cases in a hundred contain it), long
/// enough to be a word of the need rather than of the sentence — four characters or more in a
/// sentence, three in a keyword search of one or two words (<c>kyc</c>, <c>etl</c>), any length in
/// Chinese, whose terms are character pairs.
/// </para>
/// <para>
/// Measured against the real catalogue and the CLI's answers: a match by meaning alone never
/// counts — in hybrid mode a nonsense query still gets five of them, with similarities in the same
/// range as a real one — and "shares terms" alone is not enough either: a plain French sentence
/// shares <c>de</c>, <c>un</c> or <c>mes</c> with almost every sheet, so all five matches of a
/// sentence about a sick cat answered "terms". The rarity keeps the words that carry a need, the
/// length floor the short ones the catalogue happens to hold rarely (<c>est</c>, <c>il</c>). When
/// no match qualifies, nothing is suggested — no hint rather than noise.
/// </para>
/// </summary>
public static class UseCaseSuggestions
{
    /// <summary>A need of at most this many terms is a keyword search: its short terms are keywords.</summary>
    public const int KeywordSearchTerms = 2;

    /// <summary>In a sentence, a shorter term is a function word far more often than a need (<c>est</c>, <c>are</c>).</summary>
    public const int SentenceMinimumLength = 4;

    /// <summary>In a keyword search, a three-letter term is a keyword (<c>kyc</c>, <c>seo</c>).</summary>
    public const int KeywordMinimumLength = 3;

    /// <summary>The matches of <paramref name="answer"/> worth suggesting, best first; only those the catalogue knows.</summary>
    public static IReadOnlyList<UseCaseMatch> Close(UseCaseAnswer answer, UseCaseCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(catalog);

        var minimumLength = UseCaseTerms.Tokenize(answer.Query).Count <= KeywordSearchTerms
            ? KeywordMinimumLength
            : SentenceMinimumLength;

        return
        [
            .. answer.Matches.Where(match =>
                match.Reason != UseCaseMatchReason.Meaning
                && catalog.Find(match.Id) is not null
                && match.Terms.Any(term => IsDistinctive(term, catalog, minimumLength))),
        ];
    }

    /// <summary>Whether <paramref name="term"/> is rare in <paramref name="catalog"/> and long enough to carry a need.</summary>
    public static bool IsDistinctive(string term, UseCaseCatalog catalog, int minimumLength)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return !string.IsNullOrEmpty(term)
            && (UseCaseTerms.IsChinese(term) || term.Length >= minimumLength)
            && catalog.IsRare(term);
    }
}
