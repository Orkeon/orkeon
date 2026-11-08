using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Checkpointing;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Infrastructure.Orchestration;
using Cov = Orkeon.Infrastructure.Tests.CovAutonomous.CovAutonomous_SequentialCrewOrchestratorTests;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>FWK-23: <c>--resume X --input a</c> is idempotent: run again after a crash, with the answer already stored, it goes on.</summary>
public sealed class ResumeInputKickoffTests
{
    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task AnswerResumedQuestion_GivenTwiceWithTheSameText_ContinuesTheSecondTime()
    {
        var store = new InMemoryStateStore();
        var manager = new CheckpointManager(store, NullLogger<CheckpointManager>.Instance);
        var engine = new ResumeEngine(manager, store, NullLogger<ResumeEngine>.Instance);
        var scope = new AsyncLocalRunScope();
        var h = Cov.Build(runScope: scope, manager: manager, engine: engine);
        var crew = Cov.MakeCrew(ProcessType.Sequential);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);
        var crewKey = "crew@/crew/crew.yaml";
        var id = ExecutionId.New();
        await manager.StartSessionAsync(id, crewKey, TestContext.Current.CancellationToken);
        await manager.AskAsync(id.ToString(), new PendingInputRequest { RequestId = "t#1.1", Kind = PendingInputKind.Text, Prompt = "?", TaskKey = "t#1" }, TestContext.Current.CancellationToken);
        var options = new KickoffOptions { CrewKey = crewKey, Resume = id.ToString(), ResumeInput = "blue" };

        // The first attempt answers, then the process dies before the run goes on: the answer is stored, the session suspended.
        await manager.AnswerAsync(id.ToString(), null, "blue", TestContext.Current.CancellationToken);
        var again = await h.Orchestrator.KickoffAsync(crew.Id, Cov.Input(), options, TestContext.Current.CancellationToken);

        Assert.True(again.Succeeded);
        Assert.Equal(id.ToString(), again.ExecutionId);
    }
}
