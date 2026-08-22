using System.Text.Json;

namespace Orkeon.Studio.Core.Events;

/// <summary>
/// One line of an Orkeon event stream: the envelope fields plus the whole document, kept as
/// JSON — a projection reads the payload in one place, this type only guarantees the
/// envelope. The same shape carries both streaming verbs (<c>forge</c> and <c>run</c>), so
/// a client needs a single reader.
/// </summary>
public sealed record OrkeonEvent
{
    /// <summary>Protocol version of the envelope.</summary>
    public required int Version { get; init; }

    /// <summary>Strictly increasing sequence number.</summary>
    public required long Seq { get; init; }

    /// <summary>Event kind — or unknown, kept as-is so the client can still show it.</summary>
    public required string Kind { get; init; }

    /// <summary>The whole event document (envelope + flat payload), detached from its parser.</summary>
    public required JsonElement Root { get; init; }

    /// <summary>The crew execution this event belongs to; absent outside a run.</summary>
    public string? CrewId => GetString("crewId");

    /// <summary>The agent that emitted it; absent for crew-level events.</summary>
    public string? AgentId => GetString("agentId");

    /// <summary>Ties every event of one unit of work together.</summary>
    public string? CorrelationId => GetString("correlationId");

    /// <summary>
    /// The event that caused this one — what makes a delegation tree or a spawn graph
    /// reconstructible on this side of the wire.
    /// </summary>
    public string? CausationId => GetString("causationId");

    /// <summary>String property, null when absent or not a string.</summary>
    public string? GetString(string name) =>
        Root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Boolean property, null when absent.</summary>
    public bool? GetBool(string name) =>
        Root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    /// <summary>
    /// Integer property, null when absent or not an integral number. TryGetInt64, not
    /// GetInt64: a fractional or out-of-range number throws FormatException — valid JSON the
    /// tolerant-reader contract says must come back null, not explode on the reader thread.
    /// </summary>
    public long? GetInt64(string name) =>
        Root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
            ? number
            : null;

    /// <summary>Floating-point property, null when absent or not a representable number.</summary>
    public double? GetDouble(string name) =>
        Root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
            ? number
            : null;

    /// <summary>
    /// String-array property, empty when absent. Non-string entries are skipped rather than
    /// failing the whole list: a screen showing three of four choices beats one showing none.
    /// </summary>
    public IReadOnlyList<string> GetStrings(string name)
    {
        if (!Root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        var items = new List<string>(value.GetArrayLength());
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String && element.GetString() is { } text)
                items.Add(text);
        }

        return items;
    }

    /// <summary>
    /// A property rendered back to JSON text, whatever its shape — the way to carry an opaque
    /// payload through to a screen without this type having to know what is in it. Null when
    /// absent.
    /// </summary>
    public string? GetRawJson(string name) =>
        Root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetRawText()
            : null;
}

/// <summary>
/// Tolerant reader of one stream line (the <c>DoctorReportParser</c> discipline): a line
/// that is not an event — malformed JSON, missing envelope — comes back <c>false</c> and
/// the caller shows it raw instead of losing it. The protocol is versioned; the version is
/// surfaced, never enforced here — a client decides what to do with a future one.
/// </summary>
public static class OrkeonEventParser
{
    /// <summary>The protocol version this client was written against.</summary>
    public const int KnownProtocolVersion = 2;

    /// <summary>Parses one stdout line into an event; false when the line is not one.</summary>
    public static bool TryParse(string? line, out OrkeonEvent? orkeonEvent)
    {
        orkeonEvent = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty("seq", out var seq) || seq.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            // TryGet, not Get: {"v":2.5} or an out-of-range seq is valid JSON with the wrong
            // numeric shape — GetInt32/GetInt64 throw FormatException there, which escaped the
            // JsonException catch below and propagated onto the process-reader thread.
            if (!version.TryGetInt32(out var versionNumber) || !seq.TryGetInt64(out var seqNumber))
                return false;

            orkeonEvent = new OrkeonEvent
            {
                Version = versionNumber,
                Seq = seqNumber,
                Kind = kind.GetString()!,
                Root = root.Clone(),
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
