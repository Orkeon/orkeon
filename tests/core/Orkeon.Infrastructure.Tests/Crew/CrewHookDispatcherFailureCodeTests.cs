using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Infrastructure.Crew;

namespace Orkeon.Infrastructure.Tests.Crew;

/// <summary>The code of a stopped run's <c>error</c> event, by its cause (#429, #430).</summary>
public sealed class CrewHookDispatcherFailureCodeTests
{
    [Fact]
    [Trait("BR", "CH-430")]
    public void ShouldCodeTheFailureDecisionCircuitMismatch_WhenAnOptionalCrewIsResumedUnderTheOtherCircuit()
    {
        // Arrange
        var cause = new DecisionCircuitMismatchException("crew 'triage' (decision.mode: optional) was started with the decision circuit off");

        // Act
        var code = CrewHookDispatcher.FailureCode(CrewHookStatus.Failed, cause);

        // Assert
        Assert.Equal("decision_circuit_mismatch", code);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldCodeTheFailureDecisionUnavailable_WhenARequiredCrewIsRefusedWithTheCircuitOff()
    {
        // Arrange
        var cause = new DecisionUnavailableException("triage", DecisionCircuit.Off);

        // Act
        var code = CrewHookDispatcher.FailureCode(CrewHookStatus.Failed, cause);

        // Assert
        Assert.Equal("decision_unavailable", code);
    }
}
