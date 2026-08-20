using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orkeon.Scripting.Cli.Events;

/// <summary>Injectable clock so every protocol golden test is deterministic.</summary>
internal interface IOrkeonClock
{
    /// <summary>The current instant.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>The real clock.</summary>
internal sealed class SystemOrkeonClock : IOrkeonClock
{
    /// <summary>Shared stateless instance.</summary>
    public static SystemOrkeonClock Instance { get; } = new();

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// The identity an event carries in its envelope: which crew, which agent, and how it chains
/// to what caused it. Every field is optional — during the Atelier's interview no crew is
/// running, and most events have no cause. An absent field is **omitted from the line**,
/// never written as <c>null</c>: absence reads as absence, and the lines stay short.
/// </summary>
internal sealed record OrkeonEventScope
{
    /// <summary>Nothing known — the shape most Atelier events carry.</summary>
    public static readonly OrkeonEventScope None = new();

    /// <summary>The crew execution this event belongs to.</summary>
    public string? CrewId { get; init; }

    /// <summary>The agent that emitted it; absent for crew-level events.</summary>
    public string? AgentId { get; init; }

    /// <summary>Ties every event of one unit of work together.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// The event that caused this one. It is what makes a delegation tree or a spawn graph
    /// reconstructible on the client side, which no other field can replace.
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>Whether anything at all is known — an empty scope writes no envelope field.</summary>
    public bool IsEmpty =>
        CrewId is null && AgentId is null && CorrelationId is null && CausationId is null;
}

/// <summary>
/// The Orkeon event protocol (SPEC-ORKEON-FORGE §6, generalised by BUS-01): one JSON
/// document per line, common envelope <c>{ v, seq, ts, kind, …identity… }</c> followed by a
/// flat payload, <c>seq</c> strictly increasing. Both verbs that stream — <c>forge</c> and
/// <c>run</c> — emit this one shape, so a client needs a single reader.
/// <para>
/// The line — not this class — is the contract: a golden test pins the serialized form,
/// exactly like <c>orkeon doctor --json</c> pins its own.
/// </para>
/// </summary>
internal class OrkeonEventWriter
{
    /// <summary>Version of the envelope this build emits.</summary>
    public const int ProtocolVersion = 2;

    /// <summary>
    /// Relaxed escaping: the stream is a local pipe between two of our own processes, never
    /// HTML, and the conversation it carries is French — <c>é</c> must stay <c>é</c> on the
    /// wire, not become <c>é</c> in every log a human reads.
    /// </summary>
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Names the envelope owns. A payload may never write them — not even when the scope
    /// left them out: a client must be able to trust that <c>crewId</c> came from the
    /// emitter's own knowledge and not from an event's arbitrary contents.
    /// </summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "v", "seq", "ts", "kind", "crewId", "agentId", "correlationId", "causationId",
    };

    private readonly TextWriter _output;
    private readonly IOrkeonClock _clock;
    private readonly Lock _gate = new();
    private int _sequence;

    /// <summary>Creates a writer over <paramref name="output"/> (stdout in the CLI).</summary>
    public OrkeonEventWriter(TextWriter output, IOrkeonClock? clock = null)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _clock = clock ?? SystemOrkeonClock.Instance;
    }

    /// <summary>Emits one event line with no identity — the shape of a cycle-level event.</summary>
    public void Emit(string kind, object? payload = null) =>
        Emit(kind, OrkeonEventScope.None, payload);

    /// <summary>
    /// Emits one event line. The envelope carries the four fixed fields, then whatever
    /// <paramref name="scope"/> knows, then <paramref name="payload"/>'s properties merged
    /// in flat — the shape the spec's table documents. Envelope fields are not overridable:
    /// a payload naming one of them loses.
    /// </summary>
    public void Emit(string kind, OrkeonEventScope scope, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(scope);

        lock (_gate)
        {
            var envelope = new JsonObject
            {
                ["v"] = ProtocolVersion,
                ["seq"] = ++_sequence,
                ["ts"] = _clock.UtcNow.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["kind"] = kind,
            };

            AddIfPresent(envelope, "crewId", scope.CrewId);
            AddIfPresent(envelope, "agentId", scope.AgentId);
            AddIfPresent(envelope, "correlationId", scope.CorrelationId);
            AddIfPresent(envelope, "causationId", scope.CausationId);

            if (payload is not null)
            {
                var extra = JsonSerializer.SerializeToNode(payload)?.AsObject();
                if (extra is not null)
                {
                    // Copy in declaration order; ToList() because detaching a node while
                    // iterating its parent invalidates the enumerator.
                    foreach (var property in extra.ToList())
                    {
                        extra.Remove(property.Key);
                        if (!ReservedNames.Contains(property.Key))
                            envelope[property.Key] = property.Value;
                    }
                }
            }

            _output.WriteLine(envelope.ToJsonString(WireOptions));
            _output.Flush();
        }
    }

    private static void AddIfPresent(JsonObject envelope, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            envelope[name] = value;
    }
}
