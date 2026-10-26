namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Host-registered renderer for streamed LLM output (native incremental
/// rendering). When a host registers a sink, the scripted <c>ctx.llm.act</c> loop and the crews'
/// chat-client agent loop (GAP-32) switch to the provider's streaming path (when available) and
/// push every content delta here, so a REPL — or <c>orkeon run --stream</c>, which registers one —
/// can render tokens as they arrive without the script passing <c>onDelta</c>. A host that
/// registers no sink keeps the buffered behaviour byte-identical.
/// </summary>
/// <remarks>
/// Callbacks are invoked sequentially from one streaming enumeration, but that
/// enumeration runs on a pool thread, and the tasks of a crew that run at once (a parallel
/// wave) stream their turns at once — implementations must be thread-safe with respect
/// to their output device and must stay cheap (rendering, logging); they are on the
/// token-latency path.
/// </remarks>
public interface ILlmDeltaSink
{
    /// <summary>Receives one streamed content delta (never null or empty).</summary>
    void OnDelta(string delta);

    /// <summary>
    /// Signals the end of one streamed LLM turn, letting the renderer terminate the line
    /// or flush. Called once per streamed turn, after its last delta.
    /// </summary>
    void OnTurnCompleted();

    /// <summary>
    /// Opens one streamed turn: the sink the turn's deltas and its <see cref="OnTurnCompleted"/> go to.
    /// Two turns that stream at once (two acts of concurrent commands, the tasks of a parallel wave)
    /// each hold their own, so a renderer can keep their output apart (#211). The default is the
    /// sink itself: a sink that records or counts needs no turn of its own.
    /// </summary>
    /// <param name="label">What the turn belongs to as the caller knows it (a task id); null when it knows nothing.</param>
    ILlmDeltaSink BeginTurn(string? label) => this;
}
