using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Communication;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.Strategies.Lifecycle;

namespace Orkeon.Infrastructure.Tests.Strategies;

/// <summary>BR-07 at run time: the autonomous process refuses a crew holding a decision task before any task runs.</summary>
public sealed class AutonomousDecisionRefusalTests : IDisposable
{
    private readonly LifecycleFixture _fixture = new();
    private readonly MockManagerAgent _manager = new();
    private readonly InMemoryAgentChannel _channel = new(NullLogger<InMemoryAgentChannel>.Instance);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    [Trait("BR", "BR-07")]
    public async System.Threading.Tasks.Task ShouldFailBeforeFirstTask_WhenAutonomousCrewHoldsDecisionTask()
    {
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        var triage = DecisionRefusal.DecisionTask(_fixture, "triage");
        _manager.SetAssignResult(new TaskAssignment(draft.Id, worker.Id, "first agent", DateTime.UtcNow));
        var crew = LifecycleFixture.Build(
            new CrewBuilder().Goal("Autonomous").Process(ProcessType.Autonomous), [worker], [draft, triage]);
        var strategy = new AutonomousProcessStrategy(
            _fixture.Dependencies,
            _channel,
            _manager,
            TestManagerLlm.Resolver(),
            NullLogger<AutonomousProcessStrategy>.Instance,
            _fixture.Hook);

        await DecisionRefusal.AssertRefusedAsync(
            _fixture,
            () => strategy.ExecuteAutonomousAsync(crew, new AgentExecutionBudget(), cancellationToken: TestContext.Current.CancellationToken),
            "triage",
            ProcessType.Autonomous);
    }
}
