using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Tools;

/// <summary>
/// <c>email_draft</c> and <c>email_send</c> through the tool protocol: the allow-list, the
/// recipient cap and the hourly quota refuse before anything leaves, and a draft needs none of them.
/// </summary>
public sealed class EmailComposeToolsTests
{
    [Fact]
    public async Task Should_send_to_allowed_recipients_with_the_whole_envelope_and_file_a_sent_copy()
    {
        using var fixture = new ToolFixture();

        var result = ToolResults.Success(await fixture.CallAsync("email_send",
            ("to", ToolResults.Of("Client <client@example.com>")), ("cc", ToolResults.Of("boss@corp.test")), ("bcc", ToolResults.Of("audit@example.com")),
            ("subject", "Quote"), ("text", "Here is our quote.")));

        var sent = Assert.Single(fixture.Sender().Sent);
        Assert.Equal(TestAccounts.Address, sent.Sender);
        Assert.Equal(["client@example.com", "boss@corp.test", "audit@example.com"], sent.Recipients);
        Assert.Contains("From: Agent <agent@example.test>", sent.Mime, StringComparison.Ordinal);
        Assert.Equal(["client@example.com", "boss@corp.test", "audit@example.com"], ToolResults.Strings(result, "recipients"));
        Assert.Equal(("full", "2026-09-26T09:00:00.0000000+00:00"), (result["account"], result["sent_at"]));
        Assert.EndsWith("@example.test", (string)result["message_id"]!, StringComparison.Ordinal);
        Assert.False(result.ContainsKey("warning"));
        Assert.Single(fixture.Mailbox().SentCopies);
    }

    [Fact]
    public async Task Should_send_nothing_While_the_allow_list_is_empty()
    {
        using var fixture = new ToolFixture();

        var error = ToolResults.Failure(await fixture.CallAsync("email_send", ("account", "closed"), ("to", ToolResults.Of("anyone@example.com")), ("subject", "S"), ("text", "T")));

        Assert.Equal(
            "Tool execution failed: E-mail account 'closed' allows no recipient yet: an operator lists them under Send:AllowedRecipients (an address, *@domain, or *). Nothing was sent; email_draft needs no allow-list.",
            error);
        Assert.Empty(fixture.Sender("closed").Sent);
    }

    [Theory]
    [InlineData("to", "stranger@evil.test")]
    [InlineData("cc", "boss@corp.test.evil.test")]
    [InlineData("bcc", "Boss <spy@evil.test>")]
    public async Task Should_send_nothing_When_any_recipient_is_outside_the_allow_list(string field, string outsider)
    {
        using var fixture = new ToolFixture();

        var error = ToolResults.Failure(await fixture.CallAsync("email_send",
            ("to", field == "to" ? ToolResults.Of("client@example.com", outsider) : ToolResults.Of("client@example.com")),
            (field == "to" ? "unused" : field, ToolResults.Of(outsider)), ("subject", "S"), ("text", "T")));

        var address = outsider.Contains('<', StringComparison.Ordinal) ? "spy@evil.test" : outsider;
        Assert.Equal($"Tool execution failed: E-mail account 'full' may not send to {address} (outside Send:AllowedRecipients). Nothing was sent.", error);
        Assert.Empty(fixture.Sender().Sent);
        Assert.Empty(fixture.Mailbox().SentCopies);
    }

    [Fact]
    public async Task Should_send_nothing_to_more_recipients_than_the_account_allows()
    {
        using var fixture = new ToolFixture();

        var error = ToolResults.Failure(await fixture.CallAsync("email_send",
            ("to", ToolResults.Of("a@example.com", "b@example.com", "c@example.com", "d@example.com")), ("subject", "S"), ("text", "T")));

        Assert.Equal("Tool execution failed: The message has 4 recipients; account 'full' allows at most 3 (Send:MaxRecipients). Nothing was sent.", error);
        Assert.Empty(fixture.Sender().Sent);
    }

    [Fact]
    public async Task Should_stop_at_the_hourly_quota_and_resume_an_hour_later()
    {
        using var fixture = new ToolFixture();
        (string, object?)[] message = [("to", ToolResults.Of("client@example.com")), ("subject", "S"), ("text", "T")];

        ToolResults.Success(await fixture.CallAsync("email_send", message));
        ToolResults.Success(await fixture.CallAsync("email_send", message));
        var third = ToolResults.Failure(await fixture.CallAsync("email_send", message));
        fixture.Credentials.Time.Advance(TimeSpan.FromHours(1));
        ToolResults.Success(await fixture.CallAsync("email_send", message));

        Assert.Equal("Tool execution failed: E-mail account 'full' already sent 2 messages in the last hour (Send:MaxPerHour); nothing was sent.", third);
        Assert.Equal(3, fixture.Sender().Sent.Count);
    }

    [Fact]
    public async Task Should_report_a_failed_sent_copy_as_a_warning_since_the_message_is_gone()
    {
        using var fixture = new ToolFixture();
        fixture.Mailbox().AppendToSentFailure = new EmailToolException(EmailErrorCode.ServerError, "APPEND refused.");

        var result = ToolResults.Success(await fixture.CallAsync("email_send", ("to", ToolResults.Of("client@example.com")), ("subject", "S"), ("text", "T")));

        Assert.Single(fixture.Sender().Sent);
        Assert.Equal("Sent, but filing a copy in the Sent folder failed: APPEND refused.", result["warning"]);
    }

    [Fact]
    public async Task Should_file_no_copy_When_the_server_does_it()
    {
        using var fixture = new ToolFixture();

        ToolResults.Success(await fixture.CallAsync("email_send", ("account", "nocopy"), ("to", ToolResults.Of("anyone@anywhere.test")), ("subject", "S"), ("text", "T")));

        Assert.Single(fixture.Sender("nocopy").Sent);
        Assert.Empty(fixture.Mailbox("nocopy").SentCopies);
    }

    [Fact]
    public async Task Should_reply_to_the_original_sender_with_threading_headers()
    {
        using var fixture = new ToolFixture();
        fixture.Mailbox().Add("orig", MimeSamples.Plain(from: "Boss <boss@corp.test>", subject: "Budget", messageId: "<budget-1@corp.test>"));

        var result = ToolResults.Success(await fixture.CallAsync("email_send", ("reply_to_id", " orig "), ("text", "Approved.")));

        Assert.Equal(["boss@corp.test"], ToolResults.Strings(result, "recipients"));
        var sent = Assert.Single(fixture.Sender().Sent);
        Assert.Contains("Subject: Re: Budget", sent.Mime, StringComparison.Ordinal);
        Assert.Contains("In-Reply-To: <budget-1@corp.test>", sent.Mime, StringComparison.Ordinal);
        Assert.Equal(["orig"], fixture.Mailbox().Fetched);
    }

    [Fact]
    public async Task Should_refuse_a_reply_the_account_cannot_read_the_original_of()
    {
        using var fixture = new ToolFixture();
        fixture.Mailbox("drafter").Add("orig", MimeSamples.Plain());

        var error = ToolResults.Failure(await fixture.CallAsync("email_draft", ("account", "drafter"), ("reply_to_id", "orig"), ("text", "Hi")));

        Assert.Contains("does not grant the Read right (it grants: Draft)", error, StringComparison.Ordinal);
        Assert.Empty(fixture.Mailbox("drafter").Fetched);
    }

    [Fact]
    public async Task Should_forward_the_original_attached_whole()
    {
        using var fixture = new ToolFixture();
        fixture.Mailbox().Add("orig", MimeSamples.Plain(subject: "Contract"));

        ToolResults.Success(await fixture.CallAsync("email_send", ("forward_id", "orig"), ("to", ToolResults.Of("legal@example.com")), ("text", "FYI")));

        var sent = Assert.Single(fixture.Sender().Sent);
        Assert.Contains("Subject: Fwd: Contract", sent.Mime, StringComparison.Ordinal);
        Assert.Contains("Content-Type: message/rfc822", sent.Mime, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_attach_files_from_the_virtual_file_system()
    {
        using var fixture = new ToolFixture();
        fixture.Files.AddFile("/workspace/out/report.pdf", MimeSamples.PdfBytes);

        ToolResults.Success(await fixture.CallAsync("email_send",
            ("to", ToolResults.Of("client@example.com")), ("subject", "Report"), ("text", "Attached."), ("attachments", ToolResults.Of("/workspace/out/report.pdf"))));

        Assert.Contains("filename=report.pdf", Assert.Single(fixture.Sender().Sent).Mime, StringComparison.Ordinal);
        Assert.Contains(("/workspace/out/report.pdf", FileAccessRights.Read), fixture.Files.Validations);
    }

    [Fact]
    public async Task Should_refuse_to_send_without_the_Send_right()
    {
        using var fixture = new ToolFixture();

        var error = ToolResults.Failure(await fixture.CallAsync("email_send", ("account", "reader"), ("to", ToolResults.Of("a@example.com")), ("subject", "S"), ("text", "T")));

        Assert.Contains("does not grant the Send right", error, StringComparison.Ordinal);
        Assert.Empty(fixture.Sender("reader").Sent);
    }

    [Fact]
    public async Task Should_explain_what_a_new_message_lacks()
    {
        using var fixture = new ToolFixture();

        var noSubject = ToolResults.Failure(await fixture.CallAsync("email_send", ("to", ToolResults.Of("a@example.com")), ("text", "T")));
        var badAddress = ToolResults.Failure(await fixture.CallAsync("email_send", ("to", ToolResults.Of("not an address")), ("subject", "S"), ("text", "T")));

        Assert.Equal("Tool execution failed: A new message needs a `subject`.", noSubject);
        Assert.Equal("Tool execution failed: 'not an address' is not an e-mail address.", badAddress);
        Assert.Empty(fixture.Sender().Sent);
    }

    [Fact]
    public async Task Should_save_a_draft_for_anyone_without_an_allow_list()
    {
        using var fixture = new ToolFixture();

        var result = ToolResults.Success(await fixture.CallAsync("email_draft",
            ("account", "drafter"), ("to", ToolResults.Of("stranger@anywhere.test")), ("subject", "Proposal"), ("html", "<p>Our <b>offer</b></p>")));

        Assert.Equal(("drafter", "draft-1", "Drafts"), (result["account"], result["id"], result["folder"]));
        Assert.Equal(["stranger@anywhere.test"], ToolResults.Strings(result, "recipients"));
        Assert.EndsWith("@example.test", (string)result["message_id"]!, StringComparison.Ordinal);
        var draft = Assert.Single(fixture.Mailbox("drafter").Drafts);
        Assert.Contains("Subject: Proposal", draft, StringComparison.Ordinal);
        Assert.Contains("multipart/alternative", draft, StringComparison.Ordinal);
        Assert.Empty(fixture.Sender("drafter").Sent);
    }

    [Fact]
    public async Task Should_refuse_a_draft_on_a_backend_without_drafts()
    {
        using var fixture = new ToolFixture();
        fixture.Mailbox().Capabilities = MailboxCapabilities.None;

        var error = ToolResults.Failure(await fixture.CallAsync("email_draft", ("to", ToolResults.Of("a@example.com")), ("subject", "S"), ("text", "T")));

        Assert.Contains("which has no drafts folder", error, StringComparison.Ordinal);
        Assert.Empty(fixture.Mailbox().Drafts);
    }

    [Fact]
    public async Task Should_refuse_to_draft_without_the_Draft_right()
    {
        using var fixture = new ToolFixture();

        var error = ToolResults.Failure(await fixture.CallAsync("email_draft", ("account", "reader"), ("to", ToolResults.Of("a@example.com")), ("subject", "S"), ("text", "T")));

        Assert.Contains("does not grant the Draft right", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_dispose_every_original_it_fetched_whether_the_message_goes_out_or_not()
    {
        using var fixture = new ToolFixture();
        fixture.Mailbox().Add("orig", MimeSamples.Plain());

        ToolResults.Success(await fixture.CallAsync("email_send", ("forward_id", "orig"), ("to", ToolResults.Of("legal@example.com"))));
        ToolResults.Failure(await fixture.CallAsync("email_send", ("forward_id", "orig"), ("to", ToolResults.Of("not an address"))));
        ToolResults.Success(await fixture.CallAsync("email_draft", ("reply_to_id", "orig"), ("text", "Noted.")));
        ToolResults.Failure(await fixture.CallAsync("email_send", ("reply_to_id", "orig"), ("to", ToolResults.Of("stranger@evil.test")), ("text", "x")));

        Assert.Equal(4, fixture.Mailbox().Issued.Count);
        Assert.All(fixture.Mailbox().Issued, message =>
            Assert.Throws<ObjectDisposedException>(() => message.WriteTo(Stream.Null, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Should_surface_a_sender_failure_without_filing_a_copy()
    {
        using var fixture = new ToolFixture();
        fixture.Sender().Failure = new EmailToolException(EmailErrorCode.ServerError, "smtp.example.test refused the recipient.");

        var error = ToolResults.Failure(await fixture.CallAsync("email_send", ("to", ToolResults.Of("client@example.com")), ("subject", "S"), ("text", "T")));

        Assert.Equal("Tool execution failed: smtp.example.test refused the recipient.", error);
        Assert.Empty(fixture.Mailbox().SentCopies);
    }
}
