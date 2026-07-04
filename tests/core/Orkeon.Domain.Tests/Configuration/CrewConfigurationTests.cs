using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Domain.Tests.Configuration;

/// <summary>
/// Tests for Crew Configuration following Clean Architecture principles.
/// Tests the crew, agent, and task configuration records and their behavior.
/// </summary>
public class CrewConfigurationTests
{
    private static readonly string[] SprintFeatureTags = ["sprint1", "feature-x"];
    #region CrewConfiguration Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingCrewConfigurationWithDefaultConstructor()
    {
        // Act
        var config = new CrewConfiguration();

        // Assert
        Assert.Equal(string.Empty, config.Name);
        Assert.Equal(string.Empty, config.Goal);
        Assert.NotNull(config.Agents);
        Assert.Empty(config.Agents);
        Assert.NotNull(config.Tasks);
        Assert.Empty(config.Tasks);
        Assert.Equal(ProcessType.Sequential, config.Process);
        Assert.False(config.Verbose);
        Assert.False(config.Memory);
        Assert.False(config.Planning);
        Assert.Null(config.ManagerAgentId);
        Assert.Null(config.ExecutionConfig);
        Assert.NotNull(config.Metadata);
        Assert.Empty(config.Metadata);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingCrewConfigurationUsingProperties()
    {
        // Arrange
        var agents = new List<AgentConfiguration>
        {
            new AgentConfiguration { Id = AgentId.Create(), Role = AgentId.Create() },
            new AgentConfiguration { Id = AgentId.Create(), Role = AgentId.Create() }
        };
        var tasks = new List<TaskConfiguration>
        {
            new TaskConfiguration { Id = TaskId.Create(), Description = TaskId.Create() },
            new TaskConfiguration { Id = TaskId.Create(), Description = TaskId.Create() }
        };
        var executionConfig = new ExecutionConfig { MaxConcurrentTasks = 5 };
        var metadata = new Dictionary<string, object>
        {
            { "project", "Orkeon" },
            { "version", "1.0" }
        };

        // Act
        var config = new CrewConfiguration
        {
            Name = "Development Crew",
            Goal = "Build quality software",
            Agents = agents,
            Tasks = tasks,
            Process = ProcessType.Hierarchical,
            Verbose = true,
            Memory = true,
            Planning = true,
            ManagerAgentId = AgentId.Create(),
            ExecutionConfig = executionConfig,
            Metadata = metadata
        };

        // Assert
        Assert.Equal("Development Crew", config.Name);
        Assert.Equal("Build quality software", config.Goal);
        Assert.Equal(agents, config.Agents);
        Assert.Equal(2, config.Agents.Count);
        Assert.Equal(tasks, config.Tasks);
        Assert.Equal(2, config.Tasks.Count);
        Assert.Equal(ProcessType.Hierarchical, config.Process);
        Assert.True(config.Verbose);
        Assert.True(config.Memory);
        Assert.True(config.Planning);
        Assert.NotNull(config.ManagerAgentId);
        Assert.Equal(executionConfig, config.ExecutionConfig);
        Assert.Equal(metadata, config.Metadata);
        Assert.Equal(2, config.Metadata.Count);
    }

    [Theory]
    [InlineData("Sequential")]
    [InlineData("Hierarchical")]
    public void ShouldAcceptValidProcessTypes_WhenUsingCrewConfigurationProcessing(string processTypeStr)
    {
        // Act
        var processType = ProcessType.From(processTypeStr);
        var config = new CrewConfiguration { Process = processType };

        // Assert
        Assert.Equal(processType, config.Process);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    public void ShouldAcceptValues_WhenUsingCrewConfigurationUsingBooleanFlags(bool verbose, bool memory, bool planning)
    {
        // Act
        var config = new CrewConfiguration
        {
            Verbose = verbose,
            Memory = memory,
            Planning = planning
        };

        // Assert
        Assert.Equal(verbose, config.Verbose);
        Assert.Equal(memory, config.Memory);
        Assert.Equal(planning, config.Planning);
    }

    #endregion

    #region CrewConfiguration Collections Tests

    [Fact]
    public void ShouldSupportInitialization_WhenUsingCrewConfigurationUsingAgents()
    {
        // Arrange
        var agent1 = new AgentConfiguration { Id = AgentId.Create(), Role = "Agent1" };
        var agent2 = new AgentConfiguration { Id = AgentId.Create(), Role = "Agent2" };

        // Act
        var config = new CrewConfiguration
        {
            Agents = [agent1, agent2]
        };

        // Assert
        Assert.Equal(2, config.Agents.Count);
        Assert.Contains(agent1, config.Agents);
        Assert.Contains(agent2, config.Agents);
        Assert.Equal(agent1.Id, config.Agents[0].Id);
        Assert.Equal(agent2.Id, config.Agents[1].Id);
    }

    [Fact]
    public void ShouldSupportInitialization_WhenUsingCrewConfigurationUsingTasks()
    {
        // Arrange
        var task1 = new TaskConfiguration { Id = TaskId.Create(), Description = "Task1" };
        var task2 = new TaskConfiguration { Id = TaskId.Create(), Description = "Task2" };

        // Act
        var config = new CrewConfiguration
        {
            Tasks = [task1, task2]
        };

        // Assert
        Assert.Equal(2, config.Tasks.Count);
        Assert.Contains(task1, config.Tasks);
        Assert.Contains(task2, config.Tasks);
        Assert.Equal(task1.Id, config.Tasks[0].Id);
        Assert.Equal(task2.Id, config.Tasks[1].Id);
    }

    [Fact]
    public void ShouldSupportInitialization_WhenUsingCrewConfigurationUsingMetadata()
    {
        // Act
        var config = new CrewConfiguration
        {
            Metadata = new Dictionary<string, object>
            {
                { "environment", "development" },
                { "priority", "high" },
                { "startDate", DateTime.UtcNow },
                { "tags", SprintFeatureTags }
            }
        };

        // Assert
        Assert.Equal(4, config.Metadata.Count);
        Assert.Equal("development", config.Metadata["environment"]);
        Assert.Equal("high", config.Metadata["priority"]);
        Assert.IsType<DateTime>(config.Metadata["startDate"]);
        Assert.IsType<string[]>(config.Metadata["tags"]);
    }

    #endregion

    #region AgentConfiguration Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingAgentConfigurationWithDefaultConstructor()
    {
        // Act
        var config = new AgentConfiguration();

        // Assert
        Assert.NotNull(config.Id);
        Assert.Equal(string.Empty, config.Role);
        Assert.Equal(string.Empty, config.Goal);
        Assert.Equal(string.Empty, config.Backstory);
        Assert.NotNull(config.Tools);
        Assert.Empty(config.Tools);
        Assert.True(config.AllowDelegation);
        Assert.Equal(20, config.MaxIterations);
        Assert.Equal(10, config.MaxRPM);
        Assert.False(config.Verbose);
        Assert.Null(config.LlmConfig);
        Assert.Null(config.SystemTemplate);
        Assert.Null(config.PromptTemplate);
        Assert.Null(config.ResponseTemplate);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingAgentConfigurationUsingProperties()
    {
        // Arrange
        var tools = new List<string> { "WebScraper", "FileReader", "Calculator" };
        var llmConfig = LlmConfig.Create(ModelGpt4, "test-key");

        // Act
        var config = new AgentConfiguration
        {
            Id = AgentId.Create(),
            Role = RoleSeniorDeveloper,
            Goal = "Write high-quality, maintainable code",
            Backstory = "Experienced developer with 10+ years in the industry",
            Tools = tools,
            AllowDelegation = false,
            MaxIterations = 50,
            MaxRPM = 20,
            Verbose = true,
            LlmConfig = llmConfig,
            SystemTemplate = "You are a senior developer...",
            PromptTemplate = "Please {action} the following: {input}",
            ResponseTemplate = "Result: {output}"
        };

        // Assert
        Assert.NotNull(config.Id);
        Assert.Equal(RoleSeniorDeveloper, config.Role);
        Assert.Equal("Write high-quality, maintainable code", config.Goal);
        Assert.Equal("Experienced developer with 10+ years in the industry", config.Backstory);
        Assert.Equal(tools, config.Tools);
        Assert.Equal(3, config.Tools.Count);
        Assert.False(config.AllowDelegation);
        Assert.Equal(50, config.MaxIterations);
        Assert.Equal(20, config.MaxRPM);
        Assert.True(config.Verbose);
        Assert.Equal(llmConfig, config.LlmConfig);
        Assert.Equal("You are a senior developer...", config.SystemTemplate);
        Assert.Equal("Please {action} the following: {input}", config.PromptTemplate);
        Assert.Equal("Result: {output}", config.ResponseTemplate);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(100)]
    public void ShouldAcceptVariousValues_WhenUsingAgentConfigurationWithMaxIterations(int maxIterations)
    {
        // Act
        var config = new AgentConfiguration { MaxIterations = maxIterations };

        // Assert
        Assert.Equal(maxIterations, config.MaxIterations);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(60)]
    public void ShouldAcceptVariousValues_WhenUsingAgentConfigurationWithMaxRPM(int maxRPM)
    {
        // Act
        var config = new AgentConfiguration { MaxRPM = maxRPM };

        // Assert
        Assert.Equal(maxRPM, config.MaxRPM);
    }

    #endregion

    #region AgentConfiguration Tools Tests

    [Fact]
    public void ShouldSupportInitialization_WhenUsingAgentConfigurationUsingTools()
    {
        // Act
        var config = new AgentConfiguration
        {
            Tools = ["WebScraper", ToolDatabaseQuery, "EmailSender"]
        };

        // Assert
        Assert.Equal(3, config.Tools.Count);
        Assert.Contains("WebScraper", config.Tools);
        Assert.Contains(ToolDatabaseQuery, config.Tools);
        Assert.Contains("EmailSender", config.Tools);
    }

    [Fact]
    public void ShouldIntegrateCorrectly_WhenUsingAgentConfigurationWithLlmConfig()
    {
        // Arrange
        var llmConfig = LlmConfig.Gpt4(TestApiKey);

        // Act
        var config = new AgentConfiguration { LlmConfig = llmConfig };

        // Assert
        Assert.NotNull(config.LlmConfig);
        Assert.Equal(ModelGpt4, config.LlmConfig.Model);
        Assert.Equal(TestApiKey, config.LlmConfig.ApiKey);
    }

    #endregion

    #region TaskConfiguration Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingTaskConfigurationWithDefaultConstructor()
    {
        // Act
        var config = new TaskConfiguration();

        // Assert
        Assert.NotNull(config.Id);
        Assert.Equal(string.Empty, config.Description);
        Assert.Equal(string.Empty, config.ExpectedOutput);
        Assert.Null(config.AssignedAgentId);
        Assert.NotNull(config.Dependencies);
        Assert.Empty(config.Dependencies);
        Assert.NotNull(config.Context);
        Assert.Empty(config.Context);
        Assert.False(config.AsyncExecution);
        Assert.False(config.HumanInput);
        Assert.Null(config.TimeoutSeconds);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingTaskConfigurationUsingProperties()
    {
        // Arrange
        var dependencies = new List<TaskId> { TaskId.Create(), TaskId.Create() };
        var context = new Dictionary<string, object>
        {
            { "priority", "high" },
            { "environment", "production" },
            { "retries", 3 }
        };

        // Act
        var config = new TaskConfiguration
        {
            Id = TaskId.Create(),
            Description = "Implement authentication system",
            ExpectedOutput = "Secure login functionality with JWT tokens",
            AssignedAgentId = AgentId.Create(),
            Dependencies = dependencies,
            Context = context,
            AsyncExecution = true,
            HumanInput = true,
            TimeoutSeconds = 3600
        };

        // Assert
        Assert.NotNull(config.Id);
        Assert.Equal("Implement authentication system", config.Description);
        Assert.Equal("Secure login functionality with JWT tokens", config.ExpectedOutput);
        Assert.NotNull(config.AssignedAgentId);
        Assert.Equal(dependencies, config.Dependencies);
        Assert.Equal(2, config.Dependencies.Count);
        Assert.Equal(context, config.Context);
        Assert.Equal(3, config.Context.Count);
        Assert.True(config.AsyncExecution);
        Assert.True(config.HumanInput);
        Assert.Equal(3600, config.TimeoutSeconds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(30)]
    [InlineData(300)]
    [InlineData(3600)]
    public void ShouldAcceptNullableValues_WhenUsingTaskConfigurationUsingTimeoutSeconds(int? timeoutSeconds)
    {
        // Act
        var config = new TaskConfiguration { TimeoutSeconds = timeoutSeconds };

        // Assert
        Assert.Equal(timeoutSeconds, config.TimeoutSeconds);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ShouldAcceptValues_WhenUsingTaskConfigurationUsingBooleanFlags(bool asyncExecution, bool humanInput)
    {
        // Act
        var config = new TaskConfiguration
        {
            AsyncExecution = asyncExecution,
            HumanInput = humanInput
        };

        // Assert
        Assert.Equal(asyncExecution, config.AsyncExecution);
        Assert.Equal(humanInput, config.HumanInput);
    }

    #endregion

    #region TaskConfiguration Collections Tests

    [Fact]
    public void ShouldSupportInitialization_WhenUsingTaskConfigurationUsingDependencies()
    {
        // Act
        var dep1 = TaskId.Create();
        var dep2 = TaskId.Create();
        var dep3 = TaskId.Create();
        var config = new TaskConfiguration
        {
            Dependencies = [dep1, dep2, dep3]
        };

        // Assert
        Assert.Equal(3, config.Dependencies.Count);
        Assert.Contains(dep1, config.Dependencies);
        Assert.Contains(dep2, config.Dependencies);
        Assert.Contains(dep3, config.Dependencies);
    }

    [Fact]
    public void ShouldSupportInitialization_WhenUsingTaskConfigurationUsingContext()
    {
        // Act
        var config = new TaskConfiguration
        {
            Context = new Dictionary<string, object>
            {
                { "inputFile", "/path/to/input.json" },
                { "maxAttempts", 5 },
                { "notifyOnComplete", true },
                { "metadata", new { version = "2.0", author = "system" } }
            }
        };

        // Assert
        Assert.Equal(4, config.Context.Count);
        Assert.Equal("/path/to/input.json", config.Context["inputFile"]);
        Assert.Equal(5, config.Context["maxAttempts"]);
        Assert.True((bool)config.Context["notifyOnComplete"]);
        Assert.NotNull(config.Context["metadata"]);
    }

    #endregion

    #region Integration and Complex Scenario Tests

    [Fact]
    public void ShouldMaintainRelationships_WhenUsingCrewConfigurationWithCompleteSetup()
    {
        // Arrange
        var developerId = AgentId.Create();
        var testerId = AgentId.Create();
        var devTaskId = TaskId.Create();
        var testTaskId = TaskId.Create();

        var agent1 = new AgentConfiguration
        {
            Id = developerId,
            Role = "Full Stack Developer",
            Goal = "Develop web applications",
            Tools = ["IDE", "Database", "WebFramework"],
            LlmConfig = LlmConfig.Gpt4("dev-key")
        };

        var agent2 = new AgentConfiguration
        {
            Id = testerId,
            Role = "QA Tester",
            Goal = "Ensure software quality",
            Tools = ["TestFramework", "BugTracker"],
            LlmConfig = LlmConfig.Gpt35Turbo("test-key")
        };

        var task1 = new TaskConfiguration
        {
            Id = devTaskId,
            Description = "Implement user authentication",
            AssignedAgentId = developerId,
            Context = new Dictionary<string, object> { { "framework", "ASP.NET" } }
        };

        var task2 = new TaskConfiguration
        {
            Id = testTaskId,
            Description = "Test authentication functionality",
            AssignedAgentId = testerId,
            Dependencies = [devTaskId],
            Context = new Dictionary<string, object> { { "testType", "integration" } }
        };

        // Act
        var crewConfig = new CrewConfiguration
        {
            Name = "Authentication Development Crew",
            Goal = "Implement and test user authentication",
            Agents = [agent1, agent2],
            Tasks = [task1, task2],
            Process = ProcessType.Sequential,
            Memory = true,
            Planning = true,
            Verbose = true
        };

        // Assert
        Assert.Equal("Authentication Development Crew", crewConfig.Name);
        Assert.Equal(2, crewConfig.Agents.Count);
        Assert.Equal(2, crewConfig.Tasks.Count);

        // Verify agent relationships
        var developer = crewConfig.Agents.First(a => a.Id == developerId);
        var tester = crewConfig.Agents.First(a => a.Id == testerId);
        Assert.Equal("Full Stack Developer", developer.Role);
        Assert.Equal("QA Tester", tester.Role);

        // Verify task relationships
        var devTask = crewConfig.Tasks.First(t => t.Id == devTaskId);
        var testTask = crewConfig.Tasks.First(t => t.Id == testTaskId);
        Assert.Equal(developerId, devTask.AssignedAgentId);
        Assert.Equal(testerId, testTask.AssignedAgentId);
        Assert.Contains(devTaskId, testTask.Dependencies);
    }

    [Fact]
    public void ShouldRequireManager_WhenUsingCrewConfigurationUsingHierarchicalProcess()
    {
        // Arrange
        var managerId = AgentId.Create();
        var managerAgent = new AgentConfiguration
        {
            Id = managerId,
            Role = "Project Manager",
            Goal = "Coordinate team activities"
        };

        // Act
        var crewConfig = new CrewConfiguration
        {
            Process = ProcessType.Hierarchical,
            ManagerAgentId = managerId,
            Agents = [managerAgent]
        };

        // Assert
        Assert.Equal(ProcessType.Hierarchical, crewConfig.Process);
        Assert.NotNull(crewConfig.ManagerAgentId);
        Assert.Contains(crewConfig.Agents, a => a.Id == managerId);
    }

    [Fact]
    public void ShouldHandleToolChain_WhenUsingAgentConfigurationWithMultipleTools()
    {
        // Arrange
        var agent = new AgentConfiguration
        {
            Id = AgentId.Create(),
            Role = "Data Scientist",
            Tools =
            [
                "DataLoader",
                "DataCleaner",
                "StatisticalAnalyzer",
                "MachineLearningModel",
                "Visualizer"
            ]
        };

        // Act & Assert
        Assert.Equal(5, agent.Tools.Count);
        Assert.Contains("DataLoader", agent.Tools);
        Assert.Contains("MachineLearningModel", agent.Tools);
        Assert.Contains("Visualizer", agent.Tools);
    }

    [Fact]
    public void ShouldMaintainOrder_WhenUsingTaskConfigurationWithComplexDependencyChain()
    {
        // Arrange
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var taskId3 = TaskId.Create();
        var taskId4 = TaskId.Create();
        var tasks = new List<TaskConfiguration>
        {
            new TaskConfiguration { Id = taskId1, Description = "Task 1" },
            new TaskConfiguration { Id = taskId2, Description = "Task 2", Dependencies = [taskId1] },
            new TaskConfiguration { Id = taskId3, Description = "Task 3", Dependencies = [taskId2] },
            new TaskConfiguration { Id = taskId4, Description = "Task 4", Dependencies = [taskId2, taskId3] }
        };

        // Act
        var crewConfig = new CrewConfiguration { Tasks = tasks };

        // Assert
        Assert.Equal(4, crewConfig.Tasks.Count);

        var task1 = crewConfig.Tasks.First(t => t.Id == taskId1);
        var task2 = crewConfig.Tasks.First(t => t.Id == taskId2);
        var task3 = crewConfig.Tasks.First(t => t.Id == taskId3);
        var task4 = crewConfig.Tasks.First(t => t.Id == taskId4);

        Assert.Empty(task1.Dependencies);
        Assert.Contains(taskId1, task2.Dependencies);
        Assert.Contains(taskId2, task3.Dependencies);
        Assert.Contains(taskId2, task4.Dependencies);
        Assert.Contains(taskId3, task4.Dependencies);
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldAcceptValues_WhenUsingAgentConfigurationWithNegativeValues()
    {
        // Act
        var config = new AgentConfiguration
        {
            MaxIterations = -1,
            MaxRPM = -5
        };

        // Assert - No validation constraints in the record itself
        Assert.Equal(-1, config.MaxIterations);
        Assert.Equal(-5, config.MaxRPM);
    }

    [Fact]
    public void ShouldAcceptValue_WhenUsingTaskConfigurationWithNegativeTimeout()
    {
        // Act
        var config = new TaskConfiguration { TimeoutSeconds = -100 };

        // Assert
        Assert.Equal(-100, config.TimeoutSeconds);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingCrewConfigurationWithNullCollections()
    {
        // Act
        var config = new CrewConfiguration
        {
            Agents = null!,
            Tasks = null!,
            Metadata = null!
        };

        // Assert
        Assert.Null(config.Agents);
        Assert.Null(config.Tasks);
        Assert.Null(config.Metadata);
    }

    [Fact]
    public void ShouldAcceptNulls_WhenUsingAgentConfigurationWithNullStringProperties()
    {
        // Act
        var config = new AgentConfiguration
        {
            Id = null!,
            Role = null!,
            SystemTemplate = null,
            PromptTemplate = null
        };

        // Assert
        Assert.Null(config.Id);
        Assert.Null(config.Role);
        Assert.Null(config.SystemTemplate);
        Assert.Null(config.PromptTemplate);
    }

    [Fact]
    public void ShouldAcceptNulls_WhenUsingTaskConfigurationWithNullStringProperties()
    {
        // Act
        var config = new TaskConfiguration
        {
            Id = null!,
            Description = null!,
            AssignedAgentId = null
        };

        // Assert
        Assert.Null(config.Id);
        Assert.Null(config.Description);
        Assert.Null(config.AssignedAgentId);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTaskConfigurationWithVeryLargeTimeout()
    {
        // Act
        var config = new TaskConfiguration { TimeoutSeconds = int.MaxValue };

        // Assert
        Assert.Equal(int.MaxValue, config.TimeoutSeconds);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingCrewConfigurationWithUnicodeContent()
    {
        // Act
        var config = new CrewConfiguration
        {
            Name = "开发团队 Development Crew αβγ",
            Goal = "构建优质软件 Build quality software"
        };

        // Assert
        Assert.Contains("开发团队", config.Name);
        Assert.Contains("Development Crew", config.Name);
        Assert.Contains("αβγ", config.Name);
        Assert.Contains("构建优质软件", config.Goal);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingCrewConfigurationToString()
    {
        // Arrange
        var config = new CrewConfiguration { Name = "Test Crew" };

        // Act
        var stringRepresentation = config.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        Assert.Contains("CrewConfiguration", stringRepresentation);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingAgentConfigurationToString()
    {
        // Arrange
        var config = new AgentConfiguration { Id = AgentId.Create() };

        // Act
        var stringRepresentation = config.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        Assert.Contains("AgentConfiguration", stringRepresentation);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingTaskConfigurationToString()
    {
        // Arrange
        var config = new TaskConfiguration { Id = TaskId.Create() };

        // Act
        var stringRepresentation = config.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        Assert.Contains("TaskConfiguration", stringRepresentation);
    }

    #endregion
}

#pragma warning restore CS0618
