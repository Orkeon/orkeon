using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for ISecretProvider with call tracking and configurable results.
/// </summary>
public class MockSecretProvider : ISecretProvider
{
    private readonly Dictionary<string, SecretValue> _secrets = new(StringComparer.OrdinalIgnoreCase);
    private Exception? _getSecretException;

    // --- Tracking ---
    public int GetSecretCallCount { get; private set; }
    public string? LastGetSecretName { get; private set; }

    public int ExistsCallCount { get; private set; }
    public string? LastExistsName { get; private set; }

    public int ListSecretNamesCallCount { get; private set; }

    // --- Configuration ---
    public void AddSecret(string name, string value, string source = "Test")
    {
        _secrets[name] = new SecretValue(value, source);
    }

    public void AddSecret(string name, SecretValue secretValue)
    {
        _secrets[name] = secretValue;
    }

    public void ClearSecrets() => _secrets.Clear();

    /// <summary>
    /// Configures the provider to throw the specified exception on GetSecretAsync.
    /// </summary>
    public void SetGetSecretException(Exception exception) => _getSecretException = exception;

    // --- ISecretProvider ---
    public Task<SecretValue> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        GetSecretCallCount++;
        LastGetSecretName = secretName;

        if (_getSecretException != null)
            throw _getSecretException;

        if (_secrets.TryGetValue(secretName, out var secret))
            return Task.FromResult(secret);

        throw new KeyNotFoundException($"Secret '{secretName}' not found.");
    }

    private bool? _existsOverride;

    /// <summary>
    /// Overrides the ExistsAsync result regardless of internal secrets. Set null to use default behavior.
    /// </summary>
    public void SetExistsResult(bool? result) => _existsOverride = result;

    public Task<bool> ExistsAsync(string secretName, CancellationToken ct = default)
    {
        ExistsCallCount++;
        LastExistsName = secretName;
        if (_existsOverride.HasValue)
            return Task.FromResult(_existsOverride.Value);
        return Task.FromResult(_secrets.ContainsKey(secretName));
    }

    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken ct = default)
    {
        ListSecretNamesCallCount++;
        IReadOnlyList<string> names = _secrets.Keys.ToList().AsReadOnly();
        return Task.FromResult(names);
    }
}
