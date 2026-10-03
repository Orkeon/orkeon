namespace Orkeon.Application.Configuration;

/// <summary>
/// The host's caps on model requests — the <c>RateLimiting</c> section. Every model call of the host
/// counts, once, at the entrance of its provider: an agent's turns, the manager, the planner, the RAG
/// pipeline, the judges, the cognitive memory, a script's <c>ctx.llm</c> (GAP-38). Embeddings do not.
/// </summary>
public class RateLimitingOptions
{
    /// <summary>
    /// Maximum model requests per minute across all providers and agents. A request over it is held
    /// in a queue of <see cref="QueueLimit"/>, then retried, then failed.
    /// </summary>
    public int GlobalRequestsPerMinute { get; set; } = 60;

    /// <summary>Maximum model requests per minute per provider, held and refused like the global cap.</summary>
    public int ProviderRequestsPerMinute { get; set; } = 30;

    /// <summary>
    /// Maximum model requests per minute of each agent — each agent instance, not each role: it bounds
    /// the agent's own window with its <c>maxRpm</c>, the stricter winning, and a request over it waits
    /// its turn instead of failing (GAP-38). Read by the execution orchestrator and the manager.
    /// </summary>
    public int AgentRequestsPerMinute { get; set; } = 20;

    /// <summary>
    /// Maximum number of concurrent (in-flight) model requests.
    /// 0 means no concurrency limit (rate limiting only).
    /// </summary>
    public int MaxConcurrentRequests { get; set; }

    /// <summary>
    /// Maximum number of requests the global, per-provider and concurrency caps hold in a queue when
    /// they are reached; a request beyond it is refused, then retried.
    /// </summary>
    public int QueueLimit { get; set; } = 5;
}
