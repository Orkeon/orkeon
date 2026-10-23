using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Tools.Analysis.Tests.Doubles;

/// <summary>Hand-written fake of <see cref="IBuildValidator"/> returning a configured result and recording each call.</summary>
public sealed class FakeBuildValidator : IBuildValidator
{
    public BuildValidation Result = new() { Outcome = BuildOutcome.Green, Detail = "" };

    public List<IReadOnlyList<string>> Calls { get; } = [];

    public Task<BuildValidation> ValidateAsync(IReadOnlyList<string> editedPhysicalPaths, CancellationToken cancellationToken)
    {
        Calls.Add([.. editedPhysicalPaths]);
        return Task.FromResult(Result);
    }
}
