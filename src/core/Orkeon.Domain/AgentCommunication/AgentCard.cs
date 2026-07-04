using System.Text.Json.Serialization;

namespace Orkeon.Domain.AgentCommunication;

/// <summary>
/// Represents an A2A protocol agent card describing an agent's capabilities,
/// skills, and metadata for discovery by other agents.
/// </summary>
public sealed record AgentCard
{
    /// <summary>Gets the display name of the agent.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>Gets a human-readable description of the agent's purpose.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    /// <summary>Gets the base URL where this agent is hosted.</summary>
    [JsonPropertyName("url")]
    public Uri? Url { get; init; }

    /// <summary>Gets the version of the agent.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";

    /// <summary>Gets optional provider information.</summary>
    [JsonPropertyName("provider")]
    public AgentProvider? Provider { get; init; }

    /// <summary>Gets optional authentication configuration.</summary>
    [JsonPropertyName("authentication")]
    public AgentAuthentication? Authentication { get; init; }

    /// <summary>Gets the list of skills this agent offers.</summary>
    [JsonPropertyName("skills")]
    public IReadOnlyList<AgentSkill> Skills { get; init; } = Array.Empty<AgentSkill>();

    /// <summary>Gets optional extension data for vendor-specific properties.</summary>
    [JsonPropertyName("extensions")]
    public IReadOnlyDictionary<string, object>? Extensions { get; init; }
}

/// <summary>
/// Represents a skill offered by an A2A agent.
/// </summary>
public sealed record AgentSkill
{
    private static readonly string[] s_defaultTextModes = ["text/plain"];

    /// <summary>Gets the unique identifier of the skill.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>Gets the display name of the skill.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>Gets a description of what the skill does.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    /// <summary>Gets searchable tags for the skill.</summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>Gets the MIME types accepted as input.</summary>
    [JsonPropertyName("inputModes")]
    public IReadOnlyList<string> InputModes { get; init; } = s_defaultTextModes;

    /// <summary>Gets the MIME types produced as output.</summary>
    [JsonPropertyName("outputModes")]
    public IReadOnlyList<string> OutputModes { get; init; } = s_defaultTextModes;
}

/// <summary>
/// Represents provider/organization information for an A2A agent.
/// </summary>
public sealed record AgentProvider
{
    /// <summary>Gets the organization name.</summary>
    [JsonPropertyName("organization")]
    public string Organization { get; init; } = "";

    /// <summary>Gets an optional contact URL for the provider.</summary>
    [JsonPropertyName("contactUrl")]
    public Uri? ContactUrl { get; init; }
}

/// <summary>
/// Represents authentication requirements for an A2A agent.
/// </summary>
public sealed record AgentAuthentication
{
    /// <summary>Gets the supported authentication schemes (e.g., "Bearer", "ApiKey").</summary>
    [JsonPropertyName("schemes")]
    public IReadOnlyList<string> Schemes { get; init; } = Array.Empty<string>();

    /// <summary>Gets optional credentials hint or reference.</summary>
    [JsonPropertyName("credentials")]
    public string? Credentials { get; init; }
}
