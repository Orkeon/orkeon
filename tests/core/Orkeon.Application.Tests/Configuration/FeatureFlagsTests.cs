using Orkeon.Application.Configuration;

namespace Orkeon.Application.Tests.Configuration;

public class FeatureFlagsTests
{
    [Fact]
    public void ShouldSetDefaultValues_WhenConstructing()
    {
        // Act
        var featureFlags = new FeatureFlags();

        // Assert
        Assert.False(featureFlags.UseNewDomainModels);
        Assert.False(featureFlags.UseAgentExecutionService);
        Assert.False(featureFlags.UseSequentialCrewOrchestrator);
        Assert.False(featureFlags.UseCrewCompositionService);
        Assert.True(featureFlags.EnableDetailedLogging);
        Assert.True(featureFlags.EnableFallback);
        Assert.Equal(0, featureFlags.TrafficPercentage);
        Assert.Empty(featureFlags.AgentIds);
        Assert.Empty(featureFlags.CrewIds);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingNewDomainModels()
    {
        // Arrange
        var featureFlags = new FeatureFlags();

        // Act
        featureFlags.UseNewDomainModels = true;

        // Assert
        Assert.True(featureFlags.UseNewDomainModels);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingUseAgentExecutionService()
    {
        // Arrange
        var featureFlags = new FeatureFlags();

        // Act
        featureFlags.UseAgentExecutionService = true;

        // Assert
        Assert.True(featureFlags.UseAgentExecutionService);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingUseSequentialCrewOrchestrator()
    {
        // Arrange
        var featureFlags = new FeatureFlags();

        // Act
        featureFlags.UseSequentialCrewOrchestrator = true;

        // Assert
        Assert.True(featureFlags.UseSequentialCrewOrchestrator);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingUseCrewCompositionService()
    {
        // Arrange
        var featureFlags = new FeatureFlags();

        // Act
        featureFlags.UseCrewCompositionService = true;

        // Assert
        Assert.True(featureFlags.UseCrewCompositionService);
    }

    [Fact]
    public void ShouldBeSettable_WhenEnablingDetailedLogging()
    {
        // Arrange
        var featureFlags = new FeatureFlags();

        // Act
        featureFlags.EnableDetailedLogging = false;

        // Assert
        Assert.False(featureFlags.EnableDetailedLogging);
    }

    [Fact]
    public void ShouldBeSettable_WhenEnablingFallback()
    {
        // Arrange
        var featureFlags = new FeatureFlags();

        // Act
        featureFlags.EnableFallback = false;

        // Assert
        Assert.False(featureFlags.EnableFallback);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingTrafficPercentage()
    {
        // Arrange
        var featureFlags = new FeatureFlags();

        // Act
        featureFlags.TrafficPercentage = 50;

        // Assert
        Assert.Equal(50, featureFlags.TrafficPercentage);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingTrafficPercentageWithBoundaryValues()
    {
        // Arrange
        var featureFlags = new FeatureFlags();

        // Act & Assert
        featureFlags.TrafficPercentage = 0;
        Assert.Equal(0, featureFlags.TrafficPercentage);

        featureFlags.TrafficPercentage = 100;
        Assert.Equal(100, featureFlags.TrafficPercentage);

        featureFlags.TrafficPercentage = -10;
        Assert.Equal(-10, featureFlags.TrafficPercentage);

        featureFlags.TrafficPercentage = 150;
        Assert.Equal(150, featureFlags.TrafficPercentage);
    }

    [Fact]
    public void ShouldBeModifiable_WhenUsingAgentIds()
    {
        // Arrange & Act
        var featureFlags = new FeatureFlags { AgentIds = ["agent-123", "agent-456"] };

        // Assert
        Assert.Equal(2, featureFlags.AgentIds.Count);
        Assert.Contains("agent-123", featureFlags.AgentIds);
        Assert.Contains("agent-456", featureFlags.AgentIds);
    }

    [Fact]
    public void ShouldBeModifiable_WhenUsingCrewIds()
    {
        // Arrange & Act
        var featureFlags = new FeatureFlags { CrewIds = ["crew-123", "crew-456"] };

        // Assert
        Assert.Equal(2, featureFlags.CrewIds.Count);
        Assert.Contains("crew-123", featureFlags.CrewIds);
        Assert.Contains("crew-456", featureFlags.CrewIds);
    }

    [Fact]
    public void ShouldUseForAgent_WithSpecificAgentIds_ShouldReturnTrueForListedAgents()
    {
        // Arrange
        var featureFlags = new FeatureFlags { AgentIds = ["agent-123", "agent-456"] };

        // Act & Assert
        Assert.True(featureFlags.ShouldUseForAgent("agent-123"));
        Assert.True(featureFlags.ShouldUseForAgent("agent-456"));
        Assert.False(featureFlags.ShouldUseForAgent("agent-789"));
    }

    [Fact]
    public void ShouldUseForAgent_WithEmptyAgentIds_ShouldFallbackToTrafficPercentage()
    {
        // Arrange
        var featureFlags = new FeatureFlags
        {
            TrafficPercentage = 50
        };

        // Act & Assert
        // Note: The actual result depends on hash of agent ID, but we can test consistency
        var agentId = "consistent-agent-id";
        var result1 = featureFlags.ShouldUseForAgent(agentId);
        var result2 = featureFlags.ShouldUseForAgent(agentId);

        Assert.Equal(result1, result2); // Should be consistent
    }

    [Fact]
    public void ShouldUseForAgent_WithZeroTrafficPercentage_ShouldFallbackToUseNewDomainModels()
    {
        // Arrange
        var featureFlags = new FeatureFlags
        {
            TrafficPercentage = 0,
            UseNewDomainModels = true
        };

        // Act
        var result = featureFlags.ShouldUseForAgent("any-agent-id");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldUseForAgent_WithFullTrafficPercentage_ShouldFallbackToUseNewDomainModels()
    {
        // Arrange
        var featureFlags = new FeatureFlags
        {
            TrafficPercentage = 100,
            UseNewDomainModels = false
        };

        // Act
        var result = featureFlags.ShouldUseForAgent("any-agent-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldUseForCrew_WithSpecificCrewIds_ShouldReturnTrueForListedCrews()
    {
        // Arrange
        var featureFlags = new FeatureFlags { CrewIds = ["crew-123", "crew-456"] };

        // Act & Assert
        Assert.True(featureFlags.ShouldUseForCrew("crew-123"));
        Assert.True(featureFlags.ShouldUseForCrew("crew-456"));
        Assert.False(featureFlags.ShouldUseForCrew("crew-789"));
    }

    [Fact]
    public void ShouldUseForCrew_WithEmptyCrewIds_ShouldFallbackToTrafficPercentage()
    {
        // Arrange
        var featureFlags = new FeatureFlags
        {
            TrafficPercentage = 50
        };

        // Act & Assert
        // Test consistency for same crew ID
        var crewId = "consistent-crew-id";
        var result1 = featureFlags.ShouldUseForCrew(crewId);
        var result2 = featureFlags.ShouldUseForCrew(crewId);

        Assert.Equal(result1, result2); // Should be consistent
    }

    [Fact]
    public void ShouldUseForCrew_WithZeroTrafficPercentage_ShouldFallbackToUseNewDomainModels()
    {
        // Arrange
        var featureFlags = new FeatureFlags
        {
            TrafficPercentage = 0,
            UseNewDomainModels = true
        };

        // Act
        var result = featureFlags.ShouldUseForCrew("any-crew-id");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldUseForCrew_WithFullTrafficPercentage_ShouldFallbackToUseNewDomainModels()
    {
        // Arrange
        var featureFlags = new FeatureFlags
        {
            TrafficPercentage = 100,
            UseNewDomainModels = false
        };

        // Act
        var result = featureFlags.ShouldUseForCrew("any-crew-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldUseForAgent_WithTrafficPercentageRouting_ShouldBeConsistent()
    {
        // Arrange
        var featureFlags = new FeatureFlags
        {
            TrafficPercentage = 25
        };

        // Act
        var agentId = "test-agent";
        var result1 = featureFlags.ShouldUseForAgent(agentId);
        var result2 = featureFlags.ShouldUseForAgent(agentId);
        var result3 = featureFlags.ShouldUseForAgent(agentId);

        // Assert - Should be consistent for same agent ID
        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
    }

    [Fact]
    public void ShouldUseForCrew_WithTrafficPercentageRouting_ShouldBeConsistent()
    {
        // Arrange
        var featureFlags = new FeatureFlags
        {
            TrafficPercentage = 75
        };

        // Act
        var crewId = "test-crew";
        var result1 = featureFlags.ShouldUseForCrew(crewId);
        var result2 = featureFlags.ShouldUseForCrew(crewId);
        var result3 = featureFlags.ShouldUseForCrew(crewId);

        // Assert - Should be consistent for same crew ID
        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
    }

    [Fact]
    public void ShouldBeIndependent_WhenUsingAgentIdsAndCrewIds()
    {
        // Arrange & Act
        var featureFlags = new FeatureFlags { AgentIds = ["agent-123"], CrewIds = ["crew-456"] };

        // Assert
        Assert.Single(featureFlags.AgentIds);
        Assert.Single(featureFlags.CrewIds);
        Assert.Contains("agent-123", featureFlags.AgentIds);
        Assert.Contains("crew-456", featureFlags.CrewIds);
        Assert.DoesNotContain("crew-456", featureFlags.AgentIds);
        Assert.DoesNotContain("agent-123", featureFlags.CrewIds);
    }

    [Fact]
    public void ShouldDistributeEvenly_WhenUsingTrafficPercentageRouting()
    {
        // Arrange
        var featureFlags = new FeatureFlags
        {
            TrafficPercentage = 50
        };

        // Act - Test with many different agent IDs
        var results = new List<bool>();
        for (int i = 0; i < 1000; i++)
        {
            var agentId = $"agent-{i}";
            results.Add(featureFlags.ShouldUseForAgent(agentId));
        }

        // Assert - Should be roughly 50% true/false (with some variance)
        var trueCount = results.Count(r => r);
        var falseCount = results.Count(r => !r);

        Assert.True(trueCount > 400 && trueCount < 600, $"Expected ~500 true results, got {trueCount}");
        Assert.True(falseCount > 400 && falseCount < 600, $"Expected ~500 false results, got {falseCount}");
    }
}
