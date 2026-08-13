using System.Text.Json.Serialization;
using Orkeon.Studio.Core.Process;

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
        };
    }
}
