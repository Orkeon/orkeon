using Orkeon.Domain.Common;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Launch;

/// <summary>
/// What a launch prepares of a team's own folders (STUDIO-60), the rule the workshop's
/// <c>run.sh</c>/<c>run.cmd</c> already apply: a missing writable folder is created, a
/// missing read-only one refuses the launch by name, and nothing outside the team is touched.
/// </summary>
public sealed class TeamFolderPreparationTests
{
    private const string Team = "/teams/veille";

    private static ResolvedTeamMount InsideTeam(string raw) =>
        new(raw, TeamMountPaths.Resolve(Team, raw), TeamMountSource.InsideTeam, null, null);

    [Fact]
    public void A_missing_writable_folder_is_created()
    {
        var directories = new FakeDirectoryProbe(Team);

        var result = TeamFolderPreparation.Prepare(Team, [InsideTeam("./output:/output:rw")], directories);

        Assert.True(result.Succeeded);
        Assert.Equal([Path.Combine(Team, "output")], result.Created);
        Assert.Equal([Path.Combine(Team, "output")], directories.Created);
    }

    [Fact]
    public void A_missing_read_only_folder_refuses_the_launch_naming_the_folder_and_its_mount_point()
    {
        var directories = new FakeDirectoryProbe(Team);

        var result = TeamFolderPreparation.Prepare(Team, [InsideTeam("./input:/workspace:ro")], directories);

        Assert.False(result.Succeeded);
        Assert.Equal([(Path.Combine(Team, "input"), "/workspace")], result.MissingReadOnly);
        Assert.Empty(result.Created);
        Assert.Empty(directories.Created);
    }

    [Fact]
    public void Folders_that_exist_are_left_alone_and_nothing_is_refused()
    {
        var directories = new FakeDirectoryProbe(Team, Path.Combine(Team, "output"), Path.Combine(Team, "input"));

        var result = TeamFolderPreparation.Prepare(
            Team,
            [InsideTeam("./output:/output:rw"), InsideTeam("./input:/workspace:ro")],
            directories);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Created);
        Assert.Empty(directories.Created);
    }

    [Fact]
    public void A_folder_writable_without_delete_is_created_like_a_writable_one()
    {
        var directories = new FakeDirectoryProbe(Team);

        var result = TeamFolderPreparation.Prepare(Team, [InsideTeam("./rapports:/rapports:rwnd")], directories);

        Assert.Equal([Path.Combine(Team, "rapports")], result.Created);
    }

    [Fact]
    public void Entries_that_are_not_the_teams_own_are_ignored()
    {
        var id = MountId.Create();
        var directories = new FakeDirectoryProbe(Team);
        var settings = new MountDefinition { Id = id, PhysicalPath = "/data/out", VirtualPath = "/output", Rights = MountRights.ReadWrite };

        var result = TeamFolderPreparation.Prepare(
            Team,
            [
                new ResolvedTeamMount("/data/docs:/veille:ro", "/data/docs:/veille:ro", TeamMountSource.Copy, null, null),
                new ResolvedTeamMount($"{id}|/data/out:/output:rw", settings.ToMountString(), TeamMountSource.Settings, id, settings),
                new ResolvedTeamMount($"{id}|/data/in:/workspace:ro", $"{id}|/data/in:/workspace:ro", TeamMountSource.UnknownId, id, null),
                new ResolvedTeamMount("this is not a mount", "this is not a mount", TeamMountSource.Unreadable, null, null),
            ],
            directories);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Created);
        Assert.Empty(directories.Created);
    }

    [Fact]
    public void An_entry_that_escapes_the_team_is_ignored()
    {
        // Pinned: such an entry resolves InsideTeam with Effective == Raw, and must stay untouched.
        var mount = InsideTeam("./../x:/x:rw");
        Assert.Equal(TeamMountSource.InsideTeam, mount.Source);
        Assert.Equal(mount.Raw, mount.Effective);
        var directories = new FakeDirectoryProbe(Team);

        var result = TeamFolderPreparation.Prepare(Team, [mount], directories);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Created);
        Assert.Empty(directories.Created);
    }

    [Fact]
    public void An_absolute_entry_under_the_team_is_ignored()
    {
        var raw = Path.Combine(Team, "output") + ":/output:rw";
        var directories = new FakeDirectoryProbe(Team);

        var result = TeamFolderPreparation.Prepare(
            Team,
            [new ResolvedTeamMount(raw, raw, TeamMountSource.InsideTeam, null, null)],
            directories);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Created);
        Assert.Empty(directories.Created);
    }

    [Fact]
    public void Without_a_team_folder_nothing_is_prepared()
    {
        var directories = new FakeDirectoryProbe();

        var result = TeamFolderPreparation.Prepare(null, [InsideTeam("./output:/output:rw")], directories);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Created);
        Assert.Empty(directories.Created);
    }

    [Fact]
    public void A_creation_the_disk_refuses_is_a_failure_naming_the_folder_and_the_reason()
    {
        var directories = new FakeDirectoryProbe(Team)
        {
            CreateFault = _ => new UnauthorizedAccessException("Read-only media."),
        };

        var result = TeamFolderPreparation.Prepare(Team, [InsideTeam("./output:/output:rw")], directories);

        Assert.False(result.Succeeded);
        Assert.Equal([(Path.Combine(Team, "output"), "Read-only media.")], result.Failures);
        Assert.Empty(result.Created);
    }

    [Fact]
    public void Every_folder_is_examined_so_the_refusal_names_them_all()
    {
        var directories = new FakeDirectoryProbe(Team);

        var result = TeamFolderPreparation.Prepare(
            Team,
            [InsideTeam("./input:/workspace:ro"), InsideTeam("./output:/output:rw"), InsideTeam("./refs:/refs:ro")],
            directories);

        Assert.False(result.Succeeded);
        Assert.Equal(2, result.MissingReadOnly.Count);
        Assert.Equal([Path.Combine(Team, "output")], result.Created);
    }
}
