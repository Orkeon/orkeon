using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.Security.Encryption;

/// <summary>
/// Well-known constants used by the key rotation traversal (R2.8).
/// </summary>
public static class KeyRotationDefaults
{
    /// <summary>Suffix appended to an entry key to form its staging copy key.</summary>
    public const string StagingSuffix = "::rotation-staging";

    /// <summary>Reserved store key under which the rotation state marker is persisted.</summary>
    public const string StateMarkerKey = "__orkeon.key-rotation.state";

    /// <summary>Custom metadata property carrying the key version an entry is encrypted with.</summary>
    public const string KeyVersionProperty = "orkeon-key-version";

    /// <summary>Default page size used when enumerating store keys.</summary>
    public const int DefaultBatchSize = 100;

    /// <summary>Phase value: staged copies are being written (originals untouched).</summary>
    public const string PhaseStaging = "staging";

    /// <summary>Phase value: staged copies are being switched over the originals.</summary>
    public const string PhaseCommitting = "committing";

    /// <summary>Phase value: rotation completed — every entry is on the new key.</summary>
    public const string PhaseCompleted = "completed";

    /// <summary>Phase value: staging failed and staged copies were removed — store is on the old key.</summary>
    public const string PhaseRolledBack = "rolled_back";
}

/// <summary>
/// Options controlling a key rotation run.
/// </summary>
public sealed record KeyRotationOptions
{
    /// <summary>Page size used when enumerating store keys. Must be at least 1.</summary>
    public int BatchSize { get; init; } = KeyRotationDefaults.DefaultBatchSize;

    /// <summary>
    /// Stable label identifying the new key version, persisted in the state marker and on each
    /// re-encrypted entry (<see cref="KeyRotationDefaults.KeyVersionProperty"/>).
    /// When <see langword="null"/>, a timestamp-based label is generated for the run.
    /// Idempotent resume does NOT depend on this label: entries are classified by
    /// authenticated decryption (AES-GCM), not by metadata.
    /// </summary>
    public string? NewKeyVersion { get; init; }

    /// <summary>
    /// When <see langword="false"/> (default, fail-closed), an entry that cannot be decrypted
    /// with the old key nor the new key aborts the rotation before any switch and rolls back
    /// the staged copies. When <see langword="true"/>, such entries are left untouched and
    /// reported in <see cref="KeyRotationResult.Errors"/>.
    /// </summary>
    public bool ContinueOnUnreadableEntries { get; init; }
}

/// <summary>
/// Outcome of a completed key rotation run.
/// </summary>
public sealed record KeyRotationResult
{
    /// <summary>Number of candidate entries found in the store (staging copies and the state marker excluded).</summary>
    public int TotalEntries { get; init; }

    /// <summary>Number of entries actually re-encrypted from the old key to the new key during this run.</summary>
    public int RotatedEntries { get; init; }

    /// <summary>Number of entries already encrypted with the new key (skipped — typical of an idempotent resume).</summary>
    public int AlreadyRotatedEntries { get; init; }

    /// <summary>Number of entries decryptable with neither key, left untouched (only when allowed by options).</summary>
    public int UnreadableEntries { get; init; }

    /// <summary>The key version label persisted by this run.</summary>
    public required string NewKeyVersion { get; init; }

    /// <summary>Total duration of the rotation run.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Per-entry error messages (unreadable entries when skipping is enabled).</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Rotation state marker persisted in the store under
/// <see cref="KeyRotationDefaults.StateMarkerKey"/> (plaintext JSON, readable regardless of key).
/// </summary>
public sealed record KeyRotationState
{
    /// <summary>The rotation phase (one of the <c>Phase*</c> constants of <see cref="KeyRotationDefaults"/>).</summary>
    [JsonPropertyName("phase")]
    public required string Phase { get; init; }

    /// <summary>The key version label targeted by the rotation run.</summary>
    [JsonPropertyName("key_version")]
    public required string KeyVersion { get; init; }

    /// <summary>UTC timestamp at which the rotation run started.</summary>
    [JsonPropertyName("started_at_utc")]
    public DateTime StartedAtUtc { get; init; }

    /// <summary>UTC timestamp at which the run reached a terminal phase, when applicable.</summary>
    [JsonPropertyName("completed_at_utc")]
    public DateTime? CompletedAtUtc { get; init; }
}

/// <summary>
/// Raised when a key rotation run cannot complete. The message states whether the store
/// was rolled back to the old key (staging failure) or requires a re-run to resume the
/// switch (roll-forward). No success is ever logged when this exception is thrown.
/// </summary>
public sealed class KeyRotationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="KeyRotationException"/> class.</summary>
    public KeyRotationException()
    {
    }

    /// <summary>Initializes a new instance with a message describing the failure and the recovery path.</summary>
    /// <param name="message">The failure message.</param>
    public KeyRotationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and the underlying cause.</summary>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public KeyRotationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Store keys directly involved in the failure, when known.</summary>
    public IReadOnlyList<string> AffectedKeys { get; init; } = Array.Empty<string>();
}
