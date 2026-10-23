using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Analysis.Tests.Doubles;

/// <summary>
/// Build validator that signals when it is entered and waits until released, then answers green.
/// </summary>
public sealed class FakeBuildValidator : IBuildValidator
{
    public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<BuildValidation> ValidateAsync(IReadOnlyList<string> editedPhysicalPaths, CancellationToken cancellationToken)
    {
        Entered.SetResult();
        await Release.Task;
        return new BuildValidation { Outcome = BuildOutcome.Green };
    }
}
