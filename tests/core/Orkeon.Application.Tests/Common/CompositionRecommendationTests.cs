using Orkeon.Domain.Agent.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Application.Crew;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Application.Tests.Common;

public class CompositionRecommendationTests
{
    #region Test Helpers

    private static DomainAgent CreateTestAgent(string role = RoleDeveloper, string goal = GoalWriteCode)
    {
        return DomainAgent.Create(
            role: AgentRole.From(role),
            goal: AgentGoal.From(goal),
            backstory: AgentBackstory.From($"Experienced {role}")
        );
    }

    #endregion

    [Fact]
    public void ShouldCreateRecommendation_WhenConstructingWithValidParameters()
    {
        // Arrange
        var title = "Optimal Team Composition";
        var rationale = "This team composition provides the best balance of skills";
        var agents = new List<DomainAgent>
        {
            CreateTestAgent(RoleDeveloper),
            CreateTestAgent("Tester")
        };
        var confidenceScore = 0.85;
        var metadata = new Dictionary<string, object>
        {
            ["complexity"] = "medium",
            ["estimated_time"] = "2 weeks"
        };

        // Act
        var recommendation = new CompositionRecommendation(
            title,
            rationale,
            agents,
            confidenceScore,
            metadata
        );

        // Assert
        Assert.Equal(title, recommendation.Title);
        Assert.Equal(rationale, recommendation.Rationale);
        Assert.Equal(agents, recommendation.RecommendedAgents);
        Assert.Equal(confidenceScore, recommendation.ConfidenceScore);
        Assert.Equal(metadata, recommendation.Metadata);
    }

    [Fact]
    public void ShouldCreateRecommendation_WhenConstructingWithNullMetadata()
    {
        // Arrange
        var title = "Basic Team";
        var rationale = "Simple team setup";
        var agents = new List<DomainAgent> { CreateTestAgent() };
        var confidenceScore = 0.7;

        // Act
        var recommendation = new CompositionRecommendation(
            title,
            rationale,
            agents,
            confidenceScore,
            null
        );

        // Assert
        Assert.Equal(title, recommendation.Title);
        Assert.Null(recommendation.Metadata);
    }

    [Fact]
    public void ShouldCreateRecommendation_WhenConstructingWithEmptyAgentsList()
    {
        // Arrange
        var title = "No Agents Needed";
        var rationale = "Task can be completed without agents";
        var agents = new List<DomainAgent>();
        var confidenceScore = 1.0;

        // Act
        var recommendation = new CompositionRecommendation(
            title,
            rationale,
            agents,
            confidenceScore
        );

        // Assert
        Assert.Empty(recommendation.RecommendedAgents);
    }

    [Fact]
    public void ShouldBeEqual_WhenUsingEqualityWithSameValues()
    {
        // Arrange
        var agents = new List<DomainAgent> { CreateTestAgent() };
        var metadata = new Dictionary<string, object> { ["key"] = "value" };

        var recommendation1 = new CompositionRecommendation(
            "Title",
            "Rationale",
            agents,
            0.8,
            metadata
        );

        var recommendation2 = new CompositionRecommendation(
            "Title",
            "Rationale",
            agents,
            0.8,
            metadata
        );

        // Act & Assert
        Assert.Equal(recommendation1, recommendation2);
        Assert.True(recommendation1.Equals(recommendation2));
        Assert.Equal(recommendation1.GetHashCode(), recommendation2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenUsingEqualityWithDifferentValues()
    {
        // Arrange
        var recommendation1 = new CompositionRecommendation(
            "Title1",
            "Rationale",
            [],
            0.8
        );

        var recommendation2 = new CompositionRecommendation(
            "Title2",
            "Rationale",
            [],
            0.8
        );

        // Act & Assert
        Assert.NotEqual(recommendation1, recommendation2);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingWithModifyingTitle()
    {
        // Arrange
        var original = new CompositionRecommendation(
            "Original Title",
            "Rationale",
            [],
            0.75
        );

        // Act
        var modified = original with { Title = "Modified Title" };

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal("Modified Title", modified.Title);
        Assert.Equal(original.Rationale, modified.Rationale);
        Assert.Equal(original.ConfidenceScore, modified.ConfidenceScore);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingWithModifyingConfidenceScore()
    {
        // Arrange
        var original = new CompositionRecommendation(
            "Title",
            "Rationale",
            [CreateTestAgent()],
            0.6
        );

        // Act
        var modified = original with { ConfidenceScore = 0.95 };

        // Assert
        Assert.Equal(0.95, modified.ConfidenceScore);
        Assert.Equal(original.Title, modified.Title);
        Assert.Equal(original.RecommendedAgents, modified.RecommendedAgents);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingWithAddingMetadata()
    {
        // Arrange
        var original = new CompositionRecommendation(
            "Title",
            "Rationale",
            [],
            0.8,
            null
        );

        var newMetadata = new Dictionary<string, object> { ["added"] = "metadata" };

        // Act
        var modified = original with { Metadata = newMetadata };

        // Assert
        Assert.NotNull(modified.Metadata);
        Assert.Equal("metadata", modified.Metadata["added"]);
        Assert.Null(original.Metadata);
    }

    [Fact]
    public void ShouldReturnExpectedFormat_WhenCallingToString()
    {
        // Arrange
        var recommendation = new CompositionRecommendation(
            "Test Title",
            "Test Rationale",
            [CreateTestAgent()],
            0.9
        );

        // Act
        var result = recommendation.ToString();

        // Assert
        Assert.Contains("Test Title", result);
        Assert.Contains("Test Rationale", result);
        Assert.Contains("0.9", result);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(-0.1)] // Edge case - negative score
    [InlineData(1.5)]  // Edge case - score > 1
    public void ShouldAcceptAll_WhenConstructingWithVariousConfidenceScores(double score)
    {
        // Arrange & Act
        var recommendation = new CompositionRecommendation(
            "Title",
            "Rationale",
            [],
            score
        );

        // Assert
        Assert.Equal(score, recommendation.ConfidenceScore);
    }

    [Fact]
    public void ShouldMaintainOrder_WhenConstructingWithMultipleAgents()
    {
        // Arrange
        var agent1 = CreateTestAgent(RoleDeveloper, GoalWriteCode);
        var agent2 = CreateTestAgent("Designer", "Create UI");
        var agent3 = CreateTestAgent("Tester", "Test features");
        var agents = new List<DomainAgent> { agent1, agent2, agent3 };

        // Act
        var recommendation = new CompositionRecommendation(
            "Team",
            "Full team",
            agents,
            0.85
        );

        // Assert
        Assert.Equal(3, recommendation.RecommendedAgents.Count);
        Assert.Equal(RoleDeveloper, recommendation.RecommendedAgents[0].Role.Value);
        Assert.Equal("Designer", recommendation.RecommendedAgents[1].Role.Value);
        Assert.Equal("Tester", recommendation.RecommendedAgents[2].Role.Value);
    }

    [Fact]
    public void ShouldPreserveStructure_WhenConstructingWithComplexMetadata()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            ["string_value"] = "test",
            ["int_value"] = 42,
            ["bool_value"] = true,
            ["nested"] = new Dictionary<string, object>
            {
                ["inner_key"] = "inner_value"
            },
            ["array"] = new[] { 1, 2, 3 }
        };

        // Act
        var recommendation = new CompositionRecommendation(
            "Title",
            "Rationale",
            [],
            0.75,
            metadata
        );

        // Assert
        Assert.Equal("test", recommendation.Metadata!["string_value"]);
        Assert.Equal(42, recommendation.Metadata["int_value"]);
        Assert.True((bool)recommendation.Metadata["bool_value"]);
        Assert.NotNull(recommendation.Metadata["nested"]);
        Assert.NotNull(recommendation.Metadata["array"]);
    }

    [Fact]
    public void ShouldAccept_WhenConstructingWithNullTitle()
    {
        // Arrange & Act
        var recommendation = new CompositionRecommendation(
            null!,
            "Rationale",
            [],
            0.5
        );

        // Assert
        Assert.Null(recommendation.Title);
    }

    [Fact]
    public void ShouldAccept_WhenConstructingWithNullRationale()
    {
        // Arrange & Act
        var recommendation = new CompositionRecommendation(
            "Title",
            null!,
            [],
            0.5
        );

        // Assert
        Assert.Null(recommendation.Rationale);
    }

    [Fact]
    public void ShouldAccept_WhenConstructingWithNullAgentsList()
    {
        // Arrange & Act
        var recommendation = new CompositionRecommendation(
            "Title",
            "Rationale",
            null!,
            0.5
        );

        // Assert
        Assert.Null(recommendation.RecommendedAgents);
    }

    [Fact]
    public void ShouldAccept_WhenConstructingWithEmptyStrings()
    {
        // Arrange & Act
        var recommendation = new CompositionRecommendation(
            "",
            "",
            [],
            0.5
        );

        // Assert
        Assert.Equal("", recommendation.Title);
        Assert.Equal("", recommendation.Rationale);
    }

    [Fact]
    public void ShouldAccept_WhenConstructingWithVeryLongTitle()
    {
        // Arrange
        var longTitle = new string('A', 10000);

        // Act
        var recommendation = new CompositionRecommendation(
            longTitle,
            "Rationale",
            [],
            0.8
        );

        // Assert
        Assert.Equal(longTitle, recommendation.Title);
    }

    [Fact]
    public void ShouldAccept_WhenConstructingWithVeryLongRationale()
    {
        // Arrange
        var longRationale = new string('B', 50000);

        // Act
        var recommendation = new CompositionRecommendation(
            "Title",
            longRationale,
            [],
            0.7
        );

        // Assert
        Assert.Equal(longRationale, recommendation.Rationale);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenConstructingWithManyAgents()
    {
        // Arrange
        var agents = new List<DomainAgent>();
        for (int i = 0; i < 100; i++)
        {
            agents.Add(CreateTestAgent($"Agent{i}", $"Goal{i}"));
        }

        // Act
        var recommendation = new CompositionRecommendation(
            "Large Team",
            "Many agents needed",
            agents,
            0.6
        );

        // Assert
        Assert.Equal(100, recommendation.RecommendedAgents.Count);
    }

    [Fact]
    public void ShouldAcceptAll_WhenConstructingWithDuplicateAgents()
    {
        // Arrange
        var agent = CreateTestAgent();
        var agents = new List<DomainAgent> { agent, agent, agent };

        // Act
        var recommendation = new CompositionRecommendation(
            "Title",
            "Rationale",
            agents,
            0.9
        );

        // Assert
        Assert.Equal(3, recommendation.RecommendedAgents.Count);
        Assert.All(recommendation.RecommendedAgents, a => Assert.Same(agent, a));
    }

    [Fact]
    public void ShouldAccept_WhenConstructingWithSpecialCharactersInTitle()
    {
        // Arrange
        var specialTitle = "Title with special chars: @#$%^&*()[]{}|\\<>?,./~`\"'";

        // Act
        var recommendation = new CompositionRecommendation(
            specialTitle,
            "Rationale",
            [],
            0.5
        );

        // Assert
        Assert.Equal(specialTitle, recommendation.Title);
    }

    [Fact]
    public void ShouldAccept_WhenConstructingWithUnicodeInRationale()
    {
        // Arrange
        var unicodeRationale = "Rationale with unicode: 😀 🌍 中文 عربي 日本語";

        // Act
        var recommendation = new CompositionRecommendation(
            "Title",
            unicodeRationale,
            [],
            0.5
        );

        // Assert
        Assert.Equal(unicodeRationale, recommendation.Rationale);
    }

    [Fact]
    public void ShouldAccept_WhenConstructingWithNaNConfidenceScore()
    {
        // Arrange & Act
        var recommendation = new CompositionRecommendation(
            "Title",
            "Rationale",
            [],
            double.NaN
        );

        // Assert
        Assert.True(double.IsNaN(recommendation.ConfidenceScore));
    }

    [Fact]
    public void ShouldAccept_WhenConstructingWithInfinityConfidenceScore()
    {
        // Arrange & Act
        var recommendation = new CompositionRecommendation(
            "Title",
            "Rationale",
            [],
            double.PositiveInfinity
        );

        // Assert
        Assert.True(double.IsPositiveInfinity(recommendation.ConfidenceScore));
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingWithModifyingAgents()
    {
        // Arrange
        var original = new CompositionRecommendation(
            "Title",
            "Rationale",
            [CreateTestAgent("Agent1")],
            0.7
        );

        var newAgents = new List<DomainAgent> { CreateTestAgent("Agent2"), CreateTestAgent("Agent3") };

        // Act
        var modified = original with { RecommendedAgents = newAgents };

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal(2, modified.RecommendedAgents.Count);
        Assert.Single(original.RecommendedAgents);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingWithModifyingRationale()
    {
        // Arrange
        var original = new CompositionRecommendation(
            "Title",
            "Original Rationale",
            [],
            0.8
        );

        // Act
        var modified = original with { Rationale = "Modified Rationale" };

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal("Modified Rationale", modified.Rationale);
        Assert.Equal("Original Rationale", original.Rationale);
    }

    [Fact]
    public void ShouldExtractAllProperties_WhenUsingDeconstruct()
    {
        // Arrange
        var agents = new List<DomainAgent> { CreateTestAgent() };
        var metadata = new Dictionary<string, object> { ["key"] = "value" };
        var recommendation = new CompositionRecommendation(
            "Title",
            "Rationale",
            agents,
            0.9,
            metadata
        );

        // Act
        var (title, rationale, recommendedAgents, confidenceScore, meta) = recommendation;

        // Assert
        Assert.Equal("Title", title);
        Assert.Equal("Rationale", rationale);
        Assert.Same(agents, recommendedAgents);
        Assert.Equal(0.9, confidenceScore);
        Assert.Same(metadata, meta);
    }

    [Fact]
    public void ShouldNotThrow_WhenCallingGetHashCodeWithNullValues()
    {
        // Arrange
        var recommendation = new CompositionRecommendation(
            null!,
            null!,
            null!,
            0.5,
            null
        );

        // Act & Assert
        var hashCode = recommendation.GetHashCode();
        Assert.IsType<int>(hashCode);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingEqualityWithNull()
    {
        // Arrange
        var recommendation = new CompositionRecommendation(
            "Title",
            "Rationale",
            [],
            0.5
        );

        // Act & Assert
        Assert.False(recommendation.Equals(null));
        Assert.NotNull(recommendation);
        Assert.NotNull(recommendation);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingEqualityWithDifferentType()
    {
        // Arrange
        var recommendation = new CompositionRecommendation(
            "Title",
            "Rationale",
            [],
            0.5
        );

        // Act & Assert
        Assert.False(recommendation.Equals("not a recommendation"));
        Assert.False(recommendation.Equals(42));
    }
}
