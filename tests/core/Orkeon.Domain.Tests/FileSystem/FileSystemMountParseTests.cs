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
        var mount = FileSystemMount.Parse(@"C:\Users\Demo:/workspace:rw");

        Assert.Equal(@"C:\Users\Demo", mount.BasePath);
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
    /// A path the bare grammar cannot express is quoted, not escaped: a backslash escape would
    /// collide with the Windows path separator, which is the very thing that has to survive.
    /// </summary>
    [Fact]
    public void Parse_QuotedPhysicalPath_IsTakenLiterally()
    {
        var mount = FileSystemMount.Parse(@"""/data/odd:name"":/data:ro");

        Assert.Equal("/data/odd:name", mount.BasePath);
        Assert.Equal("/data", mount.VirtualPath);
        Assert.Equal(FileAccessRights.ReadOnly, mount.DefaultRights);
    }

    [Fact]
    public void Parse_QuotedSemicolonIsNotAnOverrideSeparator()
    {
        var mount = FileSystemMount.Parse(@"""/data/a;b"":/data:ro;sub:rw");

        Assert.Equal("/data/a;b", mount.BasePath);
        var over = Assert.Single(mount.Overrides);
        Assert.Equal("sub", over.RelativePath);
    }

    /// <summary>
    /// The cases the previous backslash-escape grammar could not express at all: a drive root
    /// and a path ending in the Windows separator. Both are ordinary folders.
    /// </summary>
    [Theory]
    [InlineData(@"""C:\"":/workspace:ro", @"C:\")]
    [InlineData(@"""C:\src\"":/workspace:ro", @"C:\src\")]
    [InlineData(@"""C:\src"":/workspace:ro", @"C:\src")]
    public void Parse_QuotedWindowsPaths(string spec, string expected) =>
        Assert.Equal(expected, FileSystemMount.Parse(spec).BasePath);

    /// <summary>Backslashes are ordinary characters again — nothing to escape, ever.</summary>
    [Fact]
    public void Parse_BackslashesAreLiteral()
    {
        Assert.Equal(@"C:\src\sub", FileSystemMount.Parse(@"C:\src\sub:/workspace:ro").BasePath);
        Assert.Equal(@"/data\odd", FileSystemMount.Parse(@"/data\odd:/data:ro").BasePath);
    }

    [Fact]
    public void Parse_UnterminatedQuote_ThrowsWithTheRemedy()
    {
        var ex = Assert.Throws<FormatException>(() => FileSystemMount.Parse(@"""C:\src:/workspace:ro"));
        Assert.Contains("quoted", ex.Message, StringComparison.Ordinal);
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
    [InlineData(@"""/data/odd:name"":/data:ro", "/data/odd:name")]
    [InlineData(@"""C:\src\"":/workspace:ro", @"C:\src\")]
    [InlineData("/data:/ws:rw;sub:ro", "/data")]
    public void TryGetBasePath_reads_the_physical_segment(string spec, string expected) =>
        Assert.Equal(expected, FileSystemMount.TryGetBasePath(spec));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-separator-at-all")]
    // A blank physical segment names no folder: callers resolve the answer against the working
    // directory, and Path.GetFullPath("") throws where null sends them to the parser's message.
    [InlineData(":/workspace:ro")]
    [InlineData("  :/workspace:ro")]
    public void TryGetBasePath_returns_null_for_anything_it_cannot_read(string spec) =>
        Assert.Null(FileSystemMount.TryGetBasePath(spec));

    [Fact]
    public void WithBasePath_replaces_the_folder_and_quotes_it_only_when_needed()
    {
        // A plain Windows path stays exactly as it reads.
        Assert.Equal(
            @"C:\elsewhere:/workspace:ro",
            FileSystemMount.WithBasePath(@"C:\src:/workspace:ro", @"C:\elsewhere"));

        // One that cannot be written bare is quoted, and survives a round trip.
        var rewritten = FileSystemMount.WithBasePath("/a:/workspace:ro", "/odd:name");
        Assert.Equal(@"""/odd:name"":/workspace:ro", rewritten);
        Assert.Equal("/odd:name", FileSystemMount.Parse(rewritten).BasePath);

        // Overrides are preserved.
        Assert.Equal(
            "/b:/ws:rw;sub:ro",
            FileSystemMount.WithBasePath("/a:/ws:rw;sub:ro", "/b"));
    }

    [Theory]
    [InlineData(@"C:\src", @"C:\src")]                     // bare: nothing to do
    [InlineData("/data/src", "/data/src")]                 // bare
    [InlineData("/odd:name", @"""/odd:name""")]            // separator inside
    [InlineData("/a;b", @"""/a;b""")]                      // override separator inside
    [InlineData(@"C:\src\", @"""C:\src\""")]               // trailing separator
    public void Quote_only_wraps_what_the_grammar_cannot_read_bare(string path, string expected) =>
        Assert.Equal(expected, FileSystemMount.Quote(path));

    [Fact]
    public void Quote_round_trips_every_awkward_path()
    {
        foreach (var awkward in new[] { @"/a:b;c\d", @"C:\src\", @"C:\", "/plain" })
        {
            var mount = FileSystemMount.Parse($"{FileSystemMount.Quote(awkward)}:/data:ro");
            Assert.Equal(awkward, mount.BasePath);
        }
    }
}

/// <summary>
/// VFS-90: the optional <c>&lt;ulid&gt;|</c> prefix that gives a settings entry its identity.
/// </summary>
public class FileSystemMountIdPrefixTests
{
    private const string Id = "01J9Z3K4M5N6P7Q8R9S0T1V2W3";

    [Fact]
    public void Parse_IdPrefix_IsReadAsMountId()
    {
        var mount = FileSystemMount.Parse($"{Id}|/srv/data:/data:ro");

        Assert.Equal(Id, mount.Id!.ToString());
        Assert.Equal("/srv/data", mount.BasePath);
        Assert.Equal("/data", mount.VirtualPath);
        Assert.Equal(FileAccessRights.ReadOnly, mount.DefaultRights);
    }

    [Fact]
    public void Parse_IdPrefix_WithQuotedWindowsPathAndOverrides_ParsesCorrectly()
    {
        var mount = FileSystemMount.Parse($"{Id}|\"C:\\Users\\Demo\\\":/workspace:rw;vendor:ro");

        Assert.Equal(Id, mount.Id!.ToString());
        Assert.Equal(@"C:\Users\Demo\", mount.BasePath);
        Assert.Single(mount.Overrides);
    }

    [Fact]
    public void Parse_WithoutPrefix_HasNoId()
    {
        Assert.Null(FileSystemMount.Parse("/srv/data:/data:ro").Id);
        Assert.Null(FileSystemMount.Parse(@"C:\data:/data:ro").Id);
    }

    [Theory]
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2W")]     // 25 characters
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2WI")]    // I is not Crockford
    [InlineData("abc123")]                        // a bare token that is no id at all
    public void Parse_BadIdPrefix_ThrowsWithTheRemedy(string token)
    {
        var failure = Assert.Throws<FormatException>(() => FileSystemMount.Parse($"{token}|/srv/data:/data:ro"));

        Assert.Contains($"Invalid mount id '{token}'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("26 Crockford base32 characters", failure.Message, StringComparison.Ordinal);
        Assert.Contains("quoted", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"/data/odd|name\":/x:ro", "/data/odd|name")]   // quoted: the pipe is literal
    [InlineData("/data/odd|name:/x:ro", "/data/odd|name")]         // rooted: cannot be an id token
    [InlineData("./odd|name:/x:ro", "./odd|name")]                 // relative: cannot be an id token
    [InlineData(@"C:\odd|name:/x:ro", @"C:\odd|name")]             // drive letter: cannot be an id token
    public void Parse_PipeInsideAPath_IsNotAnIdPrefix(string spec, string expectedBasePath)
    {
        var mount = FileSystemMount.Parse(spec);

        Assert.Null(mount.Id);
        Assert.Equal(expectedBasePath, mount.BasePath);
    }

    [Theory]
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2W3|/srv:/data:ro", "01J9Z3K4M5N6P7Q8R9S0T1V2W3")]
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2W3|this is not even a mount", "01J9Z3K4M5N6P7Q8R9S0T1V2W3")]
    [InlineData("/srv:/data:ro", null)]
    [InlineData("nope|/srv:/data:ro", null)]
    [InlineData("", null)]
    public void TryGetId_reads_the_prefix_without_parsing_the_rest(string spec, string? expected) =>
        Assert.Equal(expected, FileSystemMount.TryGetId(spec)?.ToString());

    [Fact]
    public void TryGetBasePath_skips_the_id_and_WithBasePath_keeps_it()
    {
        var spec = $"{Id}|./output:/output:rw;cache:ro";

        Assert.Equal("./output", FileSystemMount.TryGetBasePath(spec));

        var rebased = FileSystemMount.WithBasePath(spec, "/teams/veille/output");
        Assert.Equal($"{Id}|/teams/veille/output:/output:rw;cache:ro", rebased);
        Assert.Equal(Id, FileSystemMount.Parse(rebased).Id!.ToString());
    }

    [Theory]
    [InlineData("/srv/data:/data:ro")]
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2W3|/srv/data:/data:rw")]
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2W3|\"C:\\src\\\":/workspace:rwnd;vendor/patches:rw;vendor:ro")]
    [InlineData("\"abc123|odd\":/x:ro")]
    public void ToMountString_round_trips(string spec)
    {
        var parsed = FileSystemMount.Parse(spec);

        var written = parsed.ToMountString();
        var reparsed = FileSystemMount.Parse(written);

        // Member by member: the record holds a list, whose equality is by reference.
        Assert.Equal(parsed.Id, reparsed.Id);
        Assert.Equal(parsed.BasePath, reparsed.BasePath);
        Assert.Equal(parsed.VirtualPath, reparsed.VirtualPath);
        Assert.Equal(parsed.DefaultRights, reparsed.DefaultRights);
        Assert.Equal(parsed.Overrides, reparsed.Overrides);
        Assert.Equal(written, reparsed.ToMountString());
    }

    [Fact]
    public void Quote_wraps_a_bare_token_followed_by_a_pipe_so_it_never_reads_as_an_id()
    {
        Assert.Equal("\"abc123|odd\"", FileSystemMount.Quote("abc123|odd"));
        Assert.Equal("/data/odd|name", FileSystemMount.Quote("/data/odd|name"));
    }

    [Theory]
    [InlineData(FileAccessRights.ReadOnly, "ro")]
    [InlineData(FileAccessRights.ReadWrite, "rw")]
    [InlineData(FileAccessRights.ReadWriteNoDelete, "rwnd")]
    public void FormatRights_spells_the_grammar_tokens(FileAccessRights rights, string token) =>
        Assert.Equal(token, FileSystemMount.FormatRights(rights));

    [Fact]
    public void FormatRights_refuses_rights_the_grammar_cannot_spell() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FileSystemMount.FormatRights(FileAccessRights.Write));
}
