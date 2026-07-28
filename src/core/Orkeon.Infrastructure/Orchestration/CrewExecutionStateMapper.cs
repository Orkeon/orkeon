using System.Text.Json;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Infrastructure.Orchestration;

/// <summary>
/// Maps <see cref="CrewExecutionState"/> to/from the checkpointing
/// <see cref="SessionState"/> contract so execution states can be persisted in any
/// <see cref="IStateStore"/> (in-memory, JSON file, SQLite, PostgreSQL).
/// </summary>
/// <remarks>
/// Execution-state sessions are namespaced with <see cref="SessionNamespace"/> on both
/// the session id and the crew id so that they never collide with (nor pollute) the
/// task-level checkpoint sessions managed by <c>CheckpointManager</c>/<c>ResumeEngine</c>
/// when both features share the same store. The full state travels as a JSON document
/// stored in a single well-known <see cref="TaskCheckpoint"/> entry
/// (<see cref="StateDocumentKey"/>, kept in <c>Pending</c> status so it is never counted
/// as a completed task by session summaries).
/// </remarks>
public static class CrewExecutionStateMapper
{
    /// <summary>Namespace prefix isolating execution-state sessions in a shared store.</summary>
    public const string SessionNamespace = "crew-exec:";

    /// <summary>Well-known checkpoint key carrying the serialized state document.</summary>
    public const string StateDocumentKey = "__crew_execution_state__";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    /// <summary>Builds the namespaced store session id for an execution.</summary>
    public static string SessionIdFor(ExecutionId executionId)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        return SessionNamespace + executionId.AsString();
    }

    /// <summary>Builds the namespaced crew scope used to list executions of a crew.</summary>
    public static string CrewScopeFor(CrewId crewId)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        return SessionNamespace + crewId.AsString();
    }

    /// <summary>
    /// Converts an execution state into a storable <see cref="SessionState"/>.
    /// </summary>
    public static SessionState ToSessionState(CrewExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var snapshot = state.CreateSnapshot();
        var document = new CrewExecutionStateDocument
        {
            ExecutionId = snapshot.Id.AsString(),
            CrewId = snapshot.CrewId.AsString(),
            Status = snapshot.Status.ToString(),
            Progress = snapshot.Progress,
            CurrentTask = snapshot.CurrentTask,
            Error = snapshot.Error,
            StartTime = snapshot.StartTime,
            EndTime = snapshot.EndTime,
            InputInitialContext = state.Input?.InitialContext,
            InputVariables = ToStringVariables(state.Input),
            Output = ToPersistedOutput(state.Output),
            Metadata = ToStringMetadata(snapshot.Metadata)
        };

        return new SessionState
        {
            SessionId = SessionIdFor(snapshot.Id),
            CrewId = CrewScopeFor(snapshot.CrewId),
            Phase = ToPhase(snapshot.Status),
            CompletedTaskIndex = 0,
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>
            {
                [StateDocumentKey] = new TaskCheckpoint
                {
                    TaskId = StateDocumentKey,
                    Status = CheckpointStatus.Pending,
                    Output = JsonSerializer.Serialize(document, JsonOptions),
                    Timestamp = DateTime.UtcNow
                }
            },
            CreatedAt = snapshot.StartTime,
            UpdatedAt = DateTime.UtcNow,
            ErrorMessage = snapshot.Error
        };
    }

    /// <summary>
    /// Rehydrates an execution state from a stored session. Returns <see langword="null"/>
    /// when the session does not carry a valid execution-state document (e.g., it belongs
    /// to the task-level checkpointing feature or the payload is corrupt).
    /// </summary>
    public static CrewExecutionState? FromSessionState(SessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!session.SessionId.StartsWith(SessionNamespace, StringComparison.Ordinal))
            return null;

        if (!session.TaskCheckpoints.TryGetValue(StateDocumentKey, out var checkpoint)
            || string.IsNullOrWhiteSpace(checkpoint.Output))
        {
            return null;
        }

        CrewExecutionStateDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<CrewExecutionStateDocument>(checkpoint.Output, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (document is null
            || string.IsNullOrWhiteSpace(document.ExecutionId)
            || string.IsNullOrWhiteSpace(document.CrewId))
        {
            return null;
        }

        ExecutionId executionId;
        CrewId crewId;
        try
        {
            executionId = ExecutionId.From(document.ExecutionId);
            crewId = CrewId.Parse(document.CrewId);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }

        var status = Enum.TryParse<ExecutionState>(document.Status, ignoreCase: true, out var parsed)
            ? parsed
            : ExecutionState.Pending;

        var metadata = document.Metadata is { Count: > 0 }
            ? ExecutionMetadata.FromDictionary(document.Metadata.ToDictionary(
                kvp => kvp.Key,
                kvp => (object)kvp.Value))
            : ExecutionMetadata.Empty;

        var snapshot = new ExecutionSnapshot(
            executionId,
            crewId,
            new ExecutionStatusInfo(status, document.Progress, document.CurrentTask, document.Error),
            new ExecutionTiming(document.StartTime, document.EndTime),
            metadata);

        var variables = (document.InputVariables ?? new Dictionary<string, string>())
            .ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
        var input = new CrewInput(document.InputInitialContext, variables);

        return CrewExecutionState.Restore(snapshot, input, FromPersistedOutput(document.Output));
    }

    /// <summary>Maps an execution status to the closest session phase (terminal-aware).</summary>
    public static SessionPhase ToPhase(ExecutionState status) => status switch
    {
        ExecutionState.Pending => SessionPhase.Pending,
        ExecutionState.Running => SessionPhase.Running,
        ExecutionState.Completed => SessionPhase.Completed,
        ExecutionState.Failed => SessionPhase.Failed,
        // Cancelled has no session equivalent; mapped to a terminal phase so the
        // execution is never listed as resumable. The exact status is preserved
        // in the state document.
        ExecutionState.Cancelled => SessionPhase.Failed,
        _ => SessionPhase.Pending
    };

    private static Dictionary<string, string>? ToStringVariables(CrewInput? input)
    {
        if (input?.Variables is not { Count: > 0 })
            return [];

        return input.GetStringVariables()
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private static Dictionary<string, string>? ToStringMetadata(ExecutionMetadata? metadata)
    {
        if (metadata is null)
            return [];

        var raw = metadata.ToDictionary();
        if (raw.Count == 0)
            return [];

        // Metadata values are persisted as invariant strings. Primitive values still
        // round-trip through ExecutionMetadataValue.GetValue<T> (Convert.ChangeType)  —
        // complex values degrade to their string representation (documented v1 limit).
        return raw.ToDictionary(
            kvp => kvp.Key,
            kvp => Convert.ToString(kvp.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static PersistedCrewOutput? ToPersistedOutput(CrewOutput? output)
    {
        if (output is null)
            return null;

        return new PersistedCrewOutput
        {
            FinalOutput = output.FinalOutput,
            Duration = output.Duration,
            // null TokensUsed means "not measured" — persist the distinction (R10.8)
            // instead of flattening it into a fabricated zero usage.
            TokensMeasured = output.TokensUsed is not null,
            PromptTokens = output.TokensUsed?.PromptTokens ?? 0,
            CompletionTokens = output.TokensUsed?.CompletionTokens ?? 0,
            TotalTokens = output.TokensUsed?.TotalTokens ?? 0,
            TaskOutputs = (output.TaskOutputs ?? []).Select(t => new PersistedTaskOutput
            {
                TaskId = t.TaskId,
                AgentId = t.AgentId,
                Content = t.Content,
                CompletedAt = t.CompletedAt,
                Success = t.Success,
                ExecutionTime = t.ExecutionTime
            }).ToList()
        };
    }

    private static CrewOutput? FromPersistedOutput(PersistedCrewOutput? persisted)
    {
        if (persisted is null)
            return null;

        var taskOutputs = (persisted.TaskOutputs ?? [])
            .Select(t => new TaskOutput(
                TaskId: t.TaskId,
                AgentId: t.AgentId,
                Content: t.Content,
                CompletedAt: t.CompletedAt,
                Success: t.Success,
                ExecutionTime: t.ExecutionTime))
            .ToList();

        return new CrewOutput(
            FinalOutput: persisted.FinalOutput,
            TaskOutputs: taskOutputs,
            Duration: persisted.Duration,
            TokensUsed: persisted.TokensMeasured
                ? new TokenUsage(persisted.PromptTokens, persisted.CompletionTokens, persisted.TotalTokens)
                : null);
    }
}

/// <summary>
/// Serializable document carrying the full crew execution state inside a
/// <see cref="SessionState"/> checkpoint entry.
/// </summary>
public sealed record CrewExecutionStateDocument
{
    /// <summary>Canonical ULID string of the execution identifier.</summary>
    public required string ExecutionId { get; init; }

    /// <summary>Canonical ULID string of the crew identifier.</summary>
    public required string CrewId { get; init; }

    /// <summary>Execution status name (<see cref="ExecutionState"/>).</summary>
    public string Status { get; init; } = nameof(ExecutionState.Pending);

    /// <summary>Execution progress (0.0 to 1.0).</summary>
    public double Progress { get; init; }

    /// <summary>Currently executing task, if any.</summary>
    public string? CurrentTask { get; init; }

    /// <summary>Error message, if the execution failed.</summary>
    public string? Error { get; init; }

    /// <summary>UTC start time of the execution.</summary>
    public DateTime StartTime { get; init; }

    /// <summary>UTC end time of the execution (null while running).</summary>
    public DateTime? EndTime { get; init; }

    /// <summary>Initial context of the crew input.</summary>
    public string? InputInitialContext { get; init; }

    /// <summary>Crew input variables (persisted as strings).</summary>
    public IReadOnlyDictionary<string, string>? InputVariables { get; init; }

    /// <summary>Final crew output, if the execution produced one.</summary>
    public PersistedCrewOutput? Output { get; init; }

    /// <summary>Execution metadata (persisted as invariant strings).</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Serializable projection of <see cref="CrewOutput"/> for state persistence.
/// Tool-usage telemetry is intentionally not persisted (v1 scope).
/// </summary>
public sealed record PersistedCrewOutput
{
    /// <summary>Final output text of the crew.</summary>
    public string FinalOutput { get; init; } = "";

    /// <summary>Per-task outputs.</summary>
    public IReadOnlyList<PersistedTaskOutput> TaskOutputs { get; init; } = [];

    /// <summary>Total execution duration.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Whether token telemetry was actually measured for this output. False restores a
    /// null <c>TokensUsed</c> ("not measured", R10.8). Defaults to true so documents
    /// persisted before this field existed keep their historical zero counts.
    /// </summary>
    public bool TokensMeasured { get; init; } = true;

    /// <summary>Prompt token count.</summary>
    public int PromptTokens { get; init; }

    /// <summary>Completion token count.</summary>
    public int CompletionTokens { get; init; }

    /// <summary>Total token count.</summary>
    public int TotalTokens { get; init; }
}

/// <summary>
/// Serializable projection of a task output for state persistence.
/// </summary>
public sealed record PersistedTaskOutput
{
    /// <summary>Task identifier.</summary>
    public string TaskId { get; init; } = "";

    /// <summary>Executing agent identifier, when known.</summary>
    public string? AgentId { get; init; }

    /// <summary>Task output content.</summary>
    public string Content { get; init; } = "";

    /// <summary>UTC completion timestamp.</summary>
    public DateTime CompletedAt { get; init; }

    /// <summary>Whether the task succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Task execution duration.</summary>
    public TimeSpan ExecutionTime { get; init; }
}
