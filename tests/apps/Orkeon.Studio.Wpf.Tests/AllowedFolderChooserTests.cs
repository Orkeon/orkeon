using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Wpf.ViewModels.Mounts;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The chooser a team uses to associate folders already declared in
/// the Settings > Allowed folders screen. The point pinned here is that a team never re-declares
/// a folder and never re-chooses its rights: the settings entry is carried over verbatim.
/// </summary>
public sealed class AllowedFolderChooserTests
{
    private const string Docs = "/data/docs:/docs:ro";
    private const string Out = "/data/out:/output:rw";

    private static AllowedFolderChooserViewModel Chooser(params string[] declared) =>
        new(() => declared);

    [Fact]
    public void The_rows_are_the_folders_declared_in_the_settings_in_order()
    {
        var chooser = Chooser(Docs, Out);
        chooser.Open([], _ => { });

        Assert.True(chooser.IsOpen);
        Assert.True(chooser.HasRows);
        Assert.Equal(["/docs", "/output"], chooser.Rows.Select(r => r.VirtualPath));
        Assert.Equal(["/data/docs", "/data/out"], chooser.Rows.Select(r => r.PhysicalPath));
        Assert.True(chooser.Rows[0].IsReadOnly);
        Assert.False(chooser.Rows[1].IsReadOnly);
        Assert.All(chooser.Rows, row => Assert.True(row.IsSelectable));
        Assert.All(chooser.Rows, row => Assert.False(row.IsChecked));
    }

    [Fact]
    public void Checking_two_rows_adds_both_with_the_rights_the_settings_declare()
    {
        var chooser = Chooser(Docs, Out);
        var added = new List<MountDefinition>();
        chooser.Open([], added.Add);

        chooser.Rows[0].IsChecked = true;
        chooser.Rows[1].IsChecked = true;
        Assert.True(chooser.CanConfirm);
        chooser.ConfirmCommand.Execute(null);

        Assert.False(chooser.IsOpen);
        Assert.Equal(2, added.Count);
        // Verbatim, rights included: no plafond, no per-team widening, no re-derivation.
        Assert.Equal("/data/docs", added[0].PhysicalPath);
        Assert.Equal("/docs", added[0].VirtualPath);
        Assert.Equal(MountRights.ReadOnly, added[0].Rights);
        Assert.Equal("/data/out", added[1].PhysicalPath);
        Assert.Equal("/output", added[1].VirtualPath);
        Assert.Equal(MountRights.ReadWrite, added[1].Rights);
        Assert.Equal([Docs, Out], added.Select(m => m.ToMountString()));
    }

    [Fact]
    public void A_folder_the_team_already_carries_is_noted_and_cannot_be_picked_again()
    {
        var chooser = Chooser(Docs, Out);
        chooser.Open([Docs], _ => { });

        var row = chooser.Rows.Single(r => r.VirtualPath == "/docs");
        Assert.False(row.IsSelectable);
        Assert.True(row.HasNote);

        row.IsChecked = true;
        Assert.False(row.IsChecked);
        Assert.False(chooser.CanConfirm);
    }

    [Fact]
    public void A_virtual_root_another_folder_already_spends_is_refused_with_its_reason()
    {
        var chooser = Chooser(Out);
        // The team already writes to /output — but from a folder inside the team itself, the
        // one the blueprint implied. Two mounts on one root is not a merge, it is a drop.
        chooser.Open(["/teams/veille/output:/output:rw"], _ => { });

        var row = chooser.Rows.Single();
        Assert.False(row.IsSelectable);
        Assert.Contains("/output", row.UnavailableNote, StringComparison.Ordinal);
        Assert.DoesNotContain("/data/out", row.UnavailableNote, StringComparison.Ordinal);
    }

    [Fact]
    public void Confirm_waits_for_a_selection_and_cancel_adds_nothing()
    {
        var chooser = Chooser(Docs);
        var added = new List<MountDefinition>();
        chooser.Open([], added.Add);

        Assert.False(chooser.CanConfirm);
        Assert.False(chooser.ConfirmCommand.CanExecute(null));

        chooser.Rows[0].IsChecked = true;
        Assert.True(chooser.ConfirmCommand.CanExecute(null));

        chooser.CancelCommand.Execute(null);
        Assert.False(chooser.IsOpen);
        Assert.Empty(added);
    }

    [Fact]
    public void With_nothing_declared_the_modal_still_opens_on_its_empty_state()
    {
        var chooser = Chooser();
        chooser.Open([], _ => { });

        Assert.True(chooser.IsOpen);
        Assert.False(chooser.HasRows);
        Assert.False(chooser.CanConfirm);
        Assert.True(chooser.DeclareNewCommand.CanExecute(null));
    }

    [Fact]
    public void Declaring_a_new_folder_closes_the_modal_and_asks_for_the_settings()
    {
        // Declaring is the settings' gesture and happens on their screen: one door, so a folder
        // cannot be declared from two places and drift between them.
        var chooser = Chooser(Docs);
        var asked = 0;
        chooser.OpenSettingsRequested += (_, _) => asked++;
        chooser.Open([], _ => { });

        chooser.DeclareNewCommand.Execute(null);

        Assert.Equal(1, asked);
        Assert.False(chooser.IsOpen);
    }

    [Fact]
    public void A_folder_declared_while_away_shows_up_on_the_next_open()
    {
        var declared = new List<string> { Docs };
        var chooser = new AllowedFolderChooserViewModel(() => declared);
        chooser.Open([], _ => { });
        chooser.DeclareNewCommand.Execute(null);

        // The user declared it in the settings, then came back to the team.
        declared.Add("/data/rapports:/rapports:rw");
        chooser.Open([], _ => { });

        Assert.Equal(["/docs", "/rapports"], chooser.Rows.Select(r => r.VirtualPath));
    }

    [Fact]
    public void An_unreadable_settings_entry_is_shown_without_its_disk_path_and_cannot_be_picked()
    {
        // ADR-008: never dump the raw mount string on a team-facing screen — it carries the
        // folder on this machine.
        var chooser = Chooser("this is not a mount");
        chooser.Open([], _ => { });

        var row = chooser.Rows.Single();
        Assert.False(row.IsSelectable);
        Assert.Equal("", row.PhysicalPath);
        Assert.DoesNotContain("this is not a mount", row.VirtualPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Reopening_starts_from_a_clean_selection()
    {
        var chooser = Chooser(Docs, Out);
        chooser.Open([], _ => { });
        chooser.Rows[0].IsChecked = true;
        chooser.CancelCommand.Execute(null);

        chooser.Open([], _ => { });

        Assert.All(chooser.Rows, row => Assert.False(row.IsChecked));
        Assert.False(chooser.CanConfirm);
    }

    /// <summary>
    /// The targeted open: the modal answers ONE mount point, and the folder it hands back is
    /// bound behind that name — not behind the one the settings happened to declare.
    /// <para>
    /// Without this, a root the blueprint implies could never be answered: every declared
    /// entry carries its own virtual name, so «Autoriser un dossier» could only ever add
    /// <c>/docs</c>, and <c>/workspace</c> stayed unbound until adoption backed it with an
    /// empty folder inside the team.
    /// </para>
    /// </summary>
    [Fact]
    public void A_targeted_open_judges_the_rows_on_the_target_and_takes_one_folder()
    {
        var chooser = Chooser(Docs, Out);
        var added = new List<MountDefinition>();

        // The team already spends /workspace on something else — that is the thing being
        // replaced — AND it already carries /output, which the second row happens to be
        // declared on. Neither may disqualify a row: what matters is where the pick LANDS,
        // and every pick here lands on /workspace.
        chooser.Open(["/data/old:/workspace:ro", "/data/out:/output:rw"], added.Add, targetVirtualPath: "/workspace");

        Assert.True(chooser.IsBindingOneMount);
        Assert.Contains("/workspace", chooser.Title, StringComparison.Ordinal);
        Assert.All(chooser.Rows, r => Assert.True(r.IsSelectable, r.UnavailableNote));

        // One mount point takes one folder: the second tick replaces the first.
        chooser.Rows[0].IsChecked = true;
        chooser.Rows[1].IsChecked = true;
        Assert.False(chooser.Rows[0].IsChecked);
        Assert.True(chooser.Rows[1].IsChecked);

        chooser.ConfirmCommand.Execute(null);

        // The folder and the RIGHTS come from the settings, verbatim; only the name the
        // agents use is the team's to choose — and the caller is what applies it.
        var picked = Assert.Single(added);
        Assert.Equal("/data/out", picked.PhysicalPath);
        Assert.Equal(MountRights.ReadWrite, picked.Rights);
        Assert.Equal("/output", picked.VirtualPath);
        Assert.Null(chooser.TargetVirtualPath);
    }

    /// <summary>
    /// The one refusal a targeted open keeps: the folder already sitting behind that very
    /// mount point. Picking it again changes nothing, and the row says so.
    /// </summary>
    [Fact]
    public void A_targeted_open_still_refuses_the_folder_already_behind_that_mount_point()
    {
        var chooser = Chooser(Docs, Out);
        chooser.Open(["/data/docs:/workspace:ro"], _ => { }, targetVirtualPath: "/workspace");

        Assert.False(chooser.Rows[0].IsSelectable);
        Assert.True(chooser.Rows[0].HasNote);
        Assert.True(chooser.Rows[1].IsSelectable);
    }

    /// <summary>The untargeted open keeps its multiple choice and its own heading.</summary>
    [Fact]
    public void The_untargeted_open_still_takes_several_folders()
    {
        var chooser = Chooser(Docs, Out);
        var added = new List<MountDefinition>();
        chooser.Open([], added.Add);

        Assert.False(chooser.IsBindingOneMount);
        Assert.Equal(
            Orkeon.Studio.Core.Localization.EnglishStudioStrings.Instance[
                Orkeon.Studio.Core.Localization.StudioStringKeys.AllowedFoldersTitle],
            chooser.Title);

        chooser.Rows[0].IsChecked = true;
        chooser.Rows[1].IsChecked = true;
        Assert.True(chooser.Rows[0].IsChecked);

        chooser.ConfirmCommand.Execute(null);
        Assert.Equal(2, added.Count);
    }
}
