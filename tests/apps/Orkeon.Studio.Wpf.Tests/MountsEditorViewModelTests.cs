using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Localization;
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

    /// <summary>A port answering a French table for the badges and the labels.</summary>
    private sealed class FrenchRightsStrings : Orkeon.Studio.Core.Localization.IStudioStrings
    {
        private static readonly Dictionary<string, string> French = new(StringComparer.Ordinal)
        {
            [Orkeon.Studio.Core.Localization.StudioStringKeys.RightsBadgeReadWrite] = "écriture",
            [Orkeon.Studio.Core.Localization.StudioStringKeys.RightsReadWrite] = "Lecture / écriture (création et suppression autorisées)",
        };

        public string this[string key] =>
            French.GetValueOrDefault(key, Orkeon.Studio.Core.Localization.EnglishStudioStrings.Instance[key]);

        public event EventHandler? CultureChanged { add { } remove { } }
    }

    [Fact]
    public void The_rights_badge_is_one_word_and_the_label_is_the_tooltip()
    {
        // STUDIO-16 (D-05): the list row shows the right in a word; the 52-character label
        // that used to take the whole row and push the name out is the badge's tooltip.
        var mount = new MountEditorViewModel { PhysicalPath = "/data", VirtualPath = "/output", Rights = MountRights.ReadWrite };

        Assert.Equal("write", mount.RightsBadge);
        Assert.Equal("Read / write (create and delete allowed)", mount.RightsLabel);
        Assert.Equal("read", new MountEditorViewModel { Rights = MountRights.ReadOnly }.RightsBadge);
        Assert.Equal("write, no delete", new MountEditorViewModel { Rights = MountRights.ReadWriteNoDelete }.RightsBadge);

        // The badge moves with the rights, and with the culture.
        var raised = new List<string>();
        mount.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);
        mount.Rights = MountRights.ReadOnly;
        Assert.Contains(nameof(MountEditorViewModel.RightsBadge), raised);
        raised.Clear();
        mount.RefreshCulture();
        Assert.Contains(nameof(MountEditorViewModel.RightsBadge), raised);

        var french = new MountEditorViewModel(new FrenchRightsStrings()) { Rights = MountRights.ReadWrite };
        Assert.Equal("écriture", french.RightsBadge);
        Assert.Equal("Lecture / écriture (création et suppression autorisées)", french.RightsLabel);

        // And the row template wires it that way: the badge shows RightsBadge, its tooltip is
        // RightsLabel, and the name comes first so the badge can never shrink it.
        var xaml = File.ReadAllText(Path.Combine(WpfSourceRoot(), "Views", "MountsEditorView.xaml"));
        var row = xaml[xaml.IndexOf("<ListBox.ItemTemplate>", StringComparison.Ordinal)..xaml.IndexOf("</ListBox.ItemTemplate>", StringComparison.Ordinal)];
        Assert.Contains("Text=\"{Binding RightsBadge, Mode=OneWay}\"", row, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding RightsLabel, Mode=OneWay}\"", row, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding RightsLabel", row, StringComparison.Ordinal);
        Assert.True(
            row.IndexOf("{Binding VirtualPath, Mode=OneWay}", StringComparison.Ordinal)
                < row.IndexOf("{Binding RightsBadge, Mode=OneWay}", StringComparison.Ordinal),
            "the virtual name must be the first child of the row, the badge the last");
        Assert.Contains("TextWrapping=\"NoWrap\"", row, StringComparison.Ordinal);
    }

    private static string WpfSourceRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf"));
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

        // Audit 08/17: no error state before any interaction — the form opens clean...
        Assert.Empty(editor.ValidationMessages);
        Assert.False(editor.HasErrors);

        // ...and the first explicit validation says what is missing.
        editor.Validate();

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
        mount.VirtualPath = "/docs";

        Assert.True(changes > 0);
        Assert.Equal("/data:/docs:ro", Assert.Single(editor.ToRawEntries()));
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

    // ── STUDIO-19: « Allow a folder » is the OS folder dialog, the badge flips the rights ──

    [Fact]
    public void Should_MountThePickedFolderReadOnlyUnderItsOwnName_When_TheNoviceAllowsAFolder()
    {
        var picker = new FakePathPicker { FolderToReturn = "/data/Factures" };
        var editor = new MountsEditorViewModel(new FakeDirectoryProbe("/data/Factures"), picker, requireAtLeastOne: false);
        var changed = 0;
        editor.Changed += (_, _) => changed++;

        editor.AllowFolderCommand.Execute(null);

        Assert.Equal([EnglishStudioStrings.Instance[StudioStringKeys.DialogSelectMountFolder]], picker.Prompts);
        Assert.Equal(["/data/Factures:/factures:ro"], editor.ToRawEntries());
        Assert.Same(editor.Mounts[0], editor.SelectedMount);
        Assert.False(editor.HasErrors);
        Assert.True(changed > 0);
    }

    [Fact]
    public void Should_AddNothing_When_TheNoviceCancelsTheFolderDialog()
    {
        var picker = new FakePathPicker { FolderToReturn = null };
        var editor = new MountsEditorViewModel(new FakeDirectoryProbe(), picker, requireAtLeastOne: false);

        editor.AllowFolderCommand.Execute(null);

        Assert.Single(picker.Prompts);
        Assert.Empty(editor.Mounts);
        Assert.Null(editor.SelectedMount);
    }

    [Fact]
    public void Should_FallBackToTheFirstFreeSuggestion_When_TheFolderNameIsTaken()
    {
        var picker = new FakePathPicker { FolderToReturn = "/elsewhere/docs" };
        var editor = new MountsEditorViewModel(new FakeDirectoryProbe("/a", "/elsewhere/docs"), picker, requireAtLeastOne: false);
        editor.Load(["/a:/docs:ro"]);

        editor.AllowFolderCommand.Execute(null);

        Assert.Equal(["/a:/docs:ro", "/elsewhere/docs:/data:ro"], editor.ToRawEntries());
    }

    [Fact]
    public void Should_FlipReadOnlyAndReadWrite_When_TheCardBadgeIsClicked()
    {
        var editor = new MountsEditorViewModel(new FakeDirectoryProbe("/a"), new FakePathPicker(), requireAtLeastOne: false);
        editor.Load(["/a:/docs:ro", "/a:/tmp:rwnd"]);
        var changed = 0;
        editor.Changed += (_, _) => changed++;

        editor.ToggleRightsCommand.Execute(editor.Mounts[0]);
        Assert.Equal(MountRights.ReadWrite, editor.Mounts[0].Rights);
        editor.ToggleRightsCommand.Execute(editor.Mounts[0]);
        Assert.Equal(MountRights.ReadOnly, editor.Mounts[0].Rights);
        editor.ToggleRightsCommand.Execute(editor.Mounts[1]);
        Assert.Equal(MountRights.ReadOnly, editor.Mounts[1].Rights);

        Assert.Equal(3, changed);
        Assert.False(editor.ToggleRightsCommand.CanExecute("not a row"));
        Assert.Equal(["/a:/docs:ro", "/a:/tmp:ro"], editor.ToRawEntries());
    }
}
