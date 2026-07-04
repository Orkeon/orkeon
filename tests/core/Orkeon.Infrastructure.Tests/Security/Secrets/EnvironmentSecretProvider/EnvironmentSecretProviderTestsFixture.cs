#pragma warning disable CS0618 // Obsolete member usage

using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Security.Secrets;

namespace Orkeon.Infrastructure.Tests.Security.Secrets;

public sealed class EnvironmentSecretProviderTestsFixture : IDisposable
{
    private const string TestPrefix = "ORKEONTEST_";
    private readonly EnvironmentSecretProvider _sut;
    private readonly List<string> _envVarsToClean = [];

    public EnvironmentSecretProviderTestsFixture()
    {
        _sut = new EnvironmentSecretProvider(TestPrefix);
    }

    // --- Fluent configuration ---

    public EnvironmentSecretProviderTestsFixture WithEnvironmentVariable(string name, string value)
    {
        var fullName = TestPrefix + name.ToUpperInvariant();
        Environment.SetEnvironmentVariable(fullName, value);
        _envVarsToClean.Add(fullName);
        return this;
    }

    // --- Execution ---

    public async Task<SecretValue> GetSecretAsync(string key, CancellationToken ct = default)
        => await _sut.GetSecretAsync(key, ct);

    public async Task<bool> ExistsAsync(string key)
        => await _sut.ExistsAsync(key);

    public async Task<IReadOnlyList<string>> ListSecretNamesAsync()
        => await _sut.ListSecretNamesAsync();

    // --- Inspection ---

    public EnvironmentSecretProvider GetProvider() => _sut;

    // --- Cleanup ---

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var key in _envVarsToClean)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }
}
