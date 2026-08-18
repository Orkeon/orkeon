using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Mounts;

namespace Orkeon.Studio.Wpf.Tests;

public sealed class MountEditorViewModelTests
{
    [Fact]
    public void Should_SerializeToTheDomainFormat_When_FormIsFilled()
    {
        var mount = new MountEditorViewModel
        {
            PhysicalPath = "/data/projects",
            VirtualPath = "/workspace",
            Rights = MountRights.ReadWrite,
        };

        Assert.Equal("/data/projects:/workspace:rw", mount.MountString);
    }

    [Fact]
    public void Should_SerializeOverrides_When_SubPathsHaveTheirOwnRights()
    {
        var mount = new MountEditorViewModel
        {
            PhysicalPath = "/data",
            VirtualPath = "/workspace",
            Rights = MountRights.ReadOnly,
        };
        mount.Overrides.Add(new SubPathOverrideViewModel { RelativePath = "out", Rights = MountRights.ReadWrite });

        Assert.Equal("/data:/workspace:ro;out:rw", mount.MountString);
    }

    [Fact]
    public void Should_RoundTrip_When_BuiltFromARawEntry()
    {
        const string entry = "/data:/workspace:rwnd;cache:rw";

        var mount = MountEditorViewModel.FromRaw(entry);

        Assert.True(mount.IsParsed);
        Assert.Equal(MountRights.ReadWriteNoDelete, mount.Rights);
        Assert.Equal("cache", Assert.Single(mount.Overrides).RelativePath);
        Assert.Equal(entry, mount.MountString);
    }

    [Fact]
    public void Should_KeepTheTextVerbatim_When_TheEntryCannotBeParsed()
    {
        // Round-trip safety: a hand-written entry Studio does not understand is preserved, not dropped.
        const string entry = "this is not a mount";

        var mount = MountEditorViewModel.FromRaw(entry);

        Assert.False(mount.IsParsed);
        Assert.Equal(entry, mount.RawText);
        Assert.Equal(entry, mount.MountString);
    }

    [Fact]
    public void Should_RejectAVirtualPath_When_ItDoesNotStartWithASlash()
    {
        var mount = new MountEditorViewModel { PhysicalPath = "/data", VirtualPath = "workspace" };

        Assert.False(mount.IsVirtualPathValid);
    }

    [Fact]
    public void Should_OfferOnlyTheThreeDomainTokens()
    {
        Assert.Equal(["ro", "rw", "rwnd"], new MountEditorViewModel().RightsChoices.Select(c => c.Token));
    }

    [Fact]
    public void Should_RaiseEdited_When_AnOverrideChanges()
    {
        var mount = new MountEditorViewModel { PhysicalPath = "/data", VirtualPath = "/workspace" };
        var item = mount.AddOverride();
        var raised = 0;
        mount.Edited += (_, _) => raised++;

        item.RelativePath = "logs";

        Assert.True(raised > 0);
    }
}

public sealed class MountsEditorViewModelTests
{
    [Fact]
    public void Should_ReportAnError_When_TheListIsEmptyAndOneIsRequired()
    {
        var editor = new MountsEditorViewModel(new FakeDirectoryProbe(), requireAtLeastOne: true);

        Assert.Contains(editor.ValidationMessages, m => m.Code == ValidationCodes.MountsEmpty);
        Assert.True(editor.HasErrors);
    }

    [Fact]
    public void Should_AcceptAnEmptyList_When_UsedByTheLauncher()
    {
        // The launcher adds mounts on top of the file's, so declaring none is the normal case.
        var editor = new MountsEditorViewModel(new FakeDirectoryProbe(), requireAtLeastOne: false);

        Assert.False(editor.HasErrors);
    }

    [Fact]
    public void Should_ReportAMissingPath_When_TheFolderDoesNotExist()
    {
        var editor = new MountsEditorViewModel(new FakeDirectoryProbe(), requireAtLeastOne: false);

        editor.Load(["/absent:/workspace:ro"]);

        Assert.Contains(editor.ValidationMessages, m => m.Code == ValidationCodes.MountPathMissing);
    }

    [Fact]
    public void Should_ClearTheError_When_TheFolderIsCreated()
    {
        var probe = new FakeDirectoryProbe();
        var editor = new MountsEditorViewModel(probe, requireAtLeastOne: false);
        editor.Load(["/data:/workspace:ro"]);

        editor.CreatePhysicalFolderCommand.Execute(null);

        Assert.Equal(["/data"], probe.Created);
        Assert.DoesNotContain(editor.ValidationMessages, m => m.Code == ValidationCodes.MountPathMissing);
    }

    [Fact]
    public void Should_ReportACollision_When_TwoMountsClaimTheSameVirtualPath()
    {
        var editor = new MountsEditorViewModel(
            new FakeDirectoryProbe("/a", "/b"),
            requireAtLeastOne: false);

        editor.Load(["/a:/workspace:ro", "/b:/workspace:rw"]);

        Assert.Contains(editor.ValidationMessages, m => m.Code == ValidationCodes.MountVirtualCollision);
    }

    [Fact]
    public void Should_PreserveOrderAndUnparsedEntries_When_RoundTripping()
    {
        var editor = new MountsEditorViewModel(new FakeDirectoryProbe("/a"), requireAtLeastOne: false);
        string[] entries = ["/a:/workspace:ro", "garbage", "/a:/output:rw"];

        editor.Load(entries);

        Assert.Equal(entries, editor.ToRawEntries());
    }

    [Fact]
    public void Should_ReportTheFormatError_When_AnEntryIsUnparsed()
    {
        var editor = new MountsEditorViewModel(new FakeDirectoryProbe(), requireAtLeastOne: false);

        editor.Load(["garbage"]);

        Assert.Contains(editor.ValidationMessages, m => m.Code == ValidationCodes.MountFormat);
    }

    [Fact]
    public void Should_RevalidateAndNotify_When_AMountIsEdited()
    {
        var editor = new MountsEditorViewModel(new FakeDirectoryProbe("/data"), requireAtLeastOne: false);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        var mount = editor.AddMount();
        mount.PhysicalPath = "/data";

        Assert.True(changes > 0);
        Assert.Equal("/data:/workspace:ro", Assert.Single(editor.ToRawEntries()));
    }

    [Fact]
    public void Should_SelectANeighbour_When_TheSelectedMountIsRemoved()
    {
        var editor = new MountsEditorViewModel(new FakeDirectoryProbe("/a"), requireAtLeastOne: false);
        editor.Load(["/a:/workspace:ro", "/a:/output:rw"]);
        editor.SelectedMount = editor.Mounts[1];

        editor.RemoveMountCommand.Execute(null);

        Assert.Single(editor.Mounts);
        Assert.Same(editor.Mounts[0], editor.SelectedMount);
    }

    [Fact]
    public void Should_TakeThePickedFolder_When_TheUserBrowses()
    {
        var picker = new FakePathPicker { FolderToReturn = "/picked" };
        var editor = new MountsEditorViewModel(new FakeDirectoryProbe("/picked"), picker, requireAtLeastOne: false);
        editor.AddMount();

        editor.BrowsePhysicalPathCommand.Execute(null);

        Assert.Equal("/picked", editor.SelectedMount!.PhysicalPath);
    }

    [Fact]
    public void Should_LeaveThePathAlone_When_TheUserCancelsTheBrowser()
    {
        var picker = new FakePathPicker { FolderToReturn = null };
        var editor = new MountsEditorViewModel(new FakeDirectoryProbe(), picker, requireAtLeastOne: false);
        var mount = editor.AddMount();
        mount.PhysicalPath = "/kept";

        editor.BrowsePhysicalPathCommand.Execute(null);

        Assert.Equal("/kept", mount.PhysicalPath);
    }
}
