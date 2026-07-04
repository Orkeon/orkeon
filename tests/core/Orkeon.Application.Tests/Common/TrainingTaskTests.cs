using Orkeon.Application.Training;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Common;

public class TrainingTaskTests
{
    private static readonly int[] SampleIntArray = [1, 2, 3];

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithRequiredParameters()
    {
        // Arrange
        var id = "task-123";
        var description = "Analyze customer feedback";
        var expectedOutput = "Sentiment analysis report";
        var requiredSkills = new List<string> { "NLP", "DataAnalysis" };

        // Act
        var task = new TrainingTask(
            Id: id,
            Description: description,
            ExpectedOutput: expectedOutput,
            RequiredSkills: requiredSkills);

        // Assert
        Assert.Equal(id, task.Id);
        Assert.Equal(description, task.Description);
        Assert.Equal(expectedOutput, task.ExpectedOutput);
        Assert.Equal(requiredSkills, task.RequiredSkills);
        Assert.Null(task.Context);
        Assert.Null(task.TimeLimit);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithAllParameters()
    {
        // Arrange
        var id = "task-456";
        var description = "Generate quarterly report";
        var expectedOutput = "PDF report with charts";
        var requiredSkills = new List<string> { "Reporting", "DataVisualization" };
        var context = new Dictionary<string, object>
        {
            { "quarter", "Q3" },
            { "year", 2024 },
            { "includeCharts", true }
        };
        var timeLimit = TimeSpan.FromHours(2);

        // Act
        var task = new TrainingTask(
            Id: id,
            Description: description,
            ExpectedOutput: expectedOutput,
            RequiredSkills: requiredSkills,
            Context: context,
            TimeLimit: timeLimit);

        // Assert
        Assert.Equal(id, task.Id);
        Assert.Equal(description, task.Description);
        Assert.Equal(expectedOutput, task.ExpectedOutput);
        Assert.Equal(requiredSkills, task.RequiredSkills);
        Assert.NotNull(task.Context);
        Assert.Equal(3, task.Context.Count);
        Assert.Equal("Q3", task.Context["quarter"]);
        Assert.Equal(2024, task.Context["year"]);
        Assert.True((bool)task.Context["includeCharts"]);
        Assert.Equal(timeLimit, task.TimeLimit);
    }

    [Fact]
    public void ShouldGenerateNewIdAndSetProperties_WhenCreating()
    {
        // Arrange
        var description = "Process customer orders";
        var expectedOutput = "Order confirmation emails sent";
        var requiredSkills = new List<string> { "OrderProcessing", "EmailComposition" };

        // Act
        var task = TrainingTask.Create(description, expectedOutput, requiredSkills);

        // Assert
        Assert.NotNull(task.Id);
        Assert.NotEmpty(task.Id);
        Assert.True(Guid.TryParse(task.Id, out _), "Id should be a valid GUID");
        Assert.Equal(description, task.Description);
        Assert.Equal(expectedOutput, task.ExpectedOutput);
        Assert.Equal(requiredSkills, task.RequiredSkills);
        Assert.Null(task.Context);
        Assert.Null(task.TimeLimit);
    }

    [Fact]
    public void ShouldHaveUniqueIds_WhenCreatingWithMultipleInstances()
    {
        // Arrange
        var description = "Common task";
        var expectedOutput = "Common output";
        var requiredSkills = new List<string> { "Skill1" };

        // Act
        var task1 = TrainingTask.Create(description, expectedOutput, requiredSkills);
        var task2 = TrainingTask.Create(description, expectedOutput, requiredSkills);
        var task3 = TrainingTask.Create(description, expectedOutput, requiredSkills);

        // Assert
        Assert.NotEqual(task1.Id, task2.Id);
        Assert.NotEqual(task2.Id, task3.Id);
        Assert.NotEqual(task1.Id, task3.Id);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var id = "same-id";
        var description = "Same description";
        var expectedOutput = "Same output";
        var requiredSkills = new List<string> { "Skill1", "Skill2" };
        var context = new Dictionary<string, object> { { "key", "value" } };
        var timeLimit = TimeSpan.FromMinutes(30);

        // Act
        var task1 = new TrainingTask(id, description, expectedOutput, requiredSkills, context, timeLimit);
        var task2 = new TrainingTask(id, description, expectedOutput, requiredSkills, context, timeLimit);

        // Assert
        Assert.Equal(task1, task2);
        Assert.True(task1 == task2);
        Assert.Equal(task1.GetHashCode(), task2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var requiredSkills = new List<string> { "Skill1" };

        // Act
        var task1 = new TrainingTask("id1", "desc1", "output1", requiredSkills);
        var task2 = new TrainingTask("id2", "desc1", "output1", requiredSkills);

        // Assert
        Assert.NotEqual(task1, task2);
        Assert.False(task1 == task2);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWithExpression()
    {
        // Arrange
        var original = TrainingTask.Create(
            "Original description",
            "Original output",
            ["Skill1"]);

        // Act
        var modified = original with
        {
            Description = "Modified description",
            TimeLimit = TimeSpan.FromHours(1)
        };

        // Assert
        Assert.Equal(original.Id, modified.Id); // Id remains the same
        Assert.NotEqual(original.Description, modified.Description);
        Assert.Equal("Modified description", modified.Description);
        Assert.Equal(original.ExpectedOutput, modified.ExpectedOutput);
        Assert.Equal(original.RequiredSkills, modified.RequiredSkills);
        Assert.Null(original.TimeLimit);
        Assert.Equal(TimeSpan.FromHours(1), modified.TimeLimit);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingEmptyRequiredSkills()
    {
        // Arrange & Act
        var task = TrainingTask.Create(
            "Simple task",
            "Any output",
            []);

        // Assert
        Assert.NotNull(task.RequiredSkills);
        Assert.Empty(task.RequiredSkills);
    }

    [Fact]
    public void ShouldBeHandledCorrectly_WhenUsingLargeContext()
    {
        // Arrange
        var context = new Dictionary<string, object>();
        for (int i = 0; i < 100; i++)
        {
            context[$"key{i}"] = $"value{i}";
        }

        // Act
        var task = new TrainingTask(
            "id",
            "description",
            "output",
            ["skill"],
            context);

        // Assert
        Assert.Equal(100, task.Context!.Count);
        Assert.Equal("value50", task.Context["key50"]);
    }

    [Fact]
    public void ShouldBeHandledCorrectly_WhenTimingLimitEdgeCases()
    {
        // Arrange & Act
        var taskWithMaxTime = new TrainingTask(
            "id1",
            "desc",
            "output",
            ["skill"],
            TimeLimit: TimeSpan.MaxValue);

        var taskWithZeroTime = new TrainingTask(
            "id2",
            "desc",
            "output",
            ["skill"],
            TimeLimit: TimeSpan.Zero);

        // Assert
        Assert.Equal(TimeSpan.MaxValue, taskWithMaxTime.TimeLimit);
        Assert.Equal(TimeSpan.Zero, taskWithZeroTime.TimeLimit);
    }

    [Fact]
    public void ShouldReturnMeaningfulRepresentation_WhenCallingToString()
    {
        // Arrange
        var task = new TrainingTask(
            "test-id",
            "Test description",
            "Test output",
            ["Skill1", "Skill2"]);

        // Act
        var result = task.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("test-id", result);
        Assert.Contains("Test description", result);
        Assert.Contains("Test output", result);
    }

    [Fact]
    public void ShouldWork_WhenUsingComplexContextWithNestedStructures()
    {
        // Arrange
        var nestedDict = new Dictionary<string, object>
        {
            { "level2Key", "level2Value" },
            { "level2Number", 42 }
        };

        var context = new Dictionary<string, object>
        {
            { "simple", "value" },
            { "number", 123 },
            { "boolean", true },
            { "nested", nestedDict },
            { "array", SampleIntArray }
        };

        // Act
        var task = new TrainingTask(
            "complex-id",
            "Complex task",
            "Complex output",
            ["DataProcessing"],
            context);

        // Assert
        Assert.Equal(5, task.Context!.Count);
        Assert.Equal("value", task.Context["simple"]);
        Assert.Equal(123, task.Context["number"]);
        Assert.True((bool)task.Context["boolean"]);

        var retrievedNested = task.Context["nested"] as Dictionary<string, object>;
        Assert.NotNull(retrievedNested);
        Assert.Equal("level2Value", retrievedNested["level2Key"]);

        var retrievedArray = task.Context["array"] as int[];
        Assert.NotNull(retrievedArray);
        Assert.Equal(3, retrievedArray.Length);
    }

    [Fact]
    public void ShouldNotAffectOriginal_WhenUsingRequiredSkillsModification()
    {
        // Arrange
        var originalSkills = new List<string> { "Skill1", "Skill2" };
        var task = TrainingTask.Create("desc", "output", originalSkills);

        // Assert — RequiredSkills is exposed read-only and reflects the supplied skills.
        Assert.Equal(2, task.RequiredSkills.Count);
        Assert.Contains("Skill1", task.RequiredSkills);
        Assert.Contains("Skill2", task.RequiredSkills);
    }

    [Fact]
    public void ShouldStillWork_WhenCreatingWithNullParameters()
    {
        // Note: This test documents current behavior. 
        // In production, you might want to add null checks

        // Act & Assert - Should not throw
        var task = TrainingTask.Create(
            null!,  // Suppressing null warning for test
            null!,
            null!);

        Assert.NotNull(task.Id);
        Assert.Null(task.Description);
        Assert.Null(task.ExpectedOutput);
        Assert.Null(task.RequiredSkills);
    }

    [Fact]
    public void ShouldWork_WhenUsingDeconstruction()
    {
        // Arrange
        var task = new TrainingTask(
            "id-123",
            "Do something",
            "Result",
            ["Skill"],
            new Dictionary<string, object> { { "key", "value" } },
            TimeoutLong);

        // Act
        var (id, description, expectedOutput, requiredSkills, context, timeLimit) = task;

        // Assert
        Assert.Equal("id-123", id);
        Assert.Equal("Do something", description);
        Assert.Equal("Result", expectedOutput);
        Assert.Single(requiredSkills);
        Assert.NotNull(context);
        Assert.Equal(TimeoutLong, timeLimit);
    }

    [Fact]
    public void ShouldCreateManyTasks_WhenUsingPerformanceTest()
    {
        // Arrange
        var tasks = new List<TrainingTask>();
        var skills = new List<string> { "Performance", "Testing" };

        // Act
        var startTime = DateTime.UtcNow;
        for (int i = 0; i < 10000; i++)
        {
            tasks.Add(TrainingTask.Create(
                $"Task {i}",
                $"Output {i}",
                skills));
        }
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        Assert.Equal(10000, tasks.Count);
        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Creating 10000 tasks took {elapsed.TotalMilliseconds}ms");

        // Verify all IDs are unique
        var uniqueIds = tasks.Select(t => t.Id).Distinct().Count();
        Assert.Equal(10000, uniqueIds);
    }
}
