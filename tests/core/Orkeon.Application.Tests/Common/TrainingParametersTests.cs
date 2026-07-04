using Orkeon.Application.Training;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

namespace Orkeon.Application.Tests.Common;

public class TrainingParametersTests
{
    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingTrainingPlanParametersWithEmpty()
    {
        // Act
        var parameters = TrainingPlanParameters.Empty;

        // Assert
        Assert.NotNull(parameters);
        Assert.Equal(0, parameters.Count);
        Assert.Empty(parameters.Keys);
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingTrainingPlanParametersUsingBuilder()
    {
        // Arrange & Act
        var parameters = TrainingPlanParameters.CreateBuilder()
            .AddMaxRetries(3)
            .AddAdaptiveDifficulty(true)
            .AddFeedbackMode("detailed")
            .Add("customParam", "value")
            .Build();

        // Assert
        Assert.Equal(4, parameters.Count);
        Assert.Equal(3, parameters.GetRequired<int>("maxRetries"));
        Assert.True(parameters.GetRequired<bool>("adaptiveDifficulty"));
        Assert.Equal("detailed", parameters.GetRequired<string>("feedbackMode"));
        Assert.Equal("value", parameters.GetRequired<string>("customParam"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenUsingTrainingPlanParametersGettingWithMissingKey()
    {
        // Arrange
        var parameters = TrainingPlanParameters.Empty;

        // Act
        var result = parameters.Get<string>("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldThrow_WhenUsingTrainingPlanParametersGettingRequiredWithMissingKey()
    {
        // Arrange
        var parameters = TrainingPlanParameters.Empty;

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => parameters.GetRequired<string>("missing"));
        Assert.Contains("Required parameter 'missing' not found", ex.Message);
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingTrainingResourcesBuilder()
    {
        // Arrange
        var videoUrls = new List<string> { "https://video1.com", "https://video2.com" };
        var exampleCode = "public class Example { }";

        // Act
        var resources = TrainingResources.CreateBuilder()
            .AddDocumentationUrl(new Uri("https://docs.example.com"))
            .AddVideoUrls(videoUrls)
            .AddExampleCode(exampleCode)
            .AddDatasetPath("/data/training.csv")
            .Build();

        // Assert
        Assert.Equal(4, resources.Count);
        Assert.Equal(new Uri("https://docs.example.com"), resources.Get<Uri>("documentationUrl"));

        var storedVideos = resources.Get<IReadOnlyList<string>>("videoUrls");
        Assert.NotNull(storedVideos);
        Assert.Equal(2, storedVideos.Count);
        Assert.Contains("https://video1.com", storedVideos);

        Assert.Equal(exampleCode, resources.Get<string>("exampleCode"));
        Assert.Equal("/data/training.csv", resources.Get<string>("datasetPath"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingTrainingSuccessMetricsUsingBuilder()
    {
        // Arrange
        var requiredTools = new List<string> { "Calculator", "DataAnalyzer" };

        // Act
        var metrics = TrainingSuccessMetrics.CreateBuilder()
            .AddAccuracyThreshold(0.95)
            .AddCompletionTimeLimit(TimeSpan.FromHours(2))
            .AddMinimumScore(80.0)
            .AddRequiredToolUsage(requiredTools)
            .Build();

        // Assert
        Assert.Equal(4, metrics.Count);
        Assert.Equal(0.95, metrics.Get<double>("accuracyThreshold"));
        Assert.Equal(TimeSpan.FromHours(2), metrics.Get<TimeSpan>("completionTimeLimit"));
        Assert.Equal(80.0, metrics.Get<double>("minimumScore"));

        var storedTools = metrics.Get<IReadOnlyList<string>>("requiredToolUsage");
        Assert.NotNull(storedTools);
        Assert.Equal(2, storedTools.Count);
        Assert.Contains("Calculator", storedTools);
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingTrainingRecommendationContextUsingBuilder()
    {
        // Arrange & Act
        var context = TrainingRecommendationContext.CreateBuilder()
            .AddPreviousAttempts(2)
            .AddTimeConstraint(TimeSpan.FromHours(1))
            .AddPreferredDifficulty("intermediate")
            .Build();

        // Assert
        Assert.Equal(3, context.Count);
        Assert.Equal(2, context.Get<int>("previousAttempts"));
        Assert.Equal(TimeSpan.FromHours(1), context.Get<TimeSpan>("timeConstraint"));
        Assert.Equal("intermediate", context.Get<string>("preferredDifficulty"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingTrainingStatisticsBuilder()
    {
        // Arrange & Act
        var stats = TrainingStatistics.CreateBuilder()
            .AddTotalAttempts(15)
            .AddSuccessRate(0.87)
            .AddAverageScore(85.5)
            .AddImprovementRate(0.12)
            .Build();

        // Assert
        Assert.Equal(4, stats.Count);
        Assert.Equal(15, stats.Get<int>("totalAttempts"));
        Assert.Equal(0.87, stats.Get<double>("successRate"));
        Assert.Equal(85.5, stats.Get<double>("averageScore"));
        Assert.Equal(0.12, stats.Get<double>("improvementRate"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingScenarioPerformanceDataUsingBuilder()
    {
        // Arrange & Act
        var performanceData = ScenarioPerformanceData.CreateBuilder()
            .AddTasksCompleted(25)
            .AddErrorCount(3)
            .AddToolEfficiency(0.92)
            .Add("customMetric", 42)
            .Build();

        // Assert
        Assert.Equal(4, performanceData.Count);
        Assert.Equal(25, performanceData.Get<int>("tasksCompleted"));
        Assert.Equal(3, performanceData.Get<int>("errorCount"));
        Assert.Equal(0.92, performanceData.Get<double>("toolEfficiency"));
        Assert.Equal(42, performanceData.Get<int>("customMetric"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingAchievementCriteriaDataUsingBuilder()
    {
        // Arrange & Act
        var criteria = AchievementCriteriaData.CreateBuilder()
            .AddRequiredScore(90.0)
            .AddCompletionCount(5)
            .AddTimeLimit(TimeSpan.FromMinutes(30))
            .Build();

        // Assert
        Assert.Equal(3, criteria.Count);
        Assert.Equal(90.0, criteria.Get<double>("requiredScore"));
        Assert.Equal(5, criteria.Get<int>("completionCount"));
        Assert.Equal(TimeSpan.FromMinutes(30), criteria.Get<TimeSpan>("timeLimit"));
    }

    [Fact]
    public void ShouldCreateCorrectly_WhenUsingTrainingParameterValueFrom()
    {
        // Arrange
        var stringValue = "test";
        var intValue = 42;
        var doubleValue = 3.14;
        var listValue = new List<string> { "a", "b", "c" };

        // Act
        var stringParam = TrainingParameterValue.From(stringValue);
        var intParam = TrainingParameterValue.From(intValue);
        var doubleParam = TrainingParameterValue.From(doubleValue);
        var listParam = TrainingParameterValue.From(listValue);

        // Assert
        Assert.Equal(stringValue, stringParam.GetValue<string>());
        Assert.Equal(intValue, intParam.GetValue<int>());
        Assert.Equal(doubleValue, doubleParam.GetValue<double>());
        Assert.Equal(listValue, listParam.GetValue<List<string>>());
    }

    [Fact]
    public void ShouldThrow_WhenUsingTrainingParameterValueFromWithNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => TrainingParameterValue.From(null!));
    }

    [Fact]
    public void ShouldWork_WhenUsingTrainingParameterValueGettingValueWithTypeConversion()
    {
        // Arrange
        var param = TrainingParameterValue.From(42);

        // Act
        var asDouble = param.GetValue<double>();
        var asLong = param.GetValue<long>();
        var asString = param.GetValue<string>();

        // Assert
        Assert.Equal(42.0, asDouble);
        Assert.Equal(42L, asLong);
        Assert.Equal("42", asString);
    }

    [Fact]
    public void ShouldThrow_WhenUsingTrainingParameterValueGettingValueWithInvalidConversion()
    {
        // Arrange
        var param = TrainingParameterValue.From("not a number");

        // Act & Assert
        var ex = Assert.Throws<InvalidCastException>(() => param.GetValue<int>());
        Assert.Contains("Cannot convert training parameter value", ex.Message);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingAllClassesContains()
    {
        // Arrange
        var parameters = TrainingPlanParameters.CreateBuilder()
            .AddMaxRetries(3)
            .Build();

        // Act & Assert
        Assert.True(parameters.Contains("maxRetries"));
        Assert.False(parameters.Contains("nonexistent"));
    }

    [Fact]
    public void ShouldConvertCorrectly_WhenUsingAllClassesToDictionary()
    {
        // Arrange
        var resources = TrainingResources.CreateBuilder()
            .AddDocumentationUrl(new Uri(TestBaseUrl))
            .AddDatasetPath("/data/train.csv")
            .Build();

        // Act
        var dict = resources.ToDictionary();

        // Assert
        Assert.Equal(2, dict.Count);
        Assert.Equal(new Uri(TestBaseUrl), dict["documentationUrl"]);
        Assert.Equal("/data/train.csv", dict["datasetPath"]);
    }

    [Fact]
    public void ShouldFullTrainingSetup_WhenUsingComplexScenario()
    {
        // Arrange - Create a complete training setup
        var planParams = TrainingPlanParameters.CreateBuilder()
            .AddMaxRetries(5)
            .AddAdaptiveDifficulty(true)
            .AddFeedbackMode("realtime")
            .Build();

        var resources = TrainingResources.CreateBuilder()
            .AddDocumentationUrl(new Uri("https://docs.training.com"))
            .AddVideoUrls(["https://video1.com", "https://video2.com"])
            .AddExampleCode("// Example code here")
            .AddDatasetPath("/training/dataset.json")
            .Build();

        var successMetrics = TrainingSuccessMetrics.CreateBuilder()
            .AddAccuracyThreshold(0.9)
            .AddCompletionTimeLimit(TimeSpan.FromHours(3))
            .AddMinimumScore(85.0)
            .AddRequiredToolUsage(["Analyzer", "Validator"])
            .Build();

        var context = TrainingRecommendationContext.CreateBuilder()
            .AddPreviousAttempts(3)
            .AddTimeConstraint(TimeSpan.FromHours(2))
            .AddPreferredDifficulty("advanced")
            .Build();

        var stats = TrainingStatistics.CreateBuilder()
            .AddTotalAttempts(20)
            .AddSuccessRate(0.75)
            .AddAverageScore(82.5)
            .AddImprovementRate(0.15)
            .Build();

        // Act - Simulate accessing various properties
        var maxRetries = planParams.Get<int>("maxRetries");
        var docUrl = resources.Get<Uri>("documentationUrl");
        var accuracy = successMetrics.Get<double>("accuracyThreshold");
        var difficulty = context.Get<string>("preferredDifficulty");
        var successRate = stats.Get<double>("successRate");

        // Assert
        Assert.Equal(5, maxRetries);
        Assert.Equal(new Uri("https://docs.training.com"), docUrl);
        Assert.Equal(0.9, accuracy);
        Assert.Equal("advanced", difficulty);
        Assert.Equal(0.75, successRate);

        // Verify all can be converted to dictionary
        Assert.NotEmpty(planParams.ToDictionary());
        Assert.NotEmpty(resources.ToDictionary());
        Assert.NotEmpty(successMetrics.ToDictionary());
        Assert.NotEmpty(context.ToDictionary());
        Assert.NotEmpty(stats.ToDictionary());
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenUsingEdgeCasesWithEmptyLists()
    {
        // Arrange & Act
        var resources = TrainingResources.CreateBuilder()
            .AddVideoUrls([])
            .Build();

        var metrics = TrainingSuccessMetrics.CreateBuilder()
            .AddRequiredToolUsage([])
            .Build();

        // Assert
        var videos = resources.Get<IReadOnlyList<string>>("videoUrls");
        var tools = metrics.Get<IReadOnlyList<string>>("requiredToolUsage");

        Assert.NotNull(videos);
        Assert.Empty(videos);
        Assert.NotNull(tools);
        Assert.Empty(tools);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingEdgeCasesExtremeDurations()
    {
        // Arrange & Act
        var metrics = TrainingSuccessMetrics.CreateBuilder()
            .AddCompletionTimeLimit(TimeSpan.MaxValue)
            .Build();

        var context = TrainingRecommendationContext.CreateBuilder()
            .AddTimeConstraint(TimeSpan.Zero)
            .Build();

        // Assert
        Assert.Equal(TimeSpan.MaxValue, metrics.Get<TimeSpan>("completionTimeLimit"));
        Assert.Equal(TimeSpan.Zero, context.Get<TimeSpan>("timeConstraint"));
    }

    [Fact]
    public void ShouldLargeParameterSet_WhenUsingPerformanceTest()
    {
        // Arrange
        var builder = TrainingStatistics.CreateBuilder();

        // Add 100 custom statistics
        for (int i = 0; i < 100; i++)
        {
            builder.Add($"stat_{i}", i * 1.5);
        }

        // Act
        var stats = builder.Build();

        // Assert
        Assert.Equal(100, stats.Count);
        Assert.Equal(75.0, stats.Get<double>("stat_50"));

        // Verify ToDictionary works with large sets
        var dict = stats.ToDictionary();
        Assert.Equal(100, dict.Count);
    }

    [Fact]
    public void ShouldMaintainFluency_WhenUsingBuilderPatternUsingChainedCalls()
    {
        // Act
        var result = ScenarioPerformanceData.CreateBuilder()
            .AddTasksCompleted(10)
            .AddErrorCount(1)
            .AddToolEfficiency(0.95)
            .Add("bonus", 100)
            .Add("penalty", -10)
            .Build();

        // Assert
        Assert.Equal(5, result.Count);
        Assert.Equal(10, result.Get<int>("tasksCompleted"));
        Assert.Equal(1, result.Get<int>("errorCount"));
        Assert.Equal(0.95, result.Get<double>("toolEfficiency"));
        Assert.Equal(100, result.Get<int>("bonus"));
        Assert.Equal(-10, result.Get<int>("penalty"));
    }

    [Fact]
    public void ShouldWork_WhenUsingNullHandlingInListParameters()
    {
        // Arrange
        var listWithNulls = new List<string> { "item1", null!, "item3" };

        // Act
        var resources = TrainingResources.CreateBuilder()
            .AddVideoUrls(listWithNulls)
            .Build();

        // Assert
        var videos = resources.Get<IReadOnlyList<string>>("videoUrls");
        Assert.NotNull(videos);
        Assert.Equal(3, videos.Count);
        Assert.Null(videos[1]);
    }
}
