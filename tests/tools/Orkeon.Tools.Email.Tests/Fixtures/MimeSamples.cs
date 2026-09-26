using System.Text;
using MimeKit;

namespace Orkeon.Tools.Email.Tests.Fixtures;

/// <summary>Raw MIME messages the tests feed to the mailboxes and servers.</summary>
internal static class MimeSamples
{
    /// <summary>Bytes of the PDF attachment of <see cref="WithAttachment"/>.</summary>
    public static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 fake report content for the tests");

    /// <summary>A text/plain message.</summary>
    public static string Plain(
        string subject = "Quarterly figures",
        string from = "Alice Martin <alice@example.com>",
        string to = "agent@example.test",
        string body = "Hello,\r\nthe figures are attached.\r\nAlice",
        string date = "Sat, 19 Sep 2026 10:15:00 +0000",
        string messageId = "<q3-figures@example.com>",
        string? extraHeaders = null) =>
        $"From: {from}\r\nTo: {to}\r\nSubject: {subject}\r\nDate: {date}\r\nMessage-ID: {messageId}\r\n{extraHeaders}" +
        $"MIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: 7bit\r\n\r\n{body}\r\n";

    /// <summary>A multipart/mixed message with a text body and a PDF attachment named <paramref name="fileName"/>.</summary>
    public static string WithAttachment(string subject = "Report attached", string fileName = "report.pdf", string from = "bob@example.com")
    {
        var encoded = Convert.ToBase64String(PdfBytes);
        return $"From: {from}\r\nTo: agent@example.test\r\nSubject: {subject}\r\nDate: Sun, 20 Sep 2026 09:00:00 +0000\r\n" +
            "Message-ID: <report-1@example.com>\r\nMIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=\"b1\"\r\n\r\n" +
            "--b1\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nPlease find the report.\r\n" +
            $"--b1\r\nContent-Type: application/pdf; name=\"{fileName}\"\r\nContent-Disposition: attachment; filename=\"{fileName}\"\r\n" +
            $"Content-Transfer-Encoding: base64\r\n\r\n{encoded}\r\n--b1--\r\n";
    }

    /// <summary>An HTML-only message.</summary>
    public static string Html(string html, string subject = "Newsletter") =>
        $"From: news@example.com\r\nTo: agent@example.test\r\nSubject: {subject}\r\nDate: Mon, 21 Sep 2026 07:00:00 +0000\r\n" +
        $"Message-ID: <news-1@example.com>\r\nMIME-Version: 1.0\r\nContent-Type: text/html; charset=utf-8\r\n\r\n{html}\r\n";

    /// <summary>
    /// A message with a text and an HTML alternative, an inline image carrying a file name, a PDF
    /// attachment and a forwarded message, in that order.
    /// </summary>
    public static MimeMessage Rich()
    {
        var forwarded = new MimeMessage();
        forwarded.From.Add(new MailboxAddress("Carol", "carol@example.org"));
        forwarded.To.Add(new MailboxAddress(null, "bob@example.com"));
        forwarded.Subject = "Original thread";
        forwarded.Body = new TextPart("plain") { Text = "The original text." };

        var builder = new BodyBuilder
        {
            TextBody = "Plain body.",
            HtmlBody = "<p>Html <b>body</b>.</p><div style=\"display:none\">hidden preheader</div>",
        };
        var image = builder.LinkedResources.Add("logo.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], ContentType.Parse("image/png"));
        image.ContentId = "logo@example.com";
        builder.Attachments.Add("report.pdf", PdfBytes, ContentType.Parse("application/pdf"));
        builder.Attachments.Add(new MessagePart { Message = forwarded });

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Bob", "bob@example.com"));
        message.ReplyTo.Add(new MailboxAddress("Bob's desk", "desk@example.com"));
        message.To.Add(new MailboxAddress("Agent", "agent@example.test"));
        message.To.Add(new MailboxAddress(null, "dana@example.com"));
        message.Cc.Add(new MailboxAddress("Eve", "eve@example.com"));
        message.Subject = "Rich message";
        message.Date = new DateTimeOffset(2026, 9, 18, 14, 30, 0, TimeSpan.FromHours(2));
        message.MessageId = "rich-1@example.com";
        message.Body = builder.ToMessageBody();
        return message;
    }

    /// <summary>The MIME text of <see cref="Rich"/>.</summary>
    public static string RichText()
    {
        using var message = Rich();
        return Text(message);
    }

    /// <summary>Loads a raw MIME string.</summary>
    public static MimeMessage Load(string raw)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        return MimeMessage.Load(stream);
    }

    /// <summary>The MIME text of <paramref name="message"/>.</summary>
    public static string Text(MimeMessage message)
    {
        using var buffer = new MemoryStream();
        message.WriteTo(buffer);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
