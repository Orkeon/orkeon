using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Mailboxes.Graph;
using Orkeon.Tools.Email.Mailboxes.Imap;
using Orkeon.Tools.Email.Mailboxes.Pop3;
using Orkeon.Tools.Email.Mailboxes.Smtp;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Mailboxes;

/// <summary>One backend per account, built without dialling, and the sender its settings call for.</summary>
public sealed class MailboxProviderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_build_the_backend_of_each_protocol_once_per_account_without_connecting()
    {
        using var credentials = new CredentialsFixture();
        await using var provider = Provider(credentials);
        var imap = TestAccounts.Resolve("imap", TestAccounts.Custom());
        var popOptions = TestAccounts.Custom();
        popOptions.Incoming.Protocol = IncomingProtocol.Pop3;
        var pop = TestAccounts.Resolve("pop", popOptions);
        var graph = TestAccounts.Resolve("graph", TestAccounts.Outlook());

        var first = provider.GetMailbox(imap);

        Assert.IsType<ImapMailbox>(first);
        Assert.Same(first, provider.GetMailbox(imap with { Name = "IMAP" }));
        Assert.IsType<Pop3Mailbox>(provider.GetMailbox(pop));
        Assert.IsType<GraphMailbox>(provider.GetMailbox(graph));
        Assert.Empty(credentials.Handler.Requests);
    }

    [Fact]
    public async Task Should_hand_out_the_sender_the_account_declares()
    {
        using var credentials = new CredentialsFixture();
        await using var provider = Provider(credentials);

        Assert.IsType<SmtpMailSender>(provider.GetSender(TestAccounts.Resolve("smtp", TestAccounts.Custom(EmailRights.Send))));
        Assert.IsType<GraphMailSender>(provider.GetSender(TestAccounts.Resolve("graph", TestAccounts.Outlook(EmailRights.Send))));
    }

    [Fact]
    public async Task Should_refuse_a_sender_for_an_account_without_an_outgoing_server()
    {
        using var credentials = new CredentialsFixture();
        await using var provider = Provider(credentials);
        var options = TestAccounts.Custom();
        options.Outgoing.Host = null;

        var error = Assert.Throws<EmailToolException>(() => provider.GetSender(TestAccounts.Resolve("reader", options)));

        Assert.Equal(EmailErrorCode.Unsupported, error.Code);
        Assert.Equal("E-mail account 'reader' declares no outgoing server (Outgoing:Host), so it cannot send.", error.Message);
    }

    [Fact]
    public async Task Should_close_pooled_IMAP_connections_When_disposed()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        var provider = Provider(credentials);
        await provider.GetMailbox(TestAccounts.Loopback(IncomingProtocol.Imap, server.Port)).ListFoldersAsync(Token);

        await provider.DisposeAsync();

        Assert.Equal("LOGOUT", server.Commands[^1]);
    }

    [Fact]
    public async Task Should_drop_pooled_connections_without_a_goodbye_When_disposed_synchronously()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        var provider = Provider(credentials);
        await provider.GetMailbox(TestAccounts.Loopback(IncomingProtocol.Imap, server.Port)).ListFoldersAsync(Token);

        DisposeLikeASynchronousContainer(provider);

        Assert.DoesNotContain("LOGOUT", server.Commands);
    }

    /// <summary>What a DI container disposed synchronously does: <see cref="IDisposable.Dispose"/>, no await.</summary>
    private static void DisposeLikeASynchronousContainer(IDisposable disposable) => disposable.Dispose();

    private static MailboxProvider Provider(CredentialsFixture credentials) =>
        new(new NetworkMailServiceConnector(), credentials.Provider, () => credentials.Http, credentials.Time);
}
