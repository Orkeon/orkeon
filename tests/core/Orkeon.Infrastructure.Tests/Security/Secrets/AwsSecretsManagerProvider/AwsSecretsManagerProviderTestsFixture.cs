using Amazon.SecretsManager.Model;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Security.Secrets;

namespace Orkeon.Infrastructure.Tests.Security.Secrets;

public sealed class AwsSecretsManagerProviderTestsFixture : IDisposable
{
    private readonly MockAmazonSecretsManager _mockClient = new();
    private readonly AwsTestLogger _mockLogger = new();
    private TimeSpan? _cacheTtl;

    // --- Fluent configuration ---

    public AwsSecretsManagerProviderTestsFixture WithGetSecretValueFunc(
        Func<GetSecretValueRequest, CancellationToken, Task<GetSecretValueResponse>> func)
    {
        _mockClient.SetGetSecretValueFunc(func);
        return this;
    }

    public AwsSecretsManagerProviderTestsFixture WithDescribeSecretFunc(
        Func<DescribeSecretRequest, CancellationToken, Task<DescribeSecretResponse>> func)
    {
        _mockClient.SetDescribeSecretFunc(func);
        return this;
    }

    public AwsSecretsManagerProviderTestsFixture WithListSecretsResult(ListSecretsResponse result)
    {
        _mockClient.SetListSecretsResult(result);
        return this;
    }

    public AwsSecretsManagerProviderTestsFixture WithCacheTtl(TimeSpan ttl)
    {
        _cacheTtl = ttl;
        return this;
    }

    // --- Build ---

    public AwsSecretsManagerProvider Build()
    {
        if (_cacheTtl.HasValue)
            return new AwsSecretsManagerProvider(_mockClient, _mockLogger, _cacheTtl.Value);
        return new AwsSecretsManagerProvider(_mockClient, _mockLogger);
    }

    // --- Execution ---

    public async Task<SecretValue> GetSecretAsync(string key)
        => await Build().GetSecretAsync(key);

    public async Task<bool> ExistsAsync(string key)
        => await Build().ExistsAsync(key);

    public async Task<IReadOnlyList<string>> ListSecretNamesAsync()
        => await Build().ListSecretNamesAsync();

    // --- Inspection ---

    public MockAmazonSecretsManager GetMockClient() => _mockClient;

    public void Dispose()
    {
        _mockClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
