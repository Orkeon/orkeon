using System.Text.Json.Serialization;

namespace Orkeon.Scripting.Cli.Commands.UseCases;

/// <summary>
/// One entry of <c>examples/usecases.json</c> (STUDIO-36), under the manifest's own field names:
/// the JSON the CLI reads is the JSON it writes back on the event stream, so a reader of either
/// learns one vocabulary.
/// </summary>
internal sealed record UseCase
{
    /// <summary>The crew format of an example written as a TypeScript crew.</summary>
    public const string ScriptFormat = "ork.ts";

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
    public required string Format { get; init; }

    /// <summary>The orchestration mode of the crew.</summary>
    [JsonPropertyName("process")]
    public required string Process { get; init; }

    /// <summary>Number of agents the crew declares.</summary>
    [JsonPropertyName("agents")]
    public int Agents { get; init; }

    /// <summary>Number of tasks the crew declares.</summary>
    [JsonPropertyName("tasks")]
    public int Tasks { get; init; }

    /// <summary>The tools its agents use, sorted.</summary>
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

    /// <summary>The title, by language code; a value may still be empty.</summary>
    [JsonPropertyName("title")]
    public IReadOnlyDictionary<string, string> Title { get; init; } = new Dictionary<string, string>();

    /// <summary>The problem it solves, phrased as a user states a need, by language code.</summary>
    [JsonPropertyName("problem")]
    public IReadOnlyDictionary<string, string> Problem { get; init; } = new Dictionary<string, string>();

    /// <summary>Free keywords.</summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The mounts the crew expects, relative to its folder (<c>./data:/data:ro</c>).</summary>
    [JsonPropertyName("mounts")]
    public IReadOnlyList<string> Mounts { get; init; } = [];

    /// <summary>
    /// False for a reference-only example (DC-3): the finance crews depend on a shared
    /// <c>_tools/</c> folder the CLI does not carry — searchable, readable, not importable.
    /// </summary>
    [JsonPropertyName("importable")]
    public bool Importable { get; init; }

    /// <summary>The crew file's name inside the example folder.</summary>
    [JsonIgnore]
    public string CrewFileName => Format == ScriptFormat ? "main.ork.ts" : "config.yaml";

    /// <summary>
    /// The title in <paramref name="language"/>, else in English, else in French, else in any
    /// language that has one; empty when no language has one yet.
    /// </summary>
    public string TitleIn(string language) => InLanguage(Title, language);

    /// <summary>The problem in <paramref name="language"/>, with the fall-back of <see cref="TitleIn"/>.</summary>
    public string ProblemIn(string language) => InLanguage(Problem, language);

    private static string InLanguage(IReadOnlyDictionary<string, string> texts, string language)
    {
        foreach (var candidate in (string[])[language, UseCaseLanguages.English, UseCaseLanguages.French])
        {
            if (texts.TryGetValue(candidate, out var text) && !string.IsNullOrWhiteSpace(text))
                return text;
        }

        return texts.Values.FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty;
    }
}

/// <summary>One file the CLI embeds for an example, by its path inside the example folder.</summary>
/// <param name="Path">Relative, with forward slashes: <c>config.yaml</c>, <c>data/orders.csv</c>.</param>
/// <param name="Length">Its size in bytes.</param>
internal sealed record UseCaseFile(string Path, long Length);
