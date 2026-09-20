using Orkeon.Domain.Common;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Launch;

/// <summary>
/// What of a team's folders a launch says on the command line (STUDIO-15 D-05, VFS-90): a
/// settings declaration by its id, the team's own intent as <c>--mount</c>, and nothing for
/// a declaration this machine does not have — the launch is refused instead.
/// </summary>
public sealed class LaunchMountPlanTests
{
    private static readonly MountId A = MountId.Create();

    private static ResolvedTeamMount Resolved(string raw, TeamMountSource source, MountId? id = null, string? effective = null) =>
        new(raw, effective ?? raw, source, id, source == TeamMountSource.Settings
            ? new MountDefinition { Id = id, PhysicalPath = "/data/out", VirtualPath = "/output", Rights = MountRights.ReadWrite }
            : null);

    [Fact]
    public void A_settings_declaration_is_selected_by_id_and_never_laid_as_a_path()
    {
        var plan = LaunchMountPlan.For([Resolved($"{A}|/data/out:/output:rw", TeamMountSource.Settings, A)]);

        Assert.Equal([A.ToString()], plan.MountIds);
        Assert.Empty(plan.Mounts);
        Assert.Empty(plan.UnknownIds);
    }

    [Fact]
    public void The_same_declaration_named_twice_is_selected_once()
    {
        var plan = LaunchMountPlan.For(
        [
            Resolved($"{A}|/data/out:/output:rw", TeamMountSource.Settings, A),
            Resolved("/data/out:/output:rw", TeamMountSource.Settings, A),
        ]);

        Assert.Equal([A.ToString()], plan.MountIds);
    }

    [Fact]
    public void The_teams_own_folders_and_copies_are_laid_in_order_and_an_unreadable_entry_passes_through()
    {
        var plan = LaunchMountPlan.For(
        [
            Resolved("./output:/output:rw", TeamMountSource.InsideTeam, effective: "/teams/veille/output:/output:rw"),
            Resolved("/data/docs:/veille:ro", TeamMountSource.Copy),
            Resolved("this is not a mount", TeamMountSource.Unreadable),
        ]);

        Assert.Equal(["/teams/veille/output:/output:rw", "/data/docs:/veille:ro", "this is not a mount"], plan.Mounts);
        Assert.Empty(plan.MountIds);
    }

    [Fact]
    public void An_unknown_id_is_reported_and_nothing_stands_in_for_it()
    {
        var unknown = MountId.Create();
        var plan = LaunchMountPlan.For([Resolved($"{unknown}|/home/x/out:/output:rw", TeamMountSource.UnknownId, unknown)]);

        Assert.Equal([unknown.ToString()], plan.UnknownIds);
        Assert.Empty(plan.Mounts);
        Assert.Empty(plan.MountIds);
    }
}
