using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Orkeon.Scripting.Cli.Commands.UseCases;

/// <summary>
/// Where the catalogue's files come from, each named <c>&lt;category&gt;/&lt;id&gt;/&lt;path&gt;</c>
/// and the manifest <see cref="UseCaseCatalog.ManifestPath"/>. The CLI reads them from its own
/// assembly; a test hands in a few from memory.
/// </summary>
internal interface IUseCaseFileSource
{
    /// <summary>Every file the source holds.</summary>
    IReadOnlyCollection<string> Paths { get; }

    /// <summary>Opens one file, or returns null when the source does not hold it.</summary>
    Stream? Open(string path);
}

/// <summary>
/// The catalogue embedded in the <c>orkeon</c> tool (STUDIO-38 D-03): the manifest, each
/// example's crew file and its <c>data/</c> folder, as resources named <c>usecases/…</c>.
/// <para>
/// The examples are not shipped anywhere else — the installers copy a sample settings file and
/// nothing from <c>examples/</c> — so the tool carries what the search, the Atelier and the
/// import need, and nothing more: the finance examples' shared <c>_tools/</c> stays out (DC-3).
/// Resource names are read with either separator: MSBuild builds them from the source path, so a
/// Windows build writes backslashes where a Linux build writes slashes.
/// </para>
/// </summary>
internal sealed class EmbeddedUseCaseFileSource : IUseCaseFileSource
{
    /// <summary>The prefix of every resource of the catalogue.</summary>
    public const string ResourcePrefix = "usecases/";

    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _resources = new(StringComparer.Ordinal);

    private EmbeddedUseCaseFileSource(Assembly assembly)
    {
        _assembly = assembly;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var path = name.Replace('\\', '/');
            if (path.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                _resources[path[ResourcePrefix.Length..]] = name;
        }
    }

    /// <summary>The catalogue this build of the CLI carries.</summary>
    public static EmbeddedUseCaseFileSource Instance { get; } = new(typeof(EmbeddedUseCaseFileSource).Assembly);

    /// <inheritdoc />
    public IReadOnlyCollection<string> Paths => _resources.Keys;

    /// <inheritdoc />
    public Stream? Open(string path) =>
        _resources.TryGetValue(path, out var name) ? _assembly.GetManifestResourceStream(name) : null;
}

/// <summary>The error codes of the <c>usecases</c> verb, as the event stream and stderr spell them.</summary>
internal static class UseCaseErrorCodes
{
    /// <summary>No use case of the catalogue has this id.</summary>
    public const string UnknownId = "USECASES-UNKNOWN-ID";

    /// <summary>A session query that cannot be run: no text, a language outside the five, a bad <c>top</c>.</summary>
    public const string QueryInvalid = "USECASES-QUERY-INVALID";

    /// <summary>An option value the verb cannot honour.</summary>
    public const string OptionInvalid = "USECASES-OPTION-INVALID";

    /// <summary>A search that failed while running; the session goes on.</summary>
    public const string SearchFailed = "USECASES-SEARCH-FAILED";

    /// <summary>Anything else that went wrong: the verb ends with the runtime-error exit code.</summary>
    public const string Failed = "USECASES-FAILED";
}

/// <summary>
/// Thrown when an id names no use case of the catalogue — the typed error a caller maps to
/// <see cref="UseCaseErrorCodes.UnknownId"/> before doing anything else with the id.
/// </summary>
internal sealed class UnknownUseCaseException : Exception
{
    /// <summary>The code this error is reported under.</summary>
    public const string Code = UseCaseErrorCodes.UnknownId;

    /// <inheritdoc />
    public UnknownUseCaseException()
    {
    }

    /// <inheritdoc />
    public UnknownUseCaseException(string message)
        : base(message)
    {
    }

    /// <inheritdoc />
    public UnknownUseCaseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The id that matched nothing.</summary>
    public string? UseCaseId { get; init; }

    /// <summary>The error for <paramref name="id"/>, with the message the CLI prints.</summary>
    public static UnknownUseCaseException For(string id) =>
        new($"unknown use case '{id}' ({Code}) — `orkeon usecases list` shows every id.") { UseCaseId = id };
}

/// <summary>
/// The use-case catalogue: the manifest's sheets, and the files the CLI carries for each.
/// </summary>
internal sealed class UseCaseCatalog
{
    /// <summary>The manifest's path in an <see cref="IUseCaseFileSource"/>.</summary>
    public const string ManifestPath = "usecases.json";

    private static readonly Lazy<UseCaseCatalog> EmbeddedCatalog =
        new(() => Load(EmbeddedUseCaseFileSource.Instance), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly IUseCaseFileSource _files;
    private readonly Dictionary<string, UseCase> _byId;

    private UseCaseCatalog(IReadOnlyList<string> languages, IReadOnlyList<UseCase> useCases, IUseCaseFileSource files)
    {
        Languages = languages;
        UseCases = useCases;
        _files = files;
        _byId = useCases.ToDictionary(useCase => useCase.Id, StringComparer.Ordinal);
    }

    /// <summary>The catalogue embedded in this build, read once per process.</summary>
    public static UseCaseCatalog Embedded => EmbeddedCatalog.Value;

    /// <summary>The languages the sheets are written in, in the manifest's order.</summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>Every sheet, in the manifest's order.</summary>
    public IReadOnlyList<UseCase> UseCases { get; }

    /// <summary>Reads the manifest out of <paramref name="files"/>.</summary>
    public static UseCaseCatalog Load(IUseCaseFileSource files)
    {
        ArgumentNullException.ThrowIfNull(files);

        using var stream = files.Open(ManifestPath)
            ?? throw new InvalidOperationException($"The use-case manifest '{ManifestPath}' is missing from this build.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd(), files);
    }

    /// <summary>Builds a catalogue from a manifest's JSON, its files read from <paramref name="files"/>.</summary>
    public static UseCaseCatalog Parse(string manifestJson, IUseCaseFileSource files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestJson);
        ArgumentNullException.ThrowIfNull(files);

        using var manifest = JsonDocument.Parse(manifestJson);
        var root = manifest.RootElement;
        var languages = root.GetProperty("languages").Deserialize<List<string>>() ?? [];
        var useCases = root.GetProperty("useCases").Deserialize<List<UseCase>>() ?? [];
        return new UseCaseCatalog(languages, useCases, files);
    }

    /// <summary>The sheet of <paramref name="id"/>; an unknown id throws <see cref="UnknownUseCaseException"/>.</summary>
    public UseCase Get(string id) =>
        TryGet(id, out var useCase) ? useCase : throw UnknownUseCaseException.For(id);

    /// <summary>The sheet of <paramref name="id"/>, if the catalogue has one.</summary>
    public bool TryGet(string id, [NotNullWhen(true)] out UseCase? useCase) => _byId.TryGetValue(id, out useCase);

    /// <summary>The files carried for <paramref name="id"/>: its crew file first, then its data files.</summary>
    public IReadOnlyList<UseCaseFile> FilesOf(string id)
    {
        var useCase = Get(id);
        var prefix = PrefixOf(useCase);

        return
        [
            .. _files.Paths
                .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
                .Select(path => path[prefix.Length..])
                .OrderBy(path => path == useCase.CrewFileName ? 0 : 1)
                .ThenBy(path => path, StringComparer.Ordinal)
                .Select(path => new UseCaseFile(path, LengthOf(prefix + path))),
        ];
    }

    /// <summary>The text of the crew file of <paramref name="id"/>.</summary>
    public string ReadCrew(string id)
    {
        var useCase = Get(id);
        using var stream = _files.Open(PrefixOf(useCase) + useCase.CrewFileName)
            ?? throw new InvalidOperationException($"The crew file of '{id}' ({useCase.CrewFileName}) is missing from this build.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>Opens one of the files <see cref="FilesOf"/> lists for <paramref name="id"/>.</summary>
    public Stream OpenFile(string id, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var useCase = Get(id);
        return _files.Open(PrefixOf(useCase) + relativePath)
            ?? throw new FileNotFoundException($"'{relativePath}' is not a file of the use case '{id}'.", relativePath);
    }

    private static string PrefixOf(UseCase useCase) => $"{useCase.Category}/{useCase.Id}/";

    private long LengthOf(string path)
    {
        using var stream = _files.Open(path);
        return stream?.Length ?? 0;
    }
}
