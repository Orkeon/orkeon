using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Capture.Fixtures;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

namespace Orkeon.Studio.Wpf.Tests.Capture;

/// <summary>
/// The seeded machine, read back through the production loaders — which is the whole point of
/// seeding disk rather than doubling the stores: what the campaign photographs is what
/// <c>TeamCatalog</c>, <c>ForgeSessionCatalog</c> and the file-backed stores actually produce.
/// </summary>
public sealed class CaptureWorldWriterTests : IAsyncLifetime
{
    private CaptureWorlds _worlds = null!;

    public async ValueTask InitializeAsync() => _worlds = await CaptureWorlds.CreateAsync();

    public ValueTask DisposeAsync()
    {
        _worlds.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The sharpest requirement of the whole design: a screenshot campaign must not be able to
    /// touch the operator's own teams, history or settings. The worlds live under the temp
    /// directory and nowhere near any of the places Studio keeps real state.
    /// </summary>
    [Fact]
    public void The_worlds_live_under_temp_and_nowhere_near_the_operators_own_state()
    {
        Assert.StartsWith(
            Path.GetFullPath(Path.GetTempPath()),
            Path.GetFullPath(_worlds.Root),
            StringComparison.OrdinalIgnoreCase);

        foreach (var folder in new[]
        {
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.UserProfile,
        })
        {
            var real = Environment.GetFolderPath(folder);
            if (real.Length == 0)
                continue;

            Assert.DoesNotContain(real, _worlds.Root, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_seeded_teams_come_back_through_the_real_catalogue()
    {
        var teams = TeamCatalog.List(_worlds.Seeded.TeamsRoot);

        Assert.Equal(3, teams.Count);
        Assert.Contains(teams, team => team.Name == "Veille concurrentielle");

        // The agent count is read off the crew folder, not stored: a team folder that the real
        // detector does not recognise would report null here.
        var veille = teams.Single(team => team.Slug == "veille-concurrentielle");
        Assert.Equal(4, veille.AgentCount);
    }

    [Fact]
    public void The_seeded_sessions_come_back_through_the_real_catalogue()
    {
        var sessions = ForgeSessionCatalog.List(_worlds.Seeded.ForgeWorkspace);

        Assert.Equal(4, sessions.Count);
        Assert.Contains(sessions, session => session.Slug == StudioFixture.DryPauseSessionSlug);
        Assert.Contains(sessions, session => session.Slug == StudioFixture.FailingSessionSlug);

        // Three in progress and one adopted: the two faces My teams shows side by side.
        Assert.Equal(3, sessions.Count(session => session.CanResume));
        Assert.Single(sessions, session => session.CanRelaunch);
    }

    /// <summary>
    /// «Modifier» on a team card is gated on a session pointing back at the folder through
    /// <c>promotedTo</c>. Seeding that link is what makes step 4 of the wizard reachable at all,
    /// so it is worth an assertion of its own rather than a discovery on Windows.
    /// </summary>
    [Fact]
    public void The_adopted_team_is_found_from_its_own_session()
    {
        var session = ForgeSessionCatalog.FindByPromotedTo(
            _worlds.Seeded.ForgeWorkspace,
            _worlds.Seeded.TeamDirectory("veille-concurrentielle"));

        Assert.NotNull(session);
        Assert.Equal(StudioFixture.PromotedSessionSlug, session.Slug);
    }

    [Fact]
    public async Task The_seeded_history_and_profiles_come_back_through_their_real_stores()
    {
        var history = await _worlds.Seeded.HistoryStore.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(7, history.Entries.Count);

        var profiles = await _worlds.Seeded.ProfileStore.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, profiles.Profiles.Count);
        Assert.NotNull(profiles.Studio);
    }

    /// <summary>
    /// The unreadable mount row has to be a real verdict. One declared folder is deliberately
    /// never created, so the settings screen paints it red because the disk says so.
    /// </summary>
    [Fact]
    public void One_declared_folder_is_genuinely_missing_from_the_disk()
    {
        Assert.True(Directory.Exists(Path.Combine(_worlds.Seeded.DataDirectory, "docs")));
        Assert.False(Directory.Exists(Path.Combine(_worlds.Seeded.DataDirectory, "disparu")));
    }

    [Fact]
    public void The_pristine_world_is_empty_and_has_no_cli()
    {
        Assert.Empty(TeamCatalog.List(_worlds.Pristine.TeamsRoot));
        Assert.Empty(ForgeSessionCatalog.List(_worlds.Pristine.ForgeWorkspace));
        Assert.Null(_worlds.Pristine.Locator.Locate().Path);
    }

    [Fact]
    public void The_seeded_world_locates_its_cli()
    {
        Assert.True(_worlds.Seeded.Locator.Locate().Found);
    }
}
