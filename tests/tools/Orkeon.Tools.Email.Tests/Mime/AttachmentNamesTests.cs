using MimeKit;
using Orkeon.Tools.Email.Mime;

namespace Orkeon.Tools.Email.Tests.Mime;

/// <summary>Attachment file names chosen by a sender, made safe to write.</summary>
public sealed class AttachmentNamesTests
{
    private static readonly string[] Duplicates = ["report.pdf", "REPORT.pdf", "report.pdf", "notes"];

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\Windows\\evil.exe", "evil.exe")]
    [InlineData("C:\\Users\\me\\report.pdf", "report.pdf")]
    [InlineData("/absolute/path/notes.txt", "notes.txt")]
    [InlineData("a/b\\c.txt", "c.txt")]
    public void Should_keep_only_the_last_path_segment(string declared, string expected)
    {
        Assert.Equal(expected, AttachmentNames.Sanitize(declared, 0, null));
    }

    [Theory]
    [InlineData("invoice\u202Efdp.exe", "invoice_fdp.exe")]
    [InlineData("report\u200B.pdf", "report_.pdf")]
    [InlineData("\u2066quote\u2069.txt", "_quote_.txt")]
    [InlineData("\uFEFFnotes.txt", "_notes.txt")]
    public void Should_replace_the_invisible_characters_that_reorder_or_hide_a_name(string declared, string expected)
    {
        Assert.Equal(expected, AttachmentNames.Sanitize(declared, 0, null));
    }

    [Theory]
    [InlineData("bell\u0007name.txt", "bell_name.txt")]
    [InlineData("new\r\nline.txt", "new__line.txt")]
    [InlineData("what?<is>*this|\"thing\":.txt", "what__is__this__thing__.txt")]
    public void Should_replace_control_and_reserved_characters(string declared, string expected)
    {
        Assert.Equal(expected, AttachmentNames.Sanitize(declared, 0, null));
    }

    [Theory]
    [InlineData(".bashrc", "bashrc")]
    [InlineData("  report.pdf. . ", "report.pdf")]
    [InlineData("...hidden..", "hidden")]
    public void Should_trim_leading_dots_and_trailing_dots_and_spaces(string declared, string expected)
    {
        Assert.Equal(expected, AttachmentNames.Sanitize(declared, 0, null));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("con.txt", "_con.txt")]
    [InlineData("NUL", "_NUL")]
    [InlineData("COM1.log", "_COM1.log")]
    [InlineData("lpt9", "_lpt9")]
    [InlineData("CON.tar.gz", "_CON.tar.gz")]
    [InlineData("nul.txt.bak", "_nul.txt.bak")]
    [InlineData("Com1.backup.zip", "_Com1.backup.zip")]
    [InlineData("AUX .txt", "_AUX .txt")]
    [InlineData("CONSOLE.txt", "CONSOLE.txt")]
    [InlineData("report.con.txt", "report.con.txt")]
    public void Should_defuse_windows_device_names(string declared, string expected)
    {
        Assert.Equal(expected, AttachmentNames.Sanitize(declared, 0, null));
    }

    [Theory]
    [InlineData(null, 0, "application/pdf", "attachment-1.pdf")]
    [InlineData("", 2, "image/png", "attachment-3.png")]
    [InlineData("...", 0, "text/plain", "attachment-1.txt")]
    [InlineData("  ", 4, "application/x-unheard-of", "attachment-5.bin")]
    [InlineData(null, 0, null, "attachment-1.bin")]
    public void Should_fall_back_to_a_numbered_name_with_the_extension_of_the_content_type(string? declared, int index, string? contentType, string expected)
    {
        Assert.Equal(expected, AttachmentNames.Sanitize(declared, index, contentType is null ? null : ContentType.Parse(contentType)));
    }

    [Fact]
    public void Should_truncate_a_long_name_and_keep_its_extension()
    {
        var name = AttachmentNames.Sanitize(new string('r', 300) + ".pdf", 0, null);

        Assert.Equal(120, name.Length);
        Assert.EndsWith("r.pdf", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_drop_an_absurdly_long_extension_when_truncating()
    {
        var name = AttachmentNames.Sanitize("a." + new string('x', 200), 0, null);

        Assert.Equal(120, name.Length);
        Assert.StartsWith("a.xxx", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_number_duplicates_and_remember_what_it_handed_out()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var names = Duplicates.Select(name => AttachmentNames.Unique(name, taken)).ToList();

        Assert.Equal(["report.pdf", "REPORT (1).pdf", "report (2).pdf", "notes"], names);
        Assert.Equal(4, taken.Count);
    }
}
