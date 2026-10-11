using Orkeon.Tools.Email.Tests.Fixtures;
using Orkeon.Tools.Email.Trial;

namespace Orkeon.Tools.Email.Tests.Trial;

/// <summary>Inside a mail trial the real account's rights, allow-list and caps judge the simulated actions.</summary>
public sealed class EmailTrialGuardTests
{
    [Fact]
    [Trait("BR", "BR-11")]
    public async Task ShouldSimulateTheSend_WhenTheRecipientIsAllowed()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());

        ToolResults.Success(await Send(fixture, "client@example.com"));

        Assert.Equal(SimulatedMailActionKind.Send, Assert.Single(trial.Actions).Kind);
        Assert.Empty(fixture.Sender().Sent);
    }

    [Fact]
    [Trait("BR", "BR-11")]
    public async Task ShouldRefuseTheSendAndRecordNothing_WhenTheRecipientIsOutsideTheAllowList()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());

        var error = ToolResults.Failure(await Send(fixture, "stranger@evil.test"));

        Assert.Equal(
            "Tool execution failed: E-mail account 'full' may not send to stranger@evil.test (outside Send:AllowedRecipients). Nothing was sent.",
            error);
        Assert.Empty(trial.Actions);
    }

    [Fact]
    [Trait("BR", "BR-11")]
    public async Task ShouldRefuseTheMove_WhenTheAccountLacksOrganize()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());
        var found = ToolResults.Success(await fixture.CallAsync("email_search", ("account", "reader"), ("folder", "inbox")));
        var id = Assert.IsType<string>(ToolResults.Objects(found, "messages")[0]["id"]);

        var error = ToolResults.Failure(await fixture.CallAsync(
            "email_move", ("account", "reader"), ("folder", "inbox"), ("ids", ToolResults.Of(id)), ("destination", "archive")));

        Assert.Contains("does not grant the Organize right", error, StringComparison.Ordinal);
        Assert.Empty(trial.Actions);
    }

    [Fact]
    [Trait("BR", "BR-11")]
    public async Task ShouldCountSendsPerTrial_AndLeaveTheRealQuotaUntouched()
    {
        using var fixture = new ToolFixture(scoped: true);
        ToolResults.Success(await Send(fixture, "client@example.com"));

        using (EmailTrial.Enter(TrialMailboxSeed.Sample()))
        {
            ToolResults.Success(await Send(fixture, "client@example.com"));
            ToolResults.Success(await Send(fixture, "client@example.com"));
            var error = ToolResults.Failure(await Send(fixture, "client@example.com"));
            Assert.Contains("already sent 2 messages in the last hour", error, StringComparison.Ordinal);
        }

        ToolResults.Success(await Send(fixture, "client@example.com"));
        Assert.Equal(2, fixture.Sender().Sent.Count);
    }

    [Fact]
    [Trait("BR", "BR-11")]
    [Trait("BR", "BR-12")]
    public async Task ShouldOfferTheTrialAccount_WhenNoAccountIsConfigured()
    {
        using var fixture = new ToolFixture(scoped: true, withAccounts: false);
        using (var trial = EmailTrial.Enter(TrialMailboxSeed.Sample()))
        {
            var listed = ToolResults.Success(await fixture.CallAsync("email_accounts"));
            var account = Assert.Single(ToolResults.Objects(listed, "accounts"));
            Assert.Equal("trial", account["name"]);
            Assert.Equal(true, account["ready"]);
            Assert.Equal(true, account["default"]);
            var rights = Assert.IsType<string>(account["rights"]);
            foreach (var right in new[] { "Read", "Organize", "Draft", "Send", "Delete", "Purge" })
                Assert.Contains(right, rights, StringComparison.Ordinal);

            ToolResults.Success(await Send(fixture, "anyone@anywhere.test"));
            Assert.Equal("trial", Assert.Single(trial.Actions).Account);
        }

        var outside = ToolResults.Success(await fixture.CallAsync("email_accounts"));
        Assert.Empty(ToolResults.Objects(outside, "accounts"));
    }

    [Fact]
    [Trait("BR", "BR-11")]
    [Trait("BR", "BR-12")]
    public async Task ShouldNotOfferTheTrialAccount_WhenAnAccountIsConfigured()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());

        var listed = ToolResults.Success(await fixture.CallAsync("email_accounts"));
        Assert.DoesNotContain("trial", ToolResults.Objects(listed, "accounts").Select(a => (string?)a["name"]));
        var error = ToolResults.Failure(await fixture.CallAsync(
            "email_send", ("account", "trial"), ("to", ToolResults.Of("client@example.com")), ("subject", "s"), ("text", "t")));
        Assert.Contains("Unknown e-mail account 'trial'", error, StringComparison.Ordinal);
    }

    private static Task<Orkeon.Domain.Tools.Protocol.ToolCallResponse> Send(ToolFixture fixture, string to) =>
        fixture.CallAsync("email_send", ("to", ToolResults.Of(to)), ("subject", "Hello"), ("text", "Body"));
}
