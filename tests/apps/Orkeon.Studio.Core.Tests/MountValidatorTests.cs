using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Tests.Doubles;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Mount validation mirrors what the runtime checks at boot: the list is non-empty,
/// every entry parses, every physical path exists, and virtual paths are unique.
/// </summary>
public sealed class MountValidatorTests
{
    private static readonly string[] CreatedDirectories = ["/srv/data"];

    private static MountDefinition Mount(string physical, string virtualPath, MountRights rights = MountRights.ReadOnly) =>
        new() { PhysicalPath = physical, VirtualPath = virtualPath, Rights = rights };

    [Fact]
    public void A_well_formed_list_over_existing_folders_produces_no_message()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/srv/data", "/srv/out"));

        var messages = validator.Validate(
            [Mount("/srv/data", "/workspace"), Mount("/srv/out", "/output", MountRights.ReadWrite)]);

        Assert.Empty(messages);
    }

    [Fact]
    public void A_missing_physical_path_is_an_error()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/srv/data"));

        var messages = validator.Validate([Mount("/srv/data", "/workspace"), Mount("/nope", "/output")]);

        var message = Assert.Single(messages);
        Assert.Equal(ValidationCodes.MountPathMissing, message.Code);
        Assert.Equal(ValidationSeverity.Error, message.Severity);
        Assert.Contains("/nope", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_list_is_an_error_when_at_least_one_mount_is_required()
    {
        var messages = new MountValidator(new FakeDirectoryProbe()).Validate(Array.Empty<MountDefinition>());

        Assert.Equal(ValidationCodes.MountsEmpty, Assert.Single(messages).Code);
    }

    [Fact]
    public void An_empty_list_is_accepted_when_mounts_come_from_elsewhere()
    {
        var messages = new MountValidator(new FakeDirectoryProbe())
            .Validate(Array.Empty<MountDefinition>(), requireAtLeastOne: false);

        Assert.Empty(messages);
    }

    [Fact]
    public void Two_mounts_on_the_same_virtual_path_collide()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/a", "/b"));

        var messages = validator.Validate([Mount("/a", "/workspace"), Mount("/b", "/workspace/")]);

        var message = Assert.Single(messages);
        Assert.Equal(ValidationCodes.MountVirtualCollision, message.Code);
        Assert.Contains("/workspace", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Distinct_virtual_paths_do_not_collide()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/a", "/b"));

        var messages = validator.Validate([Mount("/a", "/workspace"), Mount("/b", "/workspace-2")]);

        Assert.Empty(messages);
    }

    [Fact]
    public void An_unparsable_raw_entry_is_reported_with_its_index()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/srv/data"));

        var messages = validator.Validate(["/srv/data:/workspace:ro", "garbage"]);

        var message = Assert.Single(messages);
        Assert.Equal(ValidationCodes.MountFormat, message.Code);
        Assert.Contains("index 1", message.Text, StringComparison.Ordinal);
        Assert.Equal("garbage", message.Path);
    }

    [Fact]
    public void An_invalid_virtual_path_typed_in_the_form_is_reported_before_the_parser_sees_it()
    {
        var validator = new MountValidator(new FakeDirectoryProbe("/srv/data"));

        var messages = validator.Validate([Mount("/srv/data", "workspace")]);

        Assert.Equal(ValidationCodes.MountFormat, Assert.Single(messages).Code);
    }

    [Fact]
    public void Raw_entries_are_validated_against_the_disk_too()
    {
        var validator = new MountValidator(new FakeDirectoryProbe());

        var messages = validator.Validate(["/srv/data:/workspace:ro"]);

        Assert.Equal(ValidationCodes.MountPathMissing, Assert.Single(messages).Code);
    }

    [Fact]
    public void Creating_the_missing_folder_clears_the_error()
    {
        var probe = new FakeDirectoryProbe();
        var validator = new MountValidator(probe);

        Assert.NotEmpty(validator.Validate(["/srv/data:/workspace:ro"]));

        probe.Create("/srv/data");

        Assert.Empty(validator.Validate(["/srv/data:/workspace:ro"]));
        Assert.Equal(CreatedDirectories, probe.Created);
    }
}
