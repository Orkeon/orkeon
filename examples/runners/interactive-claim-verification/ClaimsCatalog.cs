using System.Text.RegularExpressions;

namespace Orkeon.Examples.Interactive.ClaimVerification;

/// <summary>
/// One claim entry on disk: 3-digit slot, slug, and absolute path.
/// </summary>
public sealed record ClaimEntry(string Slot, string Slug, string FilePath);

/// <summary>
/// Read-only view over <c>inputs/claims/claim-NNN-&lt;slug&gt;.md</c> files plus
/// the experiment-level paths the commands need (config, settings, rounds root).
/// </summary>
/// <remarks>
/// // EXCEPTION-BOOTSTRAP — runs before the per-verify VFS is built; uses
/// physical paths to enumerate the claim corpus so commands can resolve a
/// slot to an on-disk file before mounting it for the kickoff host.
/// </remarks>
public sealed class ClaimsCatalog
{
    private static readonly Regex ClaimFileRegex = new(
        @"^claim-(?<slot>\d{3})-(?<slug>.+)\.md$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ClaimsCatalog(
        string configPath,
        string? settingsPath,
        string claimsRoot,
        string roundsRoot,
        int verbose,
        bool llmLogEnabled)
    {
        ConfigPath = configPath;
        SettingsPath = settingsPath;
        ClaimsRoot = claimsRoot;
        RoundsRoot = roundsRoot;
        Verbose = verbose;
        LlmLogEnabled = llmLogEnabled;
    }

    public string ConfigPath { get; }
    public string? SettingsPath { get; }
    public string ClaimsRoot { get; }
    public string RoundsRoot { get; }
    public int Verbose { get; }
    public bool LlmLogEnabled { get; }

    /// <summary>
    /// Enumerate all claim entries on disk, sorted by slot ascending.
    /// </summary>
    public IReadOnlyList<ClaimEntry> Discover()
    {
        if (!Directory.Exists(ClaimsRoot))
            return Array.Empty<ClaimEntry>();

        var entries = new List<ClaimEntry>();
        foreach (var file in Directory.EnumerateFiles(ClaimsRoot, "claim-*.md"))
        {
            var name = Path.GetFileName(file);
            var match = ClaimFileRegex.Match(name);
            if (!match.Success) continue;
            entries.Add(new ClaimEntry(
                Slot: match.Groups["slot"].Value,
                Slug: match.Groups["slug"].Value,
                FilePath: file));
        }
        return entries
            .OrderBy(e => e.Slot, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Normalises a user-supplied slot token (e.g. "1", "001", "claim-001-foo").
    /// Returns null when the slot does not match a file on disk.
    /// </summary>
    public ClaimEntry? Resolve(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        string? slot = null;
        if (int.TryParse(raw, out var n) && n >= 0 && n < 1000)
        {
            slot = n.ToString("D3");
        }
        else
        {
            var m = Regex.Match(raw, @"^claim-(\d{3})-", RegexOptions.IgnoreCase);
            if (m.Success) slot = m.Groups[1].Value;
        }
        if (slot is null) return null;

        return Discover().FirstOrDefault(e => e.Slot == slot);
    }

    /// <summary>
    /// Compute the next free 3-digit slot (max + 1, padded). Used by add-claim.
    /// </summary>
    public string NextFreeSlot()
    {
        var entries = Discover();
        if (entries.Count == 0) return "001";
        var max = entries.Max(e => int.Parse(e.Slot));
        return (max + 1).ToString("D3");
    }
}
