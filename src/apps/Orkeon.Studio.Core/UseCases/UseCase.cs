using System.Text.Json.Serialization;

namespace Orkeon.Studio.Core.UseCases;

/// <summary>
/// One use case of the example catalogue (STUDIO-36), as <c>orkeon usecases list --events jsonl</c>
/// writes it: the manifest's own field names, so a reader of the manifest and a reader of the
/// stream learn one vocabulary. Studio never reads the manifest itself — the catalogue comes from
/// the CLI that embeds it (STUDIO-39, D-05), like every other answer the wizard shows.
/// </summary>
public sealed record UseCase
{
    private static readonly IReadOnlyDictionary<string, string> NoText = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The example's folder name, unique across the catalogue (<c>03-email-pipeline</c>).</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The category folder it lives in (<c>01-enterprise</c>).</summary>
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    /// <summary>The example's number in the catalogue.</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }

    /// <summary><c>yaml</c> or <c>ork.ts</c>.</summary>
    [JsonPropertyName("format")]
    public string Format { get; init; } = "";

    /// <summary>The orchestration mode of the crew, as the engine spells it (<c>sequential</c>).</summary>
    [JsonPropertyName("process")]
    public string Process { get; init; } = "";

    /// <summary>Number of agents the crew declares.</summary>
    [JsonPropertyName("agents")]
    public int Agents { get; init; }

    /// <summary>Number of tasks the crew declares.</summary>
    [JsonPropertyName("tasks")]
    public int Tasks { get; init; }

    /// <summary>The tools its agents use.</summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>Whether the example ships a <c>data/</c> folder.</summary>
    [JsonPropertyName("hasSampleData")]
    public bool HasSampleData { get; init; }

    /// <summary>Whether one of its tools reaches the network.</summary>
    [JsonPropertyName("requiresNetwork")]
    public bool RequiresNetwork { get; init; }

    /// <summary>The environment variables of the third-party keys it needs; empty when none.</summary>
    [JsonPropertyName("requiresKeys")]
    public IReadOnlyList<string> RequiresKeys { get; init; } = [];

    /// <summary>The title, by catalogue language code (<c>fr</c>, <c>en</c>, <c>es</c>, <c>de</c>, <c>zh-Hans</c>).</summary>
    [JsonPropertyName("title")]
    public IReadOnlyDictionary<string, string> Title { get; init; } = NoText;

    /// <summary>The problem it solves, phrased the way a user states a need, by language code.</summary>
    [JsonPropertyName("problem")]
    public IReadOnlyDictionary<string, string> Problem { get; init; } = NoText;

    /// <summary>Free keywords.</summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The mounts the crew expects, relative to its folder (<c>./data:/data:ro</c>).</summary>
    [JsonPropertyName("mounts")]
    public IReadOnlyList<string> Mounts { get; init; } = [];

    /// <summary>
    /// False for a reference-only example (DC-3): its crew depends on files the CLI does not
    /// carry. It stays browsable and usable as a reference (STUDIO-39, D-06) — it only cannot be
    /// imported as it is.
    /// </summary>
    [JsonPropertyName("importable")]
    public bool Importable { get; init; }

    /// <summary>
    /// The title in <paramref name="language"/> (a catalogue code, or Studio's <c>zh</c>), else in
    /// English, else in French, else in any language that has one — the CLI's own fall-back, so a
    /// title reads the same on both sides; empty when no language has one.
    /// </summary>
    public string TitleIn(string? language) => InLanguage(Title, language);

    /// <summary>The problem in <paramref name="language"/>, with the fall-back of <see cref="TitleIn"/>.</summary>
    public string ProblemIn(string? language) => InLanguage(Problem, language);

    /// <summary>
    /// The same sheet with every list and text table present: a <c>null</c> on the wire reads as
    /// empty, so no caller has to guard a collection the protocol promises.
    /// </summary>
    internal UseCase Completed() => this with
    {
        Format = Format ?? "",
        Process = Process ?? "",
        Tools = Tools ?? [],
        RequiresKeys = RequiresKeys ?? [],
        Title = Title ?? NoText,
        Problem = Problem ?? NoText,
        Tags = Tags ?? [],
        Mounts = Mounts ?? [],
    };

    private static string InLanguage(IReadOnlyDictionary<string, string>? texts, string? language)
    {
        if (texts is null || texts.Count == 0)
            return "";

        foreach (var candidate in (string[])[UseCaseLanguages.FromUiLanguage(language), UseCaseLanguages.English, UseCaseLanguages.French])
        {
            if (texts.TryGetValue(candidate, out var text) && !string.IsNullOrWhiteSpace(text))
                return text;
        }

        return texts.Values.FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "";
    }
}

/// <summary>
/// The five languages the sheets are written in, and the one mapping Studio needs: its own
/// language switch says <c>zh</c> where the catalogue says <c>zh-Hans</c> (the only Chinese the
/// sheets are written in).
/// </summary>
public static class UseCaseLanguages
{
    /// <summary>French.</summary>
    public const string French = "fr";

    /// <summary>English — the fall-back of a language the catalogue does not know.</summary>
    public const string English = "en";

    /// <summary>Spanish.</summary>
    public const string Spanish = "es";

    /// <summary>German.</summary>
    public const string German = "de";

    /// <summary>Simplified Chinese.</summary>
    public const string SimplifiedChinese = "zh-Hans";

    /// <summary>The five, in the manifest's order.</summary>
    public static IReadOnlyList<string> All { get; } = [French, English, Spanish, German, SimplifiedChinese];

    /// <summary>
    /// The catalogue code of a UI language: <c>zh</c> becomes <c>zh-Hans</c>, any case is read, and
    /// an empty or unknown code is English — the language a sheet falls back on anyway.
    /// </summary>
    public static string FromUiLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return English;

        var trimmed = code.Trim();
        if (string.Equals(trimmed, "zh", StringComparison.OrdinalIgnoreCase))
            return SimplifiedChinese;

        return All.FirstOrDefault(language => string.Equals(language, trimmed, StringComparison.OrdinalIgnoreCase)) ?? English;
    }
}
