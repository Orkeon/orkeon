using System.Text;
using MimeKit;
using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mime;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Mime;

/// <summary>The MIME message an account sends or drafts: new, reply or forward.</summary>
public sealed class MessageComposerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_always_speak_as_the_account()
    {
        var account = Account() with { DisplayName = "Support desk" };

        using var message = await Compose(new ComposeInput { To = ["client@example.com"], Subject = "Hello", Text = "Hi" }, account);

        var from = Assert.Single(message.From.Mailboxes);
        Assert.Equal(("Support desk", TestAccounts.Address), (from.Name, from.Address));
        Assert.Equal("Hello", message.Subject);
        Assert.Equal("Hi", message.TextBody);
        Assert.EndsWith("@example.test", message.MessageId, StringComparison.Ordinal);
        Assert.NotEqual(DateTimeOffset.MinValue, message.Date);
    }

    [Fact]
    public async Task Should_never_let_a_subject_smuggle_a_header()
    {
        using var message = await Compose(new ComposeInput
        {
            To = ["client@example.com"],
            Subject = "Hello\r\nBcc: attacker@evil.test\r\nX-Injected: yes",
            Text = "Hi",
        });

        using var written = new MemoryStream();
        await message.WriteToAsync(written, Token);
        var reparsed = MimeSamples.Load(Encoding.UTF8.GetString(written.ToArray()));

        Assert.Empty(message.Bcc);
        Assert.Empty(reparsed.Bcc);
        Assert.False(reparsed.Headers.Contains("X-Injected"));
        Assert.Equal(["client@example.com"], reparsed.To.Mailboxes.Select(m => m.Address));
    }

    [Fact]
    public async Task Should_address_to_cc_and_bcc_once_each_accepting_display_names()
    {
        using var message = await Compose(new ComposeInput
        {
            To = ["Client <client@example.com>", "CLIENT@example.com", " "],
            Cc = ["boss@example.com"],
            Bcc = ["audit@example.com"],
            Subject = "Quote",
            Text = "Please find our quote.",
        });

        Assert.Equal(["Client <client@example.com>"], MimeMessageReader.Format(message.To));
        Assert.Equal(["boss@example.com"], MimeMessageReader.Format(message.Cc));
        Assert.Equal(["audit@example.com"], MimeMessageReader.Format(message.Bcc));
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("client.example.com")]
    public async Task Should_refuse_a_recipient_that_is_not_an_address(string recipient)
    {
        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await Compose(new ComposeInput { To = [recipient], Subject = "S", Text = "T" }));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.Equal(
            $"'{recipient}' is not an e-mail address: a recipient is `user@example.org` or `Name <user@example.org>`, never an account name. email_accounts gives each account's own `address`.",
            error.Message);
    }

    [Fact]
    public async Task Should_require_a_subject_a_body_and_a_recipient_for_a_new_message()
    {
        var subject = await Assert.ThrowsAsync<EmailToolException>(async () => await Compose(new ComposeInput { To = ["a@example.com"], Text = "T" }));
        var body = await Assert.ThrowsAsync<EmailToolException>(async () => await Compose(new ComposeInput { To = ["a@example.com"], Subject = "S" }));
        var recipient = await Assert.ThrowsAsync<EmailToolException>(async () => await Compose(new ComposeInput { Subject = "S", Text = "T" }));

        Assert.Equal("A new message needs a `subject`.", subject.Message);
        Assert.Equal("The message has no body: pass `text` (or `html`).", body.Message);
        Assert.Equal("The message has no recipient: pass `to`.", recipient.Message);
    }

    [Fact]
    public async Task Should_derive_a_text_alternative_from_an_HTML_only_body()
    {
        using var message = await Compose(new ComposeInput { To = ["a@example.com"], Subject = "News", Html = "<h1>Title</h1><p>Body <b>text</b></p>" });

        Assert.Equal("Title\n\nBody text", message.TextBody);
        Assert.Equal("<h1>Title</h1><p>Body <b>text</b></p>", message.HtmlBody);
        Assert.Equal("multipart/alternative", message.Body?.ContentType.MimeType);
    }

    [Fact]
    public async Task Should_reply_to_the_Reply_To_address_with_threading_headers_and_a_quote()
    {
        using var original = Original(replyTo: "Desk <desk@example.com>", references: "<root@example.com>");

        using var reply = await Compose(new ComposeInput { Text = "Thanks!", ReplyTo = original });

        Assert.Equal("Re: Original subject", reply.Subject);
        Assert.Equal(["Desk <desk@example.com>"], MimeMessageReader.Format(reply.To));
        Assert.Equal("orig-1@example.com", reply.InReplyTo);
        Assert.Equal(["root@example.com", "orig-1@example.com"], reply.References);
        Assert.StartsWith("Thanks!\n\nOn ", reply.TextBody, StringComparison.Ordinal);
        Assert.Contains("Carol <carol@example.com> wrote:\n> The original text.\n> Second line.", reply.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_reply_to_the_sender_When_there_is_no_Reply_To()
    {
        using var original = Original();

        using var reply = await Compose(new ComposeInput { Text = "Noted.", ReplyTo = original, QuoteOriginal = false });

        Assert.Equal(["Carol <carol@example.com>"], MimeMessageReader.Format(reply.To));
        Assert.Equal("Noted.", reply.TextBody);
    }

    [Theory]
    [InlineData("RE: Budget", "RE: Budget")]
    [InlineData("re:Budget", "re:Budget")]
    [InlineData("Budget", "Re: Budget")]
    public async Task Should_not_stack_reply_prefixes(string originalSubject, string expected)
    {
        using var original = Original(subject: originalSubject);

        using var reply = await Compose(new ComposeInput { Text = "Ok", ReplyTo = original });

        Assert.Equal(expected, reply.Subject);
    }

    [Fact]
    public async Task Should_use_an_explicit_subject_and_recipients_on_a_reply()
    {
        using var original = Original();

        using var reply = await Compose(new ComposeInput { To = ["other@example.com"], Subject = "Changed", Text = "Ok", ReplyTo = original });

        Assert.Equal("Changed", reply.Subject);
        Assert.Equal(["other@example.com"], MimeMessageReader.Format(reply.To));
    }

    [Fact]
    public async Task Should_reply_to_all_but_the_account_itself()
    {
        using var original = Original(to: "Agent <AGENT@example.test>, dana@example.com, carol@example.com", cc: "eve@example.com, agent@example.test");

        using var reply = await Compose(new ComposeInput { Text = "All of you", ReplyTo = original, ReplyAll = true });

        Assert.Equal(["Carol <carol@example.com>", "dana@example.com"], MimeMessageReader.Format(reply.To));
        Assert.Equal(["eve@example.com"], MimeMessageReader.Format(reply.Cc));
    }

    [Fact]
    public async Task Should_quote_the_original_in_the_HTML_part_too()
    {
        using var original = Original();

        using var reply = await Compose(new ComposeInput { Html = "<p>See below</p>", ReplyTo = original });

        Assert.StartsWith("<p>See below</p><br><blockquote>", reply.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&gt; The original text.<br>&gt; Second line.", reply.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_forward_the_original_whole_as_an_attached_message()
    {
        using var original = Original();

        using var forward = await Compose(new ComposeInput { To = ["colleague@example.com"], Text = "FYI", Forward = original });

        Assert.Equal("Fwd: Original subject", forward.Subject);
        var attached = Assert.IsType<MessagePart>(Assert.Single(forward.Attachments));
        Assert.Equal("Original subject.eml", attached.ContentDisposition?.FileName);
        Assert.Equal("orig-1@example.com", attached.Message?.MessageId);
        Assert.Equal("FYI", forward.TextBody);
    }

    [Theory]
    [InlineData("Fwd: Report", "Fwd: Report")]
    [InlineData("FW: Report", "FW: Report")]
    [InlineData("Report", "Fwd: Report")]
    public async Task Should_not_stack_forward_prefixes(string originalSubject, string expected)
    {
        using var original = Original(subject: originalSubject);

        using var forward = await Compose(new ComposeInput { To = ["colleague@example.com"], Forward = original });

        Assert.Equal(expected, forward.Subject);
    }

    [Fact]
    public async Task Should_refuse_a_message_that_is_both_a_reply_and_a_forward()
    {
        using var first = Original();
        using var second = Original();

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await Compose(new ComposeInput { To = ["a@example.com"], ReplyTo = first, Forward = second }));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.StartsWith("A message is either a reply or a forward", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_attach_files_read_through_the_virtual_file_system()
    {
        var files = new FakeRightsFileSystemService().AddMount("/workspace", FileAccessRights.ReadOnly)
            .AddFile("/workspace/out/Q3 report.pdf", MimeSamples.PdfBytes);

        using var message = await Compose(
            new ComposeInput { To = ["a@example.com"], Subject = "Report", Text = "Attached.", Attachments = ["/workspace/out/Q3 report.pdf"] },
            fileSystem: files);

        var part = Assert.IsType<MimePart>(Assert.Single(message.Attachments));
        Assert.Equal("Q3 report.pdf", part.FileName);
        Assert.Equal("application/pdf", part.ContentType.MimeType);
        using var decoded = new MemoryStream();
        Assert.NotNull(part.Content);
        await part.Content.DecodeToAsync(decoded, Token);
        Assert.Equal(MimeSamples.PdfBytes, decoded.ToArray());
        Assert.Contains(("/workspace/out/Q3 report.pdf", FileAccessRights.Read), files.Validations);
    }

    [Theory]
    [InlineData("/secret/keys.pem", "Cannot attach '/secret/keys.pem': No mount for virtual path '/secret/keys.pem'.")]
    [InlineData("/drop/upload.bin", "Cannot attach '/drop/upload.bin': The mount '/drop' does not grant the right this needs.")]
    public async Task Should_refuse_an_attachment_the_file_system_does_not_let_it_read(string path, string message)
    {
        var files = new FakeRightsFileSystemService().AddMount("/drop", FileAccessRights.Write | FileAccessRights.Create)
            .AddFile("/drop/upload.bin", [1, 2, 3]);

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await Compose(new ComposeInput { To = ["a@example.com"], Subject = "S", Text = "T", Attachments = [path] }, fileSystem: files));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.Equal(message, error.Message);
    }

    private static MimeMessage Original(
        string subject = "Original subject", string? replyTo = null, string? references = null,
        string to = "agent@example.test", string? cc = null)
    {
        var headers = new StringBuilder();
        if (replyTo is not null)
            headers.Append("Reply-To: ").Append(replyTo).Append("\r\n");
        if (references is not null)
            headers.Append("References: ").Append(references).Append("\r\n");
        if (cc is not null)
            headers.Append("Cc: ").Append(cc).Append("\r\n");
        return MimeSamples.Load(MimeSamples.Plain(
            subject: subject, from: "Carol <carol@example.com>", to: to, body: "The original text.\r\nSecond line.",
            messageId: "<orig-1@example.com>", extraHeaders: headers.ToString()));
    }

    private static ResolvedEmailAccount Account() => TestAccounts.Resolve("acct", TestAccounts.Custom(TestAccounts.AllRights));

    private static Task<MimeMessage> Compose(ComposeInput input, ResolvedEmailAccount? account = null, IFileSystemService? fileSystem = null) =>
        new MessageComposer(fileSystem ?? new FakeRightsFileSystemService()).ComposeAsync(account ?? Account(), input, Token);
}
