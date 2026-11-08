using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Crew;

/// <summary>
/// A host's delta sink that also says which DLP the tool-call fragments sent to it must go through. The producers of
/// fragments outside the crew loop (the scripted <c>ctx.llm.act</c>) read <see cref="Dlp"/> from the sink they were
/// given and hold the arguments of each call until it ends when it is set. Every call forwards to the host's sink.
/// </summary>
public sealed class ToolCallDlpDeltaSink : ILlmDeltaSink
{
    private readonly ILlmDeltaSink _inner;

    /// <summary>Wraps the host's sink.</summary>
    /// <param name="inner">The host's sink.</param>
    /// <param name="dlp">The DLP its tool-call fragments go through.</param>
    public ToolCallDlpDeltaSink(ILlmDeltaSink inner, ToolCallDeltaDlp dlp)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Dlp = dlp ?? throw new ArgumentNullException(nameof(dlp));
    }

    /// <summary>The DLP the producers apply to the fragments they send.</summary>
    public ToolCallDeltaDlp Dlp { get; }

    /// <summary>
    /// The sink the producer is to use: wrapped when the host registered DLP, as it was when it did not, and null
    /// when there is no sink (a host with no sink keeps its turns buffered).
    /// </summary>
    /// <param name="sink">The host's registered sink, or null.</param>
    /// <param name="services">The host's services.</param>
    /// <returns>The sink to hand to the producers.</returns>
    public static ILlmDeltaSink? Wrap(ILlmDeltaSink? sink, IServiceProvider services) =>
        sink is not null && ToolCallDeltaDlp.From(services) is { } dlp ? new ToolCallDlpDeltaSink(sink, dlp) : sink;

    /// <inheritdoc />
    public void OnDelta(string delta) => _inner.OnDelta(delta);

    /// <inheritdoc />
    public void OnToolCallDelta(ToolCallDelta toolCall) => _inner.OnToolCallDelta(toolCall);

    /// <inheritdoc />
    public void OnTurnCompleted() => _inner.OnTurnCompleted();

    /// <inheritdoc />
    public ILlmDeltaSink BeginTurn(string? label) => _inner.BeginTurn(label);
}
