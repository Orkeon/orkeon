namespace Orkeon.Analysis.Core.Editing;

internal sealed record EditValidation(IReadOnlyList<string>? BuildPaths)
{
    public static EditValidation Parse => new((IReadOnlyList<string>?)null);

    public static EditValidation Build(IReadOnlyList<string> editedPhysicalPaths) => new(editedPhysicalPaths);
}
