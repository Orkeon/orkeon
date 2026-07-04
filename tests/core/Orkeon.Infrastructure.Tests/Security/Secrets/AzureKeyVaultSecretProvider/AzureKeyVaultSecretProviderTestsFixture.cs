using Azure;
using Azure.Security.KeyVault.Secrets;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Security.Secrets;

namespace Orkeon.Infrastructure.Tests.Security.Secrets;

public class AzureKeyVaultSecretProviderTestsFixture
{
    private readonly AzureKvTestLogger _mockLogger = new();
    private readonly MockSecretClient _mockClient = new();
    private TimeSpan? _cacheTtl;

    // --- Fluent configuration ---

    public AzureKeyVaultSecretProviderTestsFixture WithSecret(string name, string value, DateTimeOffset? expiresOn = null)
    {
        var props = new SecretProperties(name);
        if (expiresOn.HasValue)
            props.ExpiresOn = expiresOn;
        var kvSecret = SecretModelFactory.KeyVaultSecret(props, value);
        _mockClient.SetGetSecretResult(name, kvSecret);
        return this;
    }

    public AzureKeyVaultSecretProviderTestsFixture WithSecretThrows(string name, RequestFailedException exception)
    {
        _mockClient.SetGetSecretThrows(name, exception);
        return this;
    }

    public AzureKeyVaultSecretProviderTestsFixture WithCacheTtl(TimeSpan ttl)
    {
        _cacheTtl = ttl;
        return this;
    }

    // --- Build ---

    public AzureKeyVaultSecretProvider Build()
    {
        if (_cacheTtl.HasValue)
            return new AzureKeyVaultSecretProvider(_mockClient, _mockLogger, _cacheTtl.Value);
        return new AzureKeyVaultSecretProvider(_mockClient, _mockLogger);
    }

    // --- Execution ---

    public async Task<SecretValue> GetSecretAsync(string key)
        => await Build().GetSecretAsync(key);

    public async Task<bool> ExistsAsync(string key)
        => await Build().ExistsAsync(key);

    // --- Inspection ---

    public MockSecretClient GetMockClient() => _mockClient;
}
