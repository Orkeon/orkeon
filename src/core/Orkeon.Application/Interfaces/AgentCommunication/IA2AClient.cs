
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Application.Interfaces.AgentCommunication;

/// <summary>
/// Client for sending tasks to remote A2A agents.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IA2AClient
{
    /// <summary>Sends a task to a remote agent and waits for the response.</summary>
    System.Threading.Tasks.Task<A2ATaskResponse> SendTaskAsync(
        Uri agentUrl, A2ATaskRequest request, CancellationToken ct = default);

    /// <summary>Sends a task to a remote agent and streams incremental updates via SSE.</summary>
    IAsyncEnumerable<A2ATaskUpdate> SendTaskStreamingAsync(
        Uri agentUrl, A2ATaskRequest request, CancellationToken ct = default);

    /// <summary>Cancels a previously submitted task.</summary>
    System.Threading.Tasks.Task CancelTaskAsync(Uri agentUrl, string taskId, CancellationToken ct = default);

    /// <summary>Gets the current status of a previously submitted task.</summary>
    System.Threading.Tasks.Task<A2ATaskResponse> GetTaskStatusAsync(
        Uri agentUrl, string taskId, CancellationToken ct = default);
}

/// <summary>
/// Represents a task request sent to a remote A2A agent.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record A2ATaskRequest
{
    /// <summary>Gets the unique task identifier.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>Gets the target skill identifier on the remote agent.</summary>
    [JsonPropertyName("skillId")]
    public string SkillId { get; init; } = "";

    /// <summary>Gets the input payload.</summary>
    [JsonPropertyName("input")]
    public string Input { get; init; } = "";

    /// <summary>Gets the MIME type of the input.</summary>
    [JsonPropertyName("inputMode")]
    public string InputMode { get; init; } = "text/plain";

    /// <summary>Gets optional metadata for the task.</summary>
    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Represents the response to an A2A task.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record A2ATaskResponse
{
    /// <summary>Gets the task identifier.</summary>
    [JsonPropertyName("taskId")]
    public string TaskId { get; init; } = "";

    /// <summary>Gets the current task status.</summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public A2ATaskStatus Status { get; init; } = A2ATaskStatus.Pending;

    /// <summary>Gets the task output, if completed.</summary>
    [JsonPropertyName("output")]
    public string? Output { get; init; }

    /// <summary>Gets the error message, if failed.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>Gets the timestamp of the response.</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a streaming update for an A2A task (SSE event).
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record A2ATaskUpdate
{
    /// <summary>Gets the task identifier.</summary>
    [JsonPropertyName("taskId")]
    public string TaskId { get; init; } = "";

    /// <summary>Gets the current task status.</summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public A2ATaskStatus Status { get; init; }

    /// <summary>Gets partial output for streaming.</summary>
    [JsonPropertyName("partialOutput")]
    public string? PartialOutput { get; init; }

    /// <summary>Gets the timestamp of the update.</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Status of an A2A task.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public enum A2ATaskStatus
{
    /// <summary>Task has been received but not yet started.</summary>
    Pending,

    /// <summary>Task is currently being processed.</summary>
    Working,

    /// <summary>Task completed successfully.</summary>
    Completed,

    /// <summary>Task failed with an error.</summary>
    Failed,

    /// <summary>Task was cancelled.</summary>
    Cancelled
}
