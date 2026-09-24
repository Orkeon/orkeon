using System.Globalization;

namespace Orkeon.Studio.Wpf.ViewModels.Common;

/// <summary>
/// How a search box of Studio reads what is typed — the use-case gallery's (STUDIO-39) and My
/// teams' (STUDIO-32), one rule for both: every word typed must start a word of the text, an accent
/// typed or not, a capital or not. Chinese is written without spaces, so a word of it is found
/// anywhere in the text. Studio compares with the platform's collation, loosened, and never folds
/// a spelling itself — the one normalization the use cases have is the CLI's, for its own search
/// (STUDIO-24, STUDIO-38).
/// </summary>
internal static class TextSearch
{
    private static readonly CompareInfo Collation = CultureInfo.InvariantCulture.CompareInfo;

    private const CompareOptions Loose =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType;

    /// <summary>The runs of letters or digits of <paramref name="text"/>, as typed: a query's words, or a text's.</summary>
    public static List<string> Words(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var words = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var letter = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (letter && start < 0)
            {
                start = i;
            }
            else if (!letter && start >= 0)
            {
                words.Add(text[start..i]);
                start = -1;
            }
        }

        return words;
    }

    /// <summary>Whether every word of <paramref name="query"/> is found in <paramref name="text"/>; a query without a word finds everything.</summary>
    /// <param name="query">The words typed, read by <see cref="Words"/>.</param>
    /// <param name="text">What a reader of the card can see.</param>
    public static bool Finds(IReadOnlyList<string> query, string text)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(text);

        if (query.Count == 0)
            return true;

        var words = Words(text);
        return query.All(word => IsChinese(word)
            ? Collation.IndexOf(text, word, Loose) >= 0
            : words.Any(textWord => Collation.IsPrefix(textWord, word, Loose)));
    }

    /// <summary>Whether <paramref name="word"/> holds a CJK ideograph.</summary>
    private static bool IsChinese(string word) =>
        word.Any(ch => ch is (>= '一' and <= '鿿') or (>= '㐀' and <= '䶿'));
}
