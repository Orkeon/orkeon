using Orkeon.Tools.Web.Firefox;

namespace Orkeon.Tools.Web.Tests.Firefox;

public sealed class FirefoxProfileTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("orkeon-firefox-profile-tests-");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void Should_PlaceProfilesUnderTheSnapFolder_When_FirefoxIsASnap()
    {
        var snap = Path.Combine("/home/ada", "snap/firefox/common");

        var root = FirefoxProfile.DefaultRoot("/home/ada", "/tmp", directory => directory == snap);

        Assert.Equal(Path.Combine(snap, "orkeon", "profiles"), root);
    }

    [Theory]
    [InlineData("/home/ada")]
    [InlineData("")]
    [InlineData(null)]
    public void Should_PlaceProfilesUnderTheTempFolder_When_FirefoxIsNotASnap(string? home)
    {
        var root = FirefoxProfile.DefaultRoot(home, "/tmp", _ => false);

        Assert.Equal(Path.Combine("/tmp", "orkeon-firefox"), root);
    }

    [Fact]
    public void Should_ResolveTheConfiguredRoot_When_OneIsGiven()
    {
        Assert.Equal(_root.FullName, FirefoxProfile.ResolveRoot(_root.FullName));
    }

    [Fact]
    public async Task Should_CreateAFolderOfItsOwn_WithTheRemotePreferences_AndRemoveIt()
    {
        var first = FirefoxProfile.Create(_root.FullName);
        var second = FirefoxProfile.Create(_root.FullName);

        Assert.NotEqual(first.Directory, second.Directory);
        Assert.Equal(_root.FullName, Path.GetDirectoryName(first.Directory));
        var preferences = await File.ReadAllTextAsync(Path.Combine(first.Directory, "user.js"), TestContext.Current.CancellationToken);
        Assert.Contains("""user_pref("remote.active-protocols", 1);""", preferences, StringComparison.Ordinal);
        Assert.Contains("""user_pref("signon.rememberSignons", false);""", preferences, StringComparison.Ordinal);

        await first.RemoveAsync();
        await first.RemoveAsync();

        Assert.False(Directory.Exists(first.Directory));
        Assert.True(Directory.Exists(second.Directory));
    }

    [Fact]
    public void Should_FailWithAnExpectedError_When_TheFolderCannotBeCreated()
    {
        var file = Path.Combine(_root.FullName, "not-a-folder");
        File.WriteAllText(file, string.Empty);

        var error = Assert.Throws<FirefoxException>(() => FirefoxProfile.Create(file));

        Assert.Contains("profile folder", error.Message, StringComparison.Ordinal);
    }
}
