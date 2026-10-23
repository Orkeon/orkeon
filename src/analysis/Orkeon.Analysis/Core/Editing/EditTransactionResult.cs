using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Analysis.Core.Editing;

internal sealed record EditedRange(string Path, NodeRange Range, bool Applied = true);

internal sealed record EditTransactionResult
{
    public required IReadOnlyList<EditedRange> Operations { get; init; }

    public BuildValidation? Build { get; init; }
}
