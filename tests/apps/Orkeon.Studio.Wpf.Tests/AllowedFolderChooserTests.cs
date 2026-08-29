using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Wpf.ViewModels.Mounts;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The chooser a team uses to associate folders already declared in
/// « Réglages › Dossiers autorisés ». The point pinned here is that a team never re-declares
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
    public void Declaring_a_folder_raises_the_request_and_the_answer_lands_pre_checked()
    {
        var declared = new List<string> { Docs };
        var chooser = new AllowedFolderChooserViewModel(() => declared);
        var asked = 0;
        chooser.DeclareRequested += (_, _) => asked++;

        var added = new List<MountDefinition>();
        chooser.Open([], added.Add);
        chooser.DeclareNewCommand.Execute(null);
        Assert.Equal(1, asked);

        // The shell wrote it into the settings, then reported back.
        var mount = new MountDefinition { PhysicalPath = "/data/rapports", VirtualPath = "/rapports", Rights = MountRights.ReadWrite };
        declared.Add(mount.ToMountString());
        chooser.NotifyDeclared(mount, saveError: null);

        var row = chooser.Rows.Single(r => r.VirtualPath == "/rapports");
        Assert.True(row.IsChecked);
        Assert.True(chooser.HasNotice);
        Assert.Contains("/rapports", chooser.Notice, StringComparison.Ordinal);

        chooser.ConfirmCommand.Execute(null);
        Assert.Equal(["/rapports"], added.Select(m => m.VirtualPath));
    }

    [Fact]
    public void A_declaration_the_settings_refused_to_save_says_so_and_keeps_the_folder_usable()
    {
        var declared = new List<string>();
        var chooser = new AllowedFolderChooserViewModel(() => declared);
        chooser.Open([], _ => { });

        var mount = new MountDefinition { PhysicalPath = "/data/rapports", VirtualPath = "/rapports" };
        declared.Add(mount.ToMountString());
        chooser.NotifyDeclared(mount, saveError: "No destination is selected.");

        Assert.Contains("No destination is selected.", chooser.Notice, StringComparison.Ordinal);
        // The write failed, not the declaration: the row is there and the team can take it.
        Assert.True(chooser.Rows.Single().IsChecked);
        Assert.True(chooser.CanConfirm);
    }

    [Fact]
    public void A_selection_survives_a_declaration_made_in_the_middle_of_it()
    {
        var declared = new List<string> { Docs, Out };
        var chooser = new AllowedFolderChooserViewModel(() => declared);
        var added = new List<MountDefinition>();
        chooser.Open([], added.Add);

        chooser.Rows.Single(r => r.VirtualPath == "/docs").IsChecked = true;

        var mount = new MountDefinition { PhysicalPath = "/data/rapports", VirtualPath = "/rapports" };
        declared.Add(mount.ToMountString());
        chooser.NotifyDeclared(mount, saveError: null);

        chooser.ConfirmCommand.Execute(null);
        Assert.Equal(["/docs", "/rapports"], added.Select(m => m.VirtualPath));
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
        Assert.Null(chooser.Notice);
        Assert.False(chooser.CanConfirm);
    }
}
