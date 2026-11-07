using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Infrastructure.Tests.CovAutonomous;
using Cov = Orkeon.Infrastructure.Tests.CovAutonomous.CovAutonomous_SequentialCrewOrchestratorTests;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// FWK-01: a run has one identifier. The one a caller gives (<see cref="KickoffOptions.ExecutionId"/>) or
/// the one the run mints is the checkpoint session, the run scope every decorator reads and, for a
/// no-wait run, the id the caller gets back.
/// </summary>
public sealed class KickoffAsyncRunIdentityTests
{
    private static async Task<(Cov.Harness H, Orkeon.Domain.Crew.Crew Crew, AsyncLocalRunScope Scope)> ArrangeAsync()
    {
        var scope = new AsyncLocalRunScope();
        var h = Cov.Build(withCheckpoint: true, runScope: scope);
        var crew = Cov.MakeCrew(ProcessType.Sequential);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);
        return (h, crew, scope);
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public async Task KickoffAsync_WithAnExecutionIdInTheOptions_ThatIdIsTheSessionAndTheRunScope()
    {
        var (h, crew, _) = await ArrangeAsync();
        var id = ExecutionId.New();

        await h.Orchestrator.KickoffAsync(
            crew.Id, Cov.Input(), new KickoffOptions { ExecutionId = id, CrewKey = "my-crew@/crew/crew.yaml" }, TestContext.Current.CancellationToken);

        Assert.Equal(id, h.Checkpoint!.LastSessionId);
        Assert.Equal("my-crew@/crew/crew.yaml", h.Checkpoint.LastCrewKey);
        Assert.Equal(id, h.Checkpoint.ScopeAtStart!.ExecutionId);
        Assert.Equal("my-crew@/crew/crew.yaml", h.Checkpoint.ScopeAtStart.CrewKey);
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public async Task KickoffAsync_WithoutOptions_MintsOneIdForTheSessionAndTheRunScope_AndTheRunScopeEndsWithTheRun()
    {
        var (h, crew, scope) = await ArrangeAsync();

        await h.Orchestrator.KickoffAsync(crew.Id, Cov.Input(), TestContext.Current.CancellationToken);

        Assert.NotNull(h.Checkpoint!.LastSessionId);
        Assert.Equal(h.Checkpoint.LastSessionId, h.Checkpoint.ScopeAtStart!.ExecutionId);
        Assert.Equal(crew.Name ?? crew.Id.ToString(), h.Checkpoint.LastCrewKey);
        Assert.Null(scope.Current);
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public async Task KickoffAsyncNoWait_TheReturnedIdIsTheSession_NotASecondId()
    {
        var (h, crew, _) = await ArrangeAsync();

        var execId = await h.Orchestrator.KickoffAsyncNoWait(crew.Id, Cov.Input());

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (h.State.CompleteExecutionCount == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.Equal(1, h.Checkpoint!.StartCount);
        Assert.Equal(execId.AsString(), h.Checkpoint.LastSessionId!.AsString());
        Assert.Equal(execId.AsString(), h.Checkpoint.ScopeAtStart!.ExecutionId.AsString());
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public async Task KickoffStreamingAsync_WithOptions_TheStreamedRunTakesTheGivenIdAndKey()
    {
        var (h, crew, _) = await ArrangeAsync();
        var id = ExecutionId.New();

        await foreach (var _ in h.Orchestrator.KickoffStreamingAsync(
            crew.Id, Cov.Input(), new KickoffOptions { ExecutionId = id, CrewKey = "streamed@/crew/s.yaml" }, TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal(id, h.Checkpoint!.LastSessionId);
        Assert.Equal("streamed@/crew/s.yaml", h.Checkpoint.LastCrewKey);
        Assert.Equal(id, h.Checkpoint.ScopeAtStart!.ExecutionId);
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public async Task KickoffAsync_TwoRunsAtOnce_EachSeesItsOwnIdentity()
    {
        var (h, first, _) = await ArrangeAsync();
        var second = Cov.MakeCrew(ProcessType.Sequential);
        await h.Repo.AddAsync(second, TestContext.Current.CancellationToken);
        var idA = ExecutionId.New();
        var idB = ExecutionId.New();

        await Task.WhenAll(
            h.Orchestrator.KickoffAsync(first.Id, Cov.Input(), new KickoffOptions { ExecutionId = idA }, TestContext.Current.CancellationToken),
            h.Orchestrator.KickoffAsync(second.Id, Cov.Input(), new KickoffOptions { ExecutionId = idB }, TestContext.Current.CancellationToken));

        Assert.Equal(2, h.Checkpoint!.Starts.Count);
        Assert.All(h.Checkpoint.Starts, start => Assert.Equal(start.Id, start.Scope!.ExecutionId));
        Assert.Equal(new[] { idA, idB }.Select(i => i.ToString()).Order(), h.Checkpoint.Starts.Select(s => s.Id.ToString()).Order());
        Assert.Equal(new[] { idA, idB }.Select(i => i.ToString()).Order(), h.Checkpoint.ScopesAtCheckpoint.Select(s => s!.ExecutionId.ToString()).Distinct().Order());
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public async Task KickoffAsync_NestedInsideARun_TheInnerRunOverridesTheOuterOne_AndTheOuterIsBackAfterwards()
    {
        var (h, crew, scope) = await ArrangeAsync();
        var outer = ExecutionId.New();
        var inner = ExecutionId.New();

        using (scope.Enter(new RunScope(outer, "outer")))
        {
            await h.Orchestrator.KickoffAsync(crew.Id, Cov.Input(), new KickoffOptions { ExecutionId = inner }, TestContext.Current.CancellationToken);

            Assert.Equal(inner, h.Checkpoint!.ScopeAtStart!.ExecutionId);
            Assert.Equal(outer, scope.Current!.ExecutionId);
        }

        Assert.Null(scope.Current);
    }
}
