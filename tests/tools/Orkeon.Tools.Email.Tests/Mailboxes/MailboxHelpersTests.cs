using System.Net.Sockets;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Mailboxes;

/// <summary>Folder roles, search previews, message sizes and the translation of MailKit failures.</summary>
public sealed class MailboxHelpersTests
{
    private static readonly MailEndpoint Endpoint = new("imap.example.test", 993, TransportSecurity.SslOnConnect);

    [Theory]
    [InlineData("inbox", "inbox")]
    [InlineData(" INBOX ", "inbox")]
    [InlineData("Sent", "sent")]
    [InlineData("DRAFTS", "drafts")]
    [InlineData("trash", "trash")]
    [InlineData("junk", "junk")]
    [InlineData("Spam", "junk")]
    [InlineData("archive", "archive")]
    [InlineData("all", "all")]
    [InlineData("Clients/ACME", null)]
    [InlineData("Sent Items", null)]
    public void Should_recognize_the_well_known_roles_and_leave_paths_alone(string folder, string? role)
    {
        Assert.Equal(role, FolderRoles.Parse(folder));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   \r\n\t ", null)]
    [InlineData("  Hello\r\n\r\n   world\tagain  ", "Hello world again")]
    public void Should_collapse_whitespace_in_previews(string? text, string? expected)
    {
        Assert.Equal(expected, Previews.Shorten(text));
    }

    [Theory]
    [InlineData(250, "", true)]
    [InlineData(101, "", true)]
    [InlineData(100, "", false)]
    [InlineData(100, "  \r\n\t ", false)]
    [InlineData(99, "", false)]
    public void Should_add_an_ellipsis_only_When_the_preview_cut_something(int length, string tail, bool cut)
    {
        var preview = Previews.Shorten(new string('x', length) + tail);

        Assert.Equal(new string('x', Math.Min(length, 100)) + (cut ? "…" : string.Empty), preview);
    }

    [Fact]
    public void Should_measure_a_message_as_it_travels_with_CRLF_line_endings_whatever_the_platform()
    {
        using var message = MimeSamples.Load(MimeSamples.WithAttachment());
        using var buffer = new MemoryStream();
        message.WriteTo(new FormatOptions { NewLineFormat = NewLineFormat.Dos }, buffer, TestContext.Current.CancellationToken);

        Assert.Equal(buffer.Length, MessageSizes.Measure(message));
    }

    [Fact]
    public void Should_give_the_app_password_hint_to_a_Gmail_password_account()
    {
        var gmail = TestAccounts.Resolve("perso", TestAccounts.Gmail());

        var translated = MailKitSessions.Translate(new AuthenticationException("535 bad"), gmail, Endpoint);

        Assert.NotNull(translated);
        Assert.Equal(EmailErrorCode.AuthenticationFailed, translated.Code);
        Assert.Equal(
            "imap.example.test:993 refused the credentials of e-mail account 'perso'. Gmail needs an app password (Google account > Security > 2-Step Verification > App passwords), not the account password. (535 bad)",
            translated.Message);
    }

    [Fact]
    public void Should_tell_an_OAuth_account_to_sign_in_again_When_its_token_is_refused()
    {
        var outlook = TestAccounts.Resolve("hotmail", TestAccounts.Outlook());

        var translated = MailKitSessions.Translate(new AuthenticationException("bad token"), outlook, null);

        Assert.Equal(EmailErrorCode.AuthenticationFailed, translated!.Code);
        Assert.StartsWith("the mail server refused the credentials of e-mail account 'hotmail'.", translated.Message, StringComparison.Ordinal);
        Assert.Contains("run `orkeon email login hotmail` again", translated.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tls", "ServerError", "The TLS handshake with imap.example.test:993 failed")]
    [InlineData("folder", "FolderNotFound", "The folder 'Archive' does not exist in account 'acct': list them with email_folders.")]
    [InlineData("message", "MessageNotFound", "The message no longer exists in account 'acct' (moved or deleted): search again.")]
    [InlineData("command", "ServerError", "imap.example.test:993 refused the command: no way")]
    [InlineData("protocol", "ServerError", "The connection to imap.example.test:993 failed (garbled). A dropped connection is usually transient, and the next call opens a new one: call again.")]
    [InlineData("io", "ServerError", "The connection to imap.example.test:993 failed (reset).")]
    [InlineData("socket", "ServerError", "The connection to imap.example.test:993 failed")]
    [InlineData("disconnected", "ServerError", "The connection to imap.example.test:993 failed")]
    [InlineData("timeout", "ServerError", "The connection to imap.example.test:993 failed (timed out).")]
    public void Should_turn_MailKit_and_socket_failures_into_actionable_errors(string failure, string code, string message)
    {
        var account = TestAccounts.Resolve("acct", TestAccounts.Custom());
        Exception exception = failure switch
        {
            "tls" => new SslHandshakeException("handshake"),
            "folder" => new FolderNotFoundException("Archive"),
            "message" => new MessageNotFoundException("gone"),
            "command" => new ImapCommandException(ImapCommandResponse.No, "no way", "no way"),
            "protocol" => new ImapProtocolException("garbled"),
            "io" => new IOException("reset"),
            "socket" => new SocketException((int)SocketError.ConnectionRefused),
            "timeout" => new TimeoutException("timed out"),
            _ => new ServiceNotConnectedException("not connected"),
        };

        var translated = MailKitSessions.Translate(exception, account, Endpoint);

        Assert.NotNull(translated);
        Assert.Equal(Enum.Parse<EmailErrorCode>(code), translated.Code);
        Assert.StartsWith(message, translated.Message, StringComparison.Ordinal);
        Assert.Same(exception, translated.InnerException);
    }

    [Fact]
    public void Should_leave_cancellations_own_errors_and_unknown_failures_untouched()
    {
        var account = TestAccounts.Resolve("acct", TestAccounts.Custom());

        Assert.Null(MailKitSessions.Translate(new OperationCanceledException(), account, Endpoint));
        Assert.Null(MailKitSessions.Translate(new EmailToolException(EmailErrorCode.TooLarge, "too big"), account, Endpoint));
        Assert.Null(MailKitSessions.Translate(new InvalidOperationException("bug"), account, Endpoint));
    }

    [Fact]
    public void Should_default_a_bare_email_tool_exception_to_a_server_error()
    {
        Assert.Equal(EmailErrorCode.ServerError, new EmailToolException().Code);
        Assert.Equal(EmailErrorCode.ServerError, new EmailToolException("x").Code);
        Assert.Equal(EmailErrorCode.ServerError, new EmailToolException("x", new InvalidOperationException()).Code);
        Assert.Equal(EmailErrorCode.QuotaExceeded, new EmailToolException(EmailErrorCode.QuotaExceeded, "x", new InvalidOperationException()).Code);
    }

    [Fact]
    public void Should_never_print_secrets_in_credentials()
    {
        var password = new Orkeon.Tools.Email.Auth.PasswordCredential("user@example.com", "hunter2");
        var bearer = new Orkeon.Tools.Email.Auth.BearerCredential("user@example.com", "ya29.secret");

        Assert.DoesNotContain("hunter2", password.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("ya29.secret", bearer.ToString(), StringComparison.Ordinal);
        Assert.Contains("user@example.com", password.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_refuse_to_dial_with_a_transport_security_it_does_not_know()
    {
        using var client = new ImapClient();
        var endpoint = new MailEndpoint("imap.example.test", 993, (TransportSecurity)7);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new NetworkMailServiceConnector().ConnectAsync(client, endpoint, TestContext.Current.CancellationToken));

        Assert.False(client.IsConnected);
    }

    [Fact]
    public void Should_dispose_the_MIME_message_with_the_fetched_message()
    {
        var message = MimeSamples.Load(MimeSamples.Plain());
        var fetched = new FetchedMessage("id", "INBOX", message, true, null);

        fetched.Dispose();

        Assert.Throws<ObjectDisposedException>(() => message.WriteTo(Stream.Null, TestContext.Current.CancellationToken));
    }
}
