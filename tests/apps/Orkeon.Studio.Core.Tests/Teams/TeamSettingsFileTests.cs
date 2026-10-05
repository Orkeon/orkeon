using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Teams;

/// <summary>
/// The settings file a workshop keeps for a team (STUDIO-62): found at the one place the
/// workshop's launchers read it, <c>settings/&lt;slug&gt;/appsettings.json</c> beside the teams
/// root, for a team folder right under that root — and nowhere else. The slug is the folder's
/// name; nothing is read inside the team. Every probe is a double: no test touches the disk.
/// </summary>
public sealed class TeamSettingsFileTests
{
    private const string TeamsRoot = "/ws/teams";

    /// <summary>The exact location the rule names, spelled by the layout itself.</summary>
    private static readonly string VeilleSettings = WorkshopLayout.SettingsFileOf(TeamsRoot, "veille");

    private static Func<string, bool> Files(params string[] existing)
    {
        var set = new HashSet<string>(existing, StringComparer.Ordinal);
        return path => set.Contains(path);
    }

    [Fact]
    public void Found_for_a_team_folder_right_under_the_root_when_the_file_exists()
    {
        var directories = new FakeDirectoryProbe("/ws/teams/veille");

        var found = TeamSettingsFile.Find(TeamsRoot, "/ws/teams/veille", directories, Files(VeilleSettings));

        Assert.Equal(VeilleSettings, found);
    }

    [Fact]
    public void Found_when_the_target_is_a_file_inside_the_team()
    {
        var directories = new FakeDirectoryProbe("/ws/teams/veille");

        var found = TeamSettingsFile.Find(TeamsRoot, "/ws/teams/veille/crew.yaml", directories, Files(VeilleSettings));

        Assert.Equal(VeilleSettings, found);
    }

    [Fact]
    public void Found_when_the_target_is_the_crew_file_of_a_promoted_team()
    {
        // A promoted team keeps its definition under crew/: a file picked there still belongs to
        // the team folder above, the one the workshop's launchers name.
        var directories = new FakeDirectoryProbe("/ws/teams/veille", "/ws/teams/veille/crew");

        var found = TeamSettingsFile.Find(TeamsRoot, "/ws/teams/veille/crew/config.yaml", directories, Files(VeilleSettings));

        Assert.Equal(VeilleSettings, found);
    }

    [Fact]
    public void Null_when_the_file_is_missing()
    {
        var directories = new FakeDirectoryProbe("/ws/teams/veille");

        Assert.Null(TeamSettingsFile.Find(TeamsRoot, "/ws/teams/veille", directories, Files()));
    }

    [Fact]
    public void Null_when_the_team_is_not_right_under_the_root()
    {
        var directories = new FakeDirectoryProbe("/elsewhere/veille", "/ws/teams/a", "/ws/teams/a/b");
        var files = Files(
            VeilleSettings,
            WorkshopLayout.SettingsFileOf(TeamsRoot, "b"),
            WorkshopLayout.SettingsFileOf("/elsewhere", "veille"));

        Assert.Null(TeamSettingsFile.Find(TeamsRoot, "/elsewhere/veille", directories, files));
        Assert.Null(TeamSettingsFile.Find(TeamsRoot, "/ws/teams/a/b", directories, files));
        Assert.Null(TeamSettingsFile.Find(TeamsRoot, "/ws/teams/a/b/crew.yaml", directories, files));
    }

    [Fact]
    public void Null_when_there_is_no_teams_root_or_no_target()
    {
        var directories = new FakeDirectoryProbe("/ws/teams/veille");

        Assert.Null(TeamSettingsFile.Find(null, "/ws/teams/veille", directories, Files(VeilleSettings)));
        Assert.Null(TeamSettingsFile.Find("", "/ws/teams/veille", directories, Files(VeilleSettings)));
        Assert.Null(TeamSettingsFile.Find(TeamsRoot, null, directories, Files(VeilleSettings)));
        Assert.Null(TeamSettingsFile.Find(TeamsRoot, "", directories, Files(VeilleSettings)));
    }

    [Fact]
    public void The_slug_is_the_folder_name_not_the_card_name()
    {
        // The card may say « Veille concurrentielle »; the workshop indexes everything by the folder.
        var directories = new FakeDirectoryProbe("/ws/teams/veille");
        var files = Files(WorkshopLayout.SettingsFileOf(TeamsRoot, "Veille concurrentielle"));

        Assert.Null(TeamSettingsFile.Find(TeamsRoot, "/ws/teams/veille", directories, files));
        Assert.Equal(VeilleSettings, TeamSettingsFile.Find(TeamsRoot, "/ws/teams/veille", directories, Files(VeilleSettings)));
    }

    [Fact]
    public void A_trailing_separator_on_the_root_or_the_team_changes_nothing()
    {
        var directories = new FakeDirectoryProbe("/ws/teams/veille", "/ws/teams/veille/");

        Assert.Equal(VeilleSettings, TeamSettingsFile.Find("/ws/teams/", "/ws/teams/veille", directories, Files(VeilleSettings)));
        Assert.Equal(VeilleSettings, TeamSettingsFile.Find(TeamsRoot, "/ws/teams/veille/", directories, Files(VeilleSettings)));
    }
}
