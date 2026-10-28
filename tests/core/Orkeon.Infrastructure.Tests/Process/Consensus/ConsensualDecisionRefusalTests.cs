using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Consensus;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.Strategies;
using Orkeon.Infrastructure.Tests.Strategies.Lifecycle;

namespace Orkeon.Infrastructure.Tests.Process.Consensus;

/// <summary>BR-07 at run time: the consensual process refuses a crew holding a decision task before any task runs.</summary>
public sealed class ConsensualDecisionRefusalTests : IDisposable
{
    private readonly LifecycleFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    [Trait("BR", "BR-07")]
    public async System.Threading.Tasks.Task ShouldFailBeforeFirstTask_WhenConsensualCrewHoldsDecisionTask()
    {
        var solo = _fixture.Agent("Solo");
        var draft = _fixture.Task("draft");
        var triage = DecisionRefusal.DecisionTask(_fixture, "triage");
        var crew = LifecycleFixture.Build(new CrewBuilder().Goal("Vote").Consensual(), [solo], [draft, triage]);
        var strategy = new ConsensualProcessStrategy(
            new MajorityVotingStrategy(),
            new FakeBallotCollector(),
            _fixture.Dependencies,
            new MockMemoryCoordinator(),
            NullLogger<ConsensualProcessStrategy>.Instance,
            Options.Create(new ConsensualProcessOptions()),
            _fixture.Hook);

        await DecisionRefusal.AssertRefusedAsync(
            _fixture,
            () => strategy.ExecuteConsensualAsync(crew, ct: TestContext.Current.CancellationToken),
            "triage",
            ProcessType.Consensual);
    }
}
