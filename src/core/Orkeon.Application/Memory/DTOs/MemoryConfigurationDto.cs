using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Application.Memory.DTOs;

/// <summary>
/// Memory configuration DTO with provider settings.
/// </summary>
public sealed record MemoryConfigurationDto
{
    /// <summary>Gets or sets the provider.</summary>
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether enable short term.
    /// </summary>
    [JsonPropertyName("enable_short_term")]
    public bool EnableShortTerm { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether enable long term.
    /// </summary>
    [JsonPropertyName("enable_long_term")]
    public bool EnableLongTerm { get; init; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether enable episodic.
    /// </summary>
    [JsonPropertyName("enable_episodic")]
    public bool EnableEpisodic { get; init; } = false;

    /// <summary>Gets or sets the max short term entries.</summary>
    [JsonPropertyName("max_short_term_entries")]
    public int MaxShortTermEntries { get; init; } = 1000;

    /// <summary>Gets or sets the max long term entries.</summary>
    [JsonPropertyName("max_long_term_entries")]
    public int MaxLongTermEntries { get; init; } = 10000;

    /// <summary>Gets or sets the retention period.</summary>
    [JsonPropertyName("retention_period")]
    public TimeSpan RetentionPeriod { get; init; } = MemoryDefaults.DefaultRetentionPeriod;

    /// <summary>Provider Settings.</summary>
    [JsonPropertyName("provider_settings")]
    public ImmutableDictionary<string, object> ProviderSettings { get; init; } = [];
}
