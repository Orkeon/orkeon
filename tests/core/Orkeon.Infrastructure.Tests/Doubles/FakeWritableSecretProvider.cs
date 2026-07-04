using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Security.Secrets;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IWritableSecretProvider"/> double backed by a dictionary.
/// Used by key rotation/provisioning tests (R2.8).
/// </summary>
public sealed class FakeWritableSecretProvider : IWritableSecretProvider
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    // --- Tracking ---
    public int StoreSecretCallCount { get; private set; }
    public string? LastStoredSecretName { get; private set; }

    /// <summary>Seeds a secret without going through <see cref="StoreSecretAsync"/>.</summary>
    public void SetSecret(string name, string value) => _secrets[name] = value;

    /// <summary>Direct access to the stored secrets for assertions.</summary>
    public IReadOnlyDictionary<string, string> Secrets => _secrets;

    public Task<SecretValue> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        if (!_secrets.TryGetValue(secretName, out var value))
            throw new KeyNotFoundException($"Secret '{secretName}' not found.");

        return Task.FromResult(new SecretValue(value, "FakeWritable"));
    }

    public Task<bool> ExistsAsync(string secretName, CancellationToken ct = default)
        => Task.FromResult(_secrets.ContainsKey(secretName));

    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(_secrets.Keys.ToList());

    public Task StoreSecretAsync(string secretName, string value, CancellationToken ct = default)
    {
        StoreSecretCallCount++;
        LastStoredSecretName = secretName;
        _secrets[secretName] = value;
        return Task.CompletedTask;
    }
}
