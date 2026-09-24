using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Forge;

/// <summary>
/// Where a team's schedule stands (STUDIO-27): what <c>forge schedule --check</c> answered, or
/// that nothing answered yet. A card shows the state it was told, never one it assumed.
/// </summary>
public enum TeamScheduleState
{
    /// <summary>Not asked yet, or the engine could not say — the card claims nothing.</summary>
    Unknown,

    /// <summary>« Scheduled »: the operating system runs this team, on the schedule it declares.</summary>
    Installed,

    /// <summary>« Not installed »: nothing of this team's is registered with the operating system.</summary>
    Absent,

    /// <summary>« To reinstall »: registered, but no longer what the team is — moved, renamed, rescheduled, disabled.</summary>
    Stale,
}

/// <summary>The three schedule gestures of the engine (STUDIO-27, D-01).</summary>
public enum ForgeScheduleVerb
{
    /// <summary><c>forge schedule &lt;team&gt;</c>: install, or reinstall.</summary>
    Install,

    /// <summary><c>forge schedule &lt;team&gt; --check</c>: where it stands, nothing changed.</summary>
    Check,

    /// <summary><c>forge unschedule &lt;team&gt;</c>: remove the registration and the artifacts.</summary>
    Remove,
}

/// <summary>
/// What one schedule verb answered: the folder's state from <c>schedule.state</c>, or the
/// refusal from <c>error</c> — with the command a person can run instead — plus the
/// <c>warning</c>s. Read by <see cref="ForgeClient.ScheduleAsync"/>; Studio never talks to the
/// operating system's scheduler itself.
/// </summary>
public sealed record ForgeScheduleReport
{
    /// <summary>How the engine child ended.</summary>
    public required ProcessRunResult Run { get; init; }

    /// <summary>The state the verb left the team in; <see cref="TeamScheduleState.Unknown"/> when no state came back.</summary>
    public TeamScheduleState State { get; init; } = TeamScheduleState.Unknown;

    /// <summary>Why it is absent or stale, in the engine's words (<c>moved</c>, <c>renamed</c>, <c>changed</c>, <c>copy</c>…).</summary>
    public string? Reason { get; init; }

    /// <summary>The schedule the team declares, as the engine read it.</summary>
    public string? Expression { get; init; }

    /// <summary>The names of the registration concerned.</summary>
    public IReadOnlyList<string> Names { get; init; } = [];

    /// <summary>A removal: whether a registration was actually removed.</summary>
    public bool Removed { get; init; }

    /// <summary>The refusal's code (<c>FORGE-SCHEDULE-REFUSED</c>…); null when there was none.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>The refusal's sentence, in the engine's words.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>What a person can run instead, when the engine offered it.</summary>
    public string? ManualCommand { get; init; }

    /// <summary>The engine's warnings, in its words.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>What the engine printed on stderr — the only explanation a crash leaves.</summary>
    public string? StandardError { get; init; }

    /// <summary>Whether the verb did what it was asked: the child exited 0 and said where the team stands.</summary>
    public bool Succeeded =>
        Run.Outcome == RunOutcome.Success && ErrorCode is null && State != TeamScheduleState.Unknown;

    /// <summary>Why it did not, in one line: the engine's refusal, else what it printed, else how the child ended.</summary>
    public string FailureReason =>
        ErrorMessage
        ?? (StandardError is { Length: > 0 } stderr ? stderr : null)
        ?? Run.Description;
}

/// <summary>Reads the lines of one schedule verb into a <see cref="ForgeScheduleReport"/>.</summary>
internal sealed class ForgeScheduleReading
{
    private readonly List<string> _warnings = [];
    private readonly List<string> _stderr = [];
    private OrkeonEvent? _state;
    private OrkeonEvent? _error;

    /// <summary>One event of the stream.</summary>
    public void Read(OrkeonEvent orkeonEvent)
    {
        switch (orkeonEvent.Kind)
        {
            case ForgeEventKinds.ScheduleState:
                _state = orkeonEvent;
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
    public ForgeScheduleReport Report(ProcessRunResult run) => new()
    {
        Run = run,
        State = _state is null ? TeamScheduleState.Unknown : StateOf(_state.GetString("state")),
        Reason = _state?.GetString("reason"),
        Expression = _state?.GetString("expression"),
        Names = _state?.GetStrings("names") ?? [],
        Removed = _state?.GetBool("removed") ?? false,
        ErrorCode = _error?.GetString("code"),
        ErrorMessage = _error?.GetString("message"),
        ManualCommand = _error?.GetString("command"),
        Warnings = [.. _warnings],
        StandardError = _stderr.Count > 0 ? string.Join(Environment.NewLine, _stderr) : null,
    };

    /// <summary>The wire's word for a state; anything else is unknown, never guessed.</summary>
    internal static TeamScheduleState StateOf(string? word) => word switch
    {
        "installed" => TeamScheduleState.Installed,
        "absent" => TeamScheduleState.Absent,
        "stale" => TeamScheduleState.Stale,
        _ => TeamScheduleState.Unknown,
    };
}
