using Microsoft.Extensions.Options;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Security;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Security;

/// <summary>The hourly cap, the pace of posts and the screen of what others wrote.</summary>
public sealed class ChatSecurityTests
{
    [Fact]
    public void Should_count_attempts_over_a_sliding_hour_by_account()
    {
        var time = new ManualTimeProvider();
        var quota = new SendQuota(time);

        Assert.True(quota.TryConsume("work", 2));
        time.Advance(TimeSpan.FromMinutes(40));
        Assert.True(quota.TryConsume("WORK", 2));
        Assert.False(quota.TryConsume("work", 2));
        Assert.True(quota.TryConsume("other", 2));
        time.Advance(TimeSpan.FromMinutes(20));
        Assert.True(quota.TryConsume("work", 2));
        Assert.False(quota.TryConsume("work", 2));
    }

    [Fact]
    public void Should_not_count_without_a_cap()
    {
        var quota = new SendQuota(new ManualTimeProvider());

        Assert.All(Enumerable.Range(0, 100), _ => Assert.True(quota.TryConsume("work", null)));
    }

    [Fact]
    public async Task Should_refuse_a_post_that_would_wait_too_long_without_taking_a_slot()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider();
        var pacer = new PostPacer(time);
        var queued = Enumerable.Range(0, 31).Select(_ => pacer.WaitAsync("a", cancellation)).ToList();

        var error = await Assert.ThrowsAsync<ChatToolException>(() => pacer.WaitAsync("a", cancellation));

        Assert.Equal(ChatErrorCode.Throttled, error.Code);
        Assert.Contains("nothing was posted", error.Message, StringComparison.Ordinal);
        time.Advance(TimeSpan.FromSeconds(1));
        queued.Add(pacer.WaitAsync("a", cancellation));
        time.Advance(TimeSpan.FromSeconds(30));
        await Task.WhenAll(queued);
    }

    [Fact]
    public async Task Should_give_the_slot_of_a_cancelled_wait_to_the_next()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider();
        var pacer = new PostPacer(time);
        using var interruption = new CancellationTokenSource();
        await pacer.WaitAsync("a", cancellation);
        var cancelled = pacer.WaitAsync("a", interruption.Token);
        await interruption.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        var next = pacer.WaitAsync("a", cancellation);

        Assert.Equal([TimeSpan.FromSeconds(1)], time.DueIns);
        time.Advance(TimeSpan.FromSeconds(1));
        await next;
    }

    [Theory]
    [InlineData("a\u2028b", "a b")]
    [InlineData("a\u2029b", "a b")]
    [InlineData("a\u202Eb", "ab")]
    [InlineData("a\u2066b", "ab")]
    [InlineData("a\u200Bb\uFEFF", "ab")]
    [InlineData("a\U000E0041b", "ab")]
    [InlineData("a\u200Db", "a\u200Db")]
    [InlineData("\U0001F469\u200D\U0001F4BB", "\U0001F469\u200D\U0001F4BB")]
    public void Should_put_a_name_on_one_line_as_it_reads(string name, string expected) =>
        Assert.Equal(expected, Screen(withhold: false).Label(name));

    [Fact]
    public void Should_give_back_the_unit_of_its_own_write()
    {
        var time = new ManualTimeProvider();
        var quota = new SendQuota(time);
        Assert.True(quota.TryConsume("work", 2, out var first));
        time.Advance(TimeSpan.FromMinutes(30));
        Assert.True(quota.TryConsume("work", 2));
        quota.Refund("work", first);
        Assert.True(quota.TryConsume("work", 2));
        time.Advance(TimeSpan.FromMinutes(31));

        Assert.False(quota.TryConsume("work", 2));
    }

    [Fact]
    public async Task Should_give_each_post_of_a_place_its_own_second()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider();
        var pacer = new PostPacer(time);

        await pacer.WaitAsync("a", cancellation);
        var second = pacer.WaitAsync("a", cancellation);
        var third = pacer.WaitAsync("a", cancellation);

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], time.DueIns.Order());
        Assert.True(pacer.WaitAsync("b", cancellation).IsCompleted);
        Assert.False(second.IsCompleted || third.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        await second;
        Assert.False(third.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        await third;
    }

    [Fact]
    public void Should_flag_by_default_and_withhold_on_request()
    {
        var flags = Screen(withhold: false);
        var withholds = Screen(withhold: true);

        Assert.Equal("rejected", flags.Flag("a name", Hostile.Text));
        Assert.Equal(Hostile.Text, flags.Label(Hostile.Text));
        Assert.Equal(ChatContentScreen.WithheldName, withholds.Label(Hostile.Text));
        Assert.Equal("rejected", withholds.Flag(Hostile.Text));
        Assert.Null(flags.Flag("Ada", null));
        Assert.Equal("Ada", withholds.Label("Ada"));
    }

    [Fact]
    public void Should_make_a_label_one_short_line()
    {
        var screen = Screen(withhold: false);

        Assert.Equal("a b", screen.Label(" a\nb\u0007"));
        Assert.Equal(new string('x', 120) + "…", screen.Label(new string('x', 500)));
        Assert.Null(screen.Label(" "));
    }

    private static ChatContentScreen Screen(bool withhold)
    {
        var options = new ChatToolsOptions();
        options.Screening.WithholdRejected = withhold;
        return new ChatContentScreen(Options.Create(options));
    }
}
