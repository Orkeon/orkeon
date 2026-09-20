using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Teams;

/// <summary>
/// The sidecar's folder convention (STUDIO-14): a physical segment starting with <c>./</c>
/// is relative to the team folder, so a team is a folder one carries — copied, exported or
/// moved, its own <c>input/</c> and <c>output/</c> follow it without a rebase step.
/// Everything else in a sidecar keeps its spelling, both ways.
/// </summary>
public sealed class TeamMountPathsTests
{
    private static readonly string Team = Path.Combine(Path.GetTempPath(), "orkeon-teams", "veille");

    [Fact]
    public void Resolve_binds_a_dot_slash_segment_under_the_team_folder()
    {
        var resolved = TeamMountPaths.Resolve(Team, "./output:/output:rw");

        Assert.Equal($"{Path.Combine(Team, "output")}:/output:rw", resolved);
        Assert.Equal(
            [$"{Path.Combine(Team, "input")}:/workspace:ro", $"{Path.Combine(Team, "output")}:/output:rw"],
            TeamMountPaths.ResolveAll(Team, ["./input:/workspace:ro", "./output:/output:rw"]));
        Assert.Empty(TeamMountPaths.ResolveAll(Team, null));
    }

    [Fact]
    public void Relativize_rewrites_an_absolute_in_team_folder_to_dot_slash()
    {
        var relativized = TeamMountPaths.Relativize(Team, $"{Path.Combine(Team, "output")}:/output:rw");

        Assert.Equal("./output:/output:rw", relativized);
        // A trailing separator names the same folder; a nested one keeps '/' on every OS.
        Assert.Equal("./input:/workspace:ro", TeamMountPaths.Relativize(Team, $"{Path.Combine(Team, "input")}{Path.DirectorySeparatorChar}:/workspace:ro"));
        Assert.Equal("./data/in:/in:ro", TeamMountPaths.Relativize(Team, $"{Path.Combine(Team, "data", "in")}:/in:ro"));
        // Already the convention: verbatim, never `././`.
        Assert.Equal("./output:/output:rw", TeamMountPaths.Relativize(Team, "./output:/output:rw"));
    }

    /// <summary>VFS-90 D-07: a team-local folder is not a settings entry and carries no id, whichever way it travels.</summary>
    [Fact]
    public void An_id_never_rides_a_team_local_entry()
    {
        var id = Orkeon.Domain.Common.MountId.Create();

        Assert.Equal($"{Path.Combine(Team, "output")}:/output:rw", TeamMountPaths.Resolve(Team, $"{id}|./output:/output:rw"));
        Assert.Equal("./output:/output:rw", TeamMountPaths.Relativize(Team, $"{id}|{Path.Combine(Team, "output")}:/output:rw"));
        Assert.Equal("./output:/output:rw", TeamMountPaths.Relativize(Team, $"{id}|./output:/output:rw"));
        // A folder outside the team keeps the id it was recorded with: it names a declaration.
        Assert.Equal($"{id}|/data/docs:/docs:ro", TeamMountPaths.Relativize(Team, $"{id}|/data/docs:/docs:ro"));
    }

    [Fact]
    public void A_folder_outside_the_team_is_left_alone_both_ways()
    {
        var outside = $"{Path.Combine(Path.GetTempPath(), "documents")}:/docs:ro";
        var sibling = $"{Team}-old{Path.DirectorySeparatorChar}output:/output:rw";
        var root = $"{Team}:/team:ro";

        Assert.Equal(outside, TeamMountPaths.Relativize(Team, outside));
        Assert.Equal(outside, TeamMountPaths.Resolve(Team, outside));
        // `<team>-old` is not inside `<team>`: a boundary, not a prefix.
        Assert.Equal(sibling, TeamMountPaths.Relativize(Team, sibling));
        // The team folder itself names no sub-folder to be relative to.
        Assert.Equal(root, TeamMountPaths.Relativize(Team, root));
        Assert.False(TeamMountPaths.IsTeamRelative(outside));
    }

    [Fact]
    public void A_single_letter_folder_survives_the_drive_letter_rule()
    {
        // The mount grammar reads a drive-letter prefix on every OS, so a bare one-letter root
        // splits wrong: `x:/x:rw` reads as the Windows path `x:/x` followed by `rw`.
        Assert.False(MountDefinition.TryParse("x:/x:rw", out _, out _));

        var inside = TeamMountPaths.InsideTeam("/x", MountRights.ReadWrite);

        Assert.Equal("./x:/x:rw", inside);
        Assert.True(MountDefinition.TryParse(inside, out var parsed, out _));
        Assert.Equal("./x", parsed!.PhysicalPath);
        Assert.Equal("/x", parsed.VirtualPath);
        Assert.Equal($"{Path.Combine(Team, "x")}:/x:rw", TeamMountPaths.Resolve(Team, inside));
    }

    [Fact]
    public void An_escaping_segment_is_not_team_relative()
    {
        foreach (var escaping in new[] { "./../x:/x:ro", "./a/../../x:/x:ro", "./:/x:ro", "./a//b:/x:ro" })
        {
            Assert.True(TeamMountPaths.IsTeamRelative(escaping));
            Assert.False(TeamMountPaths.TryGetRelativeFolder(escaping, out _));
            // Not bound under the team — the entry keeps its spelling, and nothing vouches for it.
            Assert.Equal(escaping, TeamMountPaths.Resolve(Team, escaping));
        }

        Assert.True(TeamMountPaths.TryGetRelativeFolder("./rapports:/rapports:rw", out var folder));
        Assert.Equal("rapports", folder);
    }

    [Fact]
    public void A_windows_team_path_with_a_colon_round_trips_quoted()
    {
        // A colon inside a team path segment only ever appears once resolved; the resolved
        // string quotes it so the runtime's parser reads the whole path back.
        var oddTeam = Path.Combine(Path.GetTempPath(), "orkeon:teams", "veille");

        var resolved = TeamMountPaths.Resolve(oddTeam, "./output:/output:rw");

        Assert.StartsWith("\"", resolved, StringComparison.Ordinal);
        Assert.True(MountDefinition.TryParse(resolved, out var parsed, out var error), error);
        Assert.Equal(Path.GetFullPath(Path.Combine(oddTeam, "output")), parsed!.PhysicalPath);
        Assert.Equal("/output", parsed.VirtualPath);
        Assert.Equal(MountRights.ReadWrite, parsed.Rights);
        // And back: the relative spelling never needs the quotes.
        Assert.Equal("./output:/output:rw", TeamMountPaths.Relativize(oddTeam, resolved));
    }

    [Fact]
    public void Inside_team_names_the_read_root_input_and_a_write_root_after_itself()
    {
        Assert.Equal("./input:/workspace:ro", TeamMountPaths.InsideTeam(TeamMountPaths.ReadRoot, MountRights.ReadOnly));
        Assert.Equal("./output:/output:rw", TeamMountPaths.InsideTeam(TeamMountPaths.WriteRoot, MountRights.ReadWrite));
        Assert.Equal("./rapports:/rapports:rw", TeamMountPaths.InsideTeam("/rapports", MountRights.ReadWrite));

        Assert.Equal("input", TeamMountPaths.FolderFor("/workspace"));
        Assert.Equal("output", TeamMountPaths.FolderFor("/output"));
        Assert.Throws<ArgumentException>(() => TeamMountPaths.FolderFor("/"));
    }

    [Fact]
    public void An_unreadable_entry_passes_through_untouched()
    {
        const string garbage = "this is not a mount";

        Assert.Equal(garbage, TeamMountPaths.Resolve(Team, garbage));
        Assert.Equal(garbage, TeamMountPaths.Relativize(Team, garbage));
        Assert.Equal([garbage], TeamMountPaths.RelativizeAll(Team, [garbage]));
        Assert.False(TeamMountPaths.IsTeamRelative(garbage));
        Assert.False(TeamMountPaths.TryGetRelativeFolder(garbage, out _));
        Assert.False(TeamMountPaths.TryGetRelativeFolder(null, out _));
    }
}
