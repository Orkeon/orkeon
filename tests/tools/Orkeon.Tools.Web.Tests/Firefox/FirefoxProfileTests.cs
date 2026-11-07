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

    [Fact]
    public void Should_LookForProfilesIni_InTheSnapFolderFirst_ThenThePlatformFolder()
    {
        Assert.Equal(
            ["/home/u/snap/firefox/common/.mozilla/firefox/profiles.ini", "/home/u/.mozilla/firefox/profiles.ini"],
            FirefoxProfile.ProfilesIniCandidates("/home/u", null, isWindows: false, isMacOS: false));
        Assert.Equal(
            ["/Users/u/Library/Application Support/Firefox/profiles.ini"],
            FirefoxProfile.ProfilesIniCandidates("/Users/u", null, isWindows: false, isMacOS: true));
        Assert.Equal(
            [Path.Combine("C:\\Users\\u\\AppData\\Roaming", "Mozilla", "Firefox", "profiles.ini")],
            FirefoxProfile.ProfilesIniCandidates("C:\\Users\\u", "C:\\Users\\u\\AppData\\Roaming", isWindows: true, isMacOS: false));
        Assert.Empty(FirefoxProfile.ProfilesIniCandidates(null, null, isWindows: false, isMacOS: false));
    }

    [Fact]
    public void Should_FindANamedProfile_InProfilesIni_RelativeOrAbsolute()
    {
        const string ini = """
            [Install4F96D1932A9F858E]
            Default=abcd1234.default-release
            Locked=1

            [Profile1]
            Name=default
            IsRelative=1
            Path=abcd1234.default

            [Profile0]
            Name=Work
            IsRelative=0
            Path=/srv/firefox/work
            Default=1

            [General]
            StartWithLastProfile=1
            Version=2
            """;
        var folder = Path.Combine(_root.FullName, "firefox");

        Assert.Equal(Path.Combine(folder, "abcd1234.default"), FirefoxProfile.FindInProfilesIni(ini, folder, "default"));
        Assert.Equal(Path.GetFullPath("/srv/firefox/work"), FirefoxProfile.FindInProfilesIni(ini, folder, "work"));
        Assert.Null(FirefoxProfile.FindInProfilesIni(ini, folder, "research"));
        Assert.Null(FirefoxProfile.FindInProfilesIni(ini, folder, "abcd1234.default"));
    }

    [Theory]
    [InlineData("127.0.1.1:+48213", 48213)]
    [InlineData("192.168.1.10:+7", 7)]
    [InlineData("127.0.0.1:48213", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Should_ReadTheProcessHoldingAUnixLock(string? target, int? expected) =>
        Assert.Equal(expected, FirefoxProfile.LockHolder(target));

    [Fact]
    public void Should_OpenANamedFolderInPlace_AndNeverRemoveIt()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root.FullName, "named")).FullName;

        var profile = FirefoxProfile.Open(folder);

        Assert.Equal(folder, profile.Directory);
        Assert.Equal(folder, profile.Name);
        Assert.False(profile.Disposable);
        Assert.False(File.Exists(Path.Combine(folder, "user.js")));
        Assert.True(profile.RemoveAsync().IsCompletedSuccessfully);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void Should_RefuseAFolderThatDoesNotExist_AndANameNoProfilesIniLists()
    {
        var missing = Assert.Throws<FirefoxException>(() => FirefoxProfile.Open(Path.Combine(_root.FullName, "missing")));
        var unknown = Assert.Throws<FirefoxException>(() => FirefoxProfile.Open("no-such-orkeon-profile-" + Guid.NewGuid().ToString("N")));

        Assert.Contains("does not exist", missing.Message, StringComparison.Ordinal);
        Assert.Contains("has no profile named", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_RefuseAProfileAnotherFirefoxHoldsOpen_AndIgnoreAStaleLock()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The Unix lock is a symbolic link naming the process.");
        var held = Directory.CreateDirectory(Path.Combine(_root.FullName, "held")).FullName;
        var stale = Directory.CreateDirectory(Path.Combine(_root.FullName, "stale")).FullName;
        File.CreateSymbolicLink(Path.Combine(held, "lock"), $"127.0.0.1:+{Environment.ProcessId}");
        File.CreateSymbolicLink(Path.Combine(stale, "lock"), "127.0.0.1:+2147483646");

        var error = Assert.Throws<FirefoxException>(() => FirefoxProfile.Open(held));
        var opened = FirefoxProfile.Open(stale);

        Assert.Contains($"is open in another Firefox (process {Environment.ProcessId})", error.Message, StringComparison.Ordinal);
        Assert.Equal(stale, opened.Directory);
    }

    [Fact]
    public void Should_RemoveTheDisposableFoldersNoFirefoxHolds_AndLeaveTheRest()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The Unix lock is a symbolic link naming the process.");
        var old = DateTime.UtcNow.AddHours(-1);
        string Folder(string name, bool aged)
        {
            var directory = Directory.CreateDirectory(Path.Combine(_root.FullName, name)).FullName;
            if (aged)
                Directory.SetLastWriteTimeUtc(directory, old);
            return directory;
        }

        var stale = Folder(Guid.NewGuid().ToString("N"), aged: true);
        File.CreateSymbolicLink(Path.Combine(stale, "lock"), "127.0.0.1:+2147483646");
        Directory.SetLastWriteTimeUtc(stale, old);
        var unlocked = Folder(Guid.NewGuid().ToString("N"), aged: true);
        var held = Folder(Guid.NewGuid().ToString("N"), aged: true);
        File.CreateSymbolicLink(Path.Combine(held, "lock"), $"127.0.0.1:+{Environment.ProcessId}");
        Directory.SetLastWriteTimeUtc(held, old);
        var young = Folder(Guid.NewGuid().ToString("N"), aged: false);
        var named = Folder("agent-work", aged: true);

        FirefoxProfile.RemoveStale(_root.FullName, FirefoxProfile.StaleAge);

        Assert.False(Directory.Exists(stale));
        Assert.False(Directory.Exists(unlocked));
        Assert.True(Directory.Exists(held));
        Assert.True(Directory.Exists(young));
        Assert.True(Directory.Exists(named));
    }
}
