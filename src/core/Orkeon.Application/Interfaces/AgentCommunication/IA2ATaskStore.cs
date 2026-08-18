using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Application.Interfaces.AgentCommunication;

/// <summary>
/// Persistence port for A2A task lifecycle records. Opt-in: when no store is
/// registered, the A2A server keeps answering task-status queries with
/// <c>501 Not Implemented</c> instead of fabricating state (see
/// docs/reference/limitations.md). A2A v1.0 treats tasks as persistent,
/// retrievable resources — this port is what makes <c>GET /a2a/tasks/{id}</c>
/// real.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IA2ATaskStore
{
    /// <summary>Creates or updates the record for a task.</summary>
    System.Threading.Tasks.Task SaveAsync(A2ATaskRecord record, CancellationToken ct = default);

    /// <summary>Returns the record for a task, or null when the task is unknown.</summary>
    System.Threading.Tasks.Task<A2ATaskRecord?> GetAsync(string taskId, CancellationToken ct = default);
}

/// <summary>
/// Persistent snapshot of an A2A task's lifecycle.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record A2ATaskRecord
{
    /// <summary>Gets the unique task identifier.</summary>
    public required string TaskId { get; init; }

    /// <summary>Gets the current task status.</summary>
    public A2ATaskStatus Status { get; init; }

    /// <summary>Gets the target skill the task was routed to.</summary>
    public string? SkillId { get; init; }

    /// <summary>Gets the task output, when completed.</summary>
    public string? Output { get; init; }

    /// <summary>Gets the error message, when failed.</summary>
    public string? Error { get; init; }

    /// <summary>Gets the UTC timestamp when the task was first recorded.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Gets the UTC timestamp of the last status change.</summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
