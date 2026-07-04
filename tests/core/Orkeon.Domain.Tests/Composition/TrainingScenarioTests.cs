using Orkeon.Domain.Training;

using Orkeon.Domain.Common;
namespace Orkeon.Domain.Tests.Composition;

/// <summary>
/// Tests for Training Scenario records following Clean Architecture principles.
/// </summary>
public class TrainingScenarioTests
{
    private static readonly string[] TimeMemoryConstraints = ["time", "memory"];
    private static readonly string[] PandasMatplotlibTools = ["pandas", "matplotlib"];
    private static readonly string[] DataTechniques = ["scaling", "encoding", "feature_creation"];
    private static readonly string[] MlAlgorithms = ["XGBoost", "RandomForest", "NeuralNetwork"];
    private static readonly int[] SimulatedErrors = [400, 401, 404, 429, 500];

    #region TrainingScenario Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingTrainingScenarioWithDefaultConstructor()
    {
        var scenario = new TrainingScenario();

        Assert.NotNull(scenario.Id);
        Assert.Equal(string.Empty, scenario.Name);
        Assert.Equal(string.Empty, scenario.Description);
        Assert.Equal(string.Empty, scenario.Goal);
        Assert.NotNull(scenario.Objectives);
        Assert.Empty(scenario.Objectives);
        Assert.NotNull(scenario.InitialContext);
        Assert.Empty(scenario.InitialContext);
        Assert.NotNull(scenario.Steps);
        Assert.Empty(scenario.Steps);
        Assert.Equal(TrainingDifficulty.Medium, scenario.Difficulty);
        Assert.Equal(TimeSpan.Zero, scenario.EstimatedDuration);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingTrainingScenarioUsingProperties()
    {
        var id = TrainingScenarioId.Create();
        var objectives = new List<TrainingObjective>
        {
            new TrainingObjective { Description = "Complete task within time limit" },
            new TrainingObjective { Description = "Achieve 90% accuracy" }
        };
        var initialContext = new Dictionary<string, object>
        {
            { "environment", "production" },
            { "dataSize", 10000 },
            { "constraints", TimeMemoryConstraints }
        };
        var steps = new List<TrainingStep>
        {
            new TrainingStep { Action = "Initialize system" },
            new TrainingStep { Action = "Process data" },
            new TrainingStep { Action = "Generate report" }
        };
        var duration = TimeSpan.FromHours(2);

        var scenario = new TrainingScenario
        {
            Id = id,
            Name = "Advanced Data Processing",
            Description = "Train crew on complex data processing workflows",
            Goal = "Successfully process large datasets with high accuracy",
            Objectives = objectives,
            InitialContext = initialContext,
            Steps = steps,
            Difficulty = TrainingDifficulty.Hard,
            EstimatedDuration = duration
        };

        Assert.Equal(id, scenario.Id);
        Assert.Equal("Advanced Data Processing", scenario.Name);
        Assert.Equal("Train crew on complex data processing workflows", scenario.Description);
        Assert.Equal("Successfully process large datasets with high accuracy", scenario.Goal);
        Assert.Equal(objectives, scenario.Objectives);
        Assert.Equal(initialContext, scenario.InitialContext);
        Assert.Equal(steps, scenario.Steps);
        Assert.Equal(TrainingDifficulty.Hard, scenario.Difficulty);
        Assert.Equal(duration, scenario.EstimatedDuration);
    }

    [Fact]
    public void ShouldHaveUniqueIds_WhenUsingTrainingScenarioWithMultipleInstances()
    {
        var scenario1 = new TrainingScenario();
        var scenario2 = new TrainingScenario();
        var scenario3 = new TrainingScenario();

        Assert.NotEqual(scenario1.Id, scenario2.Id);
        Assert.NotEqual(scenario2.Id, scenario3.Id);
        Assert.NotEqual(scenario1.Id, scenario3.Id);
    }

    #endregion

    #region TrainingObjective Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingTrainingObjectiveWithDefaultConstructor()
    {
        var objective = new TrainingObjective();

        Assert.NotNull(objective.Id);
        Assert.Equal(string.Empty, objective.Description);
        Assert.NotNull(objective.SuccessCriteria);
        Assert.Empty(objective.SuccessCriteria);
        Assert.Equal(1.0, objective.Weight);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingTrainingObjectiveProperties()
    {
        var id = TrainingObjectiveId.Create();
        var successCriteria = new Dictionary<string, object>
        {
            { "minAccuracy", 0.95 },
            { "maxDuration", 300 },
            { "errorRate", 0.02 }
        };

        var objective = new TrainingObjective
        {
            Id = id,
            Description = "Achieve high accuracy with minimal errors",
            SuccessCriteria = successCriteria,
            Weight = 2.5
        };

        Assert.Equal(id, objective.Id);
        Assert.Equal("Achieve high accuracy with minimal errors", objective.Description);
        Assert.Equal(successCriteria, objective.SuccessCriteria);
        Assert.Equal(2.5, objective.Weight);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(-1.0)]
    [InlineData(100.0)]
    public void ShouldAcceptVariousValues_WhenUsingTrainingObjectiveWeight(double weight)
    {
        var objective = new TrainingObjective { Weight = weight };
        Assert.Equal(weight, objective.Weight);
    }

    #endregion

    #region TrainingStep Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingTrainingStepWithDefaultConstructor()
    {
        var step = new TrainingStep();

        Assert.NotNull(step.Id);
        Assert.Equal(string.Empty, step.Action);
        Assert.Equal(string.Empty, step.ExpectedResult);
        Assert.NotNull(step.Context);
        Assert.Empty(step.Context);
        Assert.NotNull(step.Hints);
        Assert.Empty(step.Hints);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingTrainingStepUsingProperties()
    {
        var id = TrainingStepId.Create();
        var context = new Dictionary<string, object>
        {
            { "tool", "DataProcessor" },
            { "inputFile", "data.csv" },
            { "options", new { parallel = true, batchSize = 1000 } }
        };
        var hints = new List<string>
        {
            "Use parallel processing for better performance",
            "Check data validation before processing",
            "Monitor memory usage"
        };

        var step = new TrainingStep
        {
            Id = id,
            Action = "Process CSV file with DataProcessor tool",
            ExpectedResult = "Processed data saved to output.json with 100% success rate",
            Context = context,
            Hints = hints
        };

        Assert.Equal(id, step.Id);
        Assert.Equal("Process CSV file with DataProcessor tool", step.Action);
        Assert.Equal("Processed data saved to output.json with 100% success rate", step.ExpectedResult);
        Assert.Equal(context, step.Context);
        Assert.Equal(hints, step.Hints);
        Assert.Equal(3, step.Hints.Count);
    }

    [Fact]
    public void ShouldSupportCollections_WhenUsingTrainingStepUsingHints()
    {
        var step = new TrainingStep
        {
            Hints = ["First hint", "Third hint", "New hint"]
        };

        Assert.Equal(3, step.Hints.Count);
        Assert.Contains("First hint", step.Hints);
        Assert.Contains("New hint", step.Hints);
    }

    #endregion

    #region TrainingDifficulty Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingTrainingDifficulty()
    {
        var expectedValues = new[]
        {
            TrainingDifficulty.Beginner,
            TrainingDifficulty.Easy,
            TrainingDifficulty.Medium,
            TrainingDifficulty.Hard,
            TrainingDifficulty.Expert
        };

        foreach (var expectedValue in expectedValues)
            Assert.True(Enum.IsDefined<TrainingDifficulty>(expectedValue));

        Assert.Equal(expectedValues.Length, Enum.GetValues<TrainingDifficulty>().Length);
    }

    [Fact]
    public void ShouldBeBeginner_WhenUsingTrainingDifficultyWithDefaultValue()
    {
        Assert.Equal(TrainingDifficulty.Beginner, default(TrainingDifficulty));
    }

    [Fact]
    public void ShouldBeInOrder_WhenUsingTrainingDifficultyNumericValues()
    {
        Assert.True((int)TrainingDifficulty.Beginner < (int)TrainingDifficulty.Easy);
        Assert.True((int)TrainingDifficulty.Easy < (int)TrainingDifficulty.Medium);
        Assert.True((int)TrainingDifficulty.Medium < (int)TrainingDifficulty.Hard);
        Assert.True((int)TrainingDifficulty.Hard < (int)TrainingDifficulty.Expert);
    }

    #endregion

    #region TrainingResult Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingTrainingResultWithDefaultConstructor()
    {
        var beforeCreation = DateTime.UtcNow;
        var result = new TrainingResult();
        var afterCreation = DateTime.UtcNow;

        Assert.NotNull(result.ScenarioId);
        Assert.NotNull(result.CrewId);
        Assert.False(result.Success);
        Assert.Equal(0.0, result.Score);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Empty(result.ObjectiveScores);
        Assert.Empty(result.Feedback);
        Assert.True(result.CompletedAt >= beforeCreation);
        Assert.True(result.CompletedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, result.CompletedAt.Kind);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingTrainingResultUsingProperties()
    {
        var completedAt = DateTime.UtcNow.AddMinutes(-30);
        var objectiveScores = new Dictionary<string, double>
        {
            { "accuracy", 0.95 },
            { "speed", 0.87 },
            { "efficiency", 0.92 }
        };
        var feedback = new List<string>
        {
            "Excellent accuracy achieved",
            "Consider optimizing processing speed",
            "Good resource utilization"
        };

        var result = new TrainingResult
        {
            ScenarioId = TrainingScenarioId.Create(),
            CrewId = CrewId.Create(),
            Success = true,
            Score = 0.91,
            Duration = TimeSpan.FromMinutes(45),
            ObjectiveScores = objectiveScores,
            Feedback = feedback,
            CompletedAt = completedAt
        };

        Assert.NotNull(result.ScenarioId);
        Assert.True(result.Success);
        Assert.Equal(0.91, result.Score);
        Assert.Equal(TimeSpan.FromMinutes(45), result.Duration);
        Assert.Equal(objectiveScores, result.ObjectiveScores);
        Assert.Equal(feedback, result.Feedback);
        Assert.Equal(completedAt, result.CompletedAt);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void ShouldAcceptVariousValues_WhenUsingTrainingResultScoring(double score)
    {
        var result = new TrainingResult { Score = score };
        Assert.Equal(score, result.Score);
    }

    #endregion

    #region Integration and Scenario Tests

    [Fact]
    public void ShouldMachineLearningWorkflow_WhenUsingTrainingScenarioWithCompleteScenario()
    {
        var scenario = new TrainingScenario
        {
            Name = "Machine Learning Model Development",
            Description = "Train crew to develop, train, and deploy ML models",
            Goal = "Successfully build and deploy a production-ready ML model",
            Difficulty = TrainingDifficulty.Hard,
            EstimatedDuration = TimeSpan.FromHours(4),
            InitialContext = new Dictionary<string, object>
            {
                { "dataset", "customer_churn.csv" },
                { "targetAccuracy", 0.85 },
                { "timeLimit", TimeSpan.FromHours(3) },
                { "resources", new { gpu = true, memory = "32GB" } }
            },
            Objectives =
            [
                new TrainingObjective
                {
                    Description = "Achieve model accuracy >= 85%",
                    Weight = 3.0,
                    SuccessCriteria = new Dictionary<string, object>
                    {
                        { "metric", "accuracy" },
                        { "threshold", 0.85 },
                        { "validation", "cross-validation" }
                    }
                },
                new TrainingObjective
                {
                    Description = "Complete within time limit",
                    Weight = 2.0,
                    SuccessCriteria = new Dictionary<string, object> { { "maxDuration", TimeSpan.FromHours(3) } }
                },
                new TrainingObjective
                {
                    Description = "Deploy model to production",
                    Weight = 2.5,
                    SuccessCriteria = new Dictionary<string, object>
                    {
                        { "endpoint", "https://api.example.com/predict" },
                        { "latency", "<100ms" }
                    }
                }
            ],
            Steps =
            [
                new TrainingStep
                {
                    Action = "Load and explore dataset",
                    ExpectedResult = "Dataset loaded with shape, statistics, and visualizations",
                    Context = new Dictionary<string, object> { { "file", "customer_churn.csv" }, { "tools", PandasMatplotlibTools } },
                    Hints = ["Check for missing values", "Analyze feature distributions", "Identify target variable imbalance"]
                },
                new TrainingStep
                {
                    Action = "Preprocess and engineer features",
                    ExpectedResult = "Clean dataset with engineered features ready for training",
                    Context = new Dictionary<string, object> { { "techniques", DataTechniques } },
                    Hints = ["Handle categorical variables properly", "Create interaction features", "Apply appropriate scaling"]
                },
                new TrainingStep
                {
                    Action = "Train and evaluate models",
                    ExpectedResult = "Best model selected with performance metrics",
                    Context = new Dictionary<string, object> { { "algorithms", MlAlgorithms }, { "validation", "5-fold cross-validation" } },
                    Hints = ["Try ensemble methods", "Tune hyperparameters", "Compare ROC curves"]
                }
            ]
        };

        Assert.Equal("Machine Learning Model Development", scenario.Name);
        Assert.Equal(TrainingDifficulty.Hard, scenario.Difficulty);
        Assert.Equal(3, scenario.Objectives.Count);
        Assert.Equal(3, scenario.Steps.Count);
        Assert.Equal(4, scenario.InitialContext.Count);
        Assert.True(scenario.Objectives.All(o => o.Weight > 1.0));
        Assert.True(scenario.Steps.All(s => s.Hints.Count >= 3));
    }

    [Fact]
    public void ShouldScenario_WhenUsingTrainingResultUsingSuccessfulCompletion()
    {
        var scenarioId = TrainingScenarioId.Create();
        var crewId = CrewId.Create();
        var endTime = DateTime.UtcNow;

        var result = new TrainingResult
        {
            ScenarioId = scenarioId,
            CrewId = crewId,
            Success = true,
            Score = 0.92,
            Duration = TimeSpan.FromHours(1.8),
            CompletedAt = endTime,
            ObjectiveScores = new Dictionary<string, double>
            {
                { "accuracy_objective", 0.88 },
                { "time_objective", 0.95 },
                { "deployment_objective", 0.93 }
            },
            Feedback =
            [
                "✅ Successfully achieved target accuracy of 88%",
                "✅ Completed 25 minutes ahead of schedule",
                "✅ Model deployed and responding within latency requirements",
                "💡 Consider implementing model monitoring for production",
                "💡 Explore AutoML options for faster iteration"
            ]
        };

        Assert.True(result.Success);
        Assert.Equal(0.92, result.Score);
        Assert.Equal(3, result.ObjectiveScores.Count);
        Assert.Equal(5, result.Feedback.Count);
        Assert.Contains("✅", result.Feedback[0]);
        Assert.Contains("💡", result.Feedback[3]);
        Assert.True(result.Duration.TotalHours <= 2);
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTrainingScenarioWithNullCollections()
    {
        var scenario = new TrainingScenario
        {
            Objectives = null!,
            InitialContext = null!,
            Steps = null!
        };

        Assert.Null(scenario.Objectives);
        Assert.Null(scenario.InitialContext);
        Assert.Null(scenario.Steps);
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingTrainingStepWithEmptyId()
    {
        var step = new TrainingStep();
        Assert.NotNull(step.Id);
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingTrainingScenarioWithNegativeDuration()
    {
        var scenario = new TrainingScenario { EstimatedDuration = TimeSpan.FromHours(-2) };
        Assert.Equal(TimeSpan.FromHours(-2), scenario.EstimatedDuration);
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingTrainingResultWithFutureCompletedAt()
    {
        var futureDate = DateTime.UtcNow.AddDays(7);
        var result = new TrainingResult { CompletedAt = futureDate };
        Assert.True(result.CompletedAt > DateTime.UtcNow);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTrainingScenarioWithUnicodeContent()
    {
        var scenario = new TrainingScenario
        {
            Name = "国际化测试场景 🌍",
            Description = "测试多语言支持",
            Goal = "完成国际化任务 ✅",
            Objectives =
            [
                new TrainingObjective
                {
                    Description = "支持中文界面 🇨🇳",
                    SuccessCriteria = new Dictionary<string, object> { { "语言", "中文" }, { "完成度", "100%" } }
                }
            ],
            Steps =
            [
                new TrainingStep
                {
                    Action = "添加翻译文件 📄",
                    ExpectedResult = "所有文本已翻译",
                    Hints = ["使用专业翻译", "注意文化差异 🌏"]
                }
            ]
        };

        Assert.Contains("🌍", scenario.Name);
        Assert.Contains("✅", scenario.Goal);
        Assert.Contains("🇨🇳", scenario.Objectives[0].Description);
        Assert.Contains("📄", scenario.Steps[0].Action);
        Assert.Contains("🌏", scenario.Steps[0].Hints[1]);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingTrainingClassesToString()
    {
        var scenario = new TrainingScenario { Name = "Test Scenario" };
        var objective = new TrainingObjective { Description = "Test Objective" };
        var step = new TrainingStep { Action = "Test Action" };
        var result = new TrainingResult { ScenarioId = TrainingScenarioId.Create() };

        Assert.NotNull(scenario.ToString());
        Assert.NotNull(objective.ToString());
        Assert.NotNull(step.ToString());
        Assert.NotNull(result.ToString());
    }

    #endregion

    #region Complex Scenario Tests

    [Fact]
    public void ShouldApiIntegrationTraining_WhenUsingTrainingScenario()
    {
        var scenario = new TrainingScenario
        {
            Name = "REST API Integration Training",
            Description = "Learn to integrate with external REST APIs",
            Goal = "Successfully integrate with third-party services",
            Difficulty = TrainingDifficulty.Medium,
            EstimatedDuration = TimeSpan.FromHours(2.5),
            InitialContext = new Dictionary<string, object>
            {
                { "apiEndpoint", "https://api.example.com/v2" },
                { "authType", "Bearer Token" },
                { "rateLimit", 1000 },
                { "sandbox", true }
            },
            Steps =
            [
                new TrainingStep
                {
                    Action = "Authenticate with API",
                    ExpectedResult = "Valid access token obtained",
                    Context = new Dictionary<string, object> { { "endpoint", "/auth/token" }, { "method", "POST" }, { "credentials", "provided_in_env" } },
                    Hints = ["Check environment variables", "Handle token expiration"]
                },
                new TrainingStep
                {
                    Action = "Fetch paginated data",
                    ExpectedResult = "All records retrieved across multiple pages",
                    Context = new Dictionary<string, object> { { "endpoint", "/data/records" }, { "pageSize", 100 }, { "expectedTotal", 2500 } },
                    Hints = ["Implement pagination logic", "Handle rate limiting"]
                },
                new TrainingStep
                {
                    Action = "Handle errors gracefully",
                    ExpectedResult = "Appropriate error handling for all status codes",
                    Context = new Dictionary<string, object> { { "simulatedErrors", SimulatedErrors } },
                    Hints = ["Implement retry logic", "Use exponential backoff"]
                }
            ]
        };

        Assert.Equal(TrainingDifficulty.Medium, scenario.Difficulty);
        Assert.Equal(3, scenario.Steps.Count);
        Assert.All(scenario.Steps, s => Assert.NotEmpty(s.Hints));
        Assert.Contains("sandbox", scenario.InitialContext.Keys);
    }

    #endregion
}
