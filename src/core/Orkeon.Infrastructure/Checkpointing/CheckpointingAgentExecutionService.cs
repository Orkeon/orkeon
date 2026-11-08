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
        if (await OpenAsync(agent, task, context, cancellationToken).ConfigureAwait(false) is not { } call)
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
        if (await OpenAsync(agent, task, context, cancellationToken).ConfigureAwait(false) is not { } call)
            return await _inner.ExecuteTaskAsync<TOutput>(agent, task, context, cancellationToken).ConfigureAwait(false);

        // A stored structured output that no longer reads as the task's type (edited, or a type that changed) is not an
        // answer: the task runs again instead of failing at every resume.
        if (call.Stored is { } stored && TryRebuild<TOutput>(stored, call, task) is { } rebuilt)
            return rebuilt;

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
        if (await OpenAsync(null, task, null, cancellationToken).ConfigureAwait(false) is not { } call)
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
    private async System.Threading.Tasks.Task<Call?> OpenAsync(DomainAgent? agent, ICrewTask task, SimpleExecutionContext? context, CancellationToken ct)
    {
        if (_runScope.Current is not { } run || task.Key is not { Length: > 0 } taskKey)
            return null;

        // Invocations of one task that run at once (Consensual) count their ordinals each in its own slot: the order in which
        // they reach this line is a race, the slot is not.
        var state = _runs.GetOrCreateValue(run);
        var counted = CheckpointSlot.Current is { } slot ? $"{taskKey}@{slot}" : taskKey;
        var key = $"{counted}#{state.Ordinals.AddOrUpdate(counted, 1, static (_, n) => n + 1)}";
        var fingerprint = await FingerprintAsync(agent, task, context, ct).ConfigureAwait(false);
        var call = new Call(run, state, key, fingerprint, Lookup(run, state, key, fingerprint, task));

        // A run stopped on a question starts no more work (FWK-23); what the checkpoint already holds is still answered.
        if (call.Stored is null && run.Suspension is { } suspended)
            throw new RunSuspendedException(suspended.RequestId);

        return call;
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

        // The type a structured output is rebuilt as comes from the state store, which anyone with write access to it can edit:
        // only the type the task declares (or a plain JSON type) is resolved; any other name is refused and the task runs again.
        if (stored.StructuredType is { Length: > 0 } typeName && !IsAllowedStructuredType(task, typeName))
        {
            LogStructuredTypeRefused(key, typeName, run.ExecutionId);
            return null;
        }

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

    // The plain JSON types a structured output may be stored as, by their exact assembly-qualified name: a name that merely
    // starts like one (a generic type with arbitrary arguments, another assembly) is not one of them.
    private static readonly HashSet<string> JsonTypes = new(
        new[]
        {
            typeof(JsonElement), typeof(JsonDocument),
            typeof(System.Text.Json.Nodes.JsonNode), typeof(System.Text.Json.Nodes.JsonObject), typeof(System.Text.Json.Nodes.JsonArray),
        }.Select(t => t.AssemblyQualifiedName!),
        StringComparer.Ordinal);

    private static bool IsAllowedStructuredType(ICrewTask task, string typeName) =>
        (task.OutputPydantic is { } declared && string.Equals(declared.AssemblyQualifiedName, typeName, StringComparison.Ordinal))
        || JsonTypes.Contains(typeName);

    private TaskResult<TOutput>? TryRebuild<TOutput>(StoredResult stored, Call call, ICrewTask task)
        where TOutput : class
    {
        try
        {
            return stored.ToResult<TOutput>();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            LogStructuredOutputUnreadable(call.Key, call.Run.ExecutionId);
            call.Run.Resume?.ResumedTaskIds.TryRemove(task.TaskId.Value.ToString(), out _);
            return null;
        }
    }

    private async System.Threading.Tasks.Task<string> FingerprintAsync(DomainAgent? agent, ICrewTask task, SimpleExecutionContext? context, CancellationToken ct)
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

        // The description and expected output as the agent reads them, with the run's variables in them: a resumed run
        // given other kickoff values (--var) does not get the outputs of the first values back.
        var variables = context?.Variables;
        var text = string.Join(
            '\u0001',
            Orkeon.Application.Crew.Execution.TaskTextInterpolation.Interpolate(task.Description.Value, variables),
            Orkeon.Application.Crew.Execution.TaskTextInterpolation.Interpolate(task.ExpectedOutput.Value, variables),
            agent?.Role.Value ?? string.Empty,
            agent?.Goal.Value ?? string.Empty,
            string.Join('\u0002', dependencies));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    private async System.Threading.Tasks.Task<T> RunAsync<T>(Call call, Func<System.Threading.Tasks.Task<T>> execute)
    {
        // The questions the task puts to a human are numbered within this invocation (FWK-23).
        using var inputs = TaskInputScope.Enter(call.Key);
        var scope = TaskInputScope.Current!;
        try
        {
            var result = await execute().ConfigureAwait(false);

            // The task met a question it cannot wait for: the failure its agent reports (a tool that threw, a turn
            // that ended) is the suspension, not a result. The task runs again from its start when the run resumes.
            // Only when this task is the one that asked: a task of a parallel wave that asked nothing finished its work, and a
            // run stopped by a sibling's question must not throw that result away (it is checkpointed, and not run again).
            if (call.Run.Suspension is { } suspended && scope.Asked)
                throw new RunSuspendedException(suspended.RequestId);
            return result;
        }
        catch (RunSuspendedException)
        {
            // Not a failure: the session waits for input and the task is not recorded.
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _checkpoints.MarkFailedAsync(call.Run.ExecutionId.ToString(), call.Key, CheckpointErrors.Redacted(ex), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private System.Threading.Tasks.Task WriteAsync(Call call, StoredResult result, int retryCount, CancellationToken ct) =>
        _checkpoints.CheckpointAsync(call.Run.ExecutionId.ToString(), call.Key, JsonSerializer.Serialize(result, Json), retryCount, ct);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task {Key} of run {RunId} has a checkpoint whose structured type {TypeName} is not one the task declares: the checkpoint is refused and the task runs again")]
    private partial void LogStructuredTypeRefused(string key, string typeName, ExecutionId runId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task {Key} of run {RunId} has a checkpoint whose structured output cannot be read back: the task runs again")]
    private partial void LogStructuredOutputUnreadable(string key, ExecutionId runId);

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
                catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
                {
                    // A type the serializer cannot build (no usable constructor, an unsupported member): the JSON is the answer.
                    return element;
                }
            }

            return element;
        }
    }
}
