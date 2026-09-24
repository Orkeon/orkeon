using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Constants.Protocol;
using Orkeon.Scripting.Cli.Events;

namespace Orkeon.Scripting.Cli.Commands.UseCases;

/// <summary>
/// The use-case verb's view of the shared event protocol: the envelope and the emission come
/// from <see cref="OrkeonEventWriter"/>, this type adds the typed lines of
/// <see cref="UseCaseEventKinds"/>. A session answer carries the query's id in the envelope's
/// <c>correlationId</c> — the field every stream correlates by.
/// </summary>
internal sealed class UseCaseEventWriter : OrkeonEventWriter
{
    /// <summary>Creates a writer over <paramref name="output"/> (stdout in the CLI).</summary>
    public UseCaseEventWriter(TextWriter output, IOrkeonClock? clock = null)
        : base(output, clock)
    {
    }

    /// <summary>Session mode is open: the catalogue size, its languages, the mode of each.</summary>
    public void Ready(UseCaseCatalog catalog, UseCaseSearchPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(policy);

        Emit(UseCaseEventKinds.Ready, new
        {
            count = catalog.UseCases.Count,
            languages = catalog.Languages,
            modes = policy.Modes.ToDictionary(pair => pair.Key, pair => Spell(pair.Value), StringComparer.Ordinal),
        });
    }

    /// <summary>
    /// The answer to one search: its language and mode — and why meaning was given up, when it
    /// was — then for each use case its rank, id, score, reason, the terms it matched on, its
    /// similarity in hybrid mode, and its title in the query's language when one is written.
    /// </summary>
    public void Results(UseCaseAnswer answer, string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var results = new JsonArray();
        foreach (var match in answer.Matches)
        {
            var result = new JsonObject
            {
                ["rank"] = match.Rank,
                ["id"] = match.UseCase.Id,
                ["score"] = Math.Round(match.Score, 4),
                ["reason"] = Spell(match.Reason),
                ["terms"] = new JsonArray([.. match.Terms.Select(term => (JsonNode?)term)]),
            };

            if (match.Similarity is { } similarity)
                result["similarity"] = Math.Round(similarity, 4);

            var title = match.UseCase.TitleIn(answer.Language);
            if (title.Length > 0)
                result["title"] = title;

            results.Add(result);
        }

        Emit(UseCaseEventKinds.Results, Scope(correlationId), new
        {
            query = answer.Query,
            lang = answer.Language,
            langSource = Spell(answer.LanguageSource),
            mode = Spell(answer.Mode),
            degraded = answer.Degraded,
            results,
        });
    }

    /// <summary>The catalogue as <c>list</c> filtered it: every sheet in full, under the manifest's field names.</summary>
    public void Catalog(UseCaseCatalog catalog, IReadOnlyList<UseCase> useCases)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(useCases);

        Emit(UseCaseEventKinds.Catalog, new
        {
            count = useCases.Count,
            languages = catalog.Languages,
            useCases,
        });
    }

    /// <summary>
    /// One sheet, flat like every payload of the protocol, followed by the files the CLI carries
    /// for it and — when asked — the text of its crew file.
    /// </summary>
    public void Sheet(UseCase useCase, IReadOnlyList<UseCaseFile> files, string? crew)
    {
        ArgumentNullException.ThrowIfNull(useCase);
        ArgumentNullException.ThrowIfNull(files);

        var sheet = JsonSerializer.SerializeToNode(useCase)!.AsObject();
        sheet["files"] = new JsonArray([.. files.Select(file => (JsonNode?)new JsonObject
        {
            ["path"] = file.Path,
            ["bytes"] = file.Length,
        })]);

        if (crew is not null)
            sheet["crew"] = crew;

        Emit(UseCaseEventKinds.Sheet, sheet);
    }

    /// <summary>An anomaly; in session mode it answers the query it names, and the session goes on.</summary>
    public void Error(string code, string message, bool recoverable, string? correlationId = null) =>
        Emit(UseCaseEventKinds.Error, Scope(correlationId), new { code, message, recoverable });

    private static OrkeonEventScope Scope(string? correlationId) =>
        correlationId is null ? OrkeonEventScope.None : new OrkeonEventScope { CorrelationId = correlationId };

    /// <summary>The wire spelling of a mode.</summary>
    internal static string Spell(UseCaseSearchMode mode) => mode == UseCaseSearchMode.Hybrid ? "hybrid" : "bm25";

    private static string Spell(UseCaseLanguageSource source) => source switch
    {
        UseCaseLanguageSource.Option => "option",
        UseCaseLanguageSource.Detected => "detected",
        _ => "default",
    };

    private static string Spell(UseCaseMatchReason reason) => reason switch
    {
        UseCaseMatchReason.Terms => "terms",
        UseCaseMatchReason.Meaning => "meaning",
        _ => "terms+meaning",
    };
}
