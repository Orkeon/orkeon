using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Config.Tests.Doubles;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Config.Tests.Presentation;

public class MountFormTests
{
    [Fact]
    public void The_rights_list_offers_exactly_the_tokens_of_the_mount_format()
    {
        Assert.Equal(3, MountForm.RightsChoices.Count);
        Assert.StartsWith("ro ", MountForm.RightsChoices[0]);
        Assert.StartsWith("rw ", MountForm.RightsChoices[1]);
        Assert.StartsWith("rwnd ", MountForm.RightsChoices[2]);
    }

    [Fact]
    public void The_suggested_virtual_paths_are_the_ones_core_publishes()
    {
        Assert.Equal(MountDefinition.SuggestedVirtualPaths, MountForm.VirtualPathSuggestions);
    }

    [Fact]
    public void Build_produces_a_definition_the_domain_parser_accepts()
    {
        var form = new MountForm(new FakeDirectoryProbe("/data/in"))
        {
            PhysicalPath = "/data/in",
            VirtualPath = "/workspace",
            RightsChoiceIndex = 1,
        };

        Assert.True(form.TryBuild(out var definition, out var errors));
        Assert.Empty(errors);
        Assert.NotNull(definition);

        var mount = FileSystemMount.Parse(definition.ToMountString());
        Assert.Equal("/workspace", mount.VirtualPath);
        Assert.Equal(FileAccessRights.ReadWrite, mount.DefaultRights);
    }

    [Fact]
    public void A_virtual_path_that_does_not_start_with_a_slash_is_refused()
    {
        var form = new MountForm(new FakeDirectoryProbe("/data/in"))
        {
            PhysicalPath = "/data/in",
            VirtualPath = "workspace",
        };

        Assert.False(form.TryBuild(out _, out var errors));
        Assert.Contains(errors, error => error.Contains("workspace"));
    }

    [Fact]
    public void A_missing_physical_path_is_refused()
    {
        var form = new MountForm(new FakeDirectoryProbe()) { VirtualPath = "/workspace" };

        Assert.False(form.TryBuild(out _, out var errors));
        Assert.Contains(errors, error => error.Contains("physical path"));
    }

    [Fact]
    public void Validation_reports_a_physical_path_that_does_not_exist()
    {
        var form = new MountForm(new FakeDirectoryProbe())
        {
            PhysicalPath = "/data/missing",
            VirtualPath = "/workspace",
        };

        Assert.False(form.PhysicalPathExists);
        Assert.Contains(form.Validate(), message => message.Code == ValidationCodes.MountPathMissing);
    }

    [Fact]
    public void Creating_the_folder_clears_the_missing_path_finding()
    {
        var probe = new FakeDirectoryProbe();
        var form = new MountForm(probe)
        {
            PhysicalPath = "/data/new",
            VirtualPath = "/workspace",
        };

        Assert.True(form.TryCreatePhysicalDirectory(out var error));
        Assert.Null(error);
        Assert.Equal(["/data/new"], probe.Created);
        Assert.Empty(form.Validate());
    }

    [Fact]
    public void Sub_path_overrides_are_serialized_after_the_default_rights()
    {
        var form = new MountForm(new FakeDirectoryProbe("/data/in"))
        {
            PhysicalPath = "/data/in",
            VirtualPath = "/workspace",
            Rights = MountRights.ReadOnly,
        };

        Assert.True(form.TryAddOverride("logs", MountRights.ReadWrite, out _));
        Assert.True(form.TryBuild(out var definition, out _));
        Assert.NotNull(definition);

        Assert.Equal("/data/in:/workspace:ro;logs:rw", definition.ToMountString());

        var mount = FileSystemMount.Parse(definition.ToMountString());
        var mountOverride = Assert.Single(mount.Overrides);
        Assert.Equal("logs", mountOverride.RelativePath);
        Assert.Equal(FileAccessRights.ReadWrite, mountOverride.Rights);
    }

    [Fact]
    public void An_override_path_carrying_a_field_separator_is_refused()
    {
        var form = new MountForm(new FakeDirectoryProbe());

        Assert.False(form.TryAddOverride("logs:extra", MountRights.ReadWrite, out var error));
        Assert.NotNull(error);
        Assert.Empty(form.Overrides);
    }

    [Fact]
    public void Editing_an_existing_mount_starts_from_its_values()
    {
        var definition = MountDefinition.Parse("/data/in:/workspace:rwnd;cache:ro");

        var form = MountForm.FromDefinition(definition, new FakeDirectoryProbe("/data/in"));

        Assert.Equal("/data/in", form.PhysicalPath);
        Assert.Equal("/workspace", form.VirtualPath);
        Assert.Equal(MountRights.ReadWriteNoDelete, form.Rights);
        Assert.Equal(2, form.RightsChoiceIndex);
        Assert.Equal("cache", Assert.Single(form.Overrides).RelativePath);
    }

    [Fact]
    public void Removing_an_override_drops_it_from_the_serialized_mount()
    {
        var form = MountForm.FromDefinition(
            MountDefinition.Parse("/data/in:/workspace:ro;cache:rw"),
            new FakeDirectoryProbe("/data/in"));

        form.RemoveOverrideAt(0);

        Assert.True(form.TryBuild(out var definition, out _));
        Assert.NotNull(definition);
        Assert.Equal("/data/in:/workspace:ro", definition.ToMountString());
    }
}
