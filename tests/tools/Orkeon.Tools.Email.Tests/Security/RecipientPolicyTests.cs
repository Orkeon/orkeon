using MimeKit;
using Orkeon.Tools.Email.Security;

namespace Orkeon.Tools.Email.Tests.Security;

/// <summary>The send allow-list: exact addresses, <c>*@domain</c>, <c>*</c>, and nobody when empty.</summary>
public sealed class RecipientPolicyTests
{
    [Theory]
    [InlineData("*", true)]
    [InlineData("*@example.com", true)]
    [InlineData("boss@example.com", true)]
    [InlineData("*@", false)]
    [InlineData("*@*.example.com", false)]
    [InlineData("*@a@b.com", false)]
    [InlineData("b*ss@example.com", false)]
    [InlineData("Boss <boss@example.com>", false)]
    [InlineData("boss.example.com", false)]
    [InlineData("boss", false)]
    public void Should_accept_only_an_address_a_domain_or_a_star_as_a_pattern(string pattern, bool valid)
    {
        Assert.Equal(valid, RecipientPolicy.IsValidPattern(pattern));
    }

    [Fact]
    public void Should_allow_nobody_When_the_list_is_empty()
    {
        var refused = RecipientPolicy.Disallowed([Address("boss@example.com")], []);

        Assert.Equal("boss@example.com", Assert.Single(refused).Address);
    }

    [Fact]
    public void Should_allow_everyone_with_a_star()
    {
        Assert.Empty(RecipientPolicy.Disallowed([Address("a@x.org"), Address("b@y.net")], ["*"]));
    }

    [Fact]
    public void Should_compare_exact_addresses_ignoring_case()
    {
        var refused = RecipientPolicy.Disallowed([Address("Boss@Example.COM"), Address("other@example.com")], ["boss@example.com"]);

        Assert.Equal("other@example.com", Assert.Single(refused).Address);
    }

    [Fact]
    public void Should_allow_a_whole_domain_but_not_its_subdomains_nor_lookalikes()
    {
        var refused = RecipientPolicy.Disallowed(
            [Address("anyone@corp.example"), Address("ANYONE@CORP.EXAMPLE"), Address("x@sub.corp.example"), Address("x@evilcorp.example")],
            ["*@corp.example"]);

        Assert.Equal(["x@sub.corp.example", "x@evilcorp.example"], refused.Select(r => r.Address));
    }

    [Fact]
    public void Should_judge_the_address_and_never_the_display_name()
    {
        var disguised = MailboxAddress.Parse("\"boss@example.com\" <attacker@evil.example>");

        var refused = RecipientPolicy.Disallowed([disguised], ["boss@example.com"]);

        Assert.Equal("attacker@evil.example", Assert.Single(refused).Address);
    }

    private static MailboxAddress Address(string address) => new(null, address);
}
