using Orkeon.Application.Interfaces.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Tests.Doubles;
using ChainedSecretProviderSut = Orkeon.Infrastructure.Security.Secrets.ChainedSecretProvider;

namespace Orkeon.Infrastructure.Tests.Security.Secrets;

public class ChainedSecretProviderTests
{
    private readonly ILogger<ChainedSecretProviderSut> _logger = NullLogger<ChainedSecretProviderSut>.Instance;

    private ChainedSecretProviderSut CreateSut(params ISecretProvider[] providers)
    {
        return new ChainedSecretProviderSut(providers, _logger);
    }

    [Fact]
    public async Task ShouldReturnFromFirstProvider_WhenFirstProviderHasSecret()
    {
        // Arrange
        var provider1 = new MockSecretProvider();
        var provider2 = new MockSecretProvider();
        var expected = new SecretValue("first-value", "Provider1");
        provider1.AddSecret("key", expected);

        var sut = CreateSut(provider1, provider2);

        // Act
        var result = await sut.GetSecretAsync("key", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(expected, result);
        Assert.Equal(0, provider2.GetSecretCallCount);
    }

    [Fact]
    public async Task ShouldFallBackToSecondProvider_WhenFirstProviderFails()
    {
        // Arrange
        var provider1 = new MockSecretProvider();
        // provider1 has no "key" secret, so it will throw KeyNotFoundException

        var provider2 = new MockSecretProvider();
        var expected = new SecretValue("second-value", "Provider2");
        provider2.AddSecret("key", expected);

        var sut = CreateSut(provider1, provider2);

        // Act
        var result = await sut.GetSecretAsync("key", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ShouldThrowKeyNotFoundException_WhenNoProviderHasSecret()
    {
        // Arrange
        var provider1 = new MockSecretProvider();
        var provider2 = new MockSecretProvider();
        // Neither has the "key" secret

        var sut = CreateSut(provider1, provider2);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.GetSecretAsync("key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldContinueToNext_WhenFirstProviderThrowsGenericException()
    {
        // Arrange
        var provider1 = new MockSecretProvider();
        provider1.SetGetSecretException(new InvalidOperationException("something broke"));

        var provider2 = new MockSecretProvider();
        var expected = new SecretValue("fallback-value", "Provider2");
        provider2.AddSecret("key", expected);

        var sut = CreateSut(provider1, provider2);

        // Act
        var result = await sut.GetSecretAsync("key", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ShouldNotCatchOperationCanceledException_WhenProviderThrows()
    {
        // Arrange
        var provider1 = new MockSecretProvider();
        provider1.SetGetSecretException(new OperationCanceledException());

        var sut = CreateSut(provider1);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.GetSecretAsync("key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenSecondProviderHasSecret()
    {
        // Arrange
        var provider1 = new MockSecretProvider();
        // provider1 has no secrets

        var provider2 = new MockSecretProvider();
        provider2.AddSecret("key", "value");

        var sut = CreateSut(provider1, provider2);

        // Act
        var result = await sut.ExistsAsync("key", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ShouldAggregateNames_WhenListingFromAllProviders()
    {
        // Arrange
        var provider1 = new MockSecretProvider();
        provider1.AddSecret("KEY_A", "a");
        provider1.AddSecret("KEY_B", "b");

        var provider2 = new MockSecretProvider();
        provider2.AddSecret("KEY_B", "b2");
        provider2.AddSecret("KEY_C", "c");

        var sut = CreateSut(provider1, provider2);

        // Act
        var result = await sut.ListSecretNamesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Count); // KEY_A, KEY_B, KEY_C (deduplicated)
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenProviderListIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => CreateSut());
    }
}
