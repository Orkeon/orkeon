using Orkeon.Application.Callback;

namespace Orkeon.Application.Tests.Callbacks;

/// <summary>
/// Complements <see cref="CallbackExamplesTests"/> by invoking the callback delegates the existing
/// suite never fires: the simple callbacks' <c>OnFailed</c>/<c>OnFinally</c> branches and the full
/// async callback chain. These invocations drive the source-generated <c>CallbackExamplesLog</c>
/// logger methods (LogTaskFailed, LogTaskFinished, LogAnalytics*, LogProgressSaved,
/// LogAnalysisCompleted, LogAnalysisFailed) that otherwise stay uncovered.
/// </summary>
public class CallbackExamplesLogInvocationTests
{
    private static TaskStartedContext StartedContext() => new(
        TaskId: "task-1",
        Description: "Run analysis",
        ExpectedOutput: "report",
        AgentId: "agent-1",
        AgentRole: "Analyst",
        Timestamp: DateTime.UtcNow);

    private static TaskProgressContext ProgressContext(double pct) => new(
        TaskId: "task-1",
        AgentId: "agent-1",
        StepNumber: 3,
        TotalSteps: 6,
        ProgressPercentage: pct,
        CurrentAction: "crunching",
        Timestamp: DateTime.UtcNow);

    private static TaskCompletedContext FailedContext() => new(
        TaskId: "task-1",
        AgentId: "agent-1",
        Outcome: new TaskExecutionOutcome(false, null, null, "boom"),
        Duration: TimeSpan.FromSeconds(3),
        StepsExecuted: 2,
        Timestamp: DateTime.UtcNow);

    private static TaskCompletedContext CompletedContext() => new(
        TaskId: "task-1",
        AgentId: "agent-1",
        Outcome: new TaskExecutionOutcome(true, "done", null, null),
        Duration: TimeSpan.FromSeconds(8),
        StepsExecuted: 6,
        Timestamp: DateTime.UtcNow);

    [Fact]
    public async System.Threading.Tasks.Task SimpleCallbacks_OnFailed_LogsErrorMessage()
    {
        var logger = new TestLogger<LoggingCallbackHandler>();
        var callbacks = CallbackExamples.CreateSimpleTaskCallbacks(logger);

        await callbacks.OnFailed!.Invoke(FailedContext());

        Assert.Contains(logger.LogMessages, m => m.Contains("Task failed: boom", StringComparison.Ordinal));
    }

    [Fact]
    public async System.Threading.Tasks.Task SimpleCallbacks_OnFinally_LogsCompletionFlag()
    {
        var logger = new TestLogger<LoggingCallbackHandler>();
        var callbacks = CallbackExamples.CreateSimpleTaskCallbacks(logger);

        await callbacks.OnFinally!.Invoke(CompletedContext());

        Assert.Contains(logger.LogMessages, m => m.Contains("Task finished (Success: True)", StringComparison.Ordinal));
    }

    [Fact]
    public async System.Threading.Tasks.Task AsyncCallbacks_OnStarted_LogsStartAndInitialization()
    {
        var logger = new TestLogger<LoggingCallbackHandler>();
        var callbacks = CallbackExamples.CreateAsyncTaskCallbacks(logger);

        await callbacks.OnStarted!.Invoke(StartedContext());

        Assert.Contains(logger.LogMessages, m => m.Contains("Analytics Task Started: Run analysis", StringComparison.Ordinal));
        Assert.Contains(logger.LogMessages, m => m.Contains("Analytics systems initialized", StringComparison.Ordinal));
    }

    [Fact]
    public async System.Threading.Tasks.Task AsyncCallbacks_OnProgress_LogsSavedProgress()
    {
        var logger = new TestLogger<LoggingCallbackHandler>();
        var callbacks = CallbackExamples.CreateAsyncTaskCallbacks(logger);

        await callbacks.OnProgress!.Invoke(ProgressContext(42.0));

        Assert.Contains(logger.LogMessages, m => m.Contains("Progress saved: 42%", StringComparison.Ordinal));
    }

    [Fact]
    public async System.Threading.Tasks.Task AsyncCallbacks_OnCompleted_LogsAnalysisCompleted()
    {
        var logger = new TestLogger<LoggingCallbackHandler>();
        var callbacks = CallbackExamples.CreateAsyncTaskCallbacks(logger);

        await callbacks.OnCompleted!.Invoke(CompletedContext());

        Assert.Contains(logger.LogMessages, m => m.Contains("Analysis completed!", StringComparison.Ordinal));
    }

    [Fact]
    public async System.Threading.Tasks.Task AsyncCallbacks_OnFailed_LogsAnalysisFailed()
    {
        var logger = new TestLogger<LoggingCallbackHandler>();
        var callbacks = CallbackExamples.CreateAsyncTaskCallbacks(logger);

        await callbacks.OnFailed!.Invoke(FailedContext());

        Assert.Contains(logger.LogMessages, m => m.Contains("Analysis failed: boom", StringComparison.Ordinal));
    }

    [Fact]
    public void AsyncCallbacks_OnFinally_IsNotConfigured()
    {
        // The async example wires only Started/Progress/Completed/Failed; OnFinally stays null.
        var callbacks = CallbackExamples.CreateAsyncTaskCallbacks(new TestLogger<LoggingCallbackHandler>());

        Assert.Null(callbacks.OnFinally);
    }

    [Fact]
    public void Factories_CreateAgentEntities()
    {
        var agent = CallbackExamples.CreateAgent();
        var callbackAgent = CallbackExamples.CreateAgentForCallbacks();
        var task = CallbackExamples.CreateTask();

        Assert.Equal("Research Analyst", agent.Role.Value);
        Assert.Equal("Content Writer", callbackAgent.Role.Value);
        Assert.Equal("Research the latest AI trends in 2024", task.Description.Value);
    }
}
