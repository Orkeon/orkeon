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
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Application.Tests.Services;

public class CrewConfigurationMapperTests
{
    #region Test Doubles

    private class TestTool : IBaseTool
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
        Assert.Equal(20, configuration.MaxRpm);
    }

    // ── GAP-26, GAP-38: a crew's request rate is exported under its own key ────────────────

    [Fact]
    public void ToConfiguration_ExportsTheCrewsMaxRpmAsItsOwnKey()
    {
        var crew = DomainCrew.Create("Goal", ProcessType.Sequential, maxRpm: 42);

        var configuration = crew.ToConfiguration([], []);

        // A request rate is not a task concurrency: the export wrote it as MaxConcurrentTasks. It is
        // the crew's maxRpm: the key the YAML loader reads back (GAP-38).
        Assert.Equal(42, configuration.MaxRpm);
    }

    [Fact]
    public void ToConfiguration_ExportsNoMaxRpmForACrewWithoutOne()
    {
        var configuration = DomainCrew.Create("Goal", ProcessType.Sequential).ToConfiguration([], []);

        Assert.Null(configuration.MaxRpm);
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
        Assert.Equal(15, configuration.MaxRpm);
        Assert.Equal(TimeoutStandard, configuration.ExecutionConfig.DefaultTimeout);
        Assert.Equal(3, configuration.ExecutionConfig.MaxRetries);
        Assert.False(configuration.ExecutionConfig.EnableDebugMode);
    }

    // ── GAP-19: the export names the LLM the crew's manager runs on ──────────────────────

    private static object ManagerLlmOf(CrewConfiguration configuration) =>
        configuration.ExecutionConfig!.ExecutorSettings["ManagerLlm"];

    [Fact]
    public void ToConfiguration_NamesTheManagerAgentsProfile()
    {
        var chef = new AgentBuilder().Role("Chef").Goal("Lead").WithLlmConfig(LlmConfig.OnProfile("claude")).Build();
        var crew = new Orkeon.Domain.Crew.CrewBuilder().Goal("Ship").Hierarchical(chef).WithAgent(chef).Build();

        Assert.Equal("profile:claude", ManagerLlmOf(crew.ToConfiguration([chef], [])));
    }

    [Fact]
    public void ToConfiguration_NamesTheDefaultProfile_ForAManagerAgentThatNamesNone()
    {
        var chef = new AgentBuilder().Role("Chef").Goal("Lead").Build();
        var crew = new Orkeon.Domain.Crew.CrewBuilder().Goal("Ship").Hierarchical(chef).WithAgent(chef).Build();

        Assert.Equal("profile:default", ManagerLlmOf(crew.ToConfiguration([chef], [])));
    }

    [Fact]
    public void ToConfiguration_NamesTheProviderTheCrewGivesItsManager_WhicheverMode()
    {
        var chef = new AgentBuilder().Role("Chef").Goal("Lead").WithLlmConfig(LlmConfig.OnProfile("claude")).Build();
        var provider = new Orkeon.Tests.Shared.Doubles.StubLlmProvider { Name = "anthropic" };
        var hierarchical = new Orkeon.Domain.Crew.CrewBuilder().Goal("Ship").Hierarchical(chef).WithManagerLlm(provider).WithAgent(chef).Build();
        var autonomous = new Orkeon.Domain.Crew.CrewBuilder().Goal("Ship").Process(ProcessType.Autonomous).WithManagerLlm(provider).WithAgent(chef).Build();

        Assert.Equal("provider:anthropic", ManagerLlmOf(hierarchical.ToConfiguration([chef], [])));
        Assert.Equal("provider:anthropic", ManagerLlmOf(autonomous.ToConfiguration([chef], [])));
    }

    [Fact]
    public void ToConfiguration_NamesNoManagerLlm_ForAModeWithoutAManager()
    {
        var crew = DomainCrew.Create("Goal", ProcessType.Sequential);

        Assert.Equal(string.Empty, ManagerLlmOf(crew.ToConfiguration([], [])));
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
        Assert.Equal(42, developerConfig.MaxRpm);
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
        // GAP-07: a task's own tools are exported by name, like an agent's.
        Assert.Equal(["file_reader"], writeConfig.Tools);

        var reviewConfig = configuration.Tasks[1];
        Assert.Equal(reviewTask.Id, reviewConfig.Id);
        Assert.Equal("Review the feature", reviewConfig.Description);
        Assert.Equal("Review notes", reviewConfig.ExpectedOutput);
        Assert.False(reviewConfig.AsyncExecution);
        Assert.False(reviewConfig.HumanInput);
        Assert.Empty(reviewConfig.Tools);
        var dependency = Assert.Single(reviewConfig.Dependencies);
        Assert.Equal(writeTask.Id, dependency);
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
            .WithTool(new TestTool("file_reader"))
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
}

#pragma warning restore CS0618
