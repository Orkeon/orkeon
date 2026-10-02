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
}
