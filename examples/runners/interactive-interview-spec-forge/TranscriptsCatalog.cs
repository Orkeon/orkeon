using System.Text.RegularExpressions;

namespace Orkeon.Examples.Interactive.InterviewSpecForge;

/// <summary>
/// One transcript entry on disk: slot (3-digit), basename (without .txt),
/// interview date, person, subject slug, and the absolute file path.
/// </summary>
public sealed record TranscriptEntry(
    string Slot,
    string Basename,
    DateOnly InterviewDate,
    string Person,
    string SubjectSlug,
    string FilePath);

/// <summary>
/// Read-only view over the experiment's transcript corpus
/// (<c>YYYY-MM-DD_personne_sujet.txt</c>) plus the experiment-level paths the
/// commands need (config, settings, rounds/topics/tasks/glossary roots).
/// </summary>
/// <remarks>
/// // EXCEPTION-BOOTSTRAP — runs before the per-forge VFS is built; uses
/// physical paths to enumerate the transcript corpus so commands can
/// resolve a slot to an on-disk file before mounting it.
/// </remarks>
public sealed class TranscriptsCatalog
{
    private static readonly Regex TranscriptRegex = new(
        @"^(?<date>\d{4}-\d{2}-\d{2})_(?<person>[^_]+)_(?<subject>.+)\.txt$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public TranscriptsCatalog(
        string configPath,
        string? settingsPath,
        string transcriptsRoot,
        string experimentRoot,
        int verbose,
        bool llmLogEnabled)
    {
        ConfigPath = configPath;
        SettingsPath = settingsPath;
        TranscriptsRoot = transcriptsRoot;
        ExperimentRoot = experimentRoot;
        Verbose = verbose;
        LlmLogEnabled = llmLogEnabled;

        RoundsRoot = Path.Combine(experimentRoot, "rounds");
        TopicsRoot = Path.Combine(experimentRoot, "topics");
        TasksRoot = Path.Combine(experimentRoot, "tasks");
        GlossaryRoot = Path.Combine(experimentRoot, "glossary");
        SummariesRoot = Path.Combine(experimentRoot, "summaries");
        ReportsRoot = Path.Combine(experimentRoot, "reports");
    }

    public string ConfigPath { get; }
    public string? SettingsPath { get; }
    public string TranscriptsRoot { get; }
    public string ExperimentRoot { get; }
    public string RoundsRoot { get; }
    public string TopicsRoot { get; }
    public string TasksRoot { get; }
    public string GlossaryRoot { get; }
    public string SummariesRoot { get; }
    public string ReportsRoot { get; }
    public int Verbose { get; }
    public bool LlmLogEnabled { get; }

    /// <summary>
    /// Enumerate all transcript entries on disk, sorted by basename ascending
    /// (i.e. by interview date then person then subject). Allocates a 3-digit
    /// slot (001, 002, ...) at enumeration time.
    /// </summary>
    public IReadOnlyList<TranscriptEntry> Discover()
    {
        if (!Directory.Exists(TranscriptsRoot))
            return Array.Empty<TranscriptEntry>();

        var matches = new List<(string Basename, DateOnly Date, string Person, string Subject, string Path)>();
        foreach (var file in Directory.EnumerateFiles(TranscriptsRoot, "*.txt"))
        {
            var name = Path.GetFileName(file);
            var m = TranscriptRegex.Match(name);
            if (!m.Success) continue;
            if (!DateOnly.TryParseExact(m.Groups["date"].Value, "yyyy-MM-dd", out var d))
                continue;
            matches.Add((
                Basename: Path.GetFileNameWithoutExtension(name),
                Date: d,
                Person: m.Groups["person"].Value,
                Subject: m.Groups["subject"].Value,
                Path: file));
        }
        matches.Sort((a, b) => string.CompareOrdinal(a.Basename, b.Basename));

        var result = new List<TranscriptEntry>(matches.Count);
        for (int i = 0; i < matches.Count; i++)
        {
            var t = matches[i];
            result.Add(new TranscriptEntry(
                Slot: (i + 1).ToString("D3"),
                Basename: t.Basename,
                InterviewDate: t.Date,
                Person: t.Person,
                SubjectSlug: t.Subject,
                FilePath: t.Path));
        }
        return result;
    }

    /// <summary>
    /// Normalise a user-supplied token (e.g. "1", "001", or the full basename
    /// like "2026-01-28_clement_gestion-commandes") into one of the entries
    /// returned by <see cref="Discover"/>. Returns null on miss.
    /// </summary>
    public TranscriptEntry? Resolve(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var entries = Discover();
        if (entries.Count == 0) return null;

        if (int.TryParse(raw, out var n) && n >= 1 && n <= entries.Count)
        {
            var slot = n.ToString("D3");
            return entries.FirstOrDefault(e => e.Slot == slot);
        }

        var trimmed = raw.Trim().TrimEnd('.', 't', 'x').TrimEnd('.');
        if (trimmed.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];

        var byBasename = entries.FirstOrDefault(e =>
            string.Equals(e.Basename, trimmed, StringComparison.OrdinalIgnoreCase));
        if (byBasename is not null) return byBasename;

        var byBasenameRaw = entries.FirstOrDefault(e =>
            string.Equals(e.Basename, raw, StringComparison.OrdinalIgnoreCase));
        return byBasenameRaw;
    }

    /// <summary>
    /// Slugify a string for use as a round directory suffix.
    /// </summary>
    public static string Slugify(string raw)
    {
        var lower = raw.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder(lower.Length);
        char prev = '\0';
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                prev = ch;
            }
            else if (prev != '-' && sb.Length > 0)
            {
                sb.Append('-');
                prev = '-';
            }
        }
        var slug = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? "untitled" : slug;
    }

    /// <summary>
    /// Computes the round directory for a given transcript. Round numbering is
    /// per-transcript: every distinct basename gets its own <c>round-NN-&lt;person&gt;-&lt;subject&gt;/</c>
    /// folder under <see cref="RoundsRoot"/>, where NN = position in the
    /// corpus (matches Slot, 001..NNN).
    /// </summary>
    public string ResolveRoundDir(TranscriptEntry entry)
    {
        var slug = $"{entry.Person}-{entry.SubjectSlug}";
        return Path.Combine(RoundsRoot, $"round-{entry.Slot}-{slug}");
    }

    /// <summary>
    /// Compute the next attempt directory under a given round dir. Returns
    /// <c>attempt-01</c> if none exist, <c>attempt-MM</c> = max+1 otherwise.
    /// </summary>
    public static string ResolveNextAttemptDir(string roundDir)
    {
        if (!Directory.Exists(roundDir))
            return Path.Combine(roundDir, "attempt-01");

        int max = 0;
        foreach (var sub in Directory.EnumerateDirectories(roundDir, "attempt-*"))
        {
            var name = Path.GetFileName(sub);
            if (name.Length >= 10
                && name.StartsWith("attempt-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name[8..], out var n)
                && n > max)
            {
                max = n;
            }
        }
        return Path.Combine(roundDir, $"attempt-{(max + 1):D2}");
    }
}
