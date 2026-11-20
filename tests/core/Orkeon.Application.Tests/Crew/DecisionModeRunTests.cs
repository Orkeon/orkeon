using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// #429: what a run does with the crew's <c>decision.mode</c> when the decision circuit is off - a required
/// crew refused with a typed error, an optional crew's decision task left to its relay (#430).
/// </summary>
public sealed class DecisionModeRunTests
{
    private static TaskDecision Relayed() =>
        TaskDecision.Create(
            [ChoiceQuestionSpec.Create("route", "Pick the route", new Dictionary<string, string> { ["bug"] = "a defect", ["other"] = "anything else" })],
            null, null, false, null)
        .WithRelay(new DecisionRelay("analyst"));

    private static Orkeon.Domain.Crew.Crew CrewOf(CrewDecisionMode mode)
    {
        var builder = new CrewBuilder().Name("triage-crew").Goal("Triage").DecisionMode(mode);
        if (mode == CrewDecisionMode.Optional)
            builder.WithTasks([new CrewTaskBuilder().WithKey("triage").Description("Decide").ExpectedOutput("An outcome").WithDecision(Relayed()).Build()]);
        return builder.Build();
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseTheCrewAndSayWhy_WhenItRequiresSystemOneAndTheCircuitIsOff()
    {
        // Arrange
        var crew = CrewOf(CrewDecisionMode.Required);

        // Act
        var refusal = DecisionUnavailableException.Refusing(crew, DecisionCircuit.Off);

        // Assert
        Assert.NotNull(refusal);
        Assert.Equal(
            "crew 'triage-crew' requires System One (decision.mode: required) and the decision circuit is off: --no-decision",
            refusal.Message);
    }

    [Theory]
    [InlineData(CrewDecisionMode.Unspecified)]
    [InlineData(CrewDecisionMode.None)]
    [InlineData(CrewDecisionMode.Optional)]
    [Trait("BR", "CH-429")]
    public void ShouldLetTheCrewStart_WhenItDoesNotRequireSystemOne(CrewDecisionMode mode)
    {
        // Arrange
        var crew = CrewOf(mode);

        // Act
        var refusal = DecisionUnavailableException.Refusing(crew, DecisionCircuit.OffBy(DecisionCircuitCause.Settings));

        // Assert
        Assert.Null(refusal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldLetARequiredCrewStart_WhenTheCircuitIsOn()
    {
        // Arrange
        var crew = CrewOf(CrewDecisionMode.Required);

        // Act
        var refusal = DecisionUnavailableException.Refusing(crew, DecisionCircuit.On);

        // Assert
        Assert.Null(refusal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldSayNoProvider_WhenTheRunnerHasNoModel()
    {
        // Arrange
        var runner = new DecisionTaskRunner(null, DecisionCircuit.On, NullLogger<DecisionTaskRunner>.Instance);

        // Act
        var circuit = runner.Circuit;

        // Assert
        Assert.Equal(DecisionCircuitCause.NoProvider, circuit.Cause);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldFailTheTaskAndNameTheRelay_WhenTheCircuitIsOffAndTheHostHasNoRelay()
    {
        // Arrange
        var runner = new DecisionTaskRunner(new FakeDecisionModel(), DecisionCircuit.Off, NullLogger<DecisionTaskRunner>.Instance);
        var task = new CrewTaskBuilder().WithKey("triage").Description("Decide").ExpectedOutput("An outcome").WithDecision(Relayed()).Build();

        // Act
        var result = await runner.RunAsync(task, [], TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(
            "Decision task 'triage' is answered by its without: relay (agent 'analyst') when the decision circuit is off: --no-decision, " +
            "and this host registers no decision relay.",
            result.Error);
    }
}
