using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The my-teams screen's management gestures (30/08 mock, T-10/T-11): a delete that asks
/// first and asks once, a draft you can abandon for good, and a screen that says something
/// when it has nothing to list.
/// </summary>
public sealed class TeamsManagementTests
{
    private static ForgeSolutionSummary Draft(string slug, string directory) => new()
    {
        Slug = slug,
        Title = slug,
        State = "Brief",
        Status = "Active",
        Directory = directory,
    };

    private static string SeedTeam(string root, string slug, string name)
    {
        var directory = Path.Combine(root, slug);
        Directory.CreateDirectory(directory);
        TeamCatalog.SaveMetadata(directory, new StudioTeamMetadata { Name = name });
        return directory;
    }

    [Fact]
    public void Only_one_card_can_ask_the_question_at_a_time()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-teams-" + Guid.NewGuid().ToString("N"));
        try
        {
            SeedTeam(root, "veille", "Veille");
            SeedTeam(root, "synthese", "Synthèse");

            var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = root, LoadSessions = () => [] });
            Assert.Equal(2, teams.Teams.Count);

            teams.Teams[0].AskDeleteCommand.Execute(null);
            Assert.True(teams.Teams[0].IsConfirmingDelete);
            Assert.False(teams.Teams[1].IsConfirmingDelete);

            // Arming the second disarms the first: two open banners would ask the same
            // question twice, and the second answer would land on the wrong team.
            teams.Teams[1].AskDeleteCommand.Execute(null);
            Assert.False(teams.Teams[0].IsConfirmingDelete);
            Assert.True(teams.Teams[1].IsConfirmingDelete);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void An_abandoned_draft_can_be_discarded_from_the_list_that_keeps_offering_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-teams-" + Guid.NewGuid().ToString("N"));
        var sessionDirectory = Path.Combine(root, ".orkeon", "forge", "veille");
        try
        {
            Directory.CreateDirectory(sessionDirectory);
            var sessions = new List<ForgeSolutionSummary> { Draft("veille", sessionDirectory) };

            var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = root, LoadSessions = () => [.. sessions] });
            var session = Assert.Single(teams.InProgress);
            Assert.True(teams.HasInProgress);

            session.AskDeleteCommand.Execute(null);
            Assert.True(session.IsConfirmingDelete);

            // The list re-reads from the loader, so the loader has to forget it too —
            // exactly what ForgeSessionCatalog.List would do once the folder is gone.
            session.ConfirmDeleteCommand.Execute(null);
            sessions.Clear();
            teams.Refresh();

            Assert.False(Directory.Exists(sessionDirectory));
            Assert.False(teams.HasInProgress);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Discarding_a_draft_says_which_session_went_so_the_wizard_can_forget_it()
    {
        // Owner report of 2026-09-21: the wizard « Resume » had opened stayed on the session
        // after its row was deleted here. The screen deletes, the shell relays, and the
        // event names the directory the wizard compares against its own.
        var root = Path.Combine(Path.GetTempPath(), "orkeon-teams-" + Guid.NewGuid().ToString("N"));
        var sessionDirectory = Path.Combine(root, ".orkeon", "forge", "veille");
        try
        {
            Directory.CreateDirectory(sessionDirectory);
            var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = root, LoadSessions = () => [Draft("veille", sessionDirectory)] });
            var gone = new List<string>();
            teams.SessionDeleted += (_, e) => gone.Add(e.Session.Directory);

            Assert.Single(teams.InProgress).ConfirmDeleteCommand.Execute(null);

            Assert.Equal(sessionDirectory, Assert.Single(gone));
            Assert.False(Directory.Exists(sessionDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_discard_the_disk_refused_is_not_announced()
    {
        // Nothing was deleted, so nothing is relayed: the wizard keeps a session that is
        // still there, and the row stays — the list re-reads nothing either.
        var root = Path.Combine(Path.GetTempPath(), "orkeon-teams-" + Guid.NewGuid().ToString("N"));
        var missing = Path.Combine(root, ".orkeon", "forge", "veille");
        try
        {
            Directory.CreateDirectory(root);
            var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = root, LoadSessions = () => [Draft("veille", missing)] });
            var announced = false;
            teams.SessionDeleted += (_, _) => announced = true;

            Assert.Single(teams.InProgress).ConfirmDeleteCommand.Execute(null);

            Assert.False(announced);
            Assert.Single(teams.InProgress);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Deleting_a_session_directory_that_is_already_gone_is_refused_not_thrown()
    {
        var missing = Path.Combine(Path.GetTempPath(), "orkeon-absent-" + Guid.NewGuid().ToString("N"));

        Assert.False(ForgeSessionCatalog.Delete(missing));
    }

    /// <summary>A need pasted as a whole README: a title, a paragraph, a list, a code block.</summary>
    private const string LongNeed = """
        # Extraction des factures

        Chaque matin, les factures déposées dans `docs/` sont lues une à une, puis classées
        dans `sortie/` par mois et par fournisseur, avec un **contrôle** des doublons.

        ## Règles

        - une facture sans date est mise de côté
        - un doublon n'est jamais écrasé

        ```json
        { "doublon": false }
        ```
        """;

    [Fact]
    public void A_long_description_is_folded_and_unfolds_on_demand()
    {
        // STUDIO-16 (D-02/D-03): the card shows the derived summary folded and the whole
        // need unfolded; the toggle exists because the summary is not the whole text.
        var root = Path.Combine(Path.GetTempPath(), "orkeon-teams-" + Guid.NewGuid().ToString("N"));
        try
        {
            var directory = Path.Combine(root, "factures");
            Directory.CreateDirectory(directory);
            TeamCatalog.SaveMetadata(directory, new StudioTeamMetadata
            {
                Name = "# Extraction des factures\n\nun README entier dans le nom",
                Description = LongNeed,
            });

            var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = root, LoadSessions = () => [] });
            var card = Assert.Single(teams.Teams);

            // The name is one line, whatever the sidecar says (D-01).
            Assert.Equal("Extraction des factures", card.Name);

            Assert.True(card.HasDescription);
            Assert.True(card.DescriptionOverflows);
            Assert.False(card.IsDescriptionExpanded);
            Assert.Equal(card.DescriptionSummary, card.DescriptionDisplay);
            Assert.StartsWith("Chaque matin, les factures déposées dans docs/", card.DescriptionSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("```", card.DescriptionSummary!, StringComparison.Ordinal);

            var raised = new List<string>();
            card.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);
            card.ToggleDescriptionCommand.Execute(null);

            Assert.True(card.IsDescriptionExpanded);
            Assert.Equal(LongNeed, card.DescriptionDisplay);
            Assert.Contains(nameof(TeamCardViewModel.DescriptionDisplay), raised);

            card.ToggleDescriptionCommand.Execute(null);
            Assert.False(card.IsDescriptionExpanded);
            Assert.Equal(card.DescriptionSummary, card.DescriptionDisplay);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_short_description_offers_no_toggle()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-teams-" + Guid.NewGuid().ToString("N"));
        try
        {
            var directory = Path.Combine(root, "veille");
            Directory.CreateDirectory(directory);
            TeamCatalog.SaveMetadata(directory, new StudioTeamMetadata
            {
                Name = "Veille",
                Description = "Relit la presse du secteur chaque matin et résume ce qui a bougé.",
            });

            var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = root, LoadSessions = () => [] });
            var card = Assert.Single(teams.Teams);

            Assert.True(card.HasDescription);
            Assert.False(card.DescriptionOverflows);
            Assert.Equal(card.Description, card.DescriptionDisplay);

            // A folder without a sidecar has no description at all, and no toggle either.
            var bare = Path.Combine(root, "nu");
            Directory.CreateDirectory(bare);
            teams.Refresh();
            var bareCard = teams.Teams.Single(c => c.Slug == "nu");
            Assert.False(bareCard.HasDescription);
            Assert.False(bareCard.DescriptionOverflows);
            Assert.Null(bareCard.DescriptionDisplay);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void An_empty_screen_offers_both_ways_out()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-teams-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = root, LoadSessions = () => [] });

            Assert.True(teams.IsEmpty);
            Assert.False(teams.HasInProgress);

            var asked = 0;
            teams.CreateRequested += (_, _) => asked++;
            teams.ImportRequested += (_, _) => asked += 10;

            teams.CreateCommand.Execute(null);
            teams.ImportCommand.Execute(null);

            Assert.Equal(11, asked);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// STUDIO-25, D-04. The card never looks a session up: « Modify » is offered on a team whose
    /// forge.json names a session — the engine finds it — and on a team whose YAML crew can be
    /// read back — the engine rebuilds one — the tooltip saying which; only a team with neither
    /// keeps it disabled. The request carries the team and nothing else, whatever sessions
    /// exist: even one whose promotedTo names the folder decides nothing.
    /// </summary>
    [Fact]
    public void Modify_is_offered_on_a_team_naming_a_session_or_readable_back_and_carries_only_the_team()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-teams-" + Guid.NewGuid().ToString("N"));
        try
        {
            var promoted = SeedTeam(root, "promue", "Promue");
            File.WriteAllText(
                Path.Combine(promoted, ForgeSessionCatalog.TeamRecordFileName),
                """{"v":1,"id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","slug":"promue"}""");
            var orphan = SeedTeam(root, "orpheline", "Orpheline");
            Directory.CreateDirectory(Path.Combine(orphan, "crew"));
            File.WriteAllText(Path.Combine(orphan, "crew", "config.yaml"), "name: orpheline\n");
            var bare = SeedTeam(root, "nue", "Nue");

            // A session whose promotedTo names the bare folder: a path alone links nothing.
            var pointing = new ForgeSolutionSummary
            {
                Slug = "nue", State = "Promoted", Status = "Promoted",
                Directory = Path.Combine(root, "session"), PromotedTo = bare,
            };
            var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = root, LoadSessions = () => [pointing] });
            var requests = new List<TeamModifyEventArgs>();
            teams.ModifyRequested += (_, e) => requests.Add(e);
            var strings = Orkeon.Studio.Core.Localization.EnglishStudioStrings.Instance;

            var byName = teams.Teams.ToDictionary(card => card.Name, StringComparer.Ordinal);
            Assert.True(byName["Promue"].CanModify);
            Assert.Equal(strings[Orkeon.Studio.Core.Localization.StudioStringKeys.TeamsModifyTip], byName["Promue"].ModifyTooltip);
            Assert.True(byName["Orpheline"].CanModify);
            Assert.Equal(strings[Orkeon.Studio.Core.Localization.StudioStringKeys.TeamsModifyRebuild], byName["Orpheline"].ModifyTooltip);
            Assert.False(byName["Nue"].CanModify);
            Assert.False(byName["Nue"].ModifyCommand.CanExecute(null));
            Assert.Equal(strings[Orkeon.Studio.Core.Localization.StudioStringKeys.TeamsModifyNoSession], byName["Nue"].ModifyTooltip);

            byName["Promue"].ModifyCommand.Execute(null);
            byName["Orpheline"].ModifyCommand.Execute(null);
            byName["Nue"].ModifyCommand.Execute(null);
            Assert.Equal([promoted, orphan], requests.Select(request => request.Team.Path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// STUDIO-25, the owner's copy case end to end through the ViewModels: a team duplicated from
    /// its card carries its original's forge.json, id included, while the original's session
    /// still points at the original. « Modify » on the copy asks the engine about the COPY's
    /// folder — <c>forge reopen</c>, one child — and opens the session the engine gives the copy;
    /// the original's session is never resumed, nor even named on an argv.
    /// </summary>
    [Fact]
    public async Task Modify_on_a_duplicated_team_goes_through_forge_reopen_and_never_opens_the_originals_session()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-teams-" + Guid.NewGuid().ToString("N"));
        try
        {
            var teamsRoot = Path.Combine(root, "teams");
            var original = SeedTeam(teamsRoot, "veille", "Veille");
            Directory.CreateDirectory(Path.Combine(original, "crew"));
            await File.WriteAllTextAsync(
                Path.Combine(original, "crew", "config.yaml"), "name: veille\ngoal: g\n", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(original, ForgeSessionCatalog.TeamRecordFileName),
                """{"v":1,"id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","slug":"veille"}""",
                TestContext.Current.CancellationToken);
            var originalSession = Path.Combine(root, ".orkeon", "forge", "veille");
            Directory.CreateDirectory(originalSession);
            await File.WriteAllTextAsync(
                Path.Combine(originalSession, ForgeSessionCatalog.SessionFileName),
                $$"""{"v":1,"id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","slug":"veille","format":"yaml","state":"Promoted","status":"Promoted","promotedTo":{{System.Text.Json.JsonSerializer.Serialize(original)}}}""",
                TestContext.Current.CancellationToken);

            var copy = TeamCatalog.Duplicate(original, DateTimeOffset.UnixEpoch);
            Assert.NotNull(copy);
            var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = teamsRoot, WorkspaceDirectory = root });
            var copyCard = teams.Teams.Single(card => card.Summary.Path == copy);
            Assert.True(copyCard.CanModify);

            // The engine's answer for a copy: a session of its own, named after the copy's folder,
            // rebuilt from its crew and parked at the dry pause, its id written into the copy's
            // forge.json.
            var (wizard, processes, _) = CreateTeamWizardTests.Build(teamsRoot: teamsRoot, workspace: root);
            Assert.Equal("veille-copy", Path.GetFileName(copy));
            var ownSession = Path.Combine(root, ".orkeon", "forge", "veille-copy");
            processes.OutputToEmit.AddRange(
            [
                Orkeon.Studio.Core.Process.ProcessOutputLine.Now(Orkeon.Studio.Core.Process.ProcessOutputChannel.StandardOutput,
                    $$"""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille-copy","id":"0b9e8d7c-6a5f-4e3d-8c2b-1a0f9e8d7c6b","dir":{{System.Text.Json.JsonSerializer.Serialize(ownSession)}},"format":"yaml","resumed":false}"""),
                Orkeon.Studio.Core.Process.ProcessOutputLine.Now(Orkeon.Studio.Core.Process.ProcessOutputChannel.StandardOutput,
                    $$"""{"v":2,"seq":2,"ts":"t","kind":"team.reopened","slug":"veille-copy","dir":{{System.Text.Json.JsonSerializer.Serialize(ownSession)}},"path":{{System.Text.Json.JsonSerializer.Serialize(copy)}},"state":"test","rebuilt":true,"brief":"derived"}"""),
                Orkeon.Studio.Core.Process.ProcessOutputLine.Now(Orkeon.Studio.Core.Process.ProcessOutputChannel.StandardOutput,
                    """{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
            ]);
            Task? reopening = null;
            teams.ModifyRequested += (_, e) => reopening = wizard.ReopenTeamAsync(e.Team);

            copyCard.ModifyCommand.Execute(null);
            Assert.NotNull(reopening);
            await reopening;

            Assert.Equal(["forge", "reopen", copy!, "--events", "jsonl"], Assert.Single(processes.Requests).Arguments);
            Assert.Equal("veille-copy", wizard.SessionSlug);
            Assert.Equal(copy, wizard.ReopenedTeamPath);
            Assert.DoesNotContain(processes.Requests, request => request.Arguments.Contains("veille"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}

/// <summary>
/// The draft the navigation announces (30/08 mock, T-09): leaving the wizard must never be
/// the same as losing what was typed in it.
/// </summary>
public sealed class NavigationDraftTests
{
    private static Orkeon.Studio.Core.Process.ProcessOutputLine Out(string json) =>
        Orkeon.Studio.Core.Process.ProcessOutputLine.Now(
            Orkeon.Studio.Core.Process.ProcessOutputChannel.StandardOutput, json);

    private static string Assistant(string text) =>
        $$"""{"v":2,"seq":3,"ts":"t","kind":"assistant.message","text":"{{text}}"}""";

    private static CreateTeamViewModel Wizard(
        bool withAssistant = false, Doubles.FakeProcessLauncher? processes = null)
    {
        var document = Orkeon.Studio.Core.Configuration.AppSettingsDocument.CreateEmpty();
        var llm = new Orkeon.Studio.Wpf.ViewModels.Config.LlmSectionViewModel(
            () => document, () => { }, new Doubles.FakeLlmEndpointProbe());
        var profiles = new Orkeon.Studio.Wpf.ViewModels.Config.ModelProfilesViewModel(
            new Orkeon.Studio.Core.Profiles.InMemoryModelProfileStore(), llm,
            probe: new Doubles.FakeLlmEndpointProbe());
        if (withAssistant)
        {
            profiles.CommitEdit(
                new Orkeon.Studio.Core.Profiles.ModelProfile
                {
                    Name = "Local",
                    Provider = "Ollama",
                    Model = "qwen2.5:14b",
                    BaseUrl = "http://localhost:11434/v1",
                },
                previousName: null);
            profiles.StudioProfileName = "Local";
        }

        return new CreateTeamViewModel(
            profiles,
            new CreateTeamDependencies
            {
                Client = new Orkeon.Studio.Core.Forge.ForgeClient(
                    processes ?? new Doubles.FakeProcessLauncher(),
                    new Orkeon.Studio.Core.Process.OrkeonBinaryLocator(
                        Doubles.FakeExecutableProbe.WithOrkeonInstalled())),
                WorkspaceDirectory = "/ws",
                TeamsRoot = "/teams",
            });
    }

    [Fact]
    public void A_blank_wizard_has_nothing_to_announce()
    {
        var vm = Wizard();

        Assert.False(vm.HasDraft);
        Assert.False(vm.IsAssistantWaiting);
    }

    [Fact]
    public void A_single_typed_word_is_already_a_draft()
    {
        var vm = Wizard();

        vm.Need = "une veille documentaire";

        Assert.True(vm.HasDraft);
        Assert.Equal("1/4", vm.DraftStepShort);

        // The line is assembled from a per-culture pattern, never concatenated: the step
        // number, the total, and the step's own name.
        Assert.Contains("1", vm.DraftLine, StringComparison.Ordinal);
        Assert.Contains("4", vm.DraftLine, StringComparison.Ordinal);
        Assert.Contains("Describe", vm.DraftLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_draft_notice_changes_register_when_the_assistant_is_the_one_waiting()
    {
        var processes = new Doubles.FakeProcessLauncher();

        var vm = Wizard(withAssistant: true, processes: processes);
        vm.Need = "une veille documentaire";
        vm.FrequencyChoices[1].SelectCommand.Execute(null);
        vm.SourceChoices[0].SelectCommand.Execute(null);
        vm.OutputChoices[0].SelectCommand.Execute(null);

        var resting = vm.DraftTitle;
        Assert.True(vm.HasDraft);
        Assert.False(vm.IsAssistantWaiting);

        bool asking = false, waiting = false;
        string title = "", line = "";
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(Assistant("Quel dossier faut-il lire ?")));

            // Read while the child is alive: the brief stage is blocking on stdin there.
            asking = vm.Chat.IsAsking;
            waiting = vm.IsAssistantWaiting;
            title = vm.DraftTitle;
            line = vm.DraftLine;
        };
        await vm.ComposeCommand.ExecuteAsync();

        // The draft is no longer merely "in progress": someone is waiting on the user,
        // and the nav has to say which of the two it is. The question stands in the
        // conversation — the only place it is shown now.
        Assert.True(asking);
        Assert.True(waiting);
        Assert.NotEqual(resting, title);
        Assert.Contains("resume the conversation", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_input_of_the_projection_raises_it()
    {
        var vm = Wizard();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        vm.Need = "une veille documentaire";

        Assert.Contains(nameof(vm.HasDraft), raised);
        Assert.Contains(nameof(vm.DraftLine), raised);
        Assert.Contains(nameof(vm.DraftStepShort), raised);
    }

    [Fact]
    public async Task A_started_conversation_is_a_draft_even_before_the_form_is_filled()
    {
        var processes = new Doubles.FakeProcessLauncher();
        var vm = Wizard(withAssistant: true, processes);
        vm.Need = "une veille documentaire";
        vm.FrequencyChoices[1].SelectCommand.Execute(null);
        vm.SourceChoices[0].SelectCommand.Execute(null);
        vm.OutputChoices[0].SelectCommand.Execute(null);

        // The engine asks its first question and then blocks — the state the nav describes,
        // and it only exists while the child is alive.
        bool started = false, draft = false, waiting = false;
        string line = "";
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(Assistant("Which folder?")));
            started = vm.Chat.IsStarted;
            draft = vm.HasDraft;
            waiting = vm.IsAssistantWaiting;
            line = vm.DraftLine;
        };
        await vm.ComposeCommand.ExecuteAsync();

        Assert.True(started);
        Assert.True(draft);

        // Someone is waiting on the user, and the nav has to say which — the count would
        // only say how much has been said, which is not what to do next.
        Assert.True(waiting);
        Assert.Contains("resume the conversation", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Once_nobody_is_waiting_the_line_counts_the_conversation_instead()
    {
        var processes = new Doubles.FakeProcessLauncher();
        var vm = Wizard(withAssistant: true, processes);
        vm.Need = "une veille documentaire";
        vm.FrequencyChoices[1].SelectCommand.Execute(null);
        vm.SourceChoices[0].SelectCommand.Execute(null);
        vm.OutputChoices[0].SelectCommand.Execute(null);

        // Asked and answered: the conversation has content, but nobody is owed a reply.
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(Assistant("Which folder?")));
            vm.Chat.Draft = "Documents";
            vm.Chat.SendCommand.Execute(null);
        };
        await vm.ComposeCommand.ExecuteAsync();

        Assert.NotEmpty(vm.Chat.Turns);
        Assert.False(vm.Chat.IsAsking);
        Assert.Contains("message", vm.DraftLine, StringComparison.OrdinalIgnoreCase);
    }

}
