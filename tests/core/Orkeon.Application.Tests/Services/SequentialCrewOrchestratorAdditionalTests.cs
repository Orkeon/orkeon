using DomainCrew = Orkeon.Domain.Crew.Crew;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Infrastructure.Parsing;
using Orkeon.Application.Interfaces.Services;
using System.Diagnostics;
// Aliases pour éviter les conflits
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;
using ExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;

namespace Orkeon.Application.Tests.Services;

/// <summary>
/// Additional tests for SequentialCrewOrchestrator following Clean Architecture principles.
/// </summary>
public class SequentialCrewOrchestratorAdditionalTests
{
    #region Test Doubles

    private class TestLogger : ILogger<SequentialCrewOrchestrator>
    {
        public List<string> Logs { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Logs.Add($"[{logLevel}] {formatter(state, exception)}");
        }
    }

    private class TestCrewRepository : ICrewRepository
    {
        private readonly Dictionary<Ulid, DomainCrew?> _crews = [];
        public bool ShouldThrowOnGet { get; set; }
        public bool ShouldThrowOnUpdate { get; set; }

        public void AddCrew(DomainCrew crew)
        {
            _crews[crew.Id.Value] = crew;
        }

        public System.Threading.Tasks.Task<DomainCrew?> GetByIdAsync(CrewId id, CancellationToken cancellationToken = default)
        {
            if (ShouldThrowOnGet)
                throw new InvalidOperationException("Repository error");

            _crews.TryGetValue(id.Value, out var crew);
            return System.Threading.Tasks.Task.FromResult(crew);
        }

        public System.Threading.Tasks.Task AddAsync(DomainCrew crew, CancellationToken cancellationToken = default)
        {
            _crews[crew.Id.Value] = crew;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task UpdateAsync(DomainCrew crew, CancellationToken cancellationToken = default)
        {
            if (ShouldThrowOnUpdate)
                throw new InvalidOperationException("Update failed");

            _crews[crew.Id.Value] = crew;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task DeleteAsync(CrewId id, CancellationToken cancellationToken = default)
        {
            _crews.Remove(id.Value);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        // Méthodes supplémentaires requises par ICrewRepository
        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByIdsAsync(IEnumerable<CrewId> ids, CancellationToken cancellationToken = default)
        {
            var result = _crews.Where(c => c.Value != null && ids.Contains(c.Value.Id)).Select(c => c.Value!).ToList();
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(result);
        }

        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByStatusAsync(CrewStatus status, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        }

        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByProcessTypeAsync(ProcessType processType, CancellationToken cancellationToken = default)
        {
            var result = _crews.Values.Where(c => c != null && c.ProcessType == processType).Select(c => c!).ToList();
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(result);
        }

        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByAgentAsync(AgentId agentId, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        }

        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByTaskAsync(TaskId taskId, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        }

        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetWithRecentExecutionsAsync(DateTime since, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        }

        public System.Threading.Tasks.Task<CrewExecutionStatistics> GetExecutionStatisticsAsync(CrewId crewId, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(new CrewExecutionStatistics(0, 0, 0, 0.0, 0.0, null));
        }

        // ISpecificationRepository implementation
        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> FindAsync(ISpecification<DomainCrew> specification, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        }

        public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> FindAsync(ISpecification<DomainCrew> specification, int skip, int take, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        }

        public System.Threading.Tasks.Task<int> CountAsync(ISpecification<DomainCrew> specification, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(0);
        }

        public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<DomainCrew> specification, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(false);
        }

        // IRepository implementation
        public System.Threading.Tasks.Task<bool> ExistsAsync(CrewId id, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(_crews.ContainsKey(id.Value));
        }

        public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(_crews.Count);
        }
    }

    private class TestStateManager : ICrewExecutionStateManager
    {
        private readonly Dictionary<Ulid, CrewExecutionState> _states = [];
        private readonly Dictionary<string, CrewExecutionState> _executionStates = [];
        public bool ShouldThrowOnSave { get; set; }

        public System.Threading.Tasks.Task<CrewExecutionState> GetStateAsync(CrewId crewId, CancellationToken cancellationToken = default)
        {
            _states.TryGetValue(crewId.Value, out var state);
            return System.Threading.Tasks.Task.FromResult(state ?? new CrewExecutionState(crewId, ExecutionId.New(), new CrewInput("Default", new Dictionary<string, object>())));
        }

        public System.Threading.Tasks.Task SaveStateAsync(CrewExecutionState state, CancellationToken cancellationToken = default)
        {
            if (ShouldThrowOnSave)
                throw new InvalidOperationException("State save failed");

            _states[state.CrewId.Value] = state;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task ClearStateAsync(CrewId crewId, CancellationToken cancellationToken = default)
        {
            _states.Remove(crewId.Value);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        // Nouvelles méthodes requises
        public System.Threading.Tasks.Task<CrewExecutionState> CreateStateAsync(CrewId crewId, CancellationToken cancellationToken = default)
        {
            var state = new CrewExecutionState(crewId, ExecutionId.New(), new CrewInput("Default", new Dictionary<string, object>()));
            _states[crewId.Value] = state;
            _executionStates[Guid.NewGuid().ToString()] = state;
            return System.Threading.Tasks.Task.FromResult(state);
        }

        public System.Threading.Tasks.Task<CrewExecutionState> CreateStateAsync(CrewId crewId, ExecutionId executionId, CrewInput input, CancellationToken cancellationToken = default)
        {
            var state = new CrewExecutionState(crewId, executionId, input);
            _states[crewId.Value] = state;
            _executionStates[executionId.AsString()] = state;
            return System.Threading.Tasks.Task.FromResult(state);
        }

        public System.Threading.Tasks.Task<CrewExecutionState?> GetStateAsync(ExecutionId executionId, CancellationToken cancellationToken = default)
        {
            _executionStates.TryGetValue(executionId.AsString(), out var state);
            return System.Threading.Tasks.Task.FromResult(state);
        }

        public System.Threading.Tasks.Task UpdateStateAsync(ExecutionId executionId, Action<CrewExecutionState> updateAction, CancellationToken cancellationToken = default)
        {
            if (_executionStates.TryGetValue(executionId.AsString(), out var state))
            {
                updateAction(state);
            }
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task CompleteExecutionAsync(ExecutionId executionId, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task<IReadOnlyList<CrewExecutionState>> GetActiveExecutionsAsync(CrewId crewId, CancellationToken cancellationToken = default)
        {
            var result = _states.Values.Where(s => s.CrewId == crewId).ToList();
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewExecutionState>>(result);
        }

        public async System.Threading.Tasks.Task CleanupExpiredExecutionsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
        {
            await System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private class TestProcessStrategy : IProcessStrategy
    {
        public List<string> ExecutedTasks { get; } = [];
        public bool ShouldFail { get; set; }
        public bool ShouldThrow { get; set; }
        public IReadOnlyDictionary<string, string>? LastReceivedVariables { get; private set; }

        public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteSequentialAsync(DomainCrew crew, ExecutionPlan plan, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        {
            LastReceivedVariables = inputVariables;
            if (ShouldThrow)
                throw new InvalidOperationException("Strategy execution failed");

            ExecutedTasks.Add($"Sequential execution: {crew.Goal}");
            return CreateOutput(crew);
        }

        public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteHierarchicalAsync(DomainCrew crew, Orkeon.Domain.Common.AgentId managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        {
            LastReceivedVariables = inputVariables;
            if (ShouldThrow)
                throw new InvalidOperationException("Strategy execution failed");

            ExecutedTasks.Add($"Hierarchical execution: {crew.Goal}");
            return CreateOutput(crew);
        }

        public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteParallelAsync(DomainCrew crew, ExecutionPlan plan, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        {
            LastReceivedVariables = inputVariables;
            if (ShouldThrow)
                throw new InvalidOperationException("Strategy execution failed");

            ExecutedTasks.Add($"Parallel execution: {crew.Goal}");
            return CreateOutput(crew);
        }

        public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteAutonomousAsync(DomainCrew crew, AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Autonomous execution is not covered by this test double.");

        private System.Threading.Tasks.Task<DomainCrewOutput> CreateOutput(DomainCrew crew)
        {
            if (ShouldFail)
            {
                return System.Threading.Tasks.Task.FromResult(new DomainCrewOutput(
                    "Test failure",
                    null,
                    [],
                    false,
                    TimeSpan.Zero,
                    null));
            }

            var output = new DomainCrewOutput(
                $"Completed crew goal: {crew.Goal}",
                null,
                [],
                true,
                TimeSpan.FromSeconds(10),
                null);

            return System.Threading.Tasks.Task.FromResult(output);
        }
    }

    private class TestProcessStrategyFactory : IProcessStrategyFactory
    {
        private readonly TestProcessStrategy _strategy = new();
        public bool ShouldThrowOnCreate { get; set; }

        public TestProcessStrategy Strategy => _strategy;

        public IProcessStrategy CreateStrategy(ProcessType processType)
        {
            if (ShouldThrowOnCreate)
                throw new InvalidOperationException("Strategy creation failed");

            return _strategy;
        }
    }

    #endregion

    #region Additional Constructor Tests

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullStateManager()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var strategyFactory = new TestProcessStrategyFactory();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SequentialCrewOrchestrator(repository, logger, null!, strategyFactory, new ExecutionPlanParser()));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullStrategyFactory()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SequentialCrewOrchestrator(repository, logger, stateManager, null!, new ExecutionPlanParser()));
    }

    #endregion

    #region Input Validation Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenUsingKickoffAsyncWithNullInput()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        // Act & Assert — a null CrewInput is invalid (CrewInput has no empty form;
        // its constructor requires a non-blank InitialContext), so KickoffAsync guards it.
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => orchestrator.KickoffAsync(crew.Id, null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteSuccessfully_WhenUsingKickoffAsyncWithEmptyInputContext()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var input = new CrewInput("", new Dictionary<string, object>());

        // Act
        var output = await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(output.FinalOutput);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingKickoffAsyncWithVeryLargeInputData()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var largeData = new Dictionary<string, object>();
        for (int i = 0; i < 1000; i++)
        {
            largeData[$"key{i}"] = new string('x', 1000);
        }

        var input = new CrewInput("Large input test", largeData);

        // Act
        var output = await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(output.FinalOutput);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingKickoffAsyncWithSpecialCharactersInInput()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var input = new CrewInput(
            "Special chars: @#$%^&*()[]{}|\\<>?,./~`\"'",
            new Dictionary<string, object> { { "unicode", "测试 テスト тест" } }
        );

        // Act
        var output = await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(output.FinalOutput);
    }

    #endregion

    #region Concurrent Execution Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingKickoffAsyncWithConcurrentExecutions()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Concurrent test crew", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var tasks = new List<System.Threading.Tasks.Task<CrewOutput>>();
        for (int i = 0; i < 10; i++)
        {
            var input = new CrewInput($"Concurrent {i}", new Dictionary<string, object>());
            tasks.Add(orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken));
        }

        // Act
        var outputs = await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.Equal(10, outputs.Length);
        Assert.All(outputs, output => Assert.NotNull(output.FinalOutput));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteIndependently_WhenUsingKickoffAsyncWithMultipleCrewsSimultaneously()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crews = new List<DomainCrew>();
        for (int i = 0; i < 5; i++)
        {
            var crew = DomainCrew.Create($"Crew {i}", ProcessType.Sequential);

            // Add required components to make crew valid
            var agentId = AgentId.Create();
            var taskId = TaskId.Create();
            crew.AddAgent(agentId);
            crew.AddTask(taskId);

            repository.AddCrew(crew);
            crews.Add(crew);
        }

        var tasks = crews.Select(crew =>
        {
            var input = new CrewInput($"Input for {crew.Goal}", new Dictionary<string, object>());
            return orchestrator.KickoffAsync(crew.Id, input);
        }).ToList();

        // Act
        var outputs = await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.Equal(5, outputs.Length);
        Assert.All(outputs, output => Assert.Contains("Completed crew goal", output.FinalOutput));
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnError_WhenUsingKickoffAsyncWithRepositoryException()
    {
        // Arrange
        var repository = new TestCrewRepository();
        repository.ShouldThrowOnGet = true;
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crewId = CrewId.Create();
        var input = new CrewInput("Test", new Dictionary<string, object>());

        // Act
        var output = await orchestrator.KickoffAsync(crewId, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Crew execution failed", output.FinalOutput);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleGracefully_WhenUsingKickoffAsyncWithStateManagerException()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        stateManager.ShouldThrowOnSave = true;
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var input = new CrewInput("Test", new Dictionary<string, object>());

        // Act
        var output = await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(output); // Should still return output even if state save fails
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnError_WhenUsingKickoffAsyncWithStrategyFactoryException()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        strategyFactory.ShouldThrowOnCreate = true;
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var input = new CrewInput("Test", new Dictionary<string, object>());

        // Act
        var output = await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Crew execution failed", output.FinalOutput);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnError_WhenUsingKickoffAsyncWithStrategyExecutionException()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        strategyFactory.Strategy.ShouldThrow = true;
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var input = new CrewInput("Test", new Dictionary<string, object>());

        // Act
        var output = await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Crew execution failed", output.FinalOutput);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldCompleteInReasonableTime_WhenUsingKickoffAsyncWithManyTasks()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Performance test crew", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var input = new CrewInput("Performance test", new Dictionary<string, object>());

        var stopwatch = Stopwatch.StartNew();

        // Act
        var output = await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);

        stopwatch.Stop();

        // Assert
        Assert.NotNull(output.FinalOutput);
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, "Execution took too long");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleGracefully_WhenUsingKickoffAsyncWithNullCrewId()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var input = new CrewInput("Test", new Dictionary<string, object>());

        // Act
        var output = await orchestrator.KickoffAsync(null!, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(output);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingKickoffAsyncWithWhitespaceContext()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var input = new CrewInput("   \t\n\r   ", new Dictionary<string, object>());

        // Act
        var output = await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(output.FinalOutput);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingKickoffAsyncWithNullValuesInInputData()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var input = new CrewInput("Test", new Dictionary<string, object>
        {
            { "null_value", null! },
            { "valid_value", "test" }
        });

        // Act
        var output = await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(output.FinalOutput);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldWork_WhenUsingKickoffAsyncImmediatelyAfterCreation()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Just created crew", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var input = new CrewInput("Immediate execution", new Dictionary<string, object>());

        // Act - Execute immediately after creation
        var output = await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(output.FinalOutput);
        Assert.Contains("Completed crew goal", output.FinalOutput);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldWorkEachTime_WhenUsingKickoffAsyncWithRepeatedExecutions()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Repeated execution crew", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var outputs = new List<CrewOutput>();

        // Act - Execute same crew multiple times
        for (int i = 0; i < 5; i++)
        {
            var input = new CrewInput($"Execution {i}", new Dictionary<string, object>());
            var output = await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);
            outputs.Add(output);
        }

        // Assert
        Assert.Equal(5, outputs.Count);
        Assert.All(outputs, output => Assert.Contains("Completed crew goal", output.FinalOutput));
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogAllSteps_WhenUsingKickoffAsyncWithVerboseLogging()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Verbose logging test", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var input = new CrewInput("Test", new Dictionary<string, object>());

        // Act
        await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(logger.Logs);
        Assert.Contains(logger.Logs, log => log.Contains("[Information]"));
    }

    #endregion

    #region Synchronous Execution Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnFailure_WhenUsingKickoffAsyncWithFailingExecution()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        strategyFactory.Strategy.ShouldFail = true;

        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Sync fail test", ProcessType.Sequential);

        // Add required components to make crew valid
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        repository.AddCrew(crew);

        var input = new CrewInput("Test", new Dictionary<string, object>());

        // Act
        var output = await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Test failure", output.FinalOutput);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleGracefully_WhenUsingKickoffAsyncWithException()
    {
        // Arrange
        var repository = new TestCrewRepository();
        repository.ShouldThrowOnGet = true;
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crewId = CrewId.Create();
        var input = new CrewInput("Test", new Dictionary<string, object>());

        // Act
        var output = await orchestrator.KickoffAsync(crewId, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Crew execution failed", output.FinalOutput);
    }

    #endregion
}
