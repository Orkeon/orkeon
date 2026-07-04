using Orkeon.Domain.Common;
using Orkeon.Domain.Memory.ValueObjects;

namespace Orkeon.Domain.Memory;

/// <summary>
/// Represents an episodic memory - a complete record of an event or task execution.
/// </summary>
public sealed class EpisodicMemory : Entity<EpisodeId>
{
    /// <summary>
    /// Gets the episode title or summary.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the task ID associated with this episode.
    /// </summary>
    public TaskId? TaskId { get; }

    /// <summary>
    /// Gets the agent ID who created this episode.
    /// </summary>
    public AgentId AgentId { get; }

    /// <summary>
    /// Gets the sequence of events in this episode.
    /// </summary>
    public IReadOnlyList<EpisodeEvent> Events => _events.AsReadOnly();

    private readonly List<EpisodeEvent> _events;

    /// <summary>
    /// Gets the outcome of the episode.
    /// </summary>
    public EpisodeOutcome Outcome { get; private set; }

    /// <summary>
    /// Gets the lessons learned from this episode.
    /// </summary>
    public IReadOnlyList<string> LessonsLearned => _lessonsLearned.AsReadOnly();

    private readonly List<string> _lessonsLearned;

    /// <summary>
    /// Gets when the episode started.
    /// </summary>
    public DateTime StartedAt { get; }

    /// <summary>
    /// Gets when the episode completed.
    /// </summary>
    public DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// Gets the total duration of the episode.
    /// </summary>
    public TimeSpan? Duration => CompletedAt?.Subtract(StartedAt);

    /// <summary>
    /// Gets additional metadata about the episode.
    /// </summary>
    public EpisodicMemoryMetadata Metadata { get; private set; }

    private EpisodicMemory(
        string title,
        AgentId agentId,
        TaskId? taskId)
        : base(EpisodeId.Create())
    {
        Title = title;
        AgentId = agentId;
        TaskId = taskId;
        _events = [];
        _lessonsLearned = [];
        Outcome = EpisodeOutcome.InProgress;
        StartedAt = DateTime.UtcNow;
        Metadata = EpisodicMemoryMetadata.Empty;
    }

    /// <summary>
    /// Creates a new <see cref="EpisodicMemory"/> with validated parameters.
    /// </summary>
    /// <param name="title">The episode title or summary.</param>
    /// <param name="agentId">The agent who created this episode.</param>
    /// <param name="taskId">The optional task identifier associated with this episode.</param>
    public static EpisodicMemory Create(
        string title,
        AgentId agentId,
        TaskId? taskId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(agentId);

        return new EpisodicMemory(title, agentId, taskId);
    }

    /// <summary>
    /// Adds an event to the episode.
    /// </summary>
    internal void AddEvent(EpisodeEvent episodeEvent)
    {
        ArgumentNullException.ThrowIfNull(episodeEvent);

        if (CompletedAt.HasValue)
            throw new InvalidOperationException("Cannot add events to a completed episode");

        _events.Add(episodeEvent);
    }

    /// <summary>
    /// Completes the episode with the given outcome.
    /// </summary>
    internal void Complete(EpisodeOutcome outcome, IReadOnlyList<string>? lessonsLearned = null)
    {
        if (CompletedAt.HasValue)
            throw new InvalidOperationException("Episode is already completed");

        Outcome = outcome;
        CompletedAt = DateTime.UtcNow;

        if (lessonsLearned != null)
        {
            _lessonsLearned.AddRange(lessonsLearned);
        }
    }

    /// <summary>
    /// Adds a lesson learned from this episode.
    /// </summary>
    internal void AddLessonLearned(string lesson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lesson);

        _lessonsLearned.Add(lesson);
    }

    /// <summary>
    /// Updates the episode metadata.
    /// </summary>
    internal void UpdateMetadata(EpisodicMemoryMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        Metadata = metadata;
    }
}

/// <summary>
/// Represents a single event within an episode.
/// </summary>
public sealed class EpisodeEvent
{
    /// <summary>
    /// Gets the event type (e.g., "decision", "action", "observation", "reflection").
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Gets the event description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets when the event occurred.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Gets any additional event data.
    /// </summary>
    public EpisodeEventData Data { get; }

    private EpisodeEvent(
        string type,
        string description,
        EpisodeEventData? data)
    {
        Type = type;
        Description = description;
        Timestamp = DateTime.UtcNow;
        Data = data ?? EpisodeEventData.Empty;
    }

    /// <summary>
    /// Creates a new <see cref="EpisodeEvent"/> with validated parameters.
    /// </summary>
    /// <param name="type">The event type.</param>
    /// <param name="description">The event description.</param>
    /// <param name="data">Optional event data.</param>
    public static EpisodeEvent Create(
        string type,
        string description,
        EpisodeEventData? data = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new EpisodeEvent(type, description, data);
    }
}

/// <summary>
/// Represents the outcome of an episode.
/// </summary>
public enum EpisodeOutcome
{
    /// <summary>The episode is still in progress.</summary>
    InProgress,
    /// <summary>The episode completed successfully.</summary>
    Success,
    /// <summary>The episode completed with partial success.</summary>
    PartialSuccess,
    /// <summary>The episode failed.</summary>
    Failure,
    /// <summary>The episode was cancelled.</summary>
    Cancelled
}
