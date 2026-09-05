namespace Orkeon.Cli.Commands.Scripting.Progress;

/// <summary>
/// Immutable state of the operation currently reporting progress, as published to the
/// <see cref="ProgressBroker"/>. One of <see cref="Step"/>+<see cref="Total"/> or
/// <see cref="Percent"/> may be set — both absent means an indeterminate operation
/// (renderers show a spinner + message instead of a bar).
/// </summary>
public sealed record ProgressSnapshot
{
    /// <summary>What is running, e.g. <c>"Compacting conversation"</c>. Shown as the headline.</summary>
    public required string Label { get; init; }

    /// <summary>Current step (1-based) when the operation reports discrete steps.</summary>
    public int? Step { get; init; }

    /// <summary>Total step count, when known.</summary>
    public int? Total { get; init; }

    /// <summary>Completion ratio in [0, 100], when the operation reports a percentage.</summary>
    public double? Percent { get; init; }

    /// <summary>Free-form current-phase line (e.g. <c>"extracting memories"</c>).</summary>
    public string? Message { get; init; }

    /// <summary>When this operation FIRST reported (kept across updates of the same label).</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Ticket of the command instance this progress belongs to, when attributable.</summary>
    public string? Ticket { get; init; }

    /// <summary>
    /// The effective completion ratio in [0, 1], from <see cref="Percent"/> first, else
    /// <see cref="Step"/>/<see cref="Total"/>; null when the operation is indeterminate.
    /// </summary>
    public double? Ratio
    {
        get
        {
            if (Percent is { } percent)
                return Math.Clamp(percent / 100d, 0d, 1d);

            if (Step is { } step && Total is { } total && total > 0)
                return Math.Clamp((double)step / total, 0d, 1d);

            return null;
        }
    }
}
