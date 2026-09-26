using MimeKit;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Mailboxes.Smtp;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Mailboxes.Smtp;

/// <summary>
/// The SMTP sender driven through the production connector against a loopback
/// <see cref="FakeSmtpServer"/>: the explicit envelope, blind copies, the size limit and failures.
/// </summary>
public sealed class SmtpMailSenderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_send_to_exactly_the_checked_recipients_whatever_the_headers_say()
    {
        await using var server = new FakeSmtpServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        var sender = Sender(server, credentials);
        using var message = MimeSamples.Load(MimeSamples.Plain(
            to: "listed@example.com, not-checked@example.com",
            extraHeaders: "Resent-To: hijack@evil.example\r\nCc: copy@example.com\r\n"));

        var receipt = await sender.SendAsync(message, new MailboxAddress("Agent", TestAccounts.Address),
            [new MailboxAddress(null, "listed@example.com"), new MailboxAddress(null, "copy@example.com")], Token);

        server.AssertHealthy();
        var envelope = Assert.Single(server.Accepted);
        Assert.Equal(TestAccounts.Address, envelope.MailFrom);
        Assert.Equal(["listed@example.com", "copy@example.com"], envelope.RcptTo);
        Assert.Contains("2.0.0 Ok: queued as FAKE42", receipt.ServerResponse, StringComparison.Ordinal);
        Assert.Equal("QUIT", server.Transcript[^1]);
    }

    [Fact]
    public async Task Should_deliver_to_blind_copies_without_transmitting_the_Bcc_header()
    {
        await using var server = new FakeSmtpServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture();
        var sender = Sender(server, credentials);
        using var message = MimeSamples.Load(MimeSamples.Plain(extraHeaders: "Bcc: secret-boss@example.com\r\nResent-Bcc: auditor@example.com\r\n"));

        await sender.SendAsync(message, new MailboxAddress(null, TestAccounts.Address),
            [new MailboxAddress(null, "agent@example.test"), new MailboxAddress(null, "secret-boss@example.com")], Token);

        var envelope = Assert.Single(server.Accepted);
        Assert.Contains("secret-boss@example.com", envelope.RcptTo);
        Assert.DoesNotContain("secret-boss@example.com", envelope.Data, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auditor@example.com", envelope.Data, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Subject: Quarterly figures", envelope.Data, StringComparison.Ordinal);
        Assert.Equal("secret-boss@example.com", Assert.Single(message.Bcc.Mailboxes).Address);
    }

    [Fact]
    public async Task Should_refuse_a_message_larger_than_the_advertised_SIZE_before_MAIL_FROM()
    {
        await using var server = new FakeSmtpServer(TestAccounts.Address, TestAccounts.Password, maxSize: 2048);
        using var credentials = new CredentialsFixture();
        var sender = Sender(server, credentials);
        using var message = MimeSamples.Load(MimeSamples.Plain(body: new string('x', 8 * 1024)));

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await sender.SendAsync(message, new MailboxAddress(null, TestAccounts.Address), [new MailboxAddress(null, "a@example.com")], Token));

        Assert.Equal(EmailErrorCode.TooLarge, error.Code);
        Assert.Contains("127.0.0.1 accepts at most 2 KB", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(server.Transcript, line => line.StartsWith("MAIL FROM", StringComparison.Ordinal));
        Assert.Empty(server.Accepted);
    }

    [Fact]
    public async Task Should_refuse_a_message_that_exceeds_the_advertised_SIZE_only_once_its_lines_end_in_CRLF()
    {
        using var message = MimeSamples.Load(MimeSamples.Plain(body: string.Join("\r\n", Enumerable.Repeat("a short line", 200))));
        using var unix = new MemoryStream();
        await message.WriteToAsync(new FormatOptions { NewLineFormat = NewLineFormat.Unix }, unix, Token);
        await using var server = new FakeSmtpServer(TestAccounts.Address, TestAccounts.Password, maxSize: unix.Length);
        using var credentials = new CredentialsFixture();
        var sender = Sender(server, credentials);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await sender.SendAsync(message, new MailboxAddress(null, TestAccounts.Address), [new MailboxAddress(null, "a@example.com")], Token));

        Assert.Equal(EmailErrorCode.TooLarge, error.Code);
        Assert.Empty(server.Accepted);
    }

    [Fact]
    public async Task Should_send_a_message_under_the_advertised_SIZE()
    {
        await using var server = new FakeSmtpServer(TestAccounts.Address, TestAccounts.Password, maxSize: 64 * 1024);
        using var credentials = new CredentialsFixture();
        var sender = Sender(server, credentials);
        using var message = MimeSamples.Load(MimeSamples.Plain());

        await sender.SendAsync(message, new MailboxAddress(null, TestAccounts.Address), [new MailboxAddress(null, "a@example.com")], Token);

        Assert.Single(server.Accepted);
    }

    [Fact]
    public async Task Should_report_AuthenticationFailed_with_the_app_password_hint_When_Gmail_refuses_the_password()
    {
        await using var server = new FakeSmtpServer(TestAccounts.Address, "the-account-password");
        using var credentials = new CredentialsFixture();
        var account = TestAccounts.AsGmail(TestAccounts.Loopback(IncomingProtocol.Imap, 1, server.Port, TestAccounts.AllRights));
        var sender = new SmtpMailSender(account, new NetworkMailServiceConnector(), credentials.Provider);
        using var message = MimeSamples.Load(MimeSamples.Plain());

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await sender.SendAsync(message, new MailboxAddress(null, TestAccounts.Address), [new MailboxAddress(null, "a@example.com")], Token));

        Assert.Equal(EmailErrorCode.AuthenticationFailed, error.Code);
        Assert.Contains("Gmail needs an app password", error.Message, StringComparison.Ordinal);
        Assert.Empty(server.Accepted);
    }

    [Fact]
    public async Task Should_sign_in_with_XOAUTH2_When_the_account_uses_OAuth2()
    {
        await using var server = new FakeSmtpServer(TestAccounts.Address, TestAccounts.Password) { AccessToken = "ya29.smtp-token" };
        using var credentials = new CredentialsFixture();
        var account = TestAccounts.AsGmailOAuth(TestAccounts.Loopback(IncomingProtocol.Imap, 1, server.Port, TestAccounts.AllRights));
        credentials.SeedFreshToken(account, "ya29.smtp-token");
        var sender = new SmtpMailSender(account, new NetworkMailServiceConnector(), credentials.Provider);
        using var message = MimeSamples.Load(MimeSamples.Plain());

        await sender.SendAsync(message, new MailboxAddress(null, TestAccounts.Address), [new MailboxAddress(null, "a@example.com")], Token);

        Assert.Equal(["XOAUTH2"], server.Authentications);
        Assert.Single(server.Accepted);
    }

    [Fact]
    public async Task Should_name_the_recipient_the_server_refused()
    {
        await using var server = new FakeSmtpServer(TestAccounts.Address, TestAccounts.Password);
        server.RejectedRecipients.Add("ghost@example.com");
        using var credentials = new CredentialsFixture();
        var sender = Sender(server, credentials);
        using var message = MimeSamples.Load(MimeSamples.Plain());

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await sender.SendAsync(message, new MailboxAddress(null, TestAccounts.Address), [new MailboxAddress(null, "ghost@example.com")], Token));

        Assert.Equal(EmailErrorCode.ServerError, error.Code);
        Assert.Contains("127.0.0.1 refused the recipient ghost@example.com", error.Message, StringComparison.Ordinal);
        Assert.Empty(server.Accepted);
    }

    [Fact]
    public async Task Should_fail_with_CredentialMissing_before_dialling_When_the_password_variable_is_unset()
    {
        await using var server = new FakeSmtpServer(TestAccounts.Address, TestAccounts.Password);
        using var credentials = new CredentialsFixture(TestAccounts.Environment());
        var sender = Sender(server, credentials);
        using var message = MimeSamples.Load(MimeSamples.Plain());

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await sender.SendAsync(message, new MailboxAddress(null, TestAccounts.Address), [new MailboxAddress(null, "a@example.com")], Token));

        Assert.Equal(EmailErrorCode.CredentialMissing, error.Code);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    public void Should_refuse_an_account_without_an_outgoing_server()
    {
        using var credentials = new CredentialsFixture();
        var account = TestAccounts.Loopback(IncomingProtocol.Imap, 1);

        Assert.Throws<ArgumentException>(() => new SmtpMailSender(account, new NetworkMailServiceConnector(), credentials.Provider));
    }

    private static SmtpMailSender Sender(FakeSmtpServer server, CredentialsFixture credentials) =>
        new(TestAccounts.Loopback(IncomingProtocol.Imap, 1, server.Port, TestAccounts.AllRights), new NetworkMailServiceConnector(), credentials.Provider);
}
