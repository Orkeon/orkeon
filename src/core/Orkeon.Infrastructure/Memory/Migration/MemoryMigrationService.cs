using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Memory.Base;

namespace Orkeon.Infrastructure.Memory.Migration;

/// <summary>
/// Service that migrates memory items between two <see cref="MemoryProviderBase"/> instances.
/// Supports batching, error handling, optional deletion from source, and skip-existing semantics.
/// </summary>
public partial class MemoryMigrationService
{
    private readonly ILogger<MemoryMigrationService> _logger;

    /// <summary>Initializes a new instance of <see cref="MemoryMigrationService"/>.</summary>
    /// <param name="logger">The logger.</param>
    public MemoryMigrationService(ILogger<MemoryMigrationService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Migrates all items from <paramref name="source"/> to <paramref name="target"/>.
    /// </summary>
    /// <param name="source">The source memory provider to read from.</param>
    /// <param name="target">The target memory provider to write to.</param>
    /// <param name="options">Optional migration options controlling batch size and behavior.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="MigrationResult"/> describing the outcome.</returns>
    public Task<MigrationResult> MigrateAsync(
        MemoryProviderBase source,
        MemoryProviderBase target,
        MigrationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        return MigrateAsyncCore(source, target, options, cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Migration fault barrier: an unexpected error aborts the run, is recorded in the errors list and logged, and a partial MigrationResult is returned rather than throwing.")]
    private async Task<MigrationResult> MigrateAsyncCore(
        MemoryProviderBase source,
        MemoryProviderBase target,
        MigrationOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new MigrationOptions();

        var stopwatch = Stopwatch.StartNew();
        var totalItems = 0;
        var migratedItems = 0;
        var failedItems = 0;
        var skippedItems = 0;
        var errors = new List<string>();

        LogStartingMemoryMigrationBatchsizeDeletefromsource(options.BatchSize, options.DeleteFromSource, options.OverwriteExisting);

        try
        {
            var skip = 0;
            var batchSize = options.BatchSize > 0 ? options.BatchSize : 100;

            while (!cancellationToken.IsCancellationRequested)
            {
                var keys = await source.ListKeysAsync(skip, batchSize, cancellationToken).ConfigureAwait(false);

                if (keys.Count == 0)
                    break;

                totalItems += keys.Count;
                var (batchMigrated, batchSkipped, batchFailed) = await MigrateBatchAsync(
                    source, target, keys, options, errors, cancellationToken).ConfigureAwait(false);
                migratedItems += batchMigrated;
                skippedItems += batchSkipped;
                failedItems += batchFailed;
                skip += keys.Count;
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Migration aborted: {ex.Message}";
            errors.Add(errorMessage);
            LogMigrationAbortedDueToError(ex);
        }

        stopwatch.Stop();

        var result = new MigrationResult
        {
            TotalItems = totalItems,
            MigratedItems = migratedItems,
            FailedItems = failedItems,
            SkippedItems = skippedItems,
            Duration = stopwatch.Elapsed,
            Errors = errors
        };

        LogMigrationCompletedTotalMigratedFailed(result.TotalItems, result.MigratedItems, result.FailedItems, result.SkippedItems, result.Duration);

        return result;
    }

    /// <summary>
    /// Migrates a batch of keys from source to target. Returns counts of migrated, skipped, and failed items.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-key fault barrier: a key that fails to migrate is counted as failed, recorded in the errors list and logged, so the remaining keys in the batch are still migrated.")]
    private async Task<(int Migrated, int Skipped, int Failed)> MigrateBatchAsync(
        MemoryProviderBase source,
        MemoryProviderBase target,
        IReadOnlyList<string> keys,
        MigrationOptions options,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var migrated = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var key in keys)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var keyResult = await MigrateKeyAsync(source, target, key, options, cancellationToken).ConfigureAwait(false);
                switch (keyResult)
                {
                    case MigrateKeyResult.Migrated:
                        migrated++;
                        break;
                    case MigrateKeyResult.Skipped:
                        skipped++;
                        break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                var errorMessage = $"Failed to migrate key '{key}': {ex.Message}";
                errors.Add(errorMessage);
                LogFailedToMigrateKey(ex, key);
            }
        }

        return (migrated, skipped, failed);
    }

    /// <summary>
    /// Migrates a single key from source to target. Returns the outcome.
    /// </summary>
    private static async Task<MigrateKeyResult> MigrateKeyAsync(
        MemoryProviderBase source,
        MemoryProviderBase target,
        string key,
        MigrationOptions options,
        CancellationToken cancellationToken)
    {
        // Skip if target already has the key and OverwriteExisting is false
        if (!options.OverwriteExisting)
        {
            var existingItem = await target.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (existingItem != null)
                return MigrateKeyResult.Skipped;
        }

        // Get from source
        var item = await source.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (item == null)
            return MigrateKeyResult.Skipped;

        // Store in target
        await target.StoreAsync(key, item, cancellationToken).ConfigureAwait(false);

        // Optionally delete from source
        if (options.DeleteFromSource)
        {
            await source.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }

        return MigrateKeyResult.Migrated;
    }

    private enum MigrateKeyResult
    {
        Migrated,
        Skipped
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Starting memory migration (BatchSize={BatchSize}, DeleteFromSource={Delete}, OverwriteExisting={Overwrite})")]
    private partial void LogStartingMemoryMigrationBatchsizeDeletefromsource(int batchSize, bool delete, bool overwrite);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Skipped existing key: {Key}")]
    private partial void LogSkippedExistingKey(string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Skipped null item for key: {Key}")]
    private partial void LogSkippedNullItemForKey(string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Migrated key: {Key}")]
    private partial void LogMigratedKey(string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Failed to migrate key: {Key}")]
    private partial void LogFailedToMigrateKey(Exception ex, string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Migration aborted due to error")]
    private partial void LogMigrationAbortedDueToError(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Migration completed: Total={Total}, Migrated={Migrated}, Failed={Failed}, Skipped={Skipped}, Duration={Duration}")]
    private partial void LogMigrationCompletedTotalMigratedFailed(int total, int migrated, int failed, int skipped, TimeSpan duration);

}
