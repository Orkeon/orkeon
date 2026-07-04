
namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Result of an encryption operation containing ciphertext, nonce, and authentication tag.
/// </summary>
public record EncryptionResult(IReadOnlyList<byte> CipherText, IReadOnlyList<byte> Nonce, IReadOnlyList<byte> Tag);

/// <summary>
/// Provides encryption and decryption capabilities for data at rest.
/// </summary>
public interface IEncryptionProvider
{
    /// <summary>
    /// Encrypts raw bytes. The returned byte array includes nonce, tag, and ciphertext.
    /// </summary>
    System.Threading.Tasks.Task<byte[]> EncryptAsync(byte[] plaintext, CancellationToken ct = default);

    /// <summary>
    /// Decrypts raw bytes previously encrypted by <see cref="EncryptAsync"/>.
    /// </summary>
    System.Threading.Tasks.Task<byte[]> DecryptAsync(byte[] ciphertext, CancellationToken ct = default);

    /// <summary>
    /// Encrypts a string and returns a Base64-encoded ciphertext.
    /// </summary>
    System.Threading.Tasks.Task<string> EncryptStringAsync(string plaintext, CancellationToken ct = default);

    /// <summary>
    /// Decrypts a Base64-encoded ciphertext string.
    /// </summary>
    System.Threading.Tasks.Task<string> DecryptStringAsync(string ciphertext, CancellationToken ct = default);

    /// <summary>
    /// Rotates the encryption key, generating a new one and storing it via the secret provider.
    /// </summary>
    System.Threading.Tasks.Task RotateKeyAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether encryption is enabled.
    /// </summary>
    bool IsEnabled { get; }
}
