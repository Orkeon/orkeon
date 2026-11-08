using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Checkpointing;

/// <summary>
/// Checkpoints every task a crew run completes, and answers from the checkpoint the tasks a resumed
/// run had already completed (FWK-07). Every process strategy runs its tasks through
/// <see cref="IAgentExecutionService"/>, so this decorator is the one place where the six modes meet:
/// none of them knows about checkpoints.
/// </summary>
/// <remarks>
/// A task is checkpointed under <c>"{TaskKey}#{n}"</c>: <c>TaskKey</c> is its name in the crew
/// definition (its <see cref="Orkeon.Domain.Task.ICrewTask.TaskId"/> is new at every load) and <c>n</c> the
/// ordinal of the invocation of that key in the run, which tells a Hierarchical revision or a Graph cycle
/// from the first run of the task. Next to its output the checkpoint keeps a fingerprint of the task
/// (description, expected output, agent, dependencies): when a resumed run finds a task whose definition
/// changed since, that task and every task that runs after it run again, because what they would read
/// of the earlier outputs is stale too. The decorator is inactive outside a run (<see cref="IRunScope"/>)
/// and for a task without a key, such as one delegated by an agent. Only a successful result is
/// checkpointed: a task that failed runs again when the run is resumed. A checkpoint that cannot be
/// written fails the task.
/// </remarks>
public sealed partial class CheckpointingAgentExecutionService : IAgentExecutionService, ICheckpointedDecisions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // What a run knows while it goes, kept on the run's own scope object: every decorator the run reaches
    // shares it and it dies with the run.
    private readonly ConditionalWeakTable<RunScope, RunState> _runs = new();

    private readonly IAgentExecutionService _inner;
    private readonly IRunScope _runScope;
    private readonly ICheckpointManager _checkpoints;
    private readonly ITaskRepository? _tasks;
    private readonly ILogger<CheckpointingAgentExecutionService> _logger;

    /// <summary>Initializes a new instance of <see cref="CheckpointingAgentExecutionService"/>.</summary>
    /// <param name="inner">The execution service that runs the tasks.</param>
    /// <param name="runScope">The ambient run the checkpoints belong to.</param>
    /// <param name="checkpoints">Where the completed tasks are written.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="tasks">Resolves a task's dependencies to their keys, for its fingerprint; without it a dependency counts by position only.</param>
    public CheckpointingAgentExecutionService(
        IAgentExecutionService inner,
        IRunScope runScope,
        ICheckpointManager checkpoints,
        ILogger<CheckpointingAgentExecutionService> logger,
        ITaskRepository? tasks = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(runScope);
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(logger);
        _inner = inner;
        _runScope = runScope;
        _checkpoints = checkpoints;
        _logger = logger;
        _tasks = tasks;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (await OpenAsync(agent, task, cancellationToken).ConfigureAwait(false) is not { } call)
            return await _inner.ExecuteTaskAsync(agent, task, context, cancellationToken).ConfigureAwait(false);

        if (call.Stored is { } stored)
            return stored.ToResult();

        var result = await RunAsync(call, () => _inner.ExecuteTaskAsync(agent, task, context, cancellationToken)).ConfigureAwait(false);
        if (result.Success)
            await WriteAsync(call, StoredResult.Of(result, call.Fingerprint), result.RetryCount, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class
    {
        ArgumentNullException.ThrowIfNull(task);
        if (await OpenAsync(agent, task, cancellationToken).ConfigureAwait(false) is not { } call)
            return await _inner.ExecuteTaskAsync<TOutput>(agent, task, context, cancellationToken).ConfigureAwait(false);

        if (call.Stored is { } stored)
            return stored.ToResult<TOutput>();

        var result = await RunAsync(call, () => _inner.ExecuteTaskAsync<TOutput>(agent, task, context, cancellationToken)).ConfigureAwait(false);
        if (result.Success)
            await WriteAsync(call, StoredResult.Of(result, call.Fingerprint), result.RetryCount, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    async System.Threading.Tasks.Task<TaskResult> ICheckpointedDecisions.RunDecisionAsync(
        ICrewTask task, Func<System.Threading.Tasks.Task<TaskResult>> decide, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(decide);
        if (await OpenAsync(null, task, cancellationToken).ConfigureAwait(false) is not { } call)
            return await decide().ConfigureAwait(false);

        if (call.Stored is { } stored)
            return stored.ToResult();

        var result = await RunAsync(call, decide).ConfigureAwait(false);
        if (result.Success)
            await WriteAsync(call, StoredResult.Of(result, call.Fingerprint), result.RetryCount, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Opens the invocation: the run in flight, the key of this call, the task's fingerprint and what the
    /// checkpoint holds for it; null when the decorator has nothing to do (no run, or a task that has no
    /// name in a crew definition).
    /// </summary>
    private async System.Threading.Tasks.Task<Call?> OpenAsync(DomainAgent? agent, ICrewTask task, CancellationToken ct)
    {
        if (_runScope.Current is not { } run || task.Key is not { Length: > 0 } taskKey)
            return null;

        var state = _runs.GetOrCreateValue(run);
        var key = $"{taskKey}#{state.Ordinals.AddOrUpdate(taskKey, 1, static (_, n) => n + 1)}";
        var fingerprint = await FingerprintAsync(agent, task, ct).ConfigureAwait(false);
        return new Call(run, state, key, fingerprint, Lookup(run, state, key, fingerprint, task));
    }

    private StoredResult? Lookup(RunScope run, RunState state, string key, string fingerprint, ICrewTask task)
    {
        if (state.Stale || run.Resume is not { } plan || !plan.CompletedOutputs.TryGetValue(key, out var output) || output is null)
            return null;

        StoredResult? stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredResult>(output, Json);
        }
        catch (JsonException)
        {
            // An output written by something else (a checkpoint of an older shape): the task runs again.
            return null;
        }

        if (stored is null)
            return null;

        if (!string.Equals(stored.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            // The task was edited since the run: its output is not the one this definition would give,
            // and what the tasks after it would read of it is stale too. It and everything that runs
            // after it from now on run again.
            state.Stale = true;
            LogTaskChanged(key, run.ExecutionId);
            return null;
        }

        plan.ResumedTaskIds[task.TaskId.Value.ToString()] = 0;
        LogTaskResumed(key, run.ExecutionId);
        return stored;
    }

    private async System.Threading.Tasks.Task<string> FingerprintAsync(DomainAgent? agent, ICrewTask task, CancellationToken ct)
    {
        // The agent by what it is told to be, not by its id, which is new at every load; the dependencies
        // by their keys, for the same reason.
        var dependencies = new List<string>();
        if (task.Dependencies.Count > 0)
        {
            var known = _tasks is null
                ? []
                : await _tasks.GetByIdsAsync(task.Dependencies, ct).ConfigureAwait(false);
            dependencies.AddRange(task.Dependencies.Select(id => known.FirstOrDefault(t => t.Id == id)?.Key ?? "?"));
            dependencies.Sort(StringComparer.Ordinal);
        }

        var text = string.Join(
            '\u0001',
            task.Description.Value,
            task.ExpectedOutput.Value,
            agent?.Role.Value ?? string.Empty,
            agent?.Goal.Value ?? string.Empty,
            string.Join('\u0002', dependencies));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    private async System.Threading.Tasks.Task<T> RunAsync<T>(Call call, Func<System.Threading.Tasks.Task<T>> execute)
    {
        try
        {
            return await execute().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _checkpoints.MarkFailedAsync(call.Run.ExecutionId.ToString(), call.Key, ex, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private System.Threading.Tasks.Task WriteAsync(Call call, StoredResult result, int retryCount, CancellationToken ct) =>
        _checkpoints.CheckpointAsync(call.Run.ExecutionId.ToString(), call.Key, JsonSerializer.Serialize(result, Json), retryCount, ct);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {Key} of run {RunId} answered from its checkpoint: no model was asked")]
    private partial void LogTaskResumed(string key, ExecutionId runId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task {Key} changed since run {RunId} checkpointed it (its description, expected output, agent or dependencies): it and every task after it run again")]
    private partial void LogTaskChanged(string key, ExecutionId runId);

    private sealed class RunState
    {
        public ConcurrentDictionary<string, int> Ordinals { get; } = new(StringComparer.Ordinal);

        // Set once a resumed run meets a task that changed: from then on nothing is answered from the checkpoint.
        public volatile bool Stale;
    }

    private sealed record Call(RunScope Run, RunState State, string Key, string Fingerprint, StoredResult? Stored);

    /// <summary>What a checkpoint keeps of a task's result: enough to hand the strategy the result it would have got.</summary>
    private sealed record StoredResult
    {
        public bool Success { get; init; }

        public string Output { get; init; } = "";

        public string? Fingerprint { get; init; }

        public JsonElement? Structured { get; init; }

        public string? StructuredType { get; init; }

        public long ExecutionTicks { get; init; }

        public int TokensUsed { get; init; }

        public int PromptTokens { get; init; }

        public int CompletionTokens { get; init; }

        public long? CacheHitTokens { get; init; }

        public long? CacheMissTokens { get; init; }

        public int IterationsUsed { get; init; }

        public DateTimeOffset? EndedAt { get; init; }

        // The outcome of a decision task, which a route reads: kept so that a resumed run replays it.
        public Orkeon.Domain.Task.ValueObjects.DecisionOutcome? Decision { get; init; }

        public static StoredResult Of(TaskResult result, string fingerprint) => new()
        {
            Success = result.Success,
            Output = result.Output,
            Fingerprint = fingerprint,
            Structured = result.StructuredOutput is null ? null : JsonSerializer.SerializeToElement(result.StructuredOutput, result.StructuredOutput.GetType(), Json),
            StructuredType = result.StructuredOutput?.GetType().AssemblyQualifiedName,
            ExecutionTicks = result.ExecutionTime.Ticks,
            TokensUsed = result.TokensUsed,
            PromptTokens = result.PromptTokens,
            CompletionTokens = result.CompletionTokens,
            CacheHitTokens = result.CacheHitTokens,
            CacheMissTokens = result.CacheMissTokens,
            IterationsUsed = result.IterationsUsed,
            EndedAt = result.EndedAt,
            Decision = result.Decision,
        };

        public static StoredResult Of<TOutput>(TaskResult<TOutput> result, string fingerprint)
            where TOutput : class => new()
            {
                Success = result.Success,
                Output = result.RawOutput,
                Fingerprint = fingerprint,
                Structured = result.StructuredOutput is null ? null : JsonSerializer.SerializeToElement(result.StructuredOutput, Json),
                ExecutionTicks = result.ExecutionTime.Ticks,
                TokensUsed = result.TokensUsed,
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens,
                IterationsUsed = result.IterationsUsed,
            };

        public TaskResult ToResult() => new(Success, Output, StructuredValue(), [], TimeSpan.FromTicks(ExecutionTicks))
        {
            Resumed = true,
            TokensUsed = TokensUsed,
            PromptTokens = PromptTokens,
            CompletionTokens = CompletionTokens,
            CacheHitTokens = CacheHitTokens,
            CacheMissTokens = CacheMissTokens,
            IterationsUsed = IterationsUsed,
            EndedAt = EndedAt,
            Decision = Decision,
        };

        public TaskResult<TOutput> ToResult<TOutput>()
            where TOutput : class => new(
                Success,
                Output,
                Structured is { } element ? element.Deserialize<TOutput>(Json) : null,
                [],
                TimeSpan.FromTicks(ExecutionTicks))
            {
                Resumed = true,
                TokensUsed = TokensUsed,
                PromptTokens = PromptTokens,
                CompletionTokens = CompletionTokens,
                IterationsUsed = IterationsUsed,
            };

        // The structured output as the type it was produced as, when that type can be found again
        // (the crew's own output type, a plugin's); otherwise the JSON it was written as.
        private object? StructuredValue()
        {
            if (Structured is not { } element)
                return null;

            if (StructuredType is { Length: > 0 } name && Type.GetType(name, throwOnError: false) is { } type)
            {
                try
                {
                    return element.Deserialize(type, Json) ?? element;
                }
                catch (JsonException)
                {
                    return element;
                }
            }

            return element;
        }
    }
}
