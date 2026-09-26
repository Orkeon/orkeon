using System.Text;
using MimeKit;
using Orkeon.Tools.Email.Mime;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Mime;

/// <summary>What the tools read out of a received message: body text, attachments, addresses, date.</summary>
public sealed class MimeMessageReaderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Should_read_the_text_part_with_its_blanks_normalized()
    {
        using var message = MimeSamples.Load(MimeSamples.Plain(body: "Hello,\r\n\r\n\r\n\r\nthe   figures\tare attached."));

        var body = MimeMessageReader.ReadBody(message);

        Assert.Equal("Hello,\n\nthe figures are attached.", body.Text);
        Assert.False(body.HadHiddenContent);
    }

    [Fact]
    public void Should_render_the_HTML_part_When_there_is_no_text_part()
    {
        using var message = MimeSamples.Load(MimeSamples.Html("<p>Big <b>sale</b></p><div style=\"display:none\">preheader</div>"));

        var body = MimeMessageReader.ReadBody(message);

        Assert.Equal("Big sale", body.Text);
        Assert.True(body.HadHiddenContent);
    }

    [Fact]
    public void Should_prefer_the_text_part_but_still_report_what_the_HTML_part_hides()
    {
        using var message = Parsed(MimeSamples.Rich());

        var body = MimeMessageReader.ReadBody(message);

        Assert.Equal("Plain body.", body.Text);
        Assert.True(body.HadHiddenContent);
    }

    [Fact]
    public void Should_read_an_empty_body_When_the_message_has_no_text()
    {
        using var message = new MimeMessage { Body = new MimePart("application", "octet-stream") };

        Assert.Equal(string.Empty, MimeMessageReader.ReadBody(message).Text);
    }

    [Fact]
    public void Should_number_attachments_including_a_named_inline_image_and_a_forwarded_message()
    {
        using var message = Parsed(MimeSamples.Rich());

        var attachments = MimeMessageReader.ReadAttachments(message);

        Assert.Equal(3, attachments.Count);
        Assert.Equal((0, "logo.png", "image/png", true), (attachments[0].Index, attachments[0].FileName, attachments[0].ContentType, attachments[0].Inline));
        Assert.Equal((1, "report.pdf", "application/pdf", false), (attachments[1].Index, attachments[1].FileName, attachments[1].ContentType, attachments[1].Inline));
        Assert.Equal((2, "Original thread.eml", "message/rfc822", false), (attachments[2].Index, attachments[2].FileName, attachments[2].ContentType, attachments[2].Inline));
        Assert.InRange(attachments[1].SizeBytes, MimeSamples.PdfBytes.Length, MimeSamples.PdfBytes.Length + 4);
        Assert.True(attachments[2].SizeBytes > 50);
    }

    [Fact]
    public void Should_skip_an_inline_image_without_a_file_name()
    {
        var builder = new BodyBuilder { HtmlBody = "<img src=\"cid:pic\">" };
        var image = builder.LinkedResources.Add("pic.png", [1, 2, 3], ContentType.Parse("image/png"));
        image.ContentId = "pic";
        image.ContentDisposition = null;
        image.ContentType.Name = null;
        using var message = new MimeMessage { Body = builder.ToMessageBody() };

        Assert.Empty(MimeMessageReader.ReadAttachments(message));
    }

    [Fact]
    public void Should_sanitize_hostile_file_names_and_make_duplicates_unique()
    {
        var builder = new BodyBuilder { TextBody = "See attached." };
        builder.Attachments.Add("../../evil.sh", [1], ContentType.Parse("application/x-sh"));
        builder.Attachments.Add("notes.txt", Encoding.UTF8.GetBytes("one"), ContentType.Parse("text/plain"));
        builder.Attachments.Add("NOTES.txt", Encoding.UTF8.GetBytes("two"), ContentType.Parse("text/plain"));
        using var message = Parsed(new MimeMessage { Body = builder.ToMessageBody() });

        var names = MimeMessageReader.ReadAttachments(message).Select(entry => entry.FileName);

        Assert.Equal(["evil.sh", "notes.txt", "NOTES (1).txt"], names);
    }

    [Fact]
    public async Task Should_decode_an_attachment_and_write_a_forwarded_message_whole()
    {
        using var message = Parsed(MimeSamples.Rich());
        var attachments = MimeMessageReader.ReadAttachments(message);
        using var pdf = new MemoryStream();
        using var forwarded = new MemoryStream();

        await MimeMessageReader.WriteAttachmentAsync(attachments[1], pdf, Token);
        await MimeMessageReader.WriteAttachmentAsync(attachments[2], forwarded, Token);

        Assert.Equal(MimeSamples.PdfBytes, pdf.ToArray());
        Assert.Contains("Subject: Original thread", Encoding.UTF8.GetString(forwarded.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_format_addresses_with_their_name_or_bare()
    {
        using var message = Parsed(MimeSamples.Rich());

        Assert.Equal(["Agent <agent@example.test>", "dana@example.com"], MimeMessageReader.Format(message.To));
        Assert.Equal("Bob <bob@example.com>", MimeMessageReader.Format(message.From[0]));
        Assert.Equal(["Bob's desk <desk@example.com>"], MimeMessageReader.Format(message.ReplyTo));
    }

    [Fact]
    public void Should_flatten_group_addresses()
    {
        var group = new GroupAddress("Team", [new MailboxAddress("Ann", "ann@example.com"), new MailboxAddress(null, "ben@example.com")]);

        Assert.Equal(["Ann <ann@example.com>", "ben@example.com"], MimeMessageReader.Format(new InternetAddressList { group }));
        Assert.Equal("Team: \"Ann\" <ann@example.com>, ben@example.com;", MimeMessageReader.Format(group));
    }

    [Fact]
    public void Should_give_the_date_in_ISO_8601_with_its_offset_or_null_without_a_Date_header()
    {
        using var dated = Parsed(MimeSamples.Rich());
        using var undated = MimeSamples.Load("From: someone@example.com\r\nSubject: No date\r\n\r\nBody\r\n");

        Assert.Equal("2026-09-18T14:30:00.0000000+02:00", MimeMessageReader.FormatDate(dated));
        Assert.Null(MimeMessageReader.FormatDate(undated));
    }

    private static MimeMessage Parsed(MimeMessage message)
    {
        using (message)
            return MimeSamples.Load(MimeSamples.Text(message));
    }
}
