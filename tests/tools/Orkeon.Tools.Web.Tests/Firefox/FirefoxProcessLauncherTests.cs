using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests.Firefox;

public sealed class FirefoxProcessLauncherTests : IDisposable
{
    private const string NoDisplay = "The stand-in browser is a shell script.";

    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("orkeon-firefox-launcher-tests-");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _work.Delete(recursive: true);

    [Fact]
    public void Should_StartOnAFreePortWithItsOwnProfile_AndAWindow()
    {
        var arguments = FirefoxProcessLauncher.BuildArguments("/profiles/p1", headless: false);

        Assert.Equal(
            ["--remote-debugging-port", "0", "--profile", "/profiles/p1", "--no-remote", "--new-instance", "about:blank"],
            arguments);
    }

    [Fact]
    public void Should_AddTheHeadlessFlag_When_Headless()
    {
        var arguments = FirefoxProcessLauncher.BuildArguments("/profiles/p1", headless: true);

        Assert.Contains("--headless", arguments);
        Assert.Equal("about:blank", arguments[^1]);
    }

    [Theory]
    [InlineData(false, true, ":0", null, false)]
    [InlineData(false, true, null, "wayland-0", false)]
    [InlineData(false, true, null, null, true)]
    [InlineData(false, true, "", "", true)]
    [InlineData(false, false, null, null, false)]
    [InlineData(true, true, ":0", null, true)]
    [InlineData(true, false, null, null, true)]
    public void Should_FallBackToHeadless_OnlyWhen_ADisplayIsNeededAndNoneIsSet(
        bool configured, bool needsDisplay, string? display, string? wayland, bool expected)
    {
        var headless = FirefoxProcessLauncher.ResolveHeadless(configured, needsDisplay, name => name switch
        {
            "DISPLAY" => display,
            "WAYLAND_DISPLAY" => wayland,
            _ => null,
        });

        Assert.Equal(expected, headless);
    }

    [Fact]
    public void Should_FindFirefoxOnThePath_BeforeTheKnownFolders()
    {
        var path = string.Join(Path.PathSeparator, "relative/bin", "/opt/a", "/opt/b");
        var onB = Path.Combine("/opt/b", "firefox");

        var found = FirefoxProcessLauncher.Locate(null, path, ["/known/firefox"], file => file == onB || file == "/known/firefox", windows: false);

        Assert.Equal(onB, found);
    }

    [Fact]
    public void Should_FallBackToAKnownFolder_When_ThePathHoldsNoFirefox()
    {
        var found = FirefoxProcessLauncher.Locate(null, "/opt/a", ["/missing/firefox", "/known/firefox"], file => file == "/known/firefox", windows: false);

        Assert.Equal("/known/firefox", found);
    }

    [Fact]
    public void Should_UseTheConfiguredPathAsItIs_AndNothingElse_When_ItDoesNotExist()
    {
        var configured = Path.Combine("/opt", "nightly", "firefox");

        Assert.Equal(configured, FirefoxProcessLauncher.Locate(configured, "/opt/a", ["/known/firefox"], file => file == configured, windows: false));
        Assert.Null(FirefoxProcessLauncher.Locate(configured, "/opt/a", ["/known/firefox"], file => file == "/known/firefox", windows: false));
    }

    [Fact]
    public void Should_LookUpAConfiguredNameOnThePath_AndNotInTheKnownFolders()
    {
        var onA = Path.Combine("/opt/a", "firefox-esr");

        Assert.Equal(onA, FirefoxProcessLauncher.Locate("firefox-esr", "/opt/a", ["/known/firefox"], file => file == onA, windows: false));
        Assert.Null(FirefoxProcessLauncher.Locate("firefox-esr", "/opt/a", ["/known/firefox"], file => file == "/known/firefox", windows: false));
    }

    [Fact]
    public void Should_LookForTheExeName_UnderWindows()
    {
        var onA = Path.Combine("/opt/a", "firefox.exe");

        Assert.Equal(onA, FirefoxProcessLauncher.Locate(null, "/opt/a", [], file => file == onA, windows: true));
    }

    [Fact]
    public async Task Should_SayFirefoxIsMissing_When_TheConfiguredProgramDoesNotExist()
    {
        var launcher = new FirefoxProcessLauncher(new FirefoxToolOptions
        {
            ExecutablePath = Path.Combine(_work.FullName, "absent", "firefox"),
            ProfileRoot = Profiles(),
        });

        var error = await Assert.ThrowsAsync<FirefoxException>(() => launcher.LaunchAsync(FirefoxProfileChoice.Disposable, Ct));

        Assert.Contains("Orkeon:Tools:Firefox:ExecutablePath", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Profiles()));
    }

    [Fact]
    public async Task Should_ConnectToTheAnnouncedEndpoint_AndEndTheBrowserWithItsProfile_When_Disposed()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), NoDisplay);
        await using var server = new FakeWebSocketServer(FakeWebSocketServer.EchoAsync);
        var pidFile = Path.Combine(_work.FullName, "pid");
        var browser = Script($"""
            echo "$$" > '{pidFile}'
            echo "$@" > '{pidFile}.args'
            echo 'console.warn: starting' >&2
            echo 'WebDriver BiDi listening on ws://{server.Endpoint.Authority}' >&2
            exec sleep 600
            """);
        var launcher = new FirefoxProcessLauncher(
            new FirefoxToolOptions { ExecutablePath = browser, ProfileRoot = Profiles(), Headless = true })
        {
            ExitGrace = TimeSpan.FromMilliseconds(50),
        };

        var running = await launcher.LaunchAsync(FirefoxProfileChoice.Disposable, Ct);
        try
        {
            Assert.True(running.Headless);
            await running.Transport.SendAsync("ping", Ct);
            Assert.Equal("ping", await running.Transport.ReceiveAsync(Ct));

            var profile = Assert.Single(Directory.GetDirectories(Profiles()));
            Assert.True(File.Exists(Path.Combine(profile, "user.js")));
            var arguments = await File.ReadAllTextAsync(pidFile + ".args", Ct);
            Assert.Equal($"--remote-debugging-port 0 --profile {profile} --no-remote --new-instance --headless about:blank", arguments.Trim());
        }
        finally
        {
            await running.DisposeAsync();
        }

        await running.DisposeAsync();
        var pid = int.Parse(await File.ReadAllTextAsync(pidFile, Ct), System.Globalization.CultureInfo.InvariantCulture);
        await Polling.WaitUntilAsync(() => !IsRunning(pid));
        Assert.Empty(Directory.GetDirectories(Profiles()));
    }

    [Fact]
    public async Task Should_ReportWhatTheBrowserSaid_AndLeaveNoProfile_When_ItEndsBeforeAnnouncing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), NoDisplay);
        var browser = Script("""
            echo 'Error: no DISPLAY environment variable specified' >&2
            exit 3
            """);
        var launcher = new FirefoxProcessLauncher(new FirefoxToolOptions { ExecutablePath = browser, ProfileRoot = Profiles() });

        var error = await Assert.ThrowsAsync<FirefoxException>(() => launcher.LaunchAsync(FirefoxProfileChoice.Disposable, Ct));

        Assert.Contains("exit code 3", error.Message, StringComparison.Ordinal);
        Assert.Contains("no DISPLAY environment variable specified", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(Profiles()));
    }

    [Fact]
    public async Task Should_KillTheBrowser_AndLeaveNoProfile_When_ItNeverAnnouncesItsEndpoint()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), NoDisplay);
        var pidFile = Path.Combine(_work.FullName, "pid");
        var browser = Script($"""
            echo "$$" > '{pidFile}'
            exec sleep 600
            """);
        var launcher = new FirefoxProcessLauncher(
            new FirefoxToolOptions { ExecutablePath = browser, ProfileRoot = Profiles(), StartupTimeoutSeconds = 1 });

        var error = await Assert.ThrowsAsync<FirefoxException>(() => launcher.LaunchAsync(FirefoxProfileChoice.Disposable, Ct));

        Assert.Contains("did not open its remote protocol", error.Message, StringComparison.Ordinal);
        var pid = int.Parse(await File.ReadAllTextAsync(pidFile, Ct), System.Globalization.CultureInfo.InvariantCulture);
        await Polling.WaitUntilAsync(() => !IsRunning(pid));
        Assert.Empty(Directory.GetDirectories(Profiles()));
    }

    [Fact]
    public async Task Should_FailWithAnExpectedError_When_NothingListensAtTheAnnouncedEndpoint()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), NoDisplay);
        using var refusing = Orkeon.Tests.Shared.Network.LoopbackPorts.Refusing();
        var browser = Script($"""
            echo 'WebDriver BiDi listening on ws://{Orkeon.Tests.Shared.Network.LoopbackPorts.Host}:{refusing.Port}' >&2
            exec sleep 600
            """);
        var launcher = new FirefoxProcessLauncher(new FirefoxToolOptions { ExecutablePath = browser, ProfileRoot = Profiles() });

        var error = await Assert.ThrowsAsync<FirefoxException>(() => launcher.LaunchAsync(FirefoxProfileChoice.Disposable, Ct));

        Assert.Contains("connection to Firefox", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(Profiles()));
    }

    private string Profiles() => Path.Combine(_work.FullName, "profiles");

    /// <summary>Writes a shell script standing in for the Firefox program.</summary>
    private string Script(string body)
    {
        var path = Path.Combine(_work.FullName, $"firefox-{Guid.NewGuid():N}");
        File.WriteAllText(path, "#!/bin/sh\n" + body.ReplaceLineEndings("\n") + "\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
