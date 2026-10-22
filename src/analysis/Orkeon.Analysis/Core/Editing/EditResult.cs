namespace Orkeon.Analysis.Core.Editing;

internal sealed record EditResult
{
    public required byte[] Bytes { get; init; }

    public required NodeRange Range { get; init; }
}
