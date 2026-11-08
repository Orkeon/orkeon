using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Application.Runs;

/// <summary>The synchronous way the run observers write: they sit on callbacks that cannot await.</summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public static class RunEventSinkExtensions
{
    /// <summary>Emits one event with no identity: the shape of a cycle-level event.</summary>
    public static void Emit(this IRunEventSink sink, string kind, object? payload = null) =>
        sink.Emit(kind, RunEventScope.None, payload);

    /// <summary>
    /// Emits one event: <paramref name="scope"/>'s identity and <paramref name="payload"/>'s properties, flat. The
    /// publisher stamps sequence and time. Envelope names in the payload are dropped and null properties omitted.
    /// </summary>
    public static void Emit(this IRunEventSink sink, string kind, RunEventScope scope, object? payload = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(scope);

        var runEvent = new RunEvent
        {
            Kind = kind,
            CrewId = scope.CrewId,
            AgentId = scope.AgentId,
            CorrelationId = scope.CorrelationId,
            CausationId = scope.CausationId,
            Payload = RunEventWire.ToPayload(payload),
        };

        // The sinks complete synchronously (a line on a stream, an append): there is nothing to deadlock on.
#pragma warning disable CA2012, VSTHRD002
        sink.WriteAsync(runEvent).GetAwaiter().GetResult();
#pragma warning restore CA2012, VSTHRD002
    }
}
