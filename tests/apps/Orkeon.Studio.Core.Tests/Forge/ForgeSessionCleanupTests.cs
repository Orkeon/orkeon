using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Forge;

/// <summary>
/// What the catalog reads of a team's schedule (STUDIO-27): whether its forge.json records an
/// installation, and the sidecar forgetting the schedule once it is stopped. Real files in a
/// temporary workspace.
/// </summary>
public sealed class ForgeSessionCleanupTests : IDisposable
{
    private const string OriginalId = "6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f";

    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-studio-cleanup-" + Guid.NewGuid().ToString("N"));

    private string TeamsRoot => Path.Combine(_workspace, "teams");

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private string WriteTeam(string slug, string? id, string? scheduleBlock = null)
    {
        var team = Path.Combine(TeamsRoot, slug);
        Directory.CreateDirectory(team);
        var idPart = id is null ? "" : $"\"id\":\"{id}\",";
        var schedule = scheduleBlock is null ? "" : $",\"schedule\":{scheduleBlock}";
        File.WriteAllText(Path.Combine(team, ForgeSessionCatalog.TeamRecordFileName),
            $$"""{"v":1,{{idPart}}"slug":"{{slug}}"{{schedule}}}""");
        return team;
    }

    /// <summary>D-06: a team whose forge.json records an installed schedule has one to stop, whatever its sidecar says.</summary>
    [Fact]
    public void A_team_recording_an_installed_schedule_has_one_to_stop()
    {
        var installed = WriteTeam("installee", OriginalId,
            """{"expression":"daily@08:00","installed":{"expression":"daily@08:00","family":"windows","names":["Orkeon installee"]}}""");
        var declared = WriteTeam("declaree", null, """{"expression":"hourly"}""");
        TeamCatalog.SaveMetadata(declared, new StudioTeamMetadata { Name = "Déclarée", Schedule = "hourly" });
        var none = WriteTeam("aucune", null);

        Assert.True(ForgeSessionCatalog.ReadTeamRecord(installed).HasInstalledSchedule);
        Assert.Equal(Guid.Parse(OriginalId), ForgeSessionCatalog.ReadTeamRecord(installed).SessionId);
        Assert.True(TeamCatalog.Describe(installed).HasSchedule);
        Assert.False(TeamCatalog.Describe(declared).HasInstalledSchedule);
        Assert.True(TeamCatalog.Describe(declared).HasSchedule);
        Assert.False(TeamCatalog.Describe(none).HasSchedule);
        Assert.Equal(TeamForgeRecord.None, ForgeSessionCatalog.ReadTeamRecord(Path.Combine(TeamsRoot, "nulle-part")));
    }

    /// <summary>D-05: « Stop the schedule » forgets it in the sidecar, and only it.</summary>
    [Fact]
    public void Clearing_the_schedule_keeps_everything_else_the_sidecar_says()
    {
        var team = Path.Combine(TeamsRoot, "veille");
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
        {
            Name = "Veille",
            Description = "Chaque matin",
            Profile = "Local",
            Schedule = "daily@08:00",
            Mounts = ["./output:/output:rw"],
        });

        TeamCatalog.ClearSchedule(team);

        var metadata = TeamCatalog.Describe(team).Metadata!;
        Assert.Null(metadata.Schedule);
        Assert.Equal("Veille", metadata.Name);
        Assert.Equal("Chaque matin", metadata.Description);
        Assert.Equal("Local", metadata.Profile);
        Assert.Equal(["./output:/output:rw"], metadata.Mounts);

        // A folder without a sidecar stays without one.
        var bare = Path.Combine(TeamsRoot, "nue");
        Directory.CreateDirectory(bare);
        TeamCatalog.ClearSchedule(bare);
        Assert.False(File.Exists(Path.Combine(bare, StudioTeamMetadata.FileName)));
    }
}
