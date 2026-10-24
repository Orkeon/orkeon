namespace Orkeon.Analysis.Abstractions.Interfaces;

/// <summary>Outcome of a repository build run after a batch of edits.</summary>
public enum BuildOutcome
{
    Green,
    Red,
    Skipped,
}

/// <summary>Result of <see cref="IBuildValidator.ValidateAsync"/>.</summary>
public sealed record BuildValidation
{
    public required BuildOutcome Outcome { get; init; }

    public string Detail { get; init; } = "";
}

/// <summary>Builds the project owning the edited files.</summary>
public interface IBuildValidator
{
    Task<BuildValidation> ValidateAsync(IReadOnlyList<string> editedPhysicalPaths, CancellationToken cancellationToken);
}
