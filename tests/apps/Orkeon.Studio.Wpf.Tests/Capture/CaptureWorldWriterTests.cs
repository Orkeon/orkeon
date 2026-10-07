using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Email;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Core.Validation;
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

    /// <summary>
    /// STUDIO-67: the E-mail tab photographs a preset account and a custom one, read back through
    /// the production document — and both are accounts the run would use, so no shot of the
    /// campaign shows a warning the seed did not mean.
    /// </summary>
    [Fact]
    public void The_seeded_settings_declare_a_gmail_account_and_a_custom_one_the_run_would_use()
    {
        var document = AppSettingsDocument.Parse(File.ReadAllText(_worlds.Seeded.SettingsPath));

        Assert.Equal(2, document.Email.AccountNames.Count);
        var accounts = document.Email.AccountNames.Select(name => document.Email.GetAccount(name)!).ToList();
        Assert.Contains(accounts, account => account.Provider == "Gmail");
        Assert.Contains(accounts, account => account.Provider == "Custom" && account.IncomingHost is not null && account.OutgoingHost is not null);
        Assert.All(document.Email.AccountNames, name => Assert.Empty(EmailAccountRules.Check(document, name)));
        // The folders of the seed still stand beside the accounts, under the same root key.
        Assert.NotEmpty(document.Mounts.RawEntries);
        Assert.Empty(AppSettingsDocument.Parse(File.ReadAllText(_worlds.Pristine.SettingsPath)).Email.AccountNames);
    }

    /// <summary>
    /// STUDIO-69: the E-mail tab asks the CLI what the engine makes of each account. In the
    /// campaign that answer is a script, read back here through the production client: it names
    /// exactly the seeded accounts, one ready and one that lacks its password — the two states
    /// worth a pixel — and the ready one is the one whose password the seeded machine keeps.
    /// </summary>
    [Fact]
    public async Task The_seeded_cli_answers_the_email_accounts_listing_with_one_account_ready_and_one_not()
    {
        var document = AppSettingsDocument.Parse(
            await File.ReadAllTextAsync(_worlds.Seeded.SettingsPath, TestContext.Current.CancellationToken));

        var result = await new EmailCliClient(_worlds.Seeded.Runner)
            .ListAsync(_worlds.Seeded.SettingsPath, TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.Equal(
            document.Email.AccountNames.Order(StringComparer.Ordinal),
            result.Accounts.Select(account => account.Name).Order(StringComparer.Ordinal));
        Assert.All(result.Accounts, account => Assert.False(account.IsSetAside));

        var ready = Assert.Single(result.Accounts, account => account.Ready);
        var waiting = Assert.Single(result.Accounts, account => !account.Ready);
        Assert.NotNull(_worlds.Seeded.KeyStore.Peek(document.Email.GetAccount(ready.Name)!.PasswordEnvVar!));
        var missing = document.Email.GetAccount(waiting.Name)!.PasswordEnvVar!;
        Assert.Null(_worlds.Seeded.KeyStore.Peek(missing));
        Assert.Contains(missing, waiting.Problem, StringComparison.Ordinal);

        // Nothing was spawned: the launch is in the scripted CLI's own record, with its file named.
        Assert.Contains(_worlds.Seeded.Cli.Requests, request =>
            request.Arguments is ["email", "accounts", "--json", "--settings", var path] && path == _worlds.Seeded.SettingsPath);
    }

    [Fact]
    public void The_seeded_teams_come_back_through_the_real_catalogue()
    {
        var teams = TeamCatalog.List(_worlds.Seeded.TeamsRoot);

        // Five active teams, and one archived (STUDIO-32): the Archives view has something to show.
        Assert.Equal(5, teams.Count);
        Assert.Contains(teams, team => team.Name == "Veille concurrentielle");
        var archived = Assert.Single(TeamCatalog.List(_worlds.Seeded.TeamsRoot, TeamListFilter.Archived));
        Assert.Equal(StudioFixture.ArchivedTeamSlug, archived.Slug);
        Assert.Equal(6, TeamCatalog.List(_worlds.Seeded.TeamsRoot, TeamListFilter.All).Count);

        // The agent count is read off the crew folder, not stored: a team folder that the real
        // detector does not recognise would report null here.
        var veille = teams.Single(team => team.Slug == "veille-concurrentielle");
        Assert.Equal(4, veille.AgentCount);

        // The pasted-README team (STUDIO-16) is written RAW — a multi-line name, a forty-line
        // need — so the shots prove the display bounds it; the catalogue derives the summary.
        var pasted = teams.Single(team => team.Slug == StudioFixture.PastedReadmeTeamSlug);
        Assert.Contains('\n', pasted.Name);
        Assert.True(pasted.Description!.Split('\n').Length >= 40, "the seeded need must be forty lines long");
        Assert.DoesNotContain('\n', TeamCatalog.NormalizeName(pasted.Name));
        Assert.StartsWith("Chaque matin, les factures", pasted.Summary, StringComparison.Ordinal);
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
    /// «Modifier» on a team card is gated on the session id the team's <c>forge.json</c> carries,
    /// and the wizard asks the engine which session that is (<c>forge reopen</c>, STUDIO-25).
    /// Seeding both halves of the link — the record in the team, the scripted reopen naming the
    /// promoted session — is what makes step 4 of the wizard reachable at all, so it is worth an
    /// assertion of its own rather than a discovery on Windows.
    /// </summary>
    [Fact]
    public async Task The_adopted_team_names_its_own_session_and_the_engine_reopens_that_one()
    {
        var team = _worlds.Seeded.TeamDirectory("veille-concurrentielle");
        var id = ForgeSessionCatalog.ReadTeamSessionId(team);
        Assert.NotNull(id);
        var session = ForgeSessionCatalog.FindById(_worlds.Seeded.ForgeWorkspace, id.Value);
        Assert.NotNull(session);
        Assert.Equal(StudioFixture.PromotedSessionSlug, session.Slug);
        Assert.Equal(TeamSessionLinkKind.Linked, TeamSessionLink.Resolve(team, id, session.Promotion, ForgeSessionCatalog.ReadTeamSessionId));

        var lines = new List<string>();
        await _worlds.Seeded.Cli.RunAsync(
            new ProcessLaunchRequest { FileName = "orkeon", Arguments = ["forge", "reopen", team, "--events", "jsonl"] },
            line => lines.Add(line.Text),
            TestContext.Current.CancellationToken);
        var model = new ForgeSessionModel();
        foreach (var line in lines)
        {
            Assert.True(Orkeon.Studio.Core.Events.OrkeonEventParser.TryParse(line, out var parsed), line);
            model.Feed(parsed!);
        }

        Assert.Equal(StudioFixture.PromotedSessionSlug, model.Slug);
        Assert.Equal(id, model.SessionId);
        Assert.Equal(session.Directory, model.Directory);
        Assert.False(model.ReopenedRebuilt);
    }

    [Fact]
    public async Task The_seeded_history_and_profiles_come_back_through_their_real_stores()
    {
        var history = await _worlds.Seeded.HistoryStore.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(7, history.Entries.Count);

        var profiles = (await _worlds.Seeded.ProfileStore.LoadAsync(TestContext.Current.CancellationToken)).Set;
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
