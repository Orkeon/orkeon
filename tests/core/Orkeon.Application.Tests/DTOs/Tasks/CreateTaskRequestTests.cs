using Orkeon.Application.Task.DTOs;
using TaskPriority = Orkeon.Application.Task.DTOs.TaskPriority;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Application.Tests.DTOs.Tasks;

public class CreateTaskRequestTests
{
    [Fact]
    public void ShouldSetDefaults_WhenConstructingCreateTaskRequest()
    {
        // Act
        var request = new CreateTaskRequest { Description = "Default task description" };

        // Assert
        Assert.Equal("Default task description", request.Description);
        Assert.Null(request.ExpectedOutput);
        Assert.Null(request.AgentRole);
        Assert.Null(request.AgentId);
        Assert.Empty(request.Tools);
        Assert.Empty(request.Context);
        Assert.Equal(TaskPriority.Normal, request.Priority);
        Assert.Empty(request.Dependencies);
        Assert.Equal("text", request.OutputFormat);
        Assert.Null(request.Settings);
    }

    [Fact]
    public void ShouldPopulateAllFields_WhenUsingInitSyntax()
    {
        // Act
        var request = new CreateTaskRequest
        {
            Description = "Analyze market data",
            ExpectedOutput = "JSON report",
            AgentRole = RoleAnalyst,
            AgentId = AgentId1,
            Tools = ["web_search", "file_read"],
            Context = new Dictionary<string, object> { ["region"] = "EU" },
            Priority = TaskPriority.High,
            Dependencies = ["task-0"],
            OutputFormat = "json",
            Settings = new TaskSettingsDto { Verbose = true }
        };

        // Assert
        Assert.Equal("Analyze market data", request.Description);
        Assert.Equal("JSON report", request.ExpectedOutput);
        Assert.Equal(RoleAnalyst, request.AgentRole);
        Assert.Equal(AgentId1, request.AgentId);
        Assert.Equal(2, request.Tools.Count);
        Assert.Single(request.Context);
        Assert.Equal(TaskPriority.High, request.Priority);
        Assert.Single(request.Dependencies);
        Assert.Equal("json", request.OutputFormat);
        Assert.NotNull(request.Settings);
        Assert.True(request.Settings.Verbose);
    }

    [Fact]
    public void ShouldSupportRecordWithExpression_WhenModifying()
    {
        // Arrange
        var original = new CreateTaskRequest { Description = "Do X", Priority = TaskPriority.Normal };

        // Act
        var modified = original with { Description = "Do Y" };

        // Assert
        Assert.Equal("Do Y", modified.Description);
        Assert.Equal(TaskPriority.Normal, modified.Priority);
    }

    // ══════════════ TaskSettingsDto ══════════════

    [Fact]
    public void ShouldSetDefaults_WhenConstructingTaskSettingsDto()
    {
        // Act
        var settings = new TaskSettingsDto();

        // Assert
        Assert.Null(settings.MaxExecutionTimeSeconds);
        Assert.Null(settings.MaxIterations);
        Assert.False(settings.Verbose);
        Assert.True(settings.AllowDelegation);
        Assert.True(settings.SaveToMemory);
        Assert.Null(settings.RetryConfig);
    }

    [Fact]
    public void ShouldPopulateAllFields_WhenSettingTaskSettings()
    {
        // Act
        var settings = new TaskSettingsDto
        {
            MaxExecutionTimeSeconds = 600,
            MaxIterations = 15,
            Verbose = true,
            AllowDelegation = false,
            SaveToMemory = false,
            RetryConfig = new TaskRetryConfigDto { MaxRetries = 5 }
        };

        // Assert
        Assert.Equal(600, settings.MaxExecutionTimeSeconds);
        Assert.Equal(15, settings.MaxIterations);
        Assert.True(settings.Verbose);
        Assert.False(settings.AllowDelegation);
        Assert.False(settings.SaveToMemory);
        Assert.Equal(5, settings.RetryConfig!.MaxRetries);
    }

    // ══════════════ TaskRetryConfigDto ══════════════

    [Fact]
    public void ShouldSetDefaults_WhenConstructingTaskRetryConfigDto()
    {
        // Act
        var config = new TaskRetryConfigDto();

        // Assert
        Assert.Equal(3, config.MaxRetries);
        Assert.Equal(5, config.BaseDelaySeconds);
        Assert.True(config.UseExponentialBackoff);
        Assert.Equal(300, config.MaxDelaySeconds);
    }

    // ══════════════ UpdateTaskRequest ══════════════

    [Fact]
    public void ShouldDefaultToNull_WhenConstructingUpdateTaskRequest()
    {
        // Act
        var request = new UpdateTaskRequest();

        // Assert
        Assert.Null(request.Description);
        Assert.Null(request.ExpectedOutput);
        Assert.Null(request.AgentId);
        Assert.Null(request.Tools);
        Assert.Null(request.Context);
        Assert.Null(request.Priority);
        Assert.Null(request.Dependencies);
        Assert.Null(request.OutputFormat);
        Assert.Null(request.Settings);
    }

    [Fact]
    public void ShouldSupportWithExpression_WhenCopyingCreateTaskRequest()
    {
        // Arrange
        var original = new CreateTaskRequest
        {
            Description = "Original task",
            Priority = TaskPriority.Low
        };

        // Act
        var modified = original with { Priority = TaskPriority.Critical };

        // Assert
        Assert.Equal("Original task", modified.Description);
        Assert.Equal(TaskPriority.Critical, modified.Priority);
    }
}
