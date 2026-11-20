using Orkeon.Application.Runs;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// Adds the trial pass's decision circuit to every event it relays (#431): the <c>llm.call.*</c> lines of a pass say
/// which circuit they ran under, with its cause when off. Without a circuit, the event goes through unchanged.
/// </summary>
internal sealed class ForgeCircuitSink : IRunEventSink
{
    private readonly IRunEventSink _inner;
    private readonly Func<(string? Circuit, string? Cause)> _circuit;

    /// <summary>Relays to <paramref name="inner"/>, reading the circuit of the moment from <paramref name="circuit"/>.</summary>
    public ForgeCircuitSink(IRunEventSink inner, Func<(string? Circuit, string? Cause)> circuit)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
    }

    /// <inheritdoc />
    public Task WriteAsync(RunEvent runEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        var (circuit, cause) = _circuit();
        if (circuit is null)
            return _inner.WriteAsync(runEvent, ct);

        var payload = runEvent.Payload?.DeepClone().AsObject() ?? [];
        payload["circuit"] = circuit;
        if (cause is not null)
            payload["cause"] = cause;
        return _inner.WriteAsync(runEvent with { Payload = payload }, ct);
    }
}
