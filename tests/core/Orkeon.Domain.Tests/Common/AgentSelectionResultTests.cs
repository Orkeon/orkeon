using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.ValueObjects;

namespace Orkeon.Domain.Tests.Common;

public class AgentSelectionResultTests
{
    #region Constructor Tests

    [Fact]
    public void ShouldCreateResult_WhenConstructingWithAllParameters()
    {
        // Arrange
        var selectedAgentId = AgentId.Create();
        var isSuccess = true;
        var reason = "Best match for the task";
        var confidenceScore = 0.85;
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        var agent3 = AgentId.Create();
        var agentScores = new Dictionary<AgentId, double>
        {
            [agent1] = 0.85,
            [agent2] = 0.72,
            [agent3] = 0.65
        };
        var consideredAgents = new List<AgentId> { agent1, agent2, agent3 };

        // Act
        var result = AgentSelectionResult.Create(
            selectedAgentId,
            isSuccess,
            reason,
            confidenceScore,
            agentScores,
            consideredAgents);

        // Assert
        Assert.Equal(selectedAgentId, result.SelectedAgentId);
        Assert.Equal(isSuccess, result.IsSuccess);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(confidenceScore, result.ConfidenceScore);
        Assert.Equal(agentScores, result.AgentScores);
        Assert.Equal(consideredAgents, result.ConsideredAgents);
    }

    [Fact]
    public void ShouldUseDefaults_WhenConstructingWithMinimalParameters()
    {
        // Arrange & Act
        var agentId = AgentId.Create();
        var result = AgentSelectionResult.Create(agentId, true);

        // Assert
        Assert.Equal(agentId, result.SelectedAgentId);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Reason);
        Assert.Equal(0.0, result.ConfidenceScore);
        Assert.Empty(result.AgentScores);
        Assert.Empty(result.ConsideredAgents);
    }

    [Fact]
    public void ShouldUseEmptyCollections_WhenConstructingWithNullOptionalParameters()
    {
        // Arrange & Act
        var result = AgentSelectionResult.Create(
            AgentId.Create(),
            true,
            reason: "Selected",
            confidenceScore: 0.9,
            agentScores: null,
            consideredAgents: null);

        // Assert
        Assert.Empty(result.AgentScores);
        Assert.Empty(result.ConsideredAgents);
    }

    [Fact]
    public void ShouldAllowNull_WhenConstructingWithNullSelectedAgentId()
    {
        // Arrange & Act
        var result = AgentSelectionResult.Create(
            null,
            false,
            "No suitable agent found");

        // Assert
        Assert.Null(result.SelectedAgentId);
        Assert.False(result.IsSuccess);
        Assert.Equal("No suitable agent found", result.Reason);
    }

    #endregion

    #region Confidence Score Normalization Tests

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(double.MaxValue)]
    public void ShouldThrow_WhenConstructingWithConfidenceAboveOne(double score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AgentSelectionResult.Create(AgentId.Create(), true, confidenceScore: score));
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(-1.0)]
    [InlineData(double.MinValue)]
    public void ShouldThrow_WhenConstructingWithConfidenceBelowZero(double score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AgentSelectionResult.Create(AgentId.Create(), true, confidenceScore: score));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.99999)]
    [InlineData(1.0)]
    public void ShouldAccept_WhenConstructingWithValidConfidenceScores(double score)
    {
        var result = AgentSelectionResult.Create(AgentId.Create(), true, confidenceScore: score);
        Assert.Equal(score, result.ConfidenceScore);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ShouldNormalizeToZero_WhenConstructingWithSpecialDoubleValues(double score)
    {
        var result = AgentSelectionResult.Create(AgentId.Create(), true, confidenceScore: score);
        Assert.Equal(0.0, result.ConfidenceScore);
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void ShouldCreateSuccessResult_WhenUsingSuccessWithDefaultConfidence()
    {
        // Arrange
        var agentId = AgentId.Create();

        // Act
        var result = AgentSelectionResult.Success(agentId);

        // Assert
        Assert.Equal(agentId, result.SelectedAgentId);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Reason);
        Assert.Equal(1.0, result.ConfidenceScore);
        Assert.Empty(result.AgentScores);
        Assert.Empty(result.ConsideredAgents);
    }

    [Fact]
    public void ShouldCreateSuccessResult_WhenUsingSuccessWithCustomConfidence()
    {
        // Arrange
        var agentId = AgentId.Create();
        var confidence = 0.75;

        // Act
        var result = AgentSelectionResult.Success(agentId, confidence);

        // Assert
        Assert.Equal(agentId, result.SelectedAgentId);
        Assert.True(result.IsSuccess);
        Assert.Equal(confidence, result.ConfidenceScore);
    }

    [Fact]
    public void ShouldCreateFailureResult_WhenUsingFailure()
    {
        // Arrange
        var reason = "No agents available for the task";

        // Act
        var result = AgentSelectionResult.Failure(reason);

        // Assert
        Assert.Null(result.SelectedAgentId);
        Assert.False(result.IsSuccess);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(0.0, result.ConfidenceScore);
        Assert.Empty(result.AgentScores);
        Assert.Empty(result.ConsideredAgents);
    }

    [Fact]
    public void ShouldAllowNull_WhenUsingFailureWithNullReason()
    {
        // Act
        var result = AgentSelectionResult.Failure(null!);

        // Assert
        Assert.Null(result.SelectedAgentId);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Reason);
    }

    #endregion

    #region Computed Properties Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsHighConfidenceWithScoreAbove08()
    {
        // Arrange
        var highConfidenceScores = new[] { 0.8, 0.85, 0.9, 0.95, 1.0 };

        // Act & Assert
        foreach (var score in highConfidenceScores)
        {
            var result = AgentSelectionResult.Create(AgentId.Create(), true, confidenceScore: score);
            Assert.True(result.IsHighConfidence);
        }
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsHighConfidenceWithScoreBelow08()
    {
        // Arrange
        var lowConfidenceScores = new[] { 0.0, 0.3, 0.5, 0.7, 0.79, 0.799999 };

        // Act & Assert
        foreach (var score in lowConfidenceScores)
        {
            var result = AgentSelectionResult.Create(AgentId.Create(), true, confidenceScore: score);
            Assert.False(result.IsHighConfidence);
        }
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsLowConfidenceWithScoreBelow05()
    {
        // Arrange
        var lowConfidenceScores = new[] { 0.0, 0.1, 0.3, 0.49, 0.499999 };

        // Act & Assert
        foreach (var score in lowConfidenceScores)
        {
            var result = AgentSelectionResult.Create(AgentId.Create(), true, confidenceScore: score);
            Assert.True(result.IsLowConfidence);
        }
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsLowConfidenceWithScoreAbove05()
    {
        // Arrange
        var highConfidenceScores = new[] { 0.5, 0.6, 0.8, 0.9, 1.0 };

        // Act & Assert
        foreach (var score in highConfidenceScores)
        {
            var result = AgentSelectionResult.Create(AgentId.Create(), true, confidenceScore: score);
            Assert.False(result.IsLowConfidence);
        }
    }

    #endregion

    #region Backward Compatibility Tests

    [Fact]
    public void ShouldReturnSelectedAgentId_WhenUsingSelectedAgent()
    {
        // Arrange
        var agentId = AgentId.Create();
        var result = AgentSelectionResult.Create(agentId, true);

        // Act & Assert
        Assert.Equal(agentId, result.SelectedAgent);
        Assert.Equal(result.SelectedAgentId, result.SelectedAgent);
    }

    [Fact]
    public void ShouldReturnConfidenceScore_WhenScoring()
    {
        // Arrange
        var confidence = 0.88;
        var result = AgentSelectionResult.Create(AgentId.Create(), true, confidenceScore: confidence);

        // Act & Assert
        Assert.Equal(confidence, result.Score);
        Assert.Equal(result.ConfidenceScore, result.Score);
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var agent1 = AgentId.Create();
        var agentScores = new Dictionary<AgentId, double> { [agent1] = 0.8 };
        var consideredAgents = new List<AgentId> { agent1 };

        var result1 = AgentSelectionResult.Create(
            agent1,
            true,
            "Best match",
            0.8,
            agentScores,
            consideredAgents);

        var result2 = AgentSelectionResult.Create(
            agent1,
            true,
            "Best match",
            0.8,
            agentScores,
            consideredAgents);

        // Act & Assert
        Assert.Equal(result1, result2);
        Assert.True(result1 == result2);
        Assert.False(result1 != result2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentSelectedAgent()
    {
        // Arrange
        var result1 = AgentSelectionResult.Create(AgentId.Create(), true);
        var result2 = AgentSelectionResult.Create(AgentId.Create(), true);

        // Act & Assert
        Assert.NotEqual(result1, result2);
        Assert.False(result1 == result2);
        Assert.True(result1 != result2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentSuccess()
    {
        // Arrange
        var agentId = AgentId.Create();
        var result1 = AgentSelectionResult.Create(agentId, true);
        var result2 = AgentSelectionResult.Create(agentId, false);

        // Act & Assert
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentReason()
    {
        // Arrange
        var agentId = AgentId.Create();
        var result1 = AgentSelectionResult.Create(agentId, true, "Reason 1");
        var result2 = AgentSelectionResult.Create(agentId, true, "Reason 2");

        // Act & Assert
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentConfidence()
    {
        // Arrange
        var agentId = AgentId.Create();
        var result1 = AgentSelectionResult.Create(agentId, true, confidenceScore: 0.7);
        var result2 = AgentSelectionResult.Create(agentId, true, confidenceScore: 0.8);

        // Act & Assert
        Assert.NotEqual(result1, result2);
    }

    #endregion

    #region Record Features Tests

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var result = AgentSelectionResult.Create(
            AgentId.Create(),
            true,
            "Selected successfully",
            0.85);

        // Act
        var toString = result.ToString();

        // Assert
        Assert.Contains("AgentSelectionResult", toString);
        Assert.Contains("IsSuccess = True", toString);
    }

    [Fact]
    public void ShouldReturnSameHash_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var agent = AgentId.Create();
        var scores = new Dictionary<AgentId, double> { [agent] = 0.8 };
        var agents = new List<AgentId> { agent };

        var result1 = AgentSelectionResult.Create(agent, true, "reason", 0.8, scores, agents);
        var result2 = AgentSelectionResult.Create(agent, true, "reason", 0.8, scores, agents);

        // Act
        var hash1 = result1.GetHashCode();
        var hash2 = result2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldAgentSelectionWorkflow_WhenUsingComplexScenario()
    {
        // Arrange - Simulate an agent selection workflow
        var agentWeb = AgentId.Create();
        var agentData = AgentId.Create();
        var agentMl = AgentId.Create();
        var agentReport = AgentId.Create();
        var candidateAgents = new List<AgentId> { agentWeb, agentData, agentMl, agentReport };

        // Simulate scoring each agent
        var agentScores = new Dictionary<AgentId, double>
        {
            [agentWeb] = 0.3,
            [agentData] = 0.85,
            [agentMl] = 0.6,
            [agentReport] = 0.75
        };

        // Act - Select the best agent
        var bestAgent = agentScores
            .OrderByDescending(kvp => kvp.Value)
            .First();

        var result = AgentSelectionResult.Create(
            bestAgent.Key,
            true,
            "Selected based on highest score",
            bestAgent.Value,
            agentScores,
            candidateAgents);

        // Assert
        Assert.Equal(agentData, result.SelectedAgentId);
        Assert.True(result.IsSuccess);
        Assert.True(result.IsHighConfidence);
        Assert.False(result.IsLowConfidence);
        Assert.Equal(4, result.ConsideredAgents.Count);
        Assert.Equal(0.85, result.AgentScores[agentData]);
    }

    [Fact]
    public void ShouldNoSuitableAgentFound_WhenUsingComplexScenario()
    {
        // Arrange - All agents have low scores
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        var agent3 = AgentId.Create();
        var agentScores = new Dictionary<AgentId, double>
        {
            [agent1] = 0.2,
            [agent2] = 0.15,
            [agent3] = 0.25
        };

        var minConfidenceThreshold = 0.5;

        // Act
        var bestScore = agentScores.Values.Max();
        AgentSelectionResult result;

        if (bestScore < minConfidenceThreshold)
        {
            result = AgentSelectionResult.Failure(
                $"No agent met the minimum confidence threshold of {minConfidenceThreshold}");
        }
        else
        {
            var bestAgent = agentScores.OrderByDescending(kvp => kvp.Value).First();
            result = AgentSelectionResult.Create(
                bestAgent.Key,
                true,
                confidenceScore: bestAgent.Value,
                agentScores: agentScores,
                consideredAgents: [agent1, agent2, agent3]);
        }

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.SelectedAgentId);
        Assert.Contains("minimum confidence threshold", result.Reason);
        Assert.Equal(0.0, result.ConfidenceScore);
    }

    [Fact]
    public void ShouldMultiRoundSelection_WhenUsingComplexScenario()
    {
        // Arrange - Simulate multiple rounds of selection
        var rounds = new List<AgentSelectionResult>();

        var agentA = AgentId.Create();
        var agentB = AgentId.Create();
        var agentC = AgentId.Create();

        // Round 1: Initial selection
        rounds.Add(AgentSelectionResult.Create(
            agentB, true, "Round 1: Initial selection", 0.8,
            new Dictionary<AgentId, double> { [agentA] = 0.7, [agentB] = 0.8, [agentC] = 0.6 },
            [agentA, agentB, agentC]));

        // Round 2: agent-b failed, reselect
        rounds.Add(AgentSelectionResult.Create(
            agentA, true, "Round 2: agent-b unavailable", 0.7,
            new Dictionary<AgentId, double> { [agentA] = 0.7, [agentC] = 0.65 },
            [agentA, agentC]));

        // Round 3: All agents below threshold
        rounds.Add(AgentSelectionResult.Failure(
            "Round 3: All agents below acceptable performance threshold"));

        // Assert
        Assert.Equal(3, rounds.Count);
        Assert.True(rounds[0].IsSuccess);
        Assert.True(rounds[0].IsHighConfidence);
        Assert.True(rounds[1].IsSuccess);
        Assert.False(rounds[1].IsHighConfidence);
        Assert.False(rounds[2].IsSuccess);

        Assert.Equal(agentB, rounds[0].SelectedAgentId);
        Assert.Equal(agentA, rounds[1].SelectedAgentId);
        Assert.Null(rounds[2].SelectedAgentId);
    }

    #endregion
}
