using System.Collections.Immutable;
using System.Text.Json;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.Crew;
using Orkeon.Hosting.Runs;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Forge;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>#429: <c>run.decision</c> tells a front-end what the crew asks of System One and whether it has it.</summary>
public sealed class RunDecisionEventTests : IDisposable
{
    private readonly StringWriter _output = new();

    public void Dispose() => _output.Dispose();

    private RunEventObserver Observer() => new(new OrkeonEventWriter(_output, new FakeOrkeonClock()), inner: null, stream: false);

    private IReadOnlyList<JsonElement> Events() =>
    [
        .. _output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonElement.Parse(line)),
    ];

    private static CrewExecutionSnapshot Failed() =>
        new()
        {
            CrewId = "c-7f3a",
            StartedAt = DateTimeOffset.UnixEpoch,
            EndedAt = DateTimeOffset.UnixEpoch,
            Tasks = ImmutableList<TaskExecutionSnapshot>.Empty,
            Status = CrewHookStatus.Failed,
        };

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldCarryTheModeTheCircuitAndItsCause_WhenTheCircuitIsOff()
    {
        // Arrange
        var observer = Observer();

        // Act
        await observer.OnRunDecisionAsync(
            new RunDecisionNotice(CrewDecisionMode.Optional, DecisionCircuit.OffBy(DecisionCircuitCause.Switch)),
            TestContext.Current.CancellationToken);

        // Assert
        var e = Assert.Single(Events());
        Assert.Equal(
            ("run.decision", "optional", "off", "switch"),
            (e.GetProperty("kind").GetString(), e.GetProperty("mode").GetString(), e.GetProperty("circuit").GetString(), e.GetProperty("cause").GetString()));
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task ShouldNameTheTrialAsTheCause_WhenTheForgesRelayPassTurnsTheCircuitOff()
    {
        // Arrange
        var observer = Observer();

        // Act
        await observer.OnRunDecisionAsync(
            new RunDecisionNotice(CrewDecisionMode.Optional, DecisionCircuit.OffBy(DecisionCircuitCause.Trial)),
            TestContext.Current.CancellationToken);

        // Assert
        var e = Assert.Single(Events());
        Assert.Equal(("off", "trial"), (e.GetProperty("circuit").GetString(), e.GetProperty("cause").GetString()));
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldLeaveTheCauseOut_WhenTheCircuitIsOn()
    {
        // Arrange
        var observer = Observer();

        // Act
        await observer.OnRunDecisionAsync(new RunDecisionNotice(CrewDecisionMode.Unspecified, DecisionCircuit.On), TestContext.Current.CancellationToken);

        // Assert
        var e = Assert.Single(Events());
        Assert.Equal(("unspecified", "on"), (e.GetProperty("mode").GetString(), e.GetProperty("circuit").GetString()));
        Assert.False(e.TryGetProperty("cause", out _));
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldEmitItOnce_WhenACrewRunFromATaskReachesTheHookAgain()
    {
        // Arrange
        var observer = Observer();
        await observer.OnRunDecisionAsync(new RunDecisionNotice(CrewDecisionMode.Required, DecisionCircuit.On), TestContext.Current.CancellationToken);

        // Act
        await observer.OnRunDecisionAsync(new RunDecisionNotice(CrewDecisionMode.None, DecisionCircuit.On), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("required", Assert.Single(Events()).GetProperty("mode").GetString());
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldCodeTheErrorDecisionUnavailable_WhenTheCrewIsRefusedForWantOfSystemOne()
    {
        // Arrange
        var observer = Observer();
        var refusal = new DecisionUnavailableException("triage-crew", DecisionCircuit.Off);

        // Act
        await observer.OnCrewFailedAsync(Failed(), refusal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("decision_unavailable", Assert.Single(Events()).GetProperty("code").GetString());
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async Task ShouldCodeTheErrorDecisionCircuitMismatch_WhenAnOptionalCrewIsResumedUnderTheOtherCircuit()
    {
        // Arrange
        var observer = Observer();
        var refusal = new DecisionCircuitMismatchException("crew 'triage-crew' (decision.mode: optional) was started with the decision circuit on");

        // Act
        await observer.OnCrewFailedAsync(Failed(), refusal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("decision_circuit_mismatch", Assert.Single(Events()).GetProperty("code").GetString());
    }
}
