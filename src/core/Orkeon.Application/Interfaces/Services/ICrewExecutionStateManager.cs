
using System.Globalization;
using CrewId = Orkeon.Domain.Common.CrewId;
using Orkeon.Application.Execution;

namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Manages crew execution state in a scoped, thread-safe manner.
/// Replaces global ConcurrentDictionary with proper state isolation.
/// </summary>
/// <remarks>
/// By default, states live in memory only (no crash recovery). Implementations may
/// support durable persistence through a state store (see
/// <c>ScopedCrewExecutionStateManager</c> + <c>IStateStore</c> in Infrastructure):
/// when enabled, states are persisted at each transition and reloaded after a
/// process restart, including via the create-or-resume overload of
/// <see cref="CreateStateAsync(CrewId, ExecutionId, CrewInput, CancellationToken)"/>.
/// </remarks>
public interface ICrewExecutionStateManager
{
    /// <summary>
    /// Creates a new execution state for a crew.
    /// </summary>
    System.Threading.Tasks.Task<CrewExecutionState> CreateStateAsync(
        CrewId crewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an execution state for a crew with an explicit execution identifier and input,
    /// or resumes the persisted state when one already exists for <paramref name="executionId"/>
    /// (crash recovery — only when durable persistence is configured).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a live in-memory state already exists for <paramref name="executionId"/>.
    /// </exception>
    System.Threading.Tasks.Task<CrewExecutionState> CreateStateAsync(
        CrewId crewId,
        ExecutionId executionId,
        CrewInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the execution state by ID.
    /// </summary>
    System.Threading.Tasks.Task<CrewExecutionState?> GetStateAsync(
        ExecutionId executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the execution state.
    /// </summary>
    System.Threading.Tasks.Task UpdateStateAsync(
        ExecutionId executionId,
        Action<CrewExecutionState> update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks execution as completed and optionally archives state.
    /// </summary>
    System.Threading.Tasks.Task CompleteExecutionAsync(
        ExecutionId executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active executions for a crew.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<CrewExecutionState>> GetActiveExecutionsAsync(
        CrewId crewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired or abandoned executions.
    /// </summary>
    System.Threading.Tasks.Task CleanupExpiredExecutionsAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Unique identifier for an execution instance. ULID-backed for lexicographic sortability.
/// </summary>
public sealed class ExecutionId : Orkeon.Domain.Common.TypedId
{
    private ExecutionId(Ulid value) : base(value) { }

    /// <summary>Generates a new unique <see cref="ExecutionId"/>.</summary>
    public static ExecutionId New() => new(Ulid.NewUlid());

    /// <summary>Creates an <see cref="ExecutionId"/> from an existing ULID value.</summary>
    public static ExecutionId From(Ulid value) => new(value);

    /// <summary>Creates an <see cref="ExecutionId"/> from its canonical string representation (strict ULID parse).</summary>
    /// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is null/empty/whitespace, or not a valid ULID string.</exception>
    public static ExecutionId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("ExecutionId cannot be null, empty, or whitespace.", nameof(value));
        return new ExecutionId(Ulid.Parse(value, CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Represents the state of a crew execution.
/// </summary>
public class CrewExecutionState
{
    private readonly object _lock = new();
    private ExecutionState _status;
    private double _progress;
    private string? _currentTask;
    private string? _error;
    private CrewOutput? _output;
    private ExecutionMetadata _metadata = ExecutionMetadata.Empty;

    /// <summary>Gets or sets the id.</summary>
    public ExecutionId Id { get; }
    /// <summary>Gets or sets the crew id.</summary>
    public CrewId CrewId { get; }
    /// <summary>Gets or sets the input.</summary>
    public CrewInput Input { get; }
    /// <summary>Gets or sets the start time.</summary>
    public DateTime StartTime { get; }
    /// <summary>Gets or sets the end time.</summary>
    public DateTime? EndTime { get; private set; }

    /// <summary>
    /// Initializes a new instance of <see cref="CrewExecutionState"/>.
    /// </summary>
    public CrewExecutionState(CrewId crewId, ExecutionId executionId, CrewInput input)
    {
        CrewId = crewId;
        Id = executionId;
        Input = input;
        StartTime = DateTime.UtcNow;
        _status = ExecutionState.Pending;
        _progress = 0.0;
    }

    /// <summary>
    /// Rehydration constructor — restores a state captured by <see cref="CreateSnapshot"/>.
    /// </summary>
    private CrewExecutionState(ExecutionSnapshot snapshot, CrewInput input, CrewOutput? output)
    {
        CrewId = snapshot.CrewId;
        Id = snapshot.Id;
        Input = input;
        StartTime = snapshot.StartTime;
        EndTime = snapshot.EndTime;
        _status = snapshot.Status;
        _progress = snapshot.Progress;
        _currentTask = snapshot.CurrentTask;
        _error = snapshot.Error;
        _output = output;
        _metadata = snapshot.Metadata ?? ExecutionMetadata.Empty;
    }

    /// <summary>
    /// Restores an execution state from a persisted snapshot (counterpart of
    /// <see cref="CreateSnapshot"/>). Used by durable state-manager implementations
    /// to rehydrate executions after a process restart (crash recovery).
    /// </summary>
    public static CrewExecutionState Restore(
        ExecutionSnapshot snapshot,
        CrewInput input,
        CrewOutput? output = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(input);
        return new CrewExecutionState(snapshot, input, output);
    }

    /// <summary>Status.</summary>
    public ExecutionState Status
    {
        get { lock (_lock) return _status; }
        set
        {
            lock (_lock)
            {
                _status = value;
                if (value == ExecutionState.Completed || value == ExecutionState.Failed)
                {
                    EndTime = DateTime.UtcNow;
                }
            }
        }
    }

    /// <summary>Progress.</summary>
    public double Progress
    {
        get { lock (_lock) return _progress; }
        set { lock (_lock) _progress = Math.Clamp(value, 0, 1); }
    }

    /// <summary>Current Task.</summary>
    public string? CurrentTask
    {
        get { lock (_lock) return _currentTask; }
        set { lock (_lock) _currentTask = value; }
    }

    /// <summary>Error.</summary>
    public string? Error
    {
        get { lock (_lock) return _error; }
        set { lock (_lock) _error = value; }
    }

    /// <summary>Output.</summary>
    public CrewOutput? Output
    {
        get { lock (_lock) return _output; }
        set { lock (_lock) _output = value; }
    }

    /// <summary>Metadata.</summary>
    public ExecutionMetadata Metadata
    {
        get { lock (_lock) return _metadata; }
    }

    /// <summary>
    /// Set Metadata.
    /// </summary>
    public void SetMetadata(string key, object value)
    {
        lock (_lock)
        {
            var builder = ExecutionMetadata.CreateBuilderFrom(_metadata);
            builder.Add(key, value);
            _metadata = builder.Build();
        }
    }

    /// <summary>
    /// Reads a typed metadata value by key.
    /// </summary>
    public T? MetadataValue<T>(string key) where T : class
    {
        lock (_lock)
        {
            return _metadata.Get<T>(key);
        }
    }

    /// <summary>
    /// Builds a composite status snapshot for this execution.
    /// </summary>
    public CrewExecutionStatus ToStatus()
    {
        lock (_lock)
        {
            return new CrewExecutionStatus(
                CrewExecutionId.From(Id.Value),
                _status,
                _progress,
                _currentTask,
                _error);
        }
    }

    /// <summary>
    /// Create Snapshot.
    /// </summary>
    public ExecutionSnapshot CreateSnapshot()
    {
        lock (_lock)
        {
            return new ExecutionSnapshot(
                Id,
                CrewId,
                _status,
                _progress,
                _currentTask,
                _error,
                StartTime,
                EndTime,
                _metadata);
        }
    }
}

// Use ExecutionState from ICrewOrchestrationService

/// <summary>
/// Timing information for an execution snapshot.
/// </summary>
public record ExecutionTiming(
    DateTime StartTime,
    DateTime? EndTime);

/// <summary>
/// Status details for an execution snapshot.
/// </summary>
public record ExecutionStatusInfo(
    ExecutionState Status,
    double Progress,
    string? CurrentTask,
    string? Error);

/// <summary>
/// Read-only snapshot of execution state.
/// </summary>
public record ExecutionSnapshot(
    ExecutionId Id,
    CrewId CrewId,
    ExecutionStatusInfo StatusInfo,
    ExecutionTiming Timing,
    ExecutionMetadata Metadata)
{
    // Backward-compatible accessors
    /// <summary>Current execution state.</summary>
    public ExecutionState Status => StatusInfo.Status;
    /// <summary>Execution progress (0.0 to 1.0).</summary>
    public double Progress => StatusInfo.Progress;
    /// <summary>Currently executing task name.</summary>
    public string? CurrentTask => StatusInfo.CurrentTask;
    /// <summary>Error message if execution failed.</summary>
    public string? Error => StatusInfo.Error;
    /// <summary>When the execution started.</summary>
    public DateTime StartTime => Timing.StartTime;
    /// <summary>When the execution ended (null if still running).</summary>
    public DateTime? EndTime => Timing.EndTime;

    /// <summary>
    /// Initializes a new instance of <see cref="ExecutionSnapshot"/> for backward compatibility.
    /// </summary>
#pragma warning disable S107 // Backward-compatible overload; use primary constructor with ExecutionStatusInfo and ExecutionTiming instead
    public ExecutionSnapshot(
        ExecutionId Id,
        CrewId CrewId,
        ExecutionState Status,
        double Progress,
        string? CurrentTask,
        string? Error,
        DateTime StartTime,
        DateTime? EndTime,
        ExecutionMetadata Metadata)
        : this(Id, CrewId,
            new ExecutionStatusInfo(Status, Progress, CurrentTask, Error),
            new ExecutionTiming(StartTime, EndTime),
            Metadata)
    {
    }
#pragma warning restore S107
}
