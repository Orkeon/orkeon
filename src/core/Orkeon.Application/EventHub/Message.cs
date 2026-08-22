using System.Collections.Immutable;
using Orkeon.Domain.Common;

namespace Orkeon.Application.EventHub;

/// <summary>
/// Canonical message envelope flowing through the EventHub. Immutable.
/// </summary>
public sealed record Message
{
    /// <summary>Unique identifier of this message.</summary>
    public required MessageId Id { get; init; }

    /// <summary>Topic the message was published on. For <c>Post</c>/<c>Send</c>/<c>Reply</c>, the hub sets a synthetic topic.</summary>
    public required string Topic { get; init; }

    /// <summary>
    /// Identifier of the crew that emitted the message. Always populated by the hub from the caller context.
    /// Equals <see cref="CrewId.System"/> for messages produced by the hub itself.
    /// </summary>
    public required CrewId SourceCrewId { get; init; }

    /// <summary>Identifier of the emitting agent. <see langword="null"/> for system messages.</summary>
    public AgentId? SourceAgentId { get; init; }

    /// <summary>Optional crew scope. <see langword="null"/> ⇒ global broadcast.</summary>
    public CrewId? TargetCrewId { get; init; }

    /// <summary>Optional addressed mailbox. Populated for <c>Post</c>/<c>Send</c>/<c>Reply</c>.</summary>
    public MailboxAddress? TargetMailbox { get; init; }

    /// <summary>Correlation identifier linking a <c>Send</c> request to its <c>Reply</c>.</summary>
    public CorrelationId? CorrelationId { get; init; }

    /// <summary>Hub-stamped timestamp at publish time.</summary>
    public required DateTimeOffset PublishedAt { get; init; }

    /// <summary>Arbitrary string metadata copied into the envelope.</summary>
    public required ImmutableDictionary<string, string> Metadata { get; init; }

    /// <summary>Opaque binary payload.</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>Identifier of the schema describing <see cref="Payload"/>.</summary>
    public required string SchemaId { get; init; }

    /// <summary>
    /// The identifier stamped when the caller declared no schema at all. It means "plain JSON,
    /// no contract" — which is what every <c>Post</c>, <c>Send</c> and <c>Reply</c> carries, and
    /// why the validation stage lets it through instead of demanding a registration nobody made.
    /// Deliberately not a plausible real id (a deployment could legitimately register
    /// <c>application/json</c> as a schema, and a sentinel that collides is silently unchecked).
    /// </summary>
    public const string NoDeclaredSchemaId = "_none";
}
