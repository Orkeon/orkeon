using System.Text;
using MimeKit;
using Orkeon.Tools.Email.Mailboxes.Graph;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Mailboxes.Graph;

/// <summary>Sending through Graph <c>sendMail</c>, where the MIME headers are the envelope.</summary>
public sealed class GraphMailSenderTests
{
    private const string Base = GraphFixture.Base;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_post_the_message_to_sendMail_as_base64_MIME_with_its_Bcc_for_Graph_to_honour()
    {
        using var graph = new GraphFixture();
        graph.Http.Map(HttpMethod.Post, $"{Base}me/sendMail", new FakeHttpResponse(System.Net.HttpStatusCode.Accepted, string.Empty));
        var sender = new GraphMailSender(graph.Client);
        using var message = MimeSamples.Load(MimeSamples.Plain(to: "client@example.com", extraHeaders: "Bcc: boss@example.com\r\n"));

        var receipt = await sender.SendAsync(message, new MailboxAddress(null, "someone@outlook.com"),
            [new MailboxAddress(null, "client@example.com"), new MailboxAddress(null, "Boss@Example.com")], Token);

        Assert.Equal("202 Accepted", receipt.ServerResponse);
        var post = Assert.Single(graph.Http.Requests);
        Assert.Equal("text/plain", post.ContentType);
        var mime = Encoding.UTF8.GetString(Convert.FromBase64String(post.Body!));
        Assert.Contains("Bcc: boss@example.com", mime, StringComparison.Ordinal);
        Assert.Contains("To: client@example.com", mime, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_post_the_MIME_with_CRLF_line_endings_whatever_the_platform()
    {
        using var graph = new GraphFixture();
        graph.Http.Map(HttpMethod.Post, $"{Base}me/sendMail", new FakeHttpResponse(System.Net.HttpStatusCode.Accepted, string.Empty));
        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress(null, "someone@outlook.com"));
        message.To.Add(new MailboxAddress(null, "client@example.com"));
        message.Subject = "Hi";
        message.Body = new TextPart("plain") { Text = "line one\nline two" };

        await new GraphMailSender(graph.Client).SendAsync(
            message, new MailboxAddress(null, "someone@outlook.com"), [new MailboxAddress(null, "client@example.com")], Token);

        var mime = Encoding.UTF8.GetString(Convert.FromBase64String(Assert.Single(graph.Http.Requests).Body!));
        Assert.Contains("Subject: Hi\r\n", mime, StringComparison.Ordinal);
        Assert.Contains("line one\r\nline two", mime, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', mime.Replace("\r\n", string.Empty, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("To: client@example.com, stranger@evil.example\r\n")]
    [InlineData("To: client@example.com\r\nCc: stranger@evil.example\r\n")]
    [InlineData("To: client@example.com\r\nResent-To: stranger@evil.example\r\n")]
    [InlineData("To: client@example.com\r\nResent-Bcc: stranger@evil.example\r\n")]
    public async Task Should_refuse_a_message_whose_headers_name_an_unchecked_recipient(string headers)
    {
        using var graph = new GraphFixture();
        var sender = new GraphMailSender(graph.Client);
        using var message = MimeSamples.Load(
            "From: someone@outlook.com\r\n" + headers + "Subject: Hi\r\nMIME-Version: 1.0\r\nContent-Type: text/plain\r\n\r\nHello\r\n");

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await sender.SendAsync(message, new MailboxAddress(null, "someone@outlook.com"), [new MailboxAddress(null, "client@example.com")], Token));

        Assert.Equal(EmailErrorCode.RecipientNotAllowed, error.Code);
        Assert.Equal("The message headers name a recipient outside the checked list; it was not sent.", error.Message);
        Assert.Empty(graph.Http.Requests);
    }
}
