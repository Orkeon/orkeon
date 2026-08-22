using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Agent
{
    /// <summary>
    /// Improved agent execution service that delegates responsibilities to specialized services.
    /// Follows Single Responsibility Principle by orchestrating execution without handling callbacks, memory, or parsing directly.
    /// </summary>
    public partial class AgentExecutionService : IAgentExecutionService
    {
        private readonly ILogger<AgentExecutionService> _logger;
        private readonly IExecutionOrchestrator _executionOrchestrator;
        private readonly ICallbackOrchestrator _callbackOrchestrator;
        private readonly IMemoryCoordinator _memoryCoordinator;
        private readonly IPerformanceMetrics? _performanceMetrics;
        private readonly EventHub.IEventHubCallerContext? _hubCallerContext;

        private const string TaskCastErrorMessage = "Task must be a Domain Task";

        /// <summary>
        /// Casts an <see cref="ICrewTask"/> to a <see cref="CrewTask"/>, throwing if the cast fails.
        /// </summary>
        internal static CrewTask CastToDomainTask(ICrewTask task)
        {
            ArgumentNullException.ThrowIfNull(task);
            return task as CrewTask ?? throw new InvalidOperationException(TaskCastErrorMessage);
        }

        /// <summary>
        /// Initializes a new instance of <see cref="AgentExecutionService"/>.
        /// </summary>
        public AgentExecutionService(
            ILogger<AgentExecutionService> logger,
            IExecutionOrchestrator executionOrchestrator,
            ICallbackOrchestrator callbackOrchestrator,
            IMemoryCoordinator memoryCoordinator,
            IPerformanceMetrics? performanceMetrics = null,
            EventHub.IEventHubCallerContext? hubCallerContext = null)
        {
            ArgumentNullException.ThrowIfNull(logger);
            _logger = logger;
            ArgumentNullException.ThrowIfNull(executionOrchestrator);
            _executionOrchestrator = executionOrchestrator;
            ArgumentNullException.ThrowIfNull(callbackOrchestrator);
            _callbackOrchestrator = callbackOrchestrator;
            ArgumentNullException.ThrowIfNull(memoryCoordinator);
            _memoryCoordinator = memoryCoordinator;
            _performanceMetrics = performanceMetrics;
            _hubCallerContext = hubCallerContext;
        }

        /// <summary>
        /// Execute Task Async.
        /// </summary>
        public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
            DomainAgent agent,
            ICrewTask task,
            SimpleExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(agent);
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            return ExecuteTaskCoreAsync(agent, task, context, cancellationToken);
        }

        /// <summary>
        /// Execute Task Async.
        /// </summary>
        public async System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
            DomainAgent agent,
            ICrewTask task,
            SimpleExecutionContext context,
            CancellationToken cancellationToken = default)
            where TOutput : class
        {
            var result = await ExecuteTaskAsync(agent, task, context, cancellationToken).ConfigureAwait(false);

            // Try to convert structured output to TOutput
            TOutput? typedOutput = null;
            if (result.StructuredOutput != null)
            {
                typedOutput = result.StructuredOutput as TOutput;
            }

            return new TaskResult<TOutput>(
                Success: result.Success,
                RawOutput: result.Output,
                StructuredOutput: typedOutput,
                ToolsUsed: result.ToolsUsed,
                ExecutionTime: result.ExecutionTime,
                Error: result.Error);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Service-boundary fault barrier: any failure inside task execution is converted to a failed TaskResult and surfaced via NotifyTaskCompletedAsync so one task cannot crash the agent execution loop.")]
        private async System.Threading.Tasks.Task<TaskResult> ExecuteTaskCoreAsync(
            DomainAgent agent,
            ICrewTask task,
            SimpleExecutionContext context,
            CancellationToken cancellationToken)
        {
            LogAgentStartingTask(agent.Role, task.Description);

            // The hub identity for this task comes from the execution context, not from the
            // ambient value: the context carries the authoritative crew id on every path —
            // including ones where nothing upstream pushed (the streaming service, direct
            // service use). And the push happens only after a forced suspension: an
            // AsyncLocal mutated in an async method's synchronous prefix mutates the
            // *caller's* execution context, so without the fork the strategy loop would
            // carry a stale AgentId from task to task.
            if (_hubCallerContext is not null)
                await System.Threading.Tasks.Task.Yield();

            using var hubCallerScope = _hubCallerContext?.Push(
                new EventHub.EventHubCaller(context.CrewId, agent.Id));

            var startTime = DateTime.UtcNow;

            // Notify task started
            await _callbackOrchestrator.NotifyTaskStartedAsync(
                agent, CastToDomainTask(task),
                startTime,
                handlers: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            try
            {
                // Execute the task
                var result = await _executionOrchestrator.ExecuteTaskCoreAsync(
                    agent,
                    CastToDomainTask(task),
                    context, // Use context directly
                    cancellationToken).ConfigureAwait(false);

                // Record metrics
                _performanceMetrics?.RecordTaskExecution(
                    agent.Role,
                    task.TaskId.Value.ToString(),
                    result.ExecutionTime,
                    result.Success);

                // Store result in memory if successful
                if (result.Success && !string.IsNullOrEmpty(result.Output))
                {
                    await _memoryCoordinator.StoreTaskResultAsync(
                        agent, CastToDomainTask(task), result.Output, context, cancellationToken).ConfigureAwait(false);
                }

                // Notify task completed
                await _callbackOrchestrator.NotifyTaskCompletedAsync(
                    agent,
                    CastToDomainTask(task),
                    new TaskCompletionInfo { Result = result, StepsExecuted = 1, StartTime = startTime },
                    handlers: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                return result;
            }
            catch (Exception ex)
            {
                LogTaskExecutionError(ex, agent.Role);

                var errorResult = new TaskResult(
                    Success: false,
                    Output: string.Empty,
                    StructuredOutput: null,
                    ToolsUsed: [],
                    ExecutionTime: DateTime.UtcNow - startTime,
                    Error: ex.Message);

                // Notify task failed
                await _callbackOrchestrator.NotifyTaskCompletedAsync(
                    agent,
                    CastToDomainTask(task),
                    new TaskCompletionInfo { Result = errorResult, StepsExecuted = 0, StartTime = startTime },
                    handlers: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                return errorResult;
            }
        }

        /// <summary>
        /// Plan Task Execution Async.
        /// </summary>
        public System.Threading.Tasks.Task<TaskExecutionPlan> PlanTaskExecutionAsync(
            DomainAgent agent,
            ICrewTask task,
            SimpleExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(agent);
            ArgumentNullException.ThrowIfNull(task);

            return _executionOrchestrator.PlanExecutionAsync(
                agent,
                CastToDomainTask(task),
                context,
                cancellationToken);
        }

        /// <summary>
        /// Can Execute Task Async.
        /// </summary>
        public System.Threading.Tasks.Task<bool> CanExecuteTaskAsync(
            DomainAgent agent,
            ICrewTask task,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(agent);
            ArgumentNullException.ThrowIfNull(task);
            return CanExecuteTaskCoreAsync(agent, task, cancellationToken);
        }

        private async System.Threading.Tasks.Task<bool> CanExecuteTaskCoreAsync(
            DomainAgent agent,
            ICrewTask task,
            CancellationToken cancellationToken)
        {
            var validation = await _executionOrchestrator.ValidateExecutionAsync(
                agent,
                CastToDomainTask(task),
                cancellationToken).ConfigureAwait(false);
            return validation.CanExecute;
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Agent {Agent} starting task: {Task}")]
        private partial void LogAgentStartingTask(object? agent, object? task);

        [LoggerMessage(Level = LogLevel.Error, Message = "Error executing task for agent {Agent}")]
        private partial void LogTaskExecutionError(Exception ex, object agent);
    }
}
