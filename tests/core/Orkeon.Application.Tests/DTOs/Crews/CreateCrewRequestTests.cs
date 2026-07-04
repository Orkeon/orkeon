using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Crew.DTOs;
using Orkeon.Application.Task.DTOs;
using Orkeon.Application.Common.DTOs;
using CrewSettingsDto = Orkeon.Application.Crew.DTOs.CrewSettingsDto;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Application.Tests.DTOs.Crews;

public class CreateCrewRequestTests
{
    [Fact]
    public void ShouldInitializeCorrectly_WhenConstructingWithDefaultValues()
    {
        // Arrange & Act
        var request = new CreateCrewRequest { Name = "Test Crew" };

        // Assert
        Assert.Equal("Test Crew", request.Name);
        Assert.Equal(string.Empty, request.Description);
        Assert.Equal(ProcessType.Sequential, request.Process);
        Assert.False(request.Verbose);
        Assert.False(request.Planning);
        Assert.NotNull(request.Agents);
        Assert.Empty(request.Agents);
        Assert.NotNull(request.Tasks);
        Assert.Empty(request.Tasks);
        Assert.Null(request.Settings);
        Assert.Null(request.ManagerLlm);
        Assert.NotNull(request.Metadata);
        Assert.Empty(request.Metadata);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenConstructingWithAllProperties()
    {
        // Arrange
        var settings = new CrewSettingsDto
        {
            MaxExecutionTimeSeconds = 3600,
            MaxParallelAgents = 5,
            EnableMemorySharing = true,
            EnableOutputCaching = true
        };

        var managerLlm = new LlmConfigDto
        {
            Provider = "OpenAI",
            Model = ModelGpt4,
            Temperature = 0.7
        };

        var agents = new List<CrewAgentRequest>
        {
            new() { ExistingAgentId = "agent-123" },
            new() { NewAgent = new CreateAgentRequest
            {
                Role = RoleDeveloper,
                Goal = "Write quality code",
                Backstory = "Experienced developer"
            }}
        };

        var tasks = new List<CrewTaskRequest>
        {
            new() { ExistingTaskId = "task-456" },
            new() { NewTask = new CreateTaskRequest
            {
                Description = "Implement feature X"
            }}
        };

        var metadata = new Dictionary<string, object>
        {
            ["environment"] = "production",
            ["version"] = "1.0"
        };

        // Act
        var request = new CreateCrewRequest
        {
            Name = "Test Crew",
            Description = "A test crew for development",
            Process = ProcessType.Hierarchical,
            Verbose = true,
            Planning = true,
            Agents = agents,
            Tasks = tasks,
            Settings = settings,
            ManagerLlm = managerLlm,
            Metadata = metadata
        };

        // Assert
        Assert.Equal("Test Crew", request.Name);
        Assert.Equal("A test crew for development", request.Description);
        Assert.Equal(ProcessType.Hierarchical, request.Process);
        Assert.True(request.Verbose);
        Assert.True(request.Planning);
        Assert.Equal(2, request.Agents.Count);
        Assert.Equal(2, request.Tasks.Count);
        Assert.NotNull(request.Settings);
        Assert.NotNull(request.ManagerLlm);
        Assert.Equal(2, request.Metadata.Count);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingDescriptionWithLongText()
    {
        // Arrange
        var longDescription = new string('A', 1000);
        var request = new CreateCrewRequest
        {
            Name = "Test Crew",
            Description = longDescription
        };

        // Act & Assert
        Assert.Equal(1000, request.Description.Length);
        Assert.Equal(longDescription, request.Description);
    }

    [Theory]
    [InlineData(ProcessType.Sequential)]
    [InlineData(ProcessType.Parallel)]
    [InlineData(ProcessType.Hierarchical)]
    public void ShouldBeSettable_WhenProcessingWithAllTypes(ProcessType processType)
    {
        // Arrange & Act
        var request = new CreateCrewRequest { Name = "Test", Process = processType };

        // Assert
        Assert.Equal(processType, request.Process);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var name = "Test Crew";
        var description = "Description";
        var process = ProcessType.Sequential;
        var agents = new List<CrewAgentRequest>();
        var tasks = new List<CrewTaskRequest>();
        var metadata = new Dictionary<string, object>();

        // Act
        var request1 = new CreateCrewRequest
        {
            Name = name,
            Description = description,
            Process = process,
            Agents = agents,
            Tasks = tasks,
            Metadata = metadata
        };

        var request2 = new CreateCrewRequest
        {
            Name = name,
            Description = description,
            Process = process,
            Agents = agents,
            Tasks = tasks,
            Metadata = metadata
        };

        // Assert
        Assert.Equal(request1, request2);
        Assert.Equal(request1.GetHashCode(), request2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange & Act
        var request1 = new CreateCrewRequest { Name = "Crew 1" };
        var request2 = new CreateCrewRequest { Name = "Crew 2" };

        // Assert
        Assert.NotEqual(request1, request2);
    }
}

public class UpdateCrewRequestTests
{
    [Fact]
    public void ShouldHaveNullProperties_WhenConstructingWithDefaultValues()
    {
        // Arrange & Act
        var request = new UpdateCrewRequest();

        // Assert
        Assert.Null(request.Name);
        Assert.Null(request.Description);
        Assert.Null(request.Process);
        Assert.Null(request.Verbose);
        Assert.Null(request.Planning);
        Assert.Null(request.Settings);
        Assert.Null(request.ManagerLlm);
        Assert.Null(request.Metadata);
    }

    [Fact]
    public void ShouldOnlySetProvidedFields_WhenUsingPartialUpdate()
    {
        // Arrange & Act
        var request = new UpdateCrewRequest
        {
            Name = "Updated Name",
            Verbose = true
            // Other fields remain null
        };

        // Assert
        Assert.Equal("Updated Name", request.Name);
        Assert.True(request.Verbose);
        Assert.Null(request.Description);
        Assert.Null(request.Process);
        Assert.Null(request.Planning);
    }
}

public class CrewAgentRequestTests
{
    [Fact]
    public void ShouldHaveNullProperties_WhenConstructingWithDefaultValues()
    {
        // Arrange & Act
        var request = new CrewAgentRequest();

        // Assert
        Assert.Null(request.ExistingAgentId);
        Assert.Null(request.NewAgent);
        Assert.Null(request.CrewSpecificSettings);
    }

    [Fact]
    public void ShouldSetAgentId_WhenUsingWithExistingAgent()
    {
        // Arrange & Act
        var request = new CrewAgentRequest
        {
            ExistingAgentId = "agent-123",
            CrewSpecificSettings = new AgentSettingsDto { Verbose = true }
        };

        // Assert
        Assert.Equal("agent-123", request.ExistingAgentId);
        Assert.Null(request.NewAgent);
        Assert.NotNull(request.CrewSpecificSettings);
        Assert.True(request.CrewSpecificSettings.Verbose);
    }

    [Fact]
    public void ShouldSetAgentConfiguration_WhenUsingWithNewAgent()
    {
        // Arrange
        var newAgent = new CreateAgentRequest
        {
            Role = RoleDeveloper,
            Goal = "Write quality code",
            Backstory = "Experienced software engineer"
        };

        // Act
        var request = new CrewAgentRequest
        {
            NewAgent = newAgent,
            CrewSpecificSettings = new AgentSettingsDto
            {
                AllowDelegation = false,
                MaxIterations = 10
            }
        };

        // Assert
        Assert.Null(request.ExistingAgentId);
        Assert.NotNull(request.NewAgent);
        Assert.Equal(RoleDeveloper, request.NewAgent.Role);
        Assert.NotNull(request.CrewSpecificSettings);
        Assert.False(request.CrewSpecificSettings.AllowDelegation);
        Assert.Equal(10, request.CrewSpecificSettings.MaxIterations);
    }
}

public class CrewTaskRequestTests
{
    [Fact]
    public void ShouldHaveNullProperties_WhenConstructingWithDefaultValues()
    {
        // Arrange & Act
        var request = new CrewTaskRequest();

        // Assert
        Assert.Null(request.ExistingTaskId);
        Assert.Null(request.NewTask);
        Assert.Null(request.CrewSpecificSettings);
    }

    [Fact]
    public void ShouldSetTaskId_WhenUsingWithExistingTask()
    {
        // Arrange & Act
        var request = new CrewTaskRequest
        {
            ExistingTaskId = "task-456",
            CrewSpecificSettings = new TaskSettingsDto { Verbose = true }
        };

        // Assert
        Assert.Equal("task-456", request.ExistingTaskId);
        Assert.Null(request.NewTask);
        Assert.NotNull(request.CrewSpecificSettings);
        Assert.True(request.CrewSpecificSettings.Verbose);
    }

    [Fact]
    public void ShouldSetTaskConfiguration_WhenUsingWithNewTask()
    {
        // Arrange
        var newTask = new CreateTaskRequest
        {
            Description = "Implement new feature",
            ExpectedOutput = "Working feature implementation",
            Priority = TaskPriority.High
        };

        // Act
        var request = new CrewTaskRequest
        {
            NewTask = newTask,
            CrewSpecificSettings = new TaskSettingsDto
            {
                MaxExecutionTimeSeconds = 600,
                MaxIterations = 5
            }
        };

        // Assert
        Assert.Null(request.ExistingTaskId);
        Assert.NotNull(request.NewTask);
        Assert.Equal("Implement new feature", request.NewTask.Description);
        Assert.Equal(TaskPriority.High, request.NewTask.Priority);
        Assert.NotNull(request.CrewSpecificSettings);
        Assert.Equal(600, request.CrewSpecificSettings.MaxExecutionTimeSeconds);
        Assert.Equal(5, request.CrewSpecificSettings.MaxIterations);
    }
}

public class CrewSettingsDtoTests
{
    [Fact]
    public void ShouldSetDefaults_WhenConstructingWithDefaultValues()
    {
        // Arrange & Act
        var settings = new CrewSettingsDto();

        // Assert
        Assert.Null(settings.MaxExecutionTimeSeconds);
        Assert.Null(settings.MaxParallelAgents);
        Assert.True(settings.EnableMemorySharing);
        Assert.False(settings.EnableOutputCaching);
        Assert.Null(settings.RetryConfig);
        Assert.Null(settings.MemoryConfig);
        Assert.Null(settings.CallbackConfig);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenUsingWithAllConfigurations()
    {
        // Arrange
        var retryConfig = new CrewRetryConfigDto
        {
            MaxRetries = 3,
            RetryFailedTasks = true,
            RetryDelaySeconds = 60
        };

        var memoryConfig = new CrewMemoryConfigDto
        {
            UseSharedMemory = true,
            MemoryProvider = "Redis",
            MaxMemoryItems = 5000,
            CleanupIntervalMinutes = 30
        };

        var callbackConfig = new CrewCallbackConfigDto
        {
            WebhookUrl = new Uri("https://example.com/webhook"),
            NotifyOnCompletion = true,
            NotifyOnFailure = true
        };

        // Act
        var settings = new CrewSettingsDto
        {
            MaxExecutionTimeSeconds = 7200,
            MaxParallelAgents = 10,
            EnableMemorySharing = false,
            EnableOutputCaching = true,
            RetryConfig = retryConfig,
            MemoryConfig = memoryConfig,
            CallbackConfig = callbackConfig
        };

        // Assert
        Assert.Equal(7200, settings.MaxExecutionTimeSeconds);
        Assert.Equal(10, settings.MaxParallelAgents);
        Assert.False(settings.EnableMemorySharing);
        Assert.True(settings.EnableOutputCaching);
        Assert.NotNull(settings.RetryConfig);
        Assert.NotNull(settings.MemoryConfig);
        Assert.NotNull(settings.CallbackConfig);
    }
}

public class CrewRetryConfigDtoTests
{
    [Fact]
    public void ShouldSetDefaults_WhenConstructingWithDefaultValues()
    {
        // Arrange & Act
        var config = new CrewRetryConfigDto();

        // Assert
        Assert.Equal(1, config.MaxRetries);
        Assert.True(config.RetryFailedTasks);
        Assert.Equal(30, config.RetryDelaySeconds);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingAllProperties()
    {
        // Act
        var config = new CrewRetryConfigDto
        {
            MaxRetries = 5,
            RetryFailedTasks = false,
            RetryDelaySeconds = 120
        };

        // Assert
        Assert.Equal(5, config.MaxRetries);
        Assert.False(config.RetryFailedTasks);
        Assert.Equal(120, config.RetryDelaySeconds);
    }
}

public class CrewMemoryConfigDtoTests
{
    [Fact]
    public void ShouldSetDefaults_WhenConstructingWithDefaultValues()
    {
        // Arrange & Act
        var config = new CrewMemoryConfigDto();

        // Assert
        Assert.True(config.UseSharedMemory);
        Assert.Equal("InMemory", config.MemoryProvider);
        Assert.Equal(1000, config.MaxMemoryItems);
        Assert.Equal(60, config.CleanupIntervalMinutes);
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("Redis")]
    [InlineData("SQLite")]
    [InlineData("CustomProvider")]
    public void ShouldBeSettable_WhenUsingMemoryProviderWithDifferentProviders(string provider)
    {
        // Arrange & Act
        var config = new CrewMemoryConfigDto { MemoryProvider = provider };

        // Assert
        Assert.Equal(provider, config.MemoryProvider);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingAllProperties()
    {
        // Act
        var config = new CrewMemoryConfigDto
        {
            UseSharedMemory = false,
            MemoryProvider = "Redis",
            MaxMemoryItems = 5000,
            CleanupIntervalMinutes = 30
        };

        // Assert
        Assert.False(config.UseSharedMemory);
        Assert.Equal("Redis", config.MemoryProvider);
        Assert.Equal(5000, config.MaxMemoryItems);
        Assert.Equal(30, config.CleanupIntervalMinutes);
    }
}

public class CrewCallbackConfigDtoTests
{
    [Fact]
    public void ShouldSetDefaults_WhenConstructingWithDefaultValues()
    {
        // Arrange & Act
        var config = new CrewCallbackConfigDto();

        // Assert
        Assert.Null(config.WebhookUrl);
        Assert.False(config.NotifyOnCompletion);
        Assert.True(config.NotifyOnFailure);
        Assert.NotNull(config.CustomSettings);
        Assert.Empty(config.CustomSettings);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenUsingWithAllProperties()
    {
        // Arrange
        var customSettings = new Dictionary<string, object>
        {
            ["timeout"] = 30,
            ["retryCount"] = 3,
            ["headers"] = new Dictionary<string, string> { ["Authorization"] = "Bearer token" }
        };

        // Act
        var config = new CrewCallbackConfigDto
        {
            WebhookUrl = new Uri("https://api.example.com/callback"),
            NotifyOnCompletion = true,
            NotifyOnFailure = false,
            CustomSettings = customSettings
        };

        // Assert
        Assert.Equal(new Uri("https://api.example.com/callback"), config.WebhookUrl);
        Assert.True(config.NotifyOnCompletion);
        Assert.False(config.NotifyOnFailure);
        Assert.Equal(3, config.CustomSettings.Count);
        Assert.Equal(30, config.CustomSettings["timeout"]);
        Assert.Equal(3, config.CustomSettings["retryCount"]);
        Assert.IsType<Dictionary<string, string>>(config.CustomSettings["headers"]);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var url = new Uri("https://example.com/webhook");
        var settings = new Dictionary<string, object> { ["key"] = "value" };

        // Act
        var config1 = new CrewCallbackConfigDto
        {
            WebhookUrl = url,
            NotifyOnCompletion = true,
            NotifyOnFailure = false,
            CustomSettings = settings
        };

        var config2 = new CrewCallbackConfigDto
        {
            WebhookUrl = url,
            NotifyOnCompletion = true,
            NotifyOnFailure = false,
            CustomSettings = settings
        };

        // Assert
        Assert.Equal(config1, config2);
        Assert.Equal(config1.GetHashCode(), config2.GetHashCode());
    }
}
