using Orkeon.Infrastructure.Checkpointing;

namespace Orkeon.Infrastructure.Tests.Checkpointing;

/// <summary>
/// #430: the circuit a run was started with, read from its checkpoint to refuse resuming an optional crew
/// under the other one: on when a decision came from the model, off when one came from the relay or the
/// fallback, unknown when none is recorded.
/// </summary>
public sealed class RecordedDecisionCircuitTests
{
    public static TheoryData<string, string?, bool?> Cases => new()
    {
        { "a model decision", """{"success":true,"decisionSource":"model"}""", true },
        { "a relay decision", """{"success":true,"decisionSource":"relay"}""", false },
        { "a fallback decision", """{"success":true,"decisionSource":"fallback"}""", false },
        { "an agent task only", """{"success":true,"output":"done"}""", null },
        { "an output of another shape", "not json", null },
        { "no output", null, null },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("BR", "CH-430")]
    public void ShouldReadTheCircuitFromTheRecordedDecisionSource_WhenTheRunIsResumed(string name, string? output, bool? expected)
    {
        // Arrange
        var outputs = new Dictionary<string, string?> { ["intake"] = """{"success":true}""", ["triage"] = output };

        // Act
        var circuit = CheckpointingAgentExecutionService.RecordedDecisionCircuit(outputs);

        // Assert
        Assert.True(expected == circuit, $"{name}: expected {expected}, got {circuit}");
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public void ShouldReadNoCircuit_WhenTheOnlySourceRecordedIsOfATaskTheCrewNoLongerDeclares()
    {
        // Arrange
        var outputs = new Dictionary<string, string?>
        {
            ["intake#1"] = """{"success":true}""",
            ["routing#1"] = """{"success":true,"decisionSource":"model"}""",
            ["triage#1"] = """{"success":true,"output":"route: bug"}""",
        };

        // Act
        var circuit = CheckpointingAgentExecutionService.RecordedDecisionCircuit(outputs, ["triage"]);

        // Assert
        Assert.Null(circuit);
    }

    [Theory]
    [InlineData("triage#1")]
    [InlineData("triage@2#1")]
    [Trait("BR", "CH-430")]
    public void ShouldReadTheCircuit_WhenTheSourceIsOfADecisionTaskTheCrewStillDeclares(string key)
    {
        // Arrange
        var outputs = new Dictionary<string, string?> { ["triager#1"] = """{"success":true,"decisionSource":"model"}""", [key] = """{"success":true,"decisionSource":"relay"}""" };

        // Act
        var circuit = CheckpointingAgentExecutionService.RecordedDecisionCircuit(outputs, ["triage"]);

        // Assert
        Assert.False(circuit);
    }
}
