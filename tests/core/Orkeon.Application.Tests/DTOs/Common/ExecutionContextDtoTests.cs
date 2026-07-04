using Orkeon.Application.Common.DTOs;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.DTOs.Common;

public class ExecutionContextDtoTests
{
    private static readonly string[] s_results = ["result1", "result2"];
    private static readonly string[] s_importantVerified = ["important", "verified"];
    private static readonly string[] s_features = ["feature1", "feature2"];
    private static readonly string[] s_analysisTags = ["analysis", "historical", "comprehensive"];

    #region ExecutionContextDto Tests

    [Fact]
    public void ShouldCreateValidDto_WhenUsingExecutionContextDtoWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new ExecutionContextDto
        {
            Id = "ctx-123"
        };

        // Assert
        Assert.Equal("ctx-123", dto.Id);
        Assert.Empty(dto.Variables);
        Assert.Empty(dto.PreviousOutputs);
        Assert.Null(dto.MemoryContext);
        Assert.Null(dto.UserInput);
        Assert.Null(dto.SessionId);
        Assert.True(dto.CreatedAt <= DateTime.UtcNow);
        Assert.Empty(dto.Metadata);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenUsingExecutionContextDtoWithAllProperties()
    {
        // Arrange
        var variables = new Dictionary<string, object>
        {
            { "environment", "production" },
            { "debug", false },
            { "maxRetries", 3 },
            { "timeout", 30000 }
        };

        var previousOutputs = new List<TaskOutputDto>
        {
            new()
            {
                TaskId = TaskId1,
                AgentId = AgentId1,
                Content = "Previous result 1",
                Success = true,
                CompletedAt = DateTime.UtcNow.AddMinutes(-30),
                ExecutionTime = TimeoutStandard
            },
            new()
            {
                TaskId = TaskId2,
                AgentId = AgentId2,
                Content = "Previous result 2",
                Success = true,
                CompletedAt = DateTime.UtcNow.AddMinutes(-20),
                ExecutionTime = TimeSpan.FromMinutes(8)
            }
        };

        var memoryContext = new MemoryContextDto
        {
            MemoryProvider = "Redis",
            ShortTermMemory =
            [
                new() { Id = "mem-1", Content = "Recent memory", Type = "fact" }
            ]
        };

        var metadata = new Dictionary<string, object>
        {
            { "source", "API" },
            { "version", "2.0" }
        };

        var createdAt = DateTime.UtcNow.AddMinutes(-5);

        // Act
        var dto = new ExecutionContextDto
        {
            Id = "ctx-complete",
            Variables = variables,
            PreviousOutputs = previousOutputs,
            MemoryContext = memoryContext,
            UserInput = "Analyze the latest data",
            SessionId = "session-456",
            CreatedAt = createdAt,
            Metadata = metadata
        };

        // Assert
        Assert.Equal("ctx-complete", dto.Id);
        Assert.Equal(4, dto.Variables.Count);
        Assert.Equal("production", dto.Variables["environment"]);
        Assert.Equal(3, dto.Variables["maxRetries"]);
        Assert.Equal(2, dto.PreviousOutputs.Count);
        Assert.NotNull(dto.MemoryContext);
        Assert.Equal("Redis", dto.MemoryContext.MemoryProvider);
        Assert.Equal("Analyze the latest data", dto.UserInput);
        Assert.Equal("session-456", dto.SessionId);
        Assert.Equal(createdAt, dto.CreatedAt);
        Assert.Equal(2, dto.Metadata.Count);
    }

    [Fact]
    public void ShouldBeCurrentTime_WhenUsingExecutionContextDtoWithDefaultCreatedAt()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var dto = new ExecutionContextDto { Id = "ctx-1" };
        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.True(beforeCreation <= dto.CreatedAt);
        Assert.True(dto.CreatedAt <= afterCreation);
    }

    #endregion

    #region TaskOutputDto Tests

    [Fact]
    public void ShouldCreateValidDto_WhenUsingTaskOutputDtoWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new TaskOutputDto
        {
            TaskId = "task-123",
            AgentId = "agent-456",
            Content = "Task completed successfully"
        };

        // Assert
        Assert.Equal("task-123", dto.TaskId);
        Assert.Equal("agent-456", dto.AgentId);
        Assert.Equal("Task completed successfully", dto.Content);
        Assert.Null(dto.StructuredOutput);
        Assert.False(dto.Success);
        Assert.Equal(default(DateTime), dto.CompletedAt);
        Assert.Equal(TimeSpan.Zero, dto.ExecutionTime);
        Assert.Empty(dto.ToolsUsed);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenUsingTaskOutputDtoWithAllProperties()
    {
        // Arrange
        var toolsUsed = new List<ToolUsageDto>
        {
            new()
            {
                ToolId = "tool-1",
                ToolName = "FileReader",
                Input = new Dictionary<string, object> { { ParamPath, "/data/file.txt" } },
                Output = "File content",
                Success = true,
                ExecutionTime = TimeSpan.FromSeconds(2)
            },
            new()
            {
                ToolId = "tool-2",
                ToolName = "DataProcessor",
                Input = new Dictionary<string, object> { { "format", "json" } },
                Output = "Processed data",
                Success = true,
                ExecutionTime = TimeSpan.FromSeconds(5)
            }
        };

        var structuredOutput = new
        {
            status = "complete",
            results = s_results,
            score = 95
        };

        var completedAt = DateTime.UtcNow;

        // Act
        var dto = new TaskOutputDto
        {
            TaskId = "task-999",
            AgentId = "agent-999",
            Content = "Complex task with multiple tool calls completed",
            StructuredOutput = structuredOutput,
            Success = true,
            CompletedAt = completedAt,
            ExecutionTime = TimeSpan.FromMinutes(3),
            ToolsUsed = toolsUsed
        };

        // Assert
        Assert.Equal("task-999", dto.TaskId);
        Assert.NotNull(dto.StructuredOutput);
        Assert.True(dto.Success);
        Assert.Equal(completedAt, dto.CompletedAt);
        Assert.Equal(TimeSpan.FromMinutes(3), dto.ExecutionTime);
        Assert.Equal(2, dto.ToolsUsed.Count);
        Assert.Equal("FileReader", dto.ToolsUsed[0].ToolName);
        Assert.Equal("DataProcessor", dto.ToolsUsed[1].ToolName);
    }

    #endregion

    #region MemoryContextDto Tests

    [Fact]
    public void ShouldUseInMemory_WhenUsingMemoryContextDtoWithDefaultProvider()
    {
        // Arrange & Act
        var dto = new MemoryContextDto();

        // Assert
        Assert.Equal("InMemory", dto.MemoryProvider);
        Assert.Empty(dto.ShortTermMemory);
        Assert.Empty(dto.LongTermMemory);
        Assert.Empty(dto.EpisodicMemory);
    }

    [Fact]
    public void ShouldOrganizeByType_WhenUsingMemoryContextDtoWithMemoryItems()
    {
        // Arrange
        var shortTermMemory = new List<MemoryItemDto>
        {
            new() { Id = "st-1", Content = "Recent interaction", Type = "interaction", Relevance = 0.9 },
            new() { Id = "st-2", Content = "Current context", Type = "context", Relevance = 0.95 }
        };

        var longTermMemory = new List<MemoryItemDto>
        {
            new() { Id = "lt-1", Content = "Historical fact", Type = "fact", Relevance = 0.8 },
            new() { Id = "lt-2", Content = "Learned pattern", Type = "pattern", Relevance = 0.85 }
        };

        var episodicMemory = new List<MemoryItemDto>
        {
            new() { Id = "ep-1", Content = "Previous session", Type = "episode", Relevance = 0.7 }
        };

        // Act
        var dto = new MemoryContextDto
        {
            MemoryProvider = "Redis",
            ShortTermMemory = shortTermMemory,
            LongTermMemory = longTermMemory,
            EpisodicMemory = episodicMemory
        };

        // Assert
        Assert.Equal("Redis", dto.MemoryProvider);
        Assert.Equal(2, dto.ShortTermMemory.Count);
        Assert.Equal(2, dto.LongTermMemory.Count);
        Assert.Single(dto.EpisodicMemory);
        Assert.Equal("st-1", dto.ShortTermMemory[0].Id);
        Assert.Equal("lt-1", dto.LongTermMemory[0].Id);
        Assert.Equal("ep-1", dto.EpisodicMemory[0].Id);
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("Redis")]
    [InlineData("SQLite")]
    [InlineData("ChromaDB")]
    [InlineData("Pinecone")]
    public void ShouldBeSettable_WhenUsingMemoryContextDtoWithDifferentProviders(string provider)
    {
        // Arrange & Act
        var dto = new MemoryContextDto
        {
            MemoryProvider = provider
        };

        // Assert
        Assert.Equal(provider, dto.MemoryProvider);
    }

    #endregion

    #region MemoryItemDto Tests

    [Fact]
    public void ShouldCreateValidDto_WhenUsingMemoryItemDtoWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new MemoryItemDto
        {
            Id = "mem-123",
            Content = "Important memory content",
            Type = "fact"
        };

        // Assert
        Assert.Equal("mem-123", dto.Id);
        Assert.Equal("Important memory content", dto.Content);
        Assert.Equal("fact", dto.Type);
        Assert.Equal(0.0, dto.Relevance);
        Assert.Equal(default(DateTime), dto.CreatedAt);
        Assert.Null(dto.AgentId);
        Assert.Null(dto.TaskId);
        Assert.Empty(dto.Metadata);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenUsingMemoryItemDtoWithAllProperties()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            { "source", "user_input" },
            { "confidence", 0.95 },
            { "tags", s_importantVerified }
        };

        var createdAt = DateTime.UtcNow.AddHours(-1);

        // Act
        var dto = new MemoryItemDto
        {
            Id = "mem-complete",
            Content = "Complete memory item with all properties",
            Type = "experience",
            Relevance = 0.92,
            CreatedAt = createdAt,
            AgentId = "agent-789",
            TaskId = "task-456",
            Metadata = metadata
        };

        // Assert
        Assert.Equal("mem-complete", dto.Id);
        Assert.Equal("experience", dto.Type);
        Assert.Equal(0.92, dto.Relevance);
        Assert.Equal(createdAt, dto.CreatedAt);
        Assert.Equal("agent-789", dto.AgentId);
        Assert.Equal("task-456", dto.TaskId);
        Assert.Equal(3, dto.Metadata.Count);
        Assert.Equal("user_input", dto.Metadata["source"]);
        Assert.Equal(0.95, dto.Metadata["confidence"]);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void ShouldAcceptValidRange_WhenUsingMemoryItemDtoUsingRelevanceScore(double relevance)
    {
        // Arrange & Act
        var dto = new MemoryItemDto
        {
            Id = "mem-1",
            Content = "Content",
            Type = "fact",
            Relevance = relevance
        };

        // Assert
        Assert.Equal(relevance, dto.Relevance);
    }

    #endregion

    #region ToolUsageDto Tests

    [Fact]
    public void ShouldCreateValidDto_WhenUsingToolUsageDtoWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new ToolUsageDto
        {
            ToolId = "tool-123",
            ToolName = "Calculator",
            Output = "Result: 42"
        };

        // Assert
        Assert.Equal("tool-123", dto.ToolId);
        Assert.Equal("Calculator", dto.ToolName);
        Assert.Equal("Result: 42", dto.Output);
        Assert.Empty(dto.Input);
        Assert.False(dto.Success);
        Assert.Equal(TimeSpan.Zero, dto.ExecutionTime);
        Assert.Null(dto.Error);
        Assert.True(dto.CalledAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenUsingToolUsageDtoWithAllProperties()
    {
        // Arrange
        var input = new Dictionary<string, object>
        {
            { "operation", "multiply" },
            { "operand1", 6 },
            { "operand2", 7 }
        };

        var calledAt = DateTime.UtcNow.AddSeconds(-5);

        // Act
        var dto = new ToolUsageDto
        {
            ToolId = "tool-calc",
            ToolName = "AdvancedCalculator",
            Input = input,
            Output = "42",
            Success = true,
            ExecutionTime = TimeSpan.FromMilliseconds(50),
            Error = null,
            CalledAt = calledAt
        };

        // Assert
        Assert.Equal("tool-calc", dto.ToolId);
        Assert.Equal(3, dto.Input.Count);
        Assert.Equal("multiply", dto.Input["operation"]);
        Assert.Equal(6, dto.Input["operand1"]);
        Assert.True(dto.Success);
        Assert.Equal(TimeSpan.FromMilliseconds(50), dto.ExecutionTime);
        Assert.Null(dto.Error);
        Assert.Equal(calledAt, dto.CalledAt);
    }

    [Fact]
    public void ShouldTrackFailure_WhenUsingToolUsageDtoWithError()
    {
        // Arrange & Act
        var dto = new ToolUsageDto
        {
            ToolId = "tool-fail",
            ToolName = "FailingTool",
            Input = new Dictionary<string, object> { { "param", "value" } },
            Output = "",
            Success = false,
            ExecutionTime = TimeoutQuick,
            Error = "Connection timeout: Unable to reach external service"
        };

        // Assert
        Assert.False(dto.Success);
        Assert.Equal("Connection timeout: Unable to reach external service", dto.Error);
        Assert.Equal(TimeoutQuick, dto.ExecutionTime);
        Assert.Empty(dto.Output);
    }

    [Fact]
    public void ShouldBeCurrentTime_WhenUsingToolUsageDtoWithDefaultCalledAt()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var dto = new ToolUsageDto
        {
            ToolId = "tool-1",
            ToolName = "Tool",
            Output = "Output"
        };
        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.True(beforeCreation <= dto.CalledAt);
        Assert.True(dto.CalledAt <= afterCreation);
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void ShouldWork_WhenUsingExecutionContextDtoRecordingEquality()
    {
        // Arrange
        var variables = new Dictionary<string, object> { { "key", "value" } };
        var metadata = new Dictionary<string, object> { { "meta", "data" } };
        var previousOutputs = new List<TaskOutputDto>();
        var createdAt = DateTime.UtcNow;

        var dto1 = new ExecutionContextDto
        {
            Id = "ctx-1",
            Variables = variables,
            SessionId = SessionId1,
            Metadata = metadata,
            PreviousOutputs = previousOutputs,
            CreatedAt = createdAt
        };

        var dto2 = new ExecutionContextDto
        {
            Id = "ctx-1",
            Variables = variables,
            SessionId = SessionId1,
            Metadata = metadata,
            PreviousOutputs = previousOutputs,
            CreatedAt = createdAt
        };

        var dto3 = new ExecutionContextDto
        {
            Id = "ctx-2",
            Variables = variables,
            SessionId = SessionId1,
            Metadata = metadata,
            PreviousOutputs = previousOutputs,
            CreatedAt = createdAt
        };

        // Act & Assert
        Assert.Equal(dto1, dto2);
        Assert.NotEqual(dto1, dto3);
        Assert.Equal(dto1.GetHashCode(), dto2.GetHashCode());
    }

    [Fact]
    public void ShouldWork_WhenUsingMemoryItemDtoRecordingEquality()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var sharedMetadata = new Dictionary<string, object> { { "key", "value" } };

        var dto1 = new MemoryItemDto
        {
            Id = "mem-1",
            Content = "Content",
            Type = "fact",
            Relevance = 0.8,
            CreatedAt = createdAt,
            Metadata = sharedMetadata
        };

        var dto2 = new MemoryItemDto
        {
            Id = "mem-1",
            Content = "Content",
            Type = "fact",
            Relevance = 0.8,
            CreatedAt = createdAt,
            Metadata = sharedMetadata
        };

        var dto3 = new MemoryItemDto
        {
            Id = "mem-1",
            Content = "Different content",
            Type = "fact",
            Relevance = 0.8,
            CreatedAt = createdAt,
            Metadata = sharedMetadata
        };

        // Act & Assert
        Assert.Equal(dto1, dto2);
        Assert.NotEqual(dto1, dto3);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void ShouldBeValid_WhenUsingCompleteExecutionContextWithAllData()
    {
        // Arrange & Act
        var dto = new ExecutionContextDto
        {
            Id = "ctx-integration",
            Variables = new Dictionary<string, object>
            {
                { "mode", "production" },
                { "region", "us-west-2" },
                { "features", s_features },
                { "settings", new { timeout = 30000, retries = 3 } }
            },
            PreviousOutputs =
            [
                new()
                {
                    TaskId = "task-prev-1",
                    AgentId = "agent-prev-1",
                    Content = "Previous analysis completed",
                    Success = true,
                    CompletedAt = DateTime.UtcNow.AddHours(-1),
                    ExecutionTime = TimeoutExtended,
                    ToolsUsed =
                    [
                        new()
                        {
                            ToolId = "tool-search",
                            ToolName = "WebSearch",
                            Success = true,
                            ExecutionTime = TimeSpan.FromSeconds(5)
                        }
                    ]
                }
            ],
            MemoryContext = new MemoryContextDto
            {
                MemoryProvider = "Redis",
                ShortTermMemory =
                [
                    new()
                    {
                        Id = "mem-st-1",
                        Content = "Current session context",
                        Type = "context",
                        Relevance = 0.95,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                        AgentId = "agent-current"
                    }
                ],
                LongTermMemory =
                [
                    new()
                    {
                        Id = "mem-lt-1",
                        Content = "Historical pattern",
                        Type = "pattern",
                        Relevance = 0.85,
                        CreatedAt = DateTime.UtcNow.AddDays(-7)
                    }
                ],
                EpisodicMemory =
                [
                    new()
                    {
                        Id = "mem-ep-1",
                        Content = "Previous similar task",
                        Type = "episode",
                        Relevance = 0.75,
                        CreatedAt = DateTime.UtcNow.AddDays(-1),
                        TaskId = "task-similar"
                    }
                ]
            },
            UserInput = "Perform comprehensive analysis with historical context",
            SessionId = "session-integration",
            CreatedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>
            {
                { "client", "enterprise" },
                { "priority", "high" },
                { "tags", s_analysisTags }
            }
        };

        // Assert
        Assert.Equal("ctx-integration", dto.Id);
        Assert.Equal(4, dto.Variables.Count);
        Assert.Single(dto.PreviousOutputs);
        Assert.NotNull(dto.MemoryContext);
        Assert.Single(dto.MemoryContext.ShortTermMemory);
        Assert.Single(dto.MemoryContext.LongTermMemory);
        Assert.Single(dto.MemoryContext.EpisodicMemory);
        Assert.Equal("Perform comprehensive analysis with historical context", dto.UserInput);
        Assert.Equal("session-integration", dto.SessionId);
        Assert.Equal(3, dto.Metadata.Count);

        // Verify nested structures
        Assert.Equal("production", dto.Variables["mode"]);
        Assert.True(dto.PreviousOutputs[0].Success);
        Assert.Single(dto.PreviousOutputs[0].ToolsUsed);
        Assert.Equal(0.95, dto.MemoryContext.ShortTermMemory[0].Relevance);
    }

    #endregion
}
