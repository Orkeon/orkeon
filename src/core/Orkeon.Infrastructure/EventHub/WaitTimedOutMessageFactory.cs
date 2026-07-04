using System.Collections.Immutable;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Common;

namespace Orkeon.Infrastructure.EventHub;

/// <summary>
/// Factory for the synthetic <c>WaitTimedOutMessage</c> the hub delivers when a
/// <see cref="FiniteWaitTimeout"/> expires. Spec §9.2.
/// </summary>
public static class WaitTimedOutMessageFactory
{
    /// <summary>Reserved topic used for wait-timeout system messages.</summary>
    public const string ReservedTopic = "_system.wait_timed_out";

    /// <summary>Schema id stamped on every wait-timeout message.</summary>
    public const string SchemaId = "_system/wait_timed_out/v1";

    /// <summary>
    /// Creates a wait-timeout message with the metadata required by spec §9.2
    /// (<c>original_topic</c>, <c>original_wait_id</c>, <c>original_started_at</c>).
    /// </summary>
    /// <param name="originalTopic">The descriptor topic that timed out (best-effort: synthetic for non-topic waits).</param>
    /// <param name="originalWaitId">The unique id of the wait that expired.</param>
    /// <param name="originalStartedAt">When the wait began.</param>
    /// <param name="targetCrewId">Crew the message must be delivered to. <see langword="null"/> ⇒ global.</param>
    public static Message Create(
        string originalTopic,
        string originalWaitId,
        DateTimeOffset originalStartedAt,
        CrewId? targetCrewId)
    {
        ArgumentNullException.ThrowIfNull(originalTopic);
        ArgumentNullException.ThrowIfNull(originalWaitId);

        var metadata = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        metadata["original_topic"] = originalTopic;
        metadata["original_wait_id"] = originalWaitId;
        metadata["original_started_at"] = originalStartedAt.ToString("O");

        return new Message
        {
            Id = MessageId.NewId(),
            Topic = ReservedTopic,
            SourceCrewId = CrewId.System,
            SourceAgentId = null,
            TargetCrewId = targetCrewId,
            TargetMailbox = null,
            CorrelationId = null,
            PublishedAt = DateTimeOffset.UtcNow,
            Metadata = metadata.ToImmutable(),
            Payload = ReadOnlyMemory<byte>.Empty,
            SchemaId = SchemaId
        };
    }
}
