using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Mailboxes.Imap;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Mailboxes.Imap;

/// <summary>
/// The IMAP backend's message operations against a loopback <see cref="FakeImapServer"/>:
/// search and paging, fetch, move, marks, delete, drafts and the Gmail specifics.
/// </summary>
public sealed class ImapMailboxMessageTests
{
    private const string GmailCapabilities = "IMAP4rev1 AUTH=PLAIN SASL-IR UIDPLUS MOVE SPECIAL-USE NAMESPACE PREVIEW X-GM-EXT-1";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_search_newest_first_and_page_with_an_opaque_cursor()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        for (var i = 1; i <= 5; i++)
            server.AddMessage("INBOX", MimeSamples.Plain(subject: $"Message {i}"));
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        var validity = server.UidValidityOf("INBOX");

        var first = await mailbox.SearchAsync(new MailSearch { Limit = 2 }, Token);
        var second = await mailbox.SearchAsync(new MailSearch { Limit = 2, Cursor = first.NextCursor }, Token);
        var third = await mailbox.SearchAsync(new MailSearch { Limit = 2, Cursor = second.NextCursor }, Token);

        server.AssertHealthy();
        Assert.Equal([MessageIds.Imap("INBOX", validity, 5), MessageIds.Imap("INBOX", validity, 4)], first.Messages.Select(m => m.Id));
        Assert.Equal("u:4", first.NextCursor);
        Assert.Equal(["Message 3", "Message 2"], second.Messages.Select(m => m.Subject));
        Assert.Equal("u:2", second.NextCursor);
        Assert.Equal(["Message 1"], third.Messages.Select(m => m.Subject));
        Assert.Null(third.NextCursor);
    }

    [Fact]
    public async Task Should_hand_each_message_the_cursor_that_resumes_right_after_it()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        for (var i = 1; i <= 5; i++)
            server.AddMessage("INBOX", MimeSamples.Plain(subject: $"Message {i}"));
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var page = await mailbox.SearchAsync(new MailSearch { Limit = 4 }, Token);
        var resumed = await mailbox.SearchAsync(new MailSearch { Limit = 4, Cursor = page.Messages[1].ResumeCursor }, Token);

        server.AssertHealthy();
        Assert.Equal(["u:5", "u:4", "u:3", "u:2"], page.Messages.Select(m => m.ResumeCursor));
        Assert.Equal(["Message 3", "Message 2", "Message 1"], resumed.Messages.Select(m => m.Subject));
    }

    [Fact]
    public async Task Should_summarize_sender_subject_date_marks_attachments_and_preview()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.AddMessage("INBOX", MimeSamples.Plain(body: "Hello   there,\r\n\r\nthe   figures are attached."), seen: true, flagged: true);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var summary = Assert.Single((await mailbox.SearchAsync(new MailSearch(), Token)).Messages);

        Assert.Equal("Alice Martin <alice@example.com>", summary.From);
        Assert.Equal("Quarterly figures", summary.Subject);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 10, 15, 0, TimeSpan.Zero), summary.Date);
        Assert.True(summary.Seen);
        Assert.True(summary.Flagged);
        Assert.False(summary.HasAttachments);
        Assert.Equal("Hello there, the figures are attached.", summary.Preview);
    }

    [Fact]
    public async Task Should_translate_every_criterion_into_one_IMAP_search()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        var match = server.AddMessage("INBOX", MimeSamples.Plain(subject: "Budget figures", body: "The budget is ready."),
            flagged: true, internalDate: new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero));
        server.AddMessage("INBOX", MimeSamples.Plain(subject: "Budget figures", body: "The budget is ready."), seen: true, flagged: true,
            internalDate: new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero));
        server.AddMessage("INBOX", MimeSamples.Plain(subject: "Budget figures", body: "The budget is ready."), flagged: true,
            internalDate: new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero));
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var page = await mailbox.SearchAsync(new MailSearch
        {
            UnreadOnly = true,
            FlaggedOnly = true,
            From = "alice",
            To = "agent",
            Subject = "figures",
            Text = "budget",
            Since = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            Before = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
        }, Token);

        server.AssertHealthy();
        var search = Assert.Single(server.Commands, c => c.StartsWith("UID SEARCH", StringComparison.Ordinal));
        Assert.Equal("UID SEARCH UNSEEN FLAGGED FROM alice TO agent SUBJECT figures OR SUBJECT budget BODY budget SINCE 1-Sep-2026 BEFORE 30-Sep-2026", search);
        Assert.Equal(match, uint.Parse(Assert.Single(page.Messages).Id.Split(':')[^1], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(true, "Report attached")]
    [InlineData(false, "Quarterly figures")]
    public async Task Should_filter_on_attachments_after_reading_the_body_structure(bool wanted, string expectedSubject)
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.AddMessage("INBOX", MimeSamples.Plain());
        server.AddMessage("INBOX", MimeSamples.WithAttachment());
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var page = await mailbox.SearchAsync(new MailSearch { HasAttachments = wanted }, Token);

        var summary = Assert.Single(page.Messages);
        Assert.Equal(expectedSubject, summary.Subject);
        Assert.Equal(wanted, summary.HasAttachments);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Should_search_a_folder_named_by_role_or_by_path()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.WithStandardFolders().AddFolder("Clients").AddFolder("Clients/ACME");
        server.AddMessage("Archive", MimeSamples.Plain(subject: "Archived"));
        server.AddMessage("Clients/ACME", MimeSamples.Plain(subject: "From ACME"));
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var archived = await mailbox.SearchAsync(new MailSearch { Folder = "ARCHIVE" }, Token);
        var client = await mailbox.SearchAsync(new MailSearch { Folder = "/Clients/ACME/" }, Token);

        Assert.Equal("Archived", Assert.Single(archived.Messages).Subject);
        Assert.StartsWith(MessageIds.Imap("Clients/ACME", server.UidValidityOf("Clients/ACME"), 1), Assert.Single(client.Messages).Id, StringComparison.Ordinal);
        Assert.Contains("EXAMINE Archive", server.Commands);
    }

    [Fact]
    public async Task Should_report_FolderNotFound_for_a_missing_path_or_role()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var path = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.SearchAsync(new MailSearch { Folder = "Nowhere" }, Token));
        var role = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.SearchAsync(new MailSearch { Folder = "junk" }, Token));

        Assert.Equal(EmailErrorCode.FolderNotFound, path.Code);
        Assert.Equal("The folder 'Nowhere' does not exist: list them with email_folders.", path.Message);
        Assert.Equal(EmailErrorCode.FolderNotFound, role.Code);
        Assert.Equal("This account has no junk folder: list them with email_folders.", role.Message);
    }

    [Theory]
    [InlineData("n:abc")]
    [InlineData("u:-3")]
    [InlineData("u:")]
    public async Task Should_refuse_a_cursor_it_did_not_issue(string cursor)
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.SearchAsync(new MailSearch { Cursor = cursor }, Token));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.StartsWith("`cursor` is not a cursor of this account", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_refuse_raw_query_When_the_server_has_no_native_search_language()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.SearchAsync(new MailSearch { RawQuery = "has:attachment" }, Token));

        Assert.Equal(EmailErrorCode.Unsupported, error.Code);
        Assert.Contains("X-GM-RAW", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("UID SEARCH", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_pass_raw_query_as_X_GM_RAW_When_the_server_is_Gmail()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password, GmailCapabilities);
        server.AddMessage("INBOX", MimeSamples.Plain());
        server.AddMessage("INBOX", MimeSamples.WithAttachment());
        using var credentials = new CredentialsFixture();
        await using var mailbox = new ImapMailbox(
            TestAccounts.AsGmail(TestAccounts.Loopback(IncomingProtocol.Imap, server.Port)), new NetworkMailServiceConnector(), credentials.Provider, credentials.Time);

        var page = await mailbox.SearchAsync(new MailSearch { RawQuery = " has:attachment " }, Token);

        server.AssertHealthy();
        Assert.Equal(MailboxCapabilities.RawQuery, mailbox.Capabilities & MailboxCapabilities.RawQuery);
        Assert.Contains(server.Commands, c => c.StartsWith("UID SEARCH X-GM-RAW", StringComparison.Ordinal) && c.Contains("has:attachment", StringComparison.Ordinal));
        Assert.Equal("Report attached", Assert.Single(page.Messages).Subject);
    }

    [Fact]
    public async Task Should_fetch_a_message_whole_without_marking_it_read()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        var uid = server.AddMessage("INBOX", MimeSamples.Plain(), flagged: true);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        var id = MessageIds.Imap("INBOX", server.UidValidityOf("INBOX"), uid);

        using var fetched = await mailbox.GetMessageAsync(id, Token);

        server.AssertHealthy();
        Assert.Equal(id, fetched.Id);
        Assert.Equal("INBOX", fetched.Folder);
        Assert.Equal("Quarterly figures", fetched.Message.Subject);
        Assert.False(fetched.Seen);
        Assert.True(fetched.Flagged);
        Assert.Contains(server.Commands, c => c.EndsWith("(BODY.PEEK[])", StringComparison.Ordinal));
        Assert.DoesNotContain("\\Seen", server.FlagsOf("INBOX", uid));
    }

    [Fact]
    public async Task Should_report_MessageNotFound_When_the_folder_was_rebuilt_since_the_id_was_issued()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        var uid = server.AddMessage("INBOX", MimeSamples.Plain());
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await mailbox.GetMessageAsync(MessageIds.Imap("INBOX", server.UidValidityOf("INBOX") + 7, uid), Token));

        Assert.Equal(EmailErrorCode.MessageNotFound, error.Code);
        Assert.Contains("search again", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_report_MessageNotFound_When_the_uid_is_gone()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await mailbox.GetMessageAsync(MessageIds.Imap("INBOX", server.UidValidityOf("INBOX"), 99), Token));

        Assert.Equal(EmailErrorCode.MessageNotFound, error.Code);
    }

    [Fact]
    public async Task Should_refuse_an_id_issued_by_another_backend()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.GetMessageAsync("graph:AAMkAD", Token));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.Equal("'graph:AAMkAD' is not a message id of this account: pass an id exactly as email_search returned it.", error.Message);
    }

    [Fact]
    public async Task Should_move_messages_and_return_their_new_ids_from_COPYUID()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.WithStandardFolders();
        server.AddMessage("Archive", MimeSamples.Plain(subject: "Already archived"));
        var first = server.AddMessage("INBOX", MimeSamples.Plain(subject: "One"));
        var second = server.AddMessage("INBOX", MimeSamples.Plain(subject: "Two"));
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        var inbox = server.UidValidityOf("INBOX");
        var archive = server.UidValidityOf("Archive");

        var moved = await mailbox.MoveAsync([MessageIds.Imap("INBOX", inbox, first), MessageIds.Imap("INBOX", inbox, second)], "archive", Token);

        server.AssertHealthy();
        Assert.Equal(
            [(MessageIds.Imap("INBOX", inbox, first), MessageIds.Imap("Archive", archive, 2)), (MessageIds.Imap("INBOX", inbox, second), MessageIds.Imap("Archive", archive, 3))],
            moved.Select(m => (m.Id, m.NewId!)));
        Assert.Empty(server.UidsOf("INBOX"));
        Assert.Equal("Two", server.SubjectOf("Archive", 3));
        Assert.Contains(server.Commands, c => c.StartsWith("UID MOVE", StringComparison.Ordinal) && c.EndsWith("Archive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_keep_the_ids_without_moving_When_the_destination_is_the_source_folder()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        var uid = server.AddMessage("INBOX", MimeSamples.Plain());
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        var id = MessageIds.Imap("INBOX", server.UidValidityOf("INBOX"), uid);

        var moved = await mailbox.MoveAsync([id], "inbox", Token);

        Assert.Equal(id, Assert.Single(moved).NewId);
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("UID MOVE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_set_and_clear_the_read_and_flagged_marks()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        var first = server.AddMessage("INBOX", MimeSamples.Plain(), flagged: true);
        var second = server.AddMessage("INBOX", MimeSamples.Plain(), flagged: true);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        var validity = server.UidValidityOf("INBOX");

        var updated = await mailbox.SetFlagsAsync([MessageIds.Imap("INBOX", validity, first), MessageIds.Imap("INBOX", validity, second)], seen: true, flagged: false, Token);

        server.AssertHealthy();
        Assert.Equal(2, updated);
        Assert.Equal(["\\Seen"], server.FlagsOf("INBOX", first));
        Assert.Equal(["\\Seen"], server.FlagsOf("INBOX", second));
        Assert.Contains(server.Commands, c => c.EndsWith("+FLAGS.SILENT (\\Seen)", StringComparison.Ordinal));
        Assert.Contains(server.Commands, c => c.EndsWith("-FLAGS.SILENT (\\Flagged)", StringComparison.Ordinal));
        Assert.Contains("SELECT INBOX", server.Commands);
    }

    [Fact]
    public async Task Should_move_deleted_messages_to_the_trash()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.WithStandardFolders();
        var uid = server.AddMessage("INBOX", MimeSamples.Plain());
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var outcome = await mailbox.DeleteAsync([MessageIds.Imap("INBOX", server.UidValidityOf("INBOX"), uid)], permanent: false, Token);

        server.AssertHealthy();
        Assert.Equal(new DeleteOutcome(1, false, "Trash"), outcome);
        Assert.Empty(server.UidsOf("INBOX"));
        Assert.Single(server.UidsOf("Trash"));
    }

    [Fact]
    public async Task Should_refuse_a_trash_delete_of_messages_already_in_the_trash()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.WithStandardFolders();
        var uid = server.AddMessage("Trash", MimeSamples.Plain());
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await mailbox.DeleteAsync([MessageIds.Imap("Trash", server.UidValidityOf("Trash"), uid)], permanent: false, Token));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.Contains("already in the trash: pass `permanent: true`", error.Message, StringComparison.Ordinal);
        Assert.Single(server.UidsOf("Trash"));
    }

    [Fact]
    public async Task Should_refuse_a_trash_delete_When_the_server_has_no_trash()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        var uid = server.AddMessage("INBOX", MimeSamples.Plain());
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await mailbox.DeleteAsync([MessageIds.Imap("INBOX", server.UidValidityOf("INBOX"), uid)], permanent: false, Token));

        Assert.Equal(EmailErrorCode.Unsupported, error.Code);
        Assert.StartsWith("This server declares no trash folder", error.Message, StringComparison.Ordinal);
        Assert.Single(server.UidsOf("INBOX"));
    }

    [Fact]
    public async Task Should_expunge_only_the_given_uids_When_deleting_permanently()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        var doomed = server.AddMessage("INBOX", MimeSamples.Plain(subject: "Doomed"));
        var bystander = server.AddMessage("INBOX", MimeSamples.Plain(subject: "Marked deleted by another client"));
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        var validity = server.UidValidityOf("INBOX");
        server.MarkDeleted("INBOX", bystander);

        var outcome = await mailbox.DeleteAsync([MessageIds.Imap("INBOX", validity, doomed)], permanent: true, Token);

        server.AssertHealthy();
        Assert.Equal(new DeleteOutcome(1, true, null), outcome);
        Assert.Equal([bystander], server.UidsOf("INBOX"));
        Assert.Contains(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"UID EXPUNGE {doomed}"), server.Commands);
    }

    [Fact]
    public async Task Should_refuse_a_permanent_delete_When_the_server_lacks_UIDPLUS()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password, "IMAP4rev1 AUTH=PLAIN SASL-IR MOVE SPECIAL-USE NAMESPACE PREVIEW");
        var uid = server.AddMessage("INBOX", MimeSamples.Plain());
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await mailbox.DeleteAsync([MessageIds.Imap("INBOX", server.UidValidityOf("INBOX"), uid)], permanent: true, Token));

        Assert.Equal(EmailErrorCode.Unsupported, error.Code);
        Assert.Contains("lacks UIDPLUS", error.Message, StringComparison.Ordinal);
        Assert.Single(server.UidsOf("INBOX"));
    }

    [Fact]
    public async Task Should_purge_through_the_trash_on_Gmail_where_expunging_a_label_only_archives()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password, GmailCapabilities);
        server.AddFolder("[Gmail]").AddFolder("[Gmail]/Trash", "\\Trash");
        var uid = server.AddMessage("INBOX", MimeSamples.Plain());
        using var credentials = new CredentialsFixture();
        await using var mailbox = new ImapMailbox(
            TestAccounts.AsGmail(TestAccounts.Loopback(IncomingProtocol.Imap, server.Port)), new NetworkMailServiceConnector(), credentials.Provider, credentials.Time);

        var outcome = await mailbox.DeleteAsync([MessageIds.Imap("INBOX", server.UidValidityOf("INBOX"), uid)], permanent: true, Token);

        server.AssertHealthy();
        Assert.Equal(new DeleteOutcome(1, true, null), outcome);
        Assert.Empty(server.UidsOf("INBOX"));
        Assert.Empty(server.UidsOf("[Gmail]/Trash"));
        var moveIndex = server.Commands.ToList().FindIndex(c => c.StartsWith("UID MOVE", StringComparison.Ordinal));
        var expungeIndex = server.Commands.ToList().FindIndex(c => c.StartsWith("UID EXPUNGE", StringComparison.Ordinal));
        Assert.InRange(moveIndex, 0, expungeIndex - 1);
    }

    [Fact]
    public async Task Should_save_a_draft_flagged_as_draft_and_return_its_APPENDUID_id()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.WithStandardFolders();
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        using var draft = MimeSamples.Load(MimeSamples.Plain(subject: "Proposal draft"));

        var (id, folder) = await mailbox.SaveDraftAsync(draft, Token);

        server.AssertHealthy();
        Assert.Equal("Drafts", folder);
        Assert.Equal(MessageIds.Imap("Drafts", server.UidValidityOf("Drafts"), 1), id);
        Assert.Equal("Proposal draft", server.SubjectOf("Drafts", 1));
        Assert.True(server.FlagsOf("Drafts", 1).SetEquals(["\\Draft", "\\Seen"]));
    }

    [Fact]
    public async Task Should_refuse_a_draft_When_the_account_has_no_drafts_folder()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        using var draft = MimeSamples.Load(MimeSamples.Plain());

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.SaveDraftAsync(draft, Token));

        Assert.Equal(EmailErrorCode.Unsupported, error.Code);
        Assert.Equal("This account has no drafts folder.", error.Message);
    }

    [Fact]
    public async Task Should_file_a_read_copy_in_the_Sent_folder()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.WithStandardFolders();
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        using var sent = MimeSamples.Load(MimeSamples.Plain(subject: "Sent one"));

        await mailbox.AppendToSentAsync(sent, Token);

        Assert.Equal("Sent one", server.SubjectOf("Sent", 1));
        Assert.Equal(["\\Seen"], server.FlagsOf("Sent", 1));
    }

    [Fact]
    public async Task Should_say_there_is_no_Sent_folder_instead_of_skipping_the_copy()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        using var sent = MimeSamples.Load(MimeSamples.Plain());

        var error = await Assert.ThrowsAsync<EmailToolException>(() => mailbox.AppendToSentAsync(sent, Token));

        Assert.Equal(EmailErrorCode.FolderNotFound, error.Code);
        Assert.Equal("This account has no Sent folder to file a copy in: an operator creates one named Sent, or sets SaveSentCopy to false.", error.Message);
        server.AssertHealthy();
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("APPEND", StringComparison.Ordinal));
    }

    private static ImapMailbox Open(FakeImapServer server, CredentialsFixture credentials) =>
        new(TestAccounts.Loopback(IncomingProtocol.Imap, server.Port), new NetworkMailServiceConnector(), credentials.Provider, credentials.Time);
}
