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
    public void Name_IsCarriedToTheCrew_Trimmed()
    {
        // GAP-20: the name scopes the crew's long-term memory.
        var crew = new CrewBuilder().Name(" legal-watch ").Goal("Watch the law").WithAgent(CreateAgent()).Build();

        Assert.Equal("legal-watch", crew.Name);
    }

    [Fact]
    public void A_crew_built_without_a_name_has_none()
    {
        var crew = new CrewBuilder().Goal("Watch the law").WithAgent(CreateAgent()).Build();

        Assert.Null(crew.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_RejectsABlankName(string name)
    {
        Assert.Throws<ArgumentException>(() => new CrewBuilder().Name(name));
    }

    [Fact]
    public void A_blank_name_in_the_options_leaves_the_crew_unnamed()
    {
        var crew = Orkeon.Domain.Crew.Crew.Create(new CrewCreateOptions { Goal = "Watch the law", Name = "  " });

        Assert.Null(crew.Name);
    }

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

    /// <summary>
    /// GAP-22 — a task built with <c>.Async()</c> runs alongside the next ones in a sequential crew;
    /// the modes that order their tasks themselves would ignore it, so the builder refuses it there,
    /// naming the task, rather than build a crew that silently drops the promise.
    /// </summary>
    [Theory]
    [InlineData("Hierarchical")]
    [InlineData("Consensual")]
    [InlineData("Graph")]
    [InlineData("Autonomous")]
    public void Build_RefusesAnAsyncTask_InAModeThatOrdersItsTasksItself(string process)
    {
        var manager = CreateAgent("Manager", "Lead");
        var builder = new CrewBuilder()
            .Goal("Watch the market")
            .Process(ProcessType.From(process))
            .WithManager(manager)
            .WithAgent(CreateAgent())
            .WithTask(CreateTask("Write the report"))
            .WithTask(t => t.Description("Gather the facts").ExpectedOutput("The facts").Async());

        var ex = Assert.Throws<BuilderValidationException>(() => builder.Build());

        Assert.Contains("'Gather the facts'", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'Write the report'", ex.Message, StringComparison.Ordinal);
        Assert.Contains(process, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Sequential", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Parallel", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Sequential", true)]
    [InlineData("Parallel", true)]
    [InlineData("Graph", false)]
    public void Build_AcceptsAnAsyncTask_WhereTheModeHonoursIt_AndASynchronousOneEverywhere(string process, bool asyncExecution)
    {
        var crew = new CrewBuilder()
            .Goal("Watch the market")
            .Process(ProcessType.From(process))
            .WithAgent(CreateAgent())
            .WithTask(t => t.Description("Gather the facts").ExpectedOutput("The facts").Async(asyncExecution))
            .Build();

        Assert.Single(crew.Tasks);
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

    #endregion
}
