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

    /// <summary>
    /// Waits until browser <paramref name="count"/> has sent <paramref name="method"/> and only its wait
    /// is armed on the clock, then lets the command timeout pass. Waiting on the timer count alone races:
    /// the wait of an answered <c>session.new</c> is armed for an instant before the answer disarms it.
    /// </summary>
    private async Task TimeOutAsync(int count, string method)
    {
        await Polling.WaitUntilAsync(() =>
            Launcher.Browsers.Count == count
            && Launcher.Browsers[count - 1].Wire.Methods is [.., var last] && last == method
            && _clock.PendingTimers == 1);
        _clock.Advance(TimeSpan.FromSeconds(Options.CommandTimeoutSeconds));
    }

    /// <summary>The start, bounded: a start that never ends fails the test instead of hanging the run.</summary>
    private static Task<FirefoxBrowser> Bounded(Task<FirefoxBrowser> opening) => opening.WaitAsync(Polling.DefaultTimeout);

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
        await TimeOutAsync(1, "session.new");
        await using var browser = await Bounded(opening);

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
        await TimeOutAsync(1, "session.new");
        await TimeOutAsync(2, "session.new");
        var error = await Assert.ThrowsAsync<BiDiException>(() => Bounded(opening));

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
        await TimeOutAsync(1, "browsingContext.getTree");
        var error = await Assert.ThrowsAsync<BiDiException>(() => Bounded(opening));

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

        var error = await Assert.ThrowsAsync<BiDiException>(() => Bounded(OpenBrowserAsync()));

        Assert.Equal("session not created", error.Code);
        Assert.True(Assert.Single(Launcher.Browsers).Disposed);
    }
}
