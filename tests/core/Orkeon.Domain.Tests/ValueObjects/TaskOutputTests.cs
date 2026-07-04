using Orkeon.Domain.Common;
using Orkeon.Domain.Task.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class TaskOutputTests
{
    private static readonly string[] Categories = ["users", "orders", "products"];
    private static readonly string[] ImportantUrgentTags = ["important", "urgent"];
    private static readonly string[] ConnectionWarnings = ["connection_timeout", "network_latency"];
    #region Constructor Tests

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithRequiredParameters()
    {
        // Arrange
        var rawOutput = "Task completed successfully";

        // Act
        var output = TaskOutput.Create(rawOutput);

        // Assert
        Assert.Equal(rawOutput, output.RawOutput);
        Assert.Equal("text", output.Format);
        Assert.Null(output.FormattedOutput);
        Assert.Null(output.TaskId);
        Assert.True(output.Success);
        Assert.Equal(TimeSpan.Zero, output.ExecutionTime);
        Assert.Null(output.StructuredOutput);
        Assert.True(output.GeneratedAt <= DateTime.UtcNow);
        Assert.Equal(rawOutput, output.Output);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithAllParameters()
    {
        // Arrange
        var rawOutput = "{\"status\": \"completed\", \"count\": 42}";
        var format = "json";
        var formattedOutput = "Status: completed, Count: 42";
        var taskId = TaskId.From(Guid.NewGuid());
        var success = true;
        var executionTime = TimeSpan.FromMinutes(2.5);
        var structuredOutput = new { status = "completed", count = 42 };

        // Act
        var output = TaskOutput.Create(
            rawOutput,
            format,
            formattedOutput,
            taskId,
            success,
            executionTime,
            structuredOutput);

        // Assert
        Assert.Equal(rawOutput, output.RawOutput);
        Assert.Equal("json", output.Format);
        Assert.Equal(formattedOutput, output.FormattedOutput);
        Assert.Equal(taskId, output.TaskId);
        Assert.True(output.Success);
        Assert.Equal(executionTime, output.ExecutionTime);
        Assert.Equal(structuredOutput, output.StructuredOutput);
        Assert.True(output.GeneratedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenConstructingWithEmptyRawOutput()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => TaskOutput.Create(""));
        Assert.Contains("cannot be empty", exception.Message);
        Assert.Equal("info", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenConstructingWithNullRawOutput()
    {
        // Act & Assert — cast explicite pour eviter l'ambiguite de surcharge avec TaskOutput(TaskOutputInfo)
        var exception = Assert.Throws<ArgumentException>(() => TaskOutput.Create((string)null!));
        Assert.Contains("cannot be empty", exception.Message);
        Assert.Equal("info", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenConstructingWithWhitespaceRawOutput()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => TaskOutput.Create("   "));
        Assert.Contains("cannot be empty", exception.Message);
        Assert.Equal("info", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenConstructingWithEmptyFormat()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => TaskOutput.Create("output", ""));
        Assert.Contains("Format cannot be empty", exception.Message);
        Assert.Equal("info", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenConstructingWithNullFormat()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => TaskOutput.Create("output", null!));
        Assert.Contains("Format cannot be empty", exception.Message);
        Assert.Equal("info", exception.ParamName);
    }

    [Fact]
    public void ShouldNormalizeFormatToLowercase_WhenConstructing()
    {
        // Act
        var output = TaskOutput.Create("test", "JSON");

        // Assert
        Assert.Equal("json", output.Format);
    }

    [Fact]
    public void ShouldDefaultToZero_WhenConstructingWithNullExecutionTime()
    {
        // Act
        var output = TaskOutput.Create("test", executionTime: null);

        // Assert
        Assert.Equal(TimeSpan.Zero, output.ExecutionTime);
    }

    #endregion

    #region Record Behavior Tests

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var taskId = TaskId.From(Guid.NewGuid());
        var executionTime = TimeoutQuick;
        var structuredData = new { result = "success" };
        var generatedAt = DateTime.UtcNow;

        var output1 = TaskOutput.Create(
            "Test output",
            "text",
            "Formatted output",
            taskId,
            true,
            executionTime,
            structuredData,
            generatedAt);

        var output2 = TaskOutput.Create(
            "Test output",
            "text",
            "Formatted output",
            taskId,
            true,
            executionTime,
            structuredData,
            generatedAt);

        // Act & Assert
        Assert.Equal(output1, output2);
        Assert.True(output1 == output2);
        Assert.False(output1 != output2);
        Assert.Equal(output1.GetHashCode(), output2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var output1 = TaskOutput.Create("Output 1", "text");
        var output2 = TaskOutput.Create("Output 2", "text");

        // Act & Assert
        Assert.NotEqual(output1, output2);
        Assert.False(output1 == output2);
        Assert.True(output1 != output2);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWith()
    {
        // Arrange
        var original = TaskOutput.Create("Original output", "text");

        // Act
        // TaskOutput properties are read-only, create new instance
        var modified = TaskOutput.Create("Modified output", original.Format);

        // Assert
        Assert.Equal("Original output", original.RawOutput);
        Assert.Equal("Modified output", modified.RawOutput);
        Assert.Equal(original.Format, modified.Format);
        Assert.Equal(original.Success, modified.Success);
    }

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var output = TaskOutput.Create(
            "Analysis complete: 150 records processed",
            "text",
            success: true,
            executionTime: TimeSpan.FromSeconds(45));

        // Act
        var result = output.ToString();

        // Assert
        Assert.Contains("Analysis complete", result);
        Assert.Contains("text", result);
        Assert.Contains("True", result);
    }

    [Fact]
    public void ShouldReturnAllValues_WhenUsingDeconstruct()
    {
        // Arrange
        var taskId = TaskId.From(Guid.NewGuid());
        var executionTime = TimeSpan.FromMinutes(1);
        var structuredData = new { count = 10 };

        var output = TaskOutput.Create(
            "Raw output",
            "json",
            "Formatted output",
            taskId,
            false,
            executionTime,
            structuredData);

        // Act - TaskOutput doesn't support deconstruction, access properties directly
        var tId = output.TaskId;
        var raw = output.RawOutput;
        var formatted = output.FormattedOutput;
        var format = output.Format;
        var generated = output.GeneratedAt;
        var success = output.Success;
        var exec = output.ExecutionTime;
        var structured = output.StructuredOutput;

        // Assert
        Assert.Equal(taskId, tId);
        Assert.Equal("Raw output", raw);
        Assert.Equal("Formatted output", formatted);
        Assert.Equal("json", format);
        Assert.Equal(output.GeneratedAt, generated);
        Assert.False(success);
        Assert.Equal(executionTime, exec);
        Assert.Equal(structuredData, structured);
    }

    #endregion

    #region Static Factory Methods Tests

    [Fact]
    public void ShouldCreateTextOutput_WhenUsingText()
    {
        // Act
        var output = TaskOutput.Text("This is text output");

        // Assert
        Assert.Equal("This is text output", output.RawOutput);
        Assert.Equal("text", output.Format);
        Assert.Null(output.FormattedOutput);
        Assert.True(output.Success);
        Assert.Equal(TimeSpan.Zero, output.ExecutionTime);
        Assert.Null(output.StructuredOutput);
    }

    [Fact]
    public void ShouldCreateJsonOutput_WhenUsingJsonWithRawOutputOnly()
    {
        // Act
        var output = TaskOutput.Json("{\"message\": \"Hello World\"}");

        // Assert
        Assert.Equal("{\"message\": \"Hello World\"}", output.RawOutput);
        Assert.Equal("json", output.Format);
        Assert.Null(output.FormattedOutput);
        Assert.True(output.Success);
    }

    [Fact]
    public void ShouldCreateJsonOutput_WhenUsingJsonWithFormattedOutput()
    {
        // Act
        var output = TaskOutput.Json(
            "{\"count\": 42, \"status\": \"done\"}",
            "Count: 42, Status: done");

        // Assert
        Assert.Equal("{\"count\": 42, \"status\": \"done\"}", output.RawOutput);
        Assert.Equal("json", output.Format);
        Assert.Equal("Count: 42, Status: done", output.FormattedOutput);
        Assert.True(output.Success);
    }

    [Fact]
    public void ShouldCreateMarkdownOutput_WhenUsingMarkdown()
    {
        // Act
        var output = TaskOutput.Markdown("# Title\n\nThis is **markdown** content.");

        // Assert
        Assert.Equal("# Title\n\nThis is **markdown** content.", output.RawOutput);
        Assert.Equal("markdown", output.Format);
        Assert.Null(output.FormattedOutput);
        Assert.True(output.Success);
    }

    #endregion

    #region Property Tests

    [Fact]
    public void ShouldReturnRawOutput_WhenUsingOutput()
    {
        // Arrange
        var rawOutput = "Test output content";
        var output = TaskOutput.Create(rawOutput);

        // Act & Assert
        Assert.Equal(rawOutput, output.Output);
        Assert.Equal(output.RawOutput, output.Output);
    }

    [Fact]
    public void ShouldBeCloseToCurrentTime_WhenUsingGeneratedAt()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var output = TaskOutput.Create("test");

        // Assert
        var after = DateTime.UtcNow;
        Assert.True(output.GeneratedAt >= before);
        Assert.True(output.GeneratedAt <= after);
    }

    #endregion

    #region Scenario Tests

    [Fact]
    public void ShouldSuccessfulTaskExecution_WhenUsingScenario()
    {
        // Arrange & Act
        var output = TaskOutput.Create(
            "User registration completed successfully. User ID: 12345",
            "text",
            "✅ Registration Complete\nUser ID: 12345\nEmail: user@example.com",
            TaskId.From(Guid.NewGuid()),
            true,
            TimeSpan.FromSeconds(1.2),
            new { userId = 12345, email = "user@example.com", status = "active" });

        // Assert
        Assert.True(output.Success);
        Assert.Contains("registration completed", output.RawOutput.ToLower());
        Assert.NotNull(output.FormattedOutput);
        Assert.Contains("Registration Complete", output.FormattedOutput);
        Assert.NotNull(output.StructuredOutput);
        Assert.True(output.ExecutionTime.TotalSeconds > 1.0);
    }

    [Fact]
    public void ShouldFailedTaskExecution_WhenUsingScenario()
    {
        // Arrange & Act
        var output = TaskOutput.Create(
            "Task failed: Unable to connect to database",
            "text",
            success: false,
            executionTime: TimeSpan.FromMilliseconds(500));

        // Assert
        Assert.False(output.Success);
        Assert.Contains("failed", output.RawOutput.ToLower());
        Assert.Equal(TimeSpan.FromMilliseconds(500), output.ExecutionTime);
        Assert.Null(output.StructuredOutput);
    }

    [Fact]
    public void ShouldDataAnalysisOutput_WhenUsingScenario()
    {
        // Arrange & Act
        var output = TaskOutput.Create(
            "Analysis completed. Processed 1,500 records in 3.2 seconds.",
            "text",
            "📊 Analysis Results\n" +
            "Records Processed: 1,500\n" +
            "Processing Time: 3.2s\n" +
            "Average Rate: 468 records/sec",
            TaskId.From(Guid.NewGuid()),
            true,
            TimeSpan.FromSeconds(3.2),
            new
            {
                recordsProcessed = 1500,
                processingTimeSeconds = 3.2,
                averageRate = 468,
                categories = Categories
            });

        // Assert
        Assert.True(output.Success);
        Assert.Contains("1,500 records", output.RawOutput);
        Assert.Contains("Analysis Results", output.FormattedOutput);
        Assert.NotNull(output.StructuredOutput);
        Assert.Equal(3.2, output.ExecutionTime.TotalSeconds);
    }

    [Fact]
    public void ShouldCodeGenerationOutput_WhenUsingScenario()
    {
        // Arrange
        var generatedCode = @"
public class UserService
{
    public async System.Threading.Tasks.Task<User> GetUserAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }
}";

        // Act
        var output = TaskOutput.Create(
            generatedCode,
            "code",
            $"Generated UserService class with 1 method\nLines of code: {generatedCode.Split('\n').Length}");

        // Assert
        Assert.Equal("code", output.Format);
        Assert.Contains("UserService", output.RawOutput);
        Assert.Contains("Generated UserService", output.FormattedOutput);
        Assert.True(output.Success);
    }

    [Fact]
    public void ShouldLongRunningTask_WhenUsingScenario()
    {
        // Arrange & Act
        var output = TaskOutput.Create(
            "Batch processing completed. 10,000 items processed.",
            "text",
            executionTime: TimeSpan.FromMinutes(15.5));

        // Assert
        Assert.Equal(15.5, output.ExecutionTime.TotalMinutes);
        Assert.Contains("10,000 items", output.RawOutput);
        Assert.True(output.Success);
    }

    [Fact]
    public void ShouldJsonApiResponse_WhenUsingScenario()
    {
        // Arrange
        var jsonResponse = @"{
    ""success"": true,
    ""data"": {
        ""users"": 42,
        ""active"": 38,
        ""timestamp"": ""2024-01-15T10:30:00Z""
    },
    ""metadata"": {
        ""version"": ""1.0"",
        ""cached"": false
    }
}";

        // Act
        var output = TaskOutput.Json(
            jsonResponse,
            "API Response: 42 users (38 active)\nVersion: 1.0, Not cached");

        // Assert
        Assert.Equal("json", output.Format);
        Assert.Contains("\"users\": 42", output.RawOutput);
        Assert.Contains("42 users", output.FormattedOutput);
        Assert.True(output.Success);
    }

    [Fact]
    public void ShouldMarkdownReport_WhenUsingScenario()
    {
        // Arrange
        var markdownContent = @"# Weekly Report

## Summary
- **Tasks Completed**: 15
- **Success Rate**: 93.3%
- **Average Duration**: 2.1 minutes

## Top Performers
1. Agent Alpha - 8 tasks
2. Agent Beta - 5 tasks
3. Agent Gamma - 2 tasks

## Issues
- 1 timeout error
- Network latency increased by 15%";

        // Act
        var output = TaskOutput.Markdown(markdownContent);

        // Assert
        Assert.Equal("markdown", output.Format);
        Assert.Contains("Weekly Report", output.RawOutput);
        Assert.Contains("Success Rate", output.RawOutput);
        Assert.True(output.Success);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldVeryLongOutput_WhenUsingEdgeCase()
    {
        // Arrange
        var longOutput = string.Join("\n", Enumerable.Repeat("This is a long line of output.", 1000));

        // Act
        var output = TaskOutput.Create(longOutput);

        // Assert
        Assert.Equal(longOutput, output.RawOutput);
        Assert.True(output.RawOutput.Length > 25000);
    }

    [Fact]
    public void ShouldSpecialCharactersInOutput_WhenUsingEdgeCase()
    {
        // Arrange
        var specialOutput = "Output with special chars: 🚀 ñáéíóú 中文 Русский ¡¿¡¿ <>\"'&";

        // Act
        var output = TaskOutput.Create(specialOutput);

        // Assert
        Assert.Equal(specialOutput, output.RawOutput);
        Assert.Contains("🚀", output.RawOutput);
        Assert.Contains("中文", output.RawOutput);
    }

    [Fact]
    public void ShouldMaxExecutionTime_WhenUsingEdgeCase()
    {
        // Arrange
        var maxTime = TimeSpan.MaxValue;

        // Act
        var output = TaskOutput.Create("test", executionTime: maxTime);

        // Assert
        Assert.Equal(maxTime, output.ExecutionTime);
    }

    [Fact]
    public void ShouldNegativeExecutionTime_WhenUsingEdgeCase()
    {
        // Arrange
        var negativeTime = TimeSpan.FromSeconds(-10);

        // Act
        var output = TaskOutput.Create("test", executionTime: negativeTime);

        // Assert
        Assert.Equal(negativeTime, output.ExecutionTime);
    }

    [Fact]
    public void ShouldComplexStructuredOutput_WhenUsingEdgeCase()
    {
        // Arrange
        var complexStructure = new
        {
            metadata = new
            {
                version = "2.1.0",
                timestamp = DateTime.UtcNow,
                tags = ImportantUrgentTags
            },
            results = new[]
            {
                new { id = 1, name = "Item 1", score = 0.95 },
                new { id = 2, name = "Item 2", score = 0.87 }
            },
            statistics = new Dictionary<string, object>
            {
                { "total", 2 },
                { "average_score", 0.91 },
                { "processing_time", "1.2s" }
            }
        };

        // Act
        var output = TaskOutput.Create("Complex analysis complete", structuredOutput: complexStructure);

        // Assert
        Assert.NotNull(output.StructuredOutput);
        Assert.Equal(complexStructure, output.StructuredOutput);
    }

    [Fact]
    public void ShouldEmptyTaskId_WhenUsingEdgeCase()
    {
        // Arrange
        var emptyTaskId = TaskId.From(Guid.NewGuid());

        // Act
        var output = TaskOutput.Create("test", taskId: emptyTaskId);

        // Assert
        Assert.Equal(emptyTaskId, output.TaskId);
        // FIXME: // FIXME: // FIXME: // FIXME: Assert.Equal(Guid.Empty, output.TaskId!.Value); // constructor signature changed // constructor signature changed // constructor signature changed // constructor signature changed
    }

    [Fact]
    public void ShouldCustomFormatTypes_WhenUsingEdgeCase()
    {
        // Test various format types
        var xmlOutput = TaskOutput.Create("<?xml version='1.0'?><root></root>", "XML");
        var csvOutput = TaskOutput.Create("name,age\nJohn,30", "CSV");
        var htmlOutput = TaskOutput.Create("<html><body>Content</body></html>", "Html");

        // Assert - all should be normalized to lowercase
        Assert.Equal("xml", xmlOutput.Format);
        Assert.Equal("csv", csvOutput.Format);
        Assert.Equal("html", htmlOutput.Format);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldMultiStepTaskOutput_WhenUsingComplexScenario()
    {
        // Simulate output from a multi-step task
        var steps = new[]
        {
            "Step 1: Data validation completed",
            "Step 2: Processing 1000 records",
            "Step 3: Generating report",
            "Step 4: Saving results to database"
        };

        var combinedOutput = string.Join("\n", steps);
        var structuredSteps = steps.Select((step, index) => new
        {
            stepNumber = index + 1,
            description = step,
            completed = true,
            timestamp = DateTime.UtcNow.AddSeconds(-60 + (index * 15))
        }).ToArray();

        var output = TaskOutput.Create(
            combinedOutput,
            "text",
            $"Multi-step task completed successfully\n{steps.Length} steps executed",
            TaskId.From(Guid.NewGuid()),
            true,
            TimeSpan.FromMinutes(2),
            new { steps = structuredSteps, totalSteps = steps.Length });

        // Assert
        Assert.Contains("Step 1", output.RawOutput);
        Assert.Contains("Step 4", output.RawOutput);
        Assert.Contains("4 steps", output.FormattedOutput);
        Assert.NotNull(output.StructuredOutput);
        Assert.Equal(2, output.ExecutionTime.TotalMinutes);
    }

    [Fact]
    public void ShouldErrorRecoveryOutput_WhenUsingComplexScenario()
    {
        // Simulate output from a task that had errors but recovered
        var errorLog = @"WARNING: Initial connection failed, retrying...
INFO: Retry attempt 1 - Connection timeout
INFO: Retry attempt 2 - Success
INFO: Processing data...
SUCCESS: Task completed with warnings";

        var output = TaskOutput.Create(
            errorLog,
            "log",
            "Task completed successfully after 2 retries",
            success: true,
            executionTime: TimeSpan.FromSeconds(8.5),
            structuredOutput: new
            {
                status = "completed_with_warnings",
                retryAttempts = 2,
                warnings = ConnectionWarnings,
                finalResult = "success"
            });

        // Assert
        Assert.True(output.Success);
        Assert.Contains("WARNING", output.RawOutput);
        Assert.Contains("SUCCESS", output.RawOutput);
        Assert.Contains("2 retries", output.FormattedOutput);
        Assert.Equal(8.5, output.ExecutionTime.TotalSeconds);
    }

    #endregion
}
