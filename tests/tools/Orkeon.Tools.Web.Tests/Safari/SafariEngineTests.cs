using Orkeon.Tools.Web.Browser;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Safari;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests.Safari;

public sealed class SafariEngineTests
{
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_StartAnotherDriver_When_TheFirstOpenedNoSession()
    {
        await using var browser = new FakeBrowserProcess();
        var limits = new List<TimeSpan>();

        var opened = await SafariEngine.OpenAsync(
            (limit, _) =>
            {
                limits.Add(limit);
                return Task.FromResult<IBrowserProcess?>(limits.Count == 1 ? null : browser);
            },
            Long,
            TimeSpan.FromSeconds(15),
            Ct);

        Assert.Same(browser, opened);
        Assert.Equal(2, limits.Count);
        Assert.All(limits, limit => Assert.Equal(TimeSpan.FromSeconds(15), limit));
    }

    [Fact]
    public async Task Should_GiveADriverNoMoreThanTheStart_When_TheStartIsShorterThanAnAttempt()
    {
        await using var browser = new FakeBrowserProcess();
        var limit = TimeSpan.Zero;

        await SafariEngine.OpenAsync(
            (given, _) =>
            {
                limit = given;
                return Task.FromResult<IBrowserProcess?>(browser);
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            Ct);

        Assert.InRange(limit, TimeSpan.FromTicks(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Should_SayHowManyDriversWereTried_When_NoneOpenedASession()
    {
        var attempts = 0;

        var failed = await Assert.ThrowsAsync<FirefoxException>(() => SafariEngine.OpenAsync(
            async (limit, ct) =>
            {
                // The first driver gives up at once: a second one is started however loaded the machine is.
                if (++attempts > 1)
                    await Task.Delay(limit, ct);
                return null;
            },
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(100),
            Ct));

        Assert.True(attempts > 1, $"{attempts} attempt(s)");
        Assert.Contains("did not open a session within", failed.Message, StringComparison.Ordinal);
        Assert.Contains($"{attempts} attempt(s)", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_LetARefusalThrough_WithoutAnotherDriver()
    {
        var attempts = 0;

        var refused = await Assert.ThrowsAsync<FirefoxException>(() => SafariEngine.OpenAsync(
            (_, _) =>
            {
                attempts++;
                throw new FirefoxException("safaridriver refused the session.");
            },
            Long,
            TimeSpan.FromSeconds(15),
            Ct));

        Assert.Equal(1, attempts);
        Assert.Contains("refused", refused.Message, StringComparison.Ordinal);
    }
}
