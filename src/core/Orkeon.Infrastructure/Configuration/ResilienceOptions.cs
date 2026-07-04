namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for resilience policies applied to infrastructure providers.
/// </summary>
public class ResilienceOptions
{
    /// <summary>Gets or sets the maximum number of retries for LLM calls.</summary>
    public int LlmMaxRetries { get; set; } = 5;
    /// <summary>Gets or sets the timeout in seconds for LLM calls.</summary>
    public int LlmTimeoutSeconds { get; set; } = 60;
    /// <summary>Gets or sets the failure threshold for the circuit breaker.</summary>
    public int CircuitBreakerThreshold { get; set; } = 3;
    /// <summary>Gets or sets the circuit breaker open duration in seconds.</summary>
    public int CircuitBreakerDurationSeconds { get; set; } = 30;
    /// <summary>Gets or sets the maximum number of retries for database operations.</summary>
    public int DatabaseMaxRetries { get; set; } = 3;
    /// <summary>Gets or sets the maximum number of retries for Redis operations.</summary>
    public int RedisMaxRetries { get; set; } = 3;
}
