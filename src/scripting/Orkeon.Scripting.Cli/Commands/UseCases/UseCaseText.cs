using System.Globalization;
using System.Text;

namespace Orkeon.Scripting.Cli.Commands.UseCases;

/// <summary>
/// The one normalization the use-case index and every query go through (STUDIO-38 D-01):
/// lowercase, accents folded, Chinese cut into character bigrams.
/// <para>
/// <c>Bm25Index</c> matches terms by exact spelling, and its tokenizer keeps runs of letters or
/// digits, lowercased — nothing more. Fed raw text, "resume" never finds its accented spelling,
/// and a Chinese sentence, written without spaces, is one single term that no other sentence can
/// share. The fix has to be on both sides at once: whatever the index and the query spell
/// differently is a match that silently never happens.
/// </para>
/// <para>
/// No stemming and no stop-word list: the index covers five languages, and a stemmer for one of
/// them is noise for the four others. BM25's inverse document frequency already weighs "de",
/// "the" or "und" down to almost nothing.
/// </para>
/// </summary>
internal static class UseCaseText
{
    /// <summary>
    /// The terms of <paramref name="text"/>: runs of letters or digits, lowercased, accents
    /// removed; a run of Chinese characters becomes its overlapping character pairs (a lone
    /// character stays a term).
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var terms = new List<string>();
        var word = new StringBuilder();
        var han = new StringBuilder();

        foreach (var ch in Fold(text))
        {
            if (IsHan(ch))
            {
                Flush(word, terms);
                han.Append(ch);
            }
            else if (char.IsLetterOrDigit(ch))
            {
                FlushBigrams(han, terms);
                word.Append(ch);
            }
            else
            {
                Flush(word, terms);
                FlushBigrams(han, terms);
            }
        }

        Flush(word, terms);
        FlushBigrams(han, terms);
        return terms;
    }

    /// <summary>
    /// The terms of <paramref name="text"/> separated by single spaces: the form handed to
    /// <c>Bm25Index</c>, whose own tokenizer then splits it back into exactly these terms.
    /// </summary>
    public static string Normalize(string? text) => string.Join(' ', Tokenize(text));

    /// <summary>
    /// Compatibility decomposition (full-width letters become ASCII, ligatures their letters,
    /// an accented letter its base plus a combining mark), marks dropped, lowercase — plus the
    /// few letters of the five languages that carry no decomposable accent.
    /// </summary>
    private static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormKD);
        var folded = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            switch (char.ToLowerInvariant(ch))
            {
                case 'ß':
                    folded.Append("ss");
                    break;
                case 'æ':
                    folded.Append("ae");
                    break;
                case 'œ':
                    folded.Append("oe");
                    break;
                case 'ø':
                    folded.Append('o');
                    break;
                case var lower:
                    folded.Append(lower);
                    break;
            }
        }

        return folded.ToString();
    }

    /// <summary>
    /// CJK unified ideographs, the BMP blocks simplified Chinese is written with. A character
    /// outside the BMP arrives as a surrogate pair, which <c>Bm25Index</c> would split anyway:
    /// it separates terms here too, so both tokenizers keep agreeing.
    /// </summary>
    internal static bool IsHan(char ch) => ch is (>= '\u4E00' and <= '\u9FFF') or (>= '\u3400' and <= '\u4DBF');

    private static void Flush(StringBuilder word, List<string> terms)
    {
        if (word.Length == 0)
            return;

        terms.Add(word.ToString());
        word.Clear();
    }

    private static void FlushBigrams(StringBuilder han, List<string> terms)
    {
        if (han.Length == 1)
            terms.Add(han.ToString());

        for (var i = 0; i + 1 < han.Length; i++)
            terms.Add(han.ToString(i, 2));

        han.Clear();
    }
}
