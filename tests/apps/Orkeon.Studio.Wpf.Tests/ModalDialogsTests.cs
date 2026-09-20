using System.Text.Json;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Wpf.ViewModels.Mounts;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The two v2 modals (RC2-FEAT-05, F-01/F-03): the team-mounts rows and their save; the
/// agent editor's blueprint mutations — all pure ViewModel, no window. The folder picker
/// left with STUDIO-19: the OS folder dialog replaced it.
/// </summary>
public sealed class ModalDialogsTests
{
    // ── Team mounts ──

    [Fact]
    public void The_team_mounts_save_keeps_checked_rows_and_added_folders_only()
    {
        (string Directory, IReadOnlyList<string> Mounts)? saved = null;
        var dialog = new TeamMountsDialogViewModel(saveMounts: (dir, mounts) => saved = (dir, mounts));
        var refreshed = false;

        dialog.Open("/teams/veille", "Veille", ["/a:/docs:ro", "/b:/output:rw"], onSaved: () => refreshed = true);
        Assert.Equal(2, dialog.Rows.Count);
        Assert.All(dialog.Rows, row => Assert.True(row.IsChecked));

        dialog.Rows[0].IsChecked = false;
        dialog.AddMount(MountDefinition.Parse("/c:/archives:ro"));
        dialog.SaveCommand.Execute(null);

        Assert.False(dialog.IsOpen);
        Assert.True(refreshed);
        Assert.Equal(["/b:/output:rw", "/c:/archives:ro"], saved!.Value.Mounts);
        Assert.Equal("/teams/veille", saved.Value.Directory);
    }

    /// <summary>Same fix as the chooser's: a one-word pill, the full label as tooltip, the id tail on the row.</summary>
    [Fact]
    public void A_team_mount_row_carries_a_one_word_rights_pill_and_the_id_tail()
    {
        var id = Orkeon.Domain.Common.MountId.Create();
        var dialog = new TeamMountsDialogViewModel(saveMounts: (_, _) => { }, declaredMounts: () => [$"{id}|/data/out:/output:rw"]);
        dialog.Open("/teams/veille", "Veille", ["./input:/workspace:ro", $"{id}|/data/out:/output:rw"]);

        Assert.Equal("read", dialog.Rows[0].RightsBadge);
        Assert.Equal("", dialog.Rows[0].ShortId);
        Assert.Equal("write", dialog.Rows[1].RightsBadge);
        Assert.Equal("Read / write (create and delete allowed)", dialog.Rows[1].RightsLabel);
        Assert.Equal(id.ToString()[^6..], dialog.Rows[1].ShortId);
        Assert.Equal("/data/out", dialog.Rows[1].PhysicalPath);
    }

    [Fact]
    public void The_summary_speaks_plainly_for_none_and_for_some()
    {
        var dialog = new TeamMountsDialogViewModel(saveMounts: (_, _) => { });
        dialog.Open("/teams/x", "X", ["/a:/docs:ro"]);

        Assert.Contains("/docs", dialog.Summary, StringComparison.Ordinal);
        dialog.Rows[0].IsChecked = false;
        // Default strings are the English registry.
        Assert.Contains("neither read nor write", dialog.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// Red means one thing: this folder is outside what the machine allows. It used to also
    /// mean «the team made this folder itself», which the launcher explicitly refuses to
    /// warn about — so a team's own /output read red here and green on Run, about the
    /// very same folder.
    /// </summary>
    [Fact]
    public void A_folder_outside_the_allowed_ones_reads_red_and_the_teams_own_does_not()
    {
        var dialog = new TeamMountsDialogViewModel(
            saveMounts: (_, _) => { },
            declaredMounts: () => ["/data/docs:/docs:ro"]);

        dialog.Open("/teams/veille", "Veille",
        [
            "/data/docs:/docs:ro",                  // declared in the settings
            "/teams/veille/output:/output:rw",      // the team's own, bound at adoption
            "/elsewhere:/archives:ro",              // genuinely outside the allow-list
        ]);

        Assert.False(dialog.Rows.Single(r => r.VirtualPath == "/docs").IsUndeclared);
        Assert.False(dialog.Rows.Single(r => r.VirtualPath == "/output").IsUndeclared);
        Assert.True(dialog.Rows.Single(r => r.VirtualPath == "/archives").IsUndeclared);

        dialog.Rows.Single(r => r.VirtualPath == "/archives").IsChecked = false;
        Assert.Equal(["/data/docs:/docs:ro", "/teams/veille/output:/output:rw"], dialog.CheckedMounts);
    }

    // ── Agent editor ──

    private const string Blueprint =
        """{"crew":{"name":"veille"},"agents":[{"key":"collecteur","role":"Chercheur","goal":"Trouver","tools":["web_scrape","file_read"]},{"key":"redacteur","role":"Rédacteur","goal":"Écrire","tools":["file_write"]}],"tasks":[{"key":"t1","description":"d","agent":"collecteur"}]}""";

    [Fact]
    public void Editing_an_agent_rewrites_role_goal_and_tools_in_place()
    {
        var editor = new AgentEditorViewModel();
        string? sent = null;
        editor.Open(Blueprint, "redacteur", ["/a:/docs:ro"], json => sent = json);

        Assert.False(editor.IsNew);
        Assert.Equal("Rédacteur", editor.Name);
        Assert.Equal("Écrire", editor.WhatItDoes);
        // The palette is the union of the blueprint's tools; the agent's own are selected.
        Assert.Equal(["file_read", "file_write", "web_scrape"], editor.Chips.Select(c => c.Tool));
        Assert.True(editor.Chips.Single(c => c.Tool == "file_write").IsSelected);
        Assert.False(editor.Chips.Single(c => c.Tool == "web_scrape").IsSelected);

        editor.Name = "Relecteur";
        editor.WhatItDoes = "Relire et vérifier";
        editor.Chips.Single(c => c.Tool == "file_read").ToggleCommand.Execute(null);
        editor.SaveCommand.Execute(null);

        Assert.False(editor.IsOpen);
        using var document = JsonDocument.Parse(sent!);
        var agent = document.RootElement.GetProperty("agents").EnumerateArray()
            .Single(a => a.GetProperty("key").GetString() == "redacteur");
        Assert.Equal("Relecteur", agent.GetProperty("role").GetString());
        Assert.Equal("Relire et vérifier", agent.GetProperty("goal").GetString());
        Assert.Equal(["file_read", "file_write"], agent.GetProperty("tools").EnumerateArray().Select(t => t.GetString()));
        // The other agent is untouched.
        Assert.Equal(2, document.RootElement.GetProperty("agents").GetArrayLength());
    }

    [Fact]
    public void Adding_an_agent_appends_a_sluggged_unique_key()
    {
        var editor = new AgentEditorViewModel();
        string? sent = null;
        editor.Open(Blueprint, agentKey: null, [], json => sent = json);

        Assert.True(editor.IsNew);
        editor.Name = "Vérificateur Web";
        editor.SaveCommand.Execute(null);

        using var document = JsonDocument.Parse(sent!);
        var agents = document.RootElement.GetProperty("agents");
        Assert.Equal(3, agents.GetArrayLength());
        Assert.Equal("verificateur-web", agents[2].GetProperty("key").GetString());
        Assert.Equal("Vérificateur Web", agents[2].GetProperty("role").GetString());
    }

    [Fact]
    public void Removing_an_agent_drops_its_entry_and_only_its_entry()
    {
        var editor = new AgentEditorViewModel();
        string? sent = null;
        editor.Open(Blueprint, "collecteur", [], json => sent = json);
        editor.RemoveCommand.Execute(null);

        using var document = JsonDocument.Parse(sent!);
        var agents = document.RootElement.GetProperty("agents");
        Assert.Equal(1, agents.GetArrayLength());
        Assert.Equal("redacteur", agents[0].GetProperty("key").GetString());
        // Tasks are left alone — the engine's validation owns referential integrity.
        Assert.Equal(1, document.RootElement.GetProperty("tasks").GetArrayLength());
    }

    [Fact]
    public void An_empty_name_blocks_the_save()
    {
        var editor = new AgentEditorViewModel();
        editor.Open(Blueprint, "collecteur", [], _ => { });
        editor.Name = "  ";
        Assert.False(editor.CanSave);
    }

    /// <summary>
    /// The symptom the owner reported: the agent editor's « Sur quel dossier » line showed
    /// raw mount strings, so a novice editing an agent read <c>C:\Users\…</c> where only a
    /// VFS mount point belongs. The line names what the agent addresses, and its rights.
    /// </summary>
    [Fact]
    public void The_agent_editor_names_mounts_the_way_agents_address_them()
    {
        var editor = new AgentEditorViewModel();
        editor.Open(
            Blueprint,
            "collecteur",
            [@"C:\Users\demo\Factures:/factures:ro", @"C:\Users\demo\Sorties:/output:rw"],
            _ => { });

        Assert.DoesNotContain(@"C:\", editor.ScopeInfo, StringComparison.Ordinal);
        Assert.Contains("/factures", editor.ScopeInfo, StringComparison.Ordinal);
        Assert.Contains("/output", editor.ScopeInfo, StringComparison.Ordinal);
        // Rights travel with the path — read-only and writable read differently.
        Assert.NotEqual(
            editor.ScopeInfo.Split(" · ")[0],
            editor.ScopeInfo.Split(" · ")[1]);
    }

    [Fact]
    public void An_unreadable_mount_is_named_as_such_rather_than_dumped()
    {
        var editor = new AgentEditorViewModel();
        editor.Open(Blueprint, "collecteur", [@"C:\Users\demo\Factures"], _ => { });

        Assert.DoesNotContain(@"C:\", editor.ScopeInfo, StringComparison.Ordinal);
    }

    [Fact]
    public void No_folder_reads_as_a_dash()
    {
        var editor = new AgentEditorViewModel();
        editor.Open(Blueprint, "collecteur", [], _ => { });

        Assert.Equal("—", editor.ScopeInfo);
    }
}
