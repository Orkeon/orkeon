namespace Orkeon.Infrastructure.Memory.Migration;

/// <summary>
/// Options controlling the behavior of a memory migration operation.
/// </summary>
public sealed record MigrationOptions
{
    /// <summary>Gets the number of keys to process per batch.</summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>Gets a value indicating whether items should be deleted from the source after successful migration.</summary>
    public bool DeleteFromSource { get; init; }

    /// <summary>Gets a value indicating whether existing items in the target should be overwritten.</summary>
    public bool OverwriteExisting { get; init; } = true;
}

/// <summary>
/// Describes the outcome of a memory migration operation.
/// </summary>
public sealed record MigrationResult
{
    /// <summary>Gets the total number of items found in the source.</summary>
    public int TotalItems { get; init; }

    /// <summary>Gets the number of items successfully migrated.</summary>
    public int MigratedItems { get; init; }

    /// <summary>Gets the number of items that failed to migrate.</summary>
    public int FailedItems { get; init; }

    /// <summary>Gets the number of items skipped (e.g., already existing in target).</summary>
    public int SkippedItems { get; init; }

    /// <summary>Gets the total duration of the migration.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Gets the list of error messages encountered during migration.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
