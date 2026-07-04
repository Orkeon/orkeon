using System.Text.Json.Serialization;
using Orkeon.Domain.Constants.Platform;
using Orkeon.Domain.Constants.Resilience;
using Orkeon.Domain.Constants.Security;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Data Transfer Object for Tool entity.
/// Used for external API communication.
/// </summary>
public sealed record ToolDto
{
    /// <summary>
    /// Unique identifier for the tool.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Tool name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Tool description and purpose.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Tool category (file, web, communication, etc.).
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Tool version.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = PlatformDefaults.Version;

    /// <summary>
    /// Tool input schema definition.
    /// </summary>
    [JsonPropertyName("input_schema")]
    public ToolSchemaDto? InputSchema { get; init; }

    /// <summary>
    /// Tool output schema definition.
    /// </summary>
    [JsonPropertyName("output_schema")]
    public ToolSchemaDto? OutputSchema { get; init; }

    /// <summary>
    /// Tool configuration parameters.
    /// </summary>
    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; init; } = [];

    /// <summary>
    /// Whether the tool is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Whether the tool is deprecated.
    /// </summary>
    [JsonPropertyName("deprecated")]
    public bool Deprecated { get; init; }

    /// <summary>
    /// Tool usage metrics.
    /// </summary>
    [JsonPropertyName("metrics")]
    public ToolMetricsDto? Metrics { get; init; }

    /// <summary>
    /// Tool capabilities and features.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>
    /// Required permissions for the tool.
    /// </summary>
    [JsonPropertyName("required_permissions")]
    public IReadOnlyList<string> RequiredPermissions { get; init; } = [];

    /// <summary>
    /// Tool rate limiting configuration.
    /// </summary>
    [JsonPropertyName("rate_limit")]
    public RateLimitDto? RateLimit { get; init; }

    /// <summary>
    /// Tool security configuration.
    /// </summary>
    [JsonPropertyName("security")]
    public ToolSecurityDto? Security { get; init; }

    /// <summary>
    /// Tool creation timestamp.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; init; } = [];
}

/// <summary>
/// Data Transfer Object for tool schema definition.
/// </summary>
public sealed record ToolSchemaDto
{
    /// <summary>
    /// Schema type (object, string, number, etc.).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "object";

    /// <summary>
    /// Schema properties definition.
    /// </summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, ToolPropertyDto> Properties { get; init; } = [];

    /// <summary>
    /// Required property names.
    /// </summary>
    [JsonPropertyName("required")]
    public IReadOnlyList<string> Required { get; init; } = [];

    /// <summary>
    /// Schema title.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// Schema description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Additional validation rules.
    /// </summary>
    [JsonPropertyName("validation_rules")]
    public Dictionary<string, object> ValidationRules { get; init; } = [];
}

/// <summary>
/// Data Transfer Object for tool property definition.
/// </summary>
public sealed record ToolPropertyDto
{
    /// <summary>
    /// Property type (string, number, boolean, etc.).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "string";

    /// <summary>
    /// Property description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Default value for the property.
    /// </summary>
    [JsonPropertyName("default")]
    public object? Default { get; init; }

    /// <summary>
    /// Enumeration of allowed values.
    /// </summary>
    [JsonPropertyName("enum")]
    public IReadOnlyList<object>? Enum { get; init; }

    /// <summary>
    /// Minimum value (for numbers).
    /// </summary>
    [JsonPropertyName("minimum")]
    public double? Minimum { get; init; }

    /// <summary>
    /// Maximum value (for numbers).
    /// </summary>
    [JsonPropertyName("maximum")]
    public double? Maximum { get; init; }

    /// <summary>
    /// Minimum length (for strings).
    /// </summary>
    [JsonPropertyName("min_length")]
    public int? MinLength { get; init; }

    /// <summary>
    /// Maximum length (for strings).
    /// </summary>
    [JsonPropertyName("max_length")]
    public int? MaxLength { get; init; }

    /// <summary>
    /// Regular expression pattern (for strings).
    /// </summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; init; }

    /// <summary>
    /// Whether the property is required.
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; init; }
}

/// <summary>
/// Data Transfer Object for tool usage metrics.
/// </summary>
public sealed record ToolMetricsDto
{
    /// <summary>
    /// Total number of tool calls.
    /// </summary>
    [JsonPropertyName("total_calls")]
    public long TotalCalls { get; init; }

    /// <summary>
    /// Number of successful calls.
    /// </summary>
    [JsonPropertyName("successful_calls")]
    public long SuccessfulCalls { get; init; }

    /// <summary>
    /// Number of failed calls.
    /// </summary>
    [JsonPropertyName("failed_calls")]
    public long FailedCalls { get; init; }

    /// <summary>
    /// Average execution time in milliseconds.
    /// </summary>
    [JsonPropertyName("average_execution_time")]
    public double AverageExecutionTime { get; init; }

    /// <summary>
    /// Success rate (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("success_rate")]
    public double SuccessRate { get; init; }

    /// <summary>
    /// Last successful call timestamp.
    /// </summary>
    [JsonPropertyName("last_successful_call")]
    public DateTime? LastSuccessfulCall { get; init; }

    /// <summary>
    /// Last failed call timestamp.
    /// </summary>
    [JsonPropertyName("last_failed_call")]
    public DateTime? LastFailedCall { get; init; }

    /// <summary>
    /// Error distribution by error type.
    /// </summary>
    [JsonPropertyName("error_distribution")]
    public Dictionary<string, int> ErrorDistribution { get; init; } = [];
}

/// <summary>
/// Data Transfer Object for rate limiting configuration.
/// </summary>
public sealed record RateLimitDto
{
    /// <summary>
    /// Maximum calls per minute.
    /// </summary>
    [JsonPropertyName("calls_per_minute")]
    public int? CallsPerMinute { get; init; }

    /// <summary>
    /// Maximum calls per hour.
    /// </summary>
    [JsonPropertyName("calls_per_hour")]
    public int? CallsPerHour { get; init; }

    /// <summary>
    /// Maximum calls per day.
    /// </summary>
    [JsonPropertyName("calls_per_day")]
    public int? CallsPerDay { get; init; }

    /// <summary>
    /// Maximum concurrent calls.
    /// </summary>
    [JsonPropertyName("max_concurrent_calls")]
    public int? MaxConcurrentCalls { get; init; }

    /// <summary>
    /// Rate limit reset strategy.
    /// </summary>
    [JsonPropertyName("reset_strategy")]
    public string ResetStrategy { get; init; } = ResilienceDefaults.DefaultResetStrategy;
}

/// <summary>
/// Data Transfer Object for tool security configuration.
/// </summary>
public sealed record ToolSecurityDto
{
    /// <summary>
    /// Whether the tool requires authentication.
    /// </summary>
    [JsonPropertyName("requires_authentication")]
    public bool RequiresAuthentication { get; init; }

    /// <summary>
    /// Whether the tool requires authorization.
    /// </summary>
    [JsonPropertyName("requires_authorization")]
    public bool RequiresAuthorization { get; init; }

    /// <summary>
    /// Whether the tool allows external access.
    /// </summary>
    [JsonPropertyName("allow_external_access")]
    public bool AllowExternalAccess { get; init; }

    /// <summary>
    /// Allowed domains for external access.
    /// </summary>
    [JsonPropertyName("allowed_domains")]
    public IReadOnlyList<string> AllowedDomains { get; init; } = [];

    /// <summary>
    /// Input validation rules.
    /// </summary>
    [JsonPropertyName("input_validation_rules")]
    public IReadOnlyList<string> InputValidationRules { get; init; } = [];

    /// <summary>
    /// Output sanitization rules.
    /// </summary>
    [JsonPropertyName("output_sanitization_rules")]
    public IReadOnlyList<string> OutputSanitizationRules { get; init; } = [];

    /// <summary>
    /// Security risk level (low, medium, high, critical).
    /// </summary>
    [JsonPropertyName("risk_level")]
    public string RiskLevel { get; init; } = SecurityDefaults.DefaultRiskLevel;
}
