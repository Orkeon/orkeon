using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Run.Tests.Doubles;
using Orkeon.Studio.Run.Views;

namespace Orkeon.Studio.Run.Tests.Views;

/// <summary>
/// The launcher's mount form, built without a terminal. It has to offer the same affordances
/// as the appsettings editor's (spec §4.5): the physical folder is picked or created rather
/// than typed blind, and the overrides field is read by the shared Core parser.
/// </summary>
public class MountFormDialogTests
{
    [Fact]
    public void The_status_line_says_when_the_physical_folder_does_not_exist_yet()
    {
        using var dialog = new MountFormDialog(existing: null, new FakeDirectoryProbe());

        dialog.FillForTest("/data", "/workspace");

        Assert.Contains("does not exist", dialog.PhysicalStatusText, StringComparison.Ordinal);
        Assert.Contains("Create folder", dialog.PhysicalStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void Creating_the_folder_makes_it_exist_and_says_so()
    {
        var directories = new FakeDirectoryProbe();
        using var dialog = new MountFormDialog(existing: null, directories);
        dialog.FillForTest("/data", "/workspace");

        dialog.CreateFolderForTest();

        Assert.Equal(["/data"], directories.Created);
        Assert.Contains("The folder exists", dialog.PhysicalStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void Creating_a_folder_with_no_path_reports_the_reason_instead_of_throwing()
    {
        var directories = new FakeDirectoryProbe();
        using var dialog = new MountFormDialog(existing: null, directories);
        dialog.FillForTest("   ", "/workspace");

        dialog.CreateFolderForTest();

        Assert.Empty(directories.Created);
        Assert.Contains("Pick a physical path first", dialog.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_filled_form_produces_the_mount_string_the_runtime_parses()
    {
        using var dialog = new MountFormDialog(existing: null, new FakeDirectoryProbe("/data"));
        dialog.FillForTest("/data", "/workspace", "docs:ro");

        dialog.AcceptForTest();

        Assert.NotNull(dialog.Mount);
        Assert.Equal("/data", dialog.Mount!.PhysicalPath);
        Assert.Equal("/workspace", dialog.Mount.VirtualPath);
        var single = Assert.Single(dialog.Mount.Overrides);
        Assert.Equal("docs", single.RelativePath);
        Assert.Equal(MountRights.ReadOnly, single.Rights);
    }

    [Fact]
    public void A_malformed_override_line_is_reported_and_the_form_stays_open()
    {
        using var dialog = new MountFormDialog(existing: null, new FakeDirectoryProbe("/data"));
        dialog.FillForTest("/data", "/workspace", "nonsense");

        dialog.AcceptForTest();

        Assert.Null(dialog.Mount);
        Assert.Contains("nonsense", dialog.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_virtual_path_is_refused_by_the_domain_parser()
    {
        using var dialog = new MountFormDialog(existing: null, new FakeDirectoryProbe("/data"));
        dialog.FillForTest("/data", "workspace");

        dialog.AcceptForTest();

        Assert.Null(dialog.Mount);
        Assert.NotEmpty(dialog.ErrorText);
    }

    [Fact]
    public void An_existing_mount_is_shown_with_its_overrides_spelled_out()
    {
        var existing = new MountDefinition
        {
            PhysicalPath = "/data",
            VirtualPath = "/workspace",
            Rights = MountRights.ReadWrite,
            Overrides = [new SubPathRightsOverride("docs", MountRights.ReadOnly)],
        };

        using var dialog = new MountFormDialog(existing, new FakeDirectoryProbe("/data"));

        Assert.Contains("The folder exists", dialog.PhysicalStatusText, StringComparison.Ordinal);

        // Round-tripping the form must give the mount back unchanged, overrides included.
        dialog.AcceptForTest();
        Assert.Equal(existing.ToMountString(), dialog.Mount!.ToMountString());
    }
}
