using System.Text.Json.Serialization;
using Orkeon.Application.Constants.Execution;

namespace Orkeon.Application.Task.DTOs;

/// <summary>
/// Task execution settings DTO.
/// </summary>
public sealed record TaskSettingsDto
{
    /// <summary>
    /// Maximum execution time in seconds.
    /// </summary>
    [JsonPropertyName("max_execution_time_seconds")]
    public int? MaxExecutionTimeSeconds { get; init; }

    /// <summary>
    /// Maximum number of iterations.
    /// </summary>
    [JsonPropertyName("max_iterations")]
    public int? MaxIterations { get; init; }

    /// <summary>
    /// Whether to enable verbose logging.
    /// </summary>
    [JsonPropertyName("verbose")]
    public bool Verbose { get; init; }

    /// <summary>
    /// Whether delegation is allowed for this task.
    /// </summary>
    [JsonPropertyName("allow_delegation")]
    public bool AllowDelegation { get; init; } = true;

    /// <summary>
    /// Whether to save task output to memory.
    /// </summary>
    [JsonPropertyName("save_to_memory")]
    public bool SaveToMemory { get; init; } = true;

    /// <summary>
    /// Custom retry configuration.
    /// </summary>
    [JsonPropertyName("retry_config")]
    public TaskRetryConfigDto? RetryConfig { get; init; }
}

/// <summary>
/// Task retry configuration DTO.
/// </summary>
public sealed record TaskRetryConfigDto
{
    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    [JsonPropertyName("max_retries")]
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Base delay between retries in seconds.
    /// </summary>
    [JsonPropertyName("base_delay_seconds")]
    public int BaseDelaySeconds { get; init; } = 5;

    /// <summary>
    /// Whether to use exponential backoff.
    /// </summary>
    [JsonPropertyName("use_exponential_backoff")]
    public bool UseExponentialBackoff { get; init; } = true;

    /// <summary>
    /// Maximum delay between retries in seconds.
    /// </summary>
    [JsonPropertyName("max_delay_seconds")]
    public int MaxDelaySeconds { get; init; } = ExecutionDefaults.DefaultMaxExecutionSeconds;
}
