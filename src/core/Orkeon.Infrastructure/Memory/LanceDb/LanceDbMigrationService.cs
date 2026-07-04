using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Memory.Base;
using Orkeon.Infrastructure.Memory.Migration;

namespace Orkeon.Infrastructure.Memory.LanceDb;

/// <summary>
/// Service that migrates data from other memory providers to LanceDB.
/// Delegates to the generic <see cref="MemoryMigrationService"/> for the actual migration logic.
/// </summary>
public partial class LanceDbMigrationService
{
    private readonly MemoryMigrationService _migrationService;
    private readonly ILogger<LanceDbMigrationService> _logger;

    /// <summary>Initializes a new instance of <see cref="LanceDbMigrationService"/>.</summary>
    /// <param name="migrationService">The generic memory migration service.</param>
    /// <param name="logger">The logger.</param>
    public LanceDbMigrationService(
        MemoryMigrationService migrationService,
        ILogger<LanceDbMigrationService> logger)
    {
        ArgumentNullException.ThrowIfNull(migrationService);
        _migrationService = migrationService;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Migrates all memory items from the <paramref name="source"/> provider to the
    /// <paramref name="target"/> LanceDB provider.
    /// </summary>
    /// <param name="source">The source memory provider (e.g. InMemory, Redis, ChromaDB).</param>
    /// <param name="target">The target LanceDB memory provider.</param>
    /// <param name="options">Optional migration options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="MigrationResult"/> describing the outcome.</returns>
    public Task<MigrationResult> MigrateToLanceDbAsync(
        MemoryProviderBase source,
        LanceDbMemoryProvider target,
        MigrationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        return MigrateToLanceDbCoreAsync(source, target, options, cancellationToken);
    }

    private async Task<MigrationResult> MigrateToLanceDbCoreAsync(
        MemoryProviderBase source,
        LanceDbMemoryProvider target,
        MigrationOptions? options,
        CancellationToken cancellationToken)
    {
        LogStartingMigrationToLanceDb(source.Name);

        var result = await _migrationService.MigrateAsync(source, target, options, cancellationToken).ConfigureAwait(false);

        LogMigrationToLanceDbCompleted(result.MigratedItems, result.FailedItems, result.SkippedItems);

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting migration to LanceDB from {SourceProvider}")]
    private partial void LogStartingMigrationToLanceDb(string sourceProvider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migration to LanceDB completed: Migrated={Migrated}, Failed={Failed}, Skipped={Skipped}")]
    private partial void LogMigrationToLanceDbCompleted(int migrated, int failed, int skipped);
}
