using Orkeon.Tools.Email.Tests.Fixtures;
using Orkeon.Tools.Email.Trial;

namespace Orkeon.Tools.Email.Tests.Trial;

/// <summary>Inside a mail trial email_accounts hides the addresses of the real accounts.</summary>
public sealed class EmailTrialAccountsTests
{
    [Fact]
    [Trait("BR", "BR-12")]
    public async Task ShouldListRealNamesAndRightsWithoutAddress_WhenEmailAccountsIsCalledInATrial()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());

        var listed = ToolResults.Success(await fixture.CallAsync("email_accounts"));

        var accounts = ToolResults.Objects(listed, "accounts");
        Assert.Equal(
            ["closed", "drafter", "full", "nocopy", "organizer", "pop", "reader"],
            accounts.Select(a => (string)a["name"]!).Order(StringComparer.Ordinal).ToList());
        var full = accounts.Single(a => (string?)a["name"] == "full");
        Assert.Contains("Send", Assert.IsType<string>(full["rights"]), StringComparison.Ordinal);
        Assert.All(accounts, a => Assert.True(!a.TryGetValue("address", out var address) || string.IsNullOrEmpty(address as string)));
    }

    [Fact]
    [Trait("BR", "BR-12")]
    public async Task ShouldListTheAddress_WhenNoTrialIsEntered()
    {
        using var fixture = new ToolFixture(scoped: true);

        var listed = ToolResults.Success(await fixture.CallAsync("email_accounts"));

        var full = ToolResults.Objects(listed, "accounts").Single(a => (string?)a["name"] == "full");
        Assert.Equal("agent@example.test", full["address"]);
    }
}
