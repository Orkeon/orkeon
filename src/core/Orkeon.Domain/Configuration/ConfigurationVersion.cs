using Orkeon.Domain.Common;

namespace Orkeon.Domain.Configuration;

/// <summary>Represents a version of a configuration.</summary>
public record ConfigurationVersion
{
    /// <summary>Gets the unique identifier of this version.</summary>
    public ConfigurationVersionId Id { get; }
    /// <summary>Gets the name of the configuration.</summary>
    public string ConfigurationName { get; }
    /// <summary>Gets the version string.</summary>
    public string Version { get; }
    /// <summary>Gets the configuration content.</summary>
    public string Content { get; }
    /// <summary>Gets the timestamp when this version was created.</summary>
    public DateTime CreatedAt { get; }
    /// <summary>Gets the identifier of who created this version, or null.</summary>
    public string? CreatedBy { get; }
    /// <summary>Gets the description of this version, or null.</summary>
    public string? Description { get; }
    /// <summary>Gets additional metadata for this version.</summary>
    public Dictionary<string, object> Metadata { get; }
    /// <summary>Gets a value indicating whether this is the active version.</summary>
    public bool IsActive { get; init; }

    /// <summary>Initializes a new instance of <see cref="ConfigurationVersion"/>.</summary>
    /// <param name="configurationName">The configuration name.</param>
    /// <param name="version">The version string.</param>
    /// <param name="content">The configuration content.</param>
    /// <param name="createdBy">Who created this version.</param>
    /// <param name="description">A description of this version.</param>
    /// <param name="metadata">Additional metadata.</param>
    /// <param name="isActive">Whether this version is active.</param>
    public ConfigurationVersion(
        string configurationName,
        string version,
        string content,
        string? createdBy = null,
        string? description = null,
        Dictionary<string, object>? metadata = null,
        bool isActive = false)
    {
        Id = ConfigurationVersionId.Create();
        ArgumentNullException.ThrowIfNull(configurationName);
        ConfigurationName = configurationName;
        ArgumentNullException.ThrowIfNull(version);
        Version = version;
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
        Description = description;
        Metadata = metadata ?? [];
        IsActive = isActive;
    }

    /// <summary>Creates a new configuration version with the given name, version, and content.</summary>
    /// <param name="name">The configuration name.</param>
    /// <param name="version">The version string.</param>
    /// <param name="content">The configuration content.</param>
    /// <returns>A new <see cref="ConfigurationVersion"/>.</returns>
    public static ConfigurationVersion Create(string name, string version, string content)
    {
        return new ConfigurationVersion(name, version, content);
    }
}
