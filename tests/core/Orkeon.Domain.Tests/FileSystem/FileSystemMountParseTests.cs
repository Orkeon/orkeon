using Orkeon.Domain.FileSystem;

namespace Orkeon.Domain.Tests.FileSystem;

public class FileSystemMountParseTests
{
    [Fact]
    public void Parse_SimpleUnixMount_ParsesCorrectly()
    {
        var mount = FileSystemMount.Parse("/home/user:/workspace:rw");

        Assert.Equal("/home/user", mount.BasePath);
        Assert.Equal("/workspace", mount.VirtualPath);
        Assert.Equal(FileAccessRights.ReadWrite, mount.DefaultRights);
        Assert.Empty(mount.Overrides);
    }

    [Fact]
    public void Parse_WindowsDriveLetter_ParsesCorrectly()
    {
        var mount = FileSystemMount.Parse(@"C:\Users\Cyril:/workspace:rw");

        Assert.Equal(@"C:\Users\Cyril", mount.BasePath);
        Assert.Equal("/workspace", mount.VirtualPath);
        Assert.Equal(FileAccessRights.ReadWrite, mount.DefaultRights);
    }

    [Fact]
    public void Parse_WithSingleOverride_ParsesCorrectly()
    {
        var mount = FileSystemMount.Parse("/home/user:/workspace:rw;vendor:ro");

        Assert.Equal("/home/user", mount.BasePath);
        Assert.Equal("/workspace", mount.VirtualPath);
        Assert.Equal(FileAccessRights.ReadWrite, mount.DefaultRights);
        Assert.Single(mount.Overrides);
        Assert.Equal("vendor", mount.Overrides[0].RelativePath);
        Assert.Equal(FileAccessRights.ReadOnly, mount.Overrides[0].Rights);
    }

    [Fact]
    public void Parse_WithMultipleOverrides_ParsesCorrectly()
    {
        var mount = FileSystemMount.Parse("/home/user:/workspace:rw;vendor:ro;vendor/patches:rw");

        Assert.Equal(2, mount.Overrides.Count);
        // Sorted by length descending: vendor/patches (15) before vendor (6)
        Assert.Equal("vendor/patches", mount.Overrides[0].RelativePath);
        Assert.Equal(FileAccessRights.ReadWrite, mount.Overrides[0].Rights);
        Assert.Equal("vendor", mount.Overrides[1].RelativePath);
        Assert.Equal(FileAccessRights.ReadOnly, mount.Overrides[1].Rights);
    }

    [Fact]
    public void Parse_WindowsWithOverrides_ParsesCorrectly()
    {
        var mount = FileSystemMount.Parse(@"C:\Data:/data:ro;staging:rwnd");

        Assert.Equal(@"C:\Data", mount.BasePath);
        Assert.Equal("/data", mount.VirtualPath);
        Assert.Equal(FileAccessRights.ReadOnly, mount.DefaultRights);
        Assert.Single(mount.Overrides);
        Assert.Equal("staging", mount.Overrides[0].RelativePath);
        Assert.Equal(FileAccessRights.ReadWriteNoDelete, mount.Overrides[0].Rights);
    }

    [Theory]
    [InlineData("ro", FileAccessRights.ReadOnly)]
    [InlineData("rw", FileAccessRights.ReadWrite)]
    [InlineData("rwnd", FileAccessRights.ReadWriteNoDelete)]
    public void Parse_RightsMapping_ParsesCorrectly(string rightsStr, FileAccessRights expected)
    {
        var mount = FileSystemMount.Parse($"/src:/virt:{rightsStr}");
        Assert.Equal(expected, mount.DefaultRights);
    }

    [Fact]
    public void Parse_InvalidFormat_MissingSegments_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => FileSystemMount.Parse("invalid"));
    }

    [Fact]
    public void Parse_UnknownRights_ThrowsFormatException()
    {
        var ex = Assert.Throws<FormatException>(() => FileSystemMount.Parse("/home:/ws:xyz"));
        Assert.Contains("Unknown rights value", ex.Message);
        Assert.Contains("xyz", ex.Message);
    }

    [Fact]
    public void Parse_VirtualPathWithoutSlash_ThrowsFormatException()
    {
        var ex = Assert.Throws<FormatException>(() => FileSystemMount.Parse("/home:workspace:rw"));
        Assert.Contains("Virtual path must start with '/'", ex.Message);
    }

    [Fact]
    public void Parse_OverridesSortedByLengthDescending()
    {
        var mount = FileSystemMount.Parse("/src:/ws:rw;a:ro;aaa:ro;aa:ro");

        Assert.Equal(3, mount.Overrides.Count);
        Assert.Equal("aaa", mount.Overrides[0].RelativePath);
        Assert.Equal("aa", mount.Overrides[1].RelativePath);
        Assert.Equal("a", mount.Overrides[2].RelativePath);
    }

    [Fact]
    public void Parse_NullOrEmpty_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => FileSystemMount.Parse(null!));
        Assert.ThrowsAny<ArgumentException>(() => FileSystemMount.Parse(""));
        Assert.ThrowsAny<ArgumentException>(() => FileSystemMount.Parse("   "));
    }

    /// <summary>
    /// A ':' inside a path is escaped rather than guessed at. The drive-letter form keeps
    /// working unescaped — it is the common case and carries no ambiguity in practice.
    /// </summary>
    [Fact]
    public void Parse_EscapedColonInPhysicalPath_IsLiteral()
    {
        var mount = FileSystemMount.Parse(@"/data/odd\:name:/data:ro");

        Assert.Equal("/data/odd:name", mount.BasePath);
        Assert.Equal("/data", mount.VirtualPath);
        Assert.Equal(FileAccessRights.ReadOnly, mount.DefaultRights);
    }

    [Fact]
    public void Parse_EscapedSemicolonIsNotAnOverrideSeparator()
    {
        var mount = FileSystemMount.Parse(@"/data/a\;b:/data:ro;sub:rw");

        Assert.Equal("/data/a;b", mount.BasePath);
        var over = Assert.Single(mount.Overrides);
        Assert.Equal("sub", over.RelativePath);
    }

    /// <summary>
    /// A backslash is only special before a separator, so ordinary Windows paths need no
    /// escaping — and a path that really ends in a backslash writes it doubled.
    /// </summary>
    [Fact]
    public void Parse_BackslashesInWindowsPathsAreLiteral()
    {
        var plain = FileSystemMount.Parse(@"C:\src\sub:/workspace:ro");
        Assert.Equal(@"C:\src\sub", plain.BasePath);

        var trailing = FileSystemMount.Parse(@"C:\src\\:/workspace:ro");
        Assert.Equal(@"C:\src\", trailing.BasePath);
    }

    /// <summary>
    /// The one place that answers "which folder does this spec mount?". Three call sites used
    /// to re-invent the split; the one that did it by taking everything before the first ':'
    /// turned <c>C:\src:/workspace:ro</c> into the physical path <c>"C"</c>.
    /// </summary>
    [Theory]
    [InlineData(@"C:\src:/workspace:ro", @"C:\src")]
    [InlineData("C:/src:/workspace:ro", "C:/src")]
    [InlineData("/data/src:/workspace:rw", "/data/src")]
    [InlineData(@"/data/odd\:name:/data:ro", "/data/odd:name")]
    [InlineData("/data:/ws:rw;sub:ro", "/data")]
    public void TryGetBasePath_reads_the_physical_segment(string spec, string expected) =>
        Assert.Equal(expected, FileSystemMount.TryGetBasePath(spec));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-separator-at-all")]
    public void TryGetBasePath_returns_null_for_anything_it_cannot_read(string spec) =>
        Assert.Null(FileSystemMount.TryGetBasePath(spec));

    [Fact]
    public void WithBasePath_replaces_the_folder_and_escapes_it()
    {
        Assert.Equal(
            @"C:\elsewhere:/workspace:ro",
            FileSystemMount.WithBasePath(@"C:\src:/workspace:ro", @"C:\elsewhere"));

        // The replacement is escaped, so the result survives a round trip.
        var rewritten = FileSystemMount.WithBasePath("/a:/workspace:ro", "/odd:name");
        Assert.Equal("/odd:name", FileSystemMount.Parse(rewritten).BasePath);

        // Overrides are preserved.
        Assert.Equal(
            "/b:/ws:rw;sub:ro",
            FileSystemMount.WithBasePath("/a:/ws:rw;sub:ro", "/b"));
    }

    [Fact]
    public void Escape_round_trips_every_separator()
    {
        const string awkward = @"/a:b;c\d";
        var mount = FileSystemMount.Parse($"{FileSystemMount.Escape(awkward)}:/data:ro");

        Assert.Equal(awkward, mount.BasePath);
    }
}
