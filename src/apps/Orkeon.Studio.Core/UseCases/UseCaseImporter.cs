using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.UseCases;

/// <summary>A use case imported as a team (STUDIO-41), or why it was not.</summary>
public sealed record UseCaseImportResult
{
    /// <summary>The new team folder, under the teams root; null when <see cref="Failure"/> says why.</summary>
    public string? TeamPath { get; init; }

    /// <summary>Why no team landed — the CLI's refusal, a missing engine, the import's own refusal; null on success.</summary>
    public UseCaseFailure? Failure { get; init; }
}

/// <summary>
/// « Import as is » (STUDIO-41): a use case becomes a team without the workshop. The CLI writes it
/// as a team folder — <c>usecases export</c>, into a staging folder of its own — and the folder
/// enters the teams root through the import every other team takes (<see cref="TeamCatalog.Import"/>:
/// the launcher's own detector first, then the copy, the sidecar read back and normalized). The
/// staging folder goes either way. Which name the team takes is settled before, by the caller: the
/// title proposed here, or — when something already holds its folder — the free twin the adoption
/// offers too (STUDIO-26, D-07).
/// </summary>
public static class UseCaseImporter
{
    /// <summary>
    /// The name a use case is imported under: its title in <paramref name="language"/> — one line,
    /// within the cap of every team name — else its id.
    /// </summary>
    public static string TeamNameOf(UseCase useCase, string? language)
    {
        ArgumentNullException.ThrowIfNull(useCase);
        return useCase.TitleIn(language) is { Length: > 0 } title ? TeamCatalog.NormalizeName(title) : useCase.Id;
    }

    /// <summary>
    /// The folder <paramref name="name"/> gives under <paramref name="teamsRoot"/>, by the folder rule
    /// of every team (<see cref="FolderSlug"/>). A name the rule keeps nothing of — a title written
    /// in Chinese — falls back on the use case's id, which reads better than the team fallback.
    /// </summary>
    public static string TeamFolderOf(UseCase useCase, string name, string teamsRoot)
    {
        ArgumentNullException.ThrowIfNull(useCase);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamsRoot);

        return Path.Combine(teamsRoot, FolderSlug.From(name) ?? FolderSlug.From(useCase.Id) ?? FolderSlug.TeamFallback);
    }

    /// <summary>
    /// Exports <paramref name="useCaseId"/> and imports it as <paramref name="teamFolder"/>, named
    /// <paramref name="teamName"/> — a folder the caller found free. What the CLI answered and what
    /// the disk refused are typed failures, and none leaves anything in the teams root.
    /// </summary>
    public static async Task<UseCaseImportResult> ImportAsync(
        UseCaseClient client,
        string useCaseId,
        string teamName,
        string teamFolder,
        string teamsRoot,
        string? language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(useCaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamName);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamsRoot);

        // The import names the team's folder after the folder it copies: the export is staged
        // under the very name the team's folder takes.
        var staging = Path.Combine(Path.GetTempPath(), $"orkeon-usecase-{Guid.NewGuid():N}");
        var exported = Path.Combine(staging, Path.GetFileName(Path.TrimEndingDirectorySeparator(teamFolder)));
        try
        {
            var export = await client.ExportAsync(useCaseId, exported, language, cancellationToken).ConfigureAwait(false);
            if (export.Failure is { } failure)
                return new UseCaseImportResult { Failure = failure };

            // What was staged is what enters — never a path the answer names: the import copies
            // a whole folder into the teams root.
            var team = TeamCatalog.Import(exported, teamsRoot, out var refusal);
            if (team is null)
            {
                return new UseCaseImportResult
                {
                    Failure = new UseCaseFailure(
                        UseCaseFailureKind.NotImported,
                        refusal ?? "the teams folder refused the copy — check access to it and try again"),
                };
            }

            // The CLI named the team after its title; a taken name's free twin is Studio's word.
            if (TeamCatalog.Describe(team).Metadata is { } metadata
                && !string.Equals(metadata.Name, teamName, StringComparison.Ordinal))
            {
                TeamCatalog.SaveMetadata(team, metadata with { Name = teamName });
            }

            return new UseCaseImportResult { TeamPath = team };
        }
        finally
        {
            // Tolerant: a staging folder the disk keeps is the OS's temp to clean, not a failure.
            TeamCatalog.Delete(staging);
        }
    }
}
