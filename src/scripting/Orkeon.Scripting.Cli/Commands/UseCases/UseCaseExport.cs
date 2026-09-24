using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Commands.UseCases;

/// <summary>What <c>orkeon usecases export</c> wrote (STUDIO-41): the <c>usecases.exported</c> answer.</summary>
internal sealed record UseCaseExport
{
    /// <summary>The use case written.</summary>
    public required UseCase UseCase { get; init; }

    /// <summary>The team folder, absolute.</summary>
    public required string Destination { get; init; }

    /// <summary>The team's name: the title in <see cref="Language"/>, else the id.</summary>
    public required string Name { get; init; }

    /// <summary>The language the name and the description were read in.</summary>
    public required string Language { get; init; }

    /// <summary>
    /// Every file written, relative to the folder with forward slashes, in writing order: the
    /// crew first, the sample data, the sidecar last.
    /// </summary>
    public required IReadOnlyList<string> Files { get; init; }

    /// <summary>The folders created empty for a mount to land in, relative: <c>output</c>.</summary>
    public required IReadOnlyList<string> Folders { get; init; }
}

/// <summary>
/// Writes one use case as a team folder (STUDIO-41, D-02) — the shape of a promoted team, less
/// what only a workshop session can say. <c>crew/</c> holds the example's crew file byte for
/// byte; <c>data/</c> its sample data; a folder stands behind each team-relative mount of the
/// manifest (<c>output/</c> for every crew that writes files); and <c>studio-team.json</c> names
/// the team, describes it and records those mounts the way Studio records its own
/// (<c>./data:/data:ro</c>). No <c>forge.json</c> and no launchers: no session made this team,
/// and <c>forge reopen</c> rebuilds one from <c>crew/</c> the first time the team is modified.
/// Only the manifest's mounts are recorded — an input folder the manifest does not declare is
/// not invented here.
/// </summary>
internal static class UseCaseExporter
{
    /// <summary>The team folder's sub-folder holding the crew — the layout <c>forge promote</c> writes.</summary>
    public const string CrewDirectoryName = ForgeYamlRenderer.CrewDirectoryName;

    /// <summary>The sample data's folder, in an example and in its team alike.</summary>
    public const string DataDirectoryName = "data";

    /// <summary>What a team-relative physical segment starts with — Studio's sidecar convention.</summary>
    public const string RelativePrefix = "./";

    private static readonly JsonSerializerOptions SidecarOptions = new()
    {
        WriteIndented = true,
        // A local file people open, never HTML: a title keeps its accents and its characters.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The crew file's name under <c>crew/</c>: <c>config.yaml</c>, the settings file of a YAML
    /// crew directory — or the scripting convention for a script crew.
    /// </summary>
    public static string TeamCrewFileName(UseCase useCase)
    {
        ArgumentNullException.ThrowIfNull(useCase);
        return useCase.Format == UseCase.ScriptFormat ? ForgeScriptRenderer.ScriptFileName : ConventionalNames.CrewSettingsFile;
    }

    /// <summary>
    /// Writes <paramref name="useCase"/> into <paramref name="destination"/> — absolute, and a
    /// folder that does not exist yet or is empty, which the caller checked. A write the disk
    /// refuses halfway takes back what this export wrote: no half team is left to be imported.
    /// </summary>
    public static UseCaseExport Export(UseCaseCatalog catalog, UseCase useCase, string destination, string language)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(useCase);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        var existed = Directory.Exists(destination);
        try
        {
            return Write(catalog, useCase, destination, language);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            TakeBack(destination, existed);
            throw;
        }
    }

    private static UseCaseExport Write(UseCaseCatalog catalog, UseCase useCase, string destination, string language)
    {
        Directory.CreateDirectory(destination);
        var files = new List<string>();

        // The crew as the example has it — its bytes, never a re-serialization.
        var crewFile = $"{CrewDirectoryName}/{TeamCrewFileName(useCase)}";
        Copy(catalog, useCase.Id, useCase.CrewFileName, Path.Combine(destination, CrewDirectoryName, TeamCrewFileName(useCase)));
        files.Add(crewFile);

        foreach (var data in catalog.FilesOf(useCase.Id).Where(file => IsData(file.Path)))
        {
            Copy(catalog, useCase.Id, data.Path, Path.Combine([destination, .. data.Path.Split('/')]));
            files.Add(data.Path);
        }

        // A mount whose folder is missing is fatal when the runner builds its host: the folder
        // behind every team-relative mount exists before the first launch.
        var folders = new List<string>();
        foreach (var folder in useCase.Mounts.Select(TeamFolderOf).OfType<string>())
        {
            var path = Path.Combine([destination, .. folder.Split('/')]);
            if (Directory.Exists(path))
                continue;

            Directory.CreateDirectory(path);
            folders.Add(folder);
        }

        var name = useCase.TitleIn(language) is { Length: > 0 } title ? title : useCase.Id;
        var description = useCase.ProblemIn(language);
        var sidecar = new Sidecar(
            name,
            description.Length > 0 ? description : null,
            useCase.Mounts.Count > 0 ? useCase.Mounts : null);
        File.WriteAllText(
            Path.Combine(destination, ConventionalNames.TeamSidecarFile),
            JsonSerializer.Serialize(sidecar, SidecarOptions));
        files.Add(ConventionalNames.TeamSidecarFile);

        return new UseCaseExport
        {
            UseCase = useCase,
            Destination = destination,
            Name = name,
            Language = language,
            Files = files,
            Folders = folders,
        };
    }

    /// <summary>
    /// The folder a team-relative mount names — <c>data</c> for <c>./data:/data:ro</c> — or null
    /// for anything else: an absolute or unreadable entry, and a <c>./</c> segment that would
    /// leave the team (<c>./../x</c>) or names no folder. Studio reads the same rule.
    /// </summary>
    internal static string? TeamFolderOf(string mount)
    {
        if (FileSystemMount.TryGetBasePath(mount) is not { } physical
            || !physical.StartsWith(RelativePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var folder = physical[RelativePrefix.Length..].Replace('\\', '/');
        return folder.Length > 0
            && !Path.IsPathRooted(folder)
            && folder.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."))
            ? folder
            : null;
    }

    private static bool IsData(string path) =>
        path.StartsWith(DataDirectoryName + "/", StringComparison.Ordinal);

    private static void Copy(UseCaseCatalog catalog, string id, string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var input = catalog.OpenFile(id, source);
        using var output = File.Create(target);
        input.CopyTo(output);
    }

    /// <summary>
    /// Undoes a failed export: the folder it created goes, and a folder that was there — empty,
    /// by the caller's check — is emptied again. Best effort: the first error is the one reported.
    /// </summary>
    private static void TakeBack(string destination, bool existed)
    {
        try
        {
            if (!Directory.Exists(destination))
                return;

            if (!existed)
            {
                Directory.Delete(destination, recursive: true);
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(destination))
                Directory.Delete(directory, recursive: true);
            foreach (var file in Directory.EnumerateFiles(destination))
                File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The export's own failure is what the user needs to read, not this one.
        }
    }

    /// <summary>
    /// <c>studio-team.json</c>, under the names Studio's <c>StudioTeamMetadata</c> reads: the CLI
    /// and Studio cannot reference each other, so the shape is pinned by a golden test on each side.
    /// </summary>
    private sealed record Sidecar(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("mounts")] IReadOnlyList<string>? Mounts);
}
