using System.Text.Json.Serialization;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Task.DTOs;
using Orkeon.Application.Common.DTOs;
using static Orkeon.Domain.Constants.Memory.MemoryDefaults;

namespace Orkeon.Application.Crew.DTOs;

/// <summary>
/// Request DTO for creating a new crew.
/// </summary>
public sealed record CreateCrewRequest
{
    /// <summary>
    /// Crew name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Crew description and purpose.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Process type for task execution.
    /// </summary>
    [JsonPropertyName("process")]
    public ProcessType Process { get; init; } = ProcessType.Sequential;

    /// <summary>
    /// Whether verbose logging is enabled.
    /// </summary>
    [JsonPropertyName("verbose")]
    public bool Verbose { get; init; }

    /// <summary>
    /// Whether planning is enabled for this crew.
    /// </summary>
    [JsonPropertyName("planning")]
    public bool Planning { get; init; }

    /// <summary>
    /// List of agents to include in the crew.
    /// </summary>
    [JsonPropertyName("agents")]
    public IReadOnlyList<CrewAgentRequest> Agents { get; init; } = [];

    /// <summary>
    /// List of tasks to assign to the crew.
    /// </summary>
    [JsonPropertyName("tasks")]
    public IReadOnlyList<CrewTaskRequest> Tasks { get; init; } = [];

    /// <summary>
    /// Crew configuration settings.
    /// </summary>
    [JsonPropertyName("settings")]
    public CrewSettingsDto? Settings { get; init; }

    /// <summary>
    /// LLM configuration for manager agent (required if hierarchical process).
    /// </summary>
    [JsonPropertyName("manager_llm")]
    public LlmConfigDto? ManagerLlm { get; init; }

    /// <summary>
    /// Additional metadata for the crew.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; init; } = [];
}

/// <summary>
/// Request DTO for updating an existing crew.
/// </summary>
public sealed record UpdateCrewRequest
{
    /// <summary>
    /// Updated crew name (optional).
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Updated crew description (optional).
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Updated process type (optional).
    /// </summary>
    [JsonPropertyName("process")]
    public ProcessType? Process { get; init; }

    /// <summary>
    /// Updated verbose setting (optional).
    /// </summary>
    [JsonPropertyName("verbose")]
    public bool? Verbose { get; init; }

    /// <summary>
    /// Updated planning setting (optional).
    /// </summary>
    [JsonPropertyName("planning")]
    public bool? Planning { get; init; }

    /// <summary>
    /// Updated crew settings (optional).
    /// </summary>
    [JsonPropertyName("settings")]
    public CrewSettingsDto? Settings { get; init; }

    /// <summary>
    /// Updated manager LLM configuration (optional).
    /// </summary>
    [JsonPropertyName("manager_llm")]
    public LlmConfigDto? ManagerLlm { get; init; }

    /// <summary>
    /// Updated metadata (optional).
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Agent configuration for crew creation.
/// </summary>
public sealed record CrewAgentRequest
{
    /// <summary>
    /// Existing agent ID to add to crew (if using existing agent).
    /// </summary>
    [JsonPropertyName("existing_agent_id")]
    public string? ExistingAgentId { get; init; }

    /// <summary>
    /// New agent configuration (if creating new agent for crew).
    /// </summary>
    [JsonPropertyName("new_agent")]
    public CreateAgentRequest? NewAgent { get; init; }

    /// <summary>
    /// Agent-specific settings within this crew.
    /// </summary>
    [JsonPropertyName("crew_specific_settings")]
    public AgentSettingsDto? CrewSpecificSettings { get; init; }
}

/// <summary>
/// Task configuration for crew creation.
/// </summary>
public sealed record CrewTaskRequest
{
    /// <summary>
    /// Existing task ID to add to crew (if using existing task).
    /// </summary>
    [JsonPropertyName("existing_task_id")]
    public string? ExistingTaskId { get; init; }

    /// <summary>
    /// New task configuration (if creating new task for crew).
    /// </summary>
    [JsonPropertyName("new_task")]
    public CreateTaskRequest? NewTask { get; init; }

    /// <summary>
    /// Task-specific settings within this crew.
    /// </summary>
    [JsonPropertyName("crew_specific_settings")]
    public TaskSettingsDto? CrewSpecificSettings { get; init; }
}

/// <summary>
/// Crew configuration settings DTO.
/// </summary>
public sealed record CrewSettingsDto
{
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
    /// Whether to enable memory sharing between agents.
    /// </summary>
    [JsonPropertyName("enable_memory_sharing")]
    public bool EnableMemorySharing { get; init; } = true;

    /// <summary>
    /// Whether to enable output caching.
    /// </summary>
    [JsonPropertyName("enable_output_caching")]
    public bool EnableOutputCaching { get; init; }

    /// <summary>
    /// Retry configuration for crew execution.
    /// </summary>
    [JsonPropertyName("retry_config")]
    public CrewRetryConfigDto? RetryConfig { get; init; }

    /// <summary>
    /// Memory configuration for the crew.
    /// </summary>
    [JsonPropertyName("memory_config")]
    public CrewMemoryConfigDto? MemoryConfig { get; init; }

    /// <summary>
    /// Callback configuration for execution events.
    /// </summary>
    [JsonPropertyName("callback_config")]
    public CrewCallbackConfigDto? CallbackConfig { get; init; }
}

/// <summary>
/// Crew retry configuration DTO.
/// </summary>
public sealed record CrewRetryConfigDto
{
    /// <summary>
    /// Maximum number of crew execution retries.
    /// </summary>
    [JsonPropertyName("max_retries")]
    public int MaxRetries { get; init; } = 1;

    /// <summary>
    /// Whether to retry failed tasks individually.
    /// </summary>
    [JsonPropertyName("retry_failed_tasks")]
    public bool RetryFailedTasks { get; init; } = true;

    /// <summary>
    /// Delay between retries in seconds.
    /// </summary>
    [JsonPropertyName("retry_delay_seconds")]
    public int RetryDelaySeconds { get; init; } = 30;
}

/// <summary>
/// Crew memory configuration DTO.
/// </summary>
public sealed record CrewMemoryConfigDto
{
    /// <summary>
    /// Whether to use shared memory across agents.
    /// </summary>
    [JsonPropertyName("use_shared_memory")]
    public bool UseSharedMemory { get; init; } = true;

    /// <summary>
    /// Memory persistence type.
    /// </summary>
    [JsonPropertyName("memory_provider")]
    public string MemoryProvider { get; init; } = DefaultProvider;

    /// <summary>
    /// Maximum items in crew memory.
    /// </summary>
    [JsonPropertyName("max_memory_items")]
    public int MaxMemoryItems { get; init; } = 1000;

    /// <summary>
    /// Memory cleanup interval in minutes.
    /// </summary>
    [JsonPropertyName("cleanup_interval_minutes")]
    public int CleanupIntervalMinutes { get; init; } = 60;
}

/// <summary>
/// Crew callback configuration DTO.
/// </summary>
public sealed record CrewCallbackConfigDto
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
