using System.Globalization;
using System.Text;

namespace Orkeon.Studio.Core.UseCases;

/// <summary>
/// The CLI's search normalization (STUDIO-38 D-01), mirrored: lowercase, accents folded, Chinese
/// cut into character bigrams — what <c>UseCaseText.Tokenize</c> does to a sheet and to a query.
/// <para>
/// Studio needs the same terms for two readings the CLI does not answer. How many use cases share
/// a term — the suggestion rule keeps only the matches on a term few sheets carry, because every
/// French sentence shares <c>de</c> with almost the whole catalogue — and the gallery's own filter,
/// which must find an accented word from its plain spelling the way a search does. A term spelled
/// differently on the two sides would count as shared by no sheet, and such a term never makes a
/// suggestion: the drift fails quiet, never noisy. <c>UseCaseTermsCorpus</c>, checked by both
/// suites, is what keeps it from happening.
/// </para>
/// </summary>
public static class UseCaseTerms
{
    /// <summary>
    /// The terms of <paramref name="text"/>: runs of letters or digits, lowercased, accents removed;
    /// a run of Chinese characters becomes its overlapping character pairs (a lone character stays
    /// a term).
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

    /// <summary>Whether <paramref name="term"/> is Chinese — a bigram or a lone character.</summary>
    public static bool IsChinese(string? term) => !string.IsNullOrEmpty(term) && IsHan(term[0]);

    /// <summary>
    /// Compatibility decomposition (full-width letters become ASCII, ligatures their letters, an
    /// accented letter its base plus a combining mark), marks dropped, lowercase — plus the few
    /// letters of the five languages that carry no decomposable accent.
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

    /// <summary>CJK unified ideographs, the BMP blocks simplified Chinese is written with — the CLI's ranges.</summary>
    private static bool IsHan(char ch) => ch is (>= '一' and <= '鿿') or (>= '㐀' and <= '䶿');

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
