namespace Orkeon.Analysis.Core.Editing;

internal sealed record EditOutcome
{
    public required NodeRange Range { get; init; }

    public required string NewHash { get; init; }

    public bool Applied { get; init; } = true;
}
