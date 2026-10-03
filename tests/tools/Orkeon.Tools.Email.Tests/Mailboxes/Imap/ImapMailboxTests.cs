using Orkeon.Tests.Shared.Network;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Mailboxes.Imap;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Mailboxes.Imap;

/// <summary>
/// The IMAP backend driven through the production connector against a loopback
/// <see cref="FakeImapServer"/>: connection, authentication, folders and failure translation.
/// </summary>
public sealed class ImapMailboxTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_list_folders_with_roles_and_counts_inbox_first()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.WithStandardFolders().AddFolder("Clients").AddFolder("Clients/ACME");
        server.AddMessage("INBOX", MimeSamples.Plain(), seen: true);
        server.AddMessage("INBOX", MimeSamples.Plain(subject: "Unread one"));
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var folders = await mailbox.ListFoldersAsync(Token);

        server.AssertHealthy();
        Assert.Equal("INBOX", folders[0].Path);
        Assert.Equal(FolderRoles.Inbox, folders[0].Role);
        Assert.Equal(2, folders[0].Total);
        Assert.Equal(1, folders[0].Unread);
        Assert.Equal(FolderRoles.Sent, folders.Single(f => f.Path == "Sent").Role);
        Assert.Equal(FolderRoles.Drafts, folders.Single(f => f.Path == "Drafts").Role);
        Assert.Equal(FolderRoles.Trash, folders.Single(f => f.Path == "Trash").Role);
        Assert.Equal(FolderRoles.Junk, folders.Single(f => f.Path == "Junk").Role);
        Assert.Equal(FolderRoles.Archive, folders.Single(f => f.Path == "Archive").Role);
        var child = folders.Single(f => f.Path == "Clients/ACME");
        Assert.Equal("ACME", child.Name);
        Assert.Null(child.Role);
        Assert.Equal(["Archive", "Clients", "Clients/ACME", "Drafts", "Junk", "Sent", "Trash"], folders.Skip(1).Select(f => f.Path));
    }

    [Fact]
    public async Task Should_sign_in_with_the_password_read_from_the_named_environment_variable()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        await mailbox.ListFoldersAsync(Token);

        server.AssertHealthy();
        Assert.Equal(["PLAIN"], server.Authentications);
    }

    [Fact]
    public async Task Should_answer_the_AUTHENTICATE_continuation_When_the_server_lacks_SASL_IR()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password, "IMAP4rev1 AUTH=PLAIN UIDPLUS MOVE SPECIAL-USE NAMESPACE PREVIEW");
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var folders = await mailbox.ListFoldersAsync(Token);

        server.AssertHealthy();
        Assert.Contains(server.Commands, command => command == "AUTHENTICATE PLAIN");
        Assert.Equal("INBOX", Assert.Single(folders).Path);
    }

    [Fact]
    public async Task Should_sign_in_with_XOAUTH2_and_the_stored_token_When_the_account_uses_OAuth2()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password) { AccessToken = "ya29.stored-token" };
        using var credentials = new CredentialsFixture();
        var account = TestAccounts.AsGmailOAuth(TestAccounts.Loopback(IncomingProtocol.Imap, server.Port));
        credentials.SeedFreshToken(account, "ya29.stored-token");
        await using var mailbox = new ImapMailbox(account, new NetworkMailServiceConnector(), credentials.Provider, credentials.Time);

        await mailbox.ListFoldersAsync(Token);

        server.AssertHealthy();
        Assert.Equal(["XOAUTH2"], server.Authentications);
        Assert.Empty(credentials.Handler.Requests);
    }

    [Fact]
    public async Task Should_refresh_and_sign_in_again_When_the_server_refuses_a_token_that_looked_fresh()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password) { AccessToken = "ya29.renewed" };
        using var credentials = new CredentialsFixture();
        var account = TestAccounts.AsGmailOAuth(TestAccounts.Loopback(IncomingProtocol.Imap, server.Port));
        credentials.SeedFreshToken(account, "ya29.revoked");
        credentials.Handler.EnqueueJson("""{"access_token":"ya29.renewed","expires_in":3599}""");
        await using var mailbox = new ImapMailbox(account, new NetworkMailServiceConnector(), credentials.Provider, credentials.Time);

        await mailbox.ListFoldersAsync(Token);

        server.AssertHealthy();
        Assert.Equal(["XOAUTH2", "XOAUTH2"], server.Authentications);
        Assert.Equal("refresh_token", Assert.Single(credentials.Handler.Requests).Form["grant_type"]);
        Assert.Equal("ya29.renewed", credentials.Store.Tokens.Single().Value.AccessToken);
    }

    [Fact]
    public async Task Should_report_AuthenticationFailed_with_the_app_password_hint_When_Gmail_refuses_the_password()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, "the-real-password");
        using var credentials = new CredentialsFixture();
        var account = TestAccounts.AsGmail(TestAccounts.Loopback(IncomingProtocol.Imap, server.Port));
        await using var mailbox = new ImapMailbox(account, new NetworkMailServiceConnector(), credentials.Provider, credentials.Time);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.ListFoldersAsync(Token));

        Assert.Equal(EmailErrorCode.AuthenticationFailed, error.Code);
        Assert.Contains($"127.0.0.1:{server.Port} refused the credentials of e-mail account 'local'", error.Message, StringComparison.Ordinal);
        Assert.Contains("app password", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_report_AuthenticationFailed_with_the_generic_hint_When_a_custom_server_refuses_the_password()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, "the-real-password");
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.ListFoldersAsync(Token));

        Assert.Equal(EmailErrorCode.AuthenticationFailed, error.Code);
        Assert.Contains("Check the user name and the password variable.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_fail_with_CredentialMissing_before_dialling_When_the_password_variable_is_unset()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture(TestAccounts.Environment());
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.ListFoldersAsync(Token));

        Assert.Equal(EmailErrorCode.CredentialMissing, error.Code);
        Assert.Contains(TestAccounts.PasswordVariable, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    public async Task Should_report_ServerError_When_nothing_listens_on_the_port()
    {
        // A port bound and listened on by nobody, held for the test (GAP-41): the port a closed
        // server gives back is anyone's, and a process listening there accepts the connection the
        // test expects refused.
        using var refusing = LoopbackPorts.Refusing();
        using var credentials = new CredentialsFixture();
        await using var mailbox = new ImapMailbox(
            TestAccounts.Loopback(IncomingProtocol.Imap, refusing.Port), new NetworkMailServiceConnector(), credentials.Provider, credentials.Time);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.ListFoldersAsync(Token));

        Assert.Equal(EmailErrorCode.ServerError, error.Code);
        Assert.Contains($"The connection to 127.0.0.1:{refusing.Port} failed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_reuse_one_connection_and_reconnect_after_five_idle_minutes()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        await mailbox.ListFoldersAsync(Token);
        credentials.Time.Advance(TimeSpan.FromMinutes(4));
        await mailbox.ListFoldersAsync(Token);
        Assert.Equal(1, server.ConnectionCount);

        credentials.Time.Advance(TimeSpan.FromMinutes(6));
        await mailbox.ListFoldersAsync(Token);

        Assert.Equal(2, server.ConnectionCount);
        Assert.Contains("LOGOUT", server.Commands);
        server.AssertHealthy();
    }

    [Fact]
    public async Task Should_replace_the_connection_a_cancelled_command_broke()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        await mailbox.ListFoldersAsync(Token);
        server.StallOn = "UID SEARCH";
        using var impatient = CancellationTokenSource.CreateLinkedTokenSource(Token);
        impatient.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await mailbox.SearchAsync(new MailSearch(), impatient.Token));
        server.StallOn = null;
        var page = await mailbox.SearchAsync(new MailSearch(), Token);

        Assert.Empty(page.Messages);
        Assert.Equal(2, server.ConnectionCount);
    }

    [Fact]
    public async Task Should_report_a_server_that_hangs_up_and_reconnect_on_the_next_call()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        await mailbox.ListFoldersAsync(Token);
        server.HangUpOn = "EXAMINE";

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.SearchAsync(new MailSearch(), Token));
        server.HangUpOn = null;
        var page = await mailbox.SearchAsync(new MailSearch(), Token);

        Assert.Equal(EmailErrorCode.ServerError, error.Code);
        Assert.Contains($"127.0.0.1:{server.Port}", error.Message, StringComparison.Ordinal);
        Assert.Empty(page.Messages);
        Assert.Equal(2, server.ConnectionCount);
    }

    [Fact]
    public async Task Should_log_out_When_disposed()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);
        await mailbox.ListFoldersAsync(Token);

        await mailbox.DisposeAsync();

        Assert.Equal("LOGOUT", server.Commands[^1]);
    }

    [Fact]
    public async Task Should_drop_the_connection_without_a_goodbye_When_disposed_synchronously()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        var mailbox = Open(server, credentials);
        await mailbox.ListFoldersAsync(Token);

        DisposeLikeASynchronousContainer(mailbox);

        Assert.DoesNotContain("LOGOUT", server.Commands);
        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact]
    public async Task Should_create_the_folder_and_each_missing_parent_one_level_at_a_time()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var (folder, created) = await mailbox.CreateFolderAsync("/Clients/ACME/2026/", Token);

        server.AssertHealthy();
        Assert.True(created);
        Assert.Equal("Clients/ACME/2026", folder.Path);
        Assert.Equal("2026", folder.Name);
        Assert.Equal(["CREATE Clients", "CREATE Clients/ACME", "CREATE Clients/ACME/2026"], server.Commands.Where(c => c.StartsWith("CREATE", StringComparison.Ordinal)));
        Assert.Contains("Clients/ACME/2026", server.FolderNames);
    }

    [Fact]
    public async Task Should_answer_created_false_without_any_CREATE_When_the_folder_exists()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.AddFolder("Clients");
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var (folder, created) = await mailbox.CreateFolderAsync("Clients", Token);

        Assert.False(created);
        Assert.Equal("Clients", folder.Path);
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("CREATE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Clients//ACME")]
    [InlineData("/")]
    [InlineData("Clients/\u0007bell")]
    public async Task Should_refuse_a_folder_path_with_an_empty_or_control_segment(string path)
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.CreateFolderAsync(path, Token));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.Contains("is not a folder path", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_rename_the_last_segment_of_a_folder()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.AddFolder("Clients").AddFolder("Clients/ACME");
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var folder = await mailbox.RenameFolderAsync("Clients/ACME", " Acme Corp ", Token);

        server.AssertHealthy();
        Assert.Equal("Clients/Acme Corp", folder.Path);
        Assert.Equal("Acme Corp", folder.Name);
        Assert.Contains("Clients/Acme Corp", server.FolderNames);
        Assert.DoesNotContain("Clients/ACME", server.FolderNames);
    }

    [Theory]
    [InlineData("INBOX", "inbox")]
    [InlineData("sent", "sent")]
    [InlineData("Trash", "trash")]
    public async Task Should_refuse_to_rename_a_system_folder(string path, string role)
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.WithStandardFolders();
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.RenameFolderAsync(path, "Other", Token));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.Equal($"The {role} folder is a system folder and cannot be renamed.", error.Message);
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("RENAME", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("A/B")]
    [InlineData("   ")]
    public async Task Should_refuse_a_new_name_that_is_empty_or_holds_a_separator(string newName)
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.AddFolder("Clients");
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.RenameFolderAsync("Clients", newName, Token));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.Contains("`new_name` is a single folder name", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_report_FolderNotFound_When_renaming_a_missing_folder()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.RenameFolderAsync("Nowhere", "Else", Token));

        Assert.Equal(EmailErrorCode.FolderNotFound, error.Code);
        Assert.Equal("The folder 'Nowhere' does not exist: list them with email_folders.", error.Message);
    }

    [Fact]
    public async Task Should_translate_a_refused_command_into_ServerError()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.AddFolder("Clients").AddFolder("Clients/ACME").AddFolder("Clients/Taken");
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.RenameFolderAsync("Clients/ACME", "Taken", Token));

        Assert.Equal(EmailErrorCode.ServerError, error.Code);
        Assert.Contains($"127.0.0.1:{server.Port} refused the command", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_list_roles_from_conventional_names_When_the_server_flags_no_folder()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password, "IMAP4rev1 AUTH=PLAIN SASL-IR UIDPLUS MOVE NAMESPACE PREVIEW");
        server.AddFolder("Sent Items").AddFolder("Drafts").AddFolder("Deleted Items").AddFolder("Spam").AddFolder("Archives").AddFolder("Clients");
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var roles = (await mailbox.ListFoldersAsync(Token)).ToDictionary(folder => folder.Path, folder => folder.Role, StringComparer.Ordinal);
        var rename = await Assert.ThrowsAsync<EmailToolException>(async () => await mailbox.RenameFolderAsync("Sent Items", "Outbox", Token));

        server.AssertHealthy();
        Assert.Equal(FolderRoles.Inbox, roles["INBOX"]);
        Assert.Equal(FolderRoles.Sent, roles["Sent Items"]);
        Assert.Equal(FolderRoles.Drafts, roles["Drafts"]);
        Assert.Equal(FolderRoles.Trash, roles["Deleted Items"]);
        Assert.Equal(FolderRoles.Junk, roles["Spam"]);
        Assert.Equal(FolderRoles.Archive, roles["Archives"]);
        Assert.Null(roles["Clients"]);
        Assert.Equal("The sent folder is a system folder and cannot be renamed.", rename.Message);
    }

    [Fact]
    public async Task Should_trust_only_the_flags_When_the_server_speaks_SPECIAL_USE()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        server.AddFolder("Sent").AddFolder("Sent Mail", "\\Sent");
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);

        var roles = (await mailbox.ListFoldersAsync(Token)).ToDictionary(folder => folder.Path, folder => folder.Role, StringComparer.Ordinal);

        Assert.Null(roles["Sent"]);
        Assert.Equal(FolderRoles.Sent, roles["Sent Mail"]);
    }

    [Fact]
    public async Task Should_find_role_folders_by_conventional_names_When_the_server_lacks_SPECIAL_USE()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password, "IMAP4rev1 AUTH=PLAIN SASL-IR UIDPLUS MOVE NAMESPACE PREVIEW");
        server.AddFolder("Drafts").AddFolder("Deleted Items");
        var uid = server.AddMessage("INBOX", MimeSamples.Plain());
        using var credentials = new CredentialsFixture();
        await using var mailbox = Open(server, credentials);
        using var draft = MimeSamples.Load(MimeSamples.Plain(subject: "A draft"));

        var (draftId, draftFolder) = await mailbox.SaveDraftAsync(draft, Token);
        var outcome = await mailbox.DeleteAsync([MessageIds.Imap("INBOX", server.UidValidityOf("INBOX"), uid)], permanent: false, Token);

        server.AssertHealthy();
        Assert.Equal("Drafts", draftFolder);
        Assert.NotNull(draftId);
        Assert.Equal("Deleted Items", outcome.MovedTo);
        Assert.Single(server.UidsOf("Deleted Items"));
    }

    /// <summary>What a DI container disposed synchronously does: <see cref="IDisposable.Dispose"/>, no await.</summary>
    private static void DisposeLikeASynchronousContainer(IDisposable disposable) => disposable.Dispose();

    private static ImapMailbox Open(FakeImapServer server, CredentialsFixture credentials) =>
        new(TestAccounts.Loopback(IncomingProtocol.Imap, server.Port), new NetworkMailServiceConnector(), credentials.Provider, credentials.Time);
}
