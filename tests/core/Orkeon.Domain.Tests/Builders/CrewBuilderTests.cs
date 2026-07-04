using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Domain.Tests.Builders;

public class CrewBuilderTests
{
    private static DomainAgent CreateAgent(string role = RoleWorker, string goal = "Do work") =>
        new AgentBuilder().Role(role).Goal(goal).Build();

    private static DomainTask CreateTask(string desc = "A task", string output = "Result") =>
        new CrewTaskBuilder().Description(desc).ExpectedOutput(output).Build();

    [Fact]
    public void Build_WithGoalAndAgents_CreatesCrew()
    {
        // Arrange
        var agent1 = CreateAgent("Researcher", "Research topics");
        var agent2 = CreateAgent("Writer", "Write articles");

        // Act
        var crew = new CrewBuilder()
            .Goal("Produce articles")
            .WithAgent(agent1)
            .WithAgent(agent2)
            .Build();

        // Assert
        Assert.NotNull(crew);
        Assert.Equal("Produce articles", crew.Goal.Value);
        Assert.Equal(2, crew.Agents.Count);
    }

    [Fact]
    public void Build_WithoutGoal_ThrowsValidation()
    {
        // Arrange
        var builder = new CrewBuilder().WithAgent(CreateAgent());

        // Act & Assert
        var ex = Assert.Throws<BuilderValidationException>(() => builder.Build());
        Assert.Equal("Crew", ex.BuilderName);
        Assert.Contains("Goal is required", ex.Message);
    }

    [Fact]
    public void Sequential_SetsProcessType()
    {
        // Act
        var crew = new CrewBuilder()
            .Goal("Sequential crew")
            .Sequential()
            .Build();

        // Assert
        Assert.Equal(ProcessType.Sequential, crew.ProcessType);
    }

    [Fact]
    public void Hierarchical_WithManager_SetsManagerAndType()
    {
        // Arrange
        var manager = CreateAgent(RoleManager, "Manage team");

        // Act
        var crew = new CrewBuilder()
            .Goal("Hierarchical crew")
            .Hierarchical(manager)
            .Build();

        // Assert
        Assert.Equal(ProcessType.Hierarchical, crew.ProcessType);
        Assert.NotNull(crew.ManagerAgentId);
        Assert.Equal(manager.Id, crew.ManagerAgentId);
    }

    [Fact]
    public void Hierarchical_WithoutManager_ThrowsValidation()
    {
        // Arrange — hierarchical without manager agent or manager LLM
        var builder = new CrewBuilder()
            .Goal("Hierarchical crew")
            .Hierarchical();

        // Act & Assert
        var ex = Assert.Throws<BuilderValidationException>(() => builder.Build());
        Assert.Equal("Crew", ex.BuilderName);
        Assert.Contains("manager", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithInlineAgents_BuildsAndAdds()
    {
        // Act
        var crew = new CrewBuilder()
            .Goal("Inline agents crew")
            .WithAgent(ab => ab.Role("Coder").Goal(GoalWriteCode))
            .WithAgent(ab => ab.Role("Tester").Goal("Test code"))
            .Build();

        // Assert
        Assert.Equal(2, crew.Agents.Count);
    }

    [Fact]
    public void Build_WithInlineTasks_BuildsAndAdds()
    {
        // Act
        var crew = new CrewBuilder()
            .Goal("Inline tasks crew")
            .WithTask(tb => tb.Description("First task").ExpectedOutput("Output 1"))
            .WithTask(tb => tb.Description("Second task").ExpectedOutput("Output 2"))
            .Build();

        // Assert
        Assert.Equal(2, crew.Tasks.Count);
    }

    [Fact]
    public void Build_MixedInlineAndExplicit_CombinesBoth()
    {
        // Arrange
        var explicitAgent = CreateAgent("Explicit Agent", "Be explicit");
        var explicitTask = CreateTask("Explicit task", "Explicit output");

        // Act
        var crew = new CrewBuilder()
            .Goal("Mixed crew")
            .WithAgent(explicitAgent)
            .WithAgent(ab => ab.Role("Inline Agent").Goal("Be inline"))
            .WithTask(explicitTask)
            .WithTask(tb => tb.Description("Inline task").ExpectedOutput("Inline output"))
            .Build();

        // Assert
        Assert.Equal(2, crew.Agents.Count);
        Assert.Equal(2, crew.Tasks.Count);
    }

    [Fact]
    public void Build_FullConfiguration_SetsAllProperties()
    {
        // Arrange
        var manager = CreateAgent(RoleManager, "Manage");
        var planningLlm = new StubLlmProvider();
        var managerLlm = new StubLlmProvider();
        var stepCallback = new StubStepCallback();
        var taskCallback = new StubTaskCallback();

        // Act
        var crew = new CrewBuilder()
            .Goal("Full config crew")
            .Hierarchical(manager)
            .Verbose()
            .Planning()
            .MaxRpm(200)
            .Language("fr")
            .FullOutput()
            .EnableMemory()
            .ShareCrew(false)
            .OutputLogFile("/tmp/crew.log")
            .WithManagerLlm(managerLlm)
            .WithPlanningLlm(planningLlm)
            .WithStepCallback(stepCallback)
            .WithTaskCallback(taskCallback)
            .Build();

        // Assert
        Assert.Equal("Full config crew", crew.Goal.Value);
        Assert.Equal(ProcessType.Hierarchical, crew.ProcessType);
        Assert.True(crew.Verbose);
        Assert.True(crew.Planning);
        Assert.Equal(200, crew.MaxRpm);
        Assert.Equal("fr", crew.Language.Value);
        Assert.True(crew.FullOutput);
        Assert.True(crew.MemoryEnabled);
        Assert.False(crew.ShareCrew);
        Assert.Equal("/tmp/crew.log", crew.OutputLogFile);
        Assert.Same(managerLlm, crew.ManagerLlm);
        Assert.Equal(manager.Id, crew.ManagerAgentId);
        Assert.Same(planningLlm, crew.PlanningLlm);
        Assert.Same(stepCallback, crew.StepCallback);
        Assert.Same(taskCallback, crew.TaskCallback);
    }

    [Fact]
    public void Build_WithPlanningLlm_EnablesPlanning()
    {
        // Arrange
        var planningLlm = new StubLlmProvider();

        // Act
        var crew = new CrewBuilder()
            .Goal("Planning crew")
            .Planning()
            .WithPlanningLlm(planningLlm)
            .Build();

        // Assert
        Assert.True(crew.Planning);
        Assert.Same(planningLlm, crew.PlanningLlm);
    }

    [Fact]
    public void Build_WithBulkAgentsAndTasks_AddsAll()
    {
        // Arrange
        var agents = new[]
        {
            CreateAgent("Agent1", "Goal1"),
            CreateAgent("Agent2", "Goal2"),
            CreateAgent("Agent3", "Goal3")
        };
        var tasks = new[]
        {
            CreateTask("Task1", "Output1"),
            CreateTask("Task2", "Output2")
        };

        // Act
        var crew = new CrewBuilder()
            .Goal("Bulk crew")
            .WithAgents(agents)
            .WithTasks(tasks)
            .Build();

        // Assert
        Assert.Equal(3, crew.Agents.Count);
        Assert.Equal(2, crew.Tasks.Count);
    }

    #region Test Doubles

    private sealed class StubLlmProvider : ILlmProvider
    {
        public string Name => "StubLlm";

        public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub" });

        public System.Threading.Tasks.Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub" });
    }

    private sealed class StubStepCallback : IStepCallback
    {
        public System.Threading.Tasks.Task OnStepStartAsync(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, int iteration)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task OnStepCompletedAsync(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, int iteration, Orkeon.Domain.Agent.AgentStep step)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task OnStepFailedAsync(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, int iteration, string error)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    private sealed class StubTaskCallback : ITaskCallback
    {
        public System.Threading.Tasks.Task OnTaskStartAsync(Orkeon.Domain.Task.ICrewTask task)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task OnTaskCompletedAsync(Orkeon.Domain.Task.ICrewTask task, TaskOutput output)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task OnTaskFailedAsync(Orkeon.Domain.Task.ICrewTask task, string error)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    #endregion
}
