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

    /// <summary>
    /// The sidecar records a team's own folders relative to it (<c>./output:/output:rw</c>,
    /// STUDIO-14); the catalog resolves them ONCE, at its boundary, so the cards, the launcher
    /// and the folders modal keep receiving absolute paths and never learn the convention.
    /// The raw entries stay readable on the metadata.
    /// </summary>
    [Fact]
    public void Describe_resolves_relative_sidecar_folders_under_the_team()
    {
        var team = NewTeam("veille");
        File.WriteAllText(
            Path.Combine(team, StudioTeamMetadata.FileName),
            """{"name":"Veille","mounts":["./input:/workspace:ro","./output:/output:rw","/data/docs:/docs:ro"]}""");

        var summary = TeamCatalog.Describe(team);
        var target = TeamCatalog.DescribeTarget(team);

        Assert.Equal(
            [
                $"{Path.Combine(team, "input")}:/workspace:ro",
                $"{Path.Combine(team, "output")}:/output:rw",
                "/data/docs:/docs:ro",
            ],
            summary.Mounts);
        Assert.Equal(summary.Mounts, target.Mounts);
        Assert.Equal(summary.Mounts, Assert.Single(TeamCatalog.List(_root)).Mounts);
        Assert.Equal(
            ["./input:/workspace:ro", "./output:/output:rw", "/data/docs:/docs:ro"],
            summary.Metadata!.Mounts);
    }

    /// <summary>VFS-90: read against the settings, an id entry stands for the declaration as it is today.</summary>
    [Fact]
    public void Describe_reads_the_sidecar_against_the_settings_when_they_are_given()
    {
        var team = NewTeam("veille");
        var id = Orkeon.Domain.Common.MountId.Create();
        var unknown = Orkeon.Domain.Common.MountId.Create();
        File.WriteAllText(
            Path.Combine(team, StudioTeamMetadata.FileName),
            $$"""{"name":"Veille","mounts":["./output:/output:rw","{{id}}|/old/place:/docs:ro","{{unknown}}|/x:/x:ro"]}""");
        string[] declared = [$"{id}|/data/docs:/docs:ro"];

        var summary = TeamCatalog.Describe(team, declared);
        var target = TeamCatalog.DescribeTarget(team, declared);

        Assert.Equal(
            [$"{Path.Combine(team, "output")}:/output:rw", $"{id}|/data/docs:/docs:ro", $"{unknown}|/x:/x:ro"],
            summary.Mounts);
        Assert.Equal(summary.Mounts, target.Mounts);
        Assert.Equal(summary.Mounts, Assert.Single(TeamCatalog.List(_root, TeamListFilter.All, declared)).Mounts);
        Assert.True(summary.HasUnknownMountIds);
        Assert.Equal([unknown.ToString()], summary.UnknownMountIds());
        Assert.Equal([unknown.ToString()], target.UnknownMountIds());

        // Not consulted: the sidecar's own spelling, and no alarm.
        var alone = TeamCatalog.Describe(team);
        Assert.Equal($"{id}|/old/place:/docs:ro", alone.Mounts[1]);
        Assert.False(alone.HasUnknownMountIds);
    }

    /// <summary>D-06: the ids travel with the team, verbatim.</summary>
    [Fact]
    public void A_duplicate_and_an_export_carry_the_ids_verbatim()
    {
        var team = NewTeam("veille");
        var id = Orkeon.Domain.Common.MountId.Create();
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
        {
            Name = "Veille",
            Mounts = ["./output:/output:rw", $"{id}|/data/docs:/docs:ro"],
        });

        var copy = TeamCatalog.Duplicate(team)!;
        var exported = TeamCatalog.ExportTo(team, Path.Combine(_root, "export"))!;

        Assert.Equal($"{id}|/data/docs:/docs:ro", TeamCatalog.Describe(copy).Metadata!.Mounts![1]);
        Assert.Equal($"{id}|/data/docs:/docs:ro", TeamCatalog.Describe(exported).Metadata!.Mounts![1]);
        Assert.Equal("./output:/output:rw", TeamCatalog.Describe(exported).Metadata!.Mounts![0]);
    }

    [Fact]
    public void SaveMounts_rewrites_an_absolute_in_team_folder_to_relative()
    {
        var team = NewTeam("veille");

        TeamCatalog.SaveMounts(team, [$"{Path.Combine(team, "output")}:/output:rw", "/data/docs:/docs:ro"]);

        var sidecar = File.ReadAllText(Path.Combine(team, StudioTeamMetadata.FileName));
        Assert.Contains("\"./output:/output:rw\"", sidecar, StringComparison.Ordinal);
        Assert.Contains("\"/data/docs:/docs:ro\"", sidecar, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine(team, "output"), sidecar, StringComparison.Ordinal);
        // Read back resolved: what the launcher lays on the run is the absolute path.
        Assert.Equal(
            [$"{Path.Combine(team, "output")}:/output:rw", "/data/docs:/docs:ro"],
            TeamCatalog.Describe(team).Mounts);
    }

    /// <summary>
    /// The one place an in-team folder is materialised: the wizard binds rows "inside the
    /// team" before any folder exists, and the save creates them — every later edit too,
    /// idempotently, so an <c>input/</c> already holding documents is left alone.
    /// </summary>
    [Fact]
    public void SaveMetadata_creates_the_in_team_folders_it_records()
    {
        var team = NewTeam("veille");
        Directory.CreateDirectory(Path.Combine(team, "input"));
        File.WriteAllText(Path.Combine(team, "input", "notes.md"), "kept");

        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
        {
            Name = "Veille",
            Mounts = ["./input:/workspace:ro", "./output:/output:rw", "./rapports:/rapports:rw", "/data/docs:/docs:ro"],
        });

        Assert.True(Directory.Exists(Path.Combine(team, "output")));
        Assert.True(Directory.Exists(Path.Combine(team, "rapports")));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(team, "input", "notes.md")));
        // A folder outside the team is the user's own: nothing is created for it, anywhere
        // near the team.
        Assert.False(Directory.Exists(Path.Combine(team, "data")));
        Assert.Equal(
            ["input", "output", "rapports"],
            Directory.EnumerateDirectories(team).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_duplicated_team_keeps_relative_folders_verbatim_and_resolves_them_under_the_copy()
    {
        var team = NewTeam("veille");
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
        {
            Name = "Veille",
            Mounts = ["./output:/output:rw", $"{Path.Combine(_root, "documents")}:/docs:ro"],
        });

        var copy = TeamCatalog.Duplicate(team);

        Assert.NotNull(copy);
        var copied = TeamCatalog.Describe(copy);
        Assert.Equal(["./output:/output:rw", $"{Path.Combine(_root, "documents")}:/docs:ro"], copied.Metadata!.Mounts);
        Assert.Equal(
            [$"{Path.Combine(copy, "output")}:/output:rw", $"{Path.Combine(_root, "documents")}:/docs:ro"],
            copied.Mounts);
        Assert.True(Directory.Exists(Path.Combine(copy, "output")));
    }

    /// <summary>
    /// A sidecar written before the convention carries absolute paths under its own folder.
    /// Every copy — duplicate, export, import — rewrites them relative to the copy, so the
    /// copy writes into its own folder and never into the original's: a copy is a safeguard,
    /// not a compatibility layer, and the rewrite is what makes it one.
    /// </summary>
    [Fact]
    public void An_older_absolute_sidecar_is_rewritten_relative_on_copy()
    {
        var team = NewTeam("veille");
        // A single-file crew, so the import below recognises a team (an empty folder is refused).
        File.WriteAllText(Path.Combine(team, "crew.yaml"), "name: veille");
        Directory.CreateDirectory(Path.Combine(team, "output"));
        File.WriteAllText(
            Path.Combine(team, StudioTeamMetadata.FileName),
            System.Text.Json.JsonSerializer.Serialize(new StudioTeamMetadata
            {
                Name = "Veille",
                Mounts = [$"{Path.Combine(team, "output")}:/output:rw", "/data/docs:/docs:ro"],
            }));

        var duplicated = TeamCatalog.Duplicate(team)!;
        var exported = TeamCatalog.ExportTo(team, Path.Combine(_root, "partage"))!;
        var imported = TeamCatalog.Import(exported, Path.Combine(_root, "imports"), out _)!;

        foreach (var copy in new[] { duplicated, exported, imported })
        {
            var described = TeamCatalog.Describe(copy);
            Assert.Equal(["./output:/output:rw", "/data/docs:/docs:ro"], described.Metadata!.Mounts);
            Assert.Equal([$"{Path.Combine(copy, "output")}:/output:rw", "/data/docs:/docs:ro"], described.Mounts);
        }

        // The original is untouched by its copies.
        Assert.Equal(
            [$"{Path.Combine(team, "output")}:/output:rw", "/data/docs:/docs:ro"],
            TeamCatalog.Describe(team).Metadata!.Mounts);
    }
}
