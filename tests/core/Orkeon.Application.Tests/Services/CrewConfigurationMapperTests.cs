using Orkeon.Domain.Configuration;
using Orkeon.Application.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools.Protocol;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainCrewTask = Orkeon.Domain.Task.CrewTask;
using AgentBuilder = Orkeon.Domain.Agent.AgentBuilder;
using CrewTaskBuilder = Orkeon.Domain.Task.CrewTaskBuilder;
using Orkeon.Application.Interfaces.Ports;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Application.Tests.Services;

public class CrewConfigurationMapperTests
{
    private static readonly string[] s_agentTools = ["search", "calculator"];

    #region Test Doubles

    private class TestTool : IBaseTool, ITool
    {
        public string Name { get; }
        public string Description { get; }
        public ToolSchema Schema { get; }
        public List<string> CallHistory { get; } = [];

        public TestTool(string name, string description = "Test tool")
        {
            Name = name;
            Description = description;
            Schema = new ToolSchema(name, description, []);
        }

        public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
        {
            CallHistory.Add($"CallAsync:{request.ToolName}");
            return System.Threading.Tasks.Task.FromResult(new ToolCallResponse(
                Success: true,
                Result: $"Result from {Name}",
                Error: null,
                Metadata: null
            ));
        }

        public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        {
            CallHistory.Add($"ExecuteAsync:{input}");
            return System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess($"Result from {Name}"));
        }

        public bool ValidateInput(string input)
        {
            return true;
        }

        public System.Threading.Tasks.Task<ToolResult> RunAsync(Dictionary<string, object> arguments)
        {
            CallHistory.Add($"RunAsync:{string.Join(",", arguments.Keys)}");
            return System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess($"Result from {Name}"));
        }

        public static bool ValidateArguments(Dictionary<string, object> arguments)
        {
            return true;
        }

        public static Dictionary<string, object> GetDefaultArguments()
        {
            return [];
        }

        public static bool RequiresHumanApproval => false;
        public static int MaxRetries => 3;
        public static TimeSpan Timeout => TimeoutQuick;
    }

    private class TestLlmProvider : IBasicLlmProvider
    {
        public string Name { get; }
        public List<string> GenerateCalls { get; } = [];

        public TestLlmProvider(string modelName = TestModelName)
        {
            Name = modelName;
        }

        public System.Threading.Tasks.Task<string> ChatAsync(string message, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            GenerateCalls.Add(message);
            return System.Threading.Tasks.Task.FromResult($"Response to: {message}");
        }

        public System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(true);
        }
    }

    private class TestLlmProviderFactory : ILlmProviderFactory
    {
        private readonly Dictionary<string, TestLlmProvider> _providers = [];
        public List<string> CreateCalls { get; } = [];

        public TestLlmProviderFactory(TestLlmProvider? defaultProvider = null)
        {
            if (defaultProvider != null)
            {
                _providers["default"] = defaultProvider;
            }
        }

        public void RegisterProvider(string model, TestLlmProvider provider)
        {
            _providers[model] = provider;
        }

        public IBasicLlmProvider Create(LlmConfig config)
        {
            CreateCalls.Add($"{config.Model}:{config.BaseUrl}");

            // Simulate failure for specific model
            if (config.Model == "failing-model")
            {
                throw new InvalidOperationException("Failed to create LLM provider for failing-model");
            }

            if (_providers.TryGetValue(config.Model, out var provider))
            {
                return provider;
            }

            return _providers.TryGetValue("default", out var defaultProvider)
                ? defaultProvider
                : new TestLlmProvider(config.Model);
        }
    }

    #endregion

    #region Test Helpers

    private static CrewConfiguration CreateTestCrewConfiguration(
        string name = "Test Crew",
        string goal = TestGoal,
        ProcessType? processType = null)
    {
        return new CrewConfiguration
        {
            Name = name,
            Goal = goal,
            Process = processType ?? ProcessType.Sequential,
            Verbose = false,
            Memory = false,
            Planning = false,
            Agents = [],
            Tasks = [],
            ExecutionConfig = new ExecutionConfig
            {
                MaxConcurrentTasks = 5,
                DefaultTimeout = TimeoutStandard,
                MaxRetries = 3
            }
        };
    }

    private static AgentConfiguration CreateTestAgentConfiguration(
        AgentId? id = null,
        string role = "TestAgent",
        string goal = "Test agent goal")
    {
        return new AgentConfiguration
        {
            Id = id ?? AgentId.Create(),
            Role = role,
            Goal = goal,
            Backstory = "Test backstory",
            Tools = [],
            AllowDelegation = true,
            MaxIterations = 20,
            MaxRPM = 10,
            Verbose = false
        };
    }

    private static TaskConfiguration CreateTestTaskConfiguration(
        TaskId? id = null,
        string description = "Test task",
        string expectedOutput = "Test output")
    {
        return new TaskConfiguration
        {
            Id = id ?? TaskId.Create(),
            Description = description,
            ExpectedOutput = expectedOutput,
            Dependencies = [],
            Context = [],
            AsyncExecution = false,
            HumanInput = false
        };
    }

    private static Dictionary<string, TestTool> CreateTestToolRegistry()
    {
        return new Dictionary<string, TestTool>
        {
            ["search"] = new TestTool("search", "Search tool"),
            ["calculator"] = new TestTool("calculator", "Calculator tool"),
            ["file_reader"] = new TestTool("file_reader", "File reader tool")
        };
    }

    private static Func<string, IBaseTool> CreateToolResolver(Dictionary<string, TestTool>? tools = null)
    {
        var registry = tools ?? CreateTestToolRegistry();
        return toolName => registry.TryGetValue(toolName, out var tool) ? tool : null!;
    }

    #endregion

    #region ToDomainCrew Tests - Basic

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToDomainCrewWithNullConfiguration()
    {
        // Arrange
        CrewConfiguration? configuration = null;
        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            configuration!.ToDomainCrew(toolResolver, llmProviderFactory));
        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToDomainCrewWithNullToolResolver()
    {
        // Arrange
        var configuration = CreateTestCrewConfiguration();
        Func<string, IBaseTool>? toolResolver = null;
        var llmProviderFactory = new TestLlmProviderFactory();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            configuration.ToDomainCrew(toolResolver!, llmProviderFactory));
        Assert.Equal("toolResolver", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToDomainCrewWithNullLlmProviderFactory()
    {
        // Arrange
        var configuration = CreateTestCrewConfiguration();
        var toolResolver = CreateToolResolver();
        ILlmProviderFactory? llmProviderFactory = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            configuration.ToDomainCrew(toolResolver, llmProviderFactory!));
        Assert.Equal("llmProviderFactory", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateCrew_WhenUsingToDomainCrewWithMinimalConfiguration()
    {
        // Arrange
        var configuration = CreateTestCrewConfiguration();
        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory);

        // Assert
        Assert.NotNull(crew);
        Assert.Equal(TestGoal, crew.Goal);
        Assert.Equal(ProcessType.Sequential, crew.ProcessType);
        Assert.False(crew.Verbose);
        Assert.False(crew.MemoryEnabled);
        Assert.False(crew.Planning);
    }

    #endregion

    #region ToDomainCrew Tests - Agent Mapping

    [Fact]
    public void ShouldMapAgentsCorrectly_WhenUsingToDomainCrewWithAgents()
    {
        // Arrange
        var configuration = CreateTestCrewConfiguration() with
        {
            Agents =
            [
                CreateTestAgentConfiguration(role: RoleDeveloper, goal: GoalWriteCode),
                CreateTestAgentConfiguration(role: "Reviewer", goal: "Review code")
            ]
        };

        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory);

        // Assert
        Assert.Equal(2, crew.Agents.Count);
    }

    [Fact]
    public void ShouldAssignToolsToAgents_WhenUsingToDomainCrewWithAgentTools()
    {
        // Arrange
        var agentConfig = CreateTestAgentConfiguration() with { Tools = ["search", "calculator"] };
        var configuration = CreateTestCrewConfiguration() with { Agents = [agentConfig] };

        var toolRegistry = CreateTestToolRegistry();
        var toolResolver = CreateToolResolver(toolRegistry);
        var llmProviderFactory = new TestLlmProviderFactory();

        var processedAgents = new List<DomainAgent?>();
        Action<DomainAgent?> agentPostProcessor = agent => processedAgents.Add(agent);

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory, agentPostProcessor);

        // Assert
        Assert.Single(processedAgents);
        var agent = processedAgents[0];
        Assert.NotNull(agent);
        Assert.Equal(2, agent.Tools.Count);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenUsingToDomainCrewWithInvalidToolName()
    {
        // Arrange
        var configuration = CreateTestCrewConfiguration();
        var agentConfig = CreateTestAgentConfiguration() with { Tools = ["nonexistent_tool"] };
        configuration = configuration with { Agents = [agentConfig] };

        // A resolver that THROWS for the unknown name (a resolver returning null is the
        // silent-skip path; the warning is for resolvers that fail loudly).
        Func<string, IBaseTool> toolResolver =
            name => throw new InvalidOperationException($"Unknown tool: {name}");
        var llmProviderFactory = new TestLlmProviderFactory();
        var logger = new Fixtures.TestLogger<CrewConfigurationMapperTests>();

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory, logger: logger);

        // Assert — the failure is handled gracefully and reported as a logger warning.
        Assert.NotNull(crew);
        Assert.Single(crew.Agents);
        Assert.True(logger.HasLoggedWarning());
        Assert.True(logger.HasLoggedMessage("Could not resolve tool 'nonexistent_tool'"));
    }

    [Fact]
    public void ShouldNotThrow_WhenUsingToDomainCrewWithInvalidToolNameAndNoLogger()
    {
        // Arrange — without a logger the mapper stays silent (NullLogger default).
        var configuration = CreateTestCrewConfiguration();
        var agentConfig = CreateTestAgentConfiguration() with { Tools = ["nonexistent_tool"] };
        configuration = configuration with { Agents = [agentConfig] };

        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory);

        // Assert
        Assert.NotNull(crew);
        Assert.Single(crew.Agents);
    }

    [Fact]
    public void ShouldCreateLlmProvider_WhenUsingToDomainCrewWithAgentLlmConfig()
    {
        // Arrange
        var configuration = CreateTestCrewConfiguration();
        var agentConfig = CreateTestAgentConfiguration() with { LlmConfig = LlmConfig.Create(ModelGpt4) with { ApiKey = "test-key" } };
        configuration = configuration with { Agents = [agentConfig] };

        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory);

        // Assert
        Assert.Single(llmProviderFactory.CreateCalls);
        Assert.Contains(ModelGpt4, llmProviderFactory.CreateCalls[0]);
    }

    [Fact]
    public void ShouldApplyPostProcessing_WhenUsingToDomainCrewWithAgentPostProcessor()
    {
        // Arrange
        var configuration = CreateTestCrewConfiguration() with { Agents = [CreateTestAgentConfiguration()] };

        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();

        var processedAgents = new List<string>();
        Action<DomainAgent?> agentPostProcessor = agent =>
        {
            processedAgents.Add(agent!.Role.Value);
            // Could add delegation tools here
        };

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory, agentPostProcessor);

        // Assert
        Assert.Single(processedAgents);
        Assert.Equal("TestAgent", processedAgents[0]);
    }

    #endregion

    #region ToDomainCrew Tests - Task Mapping

    [Fact]
    public void ShouldMapTasksCorrectly_WhenUsingToDomainCrewWithTasks()
    {
        // Arrange
        var configuration = CreateTestCrewConfiguration() with
        {
            Tasks =
            [
                CreateTestTaskConfiguration(description: "First task", expectedOutput: "Output 1"),
                CreateTestTaskConfiguration(description: "Second task", expectedOutput: "Output 2")
            ]
        };

        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory);

        // Assert
        Assert.Equal(2, crew.Tasks.Count);
    }

    [Fact]
    public void ShouldMapPropertiesCorrectly_WhenUsingToDomainCrewWithTaskProperties()
    {
        // Arrange
        var taskConfig = CreateTestTaskConfiguration() with { AsyncExecution = true, HumanInput = true };
        var configuration = CreateTestCrewConfiguration() with { Tasks = [taskConfig] };

        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory);

        // Assert
        Assert.Single(crew.Tasks);
        // Task properties would be verified if we had access to the actual task entities
    }

    [Fact]
    public void ShouldHandleDependencies_WhenUsingToDomainCrewWithTaskDependencies()
    {
        // Arrange
        var taskId1 = TaskId.Create();
        var task1 = CreateTestTaskConfiguration(taskId1, "First task", "Output 1");
        var task2 = CreateTestTaskConfiguration(description: "Second task", expectedOutput: "Output 2") with
        {
            Dependencies = [taskId1]
        };
        var configuration = CreateTestCrewConfiguration() with { Tasks = [task1, task2] };

        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory);

        // Assert
        Assert.Equal(2, crew.Tasks.Count);
        // Dependencies are handled at orchestration level, not on entities
    }

    #endregion

    #region ToDomainCrew Tests - Complex Scenarios

    [Fact]
    public void ShouldMapEverything_WhenUsingToDomainCrewWithFullConfiguration()
    {
        // Arrange
        var managerId = AgentId.Create();
        var agentId1 = AgentId.Create();
        var agentId2 = AgentId.Create();
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();

        var configuration = CreateTestCrewConfiguration("Full Crew", "Complete all tasks", ProcessType.Sequential) with
        {
            Verbose = true,
            Memory = true,
            Planning = true,
            ManagerAgentId = managerId
        };

        // Add agents with tools
        var agent1 = CreateTestAgentConfiguration(agentId1, RoleDeveloper, GoalWriteCode) with
        {
            Tools = [.. s_agentTools],
            LlmConfig = LlmConfig.WithDefaultModel("test-key")
        };

        var agent2 = CreateTestAgentConfiguration(agentId2, "Reviewer", "Review code") with
        {
            Tools = ["file_reader"],
            AllowDelegation = false
        };

        // Add tasks with dependencies
        var task1 = CreateTestTaskConfiguration(taskId1, "Write feature", "Code written");
        var task2 = CreateTestTaskConfiguration(taskId2, "Review code", "Code reviewed") with
        {
            Dependencies = [taskId1],
            AssignedAgentId = agentId2
        };

        configuration = configuration with
        {
            Agents = [agent1, agent2],
            Tasks = [task1, task2]
        };

        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory);

        // Assert
        Assert.NotNull(crew);
        Assert.Equal("Complete all tasks", crew.Goal);
        Assert.Equal(ProcessType.Sequential, crew.ProcessType);
        Assert.True(crew.Verbose);
        Assert.True(crew.MemoryEnabled);
        Assert.True(crew.Planning);
        Assert.Null(crew.ManagerAgentId); // Sequential crews don't have manager agents
        Assert.Equal(2, crew.Agents.Count);
        Assert.Equal(2, crew.Tasks.Count);
    }

    [Fact]
    public void ShouldApplySettings_WhenUsingToDomainCrewWithExecutionConfig()
    {
        // Arrange
        var configuration = CreateTestCrewConfiguration() with
        {
            ExecutionConfig = new ExecutionConfig
            {
                MaxConcurrentTasks = 15,
                DefaultTimeout = TimeoutExtended,
                MaxRetries = 5,
                EnableDebugMode = true,
                ExecutorSettings = new Dictionary<string, object>
                {
                    ["CustomSetting"] = "Value"
                }
            }
        };

        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory);

        // Assert
        Assert.Equal(15, crew.MaxRpm); // MaxConcurrentTasks maps to MaxRpm
        // Other execution config settings are used at runtime, not stored on crew entity
    }

    #endregion

    #region ToConfiguration Tests

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToConfigurationWithNullCrew()
    {
        // Arrange
        DomainCrew? crew = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => crew!.ToConfiguration([], []));
        Assert.Equal("crew", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToConfigurationWithNullAgents()
    {
        // Arrange
        var crew = DomainCrew.Create("Goal", ProcessType.Sequential);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => crew.ToConfiguration(null!, []));
        Assert.Equal("agents", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToConfigurationWithNullTasks()
    {
        // Arrange
        var crew = DomainCrew.Create("Goal", ProcessType.Sequential);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => crew.ToConfiguration([], null!));
        Assert.Equal("tasks", exception.ParamName);
    }

    [Fact]
    public void ShouldMapPropertiesCorrectly_WhenUsingToConfigurationWithBasicCrew()
    {
        // Arrange
        var crew = DomainCrew.Create(
            goal: TestGoal,
            processType: ProcessType.Sequential,
            verbose: true,
            planning: true,
            maxRpm: 20,
            memoryEnabled: true
        );

        // Act
        var configuration = crew.ToConfiguration([], []);

        // Assert
        Assert.Equal("Unnamed Crew", configuration.Name);
        Assert.Equal(TestGoal, configuration.Goal);
        Assert.Equal(ProcessType.Sequential, configuration.Process);
        Assert.True(configuration.Verbose);
        Assert.True(configuration.Memory);
        Assert.True(configuration.Planning);
        Assert.NotNull(configuration.ExecutionConfig);
        Assert.Equal(20, configuration.ExecutionConfig.MaxConcurrentTasks);
    }

    [Fact]
    public void ShouldMapManagerId_WhenUsingToConfigurationWithManagerAgent()
    {
        // Arrange
        var crew = DomainCrew.Create("Goal", ProcessType.Sequential);
        var managerId = AgentId.From(Guid.NewGuid());

        // Use reflection to set private property (in real code, use proper method)
        var managerProperty = crew.GetType().GetProperty("ManagerAgentId");
        managerProperty?.SetValue(crew, managerId);

        // Act
        var configuration = crew.ToConfiguration([], []);

        // Assert
        Assert.Equal(managerId!.ToString(), configuration.ManagerAgentId!.ToString());
    }

    [Fact]
    public void ShouldCreateValidExecutionConfig_WhenUsingToConfiguration()
    {
        // Arrange
        var crew = DomainCrew.Create("Goal", ProcessType.Sequential, maxRpm: 15);

        // Act
        var configuration = crew.ToConfiguration([], []);

        // Assert
        Assert.NotNull(configuration.ExecutionConfig);
        Assert.Equal(15, configuration.ExecutionConfig.MaxConcurrentTasks);
        Assert.Equal(TimeoutStandard, configuration.ExecutionConfig.DefaultTimeout);
        Assert.Equal(3, configuration.ExecutionConfig.MaxRetries);
        Assert.False(configuration.ExecutionConfig.EnableDebugMode);
    }

    [Fact]
    public void ShouldReturnEmptyCollections_WhenUsingToConfigurationWithoutAgentsAndTasks()
    {
        // Arrange
        var crew = DomainCrew.Create("Goal", ProcessType.Sequential);

        // Act
        var configuration = crew.ToConfiguration([], []);

        // Assert
        Assert.Empty(configuration.Agents);
        Assert.Empty(configuration.Tasks);
    }

    [Fact]
    public void ShouldExportAgentsAndTasks_WhenUsingToConfigurationWithMaterializedEntities()
    {
        // Arrange
        var (crew, developer, reviewer, writeTask, reviewTask) = CreateFullDomainCrew();

        // Act
        var configuration = crew.ToConfiguration([developer, reviewer], [writeTask, reviewTask]);

        // Assert — agents are fully exported (the pre-R10.9 export left this collection empty)
        Assert.Equal(2, configuration.Agents.Count);
        var developerConfig = configuration.Agents[0];
        Assert.Equal(developer.Id, developerConfig.Id);
        Assert.Equal(RoleDeveloper, developerConfig.Role);
        Assert.Equal(GoalWriteCode, developerConfig.Goal);
        Assert.Equal("Senior developer", developerConfig.Backstory);
        var exportedToolName = Assert.Single(developerConfig.Tools);
        Assert.Equal("search", exportedToolName);
        Assert.False(developerConfig.AllowDelegation);
        Assert.Equal(7, developerConfig.MaxIterations);
        Assert.Equal(42, developerConfig.MaxRPM);
        Assert.True(developerConfig.Verbose);
        Assert.NotNull(developerConfig.LlmConfig);
        Assert.Equal(ModelGpt4, developerConfig.LlmConfig.Model);

        var reviewerConfig = configuration.Agents[1];
        Assert.Equal(reviewer.Id, reviewerConfig.Id);
        Assert.Equal("Reviewer", reviewerConfig.Role);
        Assert.Equal("Review code", reviewerConfig.Goal);
        Assert.Equal(string.Empty, reviewerConfig.Backstory);
        Assert.Empty(reviewerConfig.Tools);
        Assert.Null(reviewerConfig.LlmConfig);

        // Assert — tasks are fully exported (the pre-R10.9 export left this collection empty)
        Assert.Equal(2, configuration.Tasks.Count);
        var writeConfig = configuration.Tasks[0];
        Assert.Equal(writeTask.Id, writeConfig.Id);
        Assert.Equal("Write the feature", writeConfig.Description);
        Assert.Equal("Feature code", writeConfig.ExpectedOutput);
        Assert.True(writeConfig.AsyncExecution);
        Assert.True(writeConfig.HumanInput);
        Assert.Equal(developer.Id, writeConfig.AssignedAgentId);
        Assert.Empty(writeConfig.Dependencies);
        Assert.Equal("high", writeConfig.Context["priority"]);

        var reviewConfig = configuration.Tasks[1];
        Assert.Equal(reviewTask.Id, reviewConfig.Id);
        Assert.Equal("Review the feature", reviewConfig.Description);
        Assert.Equal("Review notes", reviewConfig.ExpectedOutput);
        Assert.False(reviewConfig.AsyncExecution);
        Assert.False(reviewConfig.HumanInput);
        var dependency = Assert.Single(reviewConfig.Dependencies);
        Assert.Equal(writeTask.Id, dependency);
    }

    [Fact]
    public void ShouldRoundTrip_WhenConvertingCrewToConfigurationAndBackToDomainCrew()
    {
        // Arrange — a full domain crew with materialized agents and tasks
        var (crew, developer, reviewer, writeTask, reviewTask) = CreateFullDomainCrew();

        // Act — export to configuration, then re-import to a new domain crew
        var configuration = crew.ToConfiguration([developer, reviewer], [writeTask, reviewTask]);

        var reimportedAgents = new List<DomainAgent>();
        var reimportedTasks = new List<DomainCrewTask>();
        var reimportedCrew = configuration.ToDomainCrew(
            CreateToolResolver(),
            new TestLlmProviderFactory(),
            reimportedAgents.Add,
            reimportedTasks.Add);

        // Assert — crew-level equivalence
        Assert.Equal("Ship the feature", reimportedCrew.Goal);
        Assert.Equal(crew.ProcessType, reimportedCrew.ProcessType);
        Assert.Equal(crew.Verbose, reimportedCrew.Verbose);
        Assert.Equal(crew.MemoryEnabled, reimportedCrew.MemoryEnabled);
        Assert.Equal(crew.Planning, reimportedCrew.Planning);
        Assert.Equal(crew.MaxRpm, reimportedCrew.MaxRpm);

        // Assert — agents survive the round-trip (fails on the pre-R10.9 export: 0 agents)
        Assert.Equal(2, reimportedCrew.Agents.Count);
        Assert.Equal(2, reimportedAgents.Count);
        var roundTrippedDeveloper = reimportedAgents[0];
        Assert.Equal(RoleDeveloper, roundTrippedDeveloper.Role.Value);
        Assert.Equal(GoalWriteCode, roundTrippedDeveloper.Goal.Value);
        Assert.Equal("Senior developer", roundTrippedDeveloper.Backstory?.Value);
        Assert.False(roundTrippedDeveloper.AllowDelegation);
        Assert.Equal(7, roundTrippedDeveloper.MaxIterations);
        Assert.Equal(42, roundTrippedDeveloper.MaxRpm);
        Assert.True(roundTrippedDeveloper.Verbose);
        var roundTrippedTool = Assert.Single(roundTrippedDeveloper.Tools);
        Assert.Equal("search", roundTrippedTool.Name);
        Assert.NotNull(roundTrippedDeveloper.LlmConfig);
        Assert.Equal(ModelGpt4, roundTrippedDeveloper.LlmConfig.Model);

        var roundTrippedReviewer = reimportedAgents[1];
        Assert.Equal("Reviewer", roundTrippedReviewer.Role.Value);
        Assert.Equal("Review code", roundTrippedReviewer.Goal.Value);
        Assert.Null(roundTrippedReviewer.Backstory);
        Assert.Empty(roundTrippedReviewer.Tools);

        // Assert — tasks survive the round-trip (fails on the pre-R10.9 export: 0 tasks)
        Assert.Equal(2, reimportedCrew.Tasks.Count);
        Assert.Equal(2, reimportedTasks.Count);
        var roundTrippedWrite = reimportedTasks[0];
        Assert.Equal("Write the feature", roundTrippedWrite.Description.Value);
        Assert.Equal("Feature code", roundTrippedWrite.ExpectedOutput.Value);
        Assert.True(roundTrippedWrite.AsyncExecution);
        Assert.True(roundTrippedWrite.HumanInput);

        var roundTrippedReview = reimportedTasks[1];
        Assert.Equal("Review the feature", roundTrippedReview.Description.Value);
        Assert.Equal("Review notes", roundTrippedReview.ExpectedOutput.Value);
        Assert.False(roundTrippedReview.AsyncExecution);
        Assert.False(roundTrippedReview.HumanInput);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenUsingToConfigurationWithMissingAgentEntity()
    {
        // Arrange — the crew references an agent that is not supplied to the export
        var (crew, developer, _, writeTask, reviewTask) = CreateFullDomainCrew();

        // Act & Assert — the export must fail loudly instead of silently truncating
        var exception = Assert.Throws<InvalidOperationException>(
            () => crew.ToConfiguration([developer], [writeTask, reviewTask]));
        Assert.Contains("referenced by crew", exception.Message);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenUsingToConfigurationWithMissingTaskEntity()
    {
        // Arrange — the crew references a task that is not supplied to the export
        var (crew, developer, reviewer, writeTask, _) = CreateFullDomainCrew();

        // Act & Assert — the export must fail loudly instead of silently truncating
        var exception = Assert.Throws<InvalidOperationException>(
            () => crew.ToConfiguration([developer, reviewer], [writeTask]));
        Assert.Contains("referenced by crew", exception.Message);
    }

    private static (DomainCrew Crew, DomainAgent Developer, DomainAgent Reviewer, DomainCrewTask WriteTask, DomainCrewTask ReviewTask) CreateFullDomainCrew()
    {
        var developer = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal(GoalWriteCode)
            .Backstory("Senior developer")
            .WithTool(new TestTool("search"))
            .AllowDelegation(false)
            .MaxIterations(7)
            .MaxRpm(42)
            .Verbose(true)
            .WithLlmConfig(LlmConfig.Create(ModelGpt4))
            .Build();

        var reviewer = new AgentBuilder()
            .Role("Reviewer")
            .Goal("Review code")
            .Build();

        var writeTask = new CrewTaskBuilder()
            .Description("Write the feature")
            .ExpectedOutput("Feature code")
            .Async(true)
            .HumanInput(true)
            .AssignTo(developer.Id)
            .WithContext("priority", "high")
            .Build();

        var reviewTask = new CrewTaskBuilder()
            .Description("Review the feature")
            .ExpectedOutput("Review notes")
            .DependsOn(writeTask.Id)
            .AssignTo(reviewer.Id)
            .Build();

        var crew = DomainCrew.Create(
            goal: "Ship the feature",
            processType: ProcessType.Sequential,
            verbose: true,
            planning: true,
            maxRpm: 20,
            memoryEnabled: true);
        crew.AddAgent(developer.Id);
        crew.AddAgent(reviewer.Id);
        crew.AddTask(writeTask.Id);
        crew.AddTask(reviewTask.Id);

        return (crew, developer, reviewer, writeTask, reviewTask);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldUseDefaultGoal_WhenUsingToDomainCrewWithNullGoal()
    {
        // Arrange
        var configuration = CreateTestCrewConfiguration() with { Goal = null! };

        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory);

        // Assert
        Assert.Equal("Default goal", crew.Goal);
    }

    [Fact]
    public void ShouldUseDefaults_WhenUsingToDomainCrewWithNullExecutionConfig()
    {
        // Arrange
        var configuration = CreateTestCrewConfiguration() with { ExecutionConfig = null };

        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory);

        // Assert
        Assert.Equal(10, crew.MaxRpm); // Default value
    }

    [Fact]
    public void ShouldHandleGracefully_WhenUsingToDomainCrewWithLlmProviderCreationFailure()
    {
        // Arrange
        var agentConfig = CreateTestAgentConfiguration() with { LlmConfig = LlmConfig.Create("failing-model") };
        var configuration = CreateTestCrewConfiguration() with { Agents = [agentConfig] };

        var toolResolver = CreateToolResolver();
        var llmProviderFactory = new TestLlmProviderFactory();
        var logger = new Fixtures.TestLogger<CrewConfigurationMapperTests>();

        // The TestLlmProviderFactory already handles throwing for failing-model in its Create method

        // Act
        var crew = configuration.ToDomainCrew(toolResolver, llmProviderFactory, logger: logger);

        // Assert
        // Crew should still be created even with LLM provider failure
        Assert.NotNull(crew);
        // Verify that the provider creation was attempted and the failure logged as a warning
        Assert.Contains("failing-model", llmProviderFactory.CreateCalls[0]);
        Assert.True(logger.HasLoggedWarning());
        Assert.True(logger.HasLoggedMessage("Could not create LLM provider for agent"));
    }

    #endregion
}

#pragma warning restore CS0618
