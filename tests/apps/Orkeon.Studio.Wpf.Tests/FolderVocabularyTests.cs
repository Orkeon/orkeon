using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mounts;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// UX lot 01: one vocabulary for the folders — the engine's fixed roots say what they hold, a
/// Novice reads a folder's name rather than its path, one « Remove » per row, and the no-delete
/// right is explained in a sentence.
/// </summary>
public sealed class FolderVocabularyTests
{
    private static readonly IStudioStrings English = EnglishStudioStrings.Instance;

    [Theory]
    [InlineData("/output", "Results (/output)")]
    [InlineData("/workspace", "Workspace (/workspace)")]
    [InlineData("/inpdf", "/inpdf")]
    public void A_fixed_root_keeps_its_alias_and_says_what_it_holds(string virtualPath, string expected) =>
        Assert.Equal(expected, MountLabels.DisplayName(virtualPath, English));

    [Fact]
    public void Describe_names_the_output_root_by_what_it_holds()
    {
        var (label, readWrite) = MountLabels.Describe("./sortie:/output:rw", English);

        Assert.Equal("Results (/output) · read, write", label);
        Assert.True(readWrite);
        Assert.Equal("/docs · read", MountLabels.Describe("./docs:/docs:ro", English).Label);
    }

    [Fact]
    public void A_composer_row_shows_one_remove_for_a_bound_folder_or_a_bare_root()
    {
        var bound = new MountRow("/docs", false, Folder: @"C:\data\docs", MountString: @"C:\data\docs:/docs:ro");
        var bare = new MountRow("/output", true);
        var question = new MountRow("/inpdf", false, Title: "Your documents");

        Assert.True(bound.CanRemove);
        Assert.Equal("docs", bound.FolderName);
        Assert.True(bare.CanRemove);
        Assert.False(question.CanRemove);
    }

    [Fact]
    public void A_no_delete_badge_says_it_can_modify_but_never_delete()
    {
        Assert.Equal("Can modify, never delete.", MountRightsTokens.GetTip(MountRights.ReadWriteNoDelete, English));
        Assert.Equal(
            MountRightsTokens.GetLabel(MountRights.ReadOnly, English),
            MountRightsTokens.GetTip(MountRights.ReadOnly, English));
        Assert.Equal(
            MountRightsTokens.GetLabel(MountRights.ReadWrite, English),
            MountRightsTokens.GetTip(MountRights.ReadWrite, English));
    }

    [Fact]
    public void A_settings_card_is_titled_by_the_folder_name_and_follows_its_path()
    {
        var mount = new MountEditorViewModel(English) { PhysicalPath = @"C:\Users\cyril\Documents\factures" };
        var raised = new List<string?>();
        mount.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.Equal("factures", mount.FolderName);
        mount.PhysicalPath = "/srv/rapports";
        Assert.Equal("rapports", mount.FolderName);
        Assert.Contains(nameof(MountEditorViewModel.FolderName), raised);

        mount.Rights = MountRights.ReadWriteNoDelete;
        Assert.Equal("Can modify, never delete.", mount.RightsTip);
    }
}
