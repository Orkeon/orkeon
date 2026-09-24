using System.Text.Json;
using Orkeon.Constants.Protocol;
using Orkeon.Studio.Core.Events;

namespace Orkeon.Studio.Core.UseCases;

/// <summary>
/// The catalogue as <c>usecases list --events jsonl</c> answered it — every sheet, in the
/// manifest's order. Studio reads it and never recomputes anything the CLI owns: the search, and
/// the normalization it runs on, stay on the CLI's side.
/// </summary>
public sealed class UseCaseCatalog
{
    private readonly Dictionary<string, UseCase> _byId;

    /// <summary>A catalogue over <paramref name="useCases"/>, in the order given.</summary>
    public UseCaseCatalog(IReadOnlyList<UseCase> useCases, IReadOnlyList<string>? languages = null)
    {
        ArgumentNullException.ThrowIfNull(useCases);

        UseCases = useCases;
        Languages = languages ?? UseCaseLanguages.All;
        _byId = new Dictionary<string, UseCase>(StringComparer.Ordinal);
        foreach (var useCase in useCases)
            _byId.TryAdd(useCase.Id, useCase);
    }

    /// <summary>Every sheet, in the manifest's order.</summary>
    public IReadOnlyList<UseCase> UseCases { get; }

    /// <summary>The languages the sheets are written in.</summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>How many use cases the catalogue holds — the N of « browse the use cases (N) ».</summary>
    public int Count => UseCases.Count;

    /// <summary>The sheet of <paramref name="id"/>, or null when the catalogue has none.</summary>
    public UseCase? Find(string? id) =>
        id is not null && _byId.TryGetValue(id, out var useCase) ? useCase : null;

    /// <summary>
    /// Reads a <c>usecases.catalog</c> line. A sheet that cannot be read — missing its id or its
    /// category, or of the wrong shape — is skipped rather than failing the whole list: a gallery
    /// showing 104 of 105 cases beats one showing none. False when the line is not a catalogue.
    /// </summary>
    public static bool TryRead(OrkeonEvent catalogEvent, out UseCaseCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(catalogEvent);

        catalog = null;
        if (!string.Equals(catalogEvent.Kind, UseCaseEventKinds.Catalog, StringComparison.Ordinal)
            || !catalogEvent.Root.TryGetProperty("useCases", out var sheets)
            || sheets.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var useCases = new List<UseCase>(sheets.GetArrayLength());
        foreach (var sheet in sheets.EnumerateArray())
        {
            if (TryReadSheet(sheet) is { } useCase)
                useCases.Add(useCase);
        }

        catalog = new UseCaseCatalog(useCases, catalogEvent.GetStrings("languages") is { Count: > 0 } languages ? languages : null);
        return true;
    }

    private static UseCase? TryReadSheet(JsonElement sheet)
    {
        if (sheet.ValueKind != JsonValueKind.Object)
            return null;

        try
        {
            var useCase = sheet.Deserialize<UseCase>();
            return useCase is { Id.Length: > 0, Category.Length: > 0 } ? useCase.Completed() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
