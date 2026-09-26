using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Mailboxes.Imap;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Mailboxes.Imap;

/// <summary>
/// The IMAP backend against servers unlike the default double: one that drops an idle session,
/// one whose personal folders live under <c>INBOX.</c>, ones without MOVE or UIDPLUS, one that
/// advertises what Gmail does (no PREVIEW, ESEARCH), and folder names and searches that are not
/// ASCII. Every double refuses an extension it does not advertise, so a test fails if the
/// backend (or MailKit on its behalf) relies on one.
/// </summary>
public sealed class ImapServerVariantsTests
{
    private const string WithoutMove = "IMAP4rev1 AUTH=PLAIN SASL-IR UIDPLUS SPECIAL-USE NAMESPACE PREVIEW";
    private const string WithoutMoveOrUidPlus = "IMAP4rev1 AUTH=PLAIN SASL-IR SPECIAL-USE NAMESPACE PREVIEW";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_check_an_idle_pooled_connection_and_reconnect_When_the_server_dropped_it()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.AddMessage("INBOX", MimeSamples.Plain(subject: "Still here"));
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        await mailbox.ListFoldersAsync(Token);

        server.DropConnections();
        credentials.Time.Advance(TimeSpan.FromMinutes(1));
        var page = await mailbox.SearchAsync(new MailSearch(), Token);

        server.AssertHealthy();
        Assert.Equal("Still here", Assert.Single(page.Messages).Subject);
        Assert.Equal(2, server.ConnectionCount);
    }

    [Fact]
    public async Task Should_check_an_idle_connection_with_a_NOOP_and_keep_it_When_it_answers()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        await mailbox.ListFoldersAsync(Token);

        credentials.Time.Advance(TimeSpan.FromMinutes(1));
        await mailbox.ListFoldersAsync(Token);

        server.AssertHealthy();
        Assert.Equal(1, server.ConnectionCount);
        Assert.Contains("NOOP", server.Commands);
    }

    [Fact]
    public async Task Should_reuse_a_recently_used_connection_without_checking_it()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        await mailbox.ListFoldersAsync(Token);
        credentials.Time.Advance(TimeSpan.FromSeconds(10));
        await mailbox.ListFoldersAsync(Token);

        server.AssertHealthy();
        Assert.Equal(1, server.ConnectionCount);
        Assert.DoesNotContain("NOOP", server.Commands);
    }

    [Fact]
    public async Task Should_find_and_create_folders_under_an_INBOX_dot_namespace()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password) { Separator = '.', NamespacePrefix = "INBOX." };
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var (clients, created) = await mailbox.CreateFolderAsync("Clients", Token);
        var (_, createdAgain) = await mailbox.CreateFolderAsync("Clients", Token);
        var (acme, _) = await mailbox.CreateFolderAsync("Clients/ACME", Token);
        server.AddMessage("INBOX.Clients.ACME", MimeSamples.Plain(subject: "Contract"));
        var page = await mailbox.SearchAsync(new MailSearch { Folder = "Clients/ACME" }, Token);

        server.AssertHealthy();
        Assert.Equal(2, server.Commands.Count(c => c.StartsWith("CREATE", StringComparison.Ordinal)));
        Assert.True(created);
        Assert.False(createdAgain);
        Assert.Equal("INBOX/Clients", clients.Path);
        Assert.Equal("INBOX/Clients/ACME", acme.Path);
        Assert.Contains("INBOX.Clients.ACME", server.FolderNames);
        Assert.Equal("Contract", Assert.Single(page.Messages).Subject);
    }

    [Fact]
    public async Task Should_move_by_copy_mark_and_expunge_on_a_server_without_MOVE()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password, WithoutMove);
        server.WithStandardFolders();
        var uid = server.AddMessage("INBOX", MimeSamples.Plain(subject: "File me"));
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var moved = await mailbox.MoveAsync([MessageIds.Imap("INBOX", server.UidValidityOf("INBOX"), uid)], "archive", Token);

        server.AssertHealthy();
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("UID MOVE", StringComparison.Ordinal));
        Assert.Contains(server.Commands, c => c.StartsWith("UID EXPUNGE", StringComparison.Ordinal));
        Assert.Empty(server.UidsOf("INBOX"));
        Assert.Equal(MessageIds.Imap("Archive", server.UidValidityOf("Archive"), 1), Assert.Single(moved).NewId);
    }

    [Fact]
    public async Task Should_move_without_MOVE_or_UIDPLUS_through_MailKit_s_guarded_expunge()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password, WithoutMoveOrUidPlus);
        server.WithStandardFolders();
        var uid = server.AddMessage("INBOX", MimeSamples.Plain(subject: "File me"));
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var moved = await mailbox.MoveAsync([MessageIds.Imap("INBOX", server.UidValidityOf("INBOX"), uid)], "archive", Token);
        var inbox = await mailbox.SearchAsync(new MailSearch(), Token);
        var archive = await mailbox.SearchAsync(new MailSearch { Folder = "archive" }, Token);

        server.AssertHealthy();
        Assert.Null(Assert.Single(moved).NewId);
        // Without UIDPLUS, MailKit looks for the other messages already marked deleted before it
        // expunges, so that only the moved one goes.
        Assert.Contains(server.Commands, c => c.StartsWith("UID SEARCH DELETED NOT UID", StringComparison.Ordinal));
        Assert.Empty(server.UidsOf("INBOX"));
        Assert.Empty(inbox.Messages);
        Assert.Equal("File me", Assert.Single(archive.Messages).Subject);
    }

    [Fact]
    public async Task Should_not_list_a_message_another_client_marked_deleted()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.AddMessage("INBOX", MimeSamples.Plain(subject: "Kept"));
        server.AddMessage("INBOX", MimeSamples.Plain(subject: "On its way out"), deleted: true);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var page = await mailbox.SearchAsync(new MailSearch(), Token);

        server.AssertHealthy();
        Assert.Equal("Kept", Assert.Single(page.Messages).Subject);
    }

    [Fact]
    public async Task Should_save_a_draft_without_an_id_on_a_server_without_UIDPLUS()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password, WithoutMoveOrUidPlus);
        server.WithStandardFolders();
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        using var draft = MimeSamples.Load(MimeSamples.Plain(subject: "Proposal"));

        var (id, folder) = await mailbox.SaveDraftAsync(draft, Token);

        server.AssertHealthy();
        Assert.Null(id);
        Assert.Equal("Drafts", folder);
        Assert.Equal("Proposal", server.SubjectOf("Drafts", 1));
    }

    [Fact]
    public async Task Should_search_read_and_archive_on_a_server_that_advertises_what_Gmail_does()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password, FakeImapServer.GmailCapabilities);
        server.AddFolder("[Gmail]").AddFolder("[Gmail]/All Mail", "\\All").AddFolder("[Gmail]/Sent Mail", "\\Sent")
            .AddFolder("[Gmail]/Drafts", "\\Drafts").AddFolder("[Gmail]/Trash", "\\Trash").AddFolder("[Gmail]/Spam", "\\Junk");
        server.AddMessage("INBOX", MimeSamples.Plain(subject: "Plain one", body: "The plain text body of the first message."));
        server.AddMessage("INBOX", MimeSamples.Html("<html><body><p>An <b>HTML</b> newsletter body.</p></body></html>", subject: "HTML one"));
        server.AddMessage("INBOX", MimeSamples.WithAttachment());
        using var credentials = new CredentialsFixture();
        await using var mailbox = new ImapMailbox(
            TestAccounts.AsGmail(TestAccounts.Loopback(IncomingProtocol.Imap, server.Port)), new NetworkMailServiceConnector(), credentials.Provider, credentials.Time);

        var folders = await mailbox.ListFoldersAsync(Token);
        var page = await mailbox.SearchAsync(new MailSearch { Limit = 10 }, Token);
        using var read = await mailbox.GetMessageAsync(page.Messages[^1].Id, Token);
        var archived = await mailbox.MoveAsync([page.Messages[0].Id], "archive", Token);

        server.AssertHealthy();
        Assert.Contains(folders, folder => folder is { Path: "[Gmail]/All Mail", Role: "all" });
        Assert.Contains(server.Commands, c => c.StartsWith("UID SEARCH RETURN", StringComparison.Ordinal));
        Assert.Contains(server.Commands, c => c.Contains("BODY.PEEK[1]<0.", StringComparison.Ordinal));
        Assert.Equal(["Report attached", "HTML one", "Plain one"], page.Messages.Select(m => m.Subject));
        Assert.All(page.Messages, message => Assert.False(string.IsNullOrWhiteSpace(message.Preview)));
        Assert.Equal("Plain one", read.Message.Subject);
        Assert.StartsWith(MessageIds.Imap("[Gmail]/All Mail", server.UidValidityOf("[Gmail]/All Mail"), 1), Assert.Single(archived).NewId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_create_search_and_read_folders_and_mail_that_are_not_ASCII()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var (folder, _) = await mailbox.CreateFolderAsync("Réunions/Comité", Token);
        var serverName = Assert.Single(server.FolderNames, name => name.EndsWith("Comit&AOk-", StringComparison.Ordinal));
        server.AddMessage(serverName, MimeSamples.Plain(subject: "=?UTF-8?B?" + Convert.ToBase64String("Ordre du jour de la réunion"u8.ToArray()) + "?=", body: "Préparez les chiffres."));
        server.AddMessage(serverName, MimeSamples.Plain(subject: "Unrelated", body: "Nothing to see."));
        var page = await mailbox.SearchAsync(new MailSearch { Folder = "Réunions/Comité", Subject = "réunion" }, Token);

        server.AssertHealthy();
        Assert.Equal("Réunions/Comité", folder.Path);
        Assert.Equal("Ordre du jour de la réunion", Assert.Single(page.Messages).Subject);
        Assert.Contains(server.Commands, c => c.Contains("CHARSET UTF-8", StringComparison.Ordinal));
    }

    private static ImapMailbox Open(FakeImapServer server, CredentialsFixture credentials) =>
        new(TestAccounts.Loopback(IncomingProtocol.Imap, server.Port), new NetworkMailServiceConnector(), credentials.Provider, credentials.Time);
}
