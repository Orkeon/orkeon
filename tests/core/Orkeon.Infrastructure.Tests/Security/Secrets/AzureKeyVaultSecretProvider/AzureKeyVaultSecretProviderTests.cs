using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Tests.Doubles;
using AzureKeyVaultSecretProviderSut = Orkeon.Infrastructure.Security.Secrets.AzureKeyVaultSecretProvider;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Infrastructure.Tests.Security.Secrets;

public class AzureKeyVaultSecretProviderTests
{
    private readonly AzureKvTestLogger _mockLogger = new();

    [Fact]
    public async Task ShouldReturnSecretValue_WhenSecretExists()
    {
        // Arrange
        var mockClient = new MockSecretClient();
        var kvSecret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("test-secret"), "my-secret-value");
        mockClient.SetGetSecretResult("test-secret", kvSecret);

        var provider = new AzureKeyVaultSecretProviderSut(mockClient, _mockLogger);

        // Act
        var result = await provider.GetSecretAsync("test-secret", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("my-secret-value", result.Value);
        Assert.Equal("azure-key-vault", result.Source);
    }

    [Fact]
    public async Task ShouldThrowKeyNotFoundException_WhenSecretNotFound()
    {
        // Arrange
        var mockClient = new MockSecretClient();
        mockClient.SetGetSecretThrows("missing", new RequestFailedException(404, "Not found"));

        var provider = new AzureKeyVaultSecretProviderSut(mockClient, _mockLogger);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => provider.GetSecretAsync("missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldSetExpiresAt_WhenSecretHasExpiration()
    {
        // Arrange
        var mockClient = new MockSecretClient();
        var expiresOn = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var props = new SecretProperties("test-secret") { ExpiresOn = expiresOn };
        var kvSecret = SecretModelFactory.KeyVaultSecret(props, "value");
        mockClient.SetGetSecretResult("test-secret", kvSecret);

        var provider = new AzureKeyVaultSecretProviderSut(mockClient, _mockLogger);

        // Act
        var result = await provider.GetSecretAsync("test-secret", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.ExpiresAt);
        Assert.Equal(expiresOn.UtcDateTime, result.ExpiresAt);
    }

    [Fact]
    public async Task ShouldReturnCachedValue_WhenCalledWithinCacheTtl()
    {
        // Arrange
        var mockClient = new MockSecretClient();
        var kvSecret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("cached"), "cached-value");
        mockClient.SetGetSecretResult("cached", kvSecret);

        var provider = new AzureKeyVaultSecretProviderSut(
            mockClient, _mockLogger, TimeoutExtended);

        // Act
        var result1 = await provider.GetSecretAsync("cached", TestContext.Current.CancellationToken);
        var result2 = await provider.GetSecretAsync("cached", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(result1, result2);
        Assert.Equal(1, mockClient.GetSecretCallCount); // Only called once, second was cached
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenSecretExists()
    {
        // Arrange
        var mockClient = new MockSecretClient();
        var kvSecret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties("exists"), "value");
        mockClient.SetGetSecretResult("exists", kvSecret);

        var provider = new AzureKeyVaultSecretProviderSut(mockClient, _mockLogger);

        // Act
        var result = await provider.ExistsAsync("exists", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenSecretIsMissing()
    {
        // Arrange
        var mockClient = new MockSecretClient();
        mockClient.SetGetSecretThrows("missing", new RequestFailedException(404, "Not found"));

        var provider = new AzureKeyVaultSecretProviderSut(mockClient, _mockLogger);

        // Act
        var result = await provider.ExistsAsync("missing", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }
}

/// <summary>
/// Simple logger for AzureKeyVaultSecretProvider tests.
/// </summary>
internal sealed class AzureKvTestLogger : ILogger<AzureKeyVaultSecretProviderSut>
{
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    { }
    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
