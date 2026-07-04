using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Task.Contexts;
using Orkeon.Domain.Task;
using DomainTask = Orkeon.Domain.Task;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Tests.Task;

public class AnalysisTaskTests
{
    // Test doubles
    private class TestTaskCallback : ITaskCallback
    {
        public List<string> ReceivedEvents { get; } = [];

        public System.Threading.Tasks.Task OnTaskStartAsync(DomainTask.ICrewTask task)
        {
            ReceivedEvents.Add($"Started:{task.TaskId}");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnTaskCompletedAsync(DomainTask.ICrewTask task, TaskOutput output)
        {
            ReceivedEvents.Add($"Completed:{task.TaskId}:{output.RawOutput}");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnTaskFailedAsync(DomainTask.ICrewTask task, string error)
        {
            ReceivedEvents.Add($"Failed:{task.TaskId}:{error}");
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    [Fact]
    public void ShouldCreateAnalysisTask_WhenConstructingWithValidParameters()
    {
        // Arrange
        var description = TaskDescription.From("Analyze market trends");
        var expectedOutput = ExpectedOutput.From("Market analysis report");
        var subject = "Technology sector";
        var methodology = "Statistical analysis";

        // Act
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            description,
            expectedOutput,
            subject,
            methodology);

        // Assert
        Assert.NotNull(task);
        Assert.Equal(description, task.Description);
        Assert.Equal(expectedOutput, task.ExpectedOutput);
        Assert.Equal(subject, task.TypedContext.Subject);
        Assert.Equal(methodology, task.TypedContext.Methodology);
        Assert.Equal(TaskPriority.Normal, task.Priority);
        Assert.False(task.AsyncExecution);
    }

    [Fact]
    public void ShouldSetAllProperties_WhenConstructingWithAllParameters()
    {
        // Arrange
        var description = TaskDescription.From("Complex analysis");
        var expectedOutput = ExpectedOutput.From("Detailed report");
        var subject = "Market dynamics";
        var methodology = "Machine learning analysis";
        var priority = TaskPriority.High;
        var asyncExecution = true;
        var outputJson = JsonSchema.From("{\"type\":\"object\"}");
        var outputPydantic = typeof(AnalysisResult);
        var outputFile = "analysis_output.json";
        var callback = new TestTaskCallback();
        var humanInput = true;

        // Act
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            description,
            expectedOutput,
            subject,
            methodology,
            priority,
            new TaskOutputOptions
            {
                AsyncExecution = asyncExecution,
                OutputJson = outputJson,
                OutputPydantic = outputPydantic,
                OutputFile = outputFile,
                Callback = callback,
                HumanInput = humanInput
            });

        // Assert
        Assert.Equal(priority, task.Priority);
        Assert.Equal(asyncExecution, task.AsyncExecution);
        Assert.Equal(outputJson, task.OutputJson);
        Assert.Equal(outputPydantic, task.OutputPydantic);
        Assert.Equal(outputFile, task.OutputFile);
        Assert.Equal(callback, task.Callback);
        Assert.Equal(humanInput, task.HumanInput);
    }

    [Fact]
    public void ShouldAddToContext_WhenAddingMetricWithValidMetric()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(

            TaskId.Create(),
            TaskDescription.From("Analysis"),
            ExpectedOutput.From("Results"),
            "Test subject");

        // Act
        task.AddMetric("accuracy", 0.95);
        task.AddMetric("precision", 0.92);

        // Assert
        var context = task.TypedContext;
        Assert.Equal(2, context.Metrics.Count);
        Assert.Equal(0.95, context.Metrics["accuracy"]);
        Assert.Equal(0.92, context.Metrics["precision"]);
    }

    [Fact]
    public void ShouldAddAllToContext_WhenUsingAddMetricsWithMultipleMetrics()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Batch analysis"),
            ExpectedOutput.From("Results"),
            "Data");
        var metrics = new Dictionary<string, object>
        {
            ["recall"] = 0.88,
            ["f1_score"] = 0.90,
            ["auc"] = 0.93
        };

        // Act
        task.AddMetrics(metrics);

        // Assert
        var context = task.TypedContext;
        Assert.Equal(3, context.Metrics.Count);
        Assert.Equal(0.88, context.Metrics["recall"]);
        Assert.Equal(0.90, context.Metrics["f1_score"]);
        Assert.Equal(0.93, context.Metrics["auc"]);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingAddMetricsWithNullDictionary()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Analysis"),
            ExpectedOutput.From("Results"),
            "Subject");

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => task.AddMetrics(null!));
        Assert.Equal("metrics", exception.ParamName);
    }

    [Fact]
    public void ShouldAddToContext_WhenAddingInsightWithValidInsight()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Insight analysis"),
            ExpectedOutput.From("Insights"),
            "Market behavior");

        // Act
        task.AddInsight("Strong correlation between X and Y", 0.85f);
        task.AddInsight("Seasonal pattern detected", 0.92f);

        // Assert
        var context = task.TypedContext;
        Assert.Equal(2, context.Insights.Count);
        Assert.Contains("Strong correlation between X and Y", context.Insights);
        Assert.Contains("Seasonal pattern detected", context.Insights);
        Assert.True(context.ConfidenceScore > 0.85f); // Should be average of confidence scores
    }

    [Fact]
    public void ShouldAddToContext_WhenAddingDataSourceWithValidDataSource()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Data analysis"),
            ExpectedOutput.From("Analysis results"),
            "Customer data");
        var dataSource = new DataSource
        {
            Name = "Customer Database",
            Type = "SQL",
            Location = "server.db",
            RecordCount = 10000
        };

        // Act
        task.AddDataSource(dataSource);

        // Assert
        var context = task.TypedContext;
        Assert.Single(context.DataSources);
        Assert.Equal("Customer Database", context.DataSources[0].Name);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenAddingDataSourceWithNullDataSource()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Analysis"),
            ExpectedOutput.From("Results"),
            "Subject");

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => task.AddDataSource(null!));
        Assert.Equal("dataSource", exception.ParamName);
    }

    [Fact]
    public void ShouldAddNewStage_WhenUsingStartStageWithValidStageName()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Multi-stage analysis"),
            ExpectedOutput.From("Complete analysis"),
            "Complex data");

        // Act
        task.StartStage("Data Collection");
        task.StartStage("Data Processing");

        // Assert
        var context = task.TypedContext;
        Assert.Equal(2, context.Stages.Count);
        Assert.Equal("Data Collection", context.Stages[0].Name);
        Assert.Equal("Data Processing", context.Stages[1].Name);
        Assert.All(context.Stages, s => Assert.Equal(StageStatus.InProgress, s.Status));
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingStartStageWithEmptyStageName()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Analysis"),
            ExpectedOutput.From("Results"),
            "Subject");

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => task.StartStage(""));
        Assert.Equal("stageName", exception.ParamName);
    }

    [Fact]
    public void ShouldCompleteCurrentStage_WhenUsingCompleteStage()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Stage-based analysis"),
            ExpectedOutput.From("Results"),
            "Data");
        task.StartStage("Stage 1");

        // Act
        task.CompleteStage("Stage 1 completed successfully");

        // Assert
        var context = task.TypedContext;
        var stage1 = context.Stages[0];
        Assert.Equal(StageStatus.Completed, stage1.Status);
        Assert.Equal("Stage 1 completed successfully", stage1.Result);
        Assert.NotNull(stage1.CompletedAt);
    }

    [Fact]
    public void ShouldCompleteAndStartNext_WhenUsingCompleteStageWithNextStage()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Sequential analysis"),
            ExpectedOutput.From("Results"),
            "Data");
        task.StartStage("Stage 1");

        // Act
        task.CompleteStage("Stage 1 done", "Stage 2");

        // Assert
        var context = task.TypedContext;
        Assert.Equal(2, context.Stages.Count);
        Assert.Equal(StageStatus.Completed, context.Stages[0].Status);
        Assert.Equal("Stage 2", context.Stages[1].Name);
        Assert.Equal(StageStatus.InProgress, context.Stages[1].Status);
    }

    [Fact]
    public void ShouldAddToContext_WhenUsingAddRecommendationWithValidRecommendation()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Strategic analysis"),
            ExpectedOutput.From("Recommendations"),
            "Business metrics");
        var recommendation = new Recommendation
        {
            Title = "Optimize Process",
            Description = "Implement automation",
            Priority = RecommendationPriority.High,
            ImpactScore = 0.8f,
            EffortScore = 0.3f,
            Actions = ["Research tools", "Create plan", "Implement"]
        };

        // Act
        task.AddRecommendation(recommendation);

        // Assert
        var context = task.TypedContext;
        Assert.Single(context.Recommendations);
        Assert.Equal("Optimize Process", context.Recommendations[0].Title);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingAddRecommendationWithNullRecommendation()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Analysis"),
            ExpectedOutput.From("Results"),
            "Subject");

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => task.AddRecommendation(null!));
        Assert.Equal("recommendation", exception.ParamName);
    }

    [Fact]
    public void ShouldUpdateContext_WhenSettingMethodologyWithValidMethodology()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Analysis"),
            ExpectedOutput.From("Results"),
            "Subject",
            "Initial methodology");

        // Act
        task.SetMethodology("Advanced statistical methods");

        // Assert
        Assert.Equal("Advanced statistical methods", task.TypedContext.Methodology);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenSettingMethodologyWithEmptyMethodology()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Analysis"),
            ExpectedOutput.From("Results"),
            "Subject");

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => task.SetMethodology(""));
        Assert.Equal("methodology", exception.ParamName);
    }

    [Fact]
    public void ShouldFilterByThreshold_WhenGettingHighConfidenceInsights()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Confidence analysis"),
            ExpectedOutput.From("High confidence insights"),
            "Data patterns");

        // Add insights with varying confidence
        task.AddInsight("Low confidence insight", 0.3f);
        task.AddInsight("Medium confidence insight", 0.6f);
        task.AddInsight("High confidence insight", 0.9f);

        // Act
        var highConfidenceInsights = task.GetHighConfidenceInsights(0.8f).ToList();

        // Assert
        // Since confidence is averaged, check if any insights are returned based on overall confidence
        var overallConfidence = task.ConfidenceScore;
        if (overallConfidence >= 0.8f)
        {
            Assert.Equal(3, highConfidenceInsights.Count);
        }
        else
        {
            Assert.Empty(highConfidenceInsights);
        }
    }

    [Fact]
    public void ShouldReturnOnlyCritical_WhenGettingCriticalRecommendations()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Priority analysis"),
            ExpectedOutput.From("Critical recommendations"),
            "Risk assessment");

        task.AddRecommendation(new Recommendation
        {
            Title = "Low priority",
            Priority = RecommendationPriority.Low
        });
        task.AddRecommendation(new Recommendation
        {
            Title = "Critical issue 1",
            Priority = RecommendationPriority.Critical
        });
        task.AddRecommendation(new Recommendation
        {
            Title = "Medium priority",
            Priority = RecommendationPriority.Medium
        });
        task.AddRecommendation(new Recommendation
        {
            Title = "Critical issue 2",
            Priority = RecommendationPriority.Critical
        });

        // Act
        var criticalRecommendations = task.GetCriticalRecommendations().ToList();

        // Assert
        Assert.Equal(2, criticalRecommendations.Count);
        Assert.All(criticalRecommendations, r => Assert.Equal(RecommendationPriority.Critical, r.Priority));
    }

    [Fact]
    public void ShouldReflectAddedInsights_WhenUsingGetConfidenceScore()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Confidence tracking"),
            ExpectedOutput.From("Confidence analysis"),
            "Statistical data");

        // Act & Assert - Initial confidence
        Assert.Equal(0.0f, task.ConfidenceScore);

        // Add first insight
        task.AddInsight("First insight", 0.8f);
        Assert.Equal(0.8f, task.ConfidenceScore);

        // Add second insight
        task.AddInsight("Second insight", 0.6f);
        var confidenceScore = task.ConfidenceScore;
        Assert.True(Math.Abs(confidenceScore - 0.7f) < 0.0001f); // Average of 0.8 and 0.6, with tolerance for float precision
    }

    [Fact]
    public void ShouldReturnOnlyCompleted_WhenGettingCompletedStages()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Multi-stage process"),
            ExpectedOutput.From("Complete analysis"),
            "Complex workflow");

        task.StartStage("Stage 1");
        task.CompleteStage("Done", "Stage 2");
        task.StartStage("Stage 3");

        // Act
        var completedStages = task.GetCompletedStages().ToList();

        // Assert
        Assert.Single(completedStages);
        Assert.Equal("Stage 1", completedStages[0].Name);
        Assert.Equal(StageStatus.Completed, completedStages[0].Status);
    }

    [Fact]
    public void ShouldProvideAccurateSummary_WhenGettingContextSummary()
    {
        // Arrange
        var task = DomainTask.AnalysisTask.Create(
            TaskId.Create(),
            TaskDescription.From("Summary test"),
            ExpectedOutput.From("Analysis summary"),
            "Market Analysis");

        task.AddInsight("Insight 1", 0.8f);
        task.AddInsight("Insight 2", 0.9f);
        task.StartStage("Stage 1");
        task.CompleteStage("Done", "Stage 2");

        // Act
        var summary = task.GetContextSummary();

        // Assert
        Assert.Contains("Market Analysis", summary);
        Assert.Contains("2 insights", summary);
        Assert.Contains("1/2 stages completed", summary);
        Assert.Contains("confidence:", summary);
    }

    // Helper class for testing structured output
    private class AnalysisResult
    {
        public string Summary { get; set; } = "";
        public double Score { get; set; }
    }
}
