using System.Security.Cryptography;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Security.Encryption;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.Security.Encryption;

/// <summary>
/// In-memory secret provider for testing.
/// </summary>
internal sealed class InMemorySecretProvider : ISecretProvider
{
    private readonly Dictionary<string, string> _secrets = [];

    public void SetSecret(string name, string value) => _secrets[name] = value;

    public Task<SecretValue> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        if (!_secrets.TryGetValue(secretName, out var value))
            throw new KeyNotFoundException($"Secret '{secretName}' not found.");
        return Task.FromResult(new SecretValue(value, "InMemory"));
    }

    public Task<bool> ExistsAsync(string secretName, CancellationToken ct = default)
        => Task.FromResult(_secrets.ContainsKey(secretName));

    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(_secrets.Keys.ToList());
}

public class AesEncryptionProviderTests
{
    private static readonly string TestKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesEncryptionProvider CreateProvider(string? keyBase64 = null, bool enabled = true)
    {
        var secrets = new InMemorySecretProvider();
        secrets.SetSecret("orkeon-encryption-key", keyBase64 ?? TestKeyBase64);

        var options = Options.Create(new AesEncryptionOptions
        {
            Enabled = enabled,
            SecretName = "orkeon-encryption-key",
            KeySizeInBits = 256
        });

        return new AesEncryptionProvider(
            secrets,
            NullLogger<AesEncryptionProvider>.Instance,
            options);
    }

    [Fact]
    public async Task EncryptDecrypt_RoundTrip_Bytes_Success()
    {
        var provider = CreateProvider();
        var original = System.Text.Encoding.UTF8.GetBytes("Hello, World!");

        var encrypted = await provider.EncryptAsync(original, TestContext.Current.CancellationToken);
        var decrypted = await provider.DecryptAsync(encrypted, TestContext.Current.CancellationToken);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public async Task EncryptDecrypt_RoundTrip_String_Success()
    {
        var provider = CreateProvider();
        var original = "Sensitive memory content with special chars: àéîöü 🔐";

        var encrypted = await provider.EncryptStringAsync(original, TestContext.Current.CancellationToken);
        var decrypted = await provider.DecryptStringAsync(encrypted, TestContext.Current.CancellationToken);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public async Task Encrypt_DifferentNonce_EachCall()
    {
        var provider = CreateProvider();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Same content");

        var encrypted1 = await provider.EncryptAsync(plaintext, TestContext.Current.CancellationToken);
        var encrypted2 = await provider.EncryptAsync(plaintext, TestContext.Current.CancellationToken);

        // Nonce is the first 12 bytes — they must differ
        var nonce1 = encrypted1.AsSpan(0, 12).ToArray();
        var nonce2 = encrypted2.AsSpan(0, 12).ToArray();

        Assert.NotEqual(nonce1, nonce2);
        // Full ciphertexts must also differ due to different nonces
        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public async Task Decrypt_CorruptedData_ThrowsException()
    {
        var provider = CreateProvider();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Original");
        var encrypted = await provider.EncryptAsync(plaintext, TestContext.Current.CancellationToken);

        // Corrupt a byte in the ciphertext area (after nonce + tag)
        encrypted[28] ^= 0xFF;

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => provider.DecryptAsync(encrypted, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Decrypt_WrongKey_Fails()
    {
        var key1 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var key2 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var provider1 = CreateProvider(key1);
        var provider2 = CreateProvider(key2);

        var plaintext = System.Text.Encoding.UTF8.GetBytes("Secret data");
        var encrypted = await provider1.EncryptAsync(plaintext, TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => provider2.DecryptAsync(encrypted, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EncryptString_ReturnsBase64()
    {
        var provider = CreateProvider();
        var encrypted = await provider.EncryptStringAsync(TestContent, TestContext.Current.CancellationToken);

        // Verify it's valid Base64
        var bytes = Convert.FromBase64String(encrypted);
        Assert.True(bytes.Length > 12 + 16); // nonce + tag + at least some ciphertext
    }

    [Fact]
    public void IsEnabled_RespectsConfiguration()
    {
        var enabledProvider = CreateProvider(enabled: true);
        Assert.True(enabledProvider.IsEnabled);

        var disabledProvider = CreateProvider(enabled: false);
        Assert.False(disabledProvider.IsEnabled);
    }

    [Fact]
    public async Task RotateKeyAsync_IsFailClosed_ThrowsNotSupported()
    {
        // R2.8 — in-place rotation would orphan every ciphertext produced with the current key.
        // The fake "new key generated" success path was removed; the real re-encryption path is
        // IKeyRotationService.RotateAsync / ProvisionNewKeyAsync.
        var provider = CreateProvider();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.RotateKeyAsync(TestContext.Current.CancellationToken));
        Assert.Contains("IKeyRotationService", ex.Message, StringComparison.Ordinal);
    }
}
