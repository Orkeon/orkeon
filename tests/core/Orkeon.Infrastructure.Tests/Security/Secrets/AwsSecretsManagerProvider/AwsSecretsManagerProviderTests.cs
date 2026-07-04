using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Tests.Doubles;
using AwsSecretsManagerProviderSut = Orkeon.Infrastructure.Security.Secrets.AwsSecretsManagerProvider;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Infrastructure.Tests.Security.Secrets;

public sealed class AwsSecretsManagerProviderTests : IDisposable
{
    private readonly MockAmazonSecretsManager _mockClient = new();
    private readonly AwsTestLogger _mockLogger = new();

    [Fact]
    public async Task ShouldReturnSecretValue_WhenSecretExists()
    {
        // Arrange
        _mockClient.SetGetSecretValueFunc((req, ct) =>
        {
            if (req.SecretId == "test-secret")
                return Task.FromResult(new GetSecretValueResponse { SecretString = "aws-secret-value" });
            throw new ResourceNotFoundException("not found");
        });

        var provider = new AwsSecretsManagerProviderSut(_mockClient, _mockLogger);

        // Act
        var result = await provider.GetSecretAsync("test-secret", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("aws-secret-value", result.Value);
        Assert.Equal("aws-secrets-manager", result.Source);
    }

    [Fact]
    public async Task ShouldThrowKeyNotFoundException_WhenSecretNotFound()
    {
        // Arrange
        _mockClient.SetGetSecretValueFunc((req, ct) =>
        {
            if (req.SecretId == "missing")
                throw new ResourceNotFoundException("not found");
            throw new ResourceNotFoundException("not found");
        });

        var provider = new AwsSecretsManagerProviderSut(_mockClient, _mockLogger);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => provider.GetSecretAsync("missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnCachedValue_WhenCalledWithinCacheTtl()
    {
        // Arrange
        _mockClient.SetGetSecretValueFunc((req, ct) =>
        {
            if (req.SecretId == "cached")
                return Task.FromResult(new GetSecretValueResponse { SecretString = "cached-value" });
            throw new ResourceNotFoundException("not found");
        });

        var provider = new AwsSecretsManagerProviderSut(
            _mockClient, _mockLogger, TimeoutExtended);

        // Act
        var result1 = await provider.GetSecretAsync("cached", TestContext.Current.CancellationToken);
        var result2 = await provider.GetSecretAsync("cached", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(result1, result2);
        Assert.Equal(1, _mockClient.GetSecretValueCallCount); // Only called once, second was cached
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenSecretExists()
    {
        // Arrange
        _mockClient.SetDescribeSecretFunc((req, ct) =>
        {
            if (req.SecretId == "exists")
                return Task.FromResult(new DescribeSecretResponse());
            throw new ResourceNotFoundException("not found");
        });

        var provider = new AwsSecretsManagerProviderSut(_mockClient, _mockLogger);

        // Act
        var result = await provider.ExistsAsync("exists", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenSecretIsMissing()
    {
        // Arrange
        _mockClient.SetDescribeSecretFunc((req, ct) =>
        {
            if (req.SecretId == "missing")
                throw new ResourceNotFoundException("not found");
            throw new ResourceNotFoundException("not found");
        });

        var provider = new AwsSecretsManagerProviderSut(_mockClient, _mockLogger);

        // Act
        var result = await provider.ExistsAsync("missing", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldReturnSecretNames_WhenListingSecrets()
    {
        // Arrange
        _mockClient.SetListSecretsResult(new ListSecretsResponse
        {
            SecretList =
            [
                new() { Name = "secret1" },
                new() { Name = "secret2" }
            ]
        });

        var provider = new AwsSecretsManagerProviderSut(_mockClient, _mockLogger);

        // Act
        var result = await provider.ListSecretNamesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("secret1", result);
        Assert.Contains("secret2", result);
    }

    public void Dispose()
    {
        _mockClient.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Simple logger for AwsSecretsManagerProvider tests.
/// </summary>
internal sealed class AwsTestLogger : ILogger<AwsSecretsManagerProviderSut>
{
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    { }
    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
