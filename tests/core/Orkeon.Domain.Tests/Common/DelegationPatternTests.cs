using Orkeon.Domain.Delegation;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Common;

public class DelegationPatternTests
{
    #region Constructor Tests

    [Fact]
    public void ShouldCreateDelegationPattern_WhenConstructingWithAllParameters()
    {
        // Arrange
        var name = "LoadBalancing";
        var description = "Distributes tasks evenly among available agents";
        var applicableScenarios = new List<string>
        {
            "High workload",
            "Multiple similar agents",
            "Parallel processing"
        };
        var parameters = new Dictionary<string, object>
        {
            ["maxAgentsPerTask"] = 3,
            ["loadThreshold"] = 0.8,
            ["algorithm"] = "RoundRobin",
            ["timeout"] = TimeoutStandard
        };
        var successRate = 0.85;
        var usageCount = 150;

        // Act
        var pattern = new DelegationPattern(
            name,
            description,
            applicableScenarios,
            parameters,
            successRate,
            usageCount);

        // Assert
        Assert.Equal(name, pattern.Name);
        Assert.Equal(description, pattern.Description);
        Assert.Equal(applicableScenarios, pattern.ApplicableScenarios);
        Assert.Equal(parameters, pattern.Parameters);
        Assert.Equal(successRate, pattern.SuccessRate);
        Assert.Equal(usageCount, pattern.UsageCount);
    }

    [Fact]
    public void ShouldUseDefaults_WhenConstructingWithMinimalParameters()
    {
        // Arrange
        var name = "SimplePattern";
        var description = "A basic delegation pattern";

        // Act
        var pattern = new DelegationPattern(name, description);

        // Assert
        Assert.Equal(name, pattern.Name);
        Assert.Equal(description, pattern.Description);
        Assert.Empty(pattern.ApplicableScenarios);
        Assert.Empty(pattern.Parameters);
        Assert.Equal(0.0, pattern.SuccessRate);
        Assert.Equal(0, pattern.UsageCount);
    }

    [Fact]
    public void ShouldUseEmptyCollections_WhenConstructingWithNullOptionalParameters()
    {
        // Arrange
        var name = "NullablePattern";
        var description = "Testing null parameters";

        // Act
        var pattern = new DelegationPattern(
            name,
            description,
            applicableScenarios: null,
            parameters: null,
            successRate: 0.7,
            usageCount: 5);

        // Assert
        Assert.NotNull(pattern.ApplicableScenarios);
        Assert.Empty(pattern.ApplicableScenarios);
        Assert.NotNull(pattern.Parameters);
        Assert.Empty(pattern.Parameters);
        Assert.Equal(0.7, pattern.SuccessRate);
        Assert.Equal(5, pattern.UsageCount);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void ShouldThrow_WhenConstructingWithNullName()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new DelegationPattern(null!, "description"));
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithNullDescription()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new DelegationPattern("name", null!));
        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void ShouldClampToOne_WhenConstructingWithSuccessRateAboveOne()
    {
        // Arrange & Act
        var pattern = new DelegationPattern(
            "HighSuccess",
            "Testing success rate clamping",
            successRate: 1.5);

        // Assert
        Assert.Equal(1.0, pattern.SuccessRate);
    }

    [Fact]
    public void ShouldClampToZero_WhenConstructingWithSuccessRateBelowZero()
    {
        // Arrange & Act
        var pattern = new DelegationPattern(
            "LowSuccess",
            "Testing success rate clamping",
            successRate: -0.5);

        // Assert
        Assert.Equal(0.0, pattern.SuccessRate);
    }

    [Fact]
    public void ShouldClampCorrectly_WhenConstructingWithVariousSuccessRates()
    {
        // Arrange
        var testCases = new[]
        {
            (input: 0.5, expected: 0.5),
            (input: 0.0, expected: 0.0),
            (input: 1.0, expected: 1.0),
            (input: 2.0, expected: 1.0),
            (input: -1.0, expected: 0.0),
            (input: 0.99999, expected: 0.99999),
            (input: double.MaxValue, expected: 1.0),
            (input: double.MinValue, expected: 0.0),
            (input: double.NaN, expected: 0.0),
            (input: double.PositiveInfinity, expected: 1.0),
            (input: double.NegativeInfinity, expected: 0.0)
        };

        // Act & Assert
        foreach (var (input, expected) in testCases)
        {
            var pattern = new DelegationPattern("Test", "Test", successRate: input);
            Assert.Equal(expected, pattern.SuccessRate);
        }
    }

    [Fact]
    public void ShouldClampToZero_WhenConstructingWithNegativeUsageCount()
    {
        // Arrange & Act
        var pattern = new DelegationPattern(
            "NegativeUsage",
            "Testing usage count clamping",
            usageCount: -10);

        // Assert
        Assert.Equal(0, pattern.UsageCount);
    }

    [Fact]
    public void ShouldClampCorrectly_WhenConstructingWithVariousUsageCounts()
    {
        // Arrange
        var testCases = new[]
        {
            (input: 0, expected: 0),
            (input: 1, expected: 1),
            (input: 100, expected: 100),
            (input: -1, expected: 0),
            (input: -100, expected: 0),
            (input: int.MaxValue, expected: int.MaxValue),
            (input: int.MinValue, expected: 0)
        };

        // Act & Assert
        foreach (var (input, expected) in testCases)
        {
            var pattern = new DelegationPattern("Test", "Test", usageCount: input);
            Assert.Equal(expected, pattern.UsageCount);
        }
    }

    #endregion

    #region Computed Properties Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsProvenWithHighUsageAndSuccessRate()
    {
        // Arrange
        var provenCases = new[]
        {
            (usage: 11, success: 0.81),
            (usage: 50, success: 0.9),
            (usage: 100, success: 0.85),
            (usage: 1000, success: 0.95)
        };

        // Act & Assert
        foreach (var (usage, success) in provenCases)
        {
            var pattern = new DelegationPattern(
                "Proven",
                "A proven pattern",
                usageCount: usage,
                successRate: success);
            Assert.True(pattern.IsProven);
        }
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsProvenWithLowUsageOrSuccessRate()
    {
        // Arrange
        var unprovenCases = new[]
        {
            (usage: 10, success: 0.9),    // Usage not > 10
            (usage: 11, success: 0.8),    // Success not > 0.8
            (usage: 5, success: 0.95),    // Low usage
            (usage: 100, success: 0.5),   // Low success
            (usage: 0, success: 0.0)      // Both low
        };

        // Act & Assert
        foreach (var (usage, success) in unprovenCases)
        {
            var pattern = new DelegationPattern(
                "Unproven",
                "An unproven pattern",
                usageCount: usage,
                successRate: success);
            Assert.False(pattern.IsProven);
        }
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsExperimentalWithLowUsage()
    {
        // Arrange
        var experimentalCases = new[] { 0, 1, 2, 3, 4 };

        // Act & Assert
        foreach (var usage in experimentalCases)
        {
            var pattern = new DelegationPattern(
                "Experimental",
                "An experimental pattern",
                usageCount: usage);
            Assert.True(pattern.IsExperimental);
        }
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsExperimentalWithHighUsage()
    {
        // Arrange
        var nonExperimentalCases = new[] { 5, 10, 50, 100, 1000 };

        // Act & Assert
        foreach (var usage in nonExperimentalCases)
        {
            var pattern = new DelegationPattern(
                "NonExperimental",
                "A non-experimental pattern",
                usageCount: usage);
            Assert.False(pattern.IsExperimental);
        }
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsReliableWithHighSuccessRate()
    {
        // Arrange
        var reliableRates = new[] { 0.91, 0.95, 0.99, 1.0 };

        // Act & Assert
        foreach (var rate in reliableRates)
        {
            var pattern = new DelegationPattern(
                "Reliable",
                "A reliable pattern",
                successRate: rate);
            Assert.True(pattern.IsReliable);
        }
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsReliableWithLowSuccessRate()
    {
        // Arrange
        var unreliableRates = new[] { 0.0, 0.5, 0.8, 0.89, 0.9 };

        // Act & Assert
        foreach (var rate in unreliableRates)
        {
            var pattern = new DelegationPattern(
                "Unreliable",
                "An unreliable pattern",
                successRate: rate);
            Assert.False(pattern.IsReliable);
        }
    }

    [Fact]
    public void ShouldWorkTogether_WhenUsingComputedProperties()
    {
        // Arrange
        var patterns = new[]
        {
            new DelegationPattern("New", "Just created", usageCount: 0, successRate: 0.0),
            new DelegationPattern("Experimental", "Being tested", usageCount: 3, successRate: 0.7),
            new DelegationPattern("Promising", "Good results", usageCount: 8, successRate: 0.85),
            new DelegationPattern("Proven", "Well established", usageCount: 50, successRate: 0.92),
            new DelegationPattern("Failing", "Poor performance", usageCount: 20, successRate: 0.3)
        };

        // Act & Assert
        var newPattern = patterns[0];
        Assert.True(newPattern.IsExperimental);
        Assert.False(newPattern.IsProven);
        Assert.False(newPattern.IsReliable);

        var experimentalPattern = patterns[1];
        Assert.True(experimentalPattern.IsExperimental);
        Assert.False(experimentalPattern.IsProven);
        Assert.False(experimentalPattern.IsReliable);

        var promisingPattern = patterns[2];
        Assert.False(promisingPattern.IsExperimental);
        Assert.False(promisingPattern.IsProven); // Not enough usage
        Assert.False(promisingPattern.IsReliable);

        var provenPattern = patterns[3];
        Assert.False(provenPattern.IsExperimental);
        Assert.True(provenPattern.IsProven);
        Assert.True(provenPattern.IsReliable);

        var failingPattern = patterns[4];
        Assert.False(failingPattern.IsExperimental);
        Assert.False(failingPattern.IsProven); // Low success rate
        Assert.False(failingPattern.IsReliable);
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var scenarios = new List<string> { "scenario1", "scenario2" };
        var parameters = new Dictionary<string, object> { ["key"] = "value" };

        var pattern1 = new DelegationPattern(
            "Pattern",
            "Description",
            scenarios,
            parameters,
            0.8,
            100);

        var pattern2 = new DelegationPattern(
            "Pattern",
            "Description",
            scenarios,
            parameters,
            0.8,
            100);

        // Act & Assert
        Assert.Equal(pattern1, pattern2);
        Assert.True(pattern1 == pattern2);
        Assert.False(pattern1 != pattern2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentName()
    {
        // Arrange
        var pattern1 = new DelegationPattern("Pattern1", "Description");
        var pattern2 = new DelegationPattern("Pattern2", "Description");

        // Act & Assert
        Assert.NotEqual(pattern1, pattern2);
        Assert.False(pattern1 == pattern2);
        Assert.True(pattern1 != pattern2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentDescription()
    {
        // Arrange
        var pattern1 = new DelegationPattern("Name", "Description1");
        var pattern2 = new DelegationPattern("Name", "Description2");

        // Act & Assert
        Assert.NotEqual(pattern1, pattern2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentSuccessRate()
    {
        // Arrange
        var pattern1 = new DelegationPattern("Name", "Description", successRate: 0.7);
        var pattern2 = new DelegationPattern("Name", "Description", successRate: 0.8);

        // Act & Assert
        Assert.NotEqual(pattern1, pattern2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentUsageCount()
    {
        // Arrange
        var pattern1 = new DelegationPattern("Name", "Description", usageCount: 10);
        var pattern2 = new DelegationPattern("Name", "Description", usageCount: 20);

        // Act & Assert
        Assert.NotEqual(pattern1, pattern2);
    }

    #endregion

    #region Record Features Tests

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWith()
    {
        // Arrange
        var original = new DelegationPattern(
            "Original",
            "Original description",
            ["scenario1"],
            new Dictionary<string, object> { ["param1"] = "value1" },
            0.7,
            10);

        var newScenarios = new List<string> { "scenario2", "scenario3" };
        var newParameters = new Dictionary<string, object> { ["param2"] = "value2" };

        // Act - Since DelegationPattern is immutable, create new instance
        var modified = new DelegationPattern(
            original.Name,
            original.Description,
            applicableScenarios: newScenarios,
            parameters: newParameters,
            successRate: 0.9,
            usageCount: 50);

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal(original.Name, modified.Name);
        Assert.Equal(original.Description, modified.Description);
        Assert.NotEqual(original.SuccessRate, modified.SuccessRate);
        Assert.NotEqual(original.UsageCount, modified.UsageCount);
        Assert.NotEqual(original.ApplicableScenarios, modified.ApplicableScenarios);
        Assert.NotEqual(original.Parameters, modified.Parameters);
        Assert.Equal(0.9, modified.SuccessRate);
        Assert.Equal(50, modified.UsageCount);
        Assert.Equal(newScenarios, modified.ApplicableScenarios);
        Assert.Equal(newParameters, modified.Parameters);
    }

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var pattern = new DelegationPattern(
            "TestPattern",
            "Test description",
            usageCount: 25,
            successRate: 0.85);

        // Act
        var result = pattern.ToString();

        // Assert
        Assert.Contains("DelegationPattern", result);
        Assert.Contains("Name = TestPattern", result);
        Assert.Contains("Description = Test description", result);
    }

    [Fact]
    public void ShouldReturnSameHash_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var scenarios = new List<string> { "scenario" };
        var parameters = new Dictionary<string, object> { ["key"] = "value" };

        var pattern1 = new DelegationPattern("Name", "Description", scenarios, parameters, 0.5, 10);
        var pattern2 = new DelegationPattern("Name", "Description", scenarios, parameters, 0.5, 10);

        // Act
        var hash1 = pattern1.GetHashCode();
        var hash2 = pattern2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }


    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldPatternEvolution_WhenUsingComplexScenario()
    {
        // Arrange - Start with a new pattern
        var newPattern = new DelegationPattern(
            "AdaptiveRouting",
            "Routes tasks based on agent performance",
            ["Dynamic workload"],
            new Dictionary<string, object> { ["algorithm"] = "adaptive" },
            0.0,
            0);

        // Act - Simulate pattern evolution over time
        var afterInitialTests = new DelegationPattern(
            newPattern.Name,
            newPattern.Description,
            newPattern.ApplicableScenarios,
            parameters: new Dictionary<string, object>
            {
                ["algorithm"] = "adaptive",
                ["learningRate"] = 0.1
            },
            successRate: 0.67,
            usageCount: 3);

        var afterRefinement = new DelegationPattern(
            afterInitialTests.Name,
            afterInitialTests.Description,
            applicableScenarios:
            [
                "Dynamic workload",
                "Heterogeneous agents",
                "Variable task complexity"
            ],
            parameters: new Dictionary<string, object>
            {
                ["algorithm"] = "adaptive_v2",
                ["learningRate"] = 0.15,
                ["historyWindow"] = 10
            },
            successRate: 0.82,
            usageCount: 15);

        var mature = new DelegationPattern(
            afterRefinement.Name,
            afterRefinement.Description,
            afterRefinement.ApplicableScenarios,
            parameters: new Dictionary<string, object>
            {
                ["algorithm"] = "adaptive_v3",
                ["learningRate"] = 0.2,
                ["historyWindow"] = 20,
                ["confidenceThreshold"] = 0.85
            },
            successRate: 0.94,
            usageCount: 500);

        // Assert - Verify evolution stages
        Assert.True(newPattern.IsExperimental);
        Assert.False(newPattern.IsProven);
        Assert.False(newPattern.IsReliable);

        Assert.True(afterInitialTests.IsExperimental);
        Assert.False(afterInitialTests.IsProven);
        Assert.False(afterInitialTests.IsReliable);

        Assert.False(afterRefinement.IsExperimental);
        Assert.True(afterRefinement.IsProven);
        Assert.False(afterRefinement.IsReliable);

        Assert.False(mature.IsExperimental);
        Assert.True(mature.IsProven);
        Assert.True(mature.IsReliable);
    }

    [Fact]
    public void ShouldPatternLibrary_WhenUsingComplexScenario()
    {
        // Arrange - Create a library of delegation patterns
        var patterns = new List<DelegationPattern>
        {
            new DelegationPattern(
                "RoundRobin",
                "Distributes tasks in circular order",
                ["Equal agent capabilities", "Uniform task complexity"],
                new Dictionary<string, object> { ["fairness"] = "high" },
                0.85,
                1000),

            new DelegationPattern(
                "SkillBased",
                "Routes tasks based on agent skills",
                ["Specialized agents", "Varied task requirements"],
                new Dictionary<string, object> { ["matchingAlgorithm"] = "cosine_similarity" },
                0.92,
                750),

            new DelegationPattern(
                "LoadBalanced",
                "Distributes based on current workload",
                ["Variable agent availability", "Dynamic workload"],
                new Dictionary<string, object> { ["threshold"] = 0.8 },
                0.88,
                500),

            new DelegationPattern(
                "PriorityQueue",
                "Handles tasks by priority",
                ["Mixed priority tasks", "Critical deadlines"],
                new Dictionary<string, object> { ["queueType"] = "min_heap" },
                0.78,
                200),

            new DelegationPattern(
                "Experimental_ML",
                "Uses machine learning for routing",
                ["Complex patterns", "Historical data available"],
                new Dictionary<string, object> { ["model"] = "neural_network" },
                0.65,
                3)
        };

        // Act - Analyze the library
        var provenPatterns = patterns.Where(p => p.IsProven).ToList();
        var experimentalPatterns = patterns.Where(p => p.IsExperimental).ToList();
        var reliablePatterns = patterns.Where(p => p.IsReliable).ToList();

        var mostUsed = patterns.OrderByDescending(p => p.UsageCount).First();
        var mostSuccessful = patterns.OrderByDescending(p => p.SuccessRate).First();
        var averageSuccessRate = patterns.Average(p => p.SuccessRate);

        // Assert
        Assert.Equal(3, provenPatterns.Count);
        Assert.Single(experimentalPatterns);
        Assert.Single(reliablePatterns);

        Assert.Equal("RoundRobin", mostUsed.Name);
        Assert.Equal("SkillBased", mostSuccessful.Name);
        Assert.Equal(0.816, averageSuccessRate, 3);

        Assert.Contains("Experimental_ML", experimentalPatterns.Select(p => p.Name));
        Assert.Contains("SkillBased", reliablePatterns.Select(p => p.Name));
    }

    [Fact]
    public void ShouldPatternSelection_WhenUsingComplexScenario()
    {
        // Arrange - Patterns with different characteristics
        var patterns = new List<DelegationPattern>
        {
            new DelegationPattern("FastButUnreliable", "Quick routing", successRate: 0.6, usageCount: 100),
            new DelegationPattern("SlowButSteady", "Thorough analysis", successRate: 0.95, usageCount: 50),
            new DelegationPattern("Balanced", "Good compromise", successRate: 0.85, usageCount: 200),
            new DelegationPattern("NewApproach", "Innovative method", successRate: 0.8, usageCount: 2)
        };

        // Scenario requirements
        var scenarios = new[]
        {
            new { Name = "Critical", RequireReliable = true, RequireProven = true },
            new { Name = "Experimental", RequireReliable = false, RequireProven = false },
            new { Name = "Standard", RequireReliable = false, RequireProven = true }
        };

        // Act - Select patterns for each scenario
        var selections = scenarios.Select(scenario => new
        {
            Scenario = scenario.Name,
            SelectedPatterns = patterns
                .Where(p => !scenario.RequireReliable || p.IsReliable)
                .Where(p => !scenario.RequireProven || p.IsProven)
                .OrderByDescending(p => p.SuccessRate * (p.IsProven ? 1.2 : 1.0))
                .ToList()
        }).ToList();

        // Assert
        var criticalSelection = selections.First(s => s.Scenario == "Critical");
        Assert.Single(criticalSelection.SelectedPatterns);
        Assert.Equal("SlowButSteady", criticalSelection.SelectedPatterns[0].Name);

        var experimentalSelection = selections.First(s => s.Scenario == "Experimental");
        Assert.Equal(4, experimentalSelection.SelectedPatterns.Count);
        Assert.Equal("SlowButSteady", experimentalSelection.SelectedPatterns[0].Name);

        var standardSelection = selections.First(s => s.Scenario == "Standard");
        Assert.Equal(2, standardSelection.SelectedPatterns.Count);
        Assert.Contains("SlowButSteady", standardSelection.SelectedPatterns.Select(p => p.Name));
        Assert.Contains("Balanced", standardSelection.SelectedPatterns.Select(p => p.Name));
    }

    [Fact]
    public void ShouldPatternMetrics_WhenUsingComplexScenario()
    {
        // Arrange
        var pattern = new DelegationPattern(
            "MetricsPattern",
            "Pattern with complex metrics",
            ["Performance critical", "High volume"],
            new Dictionary<string, object>
            {
                ["metrics"] = new Dictionary<string, object>
                {
                    ["avgResponseTime"] = 125.5,
                    ["p95ResponseTime"] = 250.0,
                    ["p99ResponseTime"] = 500.0,
                    ["throughput"] = 1000,
                    ["errorRate"] = 0.02,
                    ["successfulDelegations"] = 950,
                    ["failedDelegations"] = 20,
                    ["timeouts"] = 30
                },
                ["configuration"] = new Dictionary<string, object>
                {
                    ["maxRetries"] = 3,
                    ["timeoutMs"] = 5000,
                    ["circuitBreakerThreshold"] = 0.5
                },
                ["agentMetrics"] = new List<Dictionary<string, object>>
                {
                    new() { ["agentId"] = AgentId1, ["tasksCompleted"] = 300, ["avgTime"] = 120.0 },
                    new() { ["agentId"] = AgentId2, ["tasksCompleted"] = 350, ["avgTime"] = 110.0 },
                    new() { ["agentId"] = AgentId3, ["tasksCompleted"] = 300, ["avgTime"] = 140.0 }
                }
            },
            0.95,
            1000);

        // Act
        var metrics = pattern.Parameters["metrics"] as Dictionary<string, object>;
        var configuration = pattern.Parameters["configuration"] as Dictionary<string, object>;
        var agentMetrics = pattern.Parameters["agentMetrics"] as List<Dictionary<string, object>>;

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(125.5, metrics["avgResponseTime"]);
        Assert.Equal(0.02, metrics["errorRate"]);

        Assert.NotNull(configuration);
        Assert.Equal(3, configuration["maxRetries"]);

        Assert.NotNull(agentMetrics);
        Assert.Equal(3, agentMetrics.Count);
        Assert.All(agentMetrics, am => Assert.True((int)am["tasksCompleted"] >= 300));

        Assert.True(pattern.IsProven);
        Assert.True(pattern.IsReliable);
        Assert.False(pattern.IsExperimental);
    }

    #endregion
}
