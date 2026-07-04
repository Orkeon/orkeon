using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Base;
using Orkeon.Infrastructure.Security.Secrets;

namespace Orkeon.Infrastructure.Security.Encryption;

/// <summary>
/// Service that rotates the encryption key protecting the contents of a memory store:
/// every entry is decrypted with the old key and re-encrypted with the new key (R2.8).
/// </summary>
public interface IKeyRotationService
{
    /// <summary>
    /// Re-encrypts every entry of <paramref name="store"/> from <paramref name="oldProvider"/>
    /// to <paramref name="newProvider"/> using a two-phase traversal: staging (re-encrypted
    /// copies are written next to the originals), then switch (copies replace the originals).
    /// </summary>
    /// <remarks>
    /// <para><paramref name="store"/> must be the RAW underlying provider — not wrapped in
    /// <c>EncryptedMemoryProviderDecorator</c> — so ciphertexts are read and written as-is.</para>
    /// <para>Failure semantics:</para>
    /// <list type="bullet">
    /// <item><description><b>Staging failure</b> — staged copies are rolled back; the store
    /// remains fully readable with the old key.</description></item>
    /// <item><description><b>Switch failure</b> — roll-forward: re-running this method with the
    /// same providers resumes the rotation. Entries are classified by authenticated decryption
    /// (AES-GCM), so none is ever lost nor double-encrypted.</description></item>
    /// </list>
    /// <para>The rotation assumes no concurrent writers on the store during the run. The new key
    /// version is persisted in a state marker entry
    /// (<see cref="KeyRotationDefaults.StateMarkerKey"/>) and on each re-encrypted entry
    /// (<see cref="KeyRotationDefaults.KeyVersionProperty"/>).</para>
    /// </remarks>
    /// <param name="store">The raw memory store whose entries are re-encrypted.</param>
    /// <param name="oldProvider">The encryption provider holding the current (old) key.</param>
    /// <param name="newProvider">The encryption provider holding the new key.</param>
    /// <param name="options">Optional rotation options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="KeyRotationResult"/> describing the completed run.</returns>
    /// <exception cref="KeyRotationException">The rotation could not complete; the message states
    /// whether the store was rolled back or a re-run is required to resume.</exception>
    System.Threading.Tasks.Task<KeyRotationResult> RotateAsync(
        MemoryProviderBase store,
        IEncryptionProvider oldProvider,
        IEncryptionProvider newProvider,
        KeyRotationOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a new AES key (CSPRNG) and persists it under <paramref name="secretName"/>
    /// via the writable secret provider. Refuses to overwrite an existing secret (fail-closed):
    /// silently replacing a key that may still protect data would orphan every ciphertext
    /// produced with it. Provision the new key under a fresh, versioned secret name, then call
    /// <see cref="RotateAsync"/> with providers bound to the old and new names.
    /// </summary>
    /// <param name="secretStore">The writable secret store persisting the key.</param>
    /// <param name="secretName">The logical secret name to create.</param>
    /// <param name="keySizeInBits">The AES key size (128, 192 or 256).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">A secret already exists under that name.</exception>
    System.Threading.Tasks.Task ProvisionNewKeyAsync(
        IWritableSecretProvider secretStore,
        string secretName,
        int keySizeInBits = 256,
        CancellationToken ct = default);
}

/// <summary>
/// Real implementation of <see cref="IKeyRotationService"/> (R2.8): two-phase traversal
/// (staging then switch) with rollback on staging failure, idempotent resume (roll-forward)
/// on switch failure, and persisted key-version metadata. A success log is only emitted once
/// every entry has actually been re-encrypted and switched.
/// </summary>
public sealed partial class KeyRotationService : IKeyRotationService
{
    private static readonly string[] StateMarkerTags = { "orkeon-key-rotation-state" };

    private readonly ILogger<KeyRotationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyRotationService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public KeyRotationService(ILogger<KeyRotationService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<KeyRotationResult> RotateAsync(
        MemoryProviderBase store,
        IEncryptionProvider oldProvider,
        IEncryptionProvider newProvider,
        KeyRotationOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(oldProvider);
        ArgumentNullException.ThrowIfNull(newProvider);

        if (ReferenceEquals(oldProvider, newProvider))
            throw new ArgumentException(
                "Old and new encryption providers must be distinct instances bound to distinct keys.",
                nameof(newProvider));

        if (!oldProvider.IsEnabled || !newProvider.IsEnabled)
            throw new InvalidOperationException(
                "Key rotation requires both encryption providers to be enabled (fail-closed): " +
                "rotating against a disabled provider would silently mix plaintext and ciphertext entries.");

        var effectiveOptions = options ?? new KeyRotationOptions();
        if (effectiveOptions.BatchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(options), effectiveOptions.BatchSize,
                "BatchSize must be at least 1.");

        return RotateAsyncCore(store, oldProvider, newProvider, effectiveOptions, ct);
    }

    private async System.Threading.Tasks.Task<KeyRotationResult> RotateAsyncCore(
        MemoryProviderBase store,
        IEncryptionProvider oldProvider,
        IEncryptionProvider newProvider,
        KeyRotationOptions options,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAtUtc = DateTime.UtcNow;
        var version = string.IsNullOrWhiteSpace(options.NewKeyVersion)
            ? $"rot-{startedAtUtc:yyyyMMddTHHmmssfff}Z"
            : options.NewKeyVersion!;

        var context = new RotationContext(store, oldProvider, newProvider, options, version, startedAtUtc);

        var candidates = await SnapshotCandidateKeysAsync(store, options.BatchSize, ct).ConfigureAwait(false);
        LogRotationStarted(candidates.Count, version);

        // Phase 1 — staging: originals stay untouched; rolls itself back and throws on failure.
        await WriteStateAsync(store, KeyRotationDefaults.PhaseStaging, context, completedAtUtc: null, ct).ConfigureAwait(false);
        var staging = await StageEntriesAsync(context, candidates, ct).ConfigureAwait(false);

        // Phase 2 — switch: roll-forward on failure (re-run RotateAsync to resume).
        try
        {
            await WriteStateAsync(store, KeyRotationDefaults.PhaseCommitting, context, completedAtUtc: null, ct).ConfigureAwait(false);
            await CommitStagedAsync(context, staging.StagedKeys, ct).ConfigureAwait(false);
            await SweepStaleStagingAsync(store, options.BatchSize, ct).ConfigureAwait(false);
            await WriteStateAsync(store, KeyRotationDefaults.PhaseCompleted, context, DateTime.UtcNow, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not KeyRotationException and not OperationCanceledException)
        {
            throw new KeyRotationException(
                "Key rotation failed during the switch phase. No entry is lost: each one remains " +
                "decryptable with exactly one of the two keys. Re-run RotateAsync with the same " +
                "providers to resume (roll-forward).", ex);
        }

        stopwatch.Stop();

        var result = new KeyRotationResult
        {
            TotalEntries = candidates.Count,
            RotatedEntries = staging.StagedKeys.Count,
            AlreadyRotatedEntries = staging.AlreadyRotated,
            UnreadableEntries = staging.Unreadable,
            NewKeyVersion = version,
            Duration = stopwatch.Elapsed,
            Errors = staging.Errors,
        };

        // The success log is emitted ONLY after the switch fully completed (no false assurance).
        LogRotationCompleted(result.RotatedEntries, result.AlreadyRotatedEntries, result.UnreadableEntries, result.Duration);
        return result;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task ProvisionNewKeyAsync(
        IWritableSecretProvider secretStore,
        string secretName,
        int keySizeInBits = 256,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        if (keySizeInBits is not (128 or 192 or 256))
            throw new ArgumentOutOfRangeException(nameof(keySizeInBits), keySizeInBits,
                "AES key size must be 128, 192 or 256 bits.");

        return ProvisionNewKeyAsyncCore(secretStore, secretName, keySizeInBits, ct);
    }

    private async System.Threading.Tasks.Task ProvisionNewKeyAsyncCore(
        IWritableSecretProvider secretStore,
        string secretName,
        int keySizeInBits,
        CancellationToken ct)
    {
        if (await secretStore.ExistsAsync(secretName, ct).ConfigureAwait(false))
            throw new InvalidOperationException(
                $"Secret '{secretName}' already exists. Refusing to overwrite a key that may still " +
                "protect data (fail-closed). Provision the new key under a fresh, versioned secret name " +
                "and re-encrypt the data with IKeyRotationService.RotateAsync before retiring the old key.");

        var keyBytes = RandomNumberGenerator.GetBytes(keySizeInBits / 8);
        try
        {
            await secretStore.StoreSecretAsync(secretName, Convert.ToBase64String(keyBytes), ct).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }

        // True success: the key has actually been persisted.
        LogNewKeyProvisioned(secretName);
    }

    // -------------------------------------------------------------------------
    // Phase 1 — staging
    // -------------------------------------------------------------------------

    private async System.Threading.Tasks.Task<StagingOutcome> StageEntriesAsync(
        RotationContext context,
        IReadOnlyList<string> candidates,
        CancellationToken ct)
    {
        var stagedKeys = new List<string>();
        var errors = new List<string>();
        var alreadyRotated = 0;
        var unreadable = 0;

        try
        {
            foreach (var key in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var item = await context.Store.GetAsync(key, ct).ConfigureAwait(false);
                if (item is null)
                    continue; // Deleted between snapshot and read — nothing to rotate.

                var plaintext = await TryDecryptAsync(context.OldProvider, item.Content, ct).ConfigureAwait(false);
                if (plaintext is not null)
                {
                    var reEncrypted = await context.NewProvider.EncryptStringAsync(plaintext, ct).ConfigureAwait(false);
                    var stagedItem = BuildRotatedItem(item, reEncrypted, context.Version);
                    await context.Store.StoreAsync(key + KeyRotationDefaults.StagingSuffix, stagedItem, ct).ConfigureAwait(false);
                    stagedKeys.Add(key);
                    continue;
                }

                if (await TryDecryptAsync(context.NewProvider, item.Content, ct).ConfigureAwait(false) is not null)
                {
                    // Resume case: the entry is already on the new key — skip (never double-encrypt).
                    alreadyRotated++;
                    continue;
                }

                unreadable++;
                LogEntryUnreadable(key);
                errors.Add($"Entry '{key}' is not decryptable with the old key nor the new key; left untouched.");

                if (!context.Options.ContinueOnUnreadableEntries)
                {
                    throw new KeyRotationException(
                        $"Entry '{key}' is not decryptable with the old key nor the new key. " +
                        "Aborting before any switch (fail-closed) and rolling back staged copies. " +
                        $"Set {nameof(KeyRotationOptions)}.{nameof(KeyRotationOptions.ContinueOnUnreadableEntries)} " +
                        "to skip such entries instead.")
                    {
                        AffectedKeys = new[] { key },
                    };
                }
            }
        }
        catch (Exception ex)
        {
            LogStagingFailedRollingBack(ex, stagedKeys.Count);
            await RollbackStagingAsync(context, stagedKeys).ConfigureAwait(false);

            if (ex is KeyRotationException or OperationCanceledException)
                throw;

            throw new KeyRotationException(
                "Key rotation failed during the staging phase. All staged copies were rolled back; " +
                "the store remains fully readable with the old key.", ex);
        }

        return new StagingOutcome(stagedKeys, alreadyRotated, unreadable, errors);
    }

    /// <summary>
    /// Best-effort removal of the staged copies written by this run. Uses
    /// <see cref="CancellationToken.None"/> so cleanup still runs when the failure was a cancellation.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort rollback: failures deleting individual staged copies or writing the rolled-back state marker are logged and swallowed so the rollback completes for the remaining keys and never masks the original rotation failure.")]
    private async System.Threading.Tasks.Task RollbackStagingAsync(RotationContext context, List<string> stagedKeys)
    {
        foreach (var key in stagedKeys)
        {
            try
            {
                await context.Store.DeleteAsync(key + KeyRotationDefaults.StagingSuffix, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogRollbackDeleteFailed(ex, key);
            }
        }

        try
        {
            await WriteStateAsync(context.Store, KeyRotationDefaults.PhaseRolledBack, context, DateTime.UtcNow, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogStateMarkerWriteFailed(ex);
        }

        LogRotationRolledBack(stagedKeys.Count);
    }

    // -------------------------------------------------------------------------
    // Phase 2 — commit: promote the staged copies into place
    // -------------------------------------------------------------------------

    private async System.Threading.Tasks.Task CommitStagedAsync(
        RotationContext context,
        IReadOnlyList<string> stagedKeys,
        CancellationToken ct)
    {
        var committed = 0;
        foreach (var key in stagedKeys)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var stagingKey = key + KeyRotationDefaults.StagingSuffix;
                var stagedItem = await context.Store.GetAsync(stagingKey, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Staged copy '{stagingKey}' disappeared before the switch.");

                await context.Store.StoreAsync(key, stagedItem, ct).ConfigureAwait(false);
                await context.Store.DeleteAsync(stagingKey, ct).ConfigureAwait(false);
                committed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCommitFailed(ex, key, committed, stagedKeys.Count);
                throw new KeyRotationException(
                    $"Key rotation switch interrupted at entry '{key}' after {committed}/{stagedKeys.Count} " +
                    "switched entries. No entry is lost: each one remains decryptable with exactly one of " +
                    "the two keys. Re-run RotateAsync with the same providers to resume (roll-forward).", ex)
                {
                    AffectedKeys = new[] { key },
                };
            }
        }
    }

    /// <summary>
    /// Deletes stale staging copies left behind by previously interrupted runs. Only called after a
    /// fully completed switch, where every remaining staging key is stale by definition.
    /// </summary>
    private async System.Threading.Tasks.Task SweepStaleStagingAsync(MemoryProviderBase store, int batchSize, CancellationToken ct)
    {
        var allKeys = await ListAllKeysAsync(store, batchSize, ct).ConfigureAwait(false);
        foreach (var key in allKeys.Where(static k => k.EndsWith(KeyRotationDefaults.StagingSuffix, StringComparison.Ordinal)))
        {
            try
            {
                await store.DeleteAsync(key, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogStaleStagingDeleteFailed(ex, key);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async System.Threading.Tasks.Task<List<string>> SnapshotCandidateKeysAsync(
        MemoryProviderBase store, int batchSize, CancellationToken ct)
    {
        var allKeys = await ListAllKeysAsync(store, batchSize, ct).ConfigureAwait(false);
        return allKeys
            .Where(static k => !k.EndsWith(KeyRotationDefaults.StagingSuffix, StringComparison.Ordinal)
                               && !string.Equals(k, KeyRotationDefaults.StateMarkerKey, StringComparison.Ordinal))
            .ToList();
    }

    private static async System.Threading.Tasks.Task<List<string>> ListAllKeysAsync(
        MemoryProviderBase store, int batchSize, CancellationToken ct)
    {
        var allKeys = new List<string>();
        var skip = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await store.ListKeysAsync(skip, batchSize, ct).ConfigureAwait(false);
            if (page.Count == 0)
                break;
            allKeys.AddRange(page);
            skip += page.Count;
        }

        return allKeys;
    }

    /// <summary>
    /// Attempts an authenticated decryption; returns <see langword="null"/> when the content is not
    /// a ciphertext produced with the provider's key (AES-GCM tag mismatch, malformed Base64, or
    /// truncated payload). This classification is what makes the rotation idempotent.
    /// </summary>
    private static async System.Threading.Tasks.Task<string?> TryDecryptAsync(
        IEncryptionProvider provider, string content, CancellationToken ct)
    {
        try
        {
            return await provider.DecryptStringAsync(content, ct).ConfigureAwait(false);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Rebuilds the entry with the re-encrypted content while preserving its identity, embedding,
    /// importance and metadata, and stamps the new key version on its custom properties.
    /// </summary>
    private static MemoryItem BuildRotatedItem(MemoryItem original, string reEncryptedContent, string version)
    {
        var properties = original.Metadata.CustomProperties is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(original.Metadata.CustomProperties);
        properties[KeyRotationDefaults.KeyVersionProperty] = version;

        var metadata = original.Metadata with { CustomProperties = properties };
        return MemoryItem.Restore(original.Id, reEncryptedContent, original.Embedding, original.Importance, metadata);
    }

    private static System.Threading.Tasks.Task WriteStateAsync(
        MemoryProviderBase store,
        string phase,
        RotationContext context,
        DateTime? completedAtUtc,
        CancellationToken ct)
    {
        var state = new KeyRotationState
        {
            Phase = phase,
            KeyVersion = context.Version,
            StartedAtUtc = context.StartedAtUtc,
            CompletedAtUtc = completedAtUtc,
        };

        var item = MemoryItem.Create(
            JsonSerializer.Serialize(state),
            source: "orkeon-key-rotation",
            tags: StateMarkerTags);

        return store.StoreAsync(KeyRotationDefaults.StateMarkerKey, item, ct);
    }

    /// <summary>Immutable per-run context shared by the rotation phases.</summary>
    private sealed record RotationContext(
        MemoryProviderBase Store,
        IEncryptionProvider OldProvider,
        IEncryptionProvider NewProvider,
        KeyRotationOptions Options,
        string Version,
        DateTime StartedAtUtc);

    /// <summary>Outcome of the staging phase.</summary>
    private sealed record StagingOutcome(
        IReadOnlyList<string> StagedKeys,
        int AlreadyRotated,
        int Unreadable,
        IReadOnlyList<string> Errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "Key rotation started: {EntryCount} candidate entries, target key version '{KeyVersion}'.")]
    private partial void LogRotationStarted(int entryCount, string keyVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Key rotation completed: {Rotated} entries re-encrypted, {AlreadyRotated} already on the new key, {Unreadable} unreadable, duration {Duration}.")]
    private partial void LogRotationCompleted(int rotated, int alreadyRotated, int unreadable, TimeSpan duration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Entry '{Key}' is not decryptable with the old key nor the new key.")]
    private partial void LogEntryUnreadable(string key);

    [LoggerMessage(Level = LogLevel.Error, Message = "Key rotation staging failed after {StagedCount} staged entries; rolling back staged copies.")]
    private partial void LogStagingFailedRollingBack(Exception ex, int stagedCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete the staged copy of entry '{Key}' during rollback.")]
    private partial void LogRollbackDeleteFailed(Exception ex, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Key rotation rolled back: {StagedCount} staged copies removed; the store remains on the old key.")]
    private partial void LogRotationRolledBack(int stagedCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Key rotation switch failed at entry '{Key}' after {Committed}/{Total} switched entries; re-run RotateAsync to resume.")]
    private partial void LogCommitFailed(Exception ex, string key, int committed, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to write the key rotation state marker.")]
    private partial void LogStateMarkerWriteFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete the stale staged copy '{Key}'.")]
    private partial void LogStaleStagingDeleteFailed(Exception ex, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "New encryption key generated and persisted under secret '{SecretName}'.")]
    private partial void LogNewKeyProvisioned(string secretName);
}
