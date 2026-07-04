#pragma warning disable CS0618 // Obsolete member usage

using EnvironmentSecretProviderSut = Orkeon.Infrastructure.Security.Secrets.EnvironmentSecretProvider;

namespace Orkeon.Infrastructure.Tests.Security.Secrets;

public sealed class EnvironmentSecretProviderTests : IDisposable
{
    private const string TestPrefix = "ORKEONTEST_";
    private readonly EnvironmentSecretProviderSut _sut;
    private readonly List<string> _envVarsToClean = [];

    public EnvironmentSecretProviderTests()
    {
        _sut = new EnvironmentSecretProviderSut(TestPrefix);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var key in _envVarsToClean)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    private void SetEnvVar(string name, string value)
    {
        var fullName = TestPrefix + name.ToUpperInvariant();
        Environment.SetEnvironmentVariable(fullName, value);
        _envVarsToClean.Add(fullName);
    }

    [Fact]
    public async Task ShouldReturnCorrectValue_WhenEnvironmentVariableExists()
    {
        // Arrange
        SetEnvVar("MY_API_KEY", "sk-test1234567890abcdef");

        // Act
        using var secret = await _sut.GetSecretAsync("MY_API_KEY", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("sk-test1234567890abcdef", secret.Value);
        Assert.Equal("Environment", secret.Source);
        Assert.False(secret.IsExpired);
    }

    [Fact]
    public async Task ShouldThrowKeyNotFoundException_WhenEnvironmentVariableMissing()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.GetSecretAsync("NONEXISTENT_SECRET", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldShowMaskedValue_WhenAccessingMaskedRepresentation()
    {
        // Arrange
        SetEnvVar("MASK_TEST", "abcdefghijklmnop");

        // Act
        using var secret = await _sut.GetSecretAsync("MASK_TEST", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("abc...nop", secret.MaskedValue);
    }

    [Fact]
    public async Task ShouldNeverRevealFullValue_WhenCallingToString()
    {
        // Arrange
        var fullValue = "my-super-secret-api-key-12345";
        SetEnvVar("TOSTRING_TEST", fullValue);

        // Act
        using var secret = await _sut.GetSecretAsync("TOSTRING_TEST", TestContext.Current.CancellationToken);

        // Assert
        var stringRepresentation = secret.ToString();
        Assert.DoesNotContain(fullValue, stringRepresentation);
        Assert.Equal(secret.MaskedValue, stringRepresentation);
    }

    [Fact]
    public async Task ShouldThrowObjectDisposedException_WhenAccessingValueAfterDispose()
    {
        // Arrange
        SetEnvVar("DISPOSE_TEST", "some-secret-value-here");
        var secret = await _sut.GetSecretAsync("DISPOSE_TEST", TestContext.Current.CancellationToken);

        // Act
        secret.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => secret.Value);
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenEnvironmentVariableExists()
    {
        // Arrange
        SetEnvVar("EXISTS_TEST", "value");

        // Act
        var exists = await _sut.ExistsAsync("EXISTS_TEST", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenEnvironmentVariableMissing()
    {
        var exists = await _sut.ExistsAsync("DOES_NOT_EXIST_AT_ALL", TestContext.Current.CancellationToken);
        Assert.False(exists);
    }

    [Fact]
    public async Task ShouldReturnNamesWithoutPrefix_WhenListingSecrets()
    {
        // Arrange
        SetEnvVar("LIST_KEY_A", "valueA");
        SetEnvVar("LIST_KEY_B", "valueB");

        // Act
        var names = await _sut.ListSecretNamesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("LIST_KEY_A", names);
        Assert.Contains("LIST_KEY_B", names);
    }

    [Fact]
    public async Task ShouldReturnStars_WhenValueIsShort()
    {
        // Arrange
        SetEnvVar("SHORT", "abc");

        // Act
        using var secret = await _sut.GetSecretAsync("SHORT", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("***", secret.MaskedValue);
    }

    [Fact]
    public async Task ShouldThrowOperationCanceledException_WhenCancellationRequested()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.GetSecretAsync("ANY", cts.Token));
    }
}
