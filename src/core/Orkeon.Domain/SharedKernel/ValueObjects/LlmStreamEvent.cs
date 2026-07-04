namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>Kind of event emitted by a streaming chat completion.</summary>
public enum LlmStreamEventKind
{
    /// <summary>Incremental piece of the assistant's visible content.</summary>
    ContentDelta,
    /// <summary>Incremental piece of the model's reasoning trace (e.g. DeepSeek thinking mode).</summary>
    ReasoningDelta,
    /// <summary>Terminal event carrying the assembled <see cref="LlmStreamEvent.FinalResponse"/>.</summary>
    Completed,
}

/// <summary>
/// One event of a streamed chat completion. Streams end with exactly one
/// <see cref="LlmStreamEventKind.Completed"/> event whose <see cref="FinalResponse"/> is
/// equivalent to the non-streaming <c>ChatAsync</c> result (assembled content, usage, and —
/// when the model streamed tool calls — a synthesized <see cref="LlmResponse.RawResponseBody"/>
/// in the OpenAI chat shape so downstream tool-call parsers work unchanged).
/// </summary>
public sealed record LlmStreamEvent
{
    /// <summary>Event kind.</summary>
    public required LlmStreamEventKind Kind { get; init; }

    /// <summary>Delta text for <see cref="LlmStreamEventKind.ContentDelta"/> / <see cref="LlmStreamEventKind.ReasoningDelta"/>.</summary>
    public string? Delta { get; init; }

    /// <summary>Assembled response, set on <see cref="LlmStreamEventKind.Completed"/> only.</summary>
    public LlmResponse? FinalResponse { get; init; }

    /// <summary>Creates a content delta event.</summary>
    public static LlmStreamEvent Content(string delta) => new() { Kind = LlmStreamEventKind.ContentDelta, Delta = delta };

    /// <summary>Creates a reasoning delta event.</summary>
    public static LlmStreamEvent Reasoning(string delta) => new() { Kind = LlmStreamEventKind.ReasoningDelta, Delta = delta };

    /// <summary>Creates the terminal event.</summary>
    public static LlmStreamEvent Complete(LlmResponse response) => new() { Kind = LlmStreamEventKind.Completed, FinalResponse = response };
}
