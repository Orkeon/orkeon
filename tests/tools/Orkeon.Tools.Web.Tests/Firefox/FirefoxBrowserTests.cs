using Microsoft.Extensions.Logging;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Firefox.BiDi;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests.Firefox;

/// <summary>The start of a browser: one that leaves its first <c>session.new</c> unanswered is started again once (#335).</summary>
public class FirefoxBrowserTests : FirefoxSessionTestBase
{
    private static readonly FirefoxToolOptions Options = new() { CommandTimeoutSeconds = 30 };

    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingLogger _logger = new();

    private Task<FirefoxBrowser> OpenBrowserAsync() =>
        FirefoxBrowser.OpenAsync(
            Launcher, FirefoxProfileChoice.Disposable, Options,
            Timings with { Clock = _clock, CloseWait = TimeSpan.Zero }, _logger, Ct);

    /// <summary>Waits until browser <paramref name="count"/> waits for its session on the clock, then lets the command timeout pass.</summary>
    private async Task TimeOutSessionOfBrowserAsync(int count)
    {
        await Polling.WaitUntilAsync(() => Launcher.Browsers.Count == count && _clock.PendingTimers == 1);
        _clock.Advance(TimeSpan.FromSeconds(Options.CommandTimeoutSeconds));
    }

    [Fact]
    public async Task Should_StartAnotherBrowser_When_TheFirstLeavesTheSessionUnanswered()
    {
        var launched = 0;
        Launcher.Configure += browser =>
        {
            if (++launched == 1)
                browser.Wire.Unanswered.Add("session.new");
        };

        var opening = OpenBrowserAsync();
        await TimeOutSessionOfBrowserAsync(1);
        await using var browser = await opening;

        Assert.Equal(2, Launcher.Browsers.Count);
        Assert.True(Launcher.Browsers[0].Disposed);
        Assert.False(Launcher.Browsers[1].Disposed);
        Assert.Equal(["session.new", "browsingContext.getTree", "session.subscribe"], Launcher.Browsers[1].Wire.Methods);
        Assert.True(browser.IsAlive);
        var warning = Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("'session.new'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("starts it again once", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_FailSayingItRetried_When_TheSecondBrowserLeavesTheSessionUnansweredToo()
    {
        Launcher.Configure += browser => browser.Wire.Unanswered.Add("session.new");

        var opening = OpenBrowserAsync();
        await TimeOutSessionOfBrowserAsync(1);
        await TimeOutSessionOfBrowserAsync(2);
        var error = await Assert.ThrowsAsync<BiDiException>(() => opening);

        Assert.Equal(BiDiException.Timeout, error.Code);
        Assert.Contains("started again", error.Message, StringComparison.Ordinal);
        Assert.Equal(FirefoxBrowser.StartAttempts, Launcher.Browsers.Count);
        Assert.All(Launcher.Browsers, browser => Assert.True(browser.Disposed));
    }

    [Fact]
    public async Task Should_NotStartAnotherBrowser_When_ALaterStartCommandTimesOut()
    {
        Launcher.Configure += browser => browser.Wire.Unanswered.Add("browsingContext.getTree");

        var opening = OpenBrowserAsync();
        await TimeOutSessionOfBrowserAsync(1);
        var error = await Assert.ThrowsAsync<BiDiException>(() => opening);

        Assert.Equal(BiDiException.Timeout, error.Code);
        var browser = Assert.Single(Launcher.Browsers);
        Assert.True(browser.Disposed);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Should_NotStartAnotherBrowser_When_TheBrowserRefusesTheSession()
    {
        Launcher.Configure += browser =>
            browser.Wire.Handlers["session.new"] = _ => throw new FakeBiDiError("session not created", "Maximum number of active sessions");

        var error = await Assert.ThrowsAsync<BiDiException>(OpenBrowserAsync);

        Assert.Equal("session not created", error.Code);
        Assert.True(Assert.Single(Launcher.Browsers).Disposed);
    }
}
