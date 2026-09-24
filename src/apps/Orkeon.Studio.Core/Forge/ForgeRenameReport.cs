using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Forge;

/// <summary>
/// What <c>forge rename</c> answered (STUDIO-28): the team's new folder and name from
/// <c>team.renamed</c>, the session's new folder from <c>session.renamed</c>, the schedule's state
/// from <c>schedule.state</c> when it was reinstalled — or the refusal from <c>error</c>, after
/// which nothing changed — plus the <c>warning</c>s. Read by <see cref="ForgeClient.RenameAsync"/>.
/// </summary>
public sealed record ForgeRenameReport
{
    /// <summary>How the engine child ended.</summary>
    public required ProcessRunResult Run { get; init; }

    /// <summary>The team folder now — the one it had when the new name keeps it; null when no rename was said.</summary>
    public string? Path { get; init; }

    /// <summary>The name every title now carries.</summary>
    public string? Name { get; init; }

    /// <summary>The linked session's folder now, when it followed the team's; null when it did not move.</summary>
    public string? SessionDirectory { get; init; }

    /// <summary>The schedule's state once reinstalled under the new name; <see cref="TeamScheduleState.Unknown"/> when none was.</summary>
    public TeamScheduleState ScheduleState { get; init; } = TeamScheduleState.Unknown;

    /// <summary>The refusal's code (<c>FORGE-RENAME-TAKEN</c>…); null when there was none.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>The refusal's sentence, in the engine's words.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The engine's warnings, in its words.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>What the engine printed on stderr — the only explanation a crash leaves.</summary>
    public string? StandardError { get; init; }

    /// <summary>Whether the team was renamed: the child exited 0 and said where the team is now.</summary>
    public bool Succeeded => Run.Outcome == RunOutcome.Success && ErrorCode is null && Path is not null;

    /// <summary>Why it was not, in one line: the engine's refusal, else what it printed, else how the child ended.</summary>
    public string FailureReason =>
        ErrorMessage
        ?? (StandardError is { Length: > 0 } stderr ? stderr : null)
        ?? Run.Description;
}

/// <summary>Reads the lines of one <c>forge rename</c> into a <see cref="ForgeRenameReport"/>.</summary>
internal sealed class ForgeRenameReading
{
    private readonly List<string> _warnings = [];
    private readonly List<string> _stderr = [];
    private OrkeonEvent? _renamed;
    private OrkeonEvent? _session;
    private OrkeonEvent? _schedule;
    private OrkeonEvent? _error;

    /// <summary>One event of the stream.</summary>
    public void Read(OrkeonEvent orkeonEvent)
    {
        switch (orkeonEvent.Kind)
        {
            case ForgeEventKinds.TeamRenamed:
                _renamed = orkeonEvent;
                break;
            case ForgeEventKinds.SessionRenamed:
                _session = orkeonEvent;
                break;
            case ForgeEventKinds.ScheduleState:
                _schedule = orkeonEvent;
                break;
            case ForgeEventKinds.Error:
                _error ??= orkeonEvent;
                break;
            case ForgeEventKinds.Warning when orkeonEvent.GetString("message") is { Length: > 0 } message:
                _warnings.Add(message);
                break;
        }
    }

    /// <summary>One line the child printed on stderr.</summary>
    public void ReadError(string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
            _stderr.Add(line.Trim());
    }

    /// <summary>The report, once the child is gone.</summary>
    public ForgeRenameReport Report(ProcessRunResult run) => new()
    {
        Run = run,
        Path = _renamed?.GetString("path"),
        Name = _renamed?.GetString("name"),
        SessionDirectory = _session?.GetString("dir"),
        ScheduleState = _schedule is null ? TeamScheduleState.Unknown : ForgeScheduleReading.StateOf(_schedule.GetString("state")),
        ErrorCode = _error?.GetString("code"),
        ErrorMessage = _error?.GetString("message"),
        Warnings = [.. _warnings],
        StandardError = _stderr.Count > 0 ? string.Join(Environment.NewLine, _stderr) : null,
    };
}
