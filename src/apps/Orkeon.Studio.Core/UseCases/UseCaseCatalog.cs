using System.Text.Json;
using Orkeon.Constants.Protocol;
using Orkeon.Studio.Core.Events;

namespace Orkeon.Studio.Core.UseCases;

/// <summary>
/// The catalogue as <c>usecases list --events jsonl</c> answered it — every sheet, in the
/// manifest's order — and the one statistic Studio computes on it: how many sheets share a term.
/// </summary>
public sealed class UseCaseCatalog
{
    /// <summary>
    /// The share of the catalogue a term may appear in and still be called rare: three use cases in
    /// a hundred. Measured on the 105 sheets against the golden queries of STUDIO-38: the words that
    /// carry a need — <c>veille</c>, <c>factures</c>, <c>mails</c> — sit at one to three sheets,
    /// while the words every sentence is made of reach a fifth of the catalogue and more.
    /// </summary>
    public const int RarePercent = 3;

    private readonly Dictionary<string, UseCase> _byId;
    private readonly Lazy<Dictionary<string, int>> _frequencies;

    /// <summary>A catalogue over <paramref name="useCases"/>, in the order given.</summary>
    public UseCaseCatalog(IReadOnlyList<UseCase> useCases, IReadOnlyList<string>? languages = null)
    {
        ArgumentNullException.ThrowIfNull(useCases);

        UseCases = useCases;
        Languages = languages ?? UseCaseLanguages.All;
        _byId = new Dictionary<string, UseCase>(StringComparer.Ordinal);
        foreach (var useCase in useCases)
            _byId.TryAdd(useCase.Id, useCase);

        _frequencies = new Lazy<Dictionary<string, int>>(CountFrequencies);
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
    /// How many sheets contain <paramref name="term"/> (a normalized term, as the CLI answers it)
    /// in the text the CLI indexes: titles and problems in every language, tags, tools, and the
    /// words of the category and of the id.
    /// </summary>
    public int DocumentFrequency(string term) =>
        term is not null && _frequencies.Value.TryGetValue(term, out var count) ? count : 0;

    /// <summary>The most sheets a rare term may appear in: <see cref="RarePercent"/> of the catalogue, never less than one.</summary>
    public int RareLimit => Math.Max(1, Count * RarePercent / 100);

    /// <summary>
    /// Whether at least one sheet and at most <see cref="RareLimit"/> contain <paramref name="term"/>.
    /// A term no sheet contains is not rare either: it means Studio and the CLI spelled it
    /// differently, and a suggestion built on a misread term would be noise.
    /// </summary>
    public bool IsRare(string term)
    {
        var frequency = DocumentFrequency(term);
        return frequency > 0 && frequency <= RareLimit;
    }

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

    private Dictionary<string, int> CountFrequencies()
    {
        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var useCase in UseCases)
        {
            foreach (var term in UseCaseTerms.Tokenize(string.Join(' ', IndexedText(useCase))).Distinct(StringComparer.Ordinal))
                frequencies[term] = frequencies.GetValueOrDefault(term) + 1;
        }

        return frequencies;
    }

    /// <summary>
    /// What the CLI's index reads of a sheet — the CLI's <c>IndexedText</c>: titles and problems in
    /// every language, tags, tools, and the words of the category and of the id.
    /// </summary>
    internal static IEnumerable<string> IndexedText(UseCase useCase) =>
        useCase.Title.Values
            .Concat(useCase.Problem.Values)
            .Concat(useCase.Tags)
            .Concat(useCase.Tools)
            .Append(Words(useCase.Category))
            .Append(Words(useCase.Id));

    /// <summary>A folder name as words, its number dropped: <c>03-finance-trading</c> reads "finance trading".</summary>
    internal static string Words(string folderName)
    {
        var dash = folderName.IndexOf('-', StringComparison.Ordinal);
        var name = dash > 0 && folderName[..dash].All(char.IsAsciiDigit) ? folderName[(dash + 1)..] : folderName;
        return name.Replace('-', ' ');
    }
}
