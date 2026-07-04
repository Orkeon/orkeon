using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Memory.ValueObjects;
using Orkeon.Application.Common.Mapping;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Application.Tests.DTOs.Mapping;

public class ToolMapperTests
{
    #region Test Doubles

    private class TestTool : IBaseTool
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ToolSchema Schema { get; set; } = new ToolSchema("DefaultTool", "Default tool description", []);

        public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(new ToolCallResponse(Success: true, Result: "Test result", Error: null));
        }

        public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess("Test result"));
        }

        public bool ValidateInput(string input)
        {
            return true;
        }
    }

    #endregion

    [Fact]
    public void ShouldMapAllProperties_WhenUsingToDtoWithValidTool()
    {
        // Arrange
        var tool = new TestTool
        {
            Name = "FileReadTool",
            Description = "Reads content from files"
        };

        // Act
        var dto = ToolMapper.ToDto(tool);

        // Assert
        Assert.NotNull(dto);
        Assert.NotNull(dto.Id);
        Assert.Equal("FileReadTool", dto.Name);
        Assert.Equal("Reads content from files", dto.Description);
        Assert.Equal("file", dto.Category);
        Assert.Equal("1.0.0", dto.Version);
        Assert.NotNull(dto.InputSchema);
        Assert.NotNull(dto.OutputSchema);
        Assert.NotNull(dto.Configuration);
        Assert.True(dto.Enabled);
        Assert.False(dto.Deprecated);
        Assert.NotNull(dto.Metrics);
        Assert.NotNull(dto.Capabilities);
        Assert.NotNull(dto.RequiredPermissions);
        Assert.NotNull(dto.RateLimit);
        Assert.NotNull(dto.Security);
        Assert.NotNull(dto.Metadata);
        Assert.True(dto.CreatedAt <= DateTime.UtcNow);
        Assert.True(dto.UpdatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToDtoWithNullTool()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => ToolMapper.ToDto((IBaseTool)null!));
        Assert.Equal("tool", exception.ParamName);
    }

    [Theory]
    [InlineData("FileReadTool", "file")]
    [InlineData("WebScrapeTool", "web")]
    [InlineData("HttpRequestTool", "web")]
    [InlineData("EmailSenderTool", "communication")]
    [InlineData("SlackMessageTool", "communication")]
    [InlineData(ToolSearch, "search")]
    [InlineData("GitHubTool", "development")]
    [InlineData("JsonParserTool", "data")]
    [InlineData("CsvReaderTool", "data")]
    [InlineData("SqlQueryTool", "database")]
    [InlineData("ImageProcessorTool", "document")]
    [InlineData("PdfReaderTool", "document")]
    [InlineData("RandomTool", "utility")]
    public void ShouldInferCorrectCategory_WhenUsingToDto(string toolName, string expectedCategory)
    {
        // Arrange
        var tool = new TestTool
        {
            Name = toolName,
            Description = "Test tool"
        };

        // Act
        var dto = ToolMapper.ToDto(tool);

        // Assert
        Assert.Equal(expectedCategory, dto.Category);
    }

    [Fact]
    public void ShouldHaveFilePermissions_WhenUsingToDtoWithFileReadTool()
    {
        // Arrange
        var tool = new TestTool
        {
            Name = "FileReadTool",
            Description = "Reads files"
        };

        // Act
        var dto = ToolMapper.ToDto(tool);

        // Assert
        Assert.Contains("file_read", dto.RequiredPermissions);
        Assert.Contains("file_write", dto.RequiredPermissions);
        Assert.Contains("file_system", dto.Capabilities);
        Assert.Contains("read", dto.Capabilities);
    }

    [Fact]
    public void ShouldHaveNetworkPermissions_WhenUsingToDtoWithWebTool()
    {
        // Arrange
        var tool = new TestTool
        {
            Name = "WebScrapeTool",
            Description = "Scrapes web content"
        };

        // Act
        var dto = ToolMapper.ToDto(tool);

        // Assert
        Assert.Contains("network_access", dto.RequiredPermissions);
        Assert.Contains("web_access", dto.Capabilities);
        Assert.True(dto.Security!.AllowExternalAccess);
    }

    [Fact]
    public void ShouldHaveHighRiskSecurity_WhenUsingToDtoWithHighRiskTool()
    {
        // Arrange
        var tool = new TestTool
        {
            Name = "CommandExecutorTool",
            Description = "Executes system commands"
        };

        // Act
        var dto = ToolMapper.ToDto(tool);

        // Assert
        Assert.Equal("high", dto.Security!.RiskLevel);
        Assert.True(dto.Security.RequiresAuthorization);
    }

    [Fact]
    public void ShouldCreateValidInputSchema_WhenUsingToDto()
    {
        // Arrange
        var tool = new TestTool
        {
            Name = "TestTool",
            Description = "Test description"
        };

        // Act
        var dto = ToolMapper.ToDto(tool);

        // Assert
        Assert.NotNull(dto.InputSchema);
        Assert.Equal("object", dto.InputSchema.Type);
        Assert.Equal("TestTool Input", dto.InputSchema.Title);
        Assert.Contains("input", dto.InputSchema.Properties.Keys);
        Assert.Contains("input", dto.InputSchema.Required);
    }

    [Fact]
    public void ShouldCreateValidOutputSchema_WhenUsingToDto()
    {
        // Arrange
        var tool = new TestTool
        {
            Name = "TestTool",
            Description = "Test description"
        };

        // Act
        var dto = ToolMapper.ToDto(tool);

        // Assert
        Assert.NotNull(dto.OutputSchema);
        Assert.Equal("object", dto.OutputSchema.Type);
        Assert.Equal("Tool Output", dto.OutputSchema.Title);
        Assert.Contains("result", dto.OutputSchema.Properties.Keys);
        Assert.Contains("success", dto.OutputSchema.Properties.Keys);
        Assert.Contains("metadata", dto.OutputSchema.Properties.Keys);
        Assert.Contains("result", dto.OutputSchema.Required);
        Assert.Contains("success", dto.OutputSchema.Required);
    }

    [Fact]
    public void ShouldIncludeFilePathProperty_WhenUsingToDtoWithFilePathTool()
    {
        // Arrange
        var tool = new TestTool
        {
            Name = "FileReaderTool",
            Description = "Reads files"
        };

        // Act
        var dto = ToolMapper.ToDto(tool);

        // Assert
        Assert.Contains("file_path", dto.InputSchema!.Properties.Keys);
        var filePathProperty = dto.InputSchema.Properties["file_path"];
        Assert.Equal("string", filePathProperty.Type);
        Assert.Equal("Path to the file", filePathProperty.Description);
        Assert.True(filePathProperty.Required);
    }

    [Fact]
    public void ShouldIncludeUrlProperty_WhenUsingToDtoWithWebTool()
    {
        // Arrange
        var tool = new TestTool
        {
            Name = "WebScrapeTool",
            Description = "Scrapes web content"
        };

        // Act
        var dto = ToolMapper.ToDto(tool);

        // Assert
        Assert.Contains(ParamUrl, dto.InputSchema!.Properties.Keys);
        var urlProperty = dto.InputSchema.Properties[ParamUrl];
        Assert.Equal("string", urlProperty.Type);
        Assert.Equal("URL to access", urlProperty.Description);
        Assert.True(urlProperty.Required);
        Assert.Equal(@"^https?://.+", urlProperty.Pattern);
    }

    [Fact]
    public void ShouldIncludeQueryProperty_WhenUsingToDtoWithSearchTool()
    {
        // Arrange
        var tool = new TestTool
        {
            Name = ToolSearch,
            Description = "Searches for content"
        };

        // Act
        var dto = ToolMapper.ToDto(tool);

        // Assert
        Assert.Contains(ParamQuery, dto.InputSchema!.Properties.Keys);
        var queryProperty = dto.InputSchema.Properties[ParamQuery];
        Assert.Equal("string", queryProperty.Type);
        Assert.Equal("Search query", queryProperty.Description);
        Assert.True(queryProperty.Required);
        Assert.Equal(1, queryProperty.MinLength);
        Assert.Equal(1000, queryProperty.MaxLength);
    }

    [Fact]
    public void ShouldCreateDefaultRateLimit_WhenUsingToDto()
    {
        // Arrange
        var tool = new TestTool { Name = "TestTool", Description = "Test" };

        // Act
        var dto = ToolMapper.ToDto(tool);

        // Assert
        Assert.NotNull(dto.RateLimit);
        Assert.Equal(60, dto.RateLimit.CallsPerMinute);
        Assert.Equal(1000, dto.RateLimit.CallsPerHour);
        Assert.Equal(10000, dto.RateLimit.CallsPerDay);
        Assert.Equal(5, dto.RateLimit.MaxConcurrentCalls);
        Assert.Equal("sliding_window", dto.RateLimit.ResetStrategy);
    }

    [Fact]
    public void ShouldCreateDefaultMetrics_WhenUsingToDto()
    {
        // Arrange
        var tool = new TestTool { Name = "TestTool", Description = "Test" };

        // Act
        var dto = ToolMapper.ToDto(tool);

        // Assert
        Assert.NotNull(dto.Metrics);
        Assert.Equal(0, dto.Metrics.TotalCalls);
        Assert.Equal(0, dto.Metrics.SuccessfulCalls);
        Assert.Equal(0, dto.Metrics.FailedCalls);
        Assert.Equal(0, dto.Metrics.AverageExecutionTime);
        Assert.Equal(0, dto.Metrics.SuccessRate);
        Assert.Null(dto.Metrics.LastSuccessfulCall);
        Assert.Null(dto.Metrics.LastFailedCall);
        Assert.Empty(dto.Metrics.ErrorDistribution);
    }

    [Fact]
    public void ShouldMapAllTools_WhenUsingToDtoWithMultipleTools()
    {
        // Arrange
        var tools = new List<IBaseTool>
        {
            new TestTool { Name = "Tool1", Description = "First tool" },
            new TestTool { Name = "Tool2", Description = "Second tool" },
            new TestTool { Name = "Tool3", Description = "Third tool" }
        };

        // Act
        var dtos = ToolMapper.ToDto(tools);

        // Assert
        Assert.Equal(3, dtos.Count);
        Assert.Equal("Tool1", dtos[0].Name);
        Assert.Equal("Tool2", dtos[1].Name);
        Assert.Equal("Tool3", dtos[2].Name);
    }

    [Fact]
    public void ShouldReturnEmptyList_WhenUsingToDtoWithNullToolList()
    {
        // Act
        var dtos = ToolMapper.ToDto((IEnumerable<IBaseTool>)null!);

        // Assert
        Assert.NotNull(dtos);
        Assert.Empty(dtos);
    }

    [Fact]
    public void ShouldMapSummaryProperties_WhenUsingToSummaryDtoWithValidTool()
    {
        // Arrange
        var tool = new TestTool
        {
            Name = "SummaryTool",
            Description = "Tool for summary view"
        };

        // Act
        var summaryDto = ToolMapper.ToSummaryDto(tool);

        // Assert
        Assert.NotNull(summaryDto);
        Assert.Equal("SummaryTool", summaryDto.Name);
        Assert.Equal("Tool for summary view", summaryDto.Description);
        Assert.Equal("utility", summaryDto.Category);
        Assert.Equal("1.0.0", summaryDto.Version);
        Assert.True(summaryDto.Enabled);
        Assert.False(summaryDto.Deprecated);

        // Summary should not include detailed properties
        Assert.Null(summaryDto.InputSchema);
        Assert.Null(summaryDto.OutputSchema);
        Assert.Null(summaryDto.Configuration);
        Assert.Null(summaryDto.Metrics);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToSummaryDtoWithNullTool()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => ToolMapper.ToSummaryDto(null!));
        Assert.Equal("tool", exception.ParamName);
    }

    [Fact]
    public void ShouldMapAllProperties_WhenUsingToToolUsageDtoWithValidToolUsage()
    {
        // Arrange
        var metadata = ToolUsageMetadata.CreateBuilder()
            .Add("ToolId", "tool-123")
            .Add("ToolName", "TestTool")
            .Add(Success, true)
            .Add("Duration", TimeSpan.FromSeconds(2.5))
            .Add("param", "value")
            .Add("UsedAt", DateTime.UtcNow.AddMinutes(-5))
            .Build();

        var toolUsage = new Orkeon.Domain.Tools.ToolUsage(
            new Orkeon.Domain.Tools.ToolCallIdentity("tool-123", "TestTool", AgentId1, "task-456"),
            duration: TimeSpan.FromSeconds(2.5),
            success: true,
            error: null,
            metadata: metadata);

        // Act
        var dto = ToolMapper.ToToolUsageDto(toolUsage);

        // Assert
        Assert.NotNull(dto);
        Assert.Equal("tool-123", dto.ToolId);
        Assert.Equal("TestTool", dto.ToolName);
        Assert.Equal(Success, dto.Output);
        Assert.True(dto.Success);
        Assert.Equal(TimeSpan.FromSeconds(2.5), dto.ExecutionTime);
        Assert.Null(dto.Error);
        Assert.NotNull(dto.Input);
        Assert.Contains("param", dto.Input.Keys);
        Assert.Equal("value", dto.Input["param"]);
        Assert.True(dto.CalledAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldMapErrorCorrectly_WhenUsingToToolUsageDtoWithFailedToolUsage()
    {
        // Arrange
        var metadata = ToolUsageMetadata.CreateBuilder()
            .Add("ToolId", "tool-456")
            .Add("ToolName", "FailingTool")
            .Add(Success, false)
            .Add("Duration", TimeSpan.FromSeconds(1))
            .Add("Error", "Connection timeout")
            .Add("UsedAt", DateTime.UtcNow)
            .Build();

        var toolUsage = new Orkeon.Domain.Tools.ToolUsage(
            new Orkeon.Domain.Tools.ToolCallIdentity("tool-456", "FailingTool", AgentId1, "task-789"),
            duration: TimeSpan.FromSeconds(1),
            success: false,
            error: "Connection timeout",
            metadata: metadata);

        // Act
        var dto = ToolMapper.ToToolUsageDto(toolUsage);

        // Assert
        Assert.NotNull(dto);
        Assert.Equal(Failed, dto.Output);
        Assert.False(dto.Success);
        Assert.Equal("Connection timeout", dto.Error);
    }

    [Fact]
    public void ShouldUseDefaultError_WhenUsingToToolUsageDtoWithFailedToolUsageButNoError()
    {
        // Arrange
        var metadata = ToolUsageMetadata.CreateBuilder()
            .Add("ToolId", "tool-789")
            .Add("ToolName", "FailingTool")
            .Add(Success, false)
            .Add("Duration", TimeSpan.FromSeconds(1))
            .Add("UsedAt", DateTime.UtcNow)
            .Build();

        var toolUsage = new Orkeon.Domain.Tools.ToolUsage(
            new Orkeon.Domain.Tools.ToolCallIdentity("tool-789", "FailingTool", AgentId1, "task-999"),
            duration: TimeSpan.FromSeconds(1),
            success: false,
            error: null,
            metadata: metadata);

        // Act
        var dto = ToolMapper.ToToolUsageDto(toolUsage);

        // Assert
        Assert.NotNull(dto);
        Assert.False(dto.Success);
        Assert.Equal("Tool execution failed", dto.Error);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToToolUsageDtoWithNullToolUsage()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => ToolMapper.ToToolUsageDto(null!));
        Assert.Equal("toolUsage", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnEmptyList_WhenUsingToDtoWithEmptyToolList()
    {
        // Arrange
        var tools = new List<IBaseTool>();

        // Act
        var dtos = ToolMapper.ToDto(tools);

        // Assert
        Assert.NotNull(dtos);
        Assert.Empty(dtos);
    }
}
