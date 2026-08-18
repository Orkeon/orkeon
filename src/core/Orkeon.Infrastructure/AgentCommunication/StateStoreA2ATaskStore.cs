using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Application.Interfaces.Checkpointing;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// <see cref="IA2ATaskStore"/> adapter over the opt-in checkpointing
/// <see cref="IStateStore"/> (PUB-08): A2A task records ride the same durable
/// store the crew execution checkpoints use (in-memory, JSON file, SQLite or
/// Postgres — whichever the host registered). Each task is stored as a
/// single-checkpoint session whose payload is the serialized record, under a
/// namespaced session id so A2A rows never collide with crew sessions.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed class StateStoreA2ATaskStore : IA2ATaskStore
{
    private const string SessionPrefix = "a2a-task:";
    private const string CheckpointKey = "a2a";
    private const string CrewId = "__a2a__";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IStateStore _stateStore;

    /// <summary>Initializes a new instance of <see cref="StateStoreA2ATaskStore"/>.</summary>
    /// <param name="stateStore">The checkpointing state store carrying the records.</param>
    public StateStoreA2ATaskStore(IStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        _stateStore = stateStore;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task SaveAsync(A2ATaskRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var state = new SessionState
        {
            SessionId = SessionPrefix + record.TaskId,
            CrewId = CrewId,
            Phase = MapPhase(record.Status),
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            ErrorMessage = record.Error,
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>
            {
                [CheckpointKey] = new TaskCheckpoint
                {
                    TaskId = record.TaskId,
                    Status = record.Status switch
                    {
                        A2ATaskStatus.Completed => CheckpointStatus.Completed,
                        A2ATaskStatus.Failed => CheckpointStatus.Failed,
                        _ => CheckpointStatus.Pending
                    },
                    Output = JsonSerializer.Serialize(record, JsonOptions),
                    Timestamp = record.UpdatedAt,
                    ErrorMessage = record.Error
                }
            }
        };

        await _stateStore.SaveAsync(state, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<A2ATaskRecord?> GetAsync(string taskId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(taskId);

        var state = await _stateStore.GetAsync(SessionPrefix + taskId, ct).ConfigureAwait(false);
        if (state == null ||
            !state.TaskCheckpoints.TryGetValue(CheckpointKey, out var checkpoint) ||
            string.IsNullOrEmpty(checkpoint.Output))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<A2ATaskRecord>(checkpoint.Output, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SessionPhase MapPhase(A2ATaskStatus status) => status switch
    {
        A2ATaskStatus.Pending => SessionPhase.Pending,
        A2ATaskStatus.Working => SessionPhase.Running,
        A2ATaskStatus.Completed => SessionPhase.Completed,
        A2ATaskStatus.Failed => SessionPhase.Failed,
        A2ATaskStatus.Cancelled => SessionPhase.Suspended,
        _ => SessionPhase.Pending
    };
}
