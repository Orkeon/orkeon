using System.Text.Json.Serialization;
using static Orkeon.Domain.Constants.Llm.EmbeddingDefaults;
using Orkeon.Domain.Constants.Resilience;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Constants.Crew;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Data Transfer Object for crew settings and configuration.
/// Consolidated from Common/DTOs and Crew/DTOs versions.
/// </summary>
public sealed record CrewSettingsDto
{
    /// <summary>
    /// Maximum requests per minute for the crew.
    /// </summary>
    [JsonPropertyName("max_rpm")]
    public double? MaxRpm { get; init; }

    /// <summary>
    /// Whether to share crew context between agents.
    /// </summary>
    [JsonPropertyName("share_crew")]
    public bool ShareCrew { get; init; }

    /// <summary>
    /// Maximum number of execution iterations.
    /// </summary>
    [JsonPropertyName("max_iterations")]
    public int? MaxIterations { get; init; }

    /// <summary>
    /// Maximum execution time per task.
    /// </summary>
    [JsonPropertyName("max_execution_time")]
    public TimeSpan? MaxExecutionTime { get; init; }

    /// <summary>
    /// Maximum execution time for the entire crew in seconds.
    /// </summary>
    [JsonPropertyName("max_execution_time_seconds")]
    public int? MaxExecutionTimeSeconds { get; init; }

    /// <summary>
    /// Maximum number of parallel agents (for parallel process).
    /// </summary>
    [JsonPropertyName("max_parallel_agents")]
    public int? MaxParallelAgents { get; init; }

    /// <summary>
    /// Whether memory is enabled for this crew.
    /// </summary>
    [JsonPropertyName("memory_enabled")]
    public bool MemoryEnabled { get; init; }

    /// <summary>
    /// Whether to enable memory sharing between agents.
    /// </summary>
    [JsonPropertyName("enable_memory_sharing")]
    public bool EnableMemorySharing { get; init; } = true;

    /// <summary>
    /// Memory configuration.
    /// </summary>
    [JsonPropertyName("memory_config")]
    public MemoryConfigDto? MemoryConfig { get; init; }

    /// <summary>
    /// Whether caching is enabled.
    /// </summary>
    [JsonPropertyName("cache_enabled")]
    public bool CacheEnabled { get; init; }

    /// <summary>
    /// Whether to enable output caching.
    /// </summary>
    [JsonPropertyName("enable_output_caching")]
    public bool EnableOutputCaching { get; init; }

    /// <summary>
    /// Output log file path.
    /// </summary>
    [JsonPropertyName("output_log_file")]
    public string? OutputLogFile { get; init; }

    /// <summary>
    /// Language for localization.
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; init; } = CrewDefaults.DefaultLanguage;

    /// <summary>
    /// Custom configuration options.
    /// </summary>
    [JsonPropertyName("custom_options")]
    public Dictionary<string, object> CustomOptions { get; init; } = [];

    /// <summary>
    /// Retry configuration.
    /// </summary>
    [JsonPropertyName("retry_config")]
    public RetryConfigDto? RetryConfig { get; init; }

    /// <summary>
    /// Timeout configuration.
    /// </summary>
    [JsonPropertyName("timeout_config")]
    public TimeoutConfigDto? TimeoutConfig { get; init; }

    /// <summary>
    /// Callback configuration for execution events.
    /// </summary>
    [JsonPropertyName("callback_config")]
    public CallbackConfigDto? CallbackConfig { get; init; }
}

/// <summary>
/// Data Transfer Object for callback configuration.
/// </summary>
public sealed record CallbackConfigDto
{
    /// <summary>
    /// Webhook URL for execution events (optional).
    /// </summary>
    [JsonPropertyName("webhook_url")]
    public Uri? WebhookUrl { get; init; }

    /// <summary>
    /// Whether to send notifications on completion.
    /// </summary>
    [JsonPropertyName("notify_on_completion")]
    public bool NotifyOnCompletion { get; init; }

    /// <summary>
    /// Whether to send notifications on failure.
    /// </summary>
    [JsonPropertyName("notify_on_failure")]
    public bool NotifyOnFailure { get; init; } = true;

    /// <summary>
    /// Custom callback settings.
    /// </summary>
    [JsonPropertyName("custom_settings")]
    public Dictionary<string, object> CustomSettings { get; init; } = [];
}

/// <summary>
/// Data Transfer Object for memory configuration.
/// </summary>
public sealed record MemoryConfigDto
{
    /// <summary>
    /// Memory provider type (Redis, InMemory, etc.).
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = MemoryDefaults.DefaultProvider;

    /// <summary>
    /// Connection string for external memory providers.
    /// </summary>
    [JsonPropertyName("connection_string")]
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Memory capacity limits.
    /// </summary>
    [JsonPropertyName("limits")]
    public MemoryLimitsDto? Limits { get; init; }

    /// <summary>
    /// Memory cleanup configuration.
    /// </summary>
    [JsonPropertyName("cleanup")]
    public MemoryCleanupDto? Cleanup { get; init; }

    /// <summary>
    /// Vector search configuration.
    /// </summary>
    [JsonPropertyName("vector_config")]
    public VectorConfigDto? VectorConfig { get; init; }
}

/// <summary>
/// Data Transfer Object for memory limits.
/// </summary>
public sealed record MemoryLimitsDto
{
    /// <summary>
    /// Maximum number of short-term memory items.
    /// </summary>
    [JsonPropertyName("max_short_term_items")]
    public int? MaxShortTermItems { get; init; }

    /// <summary>
    /// Maximum number of long-term memory items.
    /// </summary>
    [JsonPropertyName("max_long_term_items")]
    public int? MaxLongTermItems { get; init; }

    /// <summary>
    /// Maximum number of episodic memory items.
    /// </summary>
    [JsonPropertyName("max_episodic_items")]
    public int? MaxEpisodicItems { get; init; }

    /// <summary>
    /// Maximum memory size in bytes.
    /// </summary>
    [JsonPropertyName("max_memory_size")]
    public long? MaxMemorySize { get; init; }
}

/// <summary>
/// Data Transfer Object for memory cleanup configuration.
/// </summary>
public sealed record MemoryCleanupDto
{
    /// <summary>
    /// Whether automatic cleanup is enabled.
    /// </summary>
    [JsonPropertyName("auto_cleanup")]
    public bool AutoCleanup { get; init; }

    /// <summary>
    /// Cleanup interval.
    /// </summary>
    [JsonPropertyName("cleanup_interval")]
    public TimeSpan? CleanupInterval { get; init; }

    /// <summary>
    /// Memory retention period.
    /// </summary>
    [JsonPropertyName("retention_period")]
    public TimeSpan? RetentionPeriod { get; init; }

    /// <summary>
    /// Minimum relevance score for retention.
    /// </summary>
    [JsonPropertyName("min_relevance_score")]
    public double? MinRelevanceScore { get; init; }
}

/// <summary>
/// Data Transfer Object for vector search configuration.
/// </summary>
public sealed record VectorConfigDto
{
    /// <summary>
    /// Vector dimension size.
    /// </summary>
    [JsonPropertyName("dimension")]
    public int Dimension { get; init; } = DefaultDimension;

    /// <summary>
    /// Similarity metric (cosine, euclidean, etc.).
    /// </summary>
    [JsonPropertyName("similarity_metric")]
    public string SimilarityMetric { get; init; } = "cosine";

    /// <summary>
    /// Minimum similarity threshold.
    /// </summary>
    [JsonPropertyName("min_similarity")]
    public double MinSimilarity { get; init; } = SearchDefaults.DefaultSimilarityThreshold;

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    [JsonPropertyName("max_results")]
    public int MaxResults { get; init; } = 10;
}

/// <summary>
/// Data Transfer Object for retry configuration.
/// </summary>
public sealed record RetryConfigDto
{
    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    [JsonPropertyName("max_attempts")]
    public int MaxAttempts { get; init; } = AgentDefaults.MaxRetryLimit;

    /// <summary>
    /// Delay between retry attempts.
    /// </summary>
    [JsonPropertyName("retry_delay")]
    public TimeSpan RetryDelay { get; init; } = ResilienceDefaults.DefaultRetryInitialDelay;

    /// <summary>
    /// Whether to use exponential backoff.
    /// </summary>
    [JsonPropertyName("use_exponential_backoff")]
    public bool UseExponentialBackoff { get; init; }

    /// <summary>
    /// Backoff multiplier.
    /// </summary>
    [JsonPropertyName("backoff_multiplier")]
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// Maximum retry delay.
    /// </summary>
    [JsonPropertyName("max_retry_delay")]
    public TimeSpan? MaxRetryDelay { get; init; }
}

/// <summary>
/// Data Transfer Object for timeout configuration.
/// </summary>
public sealed record TimeoutConfigDto
{
    /// <summary>
    /// Default timeout for LLM requests.
    /// </summary>
    [JsonPropertyName("llm_timeout")]
    public TimeSpan? LlmTimeout { get; init; }

    /// <summary>
    /// Default timeout for tool calls.
    /// </summary>
    [JsonPropertyName("tool_timeout")]
    public TimeSpan? ToolTimeout { get; init; }

    /// <summary>
    /// Default timeout for task execution.
    /// </summary>
    [JsonPropertyName("task_timeout")]
    public TimeSpan? TaskTimeout { get; init; }

    /// <summary>
    /// Default timeout for crew execution.
    /// </summary>
    [JsonPropertyName("crew_timeout")]
    public TimeSpan? CrewTimeout { get; init; }

    /// <summary>
    /// Default timeout for memory operations.
    /// </summary>
    [JsonPropertyName("memory_timeout")]
    public TimeSpan? MemoryTimeout { get; init; }
}
