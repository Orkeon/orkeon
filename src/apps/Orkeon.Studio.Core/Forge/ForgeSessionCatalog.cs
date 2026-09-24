using Orkeon.Constants.FileSystem;
using System.Text.Json;
using Orkeon.Compliance.Vfs;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Studio.Core.Forge;

/// <summary>
/// One entry of "Mes solutions" (UX study §5): a session in progress or a solution
/// already adopted — one list, one notion.
/// </summary>
public sealed record ForgeSolutionSummary
{
    /// <summary>Session slug (level 3; the display name is <see cref="Title"/>).</summary>
    public required string Slug { get; init; }

    /// <summary>
    /// The session's stable id (STUDIO-25), the one a team's <c>forge.json</c> names; null for a
    /// session written before the id existed, or one whose id does not parse — linked to no team.
    /// </summary>
    public Guid? Id { get; init; }

    /// <summary>Display name, derived from the brief's goal; falls back to the slug.</summary>
    public string? Title { get; init; }

    /// <summary>Engine state, as <c>session.json</c> spells it.</summary>
    public required string State { get; init; }

    /// <summary>Session lifecycle, as <c>session.json</c> spells it.</summary>
    public required string Status { get; init; }

    /// <summary>Render format.</summary>
    public string? Format { get; init; }

    /// <summary>Last save instant (UTC, ISO-8601) — the list sorts on it.</summary>
    public string? UpdatedAt { get; init; }

    /// <summary>Absolute destination of the promotion, when the solution was adopted.</summary>
    public string? PromotedTo { get; init; }

    /// <summary>
    /// What rule R (<see cref="TeamSessionLink"/>) reads of this session: its id and its
    /// <c>promotedTo</c>; null without an id, which links it to no team (D-06).
    /// </summary>
    public SessionPromotion? Promotion => Id is { } id ? new SessionPromotion(id, PromotedTo) : null;

    /// <summary>Absolute session directory.</summary>
    public required string Directory { get; init; }

    /// <summary>An adopted solution with a known folder: "Relancer" through the ordinary launcher.</summary>
    public bool CanRelaunch =>
        string.Equals(Status, "Promoted", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(PromotedTo);

    /// <summary>A session the cycle can pick up exactly where it stopped: "Reprendre".</summary>
    public bool CanResume =>
        string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, "BudgetExhausted", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, "Ready", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What Studio reads of a team's <c>forge.json</c> (<see cref="ForgeSessionCatalog.ReadTeamRecord"/>).</summary>
/// <param name="SessionId">The id of the session it names (STUDIO-25); null when none or unreadable.</param>
/// <param name="HasInstalledSchedule">Whether its <c>schedule</c> block records an installation (STUDIO-27).</param>
public sealed record TeamForgeRecord(Guid? SessionId, bool HasInstalledSchedule)
{
    /// <summary>No record, or one that could not be read.</summary>
    public static TeamForgeRecord None { get; } = new(null, false);

    /// <summary>
    /// When the folder was promoted — its <c>promotedAt</c>, one of the three dates a team's last
    /// activity is read from (STUDIO-31, D-05); null when absent or not a date.
    /// </summary>
    public DateTimeOffset? PromotedAt { get; init; }
}

/// <summary>
/// Reads the workspace's forge sessions straight off the disk —
/// <c>.orkeon/forge/&lt;slug&gt;/session.json</c>, the layout SPEC-ORKEON-FORGE §4.1 fixes.
/// Tolerant: a corrupt session appears with what could be read, never blocks the list (the
/// CLI's own <c>forge list</c> discipline). Reading is all it did until <c>Delete</c>, which
/// discards an abandoned draft — the one write, and it removes rather than produces. The file shape is re-declared
/// here because Studio.Core does not reference the CLI; <c>ForgeSessionCatalogTests</c>
/// pins it against a verbatim fixture — and so is the <c>id</c> of a team's <c>forge.json</c>,
/// the one field of that record Studio reads.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application reading — and, for an abandoned draft, " +
    "deleting — the CLI's session files on the physical disk, before any VFS mount exists. " +
    "The write is a recursive delete of one session directory the user asked to discard; it " +
    "never touches crew data, which is why it stays outside the VFS like the rest of this type.")]
public static class ForgeSessionCatalog
{
    /// <summary>Session root, relative to a workspace — the CLI's spelling.</summary>
    public const string RootDirectoryName = ".orkeon/forge";

    /// <summary>Name of the session document inside a session directory.</summary>
    public const string SessionFileName = "session.json";

    /// <summary>
    /// Name of the record <c>forge promote</c> writes into a team folder — the CLI's
    /// <c>forge.json</c>, whose <c>id</c> names the team's session (STUDIO-25).
    /// </summary>
    public const string TeamRecordFileName = "forge.json";

    /// <summary>Lists the workspace's sessions, most recently touched first.</summary>
    public static IReadOnlyList<ForgeSolutionSummary> List(string workspaceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);

        var root = Path.Combine(workspaceDirectory, ConventionalNames.StateDirectory, "forge");
        if (!System.IO.Directory.Exists(root))
            return [];

        var summaries = new List<ForgeSolutionSummary>();
        foreach (var directory in System.IO.Directory.EnumerateDirectories(root))
        {
            if (TryRead(directory, out var summary))
                summaries.Add(summary!);
        }

        return [.. summaries.OrderByDescending(s => s.UpdatedAt, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Deletes a session directory, recursively. Returns false when the disk refused.
    /// <para>
    /// An interrupted creation is a draft, and a draft the user abandons has to be
    /// removable from the screen that lists it — otherwise « Sessions en cours » only
    /// ever grows. Deliberately tolerant, like <c>TeamCatalog.Delete</c>: losing the
    /// gesture is a nuisance, a crash on a locked folder is not.
    /// </para>
    /// </summary>
    public static bool Delete(string sessionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);

        try
        {
            System.IO.Directory.Delete(sessionDirectory, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// The session whose id is <paramref name="id"/>, or null (STUDIO-25). Two sessions only share
    /// an id when a session directory was copied by hand; the most recently touched one answers
    /// then — the CLI's pick too. Which session a TEAM is linked to is not asked here: « Modify »
    /// asks the engine (<c>forge reopen</c>), which applies rule R.
    /// </summary>
    public static ForgeSolutionSummary? FindById(string workspaceDirectory, Guid id) =>
        id == Guid.Empty
            ? null
            : List(workspaceDirectory)
                .Where(summary => summary.Id == id)
                .OrderByDescending(summary => summary.UpdatedAt, StringComparer.Ordinal)
                .ThenBy(summary => summary.Slug, StringComparer.Ordinal)
                .FirstOrDefault();

    /// <summary>
    /// The id of the session <paramref name="teamDirectory"/>'s <c>forge.json</c> names
    /// (STUDIO-25) — read-only: the CLI writes the record. Null when the record, or its id, is
    /// absent, unreadable or not an id; never a throw.
    /// </summary>
    public static Guid? ReadTeamSessionId(string teamDirectory) => ReadTeamRecord(teamDirectory).SessionId;

    /// <summary>
    /// What Studio reads of <paramref name="teamDirectory"/>'s <c>forge.json</c>, in one read: the id
    /// of its session (STUDIO-25), and whether its <c>schedule</c> block records an installation
    /// (STUDIO-27) — a team whose registration the engine must be asked to remove before the folder
    /// goes. Read-only, tolerant: an absent or unreadable record reads as neither.
    /// </summary>
    public static TeamForgeRecord ReadTeamRecord(string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        try
        {
            var path = Path.Combine(teamDirectory, TeamRecordFileName);
            if (!File.Exists(path))
                return TeamForgeRecord.None;

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return TeamForgeRecord.None;

            var installed = root.TryGetProperty("schedule", out var schedule)
                && schedule.ValueKind == JsonValueKind.Object
                && schedule.TryGetProperty("installed", out var block)
                && block.ValueKind == JsonValueKind.Object;
            return new TeamForgeRecord(ReadId(root), installed) { PromotedAt = ReadPromotedAt(root) };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return TeamForgeRecord.None;
        }
    }

    /// <summary>
    /// The session rule R links <paramref name="teamDirectory"/> to (STUDIO-25) — the one a deletion
    /// of the team may take with it (STUDIO-27, D-07). Null when none is linked: no id, an id no
    /// session carries, or a COPY — whose id names its original's session, which deleting the copy
    /// must never touch.
    /// </summary>
    public static ForgeSolutionSummary? LinkedSession(string workspaceDirectory, string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);

        return LinkedSession(List(workspaceDirectory), teamDirectory);
    }

    /// <summary>
    /// <see cref="LinkedSession(string, string)"/> among <paramref name="sessions"/> as a screen
    /// already listed them: the most recently touched carrying the team's id — the order
    /// <see cref="FindById"/> answers in — then rule R.
    /// </summary>
    public static ForgeSolutionSummary? LinkedSession(IEnumerable<ForgeSolutionSummary> sessions, string teamDirectory)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        var id = ReadTeamSessionId(teamDirectory);
        var session = id is { } known
            ? sessions
                .Where(summary => summary.Id == known)
                .OrderByDescending(summary => summary.UpdatedAt, StringComparer.Ordinal)
                .ThenBy(summary => summary.Slug, StringComparer.Ordinal)
                .FirstOrDefault()
            : null;
        return TeamSessionLink.IsLinked(TeamSessionLink.Resolve(teamDirectory, id, session?.Promotion, ReadTeamSessionId))
            ? session
            : null;
    }

    /// <summary>
    /// The orphan sessions of the workspace (STUDIO-27, D-08): adopted ones — status
    /// <c>Promoted</c>, so listed nowhere else — whose <c>promotedTo</c> folder no longer exists.
    /// A team moved or renamed within <paramref name="teamsRoot"/> is found by the id its
    /// <c>forge.json</c> carries, and its session is not an orphan: rule R links them. One moved
    /// anywhere else cannot be told from a deleted one — the list is only a list, and nothing
    /// in it is deleted without the user.
    /// </summary>
    public static IReadOnlyList<ForgeSolutionSummary> FindOrphans(string workspaceDirectory, string? teamsRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);

        var carried = teamsRoot is { Length: > 0 } root ? IdsCarriedUnder(root) : [];
        return
        [
            .. List(workspaceDirectory).Where(session =>
                string.Equals(session.Status, "Promoted", StringComparison.OrdinalIgnoreCase)
                && session.PromotedTo is { Length: > 0 } promotedTo
                && !System.IO.Directory.Exists(promotedTo)
                && !(session.Id is { } id && carried.Contains(id))),
        ];
    }

    /// <summary>The session ids the team folders under <paramref name="teamsRoot"/> carry.</summary>
    private static HashSet<Guid> IdsCarriedUnder(string teamsRoot)
    {
        var ids = new HashSet<Guid>();
        try
        {
            if (!System.IO.Directory.Exists(teamsRoot))
                return ids;

            foreach (var team in System.IO.Directory.EnumerateDirectories(teamsRoot))
            {
                if (ReadTeamSessionId(team) is { } id)
                    ids.Add(id);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A root that cannot be read claims nothing: every session reads as it stands.
        }

        return ids;
    }

    private static bool TryRead(string directory, out ForgeSolutionSummary? summary)
    {
        summary = null;
        var path = Path.Combine(directory, SessionFileName);
        if (!File.Exists(path))
            return false;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            summary = new ForgeSolutionSummary
            {
                Slug = ReadString(root, "slug") ?? Path.GetFileName(directory),
                Id = ReadId(root),
                Title = ReadString(root, "title"),
                State = ReadString(root, "state") ?? "",
                Status = ReadString(root, "status") ?? "",
                Format = ReadString(root, "format"),
                UpdatedAt = ReadString(root, "updatedAt"),
                PromotedTo = ReadString(root, "promotedTo"),
                Directory = directory,
            };
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A broken session must not block the list; the CLI's `forge list` tolerates
            // the same way.
            return false;
        }
    }

    /// <summary>The <c>id</c> of a session or team record; <see cref="Guid.Empty"/> is no id.</summary>
    private static Guid? ReadId(JsonElement element) =>
        Guid.TryParse(ReadString(element, "id"), out var id) && id != Guid.Empty ? id : null;

    /// <summary>The record's <c>promotedAt</c> — UTC ISO-8601, as the CLI writes it — or null when absent or not a date.</summary>
    private static DateTimeOffset? ReadPromotedAt(JsonElement element) =>
        DateTimeOffset.TryParse(
            ReadString(element, "promotedAt"),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var promotedAt)
            ? promotedAt
            : null;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
