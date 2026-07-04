using Orkeon.Domain.Task.Contexts;

namespace Orkeon.Domain.Tests.Contexts;

/// <summary>
/// Tests for AnalysisTaskContext following Clean Architecture principles.
/// Tests the analysis task context classes and their behavior.
/// </summary>
public class AnalysisTaskContextTests
{
    #region AnalysisTaskContext Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingAnalysisTaskContextWithDefaultConstructor()
    {
        // Act
        var context = new AnalysisTaskContext();

        // Assert
        Assert.Equal(string.Empty, context.Subject);
        Assert.NotNull(context.Metrics);
        Assert.Empty(context.Metrics);
        Assert.NotNull(context.Insights);
        Assert.Empty(context.Insights);
        Assert.Equal(0.0f, context.ConfidenceScore);
        Assert.Equal(string.Empty, context.Methodology);
        Assert.NotNull(context.DataSources);
        Assert.Empty(context.DataSources);
        Assert.NotNull(context.Stages);
        Assert.Empty(context.Stages);
        Assert.NotNull(context.Recommendations);
        Assert.Empty(context.Recommendations);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingAnalysisTaskContextUsingProperties()
    {
        // Arrange
        var metrics = new Dictionary<string, object> { { "accuracy", 0.95 } };
        var insights = new List<string> { "Insight 1", "Insight 2" };
        var dataSources = new List<DataSource> { new DataSource { Name = "Source1" } };
        var stages = new List<AnalysisStage> { new AnalysisStage("Stage1") };
        var recommendations = new List<Recommendation> { new Recommendation { Title = "Rec1" } };

        // Act
        var context = new AnalysisTaskContext
        {
            Subject = "Customer Behavior Analysis",
            Metrics = metrics,
            Insights = insights,
            ConfidenceScore = 0.85f,
            Methodology = "Statistical Analysis",
            DataSources = dataSources,
            Stages = stages,
            Recommendations = recommendations
        };

        // Assert
        Assert.Equal("Customer Behavior Analysis", context.Subject);
        Assert.Equal(metrics, context.Metrics);
        Assert.Equal(insights, context.Insights);
        Assert.Equal(0.85f, context.ConfidenceScore);
        Assert.Equal("Statistical Analysis", context.Methodology);
        Assert.Equal(dataSources, context.DataSources);
        Assert.Equal(stages, context.Stages);
        Assert.Equal(recommendations, context.Recommendations);
    }

    #endregion

    #region AddMetric Tests

    [Fact]
    public void ShouldAddToMetrics_WhenAddingMetricWithValidNameAndValue()
    {
        // Arrange
        var context = new AnalysisTaskContext();

        // Act
        context.AddMetric("accuracy", 0.95);
        context.AddMetric("precision", 0.92);
        context.AddMetric("recall", 0.88);

        // Assert
        Assert.Equal(3, context.Metrics.Count);
        Assert.Equal(0.95, context.Metrics["accuracy"]);
        Assert.Equal(0.92, context.Metrics["precision"]);
        Assert.Equal(0.88, context.Metrics["recall"]);
    }

    [Fact]
    public void ShouldStoreAsString_WhenAddingMetricWithNullValue()
    {
        // Arrange
        var context = new AnalysisTaskContext();

        // Act
        context.AddMetric("nullMetric", null!);

        // Assert
        Assert.Single(context.Metrics);
        Assert.Equal("null", context.Metrics["nullMetric"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ShouldNotAdd_WhenAddingMetricWithInvalidName(string invalidName)
    {
        // Arrange
        var context = new AnalysisTaskContext();

        // Act
        context.AddMetric(invalidName, "value");

        // Assert
        Assert.Empty(context.Metrics);
    }

    [Fact]
    public void ShouldOverwrite_WhenAddingMetricWithExistingKey()
    {
        // Arrange
        var context = new AnalysisTaskContext();
        context.AddMetric("score", 0.5);

        // Act
        context.AddMetric("score", 0.8);

        // Assert
        Assert.Single(context.Metrics);
        Assert.Equal(0.8, context.Metrics["score"]);
    }

    [Fact]
    public void ShouldStore_WhenAddingMetricWithComplexObject()
    {
        // Arrange
        var context = new AnalysisTaskContext();
        var complexMetric = new { Min = 10, Max = 100, Average = 55.5 };

        // Act
        context.AddMetric("statistics", complexMetric);

        // Assert
        Assert.Single(context.Metrics);
        Assert.Equal(complexMetric, context.Metrics["statistics"]);
    }

    #endregion

    #region AddInsight Tests

    [Fact]
    public void ShouldAddToInsights_WhenAddingInsightWithValidInsight()
    {
        // Arrange
        var context = new AnalysisTaskContext();

        // Act
        context.AddInsight("Users prefer mobile app over web");
        context.AddInsight("Peak usage occurs between 6-8 PM");

        // Assert
        Assert.Equal(2, context.Insights.Count);
        Assert.Contains("Users prefer mobile app over web", context.Insights);
        Assert.Contains("Peak usage occurs between 6-8 PM", context.Insights);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ShouldNotAdd_WhenAddingInsightWithInvalidInsight(string invalidInsight)
    {
        // Arrange
        var context = new AnalysisTaskContext();

        // Act
        context.AddInsight(invalidInsight);

        // Assert
        Assert.Empty(context.Insights);
        Assert.Equal(0.0f, context.ConfidenceScore);
    }

    [Fact]
    public void ShouldUpdateConfidenceScore_WhenAddingInsightWithDefaultConfidence()
    {
        // Arrange
        var context = new AnalysisTaskContext();

        // Act
        context.AddInsight("First insight"); // Default confidence = 1.0

        // Assert
        Assert.Equal(1.0f, context.ConfidenceScore);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    [InlineData(0.9f)]
    [InlineData(1.0f)]
    public void ShouldUpdateConfidenceScore_WhenAddingInsightWithSpecificConfidence(float confidence)
    {
        // Arrange
        var context = new AnalysisTaskContext();

        // Act
        context.AddInsight("Insight with confidence", confidence);

        // Assert
        Assert.Equal(confidence, context.ConfidenceScore);
    }

    [Fact]
    public void ShouldCalculateWeightedAverage_WhenAddingInsightWithMultipleInsights()
    {
        // Arrange
        var context = new AnalysisTaskContext();

        // Act
        context.AddInsight("First insight", 0.8f);
        context.AddInsight("Second insight", 0.6f);
        context.AddInsight("Third insight", 0.9f);

        // Assert
        // Expected: (0.8 + 0.6 + 0.9) / 3 = 0.7666...
        Assert.Equal(0.7666667f, context.ConfidenceScore, 6);
    }

    [Theory]
    [InlineData(-0.5f, 0.0f)]
    [InlineData(1.5f, 1.0f)]
    [InlineData(2.0f, 1.0f)]
    public void ShouldClamp_WhenAddingInsightWithOutOfRangeConfidence(float inputConfidence, float expectedConfidence)
    {
        // Arrange
        var context = new AnalysisTaskContext();

        // Act
        context.AddInsight("Insight", inputConfidence);

        // Assert
        Assert.Equal(expectedConfidence, context.ConfidenceScore);
    }

    #endregion

    #region GetHighConfidenceInsights Tests

    [Fact]
    public void ShouldReturnMatchingInsights_WhenGettingHighConfidenceInsightsWithDefaultThreshold()
    {
        // Arrange
        var context = new AnalysisTaskContext();
        context.AddInsight("High confidence insight 1", 0.9f);
        context.AddInsight("High confidence insight 2", 0.85f);

        // Act
        var highConfidenceInsights = context.GetHighConfidenceInsights().ToList();

        // Assert
        Assert.Equal(2, highConfidenceInsights.Count);
        // Average confidence = (0.9 + 0.85) / 2 = 0.875, which is >= 0.8
    }

    [Fact]
    public void ShouldReturnEmpty_WhenGettingHighConfidenceInsightsBelowThreshold()
    {
        // Arrange
        var context = new AnalysisTaskContext();
        context.AddInsight("Low confidence insight 1", 0.5f);
        context.AddInsight("Low confidence insight 2", 0.6f);

        // Act
        var highConfidenceInsights = context.GetHighConfidenceInsights(0.8f).ToList();

        // Assert
        Assert.Empty(highConfidenceInsights);
        // Average confidence = (0.5 + 0.6) / 2 = 0.55, which is < 0.8
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.7f)]
    [InlineData(0.9f)]
    public void ShouldFilterCorrectly_WhenGettingHighConfidenceInsightsWithCustomThreshold(float threshold)
    {
        // Arrange
        var context = new AnalysisTaskContext();
        context.AddInsight("Insight 1", 0.8f);
        context.AddInsight("Insight 2", 0.7f);
        // Average confidence = 0.75

        // Act
        var insights = context.GetHighConfidenceInsights(threshold).ToList();

        // Assert
        if (threshold <= 0.75f)
        {
            Assert.Equal(2, insights.Count);
        }
        else
        {
            Assert.Empty(insights);
        }
    }

    #endregion

    #region CompleteStage Tests

    [Fact]
    public void ShouldCompleteAndAddNew_WhenUsingCompleteStageWithInProgressStage()
    {
        // Arrange
        var context = new AnalysisTaskContext();
        var stage1 = new AnalysisStage("Data Collection");
        context.AddStage(stage1);

        // Act
        context.CompleteStage("Data Processing", "Collected 1000 records");

        // Assert
        Assert.Equal(2, context.Stages.Count);
        // AnalysisStage is immutable — the list entry was replaced, so check via the list
        Assert.Equal(StageStatus.Completed, context.Stages[0].Status);
        Assert.Equal("Collected 1000 records", context.Stages[0].Result);
        Assert.NotNull(context.Stages[0].CompletedAt);
        Assert.Equal("Data Processing", context.Stages[1].Name);
        Assert.Equal(StageStatus.InProgress, context.Stages[1].Status);
    }

    [Fact]
    public void ShouldJustAddNew_WhenUsingCompleteStageWithNoInProgressStage()
    {
        // Arrange
        var context = new AnalysisTaskContext();

        // Act
        context.CompleteStage("Initial Stage", null!);

        // Assert
        Assert.Single(context.Stages);
        Assert.Equal("Initial Stage", context.Stages[0].Name);
        Assert.Equal(StageStatus.InProgress, context.Stages[0].Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ShouldNotAddNewStage_WhenUsingCompleteStageWithInvalidStageName(string invalidName)
    {
        // Arrange
        var context = new AnalysisTaskContext();
        var existingStage = new AnalysisStage("Existing");
        context.AddStage(existingStage);

        // Act
        context.CompleteStage(invalidName, "Result");

        // Assert
        Assert.Single(context.Stages);
        // AnalysisStage is immutable — check the list entry, not the old reference
        Assert.Equal(StageStatus.Completed, context.Stages[0].Status);
    }

    #endregion

    #region DataSource Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingDataSourceWithDefaultConstructor()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var dataSource = new DataSource();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.Equal(string.Empty, dataSource.Name);
        Assert.Equal(string.Empty, dataSource.Type);
        Assert.Equal(string.Empty, dataSource.Location);
        Assert.True(dataSource.AccessedAt >= beforeCreation);
        Assert.True(dataSource.AccessedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, dataSource.AccessedAt.Kind);
        Assert.Equal(0, dataSource.RecordCount);
        Assert.NotNull(dataSource.Metadata);
        Assert.Empty(dataSource.Metadata);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingDataSourceProperties()
    {
        // Arrange
        var accessedAt = DateTime.UtcNow.AddHours(-1);
        var metadata = new Dictionary<string, string>
        {
            { "format", "CSV" },
            { "encoding", "UTF-8" }
        };

        // Act
        var dataSource = new DataSource
        {
            Name = "Customer Database",
            Type = "SQL",
            Location = "server.company.com/customers",
            AccessedAt = accessedAt,
            RecordCount = 50000,
            Metadata = metadata
        };

        // Assert
        Assert.Equal("Customer Database", dataSource.Name);
        Assert.Equal("SQL", dataSource.Type);
        Assert.Equal("server.company.com/customers", dataSource.Location);
        Assert.Equal(accessedAt, dataSource.AccessedAt);
        Assert.Equal(50000, dataSource.RecordCount);
        Assert.Equal(metadata, dataSource.Metadata);
    }

    #endregion

    #region AnalysisStage Tests

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingAnalysisStageUsingConstructor()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;
        var stageName = "Data Validation";

        // Act
        var stage = new AnalysisStage(stageName);

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.Equal(stageName, stage.Name);
        Assert.True(stage.StartedAt >= beforeCreation);
        Assert.True(stage.StartedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, stage.StartedAt.Kind);
        Assert.Null(stage.CompletedAt);
        Assert.Equal(StageStatus.InProgress, stage.Status);
        Assert.Equal(string.Empty, stage.Result);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingAnalysisStageUsingConstructorWithNullName()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new AnalysisStage(null!));
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void ShouldUpdateProperties_WhenUsingAnalysisStageWithCompletion()
    {
        // Arrange
        var stage = new AnalysisStage("Processing");
        var beforeComplete = DateTime.UtcNow;

        // Act
        var completed = stage.WithCompletion("Successfully processed 1000 items");

        // Assert
        var afterComplete = DateTime.UtcNow;
        Assert.NotNull(completed.CompletedAt);
        Assert.True(completed.CompletedAt >= beforeComplete);
        Assert.True(completed.CompletedAt <= afterComplete);
        Assert.Equal(DateTimeKind.Utc, completed.CompletedAt.Value.Kind);
        Assert.Equal(StageStatus.Completed, completed.Status);
        Assert.Equal("Successfully processed 1000 items", completed.Result);
        // Original stage is unchanged (immutable)
        Assert.Equal(StageStatus.InProgress, stage.Status);
    }

    [Fact]
    public void ShouldSetEmptyString_WhenUsingAnalysisStageWithCompletionWithNullResult()
    {
        // Arrange
        var stage = new AnalysisStage("Test Stage");

        // Act
        var completed = stage.WithCompletion(null!);

        // Assert
        Assert.Equal(string.Empty, completed.Result);
        Assert.Equal(StageStatus.Completed, completed.Status);
    }

    #endregion

    #region StageStatus Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingStageStatus()
    {
        // Act & Assert
        var expectedValues = new[]
        {
            StageStatus.NotStarted,
            StageStatus.InProgress,
            StageStatus.Completed,
            StageStatus.Failed
        };

        foreach (var expectedValue in expectedValues)
        {
            Assert.True(Enum.IsDefined<StageStatus>(expectedValue));
        }

        var allValues = Enum.GetValues<StageStatus>();
        Assert.Equal(expectedValues.Length, allValues.Length);
    }

    [Fact]
    public void ShouldBeNotStarted_WhenUsingStageStatusWithDefaultValue()
    {
        // Act
        var defaultValue = default(StageStatus);

        // Assert
        Assert.Equal(StageStatus.NotStarted, defaultValue);
    }

    #endregion

    #region Recommendation Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingRecommendationWithDefaultConstructor()
    {
        // Act
        var recommendation = new Recommendation();

        // Assert
        Assert.Equal(string.Empty, recommendation.Title);
        Assert.Equal(string.Empty, recommendation.Description);
        Assert.Equal(RecommendationPriority.Low, recommendation.Priority);
        Assert.Equal(0.0f, recommendation.ImpactScore);
        Assert.Equal(0.0f, recommendation.EffortScore);
        Assert.NotNull(recommendation.Actions);
        Assert.Empty(recommendation.Actions);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingRecommendationProperties()
    {
        // Arrange
        var actions = new List<string> { "Action 1", "Action 2", "Action 3" };

        // Act
        var recommendation = new Recommendation
        {
            Title = "Optimize Database Queries",
            Description = "Current queries are causing performance bottlenecks",
            Priority = RecommendationPriority.High,
            ImpactScore = 0.9f,
            EffortScore = 0.6f,
            Actions = actions
        };

        // Assert
        Assert.Equal("Optimize Database Queries", recommendation.Title);
        Assert.Equal("Current queries are causing performance bottlenecks", recommendation.Description);
        Assert.Equal(RecommendationPriority.High, recommendation.Priority);
        Assert.Equal(0.9f, recommendation.ImpactScore);
        Assert.Equal(0.6f, recommendation.EffortScore);
        Assert.Equal(actions, recommendation.Actions);
        Assert.Equal(3, recommendation.Actions.Count);
    }

    #endregion

    #region RecommendationPriority Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingRecommendationPriority()
    {
        // Act & Assert
        var expectedValues = new[]
        {
            RecommendationPriority.Low,
            RecommendationPriority.Medium,
            RecommendationPriority.High,
            RecommendationPriority.Critical
        };

        foreach (var expectedValue in expectedValues)
        {
            Assert.True(Enum.IsDefined<RecommendationPriority>(expectedValue));
        }

        var allValues = Enum.GetValues<RecommendationPriority>();
        Assert.Equal(expectedValues.Length, allValues.Length);
    }

    [Fact]
    public void ShouldBeLow_WhenUsingRecommendationPriorityWithDefaultValue()
    {
        // Act
        var defaultValue = default(RecommendationPriority);

        // Assert
        Assert.Equal(RecommendationPriority.Low, defaultValue);
    }

    [Fact]
    public void ShouldBeInOrder_WhenUsingRecommendationPriorityNumericValues()
    {
        // Assert - Verify numeric values are in ascending order (priority)
        Assert.True((int)RecommendationPriority.Low < (int)RecommendationPriority.Medium);
        Assert.True((int)RecommendationPriority.Medium < (int)RecommendationPriority.High);
        Assert.True((int)RecommendationPriority.High < (int)RecommendationPriority.Critical);
    }

    #endregion

    #region Integration and Scenario Tests

    [Fact]
    public void ShouldCompleteAnalysisScenario_WhenUsingAnalysisTaskContext()
    {
        // Arrange
        var context = new AnalysisTaskContext
        {
            Subject = "Customer Churn Analysis",
            Methodology = "Predictive Analytics using Machine Learning"
        };

        // Add data sources
        context.AddDataSource(new DataSource
        {
            Name = "Customer Database",
            Type = "PostgreSQL",
            Location = "analytics.db.company.com",
            RecordCount = 100000,
            Metadata = new Dictionary<string, string> { { "timeRange", "2020-2023" } }
        });

        context.AddDataSource(new DataSource
        {
            Name = "Transaction Logs",
            Type = "CSV",
            Location = "/data/transactions/",
            RecordCount = 5000000
        });

        // Stage 1: Data Collection
        context.AddStage(new AnalysisStage("Data Collection"));
        context.CompleteStage("Data Preprocessing", "Collected 5.1M records from 2 sources");

        // Add metrics
        context.AddMetric("totalRecords", 5100000);
        context.AddMetric("timeRange", "3 years");
        context.AddMetric("churnRate", 0.23);

        // Stage 2: Data Preprocessing
        context.CompleteStage("Model Training", "Cleaned and normalized data");

        // Add insights with varying confidence
        context.AddInsight("Customers with low engagement in first 30 days have 70% churn rate", 0.95f);
        context.AddInsight("Price sensitivity is highest in 18-25 age group", 0.88f);
        context.AddInsight("Support ticket resolution time correlates with retention", 0.82f);

        // Stage 3: Model Training
        context.CompleteStage("Analysis & Recommendations", "Trained model with 89% accuracy");

        // Add model metrics
        context.AddMetric("modelAccuracy", 0.89);
        context.AddMetric("precision", 0.87);
        context.AddMetric("recall", 0.91);
        context.AddMetric("f1Score", 0.89);

        // Add recommendations
        context.AddRecommendation(new Recommendation
        {
            Title = "Implement Early Engagement Program",
            Description = "Target new customers with personalized onboarding in first 30 days",
            Priority = RecommendationPriority.High,
            ImpactScore = 0.85f,
            EffortScore = 0.4f,
            Actions =
            [
                "Create automated welcome email series",
                "Assign dedicated success manager for first month",
                "Offer incentivized product tutorials"
            ]
        });

        context.AddRecommendation(new Recommendation
        {
            Title = "Optimize Pricing for Young Demographics",
            Description = "Introduce flexible pricing tiers for 18-25 age group",
            Priority = RecommendationPriority.Medium,
            ImpactScore = 0.65f,
            EffortScore = 0.6f,
            Actions =
            [
                "Research competitor pricing models",
                "A/B test student discounts",
                "Implement usage-based pricing option"
            ]
        });

        // Final stage completion - note: this will create a new stage since stageName is not null/empty
        context.CompleteStage(null!, "Generated 2 high-impact recommendations");

        // Assert - Verify complete analysis
        Assert.Equal("Customer Churn Analysis", context.Subject);
        Assert.Equal(2, context.DataSources.Count);
        Assert.Equal(4, context.Stages.Count);
        Assert.Equal(7, context.Metrics.Count);
        Assert.Equal(3, context.Insights.Count);
        Assert.Equal(2, context.Recommendations.Count);

        // Verify confidence score (average of 0.95, 0.88, 0.82)
        Assert.Equal(0.88333333f, context.ConfidenceScore, 6);

        // Verify high confidence insights
        var highConfidenceInsights = context.GetHighConfidenceInsights(0.85f).ToList();
        Assert.Equal(3, highConfidenceInsights.Count); // All insights because average > 0.85

        // Verify all stages are completed - CompleteStage(null, ...) does not create new stage, just completes current one
        Assert.All(context.Stages, stage => Assert.Equal(StageStatus.Completed, stage.Status));
    }

    [Fact]
    public void ShouldTrackProgress_WhenUsingAnalysisTaskContextUsingMultiStageAnalysis()
    {
        // Arrange
        var context = new AnalysisTaskContext();
        var stages = new[] { "Initialization", "Data Collection", "Processing", "Analysis", "Reporting" };

        // Act - Progress through stages
        foreach (var stageName in stages)
        {
            if (context.Stages.Count > 0)
            {
                var previousStage = stageName == "Initialization" ? null : stages[Array.IndexOf(stages, stageName) - 1];
                context.CompleteStage(stageName, $"Completed {previousStage}");
            }
            else
            {
                context.AddStage(new AnalysisStage(stageName));
            }
        }

        // Complete final stage
        context.CompleteStage(null!, "Analysis complete");

        // Assert
        Assert.Equal(5, context.Stages.Count);
        Assert.All(context.Stages, stage =>
        {
            Assert.Equal(StageStatus.Completed, stage.Status);
            Assert.NotNull(stage.CompletedAt);
            Assert.NotEmpty(stage.Result);
        });
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingAnalysisTaskContextWithNullCollections()
    {
        // Arrange & Act
        var context = new AnalysisTaskContext
        {
            Metrics = null!,
            Insights = null!,
            DataSources = null!,
            Stages = null!,
            Recommendations = null!
        };

        // Assert
        Assert.Null(context.Metrics);
        Assert.Null(context.Insights);
        Assert.Null(context.DataSources);
        Assert.Null(context.Stages);
        Assert.Null(context.Recommendations);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingAnalysisTaskContextWithUnicodeContent()
    {
        // Arrange
        var context = new AnalysisTaskContext
        {
            Subject = "分析主题 📊",
            Methodology = "統計分析 & ML 🤖"
        };

        // Act
        context.AddMetric("准确率", 0.95);
        context.AddInsight("用户偏好移动应用 📱", 0.9f);

        // Assert
        Assert.Contains("分析主题", context.Subject);
        Assert.Contains("📊", context.Subject);
        Assert.Contains("統計分析", context.Methodology);
        Assert.Contains("🤖", context.Methodology);
        Assert.Contains("准确率", context.Metrics.Keys);
        Assert.Contains("📱", context.Insights[0]);
    }

    [Fact]
    public void ShouldAccept_WhenUsingDataSourceWithNegativeRecordCount()
    {
        // Arrange
        var dataSource = new DataSource();

        // Act
        var ds = new DataSource { RecordCount = -100 };

        // Assert
        Assert.Equal(-100, ds.RecordCount);
    }

    [Fact]
    public void ShouldAccept_WhenUsingRecommendationWithOutOfRangeScores()
    {
        // Arrange
        var recommendation = new Recommendation();

        // Act
        var rec = new Recommendation
        {
            ImpactScore = -0.5f,
            EffortScore = 1.5f
        };

        // Assert - No validation in the record itself
        Assert.Equal(-0.5f, rec.ImpactScore);
        Assert.Equal(1.5f, rec.EffortScore);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingAnalysisTaskContextToString()
    {
        // Arrange
        var context = new AnalysisTaskContext { Subject = "Test Analysis" };

        // Act
        var stringRepresentation = context.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        Assert.Contains("AnalysisTaskContext", stringRepresentation);
    }

    #endregion
}
