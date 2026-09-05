using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The team cards' history line and mounts (remediation v2, F-02): each card names its
/// latest run from the same store the Historique screen reads, and carries the sidecar's
/// mount strings; the launcher lays those mounts on the run itself, ahead of the
/// per-launch entries — the chips and the command cannot disagree.
/// </summary>
public sealed class TeamsLastRunTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-lastrun-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string NewTeam(string slug, params string[] mounts)
    {
        var team = Path.Combine(_root, slug);
        Directory.CreateDirectory(team);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
        {
            Name = slug,
            Mounts = mounts.Length > 0 ? mounts : null,
        });
        return team;
    }

    private static LaunchHistoryEntry Entry(string target, DateTimeOffset startedAt, RunOutcome outcome) => new()
    {
        Target = target,
        StartedAt = startedAt,
        Outcome = outcome,
    };

    [Fact]
    public async Task The_card_names_its_latest_run_and_survives_a_refresh()
    {
        var team = NewTeam("veille");
        var older = new DateTimeOffset(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
        var store = new FakeLaunchHistoryStore
        {
            History = LaunchHistory.Empty
                .Add(Entry(team, older, RunOutcome.RuntimeError))
                // The separator spelling must not split the match.
                .Add(Entry(team + Path.DirectorySeparatorChar, newer, RunOutcome.Success)),
        };

        var teams = new TeamsViewModel(
            new TeamsDependencies { TeamsRoot = _root, LoadSessions = () => [], HistoryStore = store });
        await teams.LoadLastRunsAsync(TestContext.Current.CancellationToken);

        var card = Assert.Single(teams.Teams);
        Assert.Equal(newer, card.LastRun);
        Assert.Equal(RunOutcome.Success, card.LastOutcome);

        // Refresh() rebuilds the cards synchronously and re-applies the cached map.
        teams.Refresh();
        Assert.Equal(newer, Assert.Single(teams.Teams).LastRun);
    }

    [Fact]
    public async Task A_team_that_never_ran_stays_silent()
    {
        NewTeam("muette");
        var teams = new TeamsViewModel(
            new TeamsDependencies { TeamsRoot = _root, LoadSessions = () => [], HistoryStore = new FakeLaunchHistoryStore() });
        await teams.LoadLastRunsAsync(TestContext.Current.CancellationToken);

        var card = Assert.Single(teams.Teams);
        Assert.Null(card.LastRun);
        Assert.Null(card.LastOutcome);
    }

    [Fact]
    public void The_card_carries_the_sidecar_mounts()
    {
        NewTeam("montee", "C:/docs:/docs:ro", "C:/out:/output:rw");
        var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = _root, LoadSessions = () => [] });

        var card = Assert.Single(teams.Teams);
        Assert.True(card.HasMounts);
        Assert.Equal(["C:/docs:/docs:ro", "C:/out:/output:rw"], card.Mounts);
    }

    [Fact]
    public void Team_mounts_ride_the_launch_ahead_of_the_per_launch_entries()
    {
        var mounts = new LaunchMountsViewModel();
        mounts.SetTeamMounts(["C:/docs:/docs:ro"]);
        mounts.LaunchMounts.Load(["C:/extra:/extra:rw"]);

        Assert.Equal(["C:/docs:/docs:ro", "C:/extra:/extra:rw"], mounts.ToMountArguments());

        // A non-team target clears them again.
        mounts.SetTeamMounts([]);
        Assert.Equal(["C:/extra:/extra:rw"], mounts.ToMountArguments());
    }
}
