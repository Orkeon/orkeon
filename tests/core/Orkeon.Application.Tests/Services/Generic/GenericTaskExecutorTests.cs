using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Task.Contexts;
using Orkeon.Application.Services.Generic;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces.Ports;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;
using TaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;
using CrewId = Orkeon.Domain.Common.CrewId;
using DomainValidationResult = Orkeon.Domain.SharedKernel.ValidationResult;
using ApplicationValidationResult = Orkeon.Application.Services.Generic.ValidationResult;

namespace Orkeon.Application.Tests.Services.Generic;

public class GenericTaskExecutorTests
{
    #region TaskExecutorBase Tests

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingTaskExecutorBaseConstructorWithNullLogger()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TestTaskExecutor(null!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenUsingTaskExecutorBaseExecuteAsyncWithNullTask()
    {
        // Arrange
        var logger = new TestLogger<TestTaskExecutor>();
        var executor = new TestTaskExecutor(logger);
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<TestContext>(
            CrewId.From(Guid.NewGuid()),
            new TestContext(),
            memoryScope,
            [],
            CancellationToken.None);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            executor.ExecuteAsync(null!, context, Xunit.TestContext.Current.CancellationToken));
        Assert.Equal("task", exception.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenUsingTaskExecutorBaseExecuteAsyncWithNullContext()
    {
        // Arrange
        var logger = new TestLogger<TestTaskExecutor>();
        var executor = new TestTaskExecutor(logger);
        var task = new TestTask("Test task");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            executor.ExecuteAsync(task, null!, Xunit.TestContext.Current.CancellationToken));
        Assert.Equal("context", exception.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowTaskValidationException_WhenUsingTaskExecutorBaseExecuteAsyncWithValidationFailure()
    {
        // Arrange
        var logger = new TestLogger<TestTaskExecutor>();
        var executor = new TestTaskExecutor(logger) { ShouldFailValidation = true };
        var task = new TestTask("Test task");
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<TestContext>(
            CrewId.From(Guid.NewGuid()),
            new TestContext(),
            memoryScope,
            [],
            CancellationToken.None);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<TaskValidationException>(() =>
            executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken));
        Assert.Contains("Validation failed", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnResult_WhenUsingTaskExecutorBaseExecuteAsyncWithSuccessfulExecution()
    {
        // Arrange
        var logger = new TestLogger<TestTaskExecutor>();
        var executor = new TestTaskExecutor(logger);
        var task = new TestTask("Process data");
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<TestContext>(
            CrewId.From(Guid.NewGuid()),
            new TestContext { Value = "Input" },
            memoryScope,
            [],
            CancellationToken.None);

        // Act
        var result = await executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Processed: Process data with Input", result.Output);
        Assert.True(executor.PostProcessCalled);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectCancellation_WhenUsingTaskExecutorBaseExecuteAsyncWithCancellationToken()
    {
        // Arrange
        var logger = new TestLogger<TestTaskExecutor>();
        var executor = new TestTaskExecutor(logger) { DelayMs = 1000 };
        var task = new TestTask("Long running task");
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<TestContext>(
            CrewId.From(Guid.NewGuid()),
            new TestContext(),
            memoryScope,
            [],
            CancellationToken.None);
        using var cts = new CancellationTokenSource();

        // Act
        cts.CancelAfter(50);

        // Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            executor.ExecuteAsync(task, context, cts.Token));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogExecutionSteps_WhenUsingTaskExecutorBaseLogging()
    {
        // Arrange
        var logger = new TestLogger<TestTaskExecutor>();
        var executor = new TestTaskExecutor(logger);
        var task = new TestTask("Test task");
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<TestContext>(
            CrewId.From(Guid.NewGuid()),
            new TestContext(),
            memoryScope,
            [],
            CancellationToken.None);

        // Act
        await executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(logger.LoggedMessages, m =>
            m.LogLevel == LogLevel.Information && m.Message.Contains("Executing TestTask"));
        Assert.Contains(logger.LoggedMessages, m =>
            m.LogLevel == LogLevel.Information && m.Message.Contains("Successfully executed TestTask"));
    }

    #endregion

    #region TaskExecutorFactory Tests

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingTaskExecutorFactoryUsingConstructorWithNullServiceProvider()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TaskExecutorFactory(null!, new TestLogger<TaskExecutorFactory>()));
        Assert.Equal("serviceProvider", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnExecutor_WhenUsingTaskExecutorFactoryCreatingExecutorWithRegisteredType()
    {
        // Arrange
        var serviceProvider = new TestServiceProvider();
        var executor = new TestTaskExecutor(new TestLogger<TestTaskExecutor>());
        serviceProvider.RegisterService(typeof(ITaskExecutor<TestTask, TestResult>), executor);
        var factory = new TaskExecutorFactory(serviceProvider, new TestLogger<TaskExecutorFactory>());

        // Act
        var result = factory.GetExecutor<TestTask, TestResult>();

        // Assert
        Assert.NotNull(result);
        Assert.Same(executor, result);
    }

    [Fact]
    public void ShouldReturnNull_WhenUsingTaskExecutorFactoryCreatingExecutorWithUnregisteredType()
    {
        // Arrange
        var serviceProvider = new TestServiceProvider();
        var factory = new TaskExecutorFactory(serviceProvider, new TestLogger<TaskExecutorFactory>());

        // Act
        var result = factory.GetExecutor<TestTask, TestResult>();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnCorrectType_WhenUsingTaskExecutorFactoryGettingExecutorTypeWithKnownTask()
    {
        // Arrange
        var serviceProvider = new TestServiceProvider();
        var executor = new TestTaskExecutor(new TestLogger<TestTaskExecutor>());
        serviceProvider.RegisterService(typeof(ITaskExecutor<TestTask, object>), executor);
        var factory = new TaskExecutorFactory(serviceProvider, new TestLogger<TaskExecutorFactory>());

        // Act
        var canExecute = factory.CanExecute<TestTask>();

        // Assert
        Assert.True(canExecute);
    }

    #endregion

    #region ValidationResult Tests

    [Fact]
    public void ShouldHaveCorrectProperties_WhenUsingValidationResultUsingSuccess()
    {
        // Arrange & Act
        var result = new ApplicationValidationResult([]);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ShouldHaveCorrectProperties_WhenUsingValidationResultUsingFailureWithSingleError()
    {
        // Arrange & Act
        var result = new ApplicationValidationResult(["Task description is required"]);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Task description is required", result.Errors);
    }

    [Fact]
    public void ShouldHaveAllErrors_WhenUsingValidationResultUsingFailureWithMultipleErrors()
    {
        // Arrange
        var errors = new[] { "Error 1", "Error 2", "Error 3" };

        // Act
        var result = new ApplicationValidationResult(errors.ToList());

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
        foreach (var error in errors)
        {
            Assert.Contains(error, result.Errors);
        }
    }

    #endregion

    #region Complex Workflow Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteAllSteps_WhenUsingTaskExecutorWithCompleteWorkflow()
    {
        // Arrange
        var logger = new TestLogger<TestTaskExecutor>();
        var executor = new TestTaskExecutor(logger);
        var task = new TestTask("Complex workflow task");
        var contextData = new TestContext
        {
            Value = "Initial",
            Metadata = new Dictionary<string, object> { ["step"] = 1 }
        };
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<TestContext>(
            CrewId.From(Guid.NewGuid()),
            contextData,
            memoryScope,
            [],
            CancellationToken.None);

        // Act
        var result = await executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Processed: Complex workflow task with Initial", result.Output);
        Assert.Equal(1, result.ProcessingSteps);
        Assert.True(executor.ValidationCalled);
        Assert.True(executor.ExecutionCalled);
        Assert.True(executor.PostProcessCalled);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPropagateException_WhenUsingTaskExecutorWithExceptionInCore()
    {
        // Arrange
        var logger = new TestLogger<TestTaskExecutor>();
        var executor = new TestTaskExecutor(logger) { ShouldThrowInExecution = true };
        var task = new TestTask("Task that will fail");
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<TestContext>(
            CrewId.From(Guid.NewGuid()),
            new TestContext(),
            memoryScope,
            [],
            CancellationToken.None);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken));
        Assert.Equal("Execution failed", exception.Message);

        // Verify logging
        Assert.Contains(logger.LoggedMessages, m =>
            m.LogLevel == LogLevel.Error && m.Message.Contains("Error executing TestTask"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleIndependently_WhenUsingTaskExecutorWithMultipleExecutions()
    {
        // Arrange
        var logger = new TestLogger<TestTaskExecutor>();
        var executor = new TestTaskExecutor(logger);
        var tasks = new[]
        {
            new TestTask("Task 1"),
            new TestTask("Task 2"),
            new TestTask("Task 3")
        };
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<TestContext>(
            CrewId.From(Guid.NewGuid()),
            new TestContext { Value = "Multi" },
            memoryScope,
            [],
            CancellationToken.None);

        // Act
        var results = new List<TestResult>();
        foreach (var task in tasks)
        {
            var result = await executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);
            results.Add(result);
        }

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal("Processed: Task 1 with Multi", results[0].Output);
        Assert.Equal("Processed: Task 2 with Multi", results[1].Output);
        Assert.Equal("Processed: Task 3 with Multi", results[2].Output);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingTaskExecutorWithNullContextData()
    {
        // Arrange
        using var memoryScope = new TestMemoryScope();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ExecutionContext<TestContext>(
                CrewId.From(Guid.NewGuid()),
                null!,
                memoryScope,
                [],
                CancellationToken.None));
        Assert.Equal("data", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingTaskExecutorWithEmptyTaskDescription()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => new TestTask(""));
        Assert.Contains("value cannot be null or whitespace", exception.Message);
    }

    #endregion
}

#region Test Implementations

internal class TestTask : ICrewTask
{
    private readonly List<TaskId> _dependencies = [];

    public TestTask(string description)
    {
        TaskId = TaskId.Create();
        Description = TaskDescription.From(description);
        ExpectedOutput = ExpectedOutput.From("Test output");
        CreatedAt = DateTime.UtcNow;
    }

    public TaskId TaskId { get; }
    public TaskDescription Description { get; }
    public ExpectedOutput ExpectedOutput { get; }
    public AgentId? AssignedAgent { get; private set; }
    public TaskStatus Status { get; private set; } = TaskStatus.Pending;
    public TaskOutput? Output { get; private set; }
    public IReadOnlyList<TaskId> Dependencies => _dependencies.AsReadOnly();
    public DateTime CreatedAt { get; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public bool AsyncExecution => false;
    public JsonSchema? OutputJson => null;
    public Type? OutputPydantic => null;
    public string? OutputFile => null;
    public bool HumanInput => false;

    // Domain-specific properties
    public ITaskContext<Dictionary<string, object>>? Context { get; set; }
    public bool AllowDelegation { get; set; } = true;
    public bool HumanInputRequired { get; set; }
    public int MaxIterations { get; set; } = 1;
    public TimeSpan? Timeout { get; set; }
    public Dictionary<string, object> Metadata { get; } = [];

    public void AssignTo(AgentId agentId) => AssignedAgent = agentId;

    public void Start(AgentId agentId)
    {
        AssignedAgent = agentId;
        Status = TaskStatus.InProgress;
        StartedAt = DateTime.UtcNow;
    }

    public void Complete(AgentId agentId, TaskOutput output)
    {
        Output = output;
        Status = TaskStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void Fail(string errorMessage, Exception? exception = null)
    {
        Output = TaskOutput.Create(
            errorMessage,
            "error",
            null,
            TaskId,
            false,
            TimeSpan.Zero,
            null,
            DateTime.UtcNow);
        Status = TaskStatus.Failed;
        CompletedAt = DateTime.UtcNow;
    }

    public bool CanExecute(Func<TaskId, bool> isTaskCompleted)
    {
        return Dependencies.All(isTaskCompleted);
    }

    public DomainValidationResult ValidateOutput(TaskOutput output)
    {
        return DomainValidationResult.Success();
    }

    public string GetContextSummary() => Description.Value;

    public TimeSpan GetExecutionTime()
    {
        if (StartedAt == null || CompletedAt == null) return TimeSpan.Zero;
        return CompletedAt.Value - StartedAt.Value;
    }

    // Legacy property mappings
    public TaskId Id => TaskId;
    public AgentId? AgentId => AssignedAgent;
    public void AssignToAgent(AgentId agentId) => AssignTo(agentId);
    public void UpdateStatus(TaskStatus newStatus) => Status = newStatus;
    public void SetOutput(TaskOutput output) => Complete(AssignedAgent ?? AgentId.Create(), output);
    public void AddDependency(TaskId taskId) => _dependencies.Add(taskId);
    public void UpdateContext(ITaskContext<Dictionary<string, object>> context) => Context = context;
}

internal class TestContext
{
    public string? Value { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = [];
}

internal class TestResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public int ProcessingSteps { get; set; }
}

internal class TestTaskExecutor : TaskExecutorBase<TestTask, TestResult>
{
    public bool ShouldFailValidation { get; set; }
    public bool ShouldThrowInExecution { get; set; }
    public int DelayMs { get; set; }

    public bool ValidationCalled { get; private set; }
    public bool ExecutionCalled { get; private set; }
    public bool PostProcessCalled { get; private set; }

    public TestTaskExecutor(ILogger<TestTaskExecutor> logger) : base(logger)
    {
    }

    protected override async System.Threading.Tasks.Task<ApplicationValidationResult> ValidateAsync(TestTask task, IExecutionContext context)
    {
        ValidationCalled = true;

        if (ShouldFailValidation)
        {
            return new ApplicationValidationResult(["Validation failed for testing"]);
        }

        return await base.ValidateAsync(task, context);
    }

    protected override async System.Threading.Tasks.Task<TestResult> ExecuteCoreAsync(TestTask task, IExecutionContext context, CancellationToken cancellationToken)
    {
        ExecutionCalled = true;

        if (DelayMs > 0)
        {
            await System.Threading.Tasks.Task.Delay(DelayMs, cancellationToken);
        }

        if (ShouldThrowInExecution)
        {
            throw new InvalidOperationException("Execution failed");
        }

        var typed = GetTypedContext<TestContext>(context);
        var contextValue = typed.Data?.Value ?? "<null>";
        return new TestResult
        {
            Success = true,
            Output = $"Processed: {task.Description.Value} with {contextValue}",
            ProcessingSteps = 1
        };
    }

    protected override async System.Threading.Tasks.Task PostProcessAsync(TestTask task, IExecutionContext context, TestResult result)
    {
        PostProcessCalled = true;
        await base.PostProcessAsync(task, context, result);
    }
}

internal class TestMemoryScope : IMemoryScope
{
    public string ScopeId => "test-scope";
    public string AgentId => Guid.NewGuid().ToString();

    public static System.Threading.Tasks.Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        return System.Threading.Tasks.Task.FromResult<T?>(null);
    }

    public static System.Threading.Tasks.Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public static System.Threading.Tasks.Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.FromResult(false);
    }

    public static System.Threading.Tasks.Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public async System.Threading.Tasks.Task<T> ExecuteInScopeAsync<T>(Func<System.Threading.Tasks.Task<T>> operation)
    {
        return await operation();
    }

    public async System.Threading.Tasks.Task ExecuteInScopeAsync(Func<System.Threading.Tasks.Task> operation)
    {
        await operation();
    }

    public void Dispose() { }
}

internal class TestServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = [];

    public void RegisterService(Type serviceType, object service)
    {
        _services[serviceType] = service;
    }

    public object? GetService(Type serviceType)
    {
        return _services.TryGetValue(serviceType, out var service) ? service : null;
    }
}

internal class TestLogger<T> : ILogger<T>
{
    public List<LoggedMessage> LoggedMessages { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => new TestDisposable();

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        LoggedMessages.Add(new LoggedMessage
        {
            LogLevel = logLevel,
            Message = formatter(state, exception),
            Exception = exception
        });
    }

    internal class LoggedMessage
    {
        public LogLevel LogLevel { get; init; }
        public string Message { get; init; } = string.Empty;
        public Exception? Exception { get; init; }
    }

    private class TestDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

#endregion
