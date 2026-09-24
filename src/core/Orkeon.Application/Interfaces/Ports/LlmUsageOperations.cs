namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// The kinds of work an LLM call is attributed to (<see cref="LlmUsageAttribution.Operation"/>,
/// carried as <see cref="CostUsageEvent.OperationType"/>) — what a cost report groups by. The
/// scripting facade adds its own: the name of the <c>ctx.llm.*</c> method that made the call
/// (<c>complete</c>, <c>chat</c>, <c>stream</c>, <c>extract</c>, <c>decide</c>, <c>act</c>).
/// </summary>
public static class LlmUsageOperations
{
    /// <summary>An agent working on its task: each turn, each retry, each correction round.</summary>
    public const string Agent = "agent";

    /// <summary>The hierarchical manager assigning a task or reviewing its output.</summary>
    public const string Manager = "manager";

    /// <summary>The crew planner drafting the execution plan.</summary>
    public const string Planning = "planning";

    /// <summary>A RAG pipeline: query rewriting, reranking, grading and grounded generation.</summary>
    public const string Rag = "rag";

    /// <summary>The cognitive memory services: analysis, contradiction checks, consolidation.</summary>
    public const string Memory = "memory";

    /// <summary>An LLM step of a flow.</summary>
    public const string Flow = "flow";

    /// <summary>An LLM judge grading an output.</summary>
    public const string Judge = "judge";

    /// <summary>A call no scope claimed — counted all the same.</summary>
    public const string Unattributed = "unattributed";
}
