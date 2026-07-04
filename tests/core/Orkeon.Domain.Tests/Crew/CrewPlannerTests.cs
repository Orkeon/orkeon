using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.Interfaces;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Parsing;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Domain.Tests.Crew;

public class CrewPlannerTests
{
    // Shared parser instance (stateless, safe to reuse)
    private static readonly IExecutionPlanParser s_parser = new ExecutionPlanParser();

    // Test doubles
    private class TestLlmProvider : ILlmProvider
    {
        private readonly string _mockResponse;
        private LlmConfig? _capturedConfig;

        public TestLlmProvider(string mockResponse = "")
        {
            _mockResponse = mockResponse;
        }

        public string Name => "TestProvider";

        public LlmConfig? CapturedConfig => _capturedConfig;

        public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            _capturedConfig = config;
            return System.Threading.Tasks.Task.FromResult(new LlmResponse
            {
                Content = _mockResponse,
                Model = TestModelName,
                TokensUsed = 100
            });
        }

        public System.Threading.Tasks.Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            _capturedConfig = config;
            return System.Threading.Tasks.Task.FromResult(new LlmResponse
            {
                Content = _mockResponse,
                Model = TestModelName,
                TokensUsed = 100
            });
        }
    }

    private class TestPlanningStrategy : IPlanningStrategy
    {
        private readonly Orkeon.Domain.Crew.ExecutionPlan _plan;

        public int CallCount { get; private set; }

        public TestPlanningStrategy(Orkeon.Domain.Crew.ExecutionPlan plan)
        {
            _plan = plan;
        }

        public System.Threading.Tasks.Task<Orkeon.Domain.Crew.ExecutionPlan> CreatePlanAsync(Orkeon.Domain.Crew.PlanningContext context, IReadOnlyList<TaskId> tasks, CrewInput input)
        {
            CallCount++;
            return System.Threading.Tasks.Task.FromResult(_plan);
        }
    }

    // Helper methods
    private static Orkeon.Domain.Crew.Crew CreateTestCrew(int agentCount = 2)
    {
        var crew = Orkeon.Domain.Crew.Crew.Create(
            "Complete test tasks",
            ProcessType.Sequential,
            false,
            false);

        for (int i = 0; i < agentCount; i++)
        {
            crew.AddAgent(AgentId.Create());
        }

        return crew;
    }

    private static string GenerateLlmPlanResponse(List<(TaskId taskId, int order, AgentId? agentId, List<TaskId>? dependencies)> tasks)
    {
        var response = new System.Text.StringBuilder();
        foreach (var (taskId, order, agentId, dependencies) in tasks)
        {
            response.AppendLine($"TASK: {taskId}");
            response.AppendLine($"ORDER: {order}");
            if (agentId != null)
                response.AppendLine($"AGENT: {agentId}");
            if (dependencies != null && dependencies.Count > 0)
                response.AppendLine($"DEPENDENCIES: {string.Join(",", dependencies)}");
            response.AppendLine("PARALLEL_GROUP: 0");
            response.AppendLine("INSTRUCTIONS: Execute task");
            response.AppendLine("---");
        }
        return response.ToString();
    }

    [Fact]
    public void ShouldInitialize_WhenConstructingWithValidLlmProvider()
    {
        // Arrange
        var llmProvider = new TestLlmProvider();

        // Act
        var planner = CrewPlanner.Create(llmProvider, s_parser);

        // Assert
        Assert.NotNull(planner);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullLlmProvider()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => CrewPlanner.Create(null!, s_parser));
        Assert.Equal("planningLlm", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullParser()
    {
        // Arrange
        var llmProvider = new TestLlmProvider();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => CrewPlanner.Create(llmProvider, null!));
        Assert.Equal("parser", exception.ParamName);
    }

    [Fact]
    public void ShouldUseProvidedStrategy_WhenConstructingWithCustomStrategy()
    {
        // Arrange
        var llmProvider = new TestLlmProvider();
        var customStrategy = new TestPlanningStrategy(Orkeon.Domain.Crew.ExecutionPlan.Create());

        // Act
        var planner = CrewPlanner.Create(llmProvider, s_parser, customStrategy);

        // Assert
        Assert.NotNull(planner);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDelegateToStrategy_WhenCreatingPlanWithCustomStrategy()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskIds = new List<TaskId> { TaskId.Create(), TaskId.Create() };
        var input = new CrewInput("Test context");
        var strategyPlan = Orkeon.Domain.Crew.ExecutionPlan.Create(taskIds);
        var customStrategy = new TestPlanningStrategy(strategyPlan);
        var llmProvider = new TestLlmProvider();
        var planner = CrewPlanner.Create(llmProvider, s_parser, customStrategy);

        // Act
        var plan = await planner.CreatePlanAsync(
            new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents), taskIds, input);

        // Assert — the strategy produced the plan and the LLM was never invoked
        Assert.Same(strategyPlan, plan);
        Assert.Equal(1, customStrategy.CallCount);
        Assert.Null(llmProvider.CapturedConfig);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowInvalidOperationException_WhenStrategyPlanOmitsTasks()
    {
        // Arrange — the strategy returns an empty plan while two tasks are expected
        var crew = CreateTestCrew();
        var taskIds = new List<TaskId> { TaskId.Create(), TaskId.Create() };
        var input = new CrewInput("Test context");
        var customStrategy = new TestPlanningStrategy(Orkeon.Domain.Crew.ExecutionPlan.Create());
        var planner = CrewPlanner.Create(new TestLlmProvider(), s_parser, customStrategy);

        // Act & Assert — strategy plans go through the same validation as LLM plans
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => planner.CreatePlanAsync(
                new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents), taskIds, input));
        Assert.Contains("Invalid plan", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnExecutionPlan_WhenCreatingPlanAsyncWithValidInputs()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskIds = new List<TaskId> { TaskId.Create(), TaskId.Create() };
        var input = new CrewInput("Test execution context");

        var llmResponse = GenerateLlmPlanResponse(
            taskIds.Select((id, index) => (taskId: id, order: index, agentId: (AgentId?)crew.Agents[index % crew.Agents.Count], dependencies: (List<TaskId>?)null)).ToList());
        var llmProvider = new TestLlmProvider(llmResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);

        // Act
        var plan = await planner.CreatePlanAsync(new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents), taskIds, input);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(2, plan.Tasks.Count);
        Assert.All(plan.Tasks, task => Assert.Contains(task.TaskId, taskIds));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseLowTemperatureForConsistency_WhenCreatingPlanAsync()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskIds = new List<TaskId> { TaskId.Create() };
        var input = new CrewInput("Test context");

        var llmResponse = GenerateLlmPlanResponse(
            taskIds.Select((id, index) => (id, index, (AgentId?)null, (List<TaskId>?)null)).ToList());
        var llmProvider = new TestLlmProvider(llmResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);

        // Act
        await planner.CreatePlanAsync(new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents), taskIds, input);

        // Assert
        Assert.NotNull(llmProvider.CapturedConfig);
        Assert.Equal(0.3f, llmProvider.CapturedConfig.Temperature);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldParseDependenciesCorrectly_WhenCreatingPlanAsyncWithTaskDependencies()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var taskIds = new List<TaskId> { taskId1, taskId2 };
        var input = new CrewInput("Test context");

        var llmResponse = GenerateLlmPlanResponse(
        [
            (taskId1, 0, null, null),
            (taskId2, 1, null, [taskId1])
        ]);
        var llmProvider = new TestLlmProvider(llmResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);

        // Act
        var plan = await planner.CreatePlanAsync(new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents), taskIds, input);

        // Assert
        var task2 = plan.Tasks.First(t => t.TaskId == taskId2);
        Assert.Single(task2.Dependencies);
        Assert.Equal(taskId1, task2.Dependencies[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldAssignAgents_WhenCreatingPlanAsyncWithAgentAssignments()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskIds = new List<TaskId> { TaskId.Create(), TaskId.Create() };
        var input = new CrewInput("Test context");

        var llmResponse = GenerateLlmPlanResponse(
            taskIds.Select((id, index) => (taskId: id, order: index, agentId: (AgentId?)crew.Agents[index], dependencies: (List<TaskId>?)null)).ToList());
        var llmProvider = new TestLlmProvider(llmResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);

        // Act
        var plan = await planner.CreatePlanAsync(new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents), taskIds, input);

        // Assert
        Assert.Equal(crew.Agents[0], plan.GetAssignedAgent(taskIds[0]));
        Assert.Equal(crew.Agents[1], plan.GetAssignedAgent(taskIds[1]));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowInvalidOperationException_WhenCreatingPlanAsyncWithMissingTask()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskIds = new List<TaskId> { TaskId.Create(), TaskId.Create() };
        var input = new CrewInput("Test context");

        // LLM response only includes one task
        var llmResponse = GenerateLlmPlanResponse(
            [(taskIds[0], 0, null, null)]);
        var llmProvider = new TestLlmProvider(llmResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => planner.CreatePlanAsync(new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents), taskIds, input));
        Assert.Contains("Invalid plan", exception.Message);
        Assert.Contains("missing from the plan", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowInvalidOperationException_WhenCreatingPlanAsyncWithCircularDependency()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var taskIds = new List<TaskId> { taskId1, taskId2 };
        var input = new CrewInput("Test context");

        // Create circular dependency: task1 depends on task2, task2 depends on task1
        var llmResponse = $@"
TASK: {taskId1}
ORDER: 0
DEPENDENCIES: {taskId2}
PARALLEL_GROUP: 0
INSTRUCTIONS: Execute
---
TASK: {taskId2}
ORDER: 1
DEPENDENCIES: {taskId1}
PARALLEL_GROUP: 0
INSTRUCTIONS: Execute
---";
        var llmProvider = new TestLlmProvider(llmResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => planner.CreatePlanAsync(new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents), taskIds, input));
        Assert.Contains("Invalid plan", exception.Message);
        Assert.Contains("circular dependencies", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSetParallelGroups_WhenCreatingPlanAsyncWithParallelTasks()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskIds = new List<TaskId> { TaskId.Create(), TaskId.Create(), TaskId.Create() };
        var input = new CrewInput("Test context");

        // Tasks 0 and 1 in parallel group 1, task 2 in group 2
        var llmResponse = $@"
TASK: {taskIds[0]}
ORDER: 0
PARALLEL_GROUP: 1
INSTRUCTIONS: Execute in parallel
---
TASK: {taskIds[1]}
ORDER: 1
PARALLEL_GROUP: 1
INSTRUCTIONS: Execute in parallel
---
TASK: {taskIds[2]}
ORDER: 2
PARALLEL_GROUP: 2
INSTRUCTIONS: Execute after
---";
        var llmProvider = new TestLlmProvider(llmResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);

        // Act
        var plan = await planner.CreatePlanAsync(new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents), taskIds, input);

        // Assert
        var task0 = plan.Tasks.First(t => t.TaskId == taskIds[0]);
        var task1 = plan.Tasks.First(t => t.TaskId == taskIds[1]);
        var task2 = plan.Tasks.First(t => t.TaskId == taskIds[2]);

        Assert.Equal(1, task0.ParallelGroup);
        Assert.Equal(1, task1.ParallelGroup);
        Assert.Equal(2, task2.ParallelGroup);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPreserveInstructions_WhenCreatingPlanAsyncWithInstructions()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskIds = new List<TaskId> { TaskId.Create() };
        var input = new CrewInput("Test context");
        var specialInstructions = "Use high precision mode";

        var llmResponse = $@"
TASK: {taskIds[0]}
ORDER: 0
PARALLEL_GROUP: 0
INSTRUCTIONS: {specialInstructions}
---";
        var llmProvider = new TestLlmProvider(llmResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);

        // Act
        var plan = await planner.CreatePlanAsync(new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents), taskIds, input);

        // Assert
        var task = plan.Tasks[0];
        Assert.Equal(specialInstructions, task.Instructions);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCreateSequentialPlan_WhenUsingDefaultPlanningStrategy()
    {
        // Arrange
        var strategy = new DefaultPlanningStrategy();
        var crew = CreateTestCrew();
        var taskIds = new List<TaskId> { TaskId.Create(), TaskId.Create(), TaskId.Create() };
        var input = new CrewInput("Test context");
        var context = new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents);

        // Act
        var plan = await strategy.CreatePlanAsync(context, taskIds, input);

        // Assert
        Assert.Equal(3, plan.Tasks.Count);
        Assert.Equal(0, plan.Tasks[0].ExecutionOrder);
        Assert.Equal(1, plan.Tasks[1].ExecutionOrder);
        Assert.Equal(2, plan.Tasks[2].ExecutionOrder);
    }

    [Fact]
    public void ShouldCreateDifferentInstances_WhenUsingCrewPlanner()
    {
        // Arrange
        var llmProvider1 = new TestLlmProvider();
        var llmProvider2 = new TestLlmProvider();
        var strategy = new DefaultPlanningStrategy();

        // Act
        var planner1 = CrewPlanner.Create(llmProvider1, s_parser);
        var planner2 = CrewPlanner.Create(llmProvider2, s_parser);
        var planner3 = CrewPlanner.Create(llmProvider1, s_parser, strategy);

        // Assert
        Assert.NotSame(planner1, planner2); // Different instances
        Assert.NotSame(planner1, planner3); // Different instances
        Assert.NotSame(planner2, planner3); // Different instances
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenCreatingPlanWithNullContext()
    {
        // Arrange
        var llmProvider = new TestLlmProvider();
        var planner = CrewPlanner.Create(llmProvider, s_parser);
        var taskIds = new List<TaskId> { TaskId.Create() };
        var input = new CrewInput("Test context");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => planner.CreatePlanAsync(null!, taskIds, input));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenCreatingPlanWithNullTasks()
    {
        // Arrange
        var crew = CreateTestCrew();
        var llmProvider = new TestLlmProvider();
        var planner = CrewPlanner.Create(llmProvider, s_parser);
        var input = new CrewInput("Test context");
        var context = new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => planner.CreatePlanAsync(context, null!, input));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenCreatingPlanWithNullInput()
    {
        // Arrange
        var crew = CreateTestCrew();
        var llmProvider = new TestLlmProvider();
        var planner = CrewPlanner.Create(llmProvider, s_parser);
        var taskIds = new List<TaskId> { TaskId.Create() };
        var context = new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => planner.CreatePlanAsync(context, taskIds, null!));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldParseJsonResponse_WhenLlmReturnsValidJson()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var taskIds = new List<TaskId> { taskId1, taskId2 };
        var input = new CrewInput("Test context");

        var jsonResponse = $@"{{
  ""tasks"": [
    {{
      ""task"": ""{taskId1}"",
      ""order"": 0,
      ""parallel_group"": 1,
      ""dependencies"": [],
      ""instructions"": ""First task"",
      ""agent"": ""{crew.Agents[0]}""
    }},
    {{
      ""task"": ""{taskId2}"",
      ""order"": 1,
      ""parallel_group"": 2,
      ""dependencies"": [""{taskId1}""],
      ""instructions"": ""Second task"",
      ""agent"": ""{crew.Agents[1]}""
    }}
  ]
}}";
        var llmProvider = new TestLlmProvider(jsonResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);
        var context = new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents);

        // Act
        var plan = await planner.CreatePlanAsync(context, taskIds, input);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(2, plan.Tasks.Count);
        Assert.Equal(taskId1, plan.Tasks[0].TaskId);
        Assert.Equal(taskId2, plan.Tasks[1].TaskId);
        Assert.Equal(1, plan.Tasks[0].ParallelGroup);
        Assert.Equal(2, plan.Tasks[1].ParallelGroup);
        Assert.Equal("First task", plan.Tasks[0].Instructions);
        Assert.Equal(crew.Agents[0], plan.GetAssignedAgent(taskId1));
        Assert.Equal(crew.Agents[1], plan.GetAssignedAgent(taskId2));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldFallbackToTextParsing_WhenLlmReturnsInvalidJson()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskIds = new List<TaskId> { TaskId.Create() };
        var input = new CrewInput("Test context");

        // This is not valid JSON, so it should fall back to text parsing
        var llmResponse = GenerateLlmPlanResponse(
            taskIds.Select((id, index) => (id, index, (AgentId?)null, (List<TaskId>?)null)).ToList());
        var llmProvider = new TestLlmProvider(llmResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);
        var context = new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents);

        // Act
        var plan = await planner.CreatePlanAsync(context, taskIds, input);

        // Assert
        Assert.NotNull(plan);
        Assert.Single(plan.Tasks);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmptyDefaultPlan_WhenUsingDefaultPlanningStrategyWithEmptyTaskList()
    {
        // Arrange
        var strategy = new DefaultPlanningStrategy();
        var crew = CreateTestCrew();
        var emptyTasks = new List<TaskId>();
        var input = new CrewInput("Test context");
        var context = new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents);

        // Act
        var plan = await strategy.CreatePlanAsync(context, emptyTasks, input);

        // Assert
        Assert.Empty(plan.Tasks);
    }

    // ===== P2-01: Exact ID matching tests (no false positives) =====

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotMatchPartialTaskId_WhenParsingTextResponse()
    {
        // Arrange: Two tasks — only full ULID should match, not a substring
        var crew = CreateTestCrew();
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var taskIds = new List<TaskId> { taskId1, taskId2 };
        var input = new CrewInput("Test context");

        // Use a substring of taskId1's ULID as the "TASK:" value — should NOT match anything
        var partialId = taskId1.ToString()[..13]; // First 13 chars of the 26-char ULID
        var llmResponse = $@"
TASK: {partialId}
ORDER: 0
PARALLEL_GROUP: 0
INSTRUCTIONS: Execute
---
TASK: {taskId2}
ORDER: 1
PARALLEL_GROUP: 0
INSTRUCTIONS: Execute
---";
        var llmProvider = new TestLlmProvider(llmResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);
        var context = new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents);

        // Act & Assert: Plan should be invalid because taskId1 is missing (partial match should fail)
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => planner.CreatePlanAsync(context, taskIds, input));
        Assert.Contains("missing from the plan", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotMatchPartialTaskId_WhenParsingJsonResponse()
    {
        // Arrange: Two tasks — only full ULID should match, not a substring
        var crew = CreateTestCrew();
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var taskIds = new List<TaskId> { taskId1, taskId2 };
        var input = new CrewInput("Test context");

        // Use a substring of taskId1's ULID as the task value in JSON
        var partialId = taskId1.ToString()[..13];
        var jsonResponse = $@"{{
  ""tasks"": [
    {{
      ""task"": ""{partialId}"",
      ""order"": 0,
      ""parallel_group"": 0,
      ""instructions"": ""First""
    }},
    {{
      ""task"": ""{taskId2}"",
      ""order"": 1,
      ""parallel_group"": 0,
      ""instructions"": ""Second""
    }}
  ]
}}";
        var llmProvider = new TestLlmProvider(jsonResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);
        var context = new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents);

        // Act & Assert: Plan should be invalid because taskId1 is missing (partial match should fail)
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => planner.CreatePlanAsync(context, taskIds, input));
        Assert.Contains("missing from the plan", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldMatchExactTaskId_WhenParsingTextResponse()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var taskIds = new List<TaskId> { taskId1, taskId2 };
        var input = new CrewInput("Test context");

        // Use exact full ULID strings — should match correctly
        var llmResponse = GenerateLlmPlanResponse(
        [
            (taskId1, 0, null, null),
            (taskId2, 1, null, null)
        ]);
        var llmProvider = new TestLlmProvider(llmResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);
        var context = new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents);

        // Act
        var plan = await planner.CreatePlanAsync(context, taskIds, input);

        // Assert
        Assert.Equal(2, plan.Tasks.Count);
        Assert.Equal(taskId1, plan.Tasks[0].TaskId);
        Assert.Equal(taskId2, plan.Tasks[1].TaskId);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotMatchPartialAgentId_WhenParsingTextResponse()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskId1 = TaskId.Create();
        var taskIds = new List<TaskId> { taskId1 };
        var input = new CrewInput("Test context");

        // Use a substring of the agent ULID — should NOT assign the agent
        var agentPartialId = crew.Agents[0].ToString()[..13];
        var llmResponse = $@"
TASK: {taskId1}
ORDER: 0
AGENT: {agentPartialId}
PARALLEL_GROUP: 0
INSTRUCTIONS: Execute
---";
        var llmProvider = new TestLlmProvider(llmResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);
        var context = new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents);

        // Act
        var plan = await planner.CreatePlanAsync(context, taskIds, input);

        // Assert: The agent should NOT be assigned (partial ID should not match)
        Assert.Null(plan.GetAssignedAgent(taskId1));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotMatchPartialDependencyId_WhenParsingTextResponse()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var taskIds = new List<TaskId> { taskId1, taskId2 };
        var input = new CrewInput("Test context");

        // taskId2 depends on a partial string of taskId1 — should NOT resolve the dependency
        var partialDepId = taskId1.ToString()[..13];
        var llmResponse = $@"
TASK: {taskId1}
ORDER: 0
PARALLEL_GROUP: 0
INSTRUCTIONS: Execute
---
TASK: {taskId2}
ORDER: 1
DEPENDENCIES: {partialDepId}
PARALLEL_GROUP: 0
INSTRUCTIONS: Execute
---";
        var llmProvider = new TestLlmProvider(llmResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);
        var context = new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents);

        // Act
        var plan = await planner.CreatePlanAsync(context, taskIds, input);

        // Assert: taskId2 should have no dependencies (partial match should not resolve)
        var task2 = plan.Tasks.First(t => t.TaskId == taskId2);
        Assert.Empty(task2.Dependencies);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotMatchPartialAgentId_WhenParsingJsonResponse()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskId1 = TaskId.Create();
        var taskIds = new List<TaskId> { taskId1 };
        var input = new CrewInput("Test context");

        // Use a substring of the agent ULID in JSON — should NOT assign the agent
        var agentPartialId = crew.Agents[0].ToString()[..13];
        var jsonResponse = $@"{{
  ""tasks"": [
    {{
      ""task"": ""{taskId1}"",
      ""order"": 0,
      ""parallel_group"": 0,
      ""instructions"": ""Task one"",
      ""agent"": ""{agentPartialId}""
    }}
  ]
}}";
        var llmProvider = new TestLlmProvider(jsonResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);
        var context = new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents);

        // Act
        var plan = await planner.CreatePlanAsync(context, taskIds, input);

        // Assert: The agent should NOT be assigned (partial ID should not match)
        Assert.Null(plan.GetAssignedAgent(taskId1));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotMatchPartialDependencyId_WhenParsingJsonResponse()
    {
        // Arrange
        var crew = CreateTestCrew();
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var taskIds = new List<TaskId> { taskId1, taskId2 };
        var input = new CrewInput("Test context");

        // taskId2 depends on a partial string of taskId1 — should NOT resolve the dependency
        var partialDepId = taskId1.ToString()[..13];
        var jsonResponse = $@"{{
  ""tasks"": [
    {{
      ""task"": ""{taskId1}"",
      ""order"": 0,
      ""parallel_group"": 0,
      ""dependencies"": [],
      ""instructions"": ""First""
    }},
    {{
      ""task"": ""{taskId2}"",
      ""order"": 1,
      ""parallel_group"": 0,
      ""dependencies"": [""{partialDepId}""],
      ""instructions"": ""Second""
    }}
  ]
}}";
        var llmProvider = new TestLlmProvider(jsonResponse);
        var planner = CrewPlanner.Create(llmProvider, s_parser);
        var context = new Orkeon.Domain.Crew.PlanningContext(crew.Id, crew.Goal, crew.Agents);

        // Act
        var plan = await planner.CreatePlanAsync(context, taskIds, input);

        // Assert: taskId2 should have no dependencies (partial match should not resolve)
        var task2 = plan.Tasks.First(t => t.TaskId == taskId2);
        Assert.Empty(task2.Dependencies);
    }
}
