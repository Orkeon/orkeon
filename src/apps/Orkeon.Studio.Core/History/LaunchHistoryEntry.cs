using System.Text;
using System.Text.Json.Serialization;
using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.History;

/// <summary>
/// One past launch, enough to replay it in a click: what was run, with which settings file
/// and which arguments, when, and how it ended.
/// </summary>
public sealed record LaunchHistoryEntry
{
    /// <summary>The crew that was launched — a file or a directory path.</summary>
    [JsonPropertyName("target")]
    public required string Target { get; init; }

    /// <summary>The <c>appsettings.json</c> passed with <c>--settings</c>, when one was.</summary>
    [JsonPropertyName("settings_path")]
    public string? SettingsPath { get; init; }

    /// <summary>The full argument list handed to <c>orkeon</c>, one element per argv slot.</summary>
    [JsonPropertyName("arguments")]
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Working directory of the run.</summary>
    [JsonPropertyName("working_directory")]
    public string? WorkingDirectory { get; init; }

    /// <summary>When the launch started, in UTC.</summary>
    [JsonPropertyName("started_at")]
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Exit code as shown to the user; null while the run is still in flight.</summary>
    [JsonPropertyName("exit_code")]
    public int? ExitCode { get; init; }

    /// <summary>Interpretation of <see cref="ExitCode"/>.</summary>
    [JsonPropertyName("outcome")]
    public RunOutcome Outcome { get; init; } = RunOutcome.NotStarted;

    /// <summary>
    /// How long the run took; null while in flight and for entries written before the
    /// field existed (the JSON reader tolerates its absence — old history files load).
    /// </summary>
    [JsonPropertyName("duration_seconds")]
    public double? DurationSeconds { get; init; }

    /// <summary>Typed reading of <see cref="DurationSeconds"/>.</summary>
    [JsonIgnore]
    public TimeSpan? Duration => DurationSeconds is { } s ? TimeSpan.FromSeconds(s) : null;

    /// <summary>
    /// Total tokens the run reported at its close (W-08); null for unmetered runs and
    /// for entries written before the field existed — old history files load unchanged.
    /// </summary>
    [JsonPropertyName("tokens")]
    public long? Tokens { get; init; }

    /// <summary>Cache-served prompt tokens — a partition of the prompt side; null when unmeasured.</summary>
    [JsonPropertyName("cache_hit_tokens")]
    public long? CacheHitTokens { get; init; }

    /// <summary>Cache-missed prompt tokens; null when unmeasured.</summary>
    [JsonPropertyName("cache_miss_tokens")]
    public long? CacheMissTokens { get; init; }

    /// <summary>
    /// Completes the entry with the usage the event stream reported (W-08). Separate from
    /// <see cref="WithResult"/> on purpose: the launcher knows the process, only the
    /// progress model knows the meter — and a run without a meter stays honest nulls.
    /// </summary>
    public LaunchHistoryEntry WithUsage(long? tokens, long? cacheHitTokens, long? cacheMissTokens) =>
        this with { Tokens = tokens, CacheHitTokens = cacheHitTokens, CacheMissTokens = cacheMissTokens };

    /// <summary>Starts an entry for a launch about to happen.</summary>
    public static LaunchHistoryEntry Starting(
        string target,
        IReadOnlyList<string> arguments,
        string? settingsPath = null,
        string? workingDirectory = null,
        DateTimeOffset? startedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(arguments);

        return new LaunchHistoryEntry
        {
            Target = target,
            Arguments = [.. arguments],
            SettingsPath = settingsPath,
            WorkingDirectory = workingDirectory,
            StartedAt = startedAt ?? DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Completes the entry with the outcome of the run.</summary>
    public LaunchHistoryEntry WithResult(ProcessRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return this with
        {
            ExitCode = result.Outcome == RunOutcome.NotStarted ? null : result.ExitCode,
            Outcome = result.Outcome,
            DurationSeconds = result.Outcome == RunOutcome.NotStarted ? null : result.Duration.TotalSeconds,
        };
    }

    /// <summary>Whether this launch ran the team folder <paramref name="teamDirectory"/>: the folder itself, or a file inside it.</summary>
    public bool Launches(string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        return PhysicalPathContainment.IsUnder(TeamCatalog.NormalizePath(Target), TeamCatalog.NormalizePath(teamDirectory));
    }

    /// <summary>
    /// This launch with every path under the folder <paramref name="from"/> — its target, working
    /// directory, settings file and each argument, a mount spec's folder included — spelled under
    /// <paramref name="to"/> (STUDIO-28, D-04). The argument list is replayed verbatim, so it has to
    /// name the folder the team is in now.
    /// </summary>
    public LaunchHistoryEntry Rebased(string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        var folder = TeamCatalog.NormalizePath(from);
        var renamed = TeamCatalog.NormalizePath(to);
        return this with
        {
            Target = Rebase(Target, folder, renamed),
            WorkingDirectory = WorkingDirectory is null ? null : Rebase(WorkingDirectory, folder, renamed),
            SettingsPath = SettingsPath is null ? null : Rebase(SettingsPath, folder, renamed),
            Arguments = [.. Arguments.Select(argument => Rebase(argument, folder, renamed))],
        };
    }

    /// <summary>
    /// <paramref name="text"/> with <paramref name="folder"/> replaced by <paramref name="renamed"/>
    /// wherever it starts a path — the text's start, or after a quote, a <c>=</c> or a mount id's
    /// <c>|</c> — and ends one: the end, a separator, a closing quote, the mount grammar's
    /// <c>:</c> or <c>;</c>. <c>/teams/veille-2</c> is not <c>/teams/veille</c>, and a path that
    /// merely contains the folder deeper in is another path.
    /// </summary>
    private static string Rebase(string text, string folder, string renamed)
    {
        var rebased = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            var found = text.IndexOf(folder, index, PhysicalPathContainment.Comparison);
            if (found < 0)
                break;

            var end = found + folder.Length;
            var startsAPath = found == 0 || text[found - 1] is '"' or '\'' or '=' or '|';
            var endsAPath = end == text.Length || text[end] is '/' or '\\' or '"' or '\'' or ':' or ';';
            rebased.Append(text, index, found - index);
            if (startsAPath && endsAPath)
                rebased.Append(renamed);
            else
                rebased.Append(text, found, folder.Length);

            index = end;
        }

        return rebased.Append(text, index, text.Length - index).ToString();
    }
}
