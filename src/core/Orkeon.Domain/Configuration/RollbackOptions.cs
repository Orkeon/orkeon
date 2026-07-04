using Orkeon.Domain.Common;

namespace Orkeon.Domain.Configuration;

/// <summary>Options for configuration rollback.</summary>
public sealed record RollbackOptions
{
    /// <summary>Gets the identifier of the target version to roll back to.</summary>
    public ConfigurationVersionId TargetVersionId { get; init; } = ConfigurationVersionId.Create();
    /// <summary>Gets a value indicating whether to create a backup before rollback.</summary>
    public bool CreateBackup { get; init; } = true;
    /// <summary>Gets a value indicating whether to validate the configuration before rollback.</summary>
    public bool ValidateBeforeRollback { get; init; } = true;
    /// <summary>Gets an optional comment describing the reason for rollback.</summary>
    public string? Comment { get; init; }
    /// <summary>Gets additional metadata for this rollback operation.</summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
}

/// <summary>Result of a rollback operation.</summary>
public sealed record RollbackResult
{
    /// <summary>Gets a value indicating whether the rollback succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets the error message if the rollback failed, otherwise null.</summary>
    public string? Error { get; init; }
    /// <summary>Gets the identifier of the backup version created before rollback, or null.</summary>
    public ConfigurationVersionId? BackupVersionId { get; init; }
    /// <summary>Gets the identifier of the current (rolled-back) version.</summary>
    public ConfigurationVersionId CurrentVersionId { get; init; } = ConfigurationVersionId.Create();
    /// <summary>Gets the identifier of the previous version (before rollback).</summary>
    public ConfigurationVersionId PreviousVersionId { get; init; } = ConfigurationVersionId.Create();
    /// <summary>Gets the timestamp when the rollback was performed.</summary>
    public DateTime RolledBackAt { get; init; } = DateTime.UtcNow;

    /// <summary>Creates a successful rollback result.</summary>
    /// <param name="currentVersionId">The current version after rollback.</param>
    /// <param name="previousVersionId">The previous version before rollback.</param>
    /// <param name="backupVersionId">The backup version identifier.</param>
    /// <returns>A successful <see cref="RollbackResult"/>.</returns>
    public static RollbackResult CreateSuccess(ConfigurationVersionId currentVersionId, ConfigurationVersionId previousVersionId, ConfigurationVersionId? backupVersionId = null)
    {
        return new RollbackResult
        {
            Success = true,
            CurrentVersionId = currentVersionId,
            PreviousVersionId = previousVersionId,
            BackupVersionId = backupVersionId
        };
    }

    /// <summary>Creates a failed rollback result.</summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed <see cref="RollbackResult"/>.</returns>
    public static RollbackResult CreateFailure(string error)
    {
        return new RollbackResult
        {
            Success = false,
            Error = error
        };
    }
}
