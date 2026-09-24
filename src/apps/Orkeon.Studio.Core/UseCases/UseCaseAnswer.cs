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
/// Among the first <see cref="Shown"/> matches, one counts when it shares with the need at least
/// one DISTINCTIVE term: a rare one — carried by at most three use cases in a hundred of the
/// catalogue (<see cref="RarePercent"/>) — and long enough to be a word of the need rather than of
/// the sentence: four characters or more in a sentence, three in a keyword search of one or two
/// words (<c>kyc</c>, <c>etl</c>), any length in Chinese, whose terms are character pairs. When no
/// match qualifies, nothing is suggested — no hint rather than noise.
/// </para>
/// <para>
/// How many use cases carry a term is read off the answer, never recomputed on Studio's side: the
/// wizard asks for the whole catalogue (<c>top</c> = its size), and an answer by terms lists every
/// sheet that shares a term with the need, each with every term it shares, spelled by the CLI's
/// own normalization. In hybrid mode (English) the terms half of the fusion is the best twenty
/// sheets — where the sheets of a rare term rank anyway.
/// </para>
/// <para>
/// Measured against the real catalogue: a match by meaning alone never counts — in hybrid mode a
/// nonsense query still gets five of them, with similarities in the same range as a real one — and
/// "shares terms" alone is not enough either: a plain French sentence shares <c>de</c>, <c>un</c>
/// or <c>mes</c> with almost every sheet, so all five matches of a sentence about a sick cat
/// answered "terms". Rarity keeps the words that carry a need; the length floor drops the short
/// ones the catalogue happens to hold rarely (<c>est</c>, <c>il</c>).
/// </para>
/// </summary>
public static class UseCaseSuggestions
{
    /// <summary>How many of the best matches may be suggested.</summary>
    public const int Shown = 5;

    /// <summary>
    /// The share of the catalogue a rare term appears in at most: three use cases in a hundred. On
    /// the 105 sheets, the words that carry a need — <c>veille</c>, <c>factures</c>, <c>mails</c> —
    /// sit at one to three, while the words every sentence is made of reach a fifth and more.
    /// </summary>
    public const int RarePercent = 3;

    /// <summary>A need of at most this many words is a keyword search: its short terms are keywords.</summary>
    public const int KeywordSearchWords = 2;

    /// <summary>In a sentence, a shorter term is a function word far more often than a need (<c>est</c>, <c>are</c>).</summary>
    public const int SentenceMinimumLength = 4;

    /// <summary>In a keyword search, a three-letter term is a keyword (<c>kyc</c>, <c>seo</c>).</summary>
    public const int KeywordMinimumLength = 3;

    /// <summary>The most use cases a rare term appears in, for a catalogue of <paramref name="catalogueSize"/>: never less than one.</summary>
    public static int RareLimit(int catalogueSize) => Math.Max(1, catalogueSize * RarePercent / 100);

    /// <summary>
    /// The matches of <paramref name="answer"/> worth suggesting, best first — only those the
    /// catalogue knows. <paramref name="answer"/> must be an answer for the whole catalogue
    /// (<c>top</c> at least its size), or the rarity it reads is a guess.
    /// </summary>
    public static IReadOnlyList<UseCaseMatch> Close(UseCaseAnswer answer, UseCaseCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(catalog);

        var carriers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var term in answer.Matches.SelectMany(match => match.Terms.Distinct(StringComparer.Ordinal)))
            carriers[term] = carriers.GetValueOrDefault(term) + 1;

        var rareLimit = RareLimit(catalog.Count);
        var minimumLength = WordCount(answer.Query) <= KeywordSearchWords ? KeywordMinimumLength : SentenceMinimumLength;

        return
        [
            .. answer.Matches
                .Take(Shown)
                .Where(match =>
                    match.Reason != UseCaseMatchReason.Meaning
                    && catalog.Find(match.Id) is not null
                    && match.Terms.Any(term => carriers[term] <= rareLimit && IsLongEnough(term, minimumLength))),
        ];
    }

    /// <summary>A Chinese term is a character pair: no floor applies to it.</summary>
    private static bool IsLongEnough(string term, int minimumLength) =>
        term.Length >= minimumLength || (term.Length > 0 && IsHan(term[0]));

    /// <summary>
    /// The words of the need as it was typed: runs of letters or digits. Only their number matters —
    /// one or two is a keyword search — so no spelling is normalized here.
    /// </summary>
    private static int WordCount(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var ch in text)
        {
            var letter = char.IsLetterOrDigit(ch);
            if (letter && !inWord)
                count++;
            inWord = letter;
        }

        return count;
    }

    /// <summary>CJK unified ideographs, the blocks simplified Chinese is written with — where the CLI cuts pairs.</summary>
    private static bool IsHan(char ch) => ch is (>= '\u4E00' and <= '\u9FFF') or (>= '\u3400' and <= '\u4DBF');
}
