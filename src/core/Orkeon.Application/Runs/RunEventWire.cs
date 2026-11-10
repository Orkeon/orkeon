using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orkeon.Application.Runs;

/// <summary>
/// The wire form of a run event: one JSON document per line, envelope <c>{ v, seq, ts, kind, ...identity }</c>
/// followed by the flat payload. The stdout stream of <c>orkeon run --events</c>, the JSONL history files and
/// <c>orkeon runs show</c> all write and read this one form, so a client needs a single reader.
/// </summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public static class RunEventWire
{
    /// <summary>Version of the envelope this build emits.</summary>
    public const int ProtocolVersion = 2;

    // Relaxed escaping: the stream is a local pipe between two of our own processes, never HTML, and the
    // conversation it carries is French - an accented letter must stay that letter on the wire.
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Names the envelope owns. A payload may never write them: a client must be able to trust that crewId came
    // from the emitter's own knowledge and not from an event's arbitrary contents.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "v", "seq", "ts", "kind", "crewId", "agentId", "correlationId", "causationId",
    };

    /// <summary>
    /// The payload of an event as the wire carries it: the properties of <paramref name="payload"/> in declaration
    /// order, null ones omitted (an absent key is omitted, never written as null) and the envelope's names dropped.
    /// Null when nothing is left.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null",
        Justification = "null is a state of the wire here: no payload omits the key from the event, while an empty object would write it.")]
    public static JsonObject? ToPayload(object? payload)
    {
        if (payload is null)
            return null;

        var extra = JsonSerializer.SerializeToNode(payload)?.AsObject();
        if (extra is null)
            return null;

        var clean = new JsonObject();
        foreach (var property in extra.ToList())
        {
            extra.Remove(property.Key);
            if (property.Value is not null && !ReservedNames.Contains(property.Key))
                clean[property.Key] = property.Value;
        }

        return clean.Count == 0 ? null : clean;
    }

    /// <summary>The line of <paramref name="runEvent"/>, without its end of line.</summary>
    public static string Serialize(RunEvent runEvent)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        var envelope = new JsonObject
        {
            ["v"] = ProtocolVersion,
            ["seq"] = runEvent.Seq,
            ["ts"] = runEvent.Timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["kind"] = runEvent.Kind,
        };

        AddIfPresent(envelope, "crewId", runEvent.CrewId);
        AddIfPresent(envelope, "agentId", runEvent.AgentId);
        AddIfPresent(envelope, "correlationId", runEvent.CorrelationId);
        AddIfPresent(envelope, "causationId", runEvent.CausationId);

        if (runEvent.Payload is { } payload)
        {
            foreach (var property in payload)
            {
                if (property.Value is not null && !ReservedNames.Contains(property.Key))
                    envelope[property.Key] = property.Value.DeepClone();
            }
        }

        return envelope.ToJsonString(WireOptions);
    }

    /// <summary>
    /// Reads a line back. A kind this build does not know is read like any other (a client ignores what it does
    /// not understand); a line that is not an event (no object, no kind, no sequence) is refused.
    /// </summary>
    public static bool TryParse(string line, [NotNullWhen(true)] out RunEvent? runEvent, string? runId = null)
    {
        runEvent = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        JsonObject? envelope;
        try
        {
            envelope = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return false;
        }

        if (envelope is null
            || envelope["kind"] is not JsonValue kindNode || !kindNode.TryGetValue<string>(out var kind)
            || envelope["seq"] is not JsonValue seqNode || !seqNode.TryGetValue<int>(out var seq))
        {
            return false;
        }

        var timestamp = envelope["ts"] is JsonValue tsNode && tsNode.TryGetValue<string>(out var ts)
            && DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : default;

        var payload = new JsonObject();
        foreach (var property in envelope.ToList())
        {
            if (!ReservedNames.Contains(property.Key))
                payload[property.Key] = property.Value?.DeepClone();
        }

        runEvent = new RunEvent
        {
            RunId = runId,
            Seq = seq,
            Timestamp = timestamp,
            Kind = kind,
            CrewId = Text(envelope, "crewId"),
            AgentId = Text(envelope, "agentId"),
            CorrelationId = Text(envelope, "correlationId"),
            CausationId = Text(envelope, "causationId"),
            Payload = payload.Count == 0 ? null : payload,
        };
        return true;
    }

    private static string? Text(JsonObject envelope, string name) =>
        envelope[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static void AddIfPresent(JsonObject envelope, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            envelope[name] = value;
    }
}
