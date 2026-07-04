using System.Collections.Immutable;
using Orkeon.Application.Task.DTOs;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Application.Tests.DTOs.Tasks;

public class TaskOutputDtoTests
{
    private static readonly string[] s_analysisTags = ["analysis", "report", "automated"];
    [Fact]
    public void ShouldCreateValidDto_WhenConstructingWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new TaskOutputDto
        {
            RawOutput = "Task completed successfully",
            Format = "text"
        };

        // Assert
        Assert.Equal("Task completed successfully", dto.RawOutput);
        Assert.Equal("text", dto.Format);
        Assert.Null(dto.FormattedOutput);
        Assert.Equal(0, dto.SizeBytes);
        Assert.True(dto.GeneratedAt <= DateTime.UtcNow);
        Assert.Equal("Valid", dto.ValidationStatus);
        Assert.True(dto.Metadata.IsEmpty);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenConstructingWithAllProperties()
    {
        // Arrange
        var metadata = ImmutableDictionary<string, object>.Empty
            .Add("source", "AI Agent")
            .Add("version", "1.0")
            .Add("confidence", 0.95);

        var generatedAt = DateTime.UtcNow.AddMinutes(-5);

        // Act
        var dto = new TaskOutputDto
        {
            RawOutput = "## Analysis Results\n\nThe analysis shows...",
            FormattedOutput = "<h2>Analysis Results</h2><p>The analysis shows...</p>",
            Format = "markdown",
            SizeBytes = 1024,
            GeneratedAt = generatedAt,
            ValidationStatus = "Validated",
            Metadata = metadata
        };

        // Assert
        Assert.Equal("## Analysis Results\n\nThe analysis shows...", dto.RawOutput);
        Assert.Equal("<h2>Analysis Results</h2><p>The analysis shows...</p>", dto.FormattedOutput);
        Assert.Equal("markdown", dto.Format);
        Assert.Equal(1024, dto.SizeBytes);
        Assert.Equal(generatedAt, dto.GeneratedAt);
        Assert.Equal("Validated", dto.ValidationStatus);
        Assert.Equal(3, dto.Metadata.Count);
        Assert.Equal("AI Agent", dto.Metadata["source"]);
        Assert.Equal("1.0", dto.Metadata["version"]);
        Assert.Equal(0.95, dto.Metadata["confidence"]);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("json")]
    [InlineData("xml")]
    [InlineData("markdown")]
    [InlineData("html")]
    [InlineData("csv")]
    public void ShouldBeSettable_WhenFormattingWithDifferentFormats(string format)
    {
        // Arrange & Act
        var dto = new TaskOutputDto
        {
            RawOutput = "Output content",
            Format = format
        };

        // Assert
        Assert.Equal(format, dto.Format);
    }

    [Fact]
    public void ShouldBeStoredCorrectly_WhenUsingRawOutputUsingLongText()
    {
        // Arrange
        var longOutput = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Line {i}: This is a long output text that contains multiple lines and lots of content."));

        // Act
        var dto = new TaskOutputDto
        {
            RawOutput = longOutput,
            Format = "text"
        };

        // Assert
        Assert.Equal(longOutput, dto.RawOutput);
        Assert.Contains("Line 1:", dto.RawOutput);
        Assert.Contains("Line 100:", dto.RawOutput);
    }

    [Fact]
    public void ShouldBeOptional_WhenUsingFormattedOutputWithFormatting()
    {
        // Arrange & Act
        var dtoWithFormatted = new TaskOutputDto
        {
            RawOutput = "**Bold Text**",
            FormattedOutput = "<strong>Bold Text</strong>",
            Format = "markdown"
        };

        var dtoWithoutFormatted = new TaskOutputDto
        {
            RawOutput = "Plain text",
            Format = "text"
        };

        // Assert
        Assert.NotNull(dtoWithFormatted.FormattedOutput);
        Assert.Equal("<strong>Bold Text</strong>", dtoWithFormatted.FormattedOutput);
        Assert.Null(dtoWithoutFormatted.FormattedOutput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(1024)]
    [InlineData(1048576)] // 1MB
    [InlineData(104857600)] // 100MB
    public void ShouldBeSettable_WhenUsingSizeBytesWithDifferentSizes(long sizeBytes)
    {
        // Arrange & Act
        var dto = new TaskOutputDto
        {
            RawOutput = "Output",
            Format = "text",
            SizeBytes = sizeBytes
        };

        // Assert
        Assert.Equal(sizeBytes, dto.SizeBytes);
    }

    [Fact]
    public void ShouldDefaultToCurrentTime_WhenUsingGeneratedAt()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var dto = new TaskOutputDto
        {
            RawOutput = "Output",
            Format = "text"
        };
        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.True(beforeCreation <= dto.GeneratedAt);
        Assert.True(dto.GeneratedAt <= afterCreation);
    }

    [Fact]
    public void ShouldCanBeSetToCustomTime_WhenUsingGeneratedAt()
    {
        // Arrange
        var customTime = DateTime.UtcNow.AddHours(-2);

        // Act
        var dto = new TaskOutputDto
        {
            RawOutput = "Output",
            Format = "text",
            GeneratedAt = customTime
        };

        // Assert
        Assert.Equal(customTime, dto.GeneratedAt);
    }

    [Theory]
    [InlineData("Valid")]
    [InlineData("Invalid")]
    [InlineData(Pending)]
    [InlineData("Validated")]
    [InlineData(Failed)]
    public void ShouldBeSettable_WhenUsingValidationStatusWithDifferentStatuses(string status)
    {
        // Arrange & Act
        var dto = new TaskOutputDto
        {
            RawOutput = "Output",
            Format = "text",
            ValidationStatus = status
        };

        // Assert
        Assert.Equal(status, dto.ValidationStatus);
    }

    [Fact]
    public void ShouldAcceptImmutableDictionary_WhenUsingMetadata()
    {
        // Arrange
        var metadata = ImmutableDictionary<string, object>.Empty
            .Add("taskId", "task-123")
            .Add("agentId", "agent-456")
            .Add("executionTime", 1500)
            .Add("retryCount", 0)
            .Add("tags", s_analysisTags);

        // Act
        var dto = new TaskOutputDto
        {
            RawOutput = "Task output",
            Format = "text",
            Metadata = metadata
        };

        // Assert
        Assert.Equal(5, dto.Metadata.Count);
        Assert.Equal("task-123", dto.Metadata["taskId"]);
        Assert.Equal("agent-456", dto.Metadata["agentId"]);
        Assert.Equal(1500, dto.Metadata["executionTime"]);
        Assert.Equal(0, dto.Metadata["retryCount"]);
        Assert.IsType<string[]>(dto.Metadata["tags"]);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var rawOutput = "Same output";
        var format = "json";
        var sizeBytes = 256L;
        var generatedAt = DateTime.UtcNow;
        var status = "Valid";
        var metadata = ImmutableDictionary<string, object>.Empty.Add("key", "value");

        // Act
        var dto1 = new TaskOutputDto
        {
            RawOutput = rawOutput,
            Format = format,
            SizeBytes = sizeBytes,
            GeneratedAt = generatedAt,
            ValidationStatus = status,
            Metadata = metadata
        };

        var dto2 = new TaskOutputDto
        {
            RawOutput = rawOutput,
            Format = format,
            SizeBytes = sizeBytes,
            GeneratedAt = generatedAt,
            ValidationStatus = status,
            Metadata = metadata
        };

        // Assert
        Assert.Equal(dto1, dto2);
        Assert.Equal(dto1.GetHashCode(), dto2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange & Act
        var dto1 = new TaskOutputDto
        {
            RawOutput = "Output 1",
            Format = "text"
        };

        var dto2 = new TaskOutputDto
        {
            RawOutput = "Output 2",
            Format = "json"
        };

        // Assert
        Assert.NotEqual(dto1, dto2);
    }

    [Fact]
    public void ShouldHandleComplexStructure_WhenUsingJsonOutput()
    {
        // Arrange
        var jsonOutput = @"{
            ""status"": ""success"",
            ""data"": {
                ""items"": [1, 2, 3],
                ""metadata"": {
                    ""count"": 3,
                    ""timestamp"": ""2024-01-01T00:00:00Z""
                }
            }
        }";

        // Act
        var dto = new TaskOutputDto
        {
            RawOutput = jsonOutput,
            Format = "json",
            SizeBytes = System.Text.Encoding.UTF8.GetByteCount(jsonOutput),
            ValidationStatus = "Valid"
        };

        // Assert
        Assert.Equal(jsonOutput, dto.RawOutput);
        Assert.Equal("json", dto.Format);
        Assert.True(dto.SizeBytes > 0);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingCompleteTaskOutputWithAllData()
    {
        // Arrange & Act
        var dto = new TaskOutputDto
        {
            RawOutput = "# Report\n\n## Summary\n\nTask completed with the following results:\n- Item 1\n- Item 2\n- Item 3",
            FormattedOutput = "<h1>Report</h1><h2>Summary</h2><p>Task completed with the following results:</p><ul><li>Item 1</li><li>Item 2</li><li>Item 3</li></ul>",
            Format = "markdown",
            SizeBytes = 2048,
            GeneratedAt = DateTime.UtcNow.AddMinutes(-10),
            ValidationStatus = "Validated",
            Metadata = ImmutableDictionary<string, object>.Empty
                .Add("generator", "GPT-4")
                .Add("temperature", 0.7)
                .Add("maxTokens", 1000)
                .Add("processingTime", 1250)
        };

        // Assert
        Assert.NotNull(dto.RawOutput);
        Assert.NotNull(dto.FormattedOutput);
        Assert.Equal("markdown", dto.Format);
        Assert.Equal(2048, dto.SizeBytes);
        Assert.Equal("Validated", dto.ValidationStatus);
        Assert.Equal(4, dto.Metadata.Count);
        Assert.Equal("GPT-4", dto.Metadata["generator"]);
        Assert.Equal(0.7, dto.Metadata["temperature"]);
        Assert.Equal(1000, dto.Metadata["maxTokens"]);
        Assert.Equal(1250, dto.Metadata["processingTime"]);
    }

    [Fact]
    public void ShouldBeEmpty_WhenUsingEmptyMetadata()
    {
        // Arrange & Act
        var dto = new TaskOutputDto
        {
            RawOutput = "Output",
            Format = "text"
        };

        // Assert
        Assert.NotNull(dto.Metadata);
        Assert.Empty(dto.Metadata);
        Assert.True(dto.Metadata.IsEmpty);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingDefaultValidationStatus()
    {
        // Arrange & Act
        var dto = new TaskOutputDto
        {
            RawOutput = "Output",
            Format = "text"
        };

        // Assert
        Assert.Equal("Valid", dto.ValidationStatus);
    }

    [Fact]
    public void ShouldBeText_WhenUsingDefaultFormat()
    {
        // Arrange & Act
        var dto = new TaskOutputDto
        {
            RawOutput = "Output",
            Format = "text"
        };

        // Assert
        Assert.Equal("text", dto.Format);
    }
}
