using Orkeon.Domain.Training;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for TrainingTask following Clean Architecture principles.
/// Tests the business rules and validation logic of the TrainingTask record.
/// </summary>
public class TrainingTaskTests
{
    private static readonly int[] Int123 = [1, 2, 3];
    [Fact]
    public void ShouldCreateTrainingTask_WhenConstructingWithValidParameters()
    {
        // Arrange
        var description = "Learn to analyze market data";
        var expectedOutput = "A comprehensive market analysis report";
        var context = new Dictionary<string, object>
        {
            { "market", "technology" },
            { "timeframe", "Q1 2024" }
        };
        var requiredSkills = new List<string> { "Data Analysis", "Market Research" };
        var correctApproach = "Start with historical data analysis";
        var difficultyLevel = 0.6;
        var expectedDuration = TimeSpan.FromHours(4);

        // Act
        var trainingTask = new TrainingTask(
            description,
            expectedOutput,
            context,
            requiredSkills,
            correctApproach,
            difficultyLevel,
            expectedDuration);

        // Assert
        Assert.NotNull(trainingTask.Id);
        Assert.NotNull(trainingTask.Id);
        Assert.Equal(description, trainingTask.Description);
        Assert.Equal(expectedOutput, trainingTask.ExpectedOutput);
        Assert.Equal(context, trainingTask.Context);
        Assert.Equal(requiredSkills, trainingTask.RequiredSkills);
        Assert.Equal(correctApproach, trainingTask.CorrectApproach);
        Assert.Equal(difficultyLevel, trainingTask.DifficultyLevel);
        Assert.Equal(expectedDuration, trainingTask.ExpectedDuration);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullDescription()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new TrainingTask(null!, "Expected output"));

        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullExpectedOutput()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new TrainingTask("Description", null!));

        Assert.Equal("expectedOutput", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateEmptyCollections_WhenConstructingWithNullCollections()
    {
        // Act
        var trainingTask = new TrainingTask(
            "Test description",
            "Test output",
            context: null,
            requiredSkills: null);

        // Assert
        Assert.NotNull(trainingTask.Context);
        Assert.Empty(trainingTask.Context);
        Assert.NotNull(trainingTask.RequiredSkills);
        Assert.Empty(trainingTask.RequiredSkills);
    }

    [Fact]
    public void ShouldUseMedium_WhenConstructingWithDefaultDifficultyLevel()
    {
        // Act
        var trainingTask = new TrainingTask("Description", "Output");

        // Assert
        Assert.Equal(0.5, trainingTask.DifficultyLevel);
    }

    [Fact]
    public void ShouldBeUnique_WhenUsingId()
    {
        // Act
        var task1 = new TrainingTask("Description 1", "Output 1");
        var task2 = new TrainingTask("Description 2", "Output 2");

        // Assert
        Assert.NotEqual(task1.Id, task2.Id);
    }

    [Theory]
    [InlineData(-0.5, 0.0)] // Negative should become 0
    [InlineData(0.0, 0.0)]  // Zero should remain zero
    [InlineData(0.5, 0.5)]  // Normal value should remain
    [InlineData(1.0, 1.0)]  // Maximum should remain
    [InlineData(1.5, 1.0)]  // Above max should become 1.0
    public void ShouldBeClamped_WhenUsingDifficultyLevel(double input, double expected)
    {
        // Act
        var trainingTask = new TrainingTask(
            "Test",
            "Output",
            difficultyLevel: input);

        // Assert
        Assert.Equal(expected, trainingTask.DifficultyLevel);
    }

    [Fact]
    public void ShouldBecome0_WhenUsingDifficultyLevelWithNaN()
    {
        // Act
        var trainingTask = new TrainingTask(
            "Test",
            "Output",
            difficultyLevel: double.NaN);

        // Assert
        Assert.Equal(0.0, trainingTask.DifficultyLevel);
    }

    [Theory]
    [InlineData(0.0, true, false, false)]
    [InlineData(0.2, true, false, false)]
    [InlineData(0.3, false, true, false)]
    [InlineData(0.5, false, true, false)]
    [InlineData(0.6, false, true, false)]
    [InlineData(0.7, false, false, true)]
    [InlineData(0.9, false, false, true)]
    [InlineData(1.0, false, false, true)]
    public void ShouldReturnCorrectValues_WhenUsingDifficultyProperties(
        double difficulty,
        bool expectedBasic,
        bool expectedIntermediate,
        bool expectedAdvanced)
    {
        // Act
        var trainingTask = new TrainingTask(
            "Test",
            "Output",
            difficultyLevel: difficulty);

        // Assert
        Assert.Equal(expectedBasic, trainingTask.IsBasic);
        Assert.Equal(expectedIntermediate, trainingTask.IsIntermediate);
        Assert.Equal(expectedAdvanced, trainingTask.IsAdvanced);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsBasicWithLowDifficulty()
    {
        // Act
        var trainingTask = new TrainingTask(
            "Basic task",
            "Simple output",
            difficultyLevel: 0.1);

        // Assert
        Assert.True(trainingTask.IsBasic);
        Assert.False(trainingTask.IsIntermediate);
        Assert.False(trainingTask.IsAdvanced);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsIntermediateWithMediumDifficulty()
    {
        // Act
        var trainingTask = new TrainingTask(
            "Medium task",
            "Complex output",
            difficultyLevel: 0.5);

        // Assert
        Assert.False(trainingTask.IsBasic);
        Assert.True(trainingTask.IsIntermediate);
        Assert.False(trainingTask.IsAdvanced);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsAdvancedWithHighDifficulty()
    {
        // Act
        var trainingTask = new TrainingTask(
            "Advanced task",
            "Sophisticated output",
            difficultyLevel: 0.8);

        // Assert
        Assert.False(trainingTask.IsBasic);
        Assert.False(trainingTask.IsIntermediate);
        Assert.True(trainingTask.IsAdvanced);
    }

    [Fact]
    public void ShouldAllowNull_WhenConstructingWithNullCorrectApproach()
    {
        // Act
        var trainingTask = new TrainingTask(
            "Description",
            "Output",
            correctApproach: null);

        // Assert
        Assert.Null(trainingTask.CorrectApproach);
    }

    [Fact]
    public void ShouldAllowNull_WhenConstructingWithNullExpectedDuration()
    {
        // Act
        var trainingTask = new TrainingTask(
            "Description",
            "Output",
            expectedDuration: null);

        // Assert
        Assert.Null(trainingTask.ExpectedDuration);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var context = new Dictionary<string, object> { { "key", "value" } };
        var skills = new List<string> { "skill1", "skill2" };

        var task1 = new TrainingTask(
            "Description",
            "Output",
            context,
            skills,
            "Approach",
            0.5,
            TimeSpan.FromHours(1));

        var task2 = new TrainingTask(
            "Description",
            "Output",
            context,
            skills,
            "Approach",
            0.5,
            TimeSpan.FromHours(1));

        // Act & Assert
        // Note: Records won't be equal due to different IDs
        Assert.NotEqual(task1, task2);
        Assert.NotEqual(task1.Id, task2.Id);

        // But other properties should be equal
        Assert.Equal(task1.Description, task2.Description);
        Assert.Equal(task1.ExpectedOutput, task2.ExpectedOutput);
        Assert.Equal(task1.DifficultyLevel, task2.DifficultyLevel);
    }

    [Fact]
    public void ShouldBeModifiable_WhenUsingContext()
    {
        // Arrange
        var trainingTask = new TrainingTask("Description", "Output");

        // Act
        trainingTask.Context.Add("newKey", "newValue");
        trainingTask.Context["existingKey"] = "modifiedValue";

        // Assert
        Assert.Equal("newValue", trainingTask.Context["newKey"]);
        Assert.Equal("modifiedValue", trainingTask.Context["existingKey"]);
    }

    [Fact]
    public void ShouldExposeRequiredSkills_WhenProvidedAtConstruction()
    {
        // Arrange & Act
        var trainingTask = new TrainingTask(
            "Description",
            "Output",
            requiredSkills: ["New Skill", "Another Skill"]);

        // Assert
        Assert.Equal(2, trainingTask.RequiredSkills.Count);
        Assert.Contains("New Skill", trainingTask.RequiredSkills);
        Assert.Contains("Another Skill", trainingTask.RequiredSkills);
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenConstructingWithComplexContext()
    {
        // Arrange
        var complexContext = new Dictionary<string, object>
        {
            { "stringValue", "test" },
            { "intValue", 42 },
            { "boolValue", true },
            { "arrayValue", Int123 },
            { "objectValue", new { Name = "Test", Value = 123 } }
        };

        // Act
        var trainingTask = new TrainingTask(
            "Complex task",
            "Complex output",
            complexContext);

        // Assert
        Assert.Equal(5, trainingTask.Context.Count);
        Assert.Equal("test", trainingTask.Context["stringValue"]);
        Assert.Equal(42, trainingTask.Context["intValue"]);
        Assert.True((bool)trainingTask.Context["boolValue"]);
        Assert.IsType<int[]>(trainingTask.Context["arrayValue"]);
    }

    [Theory]
    [InlineData(0, 1, 5, 30)] // seconds, minutes, hours, days
    public void ShouldAcceptAll_WhenUsingExpectedDurationWithVariousTimeSpans(
        int seconds, int minutes, int hours, int days)
    {
        // Arrange
        var duration = new TimeSpan(days, hours, minutes, seconds);

        // Act
        var trainingTask = new TrainingTask(
            "Duration test",
            "Output",
            expectedDuration: duration);

        // Assert
        Assert.Equal(duration, trainingTask.ExpectedDuration);
    }
}
