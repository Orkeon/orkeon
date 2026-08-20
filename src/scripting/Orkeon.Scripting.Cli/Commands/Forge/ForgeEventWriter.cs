using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>Injectable clock so the protocol's golden test is deterministic.</summary>
internal interface IForgeClock
{
    /// <summary>The current instant.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>The real clock.</summary>
internal sealed class SystemForgeClock : IForgeClock
{
    /// <summary>Shared stateless instance.</summary>
    public static SystemForgeClock Instance { get; } = new();

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// The forge event protocol (SPEC-ORKEON-FORGE §6): one JSON document per line, common
/// envelope <c>{ v, seq, ts, kind, … }</c>, <c>seq</c> strictly increasing. The line — not
/// this class — is the contract: a golden test pins the serialized form, exactly like
/// <c>orkeon doctor --json</c> pins its own.
/// </summary>
internal sealed class ForgeEventWriter
{
    /// <summary>Version of the envelope this build emits.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>
    /// Relaxed escaping: the stream is a local pipe between two of our own processes, never
    /// HTML, and the conversation it carries is French — <c>é</c> must stay <c>é</c> on the
    /// wire, not become <c>\u00E9</c> in every log a human reads.
    /// </summary>
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly TextWriter _output;
    private readonly IForgeClock _clock;
    private readonly Lock _gate = new();
    private int _sequence;

    /// <summary>Creates a writer over <paramref name="output"/> (stdout in the CLI).</summary>
    public ForgeEventWriter(TextWriter output, IForgeClock? clock = null)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _clock = clock ?? SystemForgeClock.Instance;
    }

    /// <summary>
    /// Emits one event line. <paramref name="payload"/> is an anonymous object whose
    /// properties are merged into the envelope after the four fixed fields, so every
    /// event stays flat — the shape the spec's table documents.
    /// </summary>
    public void Emit(string kind, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        lock (_gate)
        {
            var envelope = new JsonObject
            {
                ["v"] = ProtocolVersion,
                ["seq"] = ++_sequence,
                ["ts"] = _clock.UtcNow.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["kind"] = kind,
            };

            if (payload is not null)
            {
                var extra = JsonSerializer.SerializeToNode(payload)?.AsObject();
                if (extra is not null)
                {
                    // Copy in declaration order; ToList() because detaching a node while
                    // iterating its parent invalidates the enumerator. The envelope's own
                    // fields are not overridable: a payload naming v/seq/ts/kind loses.
                    foreach (var property in extra.ToList())
                    {
                        extra.Remove(property.Key);
                        if (!envelope.ContainsKey(property.Key))
                            envelope[property.Key] = property.Value;
                    }
                }
            }

            _output.WriteLine(envelope.ToJsonString(WireOptions));
            _output.Flush();
        }
    }

    /// <summary>Opening event of a run of the engine.</summary>
    public void SessionStarted(ForgeSession session, bool resumed) =>
        Emit("session.started", new
        {
            slug = session.Document.Slug,
            dir = session.Directory,
            format = session.Document.Format,
            resumed,
            budget = session.Document.Budget.ToEventPayload(),
        });

    /// <summary>The cycle entered a stage.</summary>
    public void StageEntered(ForgeState stage, int iteration) =>
        Emit("stage.entered", new { stage = Spell(stage), iteration });

    /// <summary>An anomaly, recoverable or not.</summary>
    public void Error(string code, string message, bool recoverable) =>
        Emit("error", new { code, message, recoverable });

    /// <summary>Closing event; mirrors the process exit code.</summary>
    public void SessionFinished(string status, int exitCode) =>
        Emit("session.finished", new { status, exitCode });

    /// <summary>Stage names on the wire are lowercase; the enum spelling stays a C# detail.</summary>
#pragma warning disable CA1308 // lowercase is the wire spelling of the protocol contract, not a comparison normalization
    internal static string Spell(ForgeState state) => state.ToString().ToLowerInvariant();
#pragma warning restore CA1308
}
