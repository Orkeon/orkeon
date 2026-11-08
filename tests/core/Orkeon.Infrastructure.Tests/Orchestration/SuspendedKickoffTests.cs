using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Orchestration;
using Cov = Orkeon.Infrastructure.Tests.CovAutonomous.CovAutonomous_SequentialCrewOrchestratorTests;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// FWK-23: a run whose strategy left it waiting for a human (<c>RunScope.Suspension</c>) ends as such: neither completed
/// nor failed in its session, with the question in its output, and nothing reported as a failure.
/// </summary>
public sealed class SuspendedKickoffTests
{
    private static readonly PendingInputRequest Question =
        new() { RequestId = "t#1.1", Kind = PendingInputKind.Text, Prompt = "Which one?", TaskKey = "t#1" };

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task KickoffAsync_WhenTheRunIsLeftWaitingForInput_EndsWithThePendingQuestionAndNeitherCompletesNorFailsTheSession()
    {
        var scope = new AsyncLocalRunScope();
        var h = Cov.Build(withCheckpoint: true, runScope: scope);
        var crew = Cov.MakeCrew(ProcessType.Sequential);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);
        h.Factory.Strategy.OnExecute = () => scope.Current!.Suspend(Question);

        var output = await h.Orchestrator.KickoffAsync(crew.Id, Cov.Input(), TestContext.Current.CancellationToken);

        Assert.False(output.Succeeded);
        Assert.Equal("t#1.1", output.PendingInput!.RequestId);
        Assert.NotNull(output.ExecutionId);
        Assert.Equal(0, h.Checkpoint!.CompleteCount);
        Assert.Equal(0, h.Checkpoint.MarkFailedCount);
        Assert.Null(scope.Current);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task KickoffAsync_WhenTheRunIsNotSuspended_CompletesItsSessionAsBefore()
    {
        var scope = new AsyncLocalRunScope();
        var h = Cov.Build(withCheckpoint: true, runScope: scope);
        var crew = Cov.MakeCrew(ProcessType.Sequential);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);

        var output = await h.Orchestrator.KickoffAsync(crew.Id, Cov.Input(), TestContext.Current.CancellationToken);

        Assert.True(output.Succeeded);
        Assert.Null(output.PendingInput);
        Assert.Equal(1, h.Checkpoint!.CompleteCount);
    }
}
