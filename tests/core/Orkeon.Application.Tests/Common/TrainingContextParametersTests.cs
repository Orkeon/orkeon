using Orkeon.Application.Training;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Common;

public class TrainingContextParametersTests
{
    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingSimulationMetricsWithEmpty()
    {
        // Act
        var metrics = SimulationMetrics.Empty;

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(0, metrics.Count);
        Assert.Empty(metrics.Keys);
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingSimulationMetricsUsingBuilder()
    {
        // Arrange & Act
        var metrics = SimulationMetrics.CreateBuilder()
            .AddExecutionTime(TimeoutLong)
            .AddMemoryUsage(1024 * 1024 * 100) // 100MB
            .AddDecisionCount(25)
            .AddToolCallCount(10)
            .Add("customMetric", 42.5)
            .Build();

        // Assert
        Assert.Equal(5, metrics.Count);
        Assert.Equal(TimeoutLong, metrics.Get<TimeSpan>("executionTime"));
        Assert.Equal(104857600L, metrics.Get<long>("memoryUsage"));
        Assert.Equal(25, metrics.Get<int>("decisionCount"));
        Assert.Equal(10, metrics.Get<int>("toolCallCount"));
        Assert.Equal(42.5, metrics.Get<double>("customMetric"));
    }

    [Fact]
    public void ShouldThrow_WhenUsingSimulationMetricsGettingRequiredWithMissingKey()
    {
        // Arrange
        var metrics = SimulationMetrics.Empty;

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => metrics.GetRequired<int>("missing"));
        Assert.Contains("Required parameter 'missing' not found", ex.Message);
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingReadinessDetailedMetricsUsingBuilder()
    {
        // Arrange & Act
        var metrics = ReadinessDetailedMetrics.CreateBuilder()
            .AddSkillReadinessScore(0.85)
            .AddExperienceLevel(3)
            .AddConsistencyScore(0.92)
            .AddReliabilityScore(0.88)
            .Add("teamworkScore", 0.9)
            .Build();

        // Assert
        Assert.Equal(5, metrics.Count);
        Assert.Equal(0.85, metrics.Get<double>("skillReadinessScore"));
        Assert.Equal(3, metrics.Get<int>("experienceLevel"));
        Assert.Equal(0.92, metrics.Get<double>("consistencyScore"));
        Assert.Equal(0.88, metrics.Get<double>("reliabilityScore"));
        Assert.Equal(0.9, metrics.Get<double>("teamworkScore"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingScenarioInitialContextUsingBuilder()
    {
        // Arrange
        var resources = new List<string> { "CPU", "Memory", "Network" };
        var constraints = new List<string> { "Time limit: 1 hour", "Memory limit: 2GB" };

        // Act
        var context = ScenarioInitialContext.CreateBuilder()
            .AddEnvironment("production")
            .AddStartingState("initialized")
            .AddAvailableResources(resources)
            .AddConstraints(constraints)
            .Add("priority", "high")
            .Build();

        // Assert
        Assert.Equal(5, context.Count);
        Assert.Equal("production", context.Get<string>("environment"));
        Assert.Equal("initialized", context.Get<string>("startingState"));

        var storedResources = context.Get<IReadOnlyList<string>>("availableResources");
        Assert.NotNull(storedResources);
        Assert.Equal(3, storedResources.Count);
        Assert.Contains("CPU", storedResources);

        var storedConstraints = context.Get<IReadOnlyList<string>>("constraints");
        Assert.NotNull(storedConstraints);
        Assert.Equal(2, storedConstraints.Count);
        Assert.Contains("Time limit: 1 hour", storedConstraints);
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingScenarioSuccessConditionsBuilder()
    {
        // Arrange
        var outcomes = new List<string> { "All tests pass", "Performance targets met" };

        // Act
        var conditions = ScenarioSuccessConditions.CreateBuilder()
            .AddGoalState("completed_successfully")
            .AddRequiredOutcomes(outcomes)
            .AddMinimumScore(0.95)
            .AddTimeLimit(TimeSpan.FromHours(2))
            .Build();

        // Assert
        Assert.Equal(4, conditions.Count);
        Assert.Equal("completed_successfully", conditions.Get<string>("goalState"));
        Assert.Equal(0.95, conditions.Get<double>("minimumScore"));
        Assert.Equal(TimeSpan.FromHours(2), conditions.Get<TimeSpan>("timeLimit"));

        var storedOutcomes = conditions.Get<IReadOnlyList<string>>("requiredOutcomes");
        Assert.NotNull(storedOutcomes);
        Assert.Equal(2, storedOutcomes.Count);
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingScenarioMetadataUsingBuilder()
    {
        // Arrange
        var createdDate = DateTime.UtcNow;
        var modifiedDate = createdDate.AddDays(1);

        // Act
        var metadata = ScenarioMetadata.CreateBuilder()
            .AddAuthor("Test Author")
            .AddVersion("1.0.0")
            .AddCreatedAt(createdDate)
            .AddLastModified(modifiedDate)
            .Add("tags", new List<string> { "test", "scenario" })
            .Build();

        // Assert
        Assert.Equal(5, metadata.Count);
        Assert.Equal("Test Author", metadata.Get<string>("author"));
        Assert.Equal("1.0.0", metadata.Get<string>("version"));
        Assert.Equal(createdDate, metadata.Get<DateTime>("createdAt"));
        Assert.Equal(modifiedDate, metadata.Get<DateTime>("lastModified"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingTaskDefinitionParametersUsingBuilder()
    {
        // Arrange
        var tools = new List<string> { "FileReader", "DataProcessor" };
        var rules = new List<string> { "Output must be JSON", "Must complete in 5 minutes" };

        // Act
        var parameters = TaskDefinitionParameters.CreateBuilder()
            .AddInput("Process customer data")
            .AddExpectedOutput("Analyzed customer segments")
            .AddToolsRequired(tools)
            .AddValidationRules(rules)
            .Build();

        // Assert
        Assert.Equal(4, parameters.Count);
        Assert.Equal("Process customer data", parameters.Get<string>("input"));
        Assert.Equal("Analyzed customer segments", parameters.Get<string>("expectedOutput"));

        var storedTools = parameters.Get<IReadOnlyList<string>>("toolsRequired");
        Assert.NotNull(storedTools);
        Assert.Equal(2, storedTools.Count);
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingAnalyticsCustomFiltersBuilder()
    {
        // Arrange & Act
        var filters = AnalyticsCustomFilters.CreateBuilder()
            .AddMinScore(75.0)
            .AddMaxDuration(TimeSpan.FromMinutes(30))
            .AddDifficultyLevel("intermediate")
            .AddTeamSize(5)
            .Add("region", "north-america")
            .Build();

        // Assert
        Assert.Equal(5, filters.Count);
        Assert.Equal(75.0, filters.Get<double>("minScore"));
        Assert.Equal(TimeSpan.FromMinutes(30), filters.Get<TimeSpan>("maxDuration"));
        Assert.Equal("intermediate", filters.Get<string>("difficultyLevel"));
        Assert.Equal(5, filters.Get<int>("teamSize"));
        Assert.Equal("north-america", filters.Get<string>("region"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingTrainingCustomMetricsUsingBuilder()
    {
        // Arrange & Act
        var metrics = TrainingCustomMetrics.CreateBuilder()
            .AddEngagementScore(0.87)
            .AddCollaborationIndex(0.92)
            .AddInnovationScore(0.78)
            .Add("adaptabilityScore", 0.84)
            .Build();

        // Assert
        Assert.Equal(4, metrics.Count);
        Assert.Equal(0.87, metrics.Get<double>("engagementScore"));
        Assert.Equal(0.92, metrics.Get<double>("collaborationIndex"));
        Assert.Equal(0.78, metrics.Get<double>("innovationScore"));
        Assert.Equal(0.84, metrics.Get<double>("adaptabilityScore"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingInsightSupportingDataUsingBuilder()
    {
        // Arrange
        var dataPoints = new List<double> { 1.2, 2.4, 3.6, 4.8, 6.0 };

        // Act
        var data = InsightSupportingData.CreateBuilder()
            .AddDataPoints(dataPoints)
            .AddTrendAnalysis("Upward trend with 20% growth")
            .AddCorrelationFactor(0.85)
            .Add("confidence", 0.95)
            .Build();

        // Assert
        Assert.Equal(4, data.Count);
        var storedPoints = data.Get<IReadOnlyList<double>>("dataPoints");
        Assert.NotNull(storedPoints);
        Assert.Equal(5, storedPoints.Count);
        Assert.Equal(1.2, storedPoints[0]);
        Assert.Equal("Upward trend with 20% growth", data.Get<string>("trendAnalysis"));
        Assert.Equal(0.85, data.Get<double>("correlationFactor"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingTeamObjectiveSuccessCriteriaBuilder()
    {
        // Arrange & Act
        var criteria = TeamObjectiveSuccessCriteria.CreateBuilder()
            .AddCompletionRate(0.95)
            .AddQualityScore(0.88)
            .AddTimeEfficiency(0.92)
            .AddCollaborationScore(0.90)
            .Build();

        // Assert
        Assert.Equal(4, criteria.Count);
        Assert.Equal(0.95, criteria.Get<double>("completionRate"));
        Assert.Equal(0.88, criteria.Get<double>("qualityScore"));
        Assert.Equal(0.92, criteria.Get<double>("timeEfficiency"));
        Assert.Equal(0.90, criteria.Get<double>("collaborationScore"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingSimulationEnvironmentVariablesBuilder()
    {
        // Arrange & Act
        var vars = SimulationEnvironmentVariables.CreateBuilder()
            .AddApiEndpoint("https://api.example.com/v1")
            .AddDataSource("production-db")
            .AddSimulationSeed(12345)
            .AddDebugMode(true)
            .Add("logLevel", "verbose")
            .Build();

        // Assert
        Assert.Equal(5, vars.Count);
        Assert.Equal("https://api.example.com/v1", vars.Get<string>("apiEndpoint"));
        Assert.Equal("production-db", vars.Get<string>("dataSource"));
        Assert.Equal(12345, vars.Get<int>("simulationSeed"));
        Assert.True(vars.Get<bool>("debugMode"));
        Assert.Equal("verbose", vars.Get<string>("logLevel"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingSimulationEventDataUsingBuilder()
    {
        // Arrange & Act
        var eventData = SimulationEventData.CreateBuilder()
            .AddEventId("evt_12345")
            .AddSourceAgent("agent_001")
            .AddPayload("{\"action\":\"task_completed\"}")
            .AddImpact("high")
            .Add("timestamp", DateTime.UtcNow)
            .Build();

        // Assert
        Assert.Equal(5, eventData.Count);
        Assert.Equal("evt_12345", eventData.Get<string>("eventId"));
        Assert.Equal("agent_001", eventData.Get<string>("sourceAgent"));
        Assert.Equal("{\"action\":\"task_completed\"}", eventData.Get<string>("payload"));
        Assert.Equal("high", eventData.Get<string>("impact"));
        Assert.True(eventData.Contains("timestamp"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingDataPointMetadataUsingBuilder()
    {
        // Arrange & Act
        var metadata = DataPointMetadata.CreateBuilder()
            .AddSource("sensor_001")
            .AddConfidence(0.98)
            .AddContext("temperature_reading")
            .Add("unit", "celsius")
            .Build();

        // Assert
        Assert.Equal(4, metadata.Count);
        Assert.Equal("sensor_001", metadata.Get<string>("source"));
        Assert.Equal(0.98, metadata.Get<double>("confidence"));
        Assert.Equal("temperature_reading", metadata.Get<string>("context"));
        Assert.Equal("celsius", metadata.Get<string>("unit"));
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingAllClassesContains()
    {
        // Arrange
        var metrics = SimulationMetrics.CreateBuilder()
            .AddExecutionTime(TimeoutExtended)
            .Build();

        // Act & Assert
        Assert.True(metrics.Contains("executionTime"));
        Assert.False(metrics.Contains("nonexistent"));
    }

    [Fact]
    public void ShouldConvertCorrectly_WhenUsingAllClassesToDictionary()
    {
        // Arrange
        var metrics = ReadinessDetailedMetrics.CreateBuilder()
            .AddSkillReadinessScore(0.75)
            .AddExperienceLevel(2)
            .Build();

        // Act
        var dict = metrics.ToDictionary();

        // Assert
        Assert.Equal(2, dict.Count);
        Assert.Equal(0.75, dict["skillReadinessScore"]);
        Assert.Equal(2, dict["experienceLevel"]);
    }

    [Fact]
    public void ShouldFullSimulationSetup_WhenUsingComplexScenario()
    {
        // Arrange - Create a complete simulation setup
        var context = ScenarioInitialContext.CreateBuilder()
            .AddEnvironment("staging")
            .AddStartingState("ready")
            .AddAvailableResources(["GPU", "Storage", "API"])
            .AddConstraints(["Max runtime: 2 hours"])
            .Build();

        var conditions = ScenarioSuccessConditions.CreateBuilder()
            .AddGoalState("all_tasks_completed")
            .AddRequiredOutcomes(["Data processed", "Report generated"])
            .AddMinimumScore(0.8)
            .AddTimeLimit(TimeSpan.FromHours(1.5))
            .Build();

        var metadata = ScenarioMetadata.CreateBuilder()
            .AddAuthor("Test Team")
            .AddVersion("2.0.0")
            .AddCreatedAt(DateTime.UtcNow)
            .Build();

        var envVars = SimulationEnvironmentVariables.CreateBuilder()
            .AddApiEndpoint("https://staging.api.com")
            .AddDataSource("test-data-set")
            .AddSimulationSeed(42)
            .AddDebugMode(false)
            .Build();

        // Act - Simulate accessing various properties
        var hasResources = context.Contains("availableResources");
        var goalState = conditions.Get<string>("goalState");
        var author = metadata.Get<string>("author");
        var debugMode = envVars.Get<bool>("debugMode");

        // Assert
        Assert.True(hasResources);
        Assert.Equal("all_tasks_completed", goalState);
        Assert.Equal("Test Team", author);
        Assert.False(debugMode);

        // Verify all can be converted to dictionary
        Assert.NotEmpty(context.ToDictionary());
        Assert.NotEmpty(conditions.ToDictionary());
        Assert.NotEmpty(metadata.ToDictionary());
        Assert.NotEmpty(envVars.ToDictionary());
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenUsingEdgeCasesWithEmptyLists()
    {
        // Arrange & Act
        var context = ScenarioInitialContext.CreateBuilder()
            .AddAvailableResources([])
            .AddConstraints([])
            .Build();

        // Assert
        var resources = context.Get<IReadOnlyList<string>>("availableResources");
        var constraints = context.Get<IReadOnlyList<string>>("constraints");

        Assert.NotNull(resources);
        Assert.Empty(resources);
        Assert.NotNull(constraints);
        Assert.Empty(constraints);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingEdgeCasesWithLargeNumbers()
    {
        // Arrange & Act
        var metrics = SimulationMetrics.CreateBuilder()
            .AddMemoryUsage(long.MaxValue)
            .AddDecisionCount(int.MaxValue)
            .Build();

        // Assert
        Assert.Equal(long.MaxValue, metrics.Get<long>("memoryUsage"));
        Assert.Equal(int.MaxValue, metrics.Get<int>("decisionCount"));
    }

    [Fact]
    public void ShouldWorkWithCompatibleTypes_WhenUsingTypeConversion()
    {
        // Arrange
        var filters = AnalyticsCustomFilters.CreateBuilder()
            .AddMinScore(75) // int instead of double
            .Build();

        // Act - Should convert int to double
        var score = filters.Get<double>("minScore");

        // Assert
        Assert.Equal(75.0, score);
    }

    [Fact]
    public void ShouldLargeParameterSet_WhenUsingPerformanceTest()
    {
        // Arrange
        var builder = TrainingCustomMetrics.CreateBuilder();

        // Add 100 metrics
        for (int i = 0; i < 100; i++)
        {
            builder.Add($"metric_{i}", i * 0.01);
        }

        // Act
        var metrics = builder.Build();

        // Assert
        Assert.Equal(100, metrics.Count);
        Assert.Equal(0.5, metrics.Get<double>("metric_50"));

        // Verify ToDictionary works with large sets
        var dict = metrics.ToDictionary();
        Assert.Equal(100, dict.Count);
    }
}
