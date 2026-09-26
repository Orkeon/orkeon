using Orkeon.Tools.Email.Security;
using Orkeon.Tools.Email.Tests.Doubles;

namespace Orkeon.Tools.Email.Tests.Security;

/// <summary>The per-account hourly send cap over a sliding hour.</summary>
public sealed class SendQuotaTests
{
    [Fact]
    public void Should_always_fit_When_no_cap_is_set()
    {
        var quota = new SendQuota(new FakeTimeProvider());

        Assert.All(Enumerable.Range(0, 100), _ => Assert.True(quota.TryConsume("acct", null)));
    }

    [Fact]
    public void Should_refuse_the_send_that_exceeds_the_cap_and_record_nothing_for_it()
    {
        var time = new FakeTimeProvider();
        var quota = new SendQuota(time);

        Assert.True(quota.TryConsume("acct", 2));
        Assert.True(quota.TryConsume("acct", 2));
        Assert.False(quota.TryConsume("acct", 2));
        Assert.False(quota.TryConsume("acct", 2));

        time.Advance(TimeSpan.FromHours(1));
        Assert.True(quota.TryConsume("acct", 2));
    }

    [Fact]
    public void Should_free_each_slot_one_hour_after_its_own_send()
    {
        var time = new FakeTimeProvider();
        var quota = new SendQuota(time);
        Assert.True(quota.TryConsume("acct", 2));
        time.Advance(TimeSpan.FromMinutes(30));
        Assert.True(quota.TryConsume("acct", 2));

        time.Advance(TimeSpan.FromMinutes(29));
        Assert.False(quota.TryConsume("acct", 2));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(quota.TryConsume("acct", 2));
        Assert.False(quota.TryConsume("acct", 2));
    }

    [Fact]
    public void Should_count_each_account_apart_whatever_the_case_of_its_name()
    {
        var quota = new SendQuota(new FakeTimeProvider());

        Assert.True(quota.TryConsume("work", 1));
        Assert.False(quota.TryConsume("WORK", 1));
        Assert.True(quota.TryConsume("perso", 1));
    }
}
