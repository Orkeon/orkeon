using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Scripting.Cli.Commands.UseCases;

/// <summary>How the language of a query was settled.</summary>
internal enum UseCaseLanguageSource
{
    /// <summary>Named by the caller: <c>--lang</c>, or the <c>lang</c> of a session query.</summary>
    Option,

    /// <summary>Read from the query itself.</summary>
    Detected,

    /// <summary>Neither named nor recognisable: <see cref="UseCaseLanguages.Fallback"/>.</summary>
    Default,
}

/// <summary>
/// The five languages the use-case sheets are written in, and the recognition of a query's
/// language when the caller does not name it.
/// <para>
/// The language matters twice: it picks the title an answer shows, and it decides whether
/// meaning is fused into the search — the local embedding model reads English only (D-02).
/// Detection is deliberately small: a Chinese character settles it, otherwise the function words
/// and the accented letters each language cannot do without cast votes. A few keywords with none
/// of either ("kyc aml") stay undetected, and fall back to English, the language technical
/// keywords are usually typed in.
/// </para>
/// </summary>
internal static class UseCaseLanguages
{
    /// <summary>French.</summary>
    public const string French = "fr";

    /// <summary>English.</summary>
    public const string English = "en";

    /// <summary>Spanish.</summary>
    public const string Spanish = "es";

    /// <summary>German.</summary>
    public const string German = "de";

    /// <summary>Simplified Chinese.</summary>
    public const string SimplifiedChinese = "zh-Hans";

    /// <summary>The language of a query nobody named and nothing gives away.</summary>
    public const string Fallback = English;

    /// <summary>The five languages, in the manifest's order — which is also the order that breaks a tie.</summary>
    public static IReadOnlyList<string> All { get; } = [French, English, Spanish, German, SimplifiedChinese];

    // Spelled as the index folds them: lowercase, accents removed. A word two languages share
    // votes for both; the other words decide.
    private static readonly (string Language, HashSet<string> Words)[] FunctionWords =
    [
        (French, Words(
            "je j tu il elle on nous vous ils elles le la les l un une des du de d au aux et ou pour avec sans "
            + "sur dans chaque mes mon ma nos notre vos votre leur leurs ce cette ces qui que qu est sont veux "
            + "voudrais faut besoin faire tous toutes tout par pas ne en se sa son ses plusieurs mais aussi chez "
            + "tres quand dont")),
        (English, Words(
            "i me my we our you your the an of to and or for with without on in into from by every each that "
            + "this these those is are be want need how what which it its all about their them they before after "
            + "when also but where very should can will")),
        (Spanish, Words(
            "yo mi mis tu tus su sus nuestro nuestra nuestros el la los las un una unos unas de del al y o para "
            + "por con sin en cada que quiero necesito es son como lo se le les este esta estos estas todos todas "
            + "muy mas tambien pero cuando donde")),
        (German, Words(
            "ich du er sie es wir ihr mein meine meiner meinen meinem unser unsere der die das den dem des ein "
            + "eine einen einem einer und oder fur mit ohne von vom zu zum zur auf im in aus bei nach jeden jede "
            + "jeder ist sind mochte will brauche wie was nicht alle bis uber auch aber wenn noch sich sehr")),
    ];

    // Letters that, before folding, only one of the four Latin-script languages uses.
    private static readonly (string Language, string Letters)[] TelltaleLetters =
    [
        (French, "çœèêàùâîôûëïÿ"),
        (Spanish, "ñ¿¡áíóú"),
        (German, "äöüß"),
    ];

    /// <summary>
    /// Reads a language code in any case; <c>zh</c> is accepted for <c>zh-Hans</c>, the only
    /// Chinese the sheets are written in.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out string? language)
    {
        language = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "zh", StringComparison.OrdinalIgnoreCase))
        {
            language = SimplifiedChinese;
            return true;
        }

        language = All.FirstOrDefault(code => string.Equals(code, trimmed, StringComparison.OrdinalIgnoreCase));
        return language is not null;
    }

    /// <summary>The language <paramref name="text"/> is written in, or null when nothing gives it away.</summary>
    public static string? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var terms = UseCaseText.Tokenize(text);
        if (terms.Any(term => UseCaseText.IsHan(term[0])))
            return SimplifiedChinese;

        var votes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            foreach (var (language, words) in FunctionWords)
            {
                if (words.Contains(term))
                    votes[language] = votes.GetValueOrDefault(language) + 1;
            }
        }

        foreach (var ch in text)
        {
            var lower = char.ToLowerInvariant(ch);
            foreach (var (language, letters) in TelltaleLetters)
            {
                if (letters.Contains(lower, StringComparison.Ordinal))
                    votes[language] = votes.GetValueOrDefault(language) + 1;
            }
        }

        if (votes.Count == 0)
            return null;

        // A tie goes to the first language of the manifest's order — except that a text written
        // without a single accented letter is far likelier English than French: "on" is both.
        var best = votes.Values.Max();
        var tied = All.Where(language => votes.GetValueOrDefault(language) == best).ToList();
        return tied.Count > 1 && tied.Contains(English) && text.All(char.IsAscii) ? English : tied[0];
    }

    private static HashSet<string> Words(string words) =>
        new(words.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
}
