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
}
