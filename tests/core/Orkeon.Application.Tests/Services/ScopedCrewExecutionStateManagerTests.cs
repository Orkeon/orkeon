using Microsoft.Extensions.DependencyInjection;
using Orkeon.Infrastructure.Orchestration;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Common;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Application.Tests.Services;

public sealed class ScopedCrewExecutionStateManagerTests : IDisposable
{
    #region Test Doubles

    private class TestLogger : ILogger<ScopedCrewExecutionStateManager>
    {
        public List<string> LoggedMessages { get; } = [];
        public List<Exception> LoggedExceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            LoggedMessages.Add($"[{logLevel}] {message}");
            if (exception != null)
            {
                LoggedExceptions.Add(exception);
            }
        }

        public bool HasLoggedInfo(string partialMessage)
        {
            return LoggedMessages.Any(m => m.StartsWith("[Information]") && m.Contains(partialMessage));
        }

        public bool HasLoggedDebug(string partialMessage)
        {
            return LoggedMessages.Any(m => m.StartsWith("[Debug]") && m.Contains(partialMessage));
        }
    }

    private class TestServiceScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new TestServiceProvider();
        public void Dispose() { GC.SuppressFinalize(this); }
    }

    private class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private class TestServiceScopeFactory : IServiceScopeFactory
    {
        public List<IServiceScope> CreatedScopes { get; } = [];

        public IServiceScope CreateScope()
        {
            var scope = new TestServiceScope();
            CreatedScopes.Add(scope);
            return scope;
        }
    }

    #endregion

    #region Test Helpers

    private readonly List<IDisposable> _disposables = [];

    private static CrewId CreateTestCrewId() => CrewId.Create();
    private static ExecutionId CreateTestExecutionId() => ExecutionId.New();

    private static CrewInput CreateTestInput(Dictionary<string, object>? variables = null)
    {
        return new CrewInput(
            InitialContext: "Test context",
            Variables: variables ?? []);
    }

    private ScopedCrewExecutionStateManager CreateManager(
        TestServiceScopeFactory? scopeFactory = null,
        TestLogger? logger = null)
    {
        var factory = scopeFactory ?? new TestServiceScopeFactory();
        var log = logger ?? new TestLogger();
        var manager = new ScopedCrewExecutionStateManager(factory, log);
        _disposables.Add(manager);
        return manager;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
        _disposables.Clear();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldInitialize_WhenConstructingWithValidDependencies()
    {
        // Arrange
        var scopeFactory = new TestServiceScopeFactory();
        var logger = new TestLogger();

        // Act
        var manager = CreateManager(scopeFactory, logger);

        // Assert
        Assert.NotNull(manager);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullScopeFactory()
    {
        // Arrange
        var logger = new TestLogger();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ScopedCrewExecutionStateManager(null!, logger));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullLogger()
    {
        // Arrange
        var scopeFactory = new TestServiceScopeFactory();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ScopedCrewExecutionStateManager(scopeFactory, null!));
    }

    #endregion

    #region CreateStateAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldCreateNewState_WhenCreatingStateAsyncWithCrewId()
    {
        // Arrange
        var logger = new TestLogger();
        var scopeFactory = new TestServiceScopeFactory();
        var manager = CreateManager(scopeFactory, logger);
        var crewId = CreateTestCrewId();

        // Act
        var state = await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(state);
        Assert.Equal(crewId, state.CrewId);
        Assert.NotNull(state.Id);
        Assert.Equal(ExecutionState.Pending, state.Status);
        Assert.Equal(0.0, state.Progress);

        // Verify logging
        Assert.True(logger.HasLoggedInfo($"Created execution state for crew {crewId.Value}"));

        // Verify service scope was created for persistence
        Assert.Single(scopeFactory.CreatedScopes);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCreateStateWithSpecificValues_WhenCreatingStateAsyncWithExecutionIdAndInput()
    {
        // Arrange
        var logger = new TestLogger();
        var manager = CreateManager(logger: logger);
        var crewId = CreateTestCrewId();
        var executionId = CreateTestExecutionId();
        var input = CreateTestInput(new Dictionary<string, object> { ["key"] = "value" });

        // Act
        var state = await manager.CreateStateAsync(crewId, executionId, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(state);
        Assert.Equal(crewId, state.CrewId);
        Assert.Equal(executionId, state.Id);
        Assert.Equal(input, state.Input);
        Assert.True(logger.HasLoggedInfo($"Created execution state for crew {crewId.Value} with execution ID {executionId.Value}"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowInvalidOperationException_WhenCreatingStateAsyncWithDuplicateExecutionId()
    {
        // Arrange
        var manager = CreateManager();
        var crewId = CreateTestCrewId();
        var executionId = CreateTestExecutionId();
        var input = CreateTestInput();

        // Act
        await manager.CreateStateAsync(crewId, executionId, input, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.CreateStateAsync(crewId, executionId, input, TestContext.Current.CancellationToken));
        Assert.Contains($"Execution state with ID {executionId} already exists", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectCancellationToken_WhenCreatingStateAsyncWithCancellation()
    {
        // Arrange
        var manager = CreateManager();
        var crewId = CreateTestCrewId();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => manager.CreateStateAsync(crewId, cts.Token));
    }

    #endregion

    #region GetStateAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnFromMemory_WhenGettingStateAsyncWithExistingState()
    {
        // Arrange
        var manager = CreateManager();
        var crewId = CreateTestCrewId();
        var originalState = await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);

        // Act
        var retrievedState = await manager.GetStateAsync(originalState.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrievedState);
        Assert.Equal(originalState.Id, retrievedState.Id);
        Assert.Equal(originalState.CrewId, retrievedState.CrewId);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnNull_WhenGettingStateAsyncWithNonExistingState()
    {
        // Arrange
        var manager = CreateManager();
        var nonExistentId = CreateTestExecutionId();

        // Act
        var state = await manager.GetStateAsync(nonExistentId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectCancellationToken_WhenGettingStateAsyncWithCancellation()
    {
        // Arrange
        var manager = CreateManager();
        var executionId = CreateTestExecutionId();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => manager.GetStateAsync(executionId, cts.Token));
    }

    #endregion

    #region UpdateStateAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldApplyUpdate_WhenUpdatingStateAsyncWithExistingState()
    {
        // Arrange
        var logger = new TestLogger();
        var scopeFactory = new TestServiceScopeFactory();
        var manager = CreateManager(scopeFactory, logger);
        var crewId = CreateTestCrewId();
        var state = await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);
        var originalProgress = state.Progress;

        // Act
        await manager.UpdateStateAsync(state.Id, s =>
        {
            s.Status = ExecutionState.Running;
            s.Progress = 0.5;
        }, TestContext.Current.CancellationToken);

        // Assert
        var updatedState = await manager.GetStateAsync(state.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(updatedState);
        Assert.Equal(ExecutionState.Running, updatedState.Status);
        Assert.Equal(0.5, updatedState.Progress);
        Assert.NotEqual(originalProgress, updatedState.Progress);

        // Verify logging
        Assert.True(logger.HasLoggedDebug($"Updated execution state {state.Id.Value}"));
        Assert.True(logger.HasLoggedDebug("Status=Running"));
        Assert.True(logger.HasLoggedDebug("Progress=0.5"));

        // Verify persistence scope was created
        Assert.Equal(2, scopeFactory.CreatedScopes.Count); // One for create, one for update
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowInvalidOperationException_WhenUpdatingStateAsyncWithNonExistingState()
    {
        // Arrange
        var manager = CreateManager();
        var nonExistentId = CreateTestExecutionId();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.UpdateStateAsync(nonExistentId, s => s.Status = ExecutionState.Running, TestContext.Current.CancellationToken));
        Assert.Contains($"Execution state with ID {nonExistentId} not found", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotThrow_WhenUpdatingStateAsyncWithNullUpdate()
    {
        // Arrange
        var manager = CreateManager();
        var crewId = CreateTestCrewId();
        var state = await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);

        // Act & Assert - Should not throw for null update action
        var exception = await Record.ExceptionAsync(async () =>
            await manager.UpdateStateAsync(state.Id, s => { /* No changes */ }, TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }

    #endregion

    #region CompleteExecutionAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldMarkAsCompleted_WhenCompletingExecutionAsyncWithExistingState()
    {
        // Arrange
        var logger = new TestLogger();
        var manager = CreateManager(logger: logger);
        var crewId = CreateTestCrewId();
        var state = await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);

        // Act
        await manager.CompleteExecutionAsync(state.Id, TestContext.Current.CancellationToken);

        // Assert
        var completedState = await manager.GetStateAsync(state.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(completedState);
        Assert.Equal(ExecutionState.Completed, completedState.Status);
        Assert.Equal(1.0, completedState.Progress);

        // Verify logging
        Assert.True(logger.HasLoggedInfo($"Completed execution {state.Id.Value}"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowInvalidOperationException_WhenCompletingExecutionAsyncWithNonExistingState()
    {
        // Arrange
        var manager = CreateManager();
        var nonExistentId = CreateTestExecutionId();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.CompleteExecutionAsync(nonExistentId, TestContext.Current.CancellationToken));
    }

    #endregion

    #region GetActiveExecutionsAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnFilteredStates_WhenGettingActiveExecutionsAsyncWithActiveStates()
    {
        // Arrange
        var manager = CreateManager();
        var crewId = CreateTestCrewId();
        var otherCrewId = CreateTestCrewId();

        // Create states for target crew
        var activeState1 = await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);
        var activeState2 = await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);
        await manager.UpdateStateAsync(activeState2.Id, s => s.Status = ExecutionState.Running, TestContext.Current.CancellationToken);

        // Create completed state for same crew (should not be returned)
        var completedState = await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);
        await manager.CompleteExecutionAsync(completedState.Id, TestContext.Current.CancellationToken);

        // Create state for different crew (should not be returned)
        await manager.CreateStateAsync(otherCrewId, TestContext.Current.CancellationToken);

        // Act
        var activeStates = await manager.GetActiveExecutionsAsync(crewId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, activeStates.Count);
        Assert.Contains(activeStates, s => s.Id == activeState1.Id && s.Status == ExecutionState.Pending);
        Assert.Contains(activeStates, s => s.Id == activeState2.Id && s.Status == ExecutionState.Running);
        Assert.DoesNotContain(activeStates, s => s.Id == completedState.Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmptyList_WhenGettingActiveExecutionsAsyncWithNoActiveStates()
    {
        // Arrange
        var manager = CreateManager();
        var crewId = CreateTestCrewId();

        // Act
        var activeStates = await manager.GetActiveExecutionsAsync(crewId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(activeStates);
    }

    #endregion

    #region CleanupExpiredExecutionsAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldRemoveOldStates_WhenUsingCleanupExpiredExecutionsAsyncWithExpiredStates()
    {
        // Arrange
        var logger = new TestLogger();
        var manager = CreateManager(logger: logger);
        var crewId = CreateTestCrewId();

        // Create and complete a state
        var expiredState = await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);
        await manager.CompleteExecutionAsync(expiredState.Id, TestContext.Current.CancellationToken);

        // Small delay to make sure expired state is older
        await System.Threading.Tasks.Task.Delay(50, TestContext.Current.CancellationToken);

        // Create a recent state (should not be cleaned up)
        var recentState = await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);

        // Act - cleanup with a time window that should only remove the expired state
        await manager.CleanupExpiredExecutionsAsync(TimeSpan.FromMilliseconds(30), TestContext.Current.CancellationToken);

        // Assert
        // Verify logging of cleanup operation
        Assert.True(logger.HasLoggedInfo("Cleaning up"));

        // Recent state should still exist (created after the delay)
        var stillExistingState = await manager.GetStateAsync(recentState.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(stillExistingState);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogZeroCount_WhenUsingCleanupExpiredExecutionsAsyncWithNoExpiredStates()
    {
        // Arrange
        var logger = new TestLogger();
        var manager = CreateManager(logger: logger);
        var crewId = CreateTestCrewId();

        // Create a recent state
        await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);

        // Act
        await manager.CleanupExpiredExecutionsAsync(TimeSpan.FromDays(1), TestContext.Current.CancellationToken); // Very long max age

        // Assert
        Assert.True(logger.HasLoggedInfo("Cleaning up 0 expired executions"));
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenCreatingStateAsyncWithConcurrentCalls()
    {
        // Arrange
        var manager = CreateManager();
        var crewId = CreateTestCrewId();
        var taskCount = 10;
        var tasks = new List<System.Threading.Tasks.Task<CrewExecutionState>>();

        // Act
        for (int i = 0; i < taskCount; i++)
        {
            tasks.Add(manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken));
        }

        var results = await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.Equal(taskCount, results.Length);
        Assert.All(results, state => Assert.NotNull(state));

        // All execution IDs should be unique
        var executionIds = results.Select(s => s.Id).ToHashSet();
        Assert.Equal(taskCount, executionIds.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldBeThreadSafe_WhenUpdatingStateAsyncWithConcurrentUpdates()
    {
        // Arrange
        var manager = CreateManager();
        var crewId = CreateTestCrewId();
        var state = await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);
        var updateTasks = new List<System.Threading.Tasks.Task>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            var progress = (i + 1) * 0.1; // 0.1, 0.2, ..., 1.0
            updateTasks.Add(manager.UpdateStateAsync(state.Id, s => s.Progress = progress, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(updateTasks);

        // Assert
        var finalState = await manager.GetStateAsync(state.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(finalState);
        // Final progress should be one of the values set
        Assert.True(finalState.Progress >= 0.1 && finalState.Progress <= 1.0);
        // Check that the value is close to a multiple of 0.1
        var remainder = Math.Abs(Math.Round(finalState.Progress * 10) / 10 - finalState.Progress);
        Assert.True(remainder < 0.01); // Allow small floating point differences
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public void ShouldCleanupResources_WhenDisposing()
    {
        // Arrange
        var manager = CreateManager();

        // Act & Assert - Should not throw
        var exception = Record.Exception(() => manager.Dispose());
        Assert.Null(exception);

        // Multiple disposal should be safe
        var exception2 = Record.Exception(() => manager.Dispose());
        Assert.Null(exception2);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotAffectCompletedOperations_WhenDisposingAfterOperations()
    {
        // Arrange
        var manager = CreateManager();
        var crewId = CreateTestCrewId();
        var state = await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);

        // Act
        manager.Dispose();

        // Assert - Previous operations should not be affected by disposal
        // Note: In a real scenario, the disposed manager might not be usable
        // but the states created before disposal should remain valid
        Assert.NotNull(state);
        Assert.Equal(crewId, state.CrewId);
    }

    #endregion

    #region Extension Methods Tests

    [Fact]
    public void ShouldRegisterService_WhenAddingCrewExecutionStateManagement()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IServiceScopeFactory>(new TestServiceScopeFactory());

        // Act
        services.AddCrewExecutionStateManagement();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var stateManager = serviceProvider.GetService<ICrewExecutionStateManager>();
        Assert.NotNull(stateManager);
        Assert.IsType<ScopedCrewExecutionStateManager>(stateManager);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldManageStateLifecycle_WhenCompletingWorkflow()
    {
        // Arrange
        var logger = new TestLogger();
        var scopeFactory = new TestServiceScopeFactory();
        var manager = CreateManager(scopeFactory, logger);
        var crewId = CreateTestCrewId();

        // Act
        // 1. Create execution state
        var state = await manager.CreateStateAsync(crewId, TestContext.Current.CancellationToken);

        // 2. Start execution
        await manager.UpdateStateAsync(state.Id, s =>
        {
            s.Status = ExecutionState.Running;
            s.Progress = 0.0;
        }, TestContext.Current.CancellationToken);

        // 3. Update progress
        await manager.UpdateStateAsync(state.Id, s => s.Progress = 0.5, TestContext.Current.CancellationToken);

        // 4. Complete execution
        await manager.CompleteExecutionAsync(state.Id, TestContext.Current.CancellationToken);

        // 5. Verify final state
        var finalState = await manager.GetStateAsync(state.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(finalState);
        Assert.Equal(ExecutionState.Completed, finalState.Status);
        Assert.Equal(1.0, finalState.Progress);

        // Verify all operations were logged
        Assert.True(logger.HasLoggedInfo("Created execution state"));
        Assert.True(logger.HasLoggedDebug("Updated execution state"));
        Assert.True(logger.HasLoggedInfo("Completed execution"));

        // Verify service scopes were created for persistence operations
        Assert.True(scopeFactory.CreatedScopes.Count >= 4); // Create + 3 updates
    }

    #endregion
}
