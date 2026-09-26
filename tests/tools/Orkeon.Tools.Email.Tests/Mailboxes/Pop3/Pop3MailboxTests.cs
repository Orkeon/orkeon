using MimeKit;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Mailboxes.Pop3;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Mailboxes.Pop3;

/// <summary>
/// The POP3 backend driven through the production connector against a loopback
/// <see cref="FakePop3Server"/>: header search, reading, deletion at QUIT, and every refusal.
/// </summary>
public sealed class Pop3MailboxTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_list_the_inbox_alone_with_its_message_count()
    {
        await using var server = Seeded();
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);

        var folders = await mailbox.ListFoldersAsync(Token);

        server.AssertHealthy();
        Assert.Equal(new MailFolderInfo("INBOX", "INBOX", FolderRoles.Inbox, 3, null), Assert.Single(folders));
        Assert.Equal(MailboxCapabilities.None, mailbox.Capabilities);
    }

    [Fact]
    public async Task Should_search_the_headers_newest_first_and_page_with_a_cursor()
    {
        await using var server = Seeded();
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);

        var first = await mailbox.SearchAsync(new MailSearch { Limit = 1 }, Token);
        var second = await mailbox.SearchAsync(new MailSearch { Limit = 1, Cursor = first.NextCursor }, Token);
        var rest = await mailbox.SearchAsync(new MailSearch { Limit = 5, Cursor = second.NextCursor }, Token);

        server.AssertHealthy();
        var newest = Assert.Single(first.Messages);
        Assert.Equal("pop3:uid-3", newest.Id);
        Assert.Equal("Invoice 42", newest.Subject);
        Assert.Equal("Bob <bob@example.com>", newest.From);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 8, 0, 0, TimeSpan.Zero), newest.Date);
        Assert.Null(newest.Seen);
        Assert.Null(newest.Flagged);
        Assert.Equal("o:1", first.NextCursor);
        Assert.Equal("pop3:uid-2", Assert.Single(second.Messages).Id);
        Assert.Equal("pop3:uid-1", Assert.Single(rest.Messages).Id);
        Assert.Null(rest.NextCursor);
        Assert.Contains("TOP 3 0", server.Transcript);
        Assert.DoesNotContain(server.Transcript, line => line.StartsWith("RETR", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_hand_each_message_the_cursor_that_resumes_right_after_it()
    {
        await using var server = Seeded();
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);

        var page = await mailbox.SearchAsync(new MailSearch { Limit = 5 }, Token);
        var resumed = await mailbox.SearchAsync(new MailSearch { Limit = 5, Cursor = page.Messages[0].ResumeCursor }, Token);

        server.AssertHealthy();
        Assert.Equal(["o:1", "o:2", "o:3"], page.Messages.Select(m => m.ResumeCursor));
        Assert.Equal(["pop3:uid-2", "pop3:uid-1"], resumed.Messages.Select(m => m.Id));
    }

    [Theory]
    [InlineData("from", "bob", "pop3:uid-3")]
    [InlineData("subject", "LUNCH", "pop3:uid-2")]
    [InlineData("to", "team@", "pop3:uid-2")]
    [InlineData("to", "cc-person", "pop3:uid-1")]
    public async Task Should_match_from_to_cc_and_subject_case_insensitively(string criterion, string value, string expectedId)
    {
        await using var server = Seeded();
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);
        var search = criterion switch
        {
            "from" => new MailSearch { From = value },
            "subject" => new MailSearch { Subject = value },
            _ => new MailSearch { To = value },
        };

        var page = await mailbox.SearchAsync(search, Token);

        Assert.Equal(expectedId, Assert.Single(page.Messages).Id);
    }

    [Fact]
    public async Task Should_filter_on_dates_client_side()
    {
        await using var server = Seeded();
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);

        var page = await mailbox.SearchAsync(new MailSearch
        {
            Since = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero),
            Before = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
        }, Token);

        Assert.Equal("pop3:uid-2", Assert.Single(page.Messages).Id);
    }

    [Fact]
    public async Task Should_stop_after_the_scan_window_and_hand_back_a_cursor()
    {
        await using var server = new FakePop3Server(TestAccounts.Address, TestAccounts.Password);
        for (var i = 1; i <= 201; i++)
            server.Add($"uid-{i}", MimeSamples.Plain(subject: $"Newsletter {i}"));
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);

        var page = await mailbox.SearchAsync(new MailSearch { Subject = "no such subject" }, Token);

        Assert.Empty(page.Messages);
        Assert.Equal("o:200", page.NextCursor);
        Assert.Equal(200, server.Transcript.Count(line => line.StartsWith("TOP ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Should_read_a_message_by_its_UIDL_id()
    {
        await using var server = Seeded();
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);

        using var fetched = await mailbox.GetMessageAsync("pop3:uid-2", Token);

        server.AssertHealthy();
        Assert.Equal("pop3:uid-2", fetched.Id);
        Assert.Equal("INBOX", fetched.Folder);
        Assert.Equal("Lunch on Friday", fetched.Message.Subject);
        Assert.Null(fetched.Seen);
        Assert.Contains("RETR 2", server.Transcript);
    }

    [Fact]
    public async Task Should_report_MessageNotFound_When_the_uid_is_no_longer_on_the_server()
    {
        await using var server = Seeded();
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.GetMessageAsync("pop3:uid-99", Token));

        Assert.Equal(EmailErrorCode.MessageNotFound, error.Code);
        Assert.Equal("The message is no longer on the server: search again.", error.Message);
    }

    [Fact]
    public async Task Should_delete_permanently_and_commit_the_deletion_at_QUIT()
    {
        await using var server = Seeded();
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);

        var outcome = await mailbox.DeleteAsync(["pop3:uid-1", "pop3:uid-3"], permanent: true, Token);

        server.AssertHealthy();
        Assert.Equal(new DeleteOutcome(2, true, null), outcome);
        Assert.Equal(["uid-2"], server.Uids);
        var transcript = server.Transcript.ToList();
        Assert.True(transcript.LastIndexOf("QUIT") > transcript.IndexOf("DELE 3"));
    }

    [Fact]
    public async Task Should_open_a_new_session_for_every_operation()
    {
        await using var server = Seeded();
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);

        await mailbox.ListFoldersAsync(Token);
        await mailbox.SearchAsync(new MailSearch { Limit = 1 }, Token);

        Assert.Equal(2, server.ConnectionCount);
        Assert.Equal(2, server.Transcript.Count(line => line == "QUIT"));
    }

    [Fact]
    public async Task Should_sign_in_with_SASL_PLAIN_When_the_server_advertises_it()
    {
        await using var server = new FakePop3Server(TestAccounts.Address, TestAccounts.Password, advertiseSasl: true);
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);

        await mailbox.ListFoldersAsync(Token);

        server.AssertHealthy();
        Assert.Contains(server.Transcript, line => line.StartsWith("AUTH PLAIN", StringComparison.Ordinal));
        Assert.DoesNotContain(server.Transcript, line => line.StartsWith("USER", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_report_AuthenticationFailed_When_the_password_is_refused()
    {
        await using var server = new FakePop3Server(TestAccounts.Address, "the-real-password");
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.ListFoldersAsync(Token));

        Assert.Equal(EmailErrorCode.AuthenticationFailed, error.Code);
        Assert.Contains("refused the credentials of e-mail account 'local'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("create", "has no folders: switch the account to IMAP (Incoming:Protocol Imap) for that")]
    [InlineData("rename", "has no folders: switch the account to IMAP (Incoming:Protocol Imap) for that")]
    [InlineData("move", "has no folders: switch the account to IMAP (Incoming:Protocol Imap) for that")]
    [InlineData("mark", "has no read or flagged marks: switch the account to IMAP (Incoming:Protocol Imap) for that")]
    [InlineData("draft", "has no drafts folder: switch the account to IMAP (Incoming:Protocol Imap) for that")]
    [InlineData("trash", "has no trash and deletes for good: pass `permanent: true` (needs the Purge right)")]
    [InlineData("search-unread", "has no read or flagged marks: switch the account to IMAP (Incoming:Protocol Imap) for that")]
    [InlineData("search-flagged", "has no read or flagged marks: switch the account to IMAP (Incoming:Protocol Imap) for that")]
    [InlineData("search-text", "cannot search message bodies (`text`, `has_attachments`, `raw_query`): search on from, to, subject and dates, or switch the account to IMAP (Incoming:Protocol Imap)")]
    [InlineData("search-attachments", "cannot search message bodies (`text`, `has_attachments`, `raw_query`): search on from, to, subject and dates, or switch the account to IMAP (Incoming:Protocol Imap)")]
    [InlineData("search-raw", "cannot search message bodies (`text`, `has_attachments`, `raw_query`): search on from, to, subject and dates, or switch the account to IMAP (Incoming:Protocol Imap)")]
    [InlineData("search-folder", "only has the inbox: switch the account to IMAP (Incoming:Protocol Imap) for other folders")]
    public async Task Should_refuse_what_POP3_cannot_do_without_connecting(string operation, string clause)
    {
        await using var server = Seeded();
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);
        using var draft = new MimeMessage();

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await (operation switch
        {
            "create" => mailbox.CreateFolderAsync("Archive", Token),
            "rename" => mailbox.RenameFolderAsync("INBOX", "Other", Token),
            "move" => mailbox.MoveAsync(["pop3:uid-1"], "archive", Token),
            "mark" => mailbox.SetFlagsAsync(["pop3:uid-1"], true, null, Token),
            "draft" => mailbox.SaveDraftAsync(draft, Token),
            "trash" => mailbox.DeleteAsync(["pop3:uid-1"], permanent: false, Token),
            "search-unread" => mailbox.SearchAsync(new MailSearch { UnreadOnly = true }, Token),
            "search-flagged" => mailbox.SearchAsync(new MailSearch { FlaggedOnly = true }, Token),
            "search-text" => mailbox.SearchAsync(new MailSearch { Text = "invoice" }, Token),
            "search-attachments" => mailbox.SearchAsync(new MailSearch { HasAttachments = true }, Token),
            "search-raw" => mailbox.SearchAsync(new MailSearch { RawQuery = "anything" }, Token),
            _ => (Task)mailbox.SearchAsync(new MailSearch { Folder = "sent" }, Token),
        }));

        Assert.Equal(EmailErrorCode.Unsupported, error.Code);
        Assert.Equal($"E-mail account 'local' reads mail over POP3, which {clause}.", error.Message);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Theory]
    [InlineData("inbox")]
    [InlineData(" INBOX ")]
    public async Task Should_accept_the_inbox_by_role_or_by_name(string folder)
    {
        await using var server = Seeded();
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);

        var page = await mailbox.SearchAsync(new MailSearch { Folder = folder, Limit = 1 }, Token);

        Assert.Single(page.Messages);
    }

    [Fact]
    public async Task Should_refuse_a_cursor_it_did_not_issue()
    {
        await using var server = Seeded();
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.SearchAsync(new MailSearch { Cursor = "u:12" }, Token));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
    }

    [Fact]
    public async Task Should_ignore_a_request_to_file_a_sent_copy()
    {
        await using var server = Seeded();
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);
        using var message = new MimeMessage();

        await mailbox.AppendToSentAsync(message, Token);

        Assert.Equal(0, server.ConnectionCount);
    }

    private static FakePop3Server Seeded()
    {
        var server = new FakePop3Server(TestAccounts.Address, TestAccounts.Password);
        server.Add("uid-1", MimeSamples.Plain(subject: "Welcome", from: "Alice <alice@example.com>", date: "Sat, 19 Sep 2026 08:00:00 +0000",
            extraHeaders: "Cc: cc-person@example.com\r\n"));
        server.Add("uid-2", MimeSamples.Plain(subject: "Lunch on Friday", from: "carol@example.com", to: "team@example.com",
            date: "Sun, 20 Sep 2026 08:00:00 +0000"));
        server.Add("uid-3", MimeSamples.Plain(subject: "Invoice 42", from: "Bob <bob@example.com>", date: "Mon, 21 Sep 2026 08:00:00 +0000"));
        return server;
    }

    private static Pop3Mailbox Open(FakePop3Server server, CredentialsFixture credentials) =>
        new(TestAccounts.Loopback(IncomingProtocol.Pop3, server.Port), new NetworkMailServiceConnector(), credentials.Provider);
}
