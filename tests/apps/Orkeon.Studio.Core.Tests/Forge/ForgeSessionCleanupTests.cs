using System.Text.Json;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Forge;

/// <summary>
/// What a clean deletion reads (STUDIO-27, T2): the session rule R links to a team — never its
/// original's for a copy — the orphan sessions whose team folder is gone, and the schedule a team
/// has to have stopped before it goes. Real files in a temporary workspace; nothing is deleted here.
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

    private string WriteSession(string slug, string? id, string status, string? promotedTo)
    {
        var directory = Path.Combine(_workspace, ".orkeon", "forge", slug);
        Directory.CreateDirectory(directory);
        var idPart = id is null ? "" : $"\"id\":\"{id}\",";
        var promoted = promotedTo is null ? "" : $",\"promotedTo\":{JsonSerializer.Serialize(promotedTo)}";
        File.WriteAllText(Path.Combine(directory, ForgeSessionCatalog.SessionFileName),
            $$"""{"v":1,{{idPart}}"slug":"{{slug}}","title":"{{slug}}","format":"yaml","state":"{{status}}","status":"{{status}}","updatedAt":"2026-09-24T08:00:00Z"{{promoted}}}""");
        return directory;
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

    /// <summary>D-07, rule R: the original is linked to its session — deleting it may take the session with it.</summary>
    [Fact]
    public void The_original_is_linked_to_its_session()
    {
        var team = WriteTeam("veille", OriginalId);
        var session = WriteSession("veille", OriginalId, "Promoted", team);

        Assert.Equal(session, ForgeSessionCatalog.LinkedSession(_workspace, team)?.Directory);
    }

    /// <summary>D-07, rule R: a copy carries its original's id while the original is still there — linked to nothing.</summary>
    [Fact]
    public void A_copy_is_linked_to_no_session_so_deleting_it_never_takes_its_originals()
    {
        var original = WriteTeam("veille", OriginalId);
        WriteSession("veille", OriginalId, "Promoted", original);
        var copy = WriteTeam("veille-copy", OriginalId);

        Assert.Null(ForgeSessionCatalog.LinkedSession(_workspace, copy));
        Assert.NotNull(ForgeSessionCatalog.LinkedSession(_workspace, original));
    }

    /// <summary>Rule R, case 3: the original moved since its promotion is still linked; a folder without an id is linked to nothing.</summary>
    [Fact]
    public void A_moved_original_stays_linked_and_a_folder_without_an_id_is_linked_to_nothing()
    {
        var moved = WriteTeam("veille-renommee", OriginalId);
        WriteSession("veille", OriginalId, "Promoted", Path.Combine(TeamsRoot, "veille"));
        var bare = WriteTeam("nue", id: null);

        Assert.NotNull(ForgeSessionCatalog.LinkedSession(_workspace, moved));
        Assert.Null(ForgeSessionCatalog.LinkedSession(_workspace, bare));
    }

    /// <summary>
    /// D-08: an orphan is an adopted session whose team folder is gone. A session still underway
    /// is listed elsewhere; one whose team is there is none; one whose team was renamed inside the
    /// teams root is found by its id and is none either.
    /// </summary>
    [Fact]
    public void Orphans_are_adopted_sessions_whose_team_folder_is_gone()
    {
        var gone = WriteSession("disparue", Guid.NewGuid().ToString(), "Promoted", Path.Combine(TeamsRoot, "disparue"));
        var present = WriteTeam("presente", "11111111-2222-3333-4444-555555555555");
        WriteSession("presente", "11111111-2222-3333-4444-555555555555", "Promoted", present);
        WriteTeam("renommee-depuis", OriginalId);
        WriteSession("renommee", OriginalId, "Promoted", Path.Combine(TeamsRoot, "renommee"));
        WriteSession("en-cours", Guid.NewGuid().ToString(), "Active", Path.Combine(TeamsRoot, "ailleurs"));

        var orphans = ForgeSessionCatalog.FindOrphans(_workspace, TeamsRoot);

        Assert.Equal(gone, Assert.Single(orphans).Directory);
        // Without the teams root, the renamed team cannot be told from a deleted one: listed —
        // and only listed. Nothing is deleted without the user.
        Assert.Equal(2, ForgeSessionCatalog.FindOrphans(_workspace).Count);
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
