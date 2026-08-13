using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Config.Tests.Doubles;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Config.Tests.Presentation;

public class MountEditorModelTests
{
    private static MountDefinition Workspace(MountRights rights = MountRights.ReadOnly) => new()
    {
        PhysicalPath = "/data/workspace",
        VirtualPath = "/workspace",
        Rights = rights,
    };

    [Fact]
    public void Rows_render_the_parsed_mounts()
    {
        var document = AppSettingsDocument.Parse("""
            { "Orkeon": { "FileSystem": { "Mounts": [ "/data/workspace:/workspace:ro" ] } } }
            """);

        var model = new MountEditorModel(new FakeDirectoryProbe("/data/workspace"));
        model.LoadFrom(document);

        var row = Assert.Single(model.Rows);
        Assert.True(row.IsParsable);
        Assert.Contains("/workspace", row.Display);
        Assert.Contains("[ro]", row.Display);
    }

    [Fact]
    public void An_unparsable_entry_is_listed_flagged_and_saved_back_untouched()
    {
        var document = AppSettingsDocument.Parse("""
            { "Orkeon": { "FileSystem": { "Mounts": [ "nonsense" ] } } }
            """);

        var model = new MountEditorModel(new FakeDirectoryProbe());
        model.LoadFrom(document);

        var row = Assert.Single(model.Rows);
        Assert.False(row.IsParsable);
        Assert.NotNull(row.Error);

        model.ApplyTo(document);
        Assert.Equal(["nonsense"], document.Mounts.RawEntries);
    }

    [Fact]
    public void Add_edit_and_remove_rewrite_the_array()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var model = new MountEditorModel(new FakeDirectoryProbe("/data/workspace", "/data/out"));

        model.LoadFrom(document);
        model.Add(Workspace());
        model.Add(new MountDefinition
        {
            PhysicalPath = "/data/out",
            VirtualPath = "/output",
            Rights = MountRights.ReadWrite,
        });

        model.Replace(0, Workspace(MountRights.ReadWriteNoDelete));
        model.ApplyTo(document);

        Assert.Equal(
            ["/data/workspace:/workspace:rwnd", "/data/out:/output:rw"],
            document.Mounts.RawEntries);

        model.RemoveAt(1);
        model.ApplyTo(document);
        Assert.Single(document.Mounts.RawEntries);
    }

    [Fact]
    public void A_ro_and_a_rw_mount_are_accepted_by_the_domain_parser()
    {
        // Spec §12.3: what the editor writes must be what FileSystemMount.Parse accepts.
        var document = AppSettingsDocument.CreateEmpty();
        var model = new MountEditorModel(new FakeDirectoryProbe("/data/in", "/data/out"));

        model.Add(new MountDefinition { PhysicalPath = "/data/in", VirtualPath = "/workspace", Rights = MountRights.ReadOnly });
        model.Add(new MountDefinition { PhysicalPath = "/data/out", VirtualPath = "/output", Rights = MountRights.ReadWrite });
        model.ApplyTo(document);

        var mounts = document.Mounts.RawEntries.Select(FileSystemMount.Parse).ToList();

        Assert.Equal(FileAccessRights.ReadOnly, mounts[0].DefaultRights);
        Assert.Equal("/workspace", mounts[0].VirtualPath);
        Assert.Equal(FileAccessRights.ReadWrite, mounts[1].DefaultRights);
        Assert.Equal("/output", mounts[1].VirtualPath);
    }

    [Fact]
    public void An_absent_section_stays_absent_when_no_mount_was_added()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var model = new MountEditorModel(new FakeDirectoryProbe());

        model.LoadFrom(document);
        model.ApplyTo(document);

        Assert.False(document.ContainsPath(MountsSection.SectionPath));
    }

    [Fact]
    public void An_empty_list_that_was_present_is_written_back_as_an_empty_array()
    {
        var document = AppSettingsDocument.Parse("""
            { "Orkeon": { "FileSystem": { "Mounts": [ "/data/workspace:/workspace:ro" ] } } }
            """);

        var model = new MountEditorModel(new FakeDirectoryProbe("/data/workspace"));
        model.LoadFrom(document);
        model.RemoveAt(0);
        model.ApplyTo(document);

        Assert.True(document.ContainsPath(MountsSection.SectionPath));
        Assert.Empty(document.Mounts.RawEntries);
    }

    [Fact]
    public void Validation_reports_an_empty_list_a_missing_path_and_a_collision()
    {
        var model = new MountEditorModel(new FakeDirectoryProbe("/data/workspace"));

        Assert.Contains(model.Validate(), message => message.Code == ValidationCodes.MountsEmpty);

        model.Add(Workspace());
        Assert.Empty(model.Validate());

        model.Add(new MountDefinition
        {
            PhysicalPath = "/data/missing",
            VirtualPath = "/workspace",
            Rights = MountRights.ReadWrite,
        });

        var messages = model.Validate();
        Assert.Contains(messages, message => message.Code == ValidationCodes.MountPathMissing);
        Assert.Contains(messages, message => message.Code == ValidationCodes.MountVirtualCollision);
    }
}
