using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The read-only « Team folders » section of Settings › Authorized folders (STUDIO-14, D-13),
/// over an in-memory catalog: what makes a row, and what does not.
/// </summary>
public sealed class TeamFoldersViewModelTests
{
    private static TeamSummary Team(string root, string slug, string name, params string[] mounts) => new()
    {
        Name = name,
        Slug = slug,
        Path = Path.Combine(root, slug),
        Metadata = new StudioTeamMetadata { Name = name, Mounts = mounts },
    };

    /// <summary>
    /// A sidecar written before the team-relative convention recorded the same folder as an
    /// absolute path under the team. It is the team's own folder all the same — the next save
    /// rewrites it <c>./output</c> — so the section lists it, by its sub-folder name.
    /// </summary>
    [Fact]
    public void An_older_absolute_in_team_entry_is_listed_too()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-teams-root");
        var older = new MountDefinition
        {
            PhysicalPath = Path.Combine(root, "veille", "output"),
            VirtualPath = "/output",
            Rights = MountRights.ReadWrite,
        }.ToMountString();

        var section = new TeamFoldersViewModel(() => [Team(root, "veille", "Veille", older)]);

        var row = Assert.Single(section.Rows);
        Assert.Equal("Veille", row.TeamName);
        Assert.Equal("/output", row.VirtualPath);
        Assert.Equal("output", row.Folder);
        Assert.Equal("Veille · /output → output", row.Label);
        Assert.True(row.IsReadWrite);
    }

    /// <summary>
    /// VFS-90: a folder the team names by id is listed with the declaration it resolves to, and
    /// flagged when this machine has no such declaration — read against the settings the shell
    /// passes; without them, only the in-team folders are listed, as before.
    /// </summary>
    [Fact]
    public void A_declared_folder_is_listed_by_its_id_and_an_unknown_id_is_flagged()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-teams-root");
        var known = Orkeon.Domain.Common.MountId.Create();
        var unknown = Orkeon.Domain.Common.MountId.Create();
        var team = Team(root, "veille", "Veille", "./output:/output:rw", $"{known}|/old/copy:/docs:ro", $"{unknown}|/x:/x:ro");

        var section = new TeamFoldersViewModel(() => [team], declaredMounts: () => [$"{known}|/data/docs:/docs:ro"]);

        Assert.Equal(3, section.Rows.Count);
        var declared = section.Rows.Single(r => r.VirtualPath == "/docs");
        Assert.True(declared.IsDeclared);
        Assert.Equal("/data/docs", declared.Folder);
        Assert.Equal(known.ToString()[^6..], declared.ShortId);
        Assert.Equal($"Veille · /docs → /data/docs ({declared.ShortId})", declared.Label);
        var missing = section.Rows.Single(r => r.VirtualPath == "/x");
        Assert.True(missing.IsUnknownId);
        Assert.Equal($"Veille · /x — declaration {unknown.ToString()[^6..]} is missing on this machine", missing.Label);

        Assert.Single(new TeamFoldersViewModel(() => [team]).Rows);
    }

    [Fact]
    public void A_relative_entry_names_its_sub_folder_and_a_nested_one_keeps_its_slashes()
    {
        var section = new TeamFoldersViewModel(() =>
        [
            Team("/teams", "veille", "Veille", "./input:/workspace:ro", "./rapports/2026:/rapports:rwnd"),
        ]);

        Assert.Equal(["input", "rapports/2026"], section.Rows.Select(r => r.Folder));
        Assert.Equal(["read", "write, no delete"], section.Rows.Select(r => r.RightsBadge));
        Assert.Equal("Read only", section.Rows[0].RightsLabel);
    }

    /// <summary>
    /// Outside the team, unreadable, or a <c>./</c> that escapes the team: none of these is a
    /// folder the team keeps inside itself, and none makes a row. The section never shows a
    /// disk path either — a folder outside the team is the settings' own business.
    /// </summary>
    [Fact]
    public void An_outside_an_unreadable_and_an_escaping_entry_make_no_row()
    {
        var section = new TeamFoldersViewModel(() =>
        [
            Team("/teams", "veille", "Veille",
                Path.Combine("/data", "docs") + ":/docs:ro",
                "not a mount string",
                "./../elsewhere:/output:rw",
                Path.Combine("/teams", "veille-old", "output") + ":/output:rw"),
            new TeamSummary { Name = "Contrats", Slug = "contrats", Path = "/teams/contrats" },
        ]);

        Assert.Empty(section.Rows);
        Assert.True(section.IsEmpty);
    }

    [Fact]
    public void The_team_name_is_the_one_line_the_card_shows()
    {
        var section = new TeamFoldersViewModel(() =>
        [
            Team("/teams", "veille", "# Veille **concurrentielle**\nsecond line", "./output:/output:rw"),
        ]);

        Assert.Equal("Veille concurrentielle", Assert.Single(section.Rows).TeamName);
    }

    /// <summary>A port that flips to a tiny French table — the hot language switch, hand-written.</summary>
    private sealed class SwitchableStrings : Orkeon.Studio.Core.Localization.IStudioStrings
    {
        private bool _french;

        public string this[string key] => _french && key == Orkeon.Studio.Core.Localization.StudioStringKeys.RightsBadgeReadWrite
            ? "écriture"
            : Orkeon.Studio.Core.Localization.EnglishStudioStrings.Instance[key];

        public event EventHandler? CultureChanged;

        public void SwitchToFrench()
        {
            _french = true;
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Fact]
    public void A_culture_change_rebuilds_the_rows_in_the_new_language()
    {
        var strings = new SwitchableStrings();
        var section = new TeamFoldersViewModel(() => [Team("/teams", "veille", "Veille", "./output:/output:rw")], strings);
        Assert.Equal("write", Assert.Single(section.Rows).RightsBadge);

        strings.SwitchToFrench();

        Assert.Equal("écriture", Assert.Single(section.Rows).RightsBadge);
    }

    [Fact]
    public void Refresh_re_reads_the_catalog_and_says_when_it_is_empty()
    {
        var teams = new List<TeamSummary> { Team("/teams", "veille", "Veille", "./output:/output:rw") };
        var section = new TeamFoldersViewModel(() => [.. teams]);
        var notified = new List<string?>();
        section.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        Assert.True(section.HasRows);

        teams.Clear();
        section.Refresh();

        Assert.Empty(section.Rows);
        Assert.True(section.IsEmpty);
        Assert.Contains(nameof(TeamFoldersViewModel.HasRows), notified);
        Assert.Contains(nameof(TeamFoldersViewModel.IsEmpty), notified);
    }
}
