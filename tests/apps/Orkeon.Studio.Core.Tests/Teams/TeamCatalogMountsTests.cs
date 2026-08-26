using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Teams;

/// <summary>
/// The v2 additions to the catalog (RC2-FEAT-05): the sidecar's mount strings, the agent
/// count that also sees the promoted <c>crew/agents</c> layout, the export that never
/// carries a settings file, and the one canonical path spelling the history match uses.
/// </summary>
public sealed class TeamCatalogMountsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-mounts-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string NewTeam(string slug)
    {
        var team = Path.Combine(_root, slug);
        Directory.CreateDirectory(team);
        return team;
    }

    [Fact]
    public void SaveMounts_round_trips_through_the_sidecar_and_the_summary()
    {
        var team = NewTeam("veille");
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = "Veille", Profile = "kimi" });

        TeamCatalog.SaveMounts(team, ["C:/docs:/docs:ro", "C:/out:/output:rw"]);

        var summary = TeamCatalog.Describe(team);
        Assert.Equal(["C:/docs:/docs:ro", "C:/out:/output:rw"], summary.Mounts);
        // The rest of the sidecar survives the mounts write.
        Assert.Equal("Veille", summary.Name);
        Assert.Equal("kimi", summary.Profile);
    }

    [Fact]
    public void SaveMounts_on_a_bare_folder_creates_a_minimal_sidecar_and_an_empty_list_clears_it()
    {
        var team = NewTeam("brute");

        TeamCatalog.SaveMounts(team, ["C:/x:/x:ro"]);
        Assert.Equal(["C:/x:/x:ro"], TeamCatalog.Describe(team).Mounts);

        TeamCatalog.SaveMounts(team, []);
        Assert.Empty(TeamCatalog.Describe(team).Mounts);
    }

    [Fact]
    public void The_agent_count_sees_both_the_flat_and_the_promoted_layouts()
    {
        var flat = NewTeam("plate");
        Directory.CreateDirectory(Path.Combine(flat, "agents"));
        File.WriteAllText(Path.Combine(flat, "agents", "a.yaml"), "role: A");
        File.WriteAllText(Path.Combine(flat, "agents", "b.yml"), "role: B");

        var promoted = NewTeam("promue");
        Directory.CreateDirectory(Path.Combine(promoted, "crew", "agents"));
        File.WriteAllText(Path.Combine(promoted, "crew", "agents", "a.yaml"), "role: A");

        Assert.Equal(2, TeamCatalog.Describe(flat).AgentCount);
        Assert.Equal(1, TeamCatalog.Describe(promoted).AgentCount);
        Assert.Equal(1, TeamCatalog.DescribeTarget(promoted).AgentCount);
        Assert.Null(TeamCatalog.Describe(NewTeam("vide")).AgentCount);
    }

    [Fact]
    public void ExportTo_copies_the_tree_but_never_the_settings_file()
    {
        var team = NewTeam("export");
        Directory.CreateDirectory(Path.Combine(team, "crew"));
        File.WriteAllText(Path.Combine(team, "crew", "config.yaml"), "name: x");
        File.WriteAllText(Path.Combine(team, "run.cmd"), "orkeon run .");
        File.WriteAllText(Path.Combine(team, "appsettings.json"), "{\"Orkeon\":{}}");

        var destinationParent = Path.Combine(_root, "partage");
        var destination = TeamCatalog.ExportTo(team, destinationParent);

        Assert.Equal(Path.Combine(destinationParent, "export"), destination);
        Assert.True(File.Exists(Path.Combine(destination!, "crew", "config.yaml")));
        Assert.True(File.Exists(Path.Combine(destination!, "run.cmd")));
        Assert.False(File.Exists(Path.Combine(destination!, "appsettings.json")));
    }

    [Fact]
    public void ExportTo_refuses_an_existing_destination()
    {
        var team = NewTeam("deja");
        var destinationParent = Path.Combine(_root, "partage");
        Directory.CreateDirectory(Path.Combine(destinationParent, "deja"));

        Assert.Null(TeamCatalog.ExportTo(team, destinationParent));
    }

    /// <summary>
    /// A team's write roots are bound to folders INSIDE it (`&lt;team&gt;/output:/output:rw`,
    /// derived from the blueprint at adoption). Copied verbatim, the duplicate would write
    /// into the ORIGINAL team's folder — two teams silently sharing one output. The mounts
    /// move with the folder; the ones pointing outside it are the user's own and stay.
    /// </summary>
    [Fact]
    public void A_duplicated_team_writes_into_its_own_folder()
    {
        var team = NewTeam("veille");
        Directory.CreateDirectory(Path.Combine(team, "output"));
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
        {
            Name = "Veille",
            Mounts =
            [
                $"{Path.Combine(team, "output")}:/output:rw",
                $"{Path.Combine(_root, "documents")}:/docs:ro",
            ],
        });

        var copy = TeamCatalog.Duplicate(team);

        Assert.NotNull(copy);
        var mounts = TeamCatalog.Describe(copy).Mounts;
        Assert.Contains(mounts, m => m.StartsWith(Path.Combine(copy, "output"), StringComparison.Ordinal));
        Assert.DoesNotContain(mounts, m => m.StartsWith(Path.Combine(team, "output"), StringComparison.Ordinal));
        // A folder the user allowed elsewhere is their choice, not the team's layout.
        Assert.Contains(mounts, m => m.StartsWith(Path.Combine(_root, "documents"), StringComparison.Ordinal));
    }

    [Fact]
    public void An_exported_team_carries_its_own_folders_too()
    {
        var team = NewTeam("partagee");
        Directory.CreateDirectory(Path.Combine(team, "output"));
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
        {
            Name = "Partagée",
            Mounts = [$"{Path.Combine(team, "output")}:/output:rw"],
        });

        var destination = TeamCatalog.ExportTo(team, Path.Combine(_root, "partage"));

        Assert.NotNull(destination);
        var mount = Assert.Single(TeamCatalog.Describe(destination).Mounts);
        Assert.StartsWith(Path.Combine(destination, "output"), mount, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizePath_strips_the_trailing_separator_and_survives_garbage()
    {
        var team = NewTeam("norme");
        Assert.Equal(TeamCatalog.NormalizePath(team), TeamCatalog.NormalizePath(team + Path.DirectorySeparatorChar));
        Assert.Equal("", TeamCatalog.NormalizePath("  "));
        Assert.Equal("\0bad", TeamCatalog.NormalizePath("\0bad"));
    }
}
