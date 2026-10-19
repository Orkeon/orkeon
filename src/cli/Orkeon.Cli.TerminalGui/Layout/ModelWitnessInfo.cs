using Orkeon.Cli.Abstractions;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>The selected model and its availability, shown right-aligned in the hint bar.</summary>
public sealed record ModelWitnessInfo
{
    public required string Model { get; init; }

    public required ModelAvailability Availability { get; init; }
}
