namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Provides secure access to secrets (API keys, tokens, etc.)
/// without embedding them in configuration objects.
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Retrieves a secret by name.
    /// </summary>
    /// <param name="secretName">The logical name of the secret.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A disposable <see cref="SecretValue"/> containing the secret.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the secret is not found.</exception>
    System.Threading.Tasks.Task<SecretValue> GetSecretAsync(string secretName, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a secret exists.
    /// </summary>
    System.Threading.Tasks.Task<bool> ExistsAsync(string secretName, CancellationToken ct = default);

    /// <summary>
    /// Lists all available secret names.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken ct = default);
}

/// <summary>
/// Wraps a secret value with metadata and safe disposal.
/// The value is cleared on disposal to reduce the window of exposure in memory.
/// </summary>
public sealed class SecretValue : IDisposable
{
    private string? _value;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="SecretValue"/>.
    /// </summary>
    public SecretValue(string value, string source, DateTime? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        ExpiresAt = expiresAt;
        RetrievedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// The secret value. Throws <see cref="ObjectDisposedException"/> after disposal.
    /// </summary>
    public string Value => _disposed
        ? throw new ObjectDisposedException(nameof(SecretValue))
        : _value ?? throw new InvalidOperationException("Secret value is null");

    /// <summary>
    /// Indicates where the secret was resolved from (e.g. "Environment", "Configuration").
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Optional expiration time for the secret.
    /// </summary>
    public DateTime? ExpiresAt { get; }

    /// <summary>
    /// UTC timestamp of when the secret was retrieved.
    /// </summary>
    public DateTime RetrievedAt { get; }

    /// <summary>
    /// Whether the secret has expired.
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt;

    /// <summary>
    /// Returns a masked representation of the value suitable for logging.
    /// Shows the first 3 and last 3 characters only.
    /// </summary>
    public string MaskedValue
    {
        get
        {
            if (_disposed || _value is null || _value.Length < 8) return "***";
            return _value[..3] + "..." + _value[^3..];
        }
    }

    /// <summary>
    /// Returns the masked value to prevent accidental logging of secrets.
    /// </summary>
    public override string ToString() => MaskedValue;

    /// <summary>
    /// Dispose.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _disposed = true;
        _value = null;
    }
}
