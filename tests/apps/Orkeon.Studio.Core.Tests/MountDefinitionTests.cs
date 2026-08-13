using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Acceptance criterion of SPEC §12.3: mounts built through the editor's fields must
/// be accepted, unchanged, by the parser the runtime uses.
/// </summary>
public sealed class MountDefinitionTests
{
    private static readonly string[] OverridesByDescendingLength = ["build/cache", "build"];

    private static readonly string[] SuggestedVirtualPaths = ["/workspace", "/output", "/tmp"];

    private static readonly string[] RightsTokens = ["ro", "rw", "rwnd"];

    [Fact]
    public void A_read_only_mount_built_from_the_form_is_accepted_by_the_runtime_parser()
    {
        var definition = new MountDefinition
        {
            PhysicalPath = "/srv/data",
            VirtualPath = "/workspace",
            Rights = MountRights.ReadOnly,
        };

        var serialized = definition.ToMountString();
        var mount = FileSystemMount.Parse(serialized);

        Assert.Equal("/srv/data:/workspace:ro", serialized);
        Assert.Equal("/srv/data", mount.BasePath);
        Assert.Equal("/workspace", mount.VirtualPath);
        Assert.Equal(FileAccessRights.ReadOnly, mount.DefaultRights);
    }

    [Fact]
    public void A_read_write_mount_built_from_the_form_is_accepted_by_the_runtime_parser()
    {
        var definition = new MountDefinition
        {
            PhysicalPath = "/srv/out",
            VirtualPath = "/output",
            Rights = MountRights.ReadWrite,
        };

        var mount = FileSystemMount.Parse(definition.ToMountString());

        Assert.Equal("/srv/out:/output:rw", definition.ToMountString());
        Assert.Equal(FileAccessRights.ReadWrite, mount.DefaultRights);
    }

    [Fact]
    public void Sub_path_overrides_survive_the_round_trip()
    {
        var definition = new MountDefinition
        {
            PhysicalPath = "/srv/data",
            VirtualPath = "/workspace",
            Rights = MountRights.ReadOnly,
            Overrides =
            [
                new SubPathRightsOverride("build", MountRights.ReadWrite),
                new SubPathRightsOverride("build/cache", MountRights.ReadWriteNoDelete),
            ],
        };

        var serialized = definition.ToMountString();
        var reparsed = MountDefinition.Parse(serialized);

        Assert.Equal("/srv/data:/workspace:ro;build:rw;build/cache:rwnd", serialized);
        // The domain sorts overrides by descending path length (longest match wins).
        Assert.Equal(
            OverridesByDescendingLength,
            reparsed.Overrides.Select(o => o.RelativePath));
        Assert.Equal(MountRights.ReadWriteNoDelete, reparsed.Overrides[0].Rights);
        Assert.Equal(MountRights.ReadWrite, reparsed.Overrides[1].Rights);
    }

    [Fact]
    public void A_windows_mount_round_trips_through_the_domain_parser()
    {
        var definition = new MountDefinition
        {
            PhysicalPath = @"C:\src",
            VirtualPath = "/workspace",
            Rights = MountRights.ReadWriteNoDelete,
        };

        var mount = definition.ToDomainMount();

        Assert.Equal(@"C:\src", mount.BasePath);
        Assert.Equal("/workspace", mount.VirtualPath);
        Assert.Equal(FileAccessRights.ReadWriteNoDelete, mount.DefaultRights);
    }

    [Fact]
    public void An_identity_mapped_windows_mount_round_trips()
    {
        var definition = new MountDefinition
        {
            PhysicalPath = @"C:\src",
            VirtualPath = @"C:\src",
            Rights = MountRights.ReadOnly,
        };

        var mount = definition.ToDomainMount();

        Assert.Equal(@"C:\src", mount.VirtualPath);
    }

    [Theory]
    [InlineData("/srv:/workspace:ro", MountRights.ReadOnly)]
    [InlineData("/srv:/workspace:rw", MountRights.ReadWrite)]
    [InlineData("/srv:/workspace:rwnd", MountRights.ReadWriteNoDelete)]
    public void Every_rights_token_parses_back_to_its_editor_value(string mountString, MountRights expected)
    {
        Assert.True(MountDefinition.TryParse(mountString, out var definition, out var error));
        Assert.Null(error);
        Assert.NotNull(definition);
        Assert.Equal(expected, definition.Rights);
        Assert.Equal(mountString, definition.ToMountString());
    }

    [Fact]
    public void An_unknown_rights_token_is_reported_with_the_valid_values()
    {
        Assert.False(MountDefinition.TryParse("/srv:/workspace:rwx", out _, out var error));

        Assert.Contains("ro, rw, rwnd", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_virtual_path_that_is_neither_rooted_nor_a_drive_is_rejected()
    {
        Assert.False(MountDefinition.TryParse("/srv:workspace:ro", out _, out var error));
        Assert.False(MountDefinition.IsValidVirtualPath("workspace"));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("/workspace", true)]
    [InlineData("/", true)]
    [InlineData(@"C:\src", true)]
    [InlineData("C:/src", true)]
    [InlineData("workspace", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Virtual_path_validity_follows_the_domain_rule(string? virtualPath, bool expected) =>
        Assert.Equal(expected, MountDefinition.IsValidVirtualPath(virtualPath));

    [Fact]
    public void Suggested_virtual_paths_are_the_documented_ones()
    {
        Assert.Equal(SuggestedVirtualPaths, MountDefinition.SuggestedVirtualPaths);
        Assert.All(MountDefinition.SuggestedVirtualPaths, p => Assert.True(MountDefinition.IsValidVirtualPath(p)));
    }

    [Fact]
    public void The_rights_drop_down_offers_exactly_the_tokens_the_runtime_accepts()
    {
        Assert.Equal(RightsTokens, MountRightsTokens.Tokens);
        Assert.All(
            MountRightsTokens.Choices,
            choice =>
            {
                Assert.Equal(choice.Token, MountRightsTokens.ToToken(choice.Rights));
                Assert.False(string.IsNullOrWhiteSpace(choice.Label));
            });
    }
}
