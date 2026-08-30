using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.ViewModels.Teams;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The create-a-team wizard over a scripted engine child: the stream in, the steps
/// out — no real binary, no LLM, the inline dispatcher. The protocol reading itself is
/// pinned in <c>Orkeon.Studio.Core.Tests</c>; these tests pin the wizard's behaviour.
/// </summary>
public class CreateTeamWizardTests
{
    private static ProcessOutputLine Out(string json) =>
        ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    private static (CreateTeamViewModel Vm, FakeProcessLauncher Processes, ModelProfilesViewModel Profiles) Build(
        string? teamsRoot = null,
        bool withAssistant = true,
        Func<IReadOnlyList<string>>? declaredMounts = null)
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { }, new FakeLlmEndpointProbe());
        var profiles = new ModelProfilesViewModel(new InMemoryModelProfileStore(), llm, probe: new FakeLlmEndpointProbe());
        if (withAssistant)
        {
            profiles.CommitEdit(
                new ModelProfile { Name = "Local", Provider = "Ollama", Model = "qwen2.5:14b", BaseUrl = "http://localhost:11434/v1" },
                previousName: null);
            profiles.StudioProfileName = "Local";
        }

        var processes = new FakeProcessLauncher();
        var client = new ForgeClient(
            processes,
            new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled()));
        var vm = new CreateTeamViewModel(
            profiles,
            client,
            dispatcher: null,
            strings: null,
            workspaceDirectory: "/ws",
            teamsRoot: teamsRoot ?? "/teams",
            declaredMounts: declaredMounts);
        return (vm, processes, profiles);
    }

    private static void FillStepOne(CreateTeamViewModel vm)
    {
        vm.Need = "une veille documentaire";
        vm.FrequencyChoices[1].SelectCommand.Execute(null);   // every day
        vm.SourceChoices[0].SelectCommand.Execute(null);      // a folder
        vm.OutputChoices[0].SelectCommand.Execute(null);      // a document
    }

    [Fact]
    public void Without_an_assistant_profile_the_wizard_is_gated()
    {
        var (vm, _, _) = Build(withAssistant: false);
        FillStepOne(vm);

        Assert.True(vm.NeedsAssistant);
        Assert.False(vm.CanCompose);
        Assert.False(vm.ComposeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Composing_sends_the_brief_under_the_assistants_profile_and_follows_the_milestones()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"stage.entered","stage":"blueprint","iteration":1}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille","goal":"Veille documentaire"},"agents":[{"key":"scanner","role":"Scanner","tools":["fs.read"]},{"key":"redacteur","role":"Rédacteur","tools":["llm.complete"]}],"tasks":[{"key":"scan","description":"Parcourt le dossier","agent":"scanner"},{"key":"resume","description":"Écrit le résumé","agent":"redacteur"}],"rationale":"Trois agents."},"iteration":1}"""),
            Out("""{"v":2,"seq":4,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);

        FillStepOne(vm);
        Assert.Equal("Everything is there — I can compose the team.", vm.Step1Hint);
        await vm.ComposeCommand.ExecuteAsync();

        // The brief folds the precisions into the engine's opening turn…
        var need = processes.LastRequest!.Arguments[1];
        Assert.StartsWith("une veille documentaire", need, StringComparison.Ordinal);
        Assert.Contains("Every day", need, StringComparison.Ordinal);
        // …and the child runs under the assistant's model, by environment, never by file.
        Assert.Equal("qwen2.5:14b", processes.LastRequest.Environment["ORKEON_Llm__Model"]);

        // The milestone advanced the stepper to Composer, with the proposal projected.
        Assert.Equal(2, vm.Step);
        Assert.Equal(2, vm.MaxStep);
        Assert.Equal(["Scanner", "Rédacteur"], vm.Agents.Select(a => a.Name));
        Assert.Equal("Trois agents.", vm.Rationale);
        Assert.True(vm.HasTools);
    }

    [Fact]
    public async Task Composing_pauses_at_the_dry_boundary_and_the_trial_is_an_explicit_resume()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":[]}],"tasks":[{"key":"t","description":"d","agent":"a"}],"rationale":"r"},"iteration":1}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);

        FillStepOne(vm);
        await vm.ComposeCommand.ExecuteAsync();

        // Composing asked the engine for the dry boundary: generate, validate, stop.
        Assert.Contains("--dry", processes.Requests[0].Arguments);
        // The pause is the Composer step, and the trial waits for the user's click.
        Assert.True(vm.CanTryTeam);
        Assert.True(vm.TryTeamCommand.CanExecute(null));

        processes.OutputToEmit.Clear();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":true}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"stage.entered","stage":"test","iteration":1}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"run.started","run":1,"target":"crew"}"""),
        ]);
        await vm.TryTeamCommand.ExecuteAsync();

        // The try-the-team action resumes WITHOUT dry: the engine picks up at the trial.
        Assert.Equal(["forge", "resume", "veille", "--events", "jsonl"], processes.Requests[1].Arguments);
        Assert.Equal(3, vm.MaxStep);   // the run advanced the stepper to Essayer
    }

    [Fact]
    public async Task At_the_dry_pause_an_agent_edit_relaunches_the_engine_with_the_amended_blueprint()
    {
        // The user's own scenario (W-10): the Composer shows the proposed team, the
        // engine is off at the dry pause — the Modify action must not be greyed out.
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille"},"agents":[{"key":"a","role":"Scanner","tools":["file_read"]}],"tasks":[{"key":"t","description":"d","agent":"a"}],"rationale":"r"},"iteration":1}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);
        FillStepOne(vm);
        await vm.ComposeCommand.ExecuteAsync();

        Assert.True(vm.CanEditAgents);
        Assert.True(vm.Agents[0].EditCommand.CanExecute(null));

        // The apply is a `resume --edit --dry`: the amended blueprint rides the launch's
        // stdin, the engine re-renders deterministically and pauses again — the Composer
        // repaints with the amended team, still ready to try.
        processes.OutputToEmit.Clear();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":true}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille"},"agents":[{"key":"a","role":"Chercheur","tools":["file_read"]}],"tasks":[{"key":"t","description":"d","agent":"a"}],"rationale":"r"},"iteration":1}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);
        vm.Agents[0].EditCommand.Execute(null);
        Assert.True(vm.AgentEditor.IsOpen);
        vm.AgentEditor.Name = "Chercheur";
        vm.AgentEditor.SaveCommand.Execute(null);

        Assert.Equal(
            ["forge", "resume", "veille", "--events", "jsonl", "--dry", "--edit"],
            processes.Requests[1].Arguments);
        var line = Assert.Single(processes.InputLines);
        Assert.StartsWith("""{"kind":"blueprint.edited","blueprint":""", line, StringComparison.Ordinal);
        Assert.Contains("Chercheur", line, StringComparison.Ordinal);
        Assert.Equal("Chercheur", Assert.Single(vm.Agents).Name);
        Assert.True(vm.CanTryTeam);
        Assert.True(vm.CanEditAgents);
    }

    [Fact]
    public async Task A_cold_resume_of_the_dry_pause_restores_the_identity_and_the_trial_button()
    {
        // Regression (W-09 exploration): the hydrate-only branch never set the model's
        // Slug — the try-the-team and save actions stayed dead on a cold resume.
        var root = Path.Combine(Path.GetTempPath(), "orkeon-wiz-resume-" + Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(root, ".orkeon", "forge", "veille");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "session.json"),
            """{"v":1,"slug":"veille","title":"Veille","format":"yaml","state":"Test","status":"Active"}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "blueprint.json"),
            """{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":[]}],"tasks":[{"key":"t","description":"d","agent":"a"}]}""", TestContext.Current.CancellationToken);
        try
        {
            var (vm, processes, _) = Build();
            await vm.ResumeAsync(new ForgeSolutionSummary
            {
                Slug = "veille", State = "Test", Status = "Active", Directory = sessionDir,
            });

            // No engine ran — the pause is a review — and yet the identity is whole.
            Assert.Empty(processes.Requests);
            Assert.Equal("veille", vm.SessionSlug);
            Assert.True(vm.CanTryTeam);
            Assert.True(vm.TryTeamCommand.CanExecute(null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Modifier_reopens_the_wizard_on_the_adopted_team_and_readopts_the_same_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-wiz-reopen-" + Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(root, ".orkeon", "forge", "veille");
        var teamDir = Path.Combine(root, "teams", "veille-docs");
        Directory.CreateDirectory(sessionDir);
        Directory.CreateDirectory(teamDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "session.json"),
            $$"""{"v":1,"slug":"veille","title":"Veille","format":"yaml","state":"Promoted","status":"Promoted","promotedTo":{{System.Text.Json.JsonSerializer.Serialize(teamDir)}}}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "blueprint.json"),
            """{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_read"]}],"tasks":[{"key":"t","description":"d","agent":"a"}]}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(teamDir, "studio-team.json"),
            """{"name":"Veille docs","description":"le besoin d'origine","profile":"Local","schedule":"daily@07:30","mounts":["/data/docs:/docs:ro"]}""", TestContext.Current.CancellationToken);
        try
        {
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"));
            processes.OutputToEmit.AddRange(
            [
                Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"SESSION","format":"yaml","resumed":true}""".Replace("SESSION", System.Text.Json.JsonSerializer.Serialize(sessionDir).Trim('"'), StringComparison.Ordinal)),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"stage.entered","stage":"ready","iteration":1}"""),
                Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);

            var team = TeamCatalog.Describe(teamDir);
            var session = new ForgeSolutionSummary
            {
                Slug = "veille", State = "Promoted", Status = "Promoted",
                Directory = sessionDir, PromotedTo = teamDir,
            };
            await vm.ReopenTeamAsync(team, session);

            // The wizard reopened at Composer with the whole stepper reachable and the
            // adoption fields seeded from the sidecar.
            Assert.Equal(2, vm.Step);
            Assert.Equal(4, vm.MaxStep);
            Assert.Equal("Veille docs", vm.TeamName);
            Assert.Equal("Local", vm.AdoptProfileName);
            Assert.Equal(1, vm.ScheduleChoice);
            Assert.Equal("07:30", vm.ScheduleTime);
            Assert.Contains("/data/docs:/docs:ro", vm.TeamMounts);
            Assert.False(vm.IsSaved);
            Assert.Equal(["forge", "resume", "veille", "--events", "jsonl"], processes.Requests[0].Arguments);

            // Re-adoption is pinned to the ORIGINAL folder: renaming only renames.
            vm.TeamName = "Veille renommée";
            processes.OutputToEmit.Clear();
            processes.OutputToEmit.Add(Out(
                """{"v":2,"seq":1,"ts":"t","kind":"promoted","path":PATH,"launcher":"run.sh","updated":true}"""
                    .Replace("PATH", System.Text.Json.JsonSerializer.Serialize(teamDir), StringComparison.Ordinal)));
            Assert.True(vm.SaveTeamCommand.CanExecute(null));
            await vm.SaveTeamCommand.ExecuteAsync();

            var promote = processes.Requests[1].Arguments.ToList();
            Assert.Equal(teamDir, promote[promote.IndexOf("--to") + 1]);

            // The sidecar kept its description (no step-1 need on a reopen) and took the
            // new display name.
            var updated = TeamCatalog.Describe(teamDir);
            Assert.Equal("Veille renommée", updated.Name);
            Assert.Equal("le besoin d'origine", updated.Description);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_notes_question_travels_down_stdin_and_the_reply_lands_in_its_thread()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"stage.entered","stage":"blueprint","iteration":1}"""),
        ]);
        processes.WhileRunning = () =>
        {
            vm.ComposeNotes.QuestionDraft = "que se passe-t-il si un fichier est illisible ?";
            vm.ComposeNotes.AskCommand.Execute(null);
        };

        FillStepOne(vm);
        await vm.ComposeCommand.ExecuteAsync();

        Assert.Contains(
            """{"kind":"user.message","text":"que se passe-t-il si un fichier est illisible ?"}""",
            processes.InputLines);
        var exchange = Assert.Single(vm.ComposeNotes.Items);
        Assert.False(exchange.HasAnswer);   // scripted stream had no turn left to answer with
    }

    [Fact]
    public async Task An_unsolicited_assistant_turn_lands_in_the_bar_not_in_a_thread()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"assistant.message","text":"Quel dossier faut-il lire ?"}"""),
        ]);

        FillStepOne(vm);
        await vm.ComposeCommand.ExecuteAsync();

        Assert.True(vm.HasAssistantPrompt);
        Assert.Equal("Quel dossier faut-il lire ?", vm.AssistantPrompt);
        Assert.Empty(vm.ComposeNotes.Items);
    }

    [Fact]
    public async Task The_verdict_projects_the_checklist_and_refine_carries_the_trial_consigne()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"brief.ready","brief":{"goal":"Veille documentaire","acceptance":[{"id":"A1","statement":"Le résumé cite ses sources","kind":"must"}]}}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"stage.entered","stage":"verdict","iteration":1}"""),
            Out("""{"v":2,"seq":4,"ts":"t","kind":"verdict.ready","score":0.4,"passing":false,"findings":[{"id":"F1","severity":"major","acceptance":"A1","statement":"Les sources manquent"}],"suggestions":[],"judge":"llm"}"""),
            Out("""{"v":2,"seq":5,"ts":"t","kind":"decision.needed","options":["accept","refine","abort"]}"""),
        ]);
        processes.WhileRunning = () =>
        {
            // The engine paused on the arbitration only after its scripted lines played; the
            // wizard's buttons come from the options and "refine" sends the Consigne note first.
        };

        FillStepOne(vm);
        vm.TryNotes.Consigne = "n'analyser que les fichiers de la semaine";
        await vm.ComposeCommand.ExecuteAsync();

        Assert.Equal(3, vm.Step);
        var line = Assert.Single(vm.Checklist);
        Assert.Equal("Le résumé cite ses sources", line.Statement);
        Assert.False(line.Passed);
        Assert.Equal("Les sources manquent", line.Detail);
        Assert.Equal("score 0.40", vm.VerdictScore.Replace(',', '.'));
        Assert.Equal(["accept", "refine", "abort"], vm.Decisions.Select(d => d.Value));
        Assert.Equal("Fix and retry", vm.Decisions[1].Label);

        // The child is gone by now, so nothing can be written — but the mapping is pinned:
        // refine's command exists and the title prefilled from the brief's goal.
        Assert.Equal("Veille documentaire", vm.TeamName);
    }

    [Fact]
    public async Task Adopting_promotes_into_the_teams_root_and_writes_the_studio_sidecar()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-wizard-{Guid.NewGuid():N}");
        var promoted = Path.Combine(root, "ma-veille-quotidienne");
        try
        {
            var (vm, processes, _) = Build(teamsRoot: root);
            processes.OutputToEmit.AddRange(
            [
                Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            FillStepOne(vm);
            await vm.ComposeCommand.ExecuteAsync();

            Assert.Equal(4, vm.Step);
            vm.TeamName = "Ma veille quotidienne";
            vm.ScheduleChoice = 1;
            vm.ScheduleTime = "07:30";
            Assert.True(vm.CanSaveTeam);

            string? adoptedPath = null;
            vm.TeamAdopted += (_, e) => adoptedPath = e.Path;

            processes.OutputToEmit.Clear();
            processes.OutputToEmit.AddRange(
            [
                Out($$"""{"v":2,"seq":1,"ts":"t","kind":"promoted","path":{{System.Text.Json.JsonSerializer.Serialize(promoted)}},"launcher":"run.cmd","install":"schtasks hint"}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            await vm.SaveTeamCommand.ExecuteAsync();

            Assert.Equal(
                ["forge", "promote", "veille", "--to", promoted, "--events", "jsonl", "--schedule", "daily@07:30"],
                processes.LastRequest!.Arguments);
            Assert.True(vm.IsSaved);
            Assert.Equal(promoted, adoptedPath);
            Assert.Equal("schtasks hint", vm.InstallCommand);

            // The sidecar records what the crew definition cannot say.
            var summary = TeamCatalog.Describe(promoted);
            Assert.Equal("Ma veille quotidienne", summary.Name);
            Assert.Equal("Local", summary.Profile);
            Assert.Equal("daily@07:30", summary.Schedule);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_refused_promotion_says_so_and_saves_nothing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-wizard-{Guid.NewGuid():N}");
        try
        {
            var (vm, processes, _) = Build(teamsRoot: root);
            processes.OutputToEmit.AddRange(
            [
                Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            FillStepOne(vm);
            await vm.ComposeCommand.ExecuteAsync();
            vm.TeamName = "Ma veille";

            // The engine exits without a `promoted` event: nothing landed on disk.
            processes.OutputToEmit.Clear();
            await vm.SaveTeamCommand.ExecuteAsync();

            Assert.False(vm.IsSaved);
            Assert.NotNull(vm.StatusMessage);
            Assert.Contains("refused the promotion", vm.StatusMessage, StringComparison.Ordinal);
            Assert.False(Directory.Exists(root));   // no sidecar, no folder
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_daily_schedule_needs_a_time_the_engine_grammar_accepts()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
        ]);
        FillStepOne(vm);
        await vm.ComposeCommand.ExecuteAsync();
        vm.TeamName = "Ma veille";
        Assert.True(vm.CanSaveTeam);

        // `forge promote` speaks daily@HH:mm — a time it would refuse never leaves Studio.
        vm.ScheduleChoice = 1;
        vm.ScheduleTime = "7h30";
        Assert.False(vm.CanSaveTeam);

        vm.ScheduleTime = "07:30";
        Assert.True(vm.CanSaveTeam);
    }

    [Fact]
    public async Task Recomposing_after_a_finished_session_restarts_the_stepper_and_the_name()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
        ]);
        FillStepOne(vm);
        await vm.ComposeCommand.ExecuteAsync();
        Assert.Equal(4, vm.MaxStep);
        vm.TeamName = "Ancien nom";

        // A second composition is a new session: the stepper and the name start over.
        processes.OutputToEmit.Clear();
        processes.OutputToEmit.Add(
            Out("""{"v":2,"seq":1,"ts":"t","kind":"stage.entered","stage":"brief","iteration":1}"""));
        await vm.ComposeCommand.ExecuteAsync();

        Assert.Equal(1, vm.Step);
        Assert.Equal(1, vm.MaxStep);
        Assert.Equal("", vm.TeamName);
    }

    [Fact]
    public void The_teams_screen_lists_the_folders_and_hands_a_launch_to_the_shell()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-teams-{Guid.NewGuid():N}");
        try
        {
            var teamDir = Path.Combine(root, "veille-doc");
            Directory.CreateDirectory(teamDir);
            TeamCatalog.SaveMetadata(teamDir, new StudioTeamMetadata
            {
                Name = "Veille documentaire",
                Profile = "Local",
                Schedule = "daily@07:30",
            });

            var teams = new TeamsViewModel(teamsRoot: root, loadSessions: () => []);
            string? launched = null;
            teams.LaunchRequested += (_, e) => launched = e.Path;

            var card = Assert.Single(teams.Teams);
            Assert.Equal("Veille documentaire", card.Name);
            Assert.True(card.IsScheduled);
            Assert.Equal("Every day at 07:30", card.ScheduleDisplay);

            card.LaunchCommand.Execute(null);
            Assert.Equal(teamDir, launched);

            // Delete is armed, not immediate: the card asks first, and a cancel puts it back.
            card.AskDeleteCommand.Execute(null);
            Assert.True(card.IsConfirmingDelete);
            Assert.False(card.IsIdle);
            card.CancelDeleteCommand.Execute(null);
            Assert.False(card.IsConfirmingDelete);
            Assert.False(teams.IsEmpty);

            card.AskDeleteCommand.Execute(null);
            card.ConfirmDeleteCommand.Execute(null);
            Assert.True(teams.IsEmpty);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_goal_length_title_is_cut_to_a_display_name_at_a_word_boundary()
    {
        // v3 W-07: the engine now demands a short crew.name, but an older session's
        // title may still be the goal sentence — never mid-word, never trailing comma.
        Assert.Equal("Veille documentaire", CreateTeamViewModel.ShortName("  Veille documentaire  "));
        Assert.Equal(
            "Résumer en un seul passage les nouveautés d'un",
            CreateTeamViewModel.ShortName(
                "Résumer en un seul passage les nouveautés d'un site, à partir des fichiers enregistrés"));
    }

    [Fact]
    public void The_adopt_profile_card_unfolds_picks_and_folds_back()
    {
        var (vm, _, profiles) = Build();
        profiles.CommitEdit(
            new ModelProfile { Name = "Cloud", Provider = "Kimi", Model = "kimi-k2", BaseUrl = "https://api.moonshot.ai/v1" },
            previousName: null);

        Assert.False(vm.IsAdoptProfilePickerOpen);
        vm.ToggleAdoptProfilePickerCommand.Execute(null);
        Assert.True(vm.IsAdoptProfilePickerOpen);

        vm.PickAdoptProfileCommand.Execute("Cloud");

        // The pick lands on the card — name, origin line — and the rows fold back.
        Assert.Equal("Cloud", vm.AdoptProfileName);
        Assert.False(vm.IsAdoptProfilePickerOpen);
        Assert.Contains("Kimi", vm.AdoptProfileSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_shell_routes_a_team_launch_into_the_ordinary_launcher()
    {
        var shell = new MainWindowViewModel(
            settingsStore: new FakeAppSettingsStore(),
            directories: new FakeDirectoryProbe(),
            targetProbe: new FakeTargetProbe(),
            picker: new FakePathPicker(),
            processRunner: new OrkeonProcessRunner(
                new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            historyStore: new FakeLaunchHistoryStore(),
            forgeClient: new ForgeClient(
                new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            forgeWorkspace: "/ws",
            teamsRoot: "/nowhere/teams");

        shell.Teams.RequestLaunch("/teams/veille");

        // The adopted folder is an ordinary target: the launcher receives it as-is.
        Assert.Equal("/teams/veille", shell.Launch.Target.SelectedPath);
    }

    /// <summary>
    /// Studio launches an adopted team with its own --mount arguments, not through run.sh.
    /// Recording only the folders the user picked left the team without the /output its own
    /// agents were told to write to: the trial passed, the launch then reported success and
    /// wrote nothing. The write roots the blueprint addresses are bound to folders inside
    /// the team; a folder the user allowed for the same root wins.
    /// </summary>
    [Fact]
    public async Task Adoption_records_the_write_folders_the_blueprint_addresses()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_write"]}],"tasks":[{"key":"t","description":"d","agent":"a","deliverable":"/output/rapport.md"}],"rationale":"r"},"iteration":1}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);
        FillStepOne(vm);
        await vm.ComposeCommand.ExecuteAsync();

        Assert.Contains(vm.DerivedMounts, m => m.VirtualPath == "/output" && m.IsReadWrite);

        var teamDirectory = Path.Combine("/teams", "veille");
        var bound = Assert.Single(vm.WithDerivedWriteMounts(teamDirectory));
        Assert.EndsWith(":/output:rw", bound, StringComparison.Ordinal);
        Assert.StartsWith(Path.Combine(teamDirectory, "output"), bound, StringComparison.Ordinal);

        // An explicit choice for the same root beats the derived binding.
        vm.AddTeamMount(new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = Path.Combine("/data", "sorties"),
            VirtualPath = "/output",
            Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadWrite,
        });

        var chosen = Assert.Single(vm.WithDerivedWriteMounts(teamDirectory));
        Assert.StartsWith(Path.Combine("/data", "sorties"), chosen, StringComparison.Ordinal);
    }

    /// <summary>
    /// A team whose agents read files is adopted with somewhere to read from.
    /// <para>
    /// The Composer shows a <c>/workspace</c> (read) chip for any blueprint naming
    /// <c>file_read</c> — the trial bench really does mount it — and adoption dropped it:
    /// only the write roots were bound. So a reading team passed its trial and could then
    /// read nothing, the exact mirror of the missing <c>/output</c>. It binds to
    /// <c>input/</c> inside the team rather than to the team's root, because
    /// <c>--with-settings</c> puts an <c>appsettings.json</c> holding API keys at that root.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Adoption_records_a_read_folder_when_the_blueprint_reads()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_read","file_write"]}],"tasks":[{"key":"t","description":"d","agent":"a","deliverable":"/output/rapport.md"}],"rationale":"r"},"iteration":1}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);
        FillStepOne(vm);
        await vm.ComposeCommand.ExecuteAsync();

        Assert.Contains(vm.DerivedMounts, m => m.VirtualPath == "/workspace" && !m.IsReadWrite);

        var teamDirectory = Path.Combine("/teams", "veille");
        var mounts = vm.WithDerivedWriteMounts(teamDirectory);

        var read = Assert.Single(mounts, m => m.EndsWith(":/workspace:ro", StringComparison.Ordinal));
        Assert.StartsWith(Path.Combine(teamDirectory, "input"), read, StringComparison.Ordinal);
        Assert.Contains(mounts, m => m.EndsWith(":/output:rw", StringComparison.Ordinal));
    }

    /// <summary>
    /// One chip per virtual root: a root the user allowed a folder for is shown as the
    /// removable chip that wins at save, not also as an informative derived chip.
    /// <para>
    /// Both lists used to render <c>/output</c>, so the same root appeared twice — and
    /// removing the removable one changed nothing at save, because the derived binding took
    /// its place silently. The screen now says what the save will do: remove the explicit
    /// chip and the derived one comes back, visibly.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_root_the_user_claimed_is_shown_once_and_its_removal_is_visible()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_write"]}],"tasks":[{"key":"t","description":"d","agent":"a","deliverable":"/output/rapport.md"}],"rationale":"r"},"iteration":1}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);
        FillStepOne(vm);
        await vm.ComposeCommand.ExecuteAsync();

        // Nothing claimed yet: the derived chip carries the information.
        Assert.Contains(vm.UnclaimedDerivedMounts, m => m.VirtualPath == "/output");
        Assert.Empty(vm.TeamMountChips);

        var chosen = new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = Path.Combine("/data", "sorties"),
            VirtualPath = "/output",
            Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadWrite,
        };
        vm.AddTeamMount(chosen);

        // Claimed: one chip, the removable one.
        Assert.Single(vm.TeamMountChips);
        Assert.DoesNotContain(vm.UnclaimedDerivedMounts, m => m.VirtualPath == "/output");

        vm.RemoveTeamMountCommand.Execute(chosen.ToMountString());

        // Removed: the derived chip is back on screen, which is exactly what the save records.
        Assert.Empty(vm.TeamMountChips);
        Assert.Contains(vm.UnclaimedDerivedMounts, m => m.VirtualPath == "/output");
        Assert.Contains(
            vm.WithDerivedWriteMounts(Path.Combine("/teams", "veille")),
            m => m.EndsWith(":/output:rw", StringComparison.Ordinal));
    }

    /// <summary>
    /// The folders the agents imply are removable like any other, and dropping one is honoured
    /// at save.
    /// <para>
    /// They used to be informative chips with no ✕ — "edit an agent to change them" — which
    /// left a team carrying a root its owner did not want with no way to say so. Dropping one
    /// now sticks: <see cref="CreateTeamViewModel.WithDerivedWriteMounts"/> no longer re-adds
    /// it, which is the same silent undo that method exists to prevent. The screen warns,
    /// because nothing will be bound to that root and the agents writing there will fail.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_folder_the_agents_imply_can_be_dropped_and_stays_dropped_at_save()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_read","file_write"]}],"tasks":[{"key":"t","description":"d","agent":"a","deliverable":"/output/rapport.md"}],"rationale":"r"},"iteration":1}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);
        FillStepOne(vm);
        await vm.ComposeCommand.ExecuteAsync();

        Assert.Equal(["/workspace", "/output"], vm.UnclaimedDerivedMounts.Select(m => m.VirtualPath));
        Assert.False(vm.HasDroppedDerivedRoots);

        vm.RemoveDerivedMountCommand.Execute("/output");

        Assert.Equal(["/workspace"], vm.UnclaimedDerivedMounts.Select(m => m.VirtualPath));
        Assert.Equal(["/output"], vm.DroppedDerivedRoots);
        Assert.True(vm.HasDroppedDerivedRoots);
        Assert.Contains("/output", vm.DroppedDerivedWarning, StringComparison.Ordinal);

        var mounts = vm.WithDerivedWriteMounts(Path.Combine("/teams", "veille"));
        Assert.DoesNotContain(mounts, m => m.EndsWith(":/output:rw", StringComparison.Ordinal));
        Assert.Contains(mounts, m => m.EndsWith(":/workspace:ro", StringComparison.Ordinal));

        // A wrong ✕ has a way back.
        Assert.True(vm.RestoreDerivedMountsCommand.CanExecute(null));
        vm.RestoreDerivedMountsCommand.Execute(null);
        Assert.False(vm.HasDroppedDerivedRoots);
        Assert.Contains(
            vm.WithDerivedWriteMounts(Path.Combine("/teams", "veille")),
            m => m.EndsWith(":/output:rw", StringComparison.Ordinal));
    }

    /// <summary>
    /// The chips of the folders the agents imply always read red: the folder behind them is
    /// created inside the team at adoption, so it is by construction not one of the settings'
    /// authorized folders. A folder taken from the settings reads neutral.
    /// </summary>
    [Fact]
    public async Task The_agent_implied_chips_read_as_undeclared_and_a_settings_folder_does_not()
    {
        var (vm, processes, _) = Build(declaredMounts: () => ["/data/sorties:/output:rw"]);
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_read"]}],"tasks":[{"key":"t","description":"d","agent":"a"}],"rationale":"r"},"iteration":1}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);
        FillStepOne(vm);
        await vm.ComposeCommand.ExecuteAsync();

        Assert.Contains(vm.UnclaimedDerivedMounts, m => m.VirtualPath == "/workspace");

        vm.AddTeamMount(new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = "/data/sorties",
            VirtualPath = "/output",
            Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadWrite,
        });
        Assert.False(Assert.Single(vm.TeamMountChips).IsUndeclared);

        vm.AddTeamMount(new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = "/elsewhere/archives",
            VirtualPath = "/archives",
        });
        Assert.True(vm.TeamMountChips.Single(c => c.MountString.StartsWith("/elsewhere", StringComparison.Ordinal)).IsUndeclared);
    }
}

/// <summary>A question asked while the engine is not listening must not vanish silently.</summary>
public sealed class AskWithoutEngineTests
{
    [Fact]
    public void The_panel_says_the_assistant_is_not_running_and_keeps_the_draft()
    {
        var notes = new StepNotesViewModel((origin, _) =>
        {
            // What CreateTeamViewModel.AskAssistant does when ForgeClient.SendMessage
            // returns false: name the refusal, refuse the send.
            origin.Notice = EnglishStudioStrings.Instance[StudioStringKeys.WizardAssistantNotRunning];
            return false;
        });

        notes.QuestionDraft = "Peut-elle lire des PDF ?";
        notes.AskCommand.Execute(null);

        Assert.NotNull(notes.Notice);
        Assert.Equal("Peut-elle lire des PDF ?", notes.QuestionDraft); // kept for the retry
        Assert.Empty(notes.Items);                                     // nothing pretended sent

        // Typing again clears the notice.
        notes.QuestionDraft = "Peut-elle lire des PDF et des CSV ?";
        Assert.Null(notes.Notice);
    }
}
