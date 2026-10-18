using System.Globalization;
using System.Text;

namespace Orkeon.Domain.FileSystem;

/// <summary>
/// The folder name a free text becomes: lowercase ASCII letters and digits, accents dropped,
/// one dash between words, at most <see cref="MaxLength"/> characters cut at a word. One
/// implementation for the CLI and Studio (STUDIO-24), because both name folders from the same
/// words — a forge session, an adopted team — and the two rules they used to follow disagreed
/// on the cap, the cut and the fallback: a name could not be trusted to give the folder the
/// other side would compute for it.
/// <para>
/// The rule answers nothing when nothing usable remains — a name written in a non-Latin
/// script, or punctuation alone — and leaves the fallback to its caller: a team or an agent
/// key falls back on <see cref="TeamFallback"/>, a forge session on its timestamp. A
/// fallback chosen here would be wrong for one of them.
/// </para>
/// </summary>
public static class FolderSlug
{
    /// <summary>
    /// Longest slug <see cref="From"/> produces. A slug is a folder name and Windows' MAX_PATH
    /// is a shared budget: a goal-length sentence must never become a 200-character directory.
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// The folder name of a team, and the key of an agent, whose name keeps no usable
    /// character. A collision with an existing folder is its caller's to settle, like any
    /// other.
    /// </summary>
    public const string TeamFallback = "equipe";

    /// <summary>
    /// The slug of <paramref name="text"/>, or null when it keeps no ASCII letter or digit.
    /// </summary>
    public static string? From(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // FormD splits an accented letter into its base letter and a combining mark; dropping
        // the marks keeps the word, so an accented title still reads in its folder name.
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(Math.Min(decomposed.Length, MaxLength + 1));
        var afterDash = true;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                afterDash = false;
            }
            else if (!afterDash)
            {
                builder.Append('-');
                afterDash = true;
            }

            // One character past the cap is all the cut needs to see: a dash there means a
            // word ends exactly at the cap, a letter means the last word crosses it.
            if (builder.Length > MaxLength)
                break;
        }

        var slug = builder.ToString().TrimEnd('-');
        if (slug.Length > MaxLength)
        {
            // Back up to the last dash inside the window while that keeps at least half the
            // cap; a single overlong word is cut where the cap falls.
            var cut = slug.LastIndexOf('-', MaxLength);
            slug = slug[..(cut >= MaxLength / 2 ? cut : MaxLength)].TrimEnd('-');

            // A cut slug never ends on a stop word (#118): « ...-et-rediger-a » reads as a
            // sentence cut in its middle. A slug the cap did not cut keeps every word.
            while (slug.LastIndexOf('-') is var last and > 0 && IsStopWord(slug[(last + 1)..]))
                slug = slug[..last];
        }

        return slug.Length > 0 ? slug : null;
    }

    /// <summary>
    /// Whether <paramref name="word"/> is an article, a preposition or a conjunction of one of
    /// the languages of Studio's interface (fr, en, es, de) — a word a name or a folder cut by
    /// a cap must not end on. Case and accents do not matter: an accented "a" is "a".
    /// </summary>
    public static bool IsStopWord(string? word) =>
        From(word) is { } slug && StopWords.Contains(slug);

    /// <summary>The stop words, in slug form (lowercase ASCII).</summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        // fr
        "a", "au", "aux", "d", "de", "des", "du", "l", "la", "le", "les", "et", "ou", "en", "un", "une",
        "pour", "par", "sur", "dans", "avec", "sans", "que", "qui", "ce", "ces", "se", "y",
        // en
        "an", "the", "of", "and", "or", "to", "in", "on", "for", "with", "by", "at", "from", "into", "as",
        // es
        "el", "los", "las", "del", "o", "una", "para", "por", "con", "al",
        // de
        "der", "die", "das", "den", "dem", "und", "oder", "zu", "im", "am", "an", "auf", "mit", "fur",
        "von", "ein", "eine", "einen", "zum", "zur",
    };
}
