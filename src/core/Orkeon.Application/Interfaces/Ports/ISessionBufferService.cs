namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// The conversation-buffer primitive of the coding agent (exp 07 SPEC §7.4). Holds the
/// ordered list of messages for the current REPL session plus its metadata, and is the
/// single source of truth read/written by the interactive loop, the session commands
/// (<c>/context</c>, <c>/compact</c>, <c>/force-snip</c>, …) and the SessionCompact crew.
/// </summary>
/// <remarks>
/// Registered as a singleton (one session per REPL) and must be thread-safe — the loop runs
/// on a pool thread while commands run on the engine thread.
/// </remarks>
public interface ISessionBufferService
{
    /// <summary>Snapshot of the current ordered message buffer.</summary>
    IReadOnlyList<SessionMessage> GetMessages();

    /// <summary>Replaces the entire buffer (used by compaction / write_messages).</summary>
    void ReplaceMessages(IReadOnlyList<SessionMessage> messages);

    /// <summary>Appends a pending note as a dedicated message; returns whether it was stored.</summary>
    bool AppendNote(string note);

    /// <summary>Current session metadata (id, title, model, counts).</summary>
    SessionMetadata GetMetadata();

    /// <summary>Sets the session title; returns whether it was applied.</summary>
    bool SetTitle(string title);

    /// <summary>Keeps the head message plus the last <paramref name="retainCount"/> messages; returns the number removed.</summary>
    int Truncate(int retainCount);

    /// <summary>Clears the buffer; returns the number removed.</summary>
    int Reset();

    /// <summary>Heuristic token estimate of the buffer (≈ chars/4 in v1).</summary>
    int EstimateTokenCount();

    /// <summary>Current message count.</summary>
    int MessageCount { get; }

    /// <summary>
    /// Reads a session-scoped state value (e.g. the auto-compaction breaker counter),
    /// or <see langword="null"/> when the key was never set. State lives as long as the
    /// session (across crew runs) and is cleared by <see cref="Reset"/>.
    /// </summary>
    string? GetState(string key);

    /// <summary>
    /// Writes a session-scoped state value; <see langword="null"/> removes the key.
    /// </summary>
    void SetState(string key, string? value);
}

/// <summary>A single conversation message in the session buffer.</summary>
public sealed record SessionMessage
{
    /// <summary>Role of the speaker: <c>user</c> / <c>assistant</c> / <c>system</c> / <c>note</c>.</summary>
    public required string Role { get; init; }

    /// <summary>Message text.</summary>
    public required string Content { get; init; }

    /// <summary>ISO-8601 timestamp; optional.</summary>
    public string? Timestamp { get; init; }
}

/// <summary>Metadata describing the current session buffer.</summary>
public sealed record SessionMetadata
{
    /// <summary>Stable identifier for the session.</summary>
    public required string SessionId { get; init; }

    /// <summary>User-set title, or null.</summary>
    public string? Title { get; init; }

    /// <summary>Active model name (informational).</summary>
    public string? Model { get; init; }

    /// <summary>
    /// Models the configured provider is DECLARED to serve (<c>Llm:AvailableModels</c>), so a
    /// scripted agent can offer a choice and flag an unserved name before spending a turn on it.
    /// Empty when the host declares none — which means "unknown", never "none available".
    /// </summary>
    /// <remarks>
    /// Declarative on purpose: it costs no request and works offline. It is therefore a HINT and
    /// may be stale, so consumers should warn on a name outside it rather than refuse — a list
    /// that has not caught up with the provider's catalogue must not block a model that works.
    /// Live discovery (<c>GET /models</c>) is a separate, network-bound concern.
    /// </remarks>
    public IReadOnlyList<string> AvailableModels { get; init; } = [];

    /// <summary>Number of messages in the buffer.</summary>
    public int MessageCount { get; init; }

    /// <summary>Heuristic token estimate of the buffer.</summary>
    public int EstimatedTokens { get; init; }
}
