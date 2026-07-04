using System.Security.Cryptography;
using System.Text;
using Orkeon.Application.Interfaces.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Security.Encryption;

/// <summary>
/// Configuration options for AES-256-GCM encryption.
/// </summary>
public sealed record AesEncryptionOptions
{
    /// <summary>The secret name used to retrieve the encryption key from <see cref="ISecretProvider"/>.</summary>
    public string SecretName { get; init; } = "orkeon-encryption-key";

    /// <summary>Whether encryption is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Key size in bits (128, 192, or 256).</summary>
    public int KeySizeInBits { get; init; } = 256;
}

/// <summary>
/// AES-256-GCM encryption provider. Thread-safe.
/// Binary format: [nonce (12 bytes)][tag (16 bytes)][ciphertext].
/// String format: Base64 encoding of the binary format.
/// </summary>
public sealed partial class AesEncryptionProvider : IEncryptionProvider
{
    private const int NonceSizeInBytes = 12; // AES-GCM standard nonce size
    private const int TagSizeInBytes = 16;   // AES-GCM standard tag size

    private readonly ISecretProvider _secretProvider;
    private readonly ILogger<AesEncryptionProvider> _logger;
    private readonly AesEncryptionOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="AesEncryptionProvider"/> class.
    /// </summary>
    /// <param name="secretProvider">The secret provider for encryption key management.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="options">The AES encryption configuration options.</param>
    public AesEncryptionProvider(
        ISecretProvider secretProvider,
        ILogger<AesEncryptionProvider> logger,
        IOptions<AesEncryptionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(secretProvider);
        _secretProvider = secretProvider;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _options = options?.Value ?? new AesEncryptionOptions();
    }

    /// <inheritdoc />
    public bool IsEnabled => _options.Enabled;

    /// <inheritdoc />
    public Task<byte[]> EncryptAsync(byte[] plaintext, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        return EncryptAsyncCore(plaintext, ct);
    }

    private async Task<byte[]> EncryptAsyncCore(byte[] plaintext, CancellationToken ct)
    {
        var key = await GetKeyAsync(ct).ConfigureAwait(false);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeInBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeInBytes];

        using var aes = new AesGcm(key, TagSizeInBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Format: [nonce][tag][ciphertext]
        var result = new byte[NonceSizeInBytes + TagSizeInBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSizeInBytes);
        Buffer.BlockCopy(tag, 0, result, NonceSizeInBytes, TagSizeInBytes);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSizeInBytes + TagSizeInBytes, ciphertext.Length);

        return result;
    }

    /// <inheritdoc />
    public Task<byte[]> DecryptAsync(byte[] ciphertext, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        if (ciphertext.Length < NonceSizeInBytes + TagSizeInBytes)
            throw new CryptographicException("Ciphertext is too short to contain nonce and tag.");

        return DecryptAsyncCore(ciphertext, ct);
    }

    private async Task<byte[]> DecryptAsyncCore(byte[] ciphertext, CancellationToken ct)
    {
        var key = await GetKeyAsync(ct).ConfigureAwait(false);

        var nonce = new byte[NonceSizeInBytes];
        var tag = new byte[TagSizeInBytes];
        var encryptedData = new byte[ciphertext.Length - NonceSizeInBytes - TagSizeInBytes];

        Buffer.BlockCopy(ciphertext, 0, nonce, 0, NonceSizeInBytes);
        Buffer.BlockCopy(ciphertext, NonceSizeInBytes, tag, 0, TagSizeInBytes);
        Buffer.BlockCopy(ciphertext, NonceSizeInBytes + TagSizeInBytes, encryptedData, 0, encryptedData.Length);

        var plaintext = new byte[encryptedData.Length];

        using var aes = new AesGcm(key, TagSizeInBytes);
        aes.Decrypt(nonce, encryptedData, tag, plaintext);

        return plaintext;
    }

    /// <inheritdoc />
    public Task<string> EncryptStringAsync(string plaintext, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        return EncryptStringAsyncCore(plaintext, ct);
    }

    private async Task<string> EncryptStringAsyncCore(string plaintext, CancellationToken ct)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = await EncryptAsync(plaintextBytes, ct).ConfigureAwait(false);
        return Convert.ToBase64String(encrypted);
    }

    /// <inheritdoc />
    public Task<string> DecryptStringAsync(string ciphertext, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        return DecryptStringAsyncCore(ciphertext, ct);
    }

    private async Task<string> DecryptStringAsyncCore(string ciphertext, CancellationToken ct)
    {
        var ciphertextBytes = Convert.FromBase64String(ciphertext);
        var decrypted = await DecryptAsync(ciphertextBytes, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(decrypted);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Fail-closed (R2.8): in-place rotation through this provider is not supported. Swapping the
    /// key behind the configured secret name would orphan every ciphertext produced with the
    /// current key, and this provider has no access to the stores holding that data. Use
    /// <c>IKeyRotationService.ProvisionNewKeyAsync</c> to generate and persist the new key under a
    /// fresh secret name, then <c>IKeyRotationService.RotateAsync(store, oldProvider, newProvider)</c>
    /// to actually re-encrypt the stored data. No success is logged here.
    /// </remarks>
    public Task RotateKeyAsync(CancellationToken ct = default)
    {
        LogInPlaceKeyRotationRejected(_options.SecretName);
        return Task.FromException(new NotSupportedException(
            "In-place key rotation is not supported by AesEncryptionProvider: replacing the key behind " +
            $"secret '{_options.SecretName}' would orphan all data encrypted with the current key. " +
            "Use IKeyRotationService.ProvisionNewKeyAsync to persist a new key under a fresh secret name, " +
            "then IKeyRotationService.RotateAsync(store, oldProvider, newProvider) to re-encrypt stored data."));
    }

    private async Task<byte[]> GetKeyAsync(CancellationToken ct)
    {
        var secret = await _secretProvider.GetSecretAsync(_options.SecretName, ct).ConfigureAwait(false);
        var keyBytes = Convert.FromBase64String(secret.Value);

        var expectedKeySize = _options.KeySizeInBits / 8;
        if (keyBytes.Length != expectedKeySize)
            throw new CryptographicException(
                $"Encryption key size mismatch. Expected {expectedKeySize} bytes but got {keyBytes.Length} bytes.");

        return keyBytes;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "In-place key rotation rejected (fail-closed) for secret '{SecretName}': no data was re-encrypted. Use IKeyRotationService.RotateAsync instead.")]
    private partial void LogInPlaceKeyRotationRejected(string secretName);
}
