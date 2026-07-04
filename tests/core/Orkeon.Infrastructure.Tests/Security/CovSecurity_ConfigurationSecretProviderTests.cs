using Microsoft.Extensions.Configuration;
using Orkeon.Infrastructure.Security.Secrets;

namespace Orkeon.Infrastructure.Tests.CovSecurity;

/// <summary>
/// Coverage for <see cref="ConfigurationSecretProvider"/>.
/// </summary>
public sealed class CovSecurity_ConfigurationSecretProviderTests
{
    private static IConfiguration BuildConfig(params (string key, string value)[] entries)
    {
        var dict = entries.ToDictionary(e => e.key, e => (string?)e.value);
        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    [Fact]
    public void Ctor_ShouldThrow_WhenConfigurationNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ConfigurationSecretProvider(null!));
    }

    [Fact]
    public void Ctor_ShouldThrow_WhenSectionNameNull()
    {
        var config = BuildConfig();
        Assert.Throws<ArgumentNullException>(
            () => new ConfigurationSecretProvider(config, null!));
    }

    [Fact]
    public async Task GetSecretAsync_ShouldReturnValue_FromDefaultSection()
    {
        var config = BuildConfig(("Secrets:OpenAI", "sk-12345"));
        var sut = new ConfigurationSecretProvider(config);

        using var secret = await sut.GetSecretAsync("OpenAI", TestContext.Current.CancellationToken);

        Assert.Equal("sk-12345", secret.Value);
        Assert.Equal("Configuration", secret.Source);
    }

    [Fact]
    public async Task GetSecretAsync_ShouldReturnValue_FromCustomSection()
    {
        var config = BuildConfig(("Vault:Token", "abc"));
        var sut = new ConfigurationSecretProvider(config, "Vault");

        using var secret = await sut.GetSecretAsync("Token", TestContext.Current.CancellationToken);

        Assert.Equal("abc", secret.Value);
    }

    [Fact]
    public async Task GetSecretAsync_ShouldThrowKeyNotFound_WhenMissing()
    {
        var config = BuildConfig();
        var sut = new ConfigurationSecretProvider(config);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => sut.GetSecretAsync("Absent", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSecretAsync_ShouldThrow_WhenSecretNameNull()
    {
        var sut = new ConfigurationSecretProvider(BuildConfig());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.GetSecretAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSecretAsync_ShouldThrow_WhenCancelled()
    {
        var sut = new ConfigurationSecretProvider(BuildConfig(("Secrets:A", "1")));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.GetSecretAsync("A", cts.Token));
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnTrue_WhenPresent()
    {
        var config = BuildConfig(("Secrets:A", "1"));
        var sut = new ConfigurationSecretProvider(config);

        Assert.True(await sut.ExistsAsync("A", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnFalse_WhenAbsent()
    {
        var sut = new ConfigurationSecretProvider(BuildConfig());

        Assert.False(await sut.ExistsAsync("Missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsAsync_ShouldThrow_WhenSecretNameNull()
    {
        var sut = new ConfigurationSecretProvider(BuildConfig());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.ExistsAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsAsync_ShouldThrow_WhenCancelled()
    {
        var sut = new ConfigurationSecretProvider(BuildConfig());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.ExistsAsync("A", cts.Token));
    }

    [Fact]
    public async Task ListSecretNamesAsync_ShouldReturnChildKeys()
    {
        var config = BuildConfig(
            ("Secrets:Alpha", "1"),
            ("Secrets:Beta", "2"));
        var sut = new ConfigurationSecretProvider(config);

        var names = await sut.ListSecretNamesAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Alpha", names);
        Assert.Contains("Beta", names);
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public async Task ListSecretNamesAsync_ShouldReturnEmpty_WhenSectionMissing()
    {
        var sut = new ConfigurationSecretProvider(BuildConfig());

        var names = await sut.ListSecretNamesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(names);
    }

    [Fact]
    public async Task ListSecretNamesAsync_ShouldThrow_WhenCancelled()
    {
        var sut = new ConfigurationSecretProvider(BuildConfig());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.ListSecretNamesAsync(cts.Token));
    }
}
