using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Orkeon.Application.Interfaces.Checkpointing;

/// <summary>What a pending question asks for (FWK-23).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PendingInputKind
{
    /// <summary>A free text answer.</summary>
    Text,

    /// <summary>A yes or no.</summary>
    Confirm,

    /// <summary>One of <see cref="PendingInputRequest.Choices"/>.</summary>
    Choice,

    /// <summary>Several of <see cref="PendingInputRequest.Choices"/>.</summary>
    MultiChoice,

    /// <summary>The permission gate's question about one tool call: allow, allow for the session, or deny.</summary>
    ToolApproval,
}

/// <summary>
/// A question a run put to a human and persisted with its session, so that it survives the process (FWK-23).
/// The prompt is the one asked the first time: a resumed run reads it back and never rebuilds it.
/// </summary>
public sealed record PendingInputRequest
{
    /// <summary>The question's identity in its run: <c>{TaskKey}#{n}.{q}</c>, stable across a resume.</summary>
    public required string RequestId { get; init; }

    /// <summary>What the question asks for.</summary>
    public required PendingInputKind Kind { get; init; }

    /// <summary>The prompt shown to the human.</summary>
    public required string Prompt { get; init; }

    /// <summary>The options of a choice, empty otherwise.</summary>
    public ImmutableList<string> Choices { get; init; } = [];

    /// <summary>The checkpoint key of the task that asked (<c>{TaskKey}#{n}</c>), or null.</summary>
    public string? TaskKey { get; init; }

    /// <summary>The tool a <see cref="PendingInputKind.ToolApproval"/> is about.</summary>
    public string? ToolName { get; init; }

    /// <summary>The hash of the arguments of the tool call a <see cref="PendingInputKind.ToolApproval"/> is about.</summary>
    public string? ArgsHash { get; init; }

    /// <summary>When the question was first asked.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
