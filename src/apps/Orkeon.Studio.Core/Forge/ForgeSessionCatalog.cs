using System.Text.Json;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.Forge;

/// <summary>
/// One entry of "Mes solutions" (UX study §5): a session in progress or a solution
/// already adopted — one list, one notion.
/// </summary>
public sealed record ForgeSolutionSummary
{
    /// <summary>Session slug (level 3; the display name is <see cref="Title"/>).</summary>
    public required string Slug { get; init; }

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

/// <summary>
/// Reads the workspace's forge sessions straight off the disk —
/// <c>.orkeon/forge/&lt;slug&gt;/session.json</c>, the layout SPEC-ORKEON-FORGE §4.1 fixes.
/// Read-only and tolerant: a corrupt session appears with what could be read, never blocks
/// the list (the CLI's own <c>forge list</c> discipline). The file shape is re-declared
/// here because Studio.Core does not reference the CLI; <c>ForgeSessionCatalogTests</c>
/// pins it against a verbatim fixture.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application reading the CLI's session files on the " +
    "physical disk, before any VFS mount exists.")]
public static class ForgeSessionCatalog
{
    /// <summary>Session root, relative to a workspace — the CLI's spelling.</summary>
    public const string RootDirectoryName = ".orkeon/forge";

    /// <summary>Name of the session document inside a session directory.</summary>
    public const string SessionFileName = "session.json";

    /// <summary>Lists the workspace's sessions, most recently touched first.</summary>
    public static IReadOnlyList<ForgeSolutionSummary> List(string workspaceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);

        var root = Path.Combine(workspaceDirectory, ".orkeon", "forge");
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
    /// The session that adopted <paramref name="teamDirectory"/>, or null — the reverse
    /// lookup «Modifier» rests on (W-09): the sidecar records no session, but every
    /// session records its <c>promotedTo</c>. Path comparison is full-path,
    /// trailing-separator-blind, and case-blind on Windows.
    /// </summary>
    public static ForgeSolutionSummary? FindByPromotedTo(string workspaceDirectory, string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string target;
        try
        {
            target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(teamDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }

        return List(workspaceDirectory).FirstOrDefault(summary =>
            summary.PromotedTo is { Length: > 0 } promoted
            && string.Equals(NormalizeOrNull(promoted), target, comparison));
    }

    /// <summary>A corrupt <c>promotedTo</c> must not take the lookup down — it just never matches.</summary>
    private static string? NormalizeOrNull(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
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

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
