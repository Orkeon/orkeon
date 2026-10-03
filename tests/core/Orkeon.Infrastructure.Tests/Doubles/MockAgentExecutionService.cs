using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Task;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IAgentExecutionService with call tracking and configurable results.
/// </summary>
public class MockAgentExecutionService : IAgentExecutionService
{
    private TaskResult _executeResult = new(
        true,
        "Mock output",
        null,
        Array.Empty<ToolUsage>(),
        TimeSpan.FromMilliseconds(100));

    // --- Tracking ---
    // Consensus/parallel strategies invoke ExecuteTaskAsync from multiple threads at once
    // (Task.Run fan-out per agent), so the call counter must be incremented atomically;
    // a plain ++ races and loses increments, yielding intermittent under-counts.
    private int _executeTaskCallCount;
    public int ExecuteTaskCallCount => _executeTaskCallCount;
    public DomainAgent? LastExecuteAgent { get; private set; }
    public ICrewTask? LastExecuteTask { get; private set; }
    public SimpleExecutionContext? LastExecuteContext { get; private set; }

    public int ExecuteTaskGenericCallCount { get; private set; }

    private Func<DomainAgent, ICrewTask, SimpleExecutionContext, CancellationToken, TaskResult>? _executeFunc;

    // --- Configuration ---
    public void SetExecuteResult(TaskResult result) => _executeResult = result;

    public void SetExecuteSuccess(string output) =>
        _executeResult = new TaskResult(true, output, null, Array.Empty<ToolUsage>(), TimeSpan.FromMilliseconds(100));

    public void SetExecuteError(string error) =>
        _executeResult = new TaskResult(false, "", null, Array.Empty<ToolUsage>(), TimeSpan.FromMilliseconds(100), error);

    /// <summary>
    /// Sets a function that generates the TaskResult dynamically based on input parameters.
    /// When set, this takes priority over SetExecuteResult.
    /// </summary>
    public void SetExecuteFunc(Func<DomainAgent, ICrewTask, SimpleExecutionContext, CancellationToken, TaskResult> func)
        => _executeFunc = func;

    // --- IAgentExecutionService ---
    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent,
        ICrewTask task,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        System.Threading.Interlocked.Increment(ref _executeTaskCallCount);
        LastExecuteAgent = agent;
        LastExecuteTask = task;
        LastExecuteContext = context;

        var result = _executeFunc != null
            ? _executeFunc(agent, task, context, cancellationToken)
            : _executeResult;
        return System.Threading.Tasks.Task.FromResult(result);
    }

    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent,
        ICrewTask task,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
        where TOutput : class
    {
        ExecuteTaskGenericCallCount++;
        LastExecuteAgent = agent;
        LastExecuteTask = task;
        LastExecuteContext = context;

        var result = new TaskResult<TOutput>(
            _executeResult.Success,
            _executeResult.Output,
            _executeResult.StructuredOutput as TOutput,
            _executeResult.ToolsUsed,
            _executeResult.ExecutionTime,
            _executeResult.Error);

        return System.Threading.Tasks.Task.FromResult(result);
    }
}
