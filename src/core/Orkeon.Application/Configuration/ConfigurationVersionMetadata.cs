namespace Orkeon.Application.Configuration;

/// <summary>
/// Metadata for a configuration version.
/// </summary>
public record ConfigurationVersionMetadata(
    string Id,
    string Version,
    DateTime CreatedAt,
    string CreatedBy,
    string? Comment,
    IReadOnlyList<string> Tags,
    bool IsActive);
