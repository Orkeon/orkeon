using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Tools.Web.Tests.Doubles;

/// <summary>
/// Simple mock ISecretProvider for unit tests.
/// Returns pre-configured secrets by name.
/// </summary>
public sealed class MockSecretProvider : ISecretProvider
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public void AddSecret(string name, string value) => _secrets[name] = value;

    public Task<SecretValue> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        if (_secrets.TryGetValue(secretName, out var value))
            return Task.FromResult(new SecretValue(value, "Test"));

        throw new KeyNotFoundException($"Secret '{secretName}' not found.");
    }

    public Task<bool> ExistsAsync(string secretName, CancellationToken ct = default)
        => Task.FromResult(_secrets.ContainsKey(secretName));

    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(_secrets.Keys.ToList().AsReadOnly());
}
