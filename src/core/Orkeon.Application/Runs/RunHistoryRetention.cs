using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Application.Runs;

/// <summary>
/// Which recorded runs a purge removes, one rule for every store: a finished run older than
/// <see cref="RunHistoryOptions.RetentionDays"/> goes, and so does the oldest finished run beyond
/// <see cref="RunHistoryOptions.MaxRuns"/>. A run still in flight is never purged: its history is being written.
/// </summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public static class RunHistoryRetention
{
    /// <summary>The ids of the runs to remove from <paramref name="runs"/> at <paramref name="now"/>.</summary>
    public static IReadOnlyList<string> Select(IReadOnlyCollection<RunSummary> runs, RunHistoryOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(options);

        var purge = new HashSet<string>(StringComparer.Ordinal);
        var finished = runs.Where(run => run.Status != RunStatus.Running).ToList();

        if (options.RetentionDays > 0)
        {
            var oldest = now - TimeSpan.FromDays(options.RetentionDays);
            foreach (var run in finished.Where(run => run.StartedAt < oldest))
                purge.Add(run.RunId);
        }

        if (options.MaxRuns > 0)
        {
            var overflow = runs.Count - options.MaxRuns;
            foreach (var run in finished.Where(run => !purge.Contains(run.RunId)).OrderBy(run => run.StartedAt).Take(Math.Max(0, overflow - purge.Count)))
                purge.Add(run.RunId);
        }

        return [.. purge];
    }
}
