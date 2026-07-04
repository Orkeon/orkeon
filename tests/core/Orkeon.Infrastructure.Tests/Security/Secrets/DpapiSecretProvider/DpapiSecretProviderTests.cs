using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Tests.Shared.FileSystem;
using DpapiSut = Orkeon.Infrastructure.Security.Secrets.DpapiSecretProvider;

namespace Orkeon.Infrastructure.Tests.Security.Secrets;

public class DpapiSecretProviderTests
{
    private const string VDir = "/secrets";

    private static DpapiSut Build(FakeFileSystemService fs)
        => new(fs, VDir, NullLogger<DpapiSut>.Instance);

    [Fact]
    public void Ctor_ShouldThrowPlatformNotSupportedException_OnNonWindows()
    {
        if (OperatingSystem.IsWindows()) return; // test is only meaningful on non-Windows

        Assert.Throws<PlatformNotSupportedException>(
            () => new DpapiSut(new FakeFileSystemService(), VDir, NullLogger<DpapiSut>.Instance));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task GetSecretAsync_ShouldReturnNull_WhenSecretDoesNotExist()
    {
        if (!OperatingSystem.IsWindows()) return;

        var fs = new FakeFileSystemService();
        using var provider = Build(fs);
        await provider.InitializeAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => provider.GetSecretAsync("missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnFalse_WhenSecretDoesNotExist()
    {
        if (!OperatingSystem.IsWindows()) return;

        var fs = new FakeFileSystemService();
        using var provider = Build(fs);
        await provider.InitializeAsync(TestContext.Current.CancellationToken);

        var exists = await provider.ExistsAsync("nonexistent", TestContext.Current.CancellationToken);
        Assert.False(exists);
    }

    [Fact]
    public async Task ListSecretNamesAsync_ShouldReturnEmpty_WhenNoSecrets()
    {
        if (!OperatingSystem.IsWindows()) return;

        var fs = new FakeFileSystemService();
        using var provider = Build(fs);
        await provider.InitializeAsync(TestContext.Current.CancellationToken);

        var names = await provider.ListSecretNamesAsync(TestContext.Current.CancellationToken);
        Assert.Empty(names);
    }

    [Fact]
    public async Task StoreAndGet_ShouldRoundtrip()
    {
        if (!OperatingSystem.IsWindows()) return;

        var fs = new FakeFileSystemService();
        using var provider = Build(fs);
        await provider.InitializeAsync(TestContext.Current.CancellationToken);

        await provider.StoreSecretAsync("mykey", "myvalue", TestContext.Current.CancellationToken);

        using var secret = await provider.GetSecretAsync("mykey", TestContext.Current.CancellationToken);
        Assert.Equal("myvalue", secret.Value);
        Assert.Equal("dpapi", secret.Source);
    }

    [Fact]
    public async Task ListSecretNamesAsync_ShouldReturnStoredKeys()
    {
        if (!OperatingSystem.IsWindows()) return;

        var fs = new FakeFileSystemService();
        using var provider = Build(fs);
        await provider.InitializeAsync(TestContext.Current.CancellationToken);

        await provider.StoreSecretAsync("alpha", "val1", TestContext.Current.CancellationToken);
        await provider.StoreSecretAsync("beta", "val2", TestContext.Current.CancellationToken);

        var names = await provider.ListSecretNamesAsync(TestContext.Current.CancellationToken);
        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
    }
}
