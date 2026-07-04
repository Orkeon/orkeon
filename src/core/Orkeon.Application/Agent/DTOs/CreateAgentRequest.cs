using System.Text.Json.Serialization;
using Orkeon.Application.Common.DTOs;

namespace Orkeon.Application.Agent.DTOs;

/// <summary>
/// Request DTO for creating a new agent.
/// </summary>
public sealed record CreateAgentRequest
{
    /// <summary>
    /// Agent's role (e.g., "Senior Developer", "Marketing Specialist").
    /// </summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>
    /// Agent's primary goal or objective.
    /// </summary>
    [JsonPropertyName("goal")]
    public required string Goal { get; init; }

    /// <summary>
    /// Agent's background story and expertise.
    /// </summary>
    [JsonPropertyName("backstory")]
    public string Backstory { get; init; } = string.Empty;

    /// <summary>
    /// List of tool names to assign to this agent.
    /// </summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>
    /// Agent settings and behavior configuration.
    /// </summary>
    [JsonPropertyName("settings")]
    public AgentSettingsDto? Settings { get; init; }

    /// <summary>
    /// LLM configuration for this agent (optional).
    /// </summary>
    [JsonPropertyName("llm_config")]
    public LlmConfigDto? LlmConfig { get; init; }

    /// <summary>
    /// Agent capabilities and skills.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string> Capabilities { get; init; } = [];
}

/// <summary>
/// Request DTO for updating an existing agent.
/// </summary>
public sealed record UpdateAgentRequest
{
    /// <summary>
    /// Updated agent goal (optional).
    /// </summary>
    [JsonPropertyName("goal")]
    public string? Goal { get; init; }

    /// <summary>
    /// Updated agent backstory (optional).
    /// </summary>
    [JsonPropertyName("backstory")]
    public string? Backstory { get; init; }

    /// <summary>
    /// Updated list of tool names (optional).
    /// </summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>
    /// Updated agent settings (optional).
    /// </summary>
    [JsonPropertyName("settings")]
    public AgentSettingsDto? Settings { get; init; }

    /// <summary>
    /// Updated LLM configuration (optional).
    /// </summary>
    [JsonPropertyName("llm_config")]
    public LlmConfigDto? LlmConfig { get; init; }

    /// <summary>
    /// Updated agent capabilities (optional).
    /// </summary>
    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string>? Capabilities { get; init; }
}
