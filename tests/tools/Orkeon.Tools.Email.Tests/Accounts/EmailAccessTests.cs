using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Accounts;

/// <summary>The gate between a tool and a mailbox: rights first, then backend capabilities.</summary>
public sealed class EmailAccessTests
{
    [Fact]
    public void Should_refuse_a_right_the_account_does_not_grant_and_say_where_to_add_it()
    {
        var access = Access(out _, TestAccounts.Custom(EmailRights.Read | EmailRights.Draft));

        var error = Assert.Throws<EmailToolException>(() => access.Authorize("acct", EmailRights.Send));

        Assert.Equal(EmailErrorCode.RightDenied, error.Code);
        Assert.Equal(
            "E-mail account 'acct' does not grant the Send right (it grants: Read, Draft). An operator adds it under Orkeon:Tools:Email:Accounts:acct:Rights.",
            error.Message);
    }

    [Fact]
    public void Should_require_every_right_of_a_combined_demand()
    {
        var access = Access(out _, TestAccounts.Custom(EmailRights.Read));

        var error = Assert.Throws<EmailToolException>(() => access.Authorize("acct", EmailRights.Read | EmailRights.Organize));

        Assert.Contains("does not grant the Read, Organize right", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_hand_out_the_account_When_the_right_is_granted()
    {
        var access = Access(out _, TestAccounts.Custom(EmailRights.Read | EmailRights.Organize));

        var account = access.Authorize(null, EmailRights.Organize);

        Assert.Equal("acct", account.Name);
        EmailAccess.Demand(account, EmailRights.None);
    }

    [Fact]
    public void Should_refuse_a_capability_the_backend_lacks_with_a_way_out()
    {
        var access = Access(out var mailboxes, TestAccounts.Custom());
        mailboxes.MailboxOf("acct").Capabilities = MailboxCapabilities.None;
        var account = access.Authorize("acct", EmailRights.Read);

        var folders = Assert.Throws<EmailToolException>(() => access.Mailbox(account, MailboxCapabilities.Folders));
        var trash = Assert.Throws<EmailToolException>(() => access.Mailbox(account, MailboxCapabilities.Trash));

        Assert.Equal(EmailErrorCode.Unsupported, folders.Code);
        Assert.Equal("E-mail account 'acct' reads mail over Imap, which has no folders (switch the account to IMAP for that).", folders.Message);
        Assert.Contains("which has no trash: delete for good with `permanent: true` (needs the Purge right)", trash.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Move", "folders to move messages into")]
    [InlineData("Flags", "read or flagged marks")]
    [InlineData("Drafts", "drafts folder")]
    public void Should_name_each_missing_capability(string capability, string description)
    {
        var access = Access(out var mailboxes, TestAccounts.Custom());
        mailboxes.MailboxOf("acct").Capabilities = MailboxCapabilities.None;
        var account = access.Authorize("acct", EmailRights.Read);

        var error = Assert.Throws<EmailToolException>(() => access.Mailbox(account, Enum.Parse<MailboxCapabilities>(capability)));

        Assert.Contains(description, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_hand_out_the_mailbox_and_the_sender_of_the_account()
    {
        var access = Access(out var mailboxes, TestAccounts.Custom());
        var account = access.Authorize("acct", EmailRights.Read);

        Assert.Same(mailboxes.MailboxOf("acct"), access.Mailbox(account, MailboxCapabilities.Folders | MailboxCapabilities.Flags));
        Assert.Same(mailboxes.SenderOf("acct"), access.Sender(account));
        Assert.Equal(["acct"], access.Accounts.Names);
    }

    private static EmailAccess Access(out FakeMailboxProvider mailboxes, EmailAccountOptions account)
    {
        var options = new EmailToolsOptions();
        options.Accounts["acct"] = account;
        mailboxes = new FakeMailboxProvider();
        return new EmailAccess(new EmailAccountRegistry(Microsoft.Extensions.Options.Options.Create(options)), mailboxes);
    }
}
