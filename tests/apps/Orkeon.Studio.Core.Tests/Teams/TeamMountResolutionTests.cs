using Orkeon.Domain.Common;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Teams;

/// <summary>
/// A sidecar read against this machine's settings (VFS-90): an entry naming a declaration by
/// id stands for that declaration as it is today, an older copy is matched by what it
/// declares, the team's own folders resolve under the team, and the launch plan says what
/// goes on the command line — <c>--mount-id</c> for a declaration, <c>--mount</c> for the
/// team's own intent, nothing at all for an id this machine does not have.
/// </summary>
public sealed class TeamMountResolutionTests
{
    private static readonly string Team = Path.Combine(Path.GetTempPath(), "orkeon-teams", "veille");

    private static readonly MountId A = MountId.Create();
    private static readonly MountId B = MountId.Create();

    private static readonly string[] Declared =
    [
        $"{A}|/data/out-a:/output:rw",
        $"{B}|/data/out-b:/output:rw",
        "/data/docs:/docs:ro",
    ];

    [Fact]
    public void An_entry_naming_a_declaration_by_id_stands_for_that_declaration_as_it_is_today()
    {
        // The sidecar's copy is stale: the folder moved since. The id wins (D-02).
        var resolved = TeamMountResolution.Resolve(Team, [$"{B}|/old/place:/output:rw"], Declared);

        var mount = Assert.Single(resolved);
        Assert.Equal(TeamMountSource.Settings, mount.Source);
        Assert.Equal($"{B}|/data/out-b:/output:rw", mount.Effective);
        Assert.Equal(B, mount.Id);
        Assert.Equal("/data/out-b", mount.SettingsEntry!.PhysicalPath);
    }

    [Fact]
    public void An_id_this_machine_does_not_declare_is_unknown_and_blocks_the_plan()
    {
        var unknown = MountId.Create();
        var resolved = TeamMountResolution.Resolve(Team, [$"{unknown}|/home/x/out:/output:rw", "./output:/result:rw"], Declared);

        Assert.Equal(TeamMountSource.UnknownId, resolved[0].Source);
        Assert.Equal(unknown, resolved[0].Id);

        var plan = LaunchMountPlan.For(resolved);
        Assert.Equal([unknown.ToString()], plan.UnknownIds);
        Assert.Empty(plan.MountIds);
        Assert.Equal([$"{Path.Combine(Team, "output")}:/result:rw"], plan.Mounts);
    }

    [Fact]
    public void An_older_copy_is_matched_by_what_it_declares_and_selected_by_the_entrys_id()
    {
        var resolved = TeamMountResolution.Resolve(Team, ["/data/out-a/:/output:rw"], Declared);

        var mount = Assert.Single(resolved);
        Assert.Equal(TeamMountSource.Settings, mount.Source);
        Assert.Equal(A, mount.Id);
        Assert.Equal([A.ToString()], LaunchMountPlan.For(resolved).MountIds);
    }

    [Fact]
    public void A_declaration_without_an_id_that_holds_the_copy_needs_nothing_on_the_command_line()
    {
        var resolved = TeamMountResolution.Resolve(Team, ["/data/docs:/docs:ro"], Declared);

        Assert.Equal(TeamMountSource.Settings, Assert.Single(resolved).Source);
        var plan = LaunchMountPlan.For(resolved);
        Assert.Empty(plan.MountIds);
        Assert.Empty(plan.Mounts);
    }

    [Fact]
    public void The_teams_own_folders_and_its_copies_go_as_mount_arguments()
    {
        var resolved = TeamMountResolution.Resolve(
            Team,
            ["./input:/workspace:ro", "/data/docs:/veille:rw", $"{Path.Combine(Team, "output")}:/output:rw", "not a mount"],
            Declared);

        Assert.Equal(
            [TeamMountSource.InsideTeam, TeamMountSource.Copy, TeamMountSource.InsideTeam, TeamMountSource.Unreadable],
            resolved.Select(m => m.Source));

        var plan = LaunchMountPlan.For(resolved);
        Assert.Equal(
            [$"{Path.Combine(Team, "input")}:/workspace:ro", "/data/docs:/veille:rw", $"{Path.Combine(Team, "output")}:/output:rw", "not a mount"],
            plan.Mounts);
        Assert.Empty(plan.MountIds);
        Assert.Empty(plan.UnknownIds);
    }

    [Fact]
    public void Without_settings_to_consult_an_id_entry_is_a_copy_never_an_alarm()
    {
        var resolved = TeamMountResolution.Resolve(Team, [$"{A}|/data/out-a:/output:rw"], declaredMounts: null);

        var mount = Assert.Single(resolved);
        Assert.Equal(TeamMountSource.Copy, mount.Source);
        Assert.Equal(A, mount.Id);
        Assert.Empty(LaunchMountPlan.For(resolved).UnknownIds);
    }

    [Fact]
    public void A_stray_id_on_a_team_local_entry_is_dropped()
    {
        var resolved = TeamMountResolution.Resolve(Team, [$"{A}|./output:/output:rw"], Declared);

        var mount = Assert.Single(resolved);
        Assert.Equal(TeamMountSource.InsideTeam, mount.Source);
        Assert.Equal($"{Path.Combine(Team, "output")}:/output:rw", mount.Effective);
        Assert.Null(mount.Id);
    }

    [Fact]
    public void Two_teams_naming_one_declaration_are_both_listed_under_its_id()
    {
        var teams = new[]
        {
            new TeamSummary { Name = "Veille", Slug = "veille", Path = Team, Metadata = new StudioTeamMetadata { Mounts = [$"{A}|/x:/output:rw"] } },
            new TeamSummary { Name = "Audit", Slug = "audit", Path = Team + "-2", Metadata = new StudioTeamMetadata { Mounts = [$"{A}|/x:/output:rw", "./output:/result:rw"] } },
            new TeamSummary { Name = "Brute", Slug = "brute", Path = Team + "-3" },
        };

        var byId = TeamMountReferences.ByMountId(teams);

        Assert.Equal(["Veille", "Audit"], byId[A].Select(t => t.Name));
        Assert.False(byId.ContainsKey(B));
        Assert.Empty(TeamMountReferences.TeamsUsing(B, teams));
    }
}
