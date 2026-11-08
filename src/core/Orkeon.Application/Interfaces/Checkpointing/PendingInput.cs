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

/// <summary>
/// The answer a run's question received, kept with what the question was about (FWK-23): a replayed task gets the answer
/// back only when it asks the same thing under the same id: the same kind and, for a tool approval, the same tool and the
/// same arguments. An answer never approves another call than the one it was given to.
/// </summary>
public sealed record AnsweredInput
{
    /// <summary>The answer, as text.</summary>
    public required string Answer { get; init; }

    /// <summary>What the question answered asked for.</summary>
    public required PendingInputKind Kind { get; init; }

    /// <summary>The tool of the approval answered, or null.</summary>
    public string? ToolName { get; init; }

    /// <summary>The hash of the arguments of the tool call answered, or null.</summary>
    public string? ArgsHash { get; init; }

    /// <summary>When the answer was given.</summary>
    public DateTimeOffset AnsweredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The answer <paramref name="answer"/> to <paramref name="request"/>.</summary>
    /// <param name="request">The question answered.</param>
    /// <param name="answer">The answer.</param>
    /// <returns>The record of the answer.</returns>
    public static AnsweredInput To(PendingInputRequest request, string answer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(answer);
        return new AnsweredInput { Answer = answer, Kind = request.Kind, ToolName = request.ToolName, ArgsHash = request.ArgsHash };
    }

    /// <summary>True when this answer was given to the same question as <paramref name="request"/> asks.</summary>
    /// <param name="request">The question asked again.</param>
    /// <returns>True when the kind, the tool and the arguments match.</returns>
    public bool Answers(PendingInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Kind == request.Kind
            && string.Equals(ToolName, request.ToolName, StringComparison.Ordinal)
            && string.Equals(ArgsHash, request.ArgsHash, StringComparison.Ordinal);
    }
}
