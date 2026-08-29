using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.FileSystem;

/// <summary>
/// « Réglages › Dossiers autorisés » is the list of what this machine allows. A team associates
/// entries from it and never declares its own, so a team folder outside that list is either the
/// team's own plumbing or a reach the launcher must refuse.
/// </summary>
public sealed class DeclaredMountsTests
{
    private static string Team => Path.Combine(Path.GetTempPath(), "orkeon-teams", "veille");

    [Fact]
    public void The_team_folder_of_a_picked_folder_is_that_folder()
    {
        var directories = new FakeDirectoryProbe(Team);

        Assert.Equal(Team, DeclaredMounts.TeamDirectoryOf(Team, directories));
    }

    [Fact]
    public void The_team_folder_of_a_picked_crew_file_is_the_folder_holding_it()
    {
        // The two shapes TeamCatalog.DescribeTarget reads a sidecar from: the folder, or a file
        // inside it. A launcher that answered only the first would block every YAML-file target.
        var crewFile = Path.Combine(Team, "crew.yaml");
        var directories = new FakeDirectoryProbe(Team);

        Assert.Equal(Team, DeclaredMounts.TeamDirectoryOf(crewFile, directories));
    }

    [Fact]
    public void No_selection_names_no_team_folder()
    {
        var directories = new FakeDirectoryProbe();

        Assert.Null(DeclaredMounts.TeamDirectoryOf(null, directories));
        Assert.Null(DeclaredMounts.TeamDirectoryOf("", directories));
    }

    [Fact]
    public void A_path_the_platform_refuses_names_no_team_folder()
    {
        // Reporting "no team folder" is what makes the mount fall through to the allow-list;
        // throwing here would take the launch screen down with it.
        var directories = new FakeDirectoryProbe();

        Assert.Null(DeclaredMounts.TeamDirectoryOf("\0invalid", directories));
    }

    [Fact]
    public void The_teams_own_folders_never_block_it_when_a_crew_file_was_picked()
    {
        // The regression this pairs with: TeamDirectoryOf and BlockingFolders must agree, or a
        // team picked by its crew.yaml blocks on the /output it created itself at adoption.
        var directories = new FakeDirectoryProbe(Team);
        var teamDirectory = DeclaredMounts.TeamDirectoryOf(Path.Combine(Team, "crew.yaml"), directories);

        var blocking = DeclaredMounts.BlockingFolders(
            [$"{Path.Combine(Team, "output")}:/output:rw"], [], teamDirectory);

        Assert.Empty(blocking);
    }

    [Fact]
    public void A_folder_is_declared_on_its_disk_path_whatever_the_team_calls_it()
    {
        IReadOnlyList<string> declared = ["/data/docs:/docs:ro"];

        // Same folder, another virtual spelling and other rights: still the same allowed folder.
        Assert.True(DeclaredMounts.IsDeclared("/data/docs:/veille:rw", declared));
        Assert.True(DeclaredMounts.IsDeclared("/data/docs/:/docs:ro", declared));
        Assert.False(DeclaredMounts.IsDeclared("/data/autre:/docs:ro", declared));
        Assert.False(DeclaredMounts.IsDeclared("not a mount", declared));
    }

    [Fact]
    public void A_team_using_only_declared_folders_blocks_on_nothing()
    {
        var blocking = DeclaredMounts.BlockingFolders(
            ["/data/docs:/docs:ro"], ["/data/docs:/docs:ro"], Team);

        Assert.Empty(blocking);
    }

    [Fact]
    public void The_teams_own_folders_never_block_it()
    {
        // /output and /input are created inside the team at adoption and are never declared.
        // Counting them would make every adopted team unlaunchable.
        var blocking = DeclaredMounts.BlockingFolders(
            [
                $"{Path.Combine(Team, "output")}:/output:rw",
                $"{Path.Combine(Team, "input")}:/workspace:ro",
            ],
            [],
            Team);

        Assert.Empty(blocking);
    }

    [Fact]
    public void A_folder_neither_declared_nor_inside_the_team_blocks_and_is_named_by_its_virtual_path()
    {
        var blocking = DeclaredMounts.BlockingFolders(
            ["/data/docs:/docs:ro", "/elsewhere/archives:/archives:ro"],
            ["/data/docs:/docs:ro"],
            Team);

        // ADR-008: the screen may name the virtual path, never the folder on this machine.
        Assert.Equal(["/archives"], blocking);
    }

    [Fact]
    public void An_entry_nobody_can_parse_blocks_too()
    {
        var blocking = DeclaredMounts.BlockingFolders(["this is not a mount"], [], Team);

        Assert.Equal(["this is not a mount"], blocking);
    }

    [Fact]
    public void Without_a_team_directory_only_the_settings_vouch_for_a_folder()
    {
        // A crew file picked by hand, outside any adopted team: nothing is "inside the team".
        var blocking = DeclaredMounts.BlockingFolders(
            ["/data/docs:/docs:ro", "/elsewhere:/x:ro"], ["/data/docs:/docs:ro"], teamDirectory: null);

        Assert.Equal(["/x"], blocking);
    }
}
