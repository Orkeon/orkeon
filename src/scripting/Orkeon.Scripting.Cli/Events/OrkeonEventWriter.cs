using Orkeon.Application.Runs;

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
internal class OrkeonEventWriter : IRunEventSink
{
    /// <summary>Version of the envelope this build emits.</summary>
    public const int ProtocolVersion = RunEventWire.ProtocolVersion;

    private readonly TextWriter _output;
    private readonly IOrkeonClock _clock;
    private readonly Lock _gate = new();
    private int _sequence;

    /// <summary>
    /// The run in flight, when the writer sits in a host that runs crews: an event the scope gives no
    /// correlation id carries the run's identifier, the one the checkpoint session and the A2A and API
    /// task carry too (FWK-01). Null: only what the scope names is written.
    /// </summary>
    public Orkeon.Application.Interfaces.Services.IRunScope? RunScope { get; init; }

    /// <summary>Creates a writer over <paramref name="output"/> (stdout in the CLI).</summary>
    public OrkeonEventWriter(TextWriter output, IOrkeonClock? clock = null)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _clock = clock ?? SystemOrkeonClock.Instance;
    }

    /// <summary>
    /// Takes an event a shared observer produced (the model-call observer serves the Forge stream too): this writer
    /// numbers and times its own stream, so only the event's kind, identity and payload are kept.
    /// </summary>
    public System.Threading.Tasks.Task WriteAsync(RunEvent runEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        Emit(
            runEvent.Kind,
            new OrkeonEventScope
            {
                CrewId = runEvent.CrewId,
                AgentId = runEvent.AgentId,
                CorrelationId = runEvent.CorrelationId,
                CausationId = runEvent.CausationId,
            },
            runEvent.Payload);
        return System.Threading.Tasks.Task.CompletedTask;
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
            var runEvent = new RunEvent
            {
                Seq = ++_sequence,
                Timestamp = _clock.UtcNow,
                Kind = kind,
                CrewId = scope.CrewId,
                AgentId = scope.AgentId,
                CorrelationId = scope.CorrelationId ?? RunScope?.Current?.ExecutionId.ToString(),
                CausationId = scope.CausationId,
                Payload = RunEventWire.ToPayload(payload),
            };

            _output.WriteLine(RunEventWire.Serialize(runEvent));
            _output.Flush();
        }
    }
}
