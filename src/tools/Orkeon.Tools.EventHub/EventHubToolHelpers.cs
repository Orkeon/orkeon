using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Application.EventHub;
using Orkeon.Tools.EventHub.Dtos;

namespace Orkeon.Tools.EventHub;

/// <summary>
/// Helpers shared by the EventHub tools (envelope conversion + payload (de)serialization).
/// </summary>
internal static class EventHubToolHelpers
{
    /// <summary>Reserved system topic prefix that agents may never publish on (spec §5.1, §9.2).</summary>
    public const string SystemTopicPrefix = "_system.";

    /// <summary>Serializes a JSON payload (as <see cref="JsonNode"/>) to UTF-8 bytes for the hub.</summary>
    public static object PayloadAsObject(JsonNode? node)
        => node is null ? new { } : (object)node;

    /// <summary>Converts a <see cref="Message"/> envelope into the wire-friendly DTO.</summary>
    public static EventMessageEnvelope ToEnvelope(Message message)
    {
        JsonNode? payload = null;
        if (!message.Payload.IsEmpty)
        {
            try { payload = JsonNode.Parse(message.Payload.Span); }
            catch (JsonException) { payload = JsonValue.Create(Convert.ToBase64String(message.Payload.ToArray())); }
        }

        return new EventMessageEnvelope
        {
            MessageId = message.Id.AsString(),
            Topic = message.Topic,
            SourceCrewId = message.SourceCrewId.ToString(),
            SourceAgentId = message.SourceAgentId?.ToString(),
            CorrelationId = message.CorrelationId?.AsString(),
            PublishedAt = message.PublishedAt.ToString("O"),
            Metadata = message.Metadata,
            Payload = payload
        };
    }

    /// <summary>Returns true when the message is a synthetic <c>WaitTimedOutMessage</c>.</summary>
    public static bool IsWaitTimedOut(Message message)
        => string.Equals(message.Topic, "_system.wait_timed_out", StringComparison.Ordinal);
}
