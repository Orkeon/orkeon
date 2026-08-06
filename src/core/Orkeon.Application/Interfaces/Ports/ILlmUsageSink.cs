namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Host-registered receiver for LLM usage events emitted by the scripted runtime
/// (<c>ctx.llm.*</c>): one event per completed LLM call, carrying the token counts the
/// provider reported. A host typically forwards them to <see cref="ICostBudgetManager"/>
/// (session accounting) and/or attributes them to the unit of work that made the call
/// (per-agent token readouts). A host that registers no sink keeps the runtime behaviour
/// byte-identical — usage is then simply not observed.
/// </summary>
/// <remarks>
/// Called from the runtime's async flow, potentially on pool threads — implementations
/// must be thread-safe and stay cheap (counter updates, dictionary writes); they sit on
/// the LLM-call latency path. Implementations must not throw; the runtime additionally
/// shields itself, so a faulty sink degrades to unobserved usage, never to a failed call.
/// </remarks>
public interface ILlmUsageSink
{
    /// <summary>Receives the usage of one completed LLM call.</summary>
    void Record(CostUsageEvent usage);
}
