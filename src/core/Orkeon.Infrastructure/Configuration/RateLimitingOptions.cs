namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for LLM rate limiting.
/// </summary>
public class RateLimitingOptions
{
    /// <summary>
    /// Maximum LLM requests per minute across all providers and agents.
    /// </summary>
    public int GlobalRequestsPerMinute { get; set; } = 60;

    /// <summary>
    /// Maximum LLM requests per minute per provider.
    /// </summary>
    public int ProviderRequestsPerMinute { get; set; } = 30;

    /// <summary>
    /// Maximum LLM requests per minute per agent.
    /// </summary>
    public int AgentRequestsPerMinute { get; set; } = 20;

    /// <summary>
    /// Maximum number of concurrent (in-flight) LLM requests.
    /// 0 means no concurrency limit (rate limiting only).
    /// </summary>
    public int MaxConcurrentRequests { get; set; }

    /// <summary>
    /// Maximum number of requests that can be queued when the limit is reached.
    /// </summary>
    public int QueueLimit { get; set; } = 5;
}
