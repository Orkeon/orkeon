using Orkeon.Tools.Web.Browser;
using Orkeon.Tools.Web.Chromium;
using Orkeon.Tools.Web.Firefox;

namespace Orkeon.Tools.Web.Tests.Chromium;

public sealed class ChromiumEngineTests : IDisposable
{
    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("orkeon-chromium-tests-");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _work.Delete(recursive: true);

    [Theory]
    [InlineData("chrome", "Chrome")]
    [InlineData("Edge", "Edge")]
    [InlineData("BRAVE", "Brave")]
    public void Should_BuildTheEngineTheOptionsName(string id, string displayName)
    {
        var engine = BrowserEngines.Create(new FirefoxToolOptions { Engine = id }, null);

        Assert.IsType<ChromiumEngine>(engine);
        Assert.Equal(displayName, engine.DisplayName);
        Assert.Equal(id, engine.Id, ignoreCase: true);
    }

    [Fact]
    public void Should_StartOnAFreePortAndAFolderOfItsOwn_WithTheSandboxOn()
    {
        var arguments = ChromiumEngine.BuildArguments("/profiles/p1", disposable: true, headless: true);

        Assert.Contains("--remote-debugging-port=0", arguments);
        Assert.Contains("--user-data-dir=/profiles/p1", arguments);
        Assert.Contains("--headless=new", arguments);
        Assert.Equal("about:blank", arguments[^1]);
        Assert.DoesNotContain("--no-sandbox", arguments);
    }

    [Fact]
    public void Should_KeepTheKeyringAndTheSyncOfANamedFolder()
    {
        var arguments = ChromiumEngine.BuildArguments("/profiles/p1", disposable: false, headless: false);

        Assert.DoesNotContain("--password-store=basic", arguments);
        Assert.DoesNotContain("--disable-sync", arguments);
        Assert.DoesNotContain("--headless=new", arguments);
    }

    [Theory]
    [InlineData("DevTools listening on ws://127.0.0.1:41233/devtools/browser/a-b", "ws://127.0.0.1:41233/devtools/browser/a-b")]
    [InlineData("DevTools listening on http://127.0.0.1:1/", null)]
    [InlineData("[1:1:ERROR:gpu] nothing", null)]
    public void Should_ReadTheEndpointAnnounced(string line, string? expected) =>
        Assert.Equal(expected, ChromiumEngine.ReadEndpoint(line)?.ToString());

    [Fact]
    public async Task Should_SayTheBrowserIsMissing_When_TheConfiguredProgramDoesNotExist()
    {
        var engine = new ChromiumEngine(ChromiumVariant.Edge, new FirefoxToolOptions
        {
            ExecutablePath = Path.Combine(_work.FullName, "absent", "msedge"),
        });

        var error = await Assert.ThrowsAsync<FirefoxException>(() => engine.LaunchAsync(BrowserProfileChoice.Disposable, Ct));

        Assert.StartsWith("Edge was not found", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_FindTheProgramsOfThePlatform()
    {
        Assert.Contains("/usr/bin/microsoft-edge", ChromiumVariant.Edge.KnownPrograms(windows: false, macOS: false, []));
        Assert.Contains("/Applications/Brave Browser.app/Contents/MacOS/Brave Browser", ChromiumVariant.Brave.KnownPrograms(windows: false, macOS: true, []));
        Assert.Equal(
            [Path.Combine("pf", "Google/Chrome/Application/chrome.exe")],
            ChromiumVariant.Chrome.KnownPrograms(windows: true, macOS: false, ["pf", ""]));
    }

    [Fact]
    public void Should_PutDisposableFoldersWhereTheSnapCanReadThem()
    {
        var snap = Path.Combine("/home/ada", "snap", "chromium", "common");

        Assert.Equal(
            Path.Combine(snap, "orkeon", "profiles"),
            ChromiumProfile.DefaultRoot(ChromiumVariant.Chrome, "/home/ada", "/tmp", folder => folder == snap));
        Assert.Equal(
            Path.Combine("/tmp", "orkeon-edge"),
            ChromiumProfile.DefaultRoot(ChromiumVariant.Edge, "/home/ada", "/tmp", _ => true));
    }

    [Fact]
    public async Task Should_RemoveADisposableFolder_AndLeaveANamedOne()
    {
        var disposable = ChromiumProfile.Create(_work.FullName);
        var named = ChromiumProfile.Open("Chrome", Directory.CreateDirectory(Path.Combine(_work.FullName, "kept")).FullName, []);

        await disposable.RemoveAsync();
        await named.RemoveAsync();

        Assert.False(Directory.Exists(disposable.Directory));
        Assert.True(Directory.Exists(named.Directory));
    }

    [Fact]
    public void Should_SweepOnlyTheFoldersOfTheTool()
    {
        var ours = ChromiumProfile.Create(_work.FullName).Directory;
        var other = Directory.CreateDirectory(Path.Combine(_work.FullName, "other")).FullName;

        ChromiumProfile.RemoveStale(_work.FullName, ChromiumProfile.StaleAge);
        Assert.True(Directory.Exists(ours));

        ChromiumProfile.RemoveStale(_work.FullName, TimeSpan.FromHours(-1));
        Assert.False(Directory.Exists(ours));
        Assert.True(Directory.Exists(other));
    }

    [Fact]
    public void Should_RefuseAProfileNameOfTheUsersBrowser()
    {
        var error = Assert.Throws<FirefoxException>(() => ChromiumProfile.Open("Chrome", "Default", []));

        Assert.Contains("full path", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Profile 1")]
    public void Should_RefuseTheDataFolderOfTheUsersBrowser_AndWhatItHolds(string below)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_work.FullName, "user", below)).FullName;

        var error = Assert.Throws<FirefoxException>(
            () => ChromiumProfile.Open("Chrome", folder, [Path.Combine(_work.FullName, "user")]));

        Assert.Contains("user's own Chrome", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_RefuseAFolderThatDoesNotExist() =>
        Assert.Throws<FirefoxException>(() => ChromiumProfile.Open("Chrome", Path.Combine(_work.FullName, "absent"), []));

    [Fact]
    public void Should_RefuseAFolderAnotherBrowserHolds()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The lock is a link under Unix only.");
        File.CreateSymbolicLink(Path.Combine(_work.FullName, "SingletonLock"), $"some-host-{Environment.ProcessId}");

        var error = Assert.Throws<FirefoxException>(() => ChromiumProfile.Open("Chrome", _work.FullName, []));

        Assert.Contains("another Chrome", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("my-host-4242", 4242)]
    [InlineData("my-host", null)]
    [InlineData(null, null)]
    public void Should_ReadTheHolderOfALock(string? target, int? expected) => Assert.Equal(expected, ChromiumProfile.LockHolder(target));

    [Fact]
    public void Should_KnowTheUsersOwnFolders()
    {
        Assert.Contains(
            Path.Combine("/home/ada", ".config", "google-chrome"),
            ChromiumVariant.Chrome.UserDataFolders(windows: false, macOS: false, "/home/ada", "unused"));
        Assert.Empty(ChromiumVariant.Chrome.UserDataFolders(windows: true, macOS: false, "/home/ada", ""));
    }
}
