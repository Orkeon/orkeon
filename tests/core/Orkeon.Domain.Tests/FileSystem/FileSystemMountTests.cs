using Orkeon.Domain.FileSystem;

namespace Orkeon.Domain.Tests.FileSystem;

public class FileSystemMountTests
{
    [Fact]
    public void ResolveRights_NoOverrides_ReturnsDefaultRights()
    {
        var mount = new FileSystemMount("/base", "/ws", FileAccessRights.ReadWrite);

        var rights = mount.ResolveRights("src/file.cs");

        Assert.Equal(FileAccessRights.ReadWrite, rights);
    }

    [Fact]
    public void ResolveRights_MatchingOverride_ReturnsOverrideRights()
    {
        var overrides = new List<SubPathOverride>
        {
            new("vendor", FileAccessRights.ReadOnly)
        };
        var mount = new FileSystemMount("/base", "/ws", FileAccessRights.ReadWrite, overrides);

        var rights = mount.ResolveRights("vendor/lib.dll");

        Assert.Equal(FileAccessRights.ReadOnly, rights);
    }

    [Fact]
    public void ResolveRights_NestedOverride_MostSpecificWins()
    {
        var overrides = new List<SubPathOverride>
        {
            new("vendor", FileAccessRights.ReadOnly),
            new("vendor/patches", FileAccessRights.ReadWrite)
        };
        var mount = new FileSystemMount("/base", "/ws", FileAccessRights.ReadWrite, overrides);

        var rights = mount.ResolveRights("vendor/patches/fix.diff");

        Assert.Equal(FileAccessRights.ReadWrite, rights);
    }

    [Fact]
    public void ResolveRights_BoundaryCheck_VendorToolsDoesNotMatchVendor()
    {
        var overrides = new List<SubPathOverride>
        {
            new("vendor", FileAccessRights.ReadOnly)
        };
        var mount = new FileSystemMount("/base", "/ws", FileAccessRights.ReadWrite, overrides);

        var rights = mount.ResolveRights("vendor-tools/file.cs");

        Assert.Equal(FileAccessRights.ReadWrite, rights);
    }

    [Fact]
    public void ResolveRights_ExactDirectoryMatch_ReturnsOverrideRights()
    {
        var overrides = new List<SubPathOverride>
        {
            new("vendor", FileAccessRights.ReadOnly)
        };
        var mount = new FileSystemMount("/base", "/ws", FileAccessRights.ReadWrite, overrides);

        var rights = mount.ResolveRights("vendor");

        Assert.Equal(FileAccessRights.ReadOnly, rights);
    }

    [Fact]
    public void ResolveRights_BackslashNormalization_MatchesOverride()
    {
        var overrides = new List<SubPathOverride>
        {
            new("vendor", FileAccessRights.ReadOnly)
        };
        var mount = new FileSystemMount("/base", "/ws", FileAccessRights.ReadWrite, overrides);

        var rights = mount.ResolveRights(@"vendor\lib.dll");

        Assert.Equal(FileAccessRights.ReadOnly, rights);
    }

    [Fact]
    public void ResolveRights_NoMatchingOverride_ReturnsDefault()
    {
        var overrides = new List<SubPathOverride>
        {
            new("vendor", FileAccessRights.ReadOnly)
        };
        var mount = new FileSystemMount("/base", "/ws", FileAccessRights.ReadWrite, overrides);

        var rights = mount.ResolveRights("src/main.cs");

        Assert.Equal(FileAccessRights.ReadWrite, rights);
    }

    [Fact]
    public void Constructor_VirtualPathWithoutSlash_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() =>
            new FileSystemMount("/base", "workspace", FileAccessRights.ReadWrite));
    }

    [Fact]
    public void Constructor_NullBasePath_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new FileSystemMount(null!, "/ws", FileAccessRights.ReadWrite));
    }

    [Fact]
    public void Constructor_EmptyVirtualPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new FileSystemMount("/base", "", FileAccessRights.ReadWrite));
    }

    [Fact]
    public void Constructor_NullOverrides_DefaultsToEmpty()
    {
        var mount = new FileSystemMount("/base", "/ws", FileAccessRights.ReadWrite, null);

        Assert.Empty(mount.Overrides);
    }

    [Fact]
    public void Constructor_EmptyOverridesList_DefaultsToEmpty()
    {
        var mount = new FileSystemMount("/base", "/ws", FileAccessRights.ReadWrite,
            new List<SubPathOverride>());

        Assert.Empty(mount.Overrides);
    }
}
