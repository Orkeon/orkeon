using Orkeon.Tools.Email.Mime;

namespace Orkeon.Host.Tests;

/// <summary>The name core shared with the e-mail tools, as the host compiles it: no MIME library behind it.</summary>
public sealed class SafeFileNamesTests
{
    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("invoice‮fdp.exe", "invoice_fdp.exe")]
    [InlineData("CON.tar.gz", "_CON.tar.gz")]
    [InlineData(null, "attachment-3.bin")]
    public void A_name_a_sender_chose_is_made_safe(string? declared, string expected) =>
        Assert.Equal(expected, SafeFileNames.Sanitize(declared, 2));

    [Fact]
    public void Duplicates_are_numbered()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.pdf" };

        Assert.Equal("A (1).pdf", SafeFileNames.Unique("A.pdf", taken));
    }

    [Fact]
    public void A_nameless_file_takes_the_extension_it_is_given() =>
        Assert.Equal("attachment-1.png", SafeFileNames.Sanitize(null, 0, ".png"));
}
