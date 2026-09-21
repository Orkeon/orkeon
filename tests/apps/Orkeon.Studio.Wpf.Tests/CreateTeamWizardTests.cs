using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Services;
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
        Func<IReadOnlyList<string>>? declaredMounts = null,
        bool cliInstalled = true,
        Orkeon.Studio.Wpf.ViewModels.Mvvm.IShellOpener? shellOpener = null,
        Orkeon.Studio.Wpf.ViewModels.Mvvm.IUiDispatcher? dispatcher = null,
        string? workspace = null)
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
        // A machine without the CLI is the first-run machine, not an edge: the wizard's
        // failure card (STUDIO-13) is what it shows instead of a spinner and step 1 again.
        var client = new ForgeClient(
            processes,
            new OrkeonBinaryLocator(cliInstalled
                ? FakeExecutableProbe.WithOrkeonInstalled()
                : new FakeExecutableProbe("/opt/orkeon")));
        var vm = new CreateTeamViewModel(
            profiles,
            new CreateTeamDependencies
            {
                Client = client,
                WorkspaceDirectory = workspace ?? "/ws",
                TeamsRoot = teamsRoot ?? "/teams",
                DeclaredMounts = declaredMounts,
                ShellOpener = shellOpener,
                Dispatcher = dispatcher,
            });
        return (vm, processes, profiles);
    }

    /// <summary>
    /// The compose-the-team button from the outside. The gesture IS the engine now: there is no
    /// local questionnaire to drain first, and whatever the model wants to ask it asks
    /// down the wire once the run is under way.
    /// </summary>
    private static Task Compose(CreateTeamViewModel vm) => vm.ComposeCommand.ExecuteAsync();

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
        await Compose(vm);

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
        await Compose(vm);

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
        await Compose(vm);

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

    /// <summary>
    /// Owner report of 2026-09-21: « Modify » parks the team's session under « Sessions in
    /// progress », « Resume » opens the wizard on it, and deleting the row there left the
    /// wizard exactly as it was — a Composer over a directory that no longer existed. The
    /// wizard now forgets the session when told it is gone, and only that one.
    /// </summary>
    [Fact]
    public async Task A_session_discarded_from_my_teams_takes_the_wizard_open_on_it_back_to_step_1()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-wiz-forget-" + Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(root, ".orkeon", "forge", "veille");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "session.json"),
            """{"v":1,"slug":"veille","title":"Veille","format":"yaml","state":"Test","status":"Active"}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "blueprint.json"),
            """{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":[]}],"tasks":[{"key":"t","description":"d","agent":"a"}]}""", TestContext.Current.CancellationToken);
        try
        {
            var (vm, _, _) = Build();
            await vm.ResumeAsync(new ForgeSolutionSummary
            {
                Slug = "veille", State = "Test", Status = "Active", Directory = sessionDir,
            });
            Assert.True(vm.HasDraft);
            Assert.Single(vm.Agents);

            // Another session going is none of this wizard's business.
            vm.ForgetSession(Path.Combine(root, ".orkeon", "forge", "autre"));
            Assert.Equal("veille", vm.SessionSlug);
            Assert.Single(vm.Agents);

            // Its own — spelled with a trailing separator, as a catalog might — is the end
            // of the creation: the same blank step 1 as « Start over ».
            vm.ForgetSession(sessionDir + Path.DirectorySeparatorChar);
            Assert.Equal(1, vm.Step);
            Assert.Equal(1, vm.MaxStep);
            Assert.False(vm.HasDraft);
            Assert.Null(vm.SessionSlug);
            Assert.Empty(vm.Agents);
            Assert.False(vm.CanTryTeam);
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
    public async Task A_free_question_travels_down_stdin_while_the_engine_is_listening()
    {
        // The channel the three per-step mini-threads used is not lost with them: the one
        // conversation still sends down stdin whenever a forge session is listening. What
        // changed is where the question is typed, and that its history survives the step.
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"stage.entered","stage":"blueprint","iteration":1}"""),
        ]);
        processes.WhileRunning = () =>
        {
            // The composer is locked while the assistant is thinking, so the model
            // speaks first — which is also the only moment a user could type.
            processes.Emit(Out("""{"v":2,"seq":3,"ts":"t","kind":"assistant.message","text":"Quel dossier ?"}"""));
            vm.Chat.Draft = "que se passe-t-il si un fichier est illisible ?";
            vm.Chat.SendCommand.Execute(null);
        };

        FillStepOne(vm);
        await Compose(vm);

        Assert.Contains(
            """{"kind":"user.message","text":"que se passe-t-il si un fichier est illisible ?"}""",
            processes.InputLines);

        // It is in the thread as the user's own turn, waiting for the engine's answer —
        // the scripted stream had no turn left to give it one.
        Assert.Contains(
            vm.Chat.Turns,
            t => !t.IsBot && t.Body == "que se passe-t-il si un fichier est illisible ?");
    }

    [Fact]
    public async Task An_unsolicited_assistant_turn_lands_in_the_conversation()
    {
        // Inverted by the 30/08 mock (T-01/T-08). This test used to assert the opposite —
        // that the turn surfaced in a bar above the form — which is the ergonomic defect
        // the thread replaces: a question arriving outside the reading flow, alone, with
        // nothing behind it saying what the assistant had already understood.
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"assistant.message","text":"Quel dossier faut-il lire ?"}"""),
        ]);

        FillStepOne(vm);
        await Compose(vm);

        var turn = Assert.Single(vm.Chat.Turns, t => t.IsBot && t.Body == "Quel dossier faut-il lire ?");
        Assert.True(turn.IsBot);
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
        await Compose(vm);

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

    /// <summary>
    /// The end of the tunnel (STUDIO-20): the promotion lands in the teams root with its sidecar,
    /// the shell hears it, and the wizard is a blank step 1 again — nothing of the adopted
    /// creation survives but the one line that says where it went.
    /// </summary>
    [Fact]
    public async Task Adopting_writes_the_sidecar_and_ends_the_tunnel_at_a_blank_step_one()
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
            await Compose(vm);

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
            Assert.Equal(promoted, adoptedPath);

            // The sidecar records what the crew definition cannot say.
            var summary = TeamCatalog.Describe(promoted);
            Assert.Equal("Ma veille quotidienne", summary.Name);
            Assert.Equal("Local", summary.Profile);
            Assert.Equal("daily@07:30", summary.Schedule);

            // And the wizard is back where a creation starts.
            Assert.Equal(1, vm.Step);
            Assert.Equal(1, vm.MaxStep);
            Assert.Equal("", vm.TeamName);
            Assert.Equal("", vm.Need);
            Assert.All(vm.FrequencyChoices.Concat(vm.SourceChoices).Concat(vm.OutputChoices), choice => Assert.False(choice.IsSelected));
            Assert.Empty(vm.TeamMounts);
            Assert.True(vm.Chat.IsEmpty);
            Assert.False(vm.HasDraft);
            Assert.False(vm.CanSaveTeam);
            Assert.False(vm.RestartCommand.CanExecute(null));
            Assert.Equal("Team “Ma veille quotidienne” is saved in My teams.", vm.StatusMessage);
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
            await Compose(vm);
            vm.TeamName = "Ma veille";

            // The engine exits without a `promoted` event: nothing landed on disk.
            processes.OutputToEmit.Clear();
            await vm.SaveTeamCommand.ExecuteAsync();

            // A refusal ends nothing: the form stays, with the sentence that says why.
            Assert.Equal(4, vm.Step);
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
        await Compose(vm);
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
        await Compose(vm);
        Assert.Equal(4, vm.MaxStep);
        vm.TeamName = "Ancien nom";

        // A second composition is a new session: the stepper and the name start over.
        processes.OutputToEmit.Clear();
        processes.OutputToEmit.Add(
            Out("""{"v":2,"seq":1,"ts":"t","kind":"stage.entered","stage":"brief","iteration":1}"""));
        await Compose(vm);

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

            var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = root, LoadSessions = () => [] });
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

    /// <summary>The README a user pastes into the name field: headings, a quote, a list.</summary>
    private const string PastedPage = """
        # Extraire les factures fournisseurs déposées en `docs/`, classées par mois et par fournisseur, avec un **contrôle** des doublons

        > Un README entier.

        - lit chaque PDF
        - en extrait le fournisseur et la date
        """;

    [Fact]
    public async Task Adoption_writes_a_normalized_name_even_when_the_user_pasted_a_page()
    {
        // STUDIO-16 (D-04): the sidecar's name is one line, under 64, without markup, whatever
        // the field holds — the run card, the my-teams card and the history read it as a title.
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-wizard-{Guid.NewGuid():N}");
        try
        {
            var (vm, processes, _) = Build(teamsRoot: root);
            processes.OutputToEmit.AddRange(
            [
                Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            vm.Need = PastedPage;
            vm.FrequencyChoices[1].SelectCommand.Execute(null);
            vm.SourceChoices[0].SelectCommand.Execute(null);
            vm.OutputChoices[0].SelectCommand.Execute(null);
            await Compose(vm);

            vm.TeamName = PastedPage;
            Assert.True(vm.CanSaveTeam);
            var promoted = Path.Combine(root, TeamCatalog.Slugify(vm.TeamName));

            processes.OutputToEmit.Clear();
            processes.OutputToEmit.AddRange(
            [
                Out($$"""{"v":2,"seq":1,"ts":"t","kind":"promoted","path":{{System.Text.Json.JsonSerializer.Serialize(promoted)}},"launcher":"run.cmd"}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            await vm.SaveTeamCommand.ExecuteAsync();

            var summary = TeamCatalog.Describe(promoted);
            Assert.Equal("Extraire les factures fournisseurs déposées en docs/, classées", summary.Name);
            Assert.True(summary.Name.Length <= TeamCatalog.MaxNameLength);
            // The need stays whole: it is the archive of what was asked, the summary is derived.
            Assert.Equal(PastedPage, summary.Description);
            Assert.Equal("Un README entier.", summary.Summary);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_name_field_shows_the_normalized_value_live_without_fighting_a_keystroke()
    {
        var (vm, _, _) = Build();

        // A pasted page collapses to its first line at once, markup gone, cut at a word.
        vm.TeamName = PastedPage;
        Assert.Equal("Extraire les factures fournisseurs déposées en docs/, classées", vm.TeamName);

        // Typing is never fought: the space before the next word stays, and so does the
        // leading one; the write trims them. A character past the cap is refused whole,
        // the way MaxLength would, rather than costing the word being typed.
        vm.TeamName = "Ma veille ";
        Assert.Equal("Ma veille ", vm.TeamName);
        vm.TeamName = "  Ma veille";
        Assert.Equal("  Ma veille", vm.TeamName);
        var atTheCap = new string('a', 60) + " bcd";
        vm.TeamName = atTheCap;
        Assert.Equal(atTheCap, vm.TeamName);
        vm.TeamName = atTheCap + "e";
        Assert.Equal(atTheCap, vm.TeamName);

        // An empty field stays empty — the fallback on the slug belongs to the write — and
        // a lone marker is nothing to keep.
        vm.TeamName = "";
        Assert.Equal("", vm.TeamName);
        vm.TeamName = "#";
        Assert.Equal("", vm.TeamName);
        Assert.False(vm.CanSaveTeam);
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
            new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                TargetProbe = new FakeTargetProbe(),
                Picker = new FakePathPicker(),
                ProcessRunner = new OrkeonProcessRunner(
                    new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
                ForgeClient = new ForgeClient(
                    new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            },
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
    /// the team; a folder the user allowed for the same root wins. What the sidecar records
    /// for an in-team folder is the team-relative spelling, <c>./output:/output:rw</c>, and
    /// the save is what creates the folder (STUDIO-14).
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
        await Compose(vm);

        Assert.Contains(vm.DerivedMounts, m => m.VirtualPath == "/output" && m.IsReadWrite);

        var root = Path.Combine(Path.GetTempPath(), $"orkeon-wizard-{Guid.NewGuid():N}");
        var teamDirectory = Path.Combine(root, "veille");
        try
        {
            // Team-relative, with no team directory in sight: the entry is the same wherever
            // the team lands, and the save is what binds it under the folder.
            var bound = Assert.Single(vm.SidecarMounts());
            Assert.Equal("./output:/output:rw", bound);

            // The sidecar carries the in-team folder relative to the team, and the save
            // creates it; the catalog hands it back absolute, under the team.
            TeamCatalog.SaveMetadata(teamDirectory, new StudioTeamMetadata { Name = "Veille", Mounts = [bound] });
            var team = TeamCatalog.Describe(teamDirectory);
            Assert.Equal(["./output:/output:rw"], team.Metadata!.Mounts);
            Assert.Equal([$"{Path.Combine(teamDirectory, "output")}:/output:rw"], team.Mounts);
            Assert.True(Directory.Exists(Path.Combine(teamDirectory, "output")));

            // An explicit choice for the same root beats the derived binding — and, being
            // outside the team, is recorded as it is.
            vm.AddTeamMount(new Orkeon.Studio.Core.FileSystem.MountDefinition
            {
                PhysicalPath = Path.Combine("/data", "sorties"),
                VirtualPath = "/output",
                Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadWrite,
            });

            var chosen = Assert.Single(vm.SidecarMounts());
            Assert.StartsWith(Path.Combine("/data", "sorties"), chosen, StringComparison.Ordinal);
            TeamCatalog.SaveMetadata(teamDirectory, new StudioTeamMetadata { Name = "Veille", Mounts = [chosen] });
            Assert.Equal([chosen], TeamCatalog.Describe(teamDirectory).Metadata!.Mounts);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
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
        await Compose(vm);

        Assert.Contains(vm.DerivedMounts, m => m.VirtualPath == "/workspace" && !m.IsReadWrite);

        var root = Path.Combine(Path.GetTempPath(), $"orkeon-wizard-{Guid.NewGuid():N}");
        var teamDirectory = Path.Combine(root, "veille");
        try
        {
            var mounts = vm.SidecarMounts();

            Assert.Contains("./input:/workspace:ro", mounts);
            Assert.Contains("./output:/output:rw", mounts);

            // In the sidecar, both are the team's own folders, relative to it — and both
            // exist once it is written (STUDIO-14).
            TeamCatalog.SaveMetadata(teamDirectory, new StudioTeamMetadata { Name = "Veille", Mounts = mounts });
            var recorded = TeamCatalog.Describe(teamDirectory).Metadata!.Mounts!;
            Assert.Contains("./input:/workspace:ro", recorded);
            Assert.Contains("./output:/output:rw", recorded);
            Assert.True(Directory.Exists(Path.Combine(teamDirectory, "input")));
            Assert.True(Directory.Exists(Path.Combine(teamDirectory, "output")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
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
        await Compose(vm);

        // Nothing bound yet: the row names the mount point and offers to answer it.
        Assert.Contains(vm.MountRows, r => r is { VirtualPath: "/output", IsBound: false });

        var chosen = new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = Path.Combine("/data", "sorties"),
            VirtualPath = "/output",
            Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadWrite,
        };
        vm.AddTeamMount(chosen);

        // Bound: ONE row for the mount point, carrying the folder and removable.
        var bound = Assert.Single(vm.MountRows, r => r.VirtualPath == "/output");
        Assert.True(bound.IsBound);
        Assert.Equal(Path.Combine("/data", "sorties"), bound.Folder);

        vm.RemoveTeamMountCommand.Execute(chosen.ToMountString());

        // Removed: the mount point is back to unanswered, which is what the save records.
        Assert.Contains(vm.MountRows, r => r is { VirtualPath: "/output", IsBound: false });
        Assert.Contains(
            vm.SidecarMounts(),
            m => m.EndsWith(":/output:rw", StringComparison.Ordinal));
    }

    /// <summary>
    /// The folders the agents imply are removable like any other, and dropping one is honoured
    /// at save.
    /// <para>
    /// They used to be informative chips with no ✕ — "edit an agent to change them" — which
    /// left a team carrying a root its owner did not want with no way to say so. Dropping one
    /// now sticks: <see cref="CreateTeamViewModel.SidecarMounts"/> no longer re-adds
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
        await Compose(vm);

        Assert.Equal(["/workspace", "/output"], vm.MountRows.Select(r => r.VirtualPath));
        Assert.False(vm.HasDroppedDerivedRoots);

        vm.RemoveDerivedMountCommand.Execute("/output");

        Assert.Equal(["/workspace"], vm.MountRows.Select(r => r.VirtualPath));
        Assert.Equal(["/output"], vm.DroppedDerivedRoots);
        Assert.True(vm.HasDroppedDerivedRoots);
        Assert.Contains("/output", vm.DroppedDerivedWarning, StringComparison.Ordinal);

        var mounts = vm.SidecarMounts();
        Assert.DoesNotContain(mounts, m => m.EndsWith(":/output:rw", StringComparison.Ordinal));
        Assert.Contains(mounts, m => m.EndsWith(":/workspace:ro", StringComparison.Ordinal));

        // A wrong ✕ has a way back.
        Assert.True(vm.RestoreDerivedMountsCommand.CanExecute(null));
        vm.RestoreDerivedMountsCommand.Execute(null);
        Assert.False(vm.HasDroppedDerivedRoots);
        Assert.Contains(
            vm.SidecarMounts(),
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
        await Compose(vm);

        Assert.Contains(vm.MountRows, r => r is { VirtualPath: "/workspace", IsBound: false });

        vm.AddTeamMount(new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = "/data/sorties",
            VirtualPath = "/output",
            Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadWrite,
        });
        Assert.False(Assert.Single(vm.MountRows, r => r.IsBound).IsUndeclared);

        vm.AddTeamMount(new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = "/elsewhere/archives",
            VirtualPath = "/archives",
        });
        Assert.True(vm.MountRows.Single(r => r.MountString.StartsWith("/elsewhere", StringComparison.Ordinal)).IsUndeclared);
    }

    /// <summary>
    /// Binding a folder behind a mount point the blueprint implied — the gesture the card had
    /// no way to express.
    /// <para>
    /// The row for <c>/workspace</c> existed and said who addressed it; what it could not say
    /// is WHERE. So a reading team was adopted with its own empty <c>input/</c> behind that
    /// root, and its first task — «find under /workspace the folder containing the notes» —
    /// found nothing. Answering the row is what puts a real folder there, at adoption, in the
    /// sidecar.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_folder_can_be_bound_behind_a_mount_point_the_agents_imply()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_read"]}],"tasks":[{"key":"t","description":"d","agent":"a"}],"rationale":"r"},"iteration":1}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);
        FillStepOne(vm);
        await Compose(vm);

        // Unanswered: the row says so, and so does the sentence about input/.
        var before = Assert.Single(vm.MountRows);
        Assert.Equal("/workspace", before.VirtualPath);
        Assert.False(before.HasFolder);
        Assert.True(vm.NeedsInputFolder);

        // The row's gesture asks the shell for a folder, naming the mount point it answers.
        string? asked = null;
        vm.AllowFolderRequested += (_, e) => asked = e.TargetVirtualPath;
        vm.BindMountCommand.Execute("/workspace");
        Assert.Equal("/workspace", asked);

        vm.BindTeamMount("/workspace", new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = Path.Combine("/data", "notes"),
            VirtualPath = "/workspace",
            Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadOnly,
        });

        // One row still, now carrying the folder — and the rights the settings declared.
        var bound = Assert.Single(vm.MountRows);
        Assert.Equal("/workspace", bound.VirtualPath);
        Assert.Equal(Path.Combine("/data", "notes"), bound.Folder);
        Assert.False(bound.IsReadWrite);
        Assert.False(vm.NeedsInputFolder);

        // And the save records the user's folder, not the empty one inside the team.
        var mounts = vm.SidecarMounts();
        Assert.Contains(mounts, m => m.StartsWith(Path.Combine("/data", "notes"), StringComparison.Ordinal)
            && m.EndsWith(":/workspace:ro", StringComparison.Ordinal));
        Assert.DoesNotContain(mounts, m => m.Contains(Path.Combine("veille", "input"), StringComparison.Ordinal));
    }

    /// <summary>
    /// A mount string the parser refuses still gets a line — dropping it silently would hide
    /// a mount the team will carry — but it offers no binding: what it displays is a message,
    /// not a virtual path, and targeting the chooser at it would bind a folder behind that
    /// message. All it can offer is its own removal.
    /// </summary>
    [Fact]
    public void An_unreadable_mount_is_listed_but_offers_no_folder_to_choose()
    {
        var (vm, _, _) = Build();
        vm.TeamMounts.Add("this is not a mount string");

        var row = Assert.Single(vm.MountRows);
        Assert.True(row.IsUnreadable);
        Assert.True(row.IsUndeclared);
        Assert.True(row.IsBound);
        Assert.False(row.CanChooseFolder);
        Assert.Equal("this is not a mount string", row.MountString);
    }

    /// <summary>
    /// The other answer to the dry pause, from Studio: "Adopt without trying" resumes the
    /// same session with <c>--adopt</c>. It is offered exactly when the trial is — the pause
    /// is where both answers exist — and it runs the engine offline, without a trial.
    /// </summary>
    [Fact]
    public async Task Adopting_without_a_trial_resumes_the_paused_session_with_adopt()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);
        FillStepOne(vm);
        await Compose(vm);

        // Both answers to the pause are offered, and by the same gate.
        Assert.True(vm.CanTryTeam);
        Assert.True(vm.AdoptWithoutTrialCommand.CanExecute(null));

        processes.OutputToEmit.Clear();
        processes.OutputToEmit.Add(
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""));
        vm.TeamName = "veille";

        await vm.AdoptWithoutTrialCommand.ExecuteAsync();

        var argv = processes.Requests[^1].Arguments;
        Assert.Equal(["forge", "resume", "veille", "--events", "jsonl", "--adopt"], argv);

        // Ready: the team may be saved, and the pause's two buttons are gone.
        Assert.True(vm.CanSaveTeam);
        Assert.False(vm.CanTryTeam);
    }

    /// <summary>A mount point takes one folder: binding again replaces, never accumulates.</summary>
    [Fact]
    public async Task Binding_a_mount_point_twice_replaces_the_folder_behind_it()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_read"]}],"tasks":[{"key":"t","description":"d","agent":"a"}],"rationale":"r"},"iteration":1}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);
        FillStepOne(vm);
        await Compose(vm);

        vm.BindTeamMount("/workspace", new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = "/data/first", VirtualPath = "/workspace",
            Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadOnly,
        });
        vm.BindTeamMount("/workspace", new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = "/data/second", VirtualPath = "/workspace",
            Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadOnly,
        });

        var row = Assert.Single(vm.MountRows);
        Assert.Equal("/data/second", row.Folder);
        Assert.Single(vm.TeamMounts);
    }

    // ── STUDIO-14: where a team's folders live — the step-1 policy, the in-team answers, the
    //    relative sidecar, the trial's read root and the header's « Open the folder » ──

    private const string SessionStarted =
        """{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}""";

    private const string Paused =
        """{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}""";

    /// <summary>A blueprint whose agent reads and whose task writes under <paramref name="deliverableRoot"/>.</summary>
    private static string ReadingWritingBlueprint(string deliverableRoot = "/output") =>
        $$"""{"v":2,"seq":2,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_read","file_write"]}],"tasks":[{"key":"t","description":"d","agent":"a","deliverable":"{{deliverableRoot}}/rapport.md"}],"rationale":"r"},"iteration":1}""";

    private static WizardChoice Policy(CreateTeamViewModel vm, FolderPolicy policy) =>
        vm.FolderPolicyChoices.Single(c => c.Key == policy.ToString());

    /// <summary>
    /// D-06/D-07. «Created inside the team» answers both canonical rows team-relative, before any
    /// blueprint exists, and composing — which used to clear every folder — keeps them: they
    /// are the user's answers, not the previous blueprint's.
    /// </summary>
    [Fact]
    public async Task Choosing_inside_the_team_at_step_one_seeds_both_roots_relative_and_they_survive_the_compose()
    {
        var (vm, processes, _) = Build();
        FillStepOne(vm);

        // Later by default, nothing to show, and the compose button does not wait for an answer.
        Assert.Equal(FolderPolicy.Later, vm.FolderPolicy);
        Assert.True(Policy(vm, FolderPolicy.Later).IsSelected);
        Assert.False(vm.HasStepOneRows);
        Assert.True(vm.CanCompose);

        Policy(vm, FolderPolicy.InsideTeam).SelectCommand.Execute(null);

        Assert.Equal(FolderPolicy.InsideTeam, vm.FolderPolicy);
        Assert.True(vm.HasStepOneRows);
        Assert.Equal(["./input:/workspace:ro", "./output:/output:rw"], vm.TeamMounts);
        var rows = vm.StepOneRows;
        Assert.Equal(["Your documents", "The results"], rows.Select(r => r.Title));
        Assert.Equal(["/workspace", "/output"], rows.Select(r => r.VirtualPath));
        Assert.All(rows, r => Assert.True(r.IsInsideTeam));
        Assert.All(rows, r => Assert.False(r.IsUndeclared));
        Assert.Equal(["inside the team: input", "inside the team: output"], rows.Select(r => r.Folder));
        Assert.True(vm.CanCompose);

        processes.OutputToEmit.AddRange([Out(SessionStarted), Out(ReadingWritingBlueprint()), Out(Paused)]);
        await Compose(vm);

        // The compose kept both answers, and the Composer's rows show them answered.
        Assert.Equal(2, vm.Step);
        Assert.Equal(["./input:/workspace:ro", "./output:/output:rw"], vm.TeamMounts);
        Assert.All(vm.MountRows, r => Assert.True(r.IsInsideTeam));
        Assert.Equal(["/workspace", "/output"], vm.MountRows.Select(r => r.VirtualPath));
        // The trial has nothing to read yet — the folder is born at adoption — and the argv
        // says nothing about it: no --read for a folder that does not exist.
        Assert.True(vm.TrialReadsInsideTeam);
        Assert.DoesNotContain("--read", processes.Requests[0].Arguments);
    }

    /// <summary>
    /// D-06/D-10. «Existing folders» shows the two rows unanswered and their « Choose the
    /// folder… » asks the shell for the DISK picker — not the declared list — on the row's
    /// rights: read-only for «Your documents», read-and-write for «The results».
    /// </summary>
    [Fact]
    public void Choosing_existing_folders_at_step_one_asks_the_disk_picker_with_the_rows_rights()
    {
        var (vm, _, _) = Build();
        var picks = new List<(string? Target, Orkeon.Studio.Core.FileSystem.MountRights Rights)>();
        var chooserOpened = 0;
        vm.PickFolderRequested += (_, e) => picks.Add((e.TargetVirtualPath, e.Rights));
        vm.AllowFolderRequested += (_, _) => chooserOpened++;

        Policy(vm, FolderPolicy.ExistingFolders).SelectCommand.Execute(null);

        Assert.True(vm.HasStepOneRows);
        Assert.Empty(vm.TeamMounts);
        Assert.All(vm.StepOneRows, r => Assert.True(r.CanChooseFolder));
        Assert.All(vm.StepOneRows, r => Assert.False(r.IsDroppable));

        vm.BindMountCommand.Execute("/workspace");
        vm.BindMountCommand.Execute("/output");

        Assert.Equal(
            [("/workspace", Orkeon.Studio.Core.FileSystem.MountRights.ReadOnly), ("/output", Orkeon.Studio.Core.FileSystem.MountRights.ReadWrite)],
            picks);
        Assert.Equal(0, chooserOpened);

        // What the shell binds back lands on the row, and reads as a real folder.
        vm.BindTeamMount("/workspace", new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = "/data/factures", VirtualPath = "/workspace",
            Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadOnly,
        });
        var documents = vm.StepOneRows.Single(r => r.VirtualPath == "/workspace");
        Assert.Equal("/data/factures", documents.Folder);
        Assert.False(documents.IsInsideTeam);
        Assert.True(documents.IsBound);
    }

    /// <summary>D-06. «Later» is today's behaviour: no rows, nothing bound, the Composer step asks.</summary>
    [Fact]
    public async Task The_later_policy_leaves_the_rows_unanswered_as_before()
    {
        var (vm, processes, _) = Build();
        FillStepOne(vm);
        Policy(vm, FolderPolicy.Later).SelectCommand.Execute(null);

        Assert.False(vm.HasStepOneRows);
        Assert.Empty(vm.TeamMounts);

        processes.OutputToEmit.AddRange([Out(SessionStarted), Out(ReadingWritingBlueprint()), Out(Paused)]);
        await Compose(vm);

        // The Composer's rows are the bare implied roots, and « Choose the folder… » still
        // opens the declared list — the second way in, unchanged.
        Assert.Empty(vm.TeamMounts);
        Assert.All(vm.MountRows, r => Assert.False(r.IsBound));
        string? asked = null;
        vm.AllowFolderRequested += (_, e) => asked = e.TargetVirtualPath;
        vm.BindMountCommand.Execute("/workspace");
        Assert.Equal("/workspace", asked);
        Assert.True(vm.NeedsInputFolder);
    }

    /// <summary>
    /// D-06. The policy chip touches the two canonical roots and nothing else: a third folder
    /// the user bound stays through every switch; «later» empties the two, «inside the team»
    /// rewrites the two — even over a real folder — and «existing folders» empties an in-team
    /// answer so the row offers the picker again.
    /// </summary>
    [Fact]
    public void Switching_the_policy_rewrites_only_the_two_canonical_rows()
    {
        var (vm, _, _) = Build();
        vm.AddTeamMount(new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = "/data/archives", VirtualPath = "/archives",
            Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadOnly,
        });

        Policy(vm, FolderPolicy.ExistingFolders).SelectCommand.Execute(null);
        vm.BindTeamMount("/workspace", new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = "/data/notes", VirtualPath = "/workspace",
            Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadOnly,
        });
        Assert.Equal(["/data/archives:/archives:ro", "/data/notes:/workspace:ro"], vm.TeamMounts);

        Policy(vm, FolderPolicy.InsideTeam).SelectCommand.Execute(null);
        Assert.Equal(["/data/archives:/archives:ro", "./input:/workspace:ro", "./output:/output:rw"], vm.TeamMounts);

        Policy(vm, FolderPolicy.ExistingFolders).SelectCommand.Execute(null);
        Assert.Equal(["/data/archives:/archives:ro"], vm.TeamMounts);
        // The two canonical rows are unanswered again; the folder added at step 1 is the
        // user's own third row (owner review of 2026-09-19), answered, and untouched.
        Assert.Equal(["/workspace", "/output", "/archives"], vm.StepOneRows.Select(r => r.VirtualPath));
        Assert.All(vm.StepOneRows.Where(r => r.HasTitle), r => Assert.True(r.CanChooseFolder));
        Assert.Equal("/data/archives", vm.StepOneRows[2].Folder);

        Policy(vm, FolderPolicy.InsideTeam).SelectCommand.Execute(null);
        Policy(vm, FolderPolicy.Later).SelectCommand.Execute(null);
        Assert.Equal(["/data/archives:/archives:ro"], vm.TeamMounts);
        // «Later» takes the two canonical rows away and leaves the user's own.
        Assert.True(vm.HasStepOneRows);
        Assert.Equal(["/archives"], vm.StepOneRows.Select(r => r.VirtualPath));
    }

    /// <summary>
    /// D-07. Step 1 knows two roots; a blueprint may address a third. Under «inside the team»
    /// the third root is answered the way the first two were, as it appears — the user's
    /// answer, kept for the roots step 1 could not foresee. A root the user dropped stays dropped.
    /// </summary>
    [Fact]
    public async Task Under_inside_team_a_new_deliverable_root_gets_its_own_in_team_row()
    {
        var (vm, processes, _) = Build();
        FillStepOne(vm);
        Policy(vm, FolderPolicy.InsideTeam).SelectCommand.Execute(null);

        processes.OutputToEmit.AddRange([Out(SessionStarted), Out(ReadingWritingBlueprint("/rapports")), Out(Paused)]);
        await Compose(vm);

        Assert.Contains("./rapports:/rapports:rw", vm.TeamMounts);
        var rapports = vm.MountRows.Single(r => r.VirtualPath == "/rapports");
        Assert.True(rapports.IsInsideTeam);
        Assert.True(rapports.IsReadWrite);
        Assert.Equal("inside the team: rapports", rapports.Folder);
        Assert.False(rapports.IsUndeclared);
        // The step-1 /output answer is still there, unclaimed by this blueprint and untouched.
        Assert.Contains("./output:/output:rw", vm.TeamMounts);
        Assert.Equal(["./input:/workspace:ro", "./output:/output:rw", "./rapports:/rapports:rw"], vm.SidecarMounts());

        // Dropping the root sticks: the same blueprint synced again re-answers nothing.
        vm.RemoveTeamMountCommand.Execute("./rapports:/rapports:rw");
        vm.RemoveDerivedMountCommand.Execute("/rapports");
        Assert.DoesNotContain(vm.SidecarMounts(), m => m.EndsWith(":/rapports:rw", StringComparison.Ordinal));
    }

    /// <summary>D-08. The global button answers every row still offering it, in one gesture.</summary>
    [Fact]
    public async Task Create_all_inside_the_team_answers_every_unbound_row()
    {
        var (vm, processes, _) = Build();
        FillStepOne(vm);
        processes.OutputToEmit.AddRange([Out(SessionStarted), Out(ReadingWritingBlueprint()), Out(Paused)]);
        await Compose(vm);

        Assert.True(vm.CreateAllInsideTeamCommand.CanExecute(null));
        Assert.Equal(2, vm.MountRows.Count(r => r.CanCreateInsideTeam));

        vm.CreateAllInsideTeamCommand.Execute(null);

        Assert.Equal(["./input:/workspace:ro", "./output:/output:rw"], vm.TeamMounts);
        Assert.All(vm.MountRows, r => Assert.True(r.IsInsideTeam));
        Assert.All(vm.MountRows, r => Assert.False(r.CanCreateInsideTeam));
        Assert.All(vm.MountRows, r => Assert.False(r.CanChooseFolder));
        // Nothing left to answer: the button greys out rather than pretending.
        Assert.False(vm.CreateAllInsideTeamCommand.CanExecute(null));
    }

    /// <summary>
    /// D-08. The per-row button answers that row only, and a READ root answered inside the team
    /// keeps the input/ warning: the folder it gets is created empty all the same.
    /// </summary>
    [Fact]
    public async Task Create_this_one_inside_the_team_answers_one_row_and_keeps_the_input_warning()
    {
        var (vm, processes, _) = Build();
        FillStepOne(vm);
        processes.OutputToEmit.AddRange([Out(SessionStarted), Out(ReadingWritingBlueprint()), Out(Paused)]);
        await Compose(vm);
        Assert.True(vm.NeedsInputFolder);

        vm.CreateInsideTeamCommand.Execute("/workspace");

        Assert.Equal(["./input:/workspace:ro"], vm.TeamMounts);
        var documents = vm.MountRows.Single(r => r.VirtualPath == "/workspace");
        Assert.True(documents.IsInsideTeam);
        Assert.False(documents.IsReadWrite);
        Assert.Equal("inside the team: input", documents.Folder);
        Assert.Equal("./input:/workspace:ro", documents.MountString);
        // The other row is untouched and still offers both answers.
        var results = vm.MountRows.Single(r => r.VirtualPath == "/output");
        Assert.True(results.CanCreateInsideTeam);
        Assert.True(results.CanChooseFolder);
        // An input/ created at adoption is an empty input/: the warning stays.
        Assert.True(vm.NeedsInputFolder);

        // A real folder behind the read root is what silences it.
        vm.BindTeamMount("/workspace", new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = "/data/notes", VirtualPath = "/workspace",
            Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadOnly,
        });
        Assert.False(vm.NeedsInputFolder);
    }

    /// <summary>
    /// D-08. A folder inside the team is vouched for by that alone, before the folder exists and
    /// before the team does — it never reads red, on a fresh creation or a reopened one; a real
    /// folder the settings do not declare still does.
    /// </summary>
    [Fact]
    public async Task An_in_team_row_never_reads_undeclared()
    {
        var (vm, processes, _) = Build(declaredMounts: () => []);
        FillStepOne(vm);
        Policy(vm, FolderPolicy.InsideTeam).SelectCommand.Execute(null);
        Assert.All(vm.StepOneRows, r => Assert.False(r.IsUndeclared));

        processes.OutputToEmit.AddRange([Out(SessionStarted), Out(ReadingWritingBlueprint()), Out(Paused)]);
        await Compose(vm);

        Assert.All(vm.MountRows, r => Assert.False(r.IsUndeclared));
        Assert.False(vm.HasUndeclaredTeamMounts);

        vm.AddTeamMount(new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = "/elsewhere/archives", VirtualPath = "/archives",
        });
        Assert.True(vm.MountRows.Single(r => r.VirtualPath == "/archives").IsUndeclared);
        Assert.True(vm.HasUndeclaredTeamMounts);
        Assert.Equal(2, vm.MountRows.Count(r => r.IsInsideTeam && !r.IsUndeclared));
    }

    /// <summary>
    /// The adoption writes <c>./input:/workspace:ro</c> / <c>./output:/output:rw</c> into the
    /// sidecar for the in-team answers, and the save creates the folders under the team —
    /// the team is a folder one carries.
    /// </summary>
    [Fact]
    public async Task Adoption_writes_relative_paths_for_in_team_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-wizard-{Guid.NewGuid():N}");
        var promoted = Path.Combine(root, "veille-docs");
        try
        {
            var (vm, processes, _) = Build(teamsRoot: root);
            FillStepOne(vm);
            Policy(vm, FolderPolicy.InsideTeam).SelectCommand.Execute(null);
            processes.OutputToEmit.AddRange(
            [
                Out(SessionStarted),
                Out(ReadingWritingBlueprint()),
                Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            await Compose(vm);
            vm.TeamName = "Veille docs";
            Assert.True(vm.CanSaveTeam);

            processes.OutputToEmit.Clear();
            processes.OutputToEmit.AddRange(
            [
                Out($$"""{"v":2,"seq":1,"ts":"t","kind":"promoted","path":{{System.Text.Json.JsonSerializer.Serialize(promoted)}},"launcher":"run.sh"}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            await vm.SaveTeamCommand.ExecuteAsync();

            var team = TeamCatalog.Describe(promoted);
            Assert.Equal(["./input:/workspace:ro", "./output:/output:rw"], team.Metadata!.Mounts);
            Assert.Equal(
                [$"{Path.Combine(promoted, "input")}:/workspace:ro", $"{Path.Combine(promoted, "output")}:/output:rw"],
                team.Mounts);
            Assert.True(Directory.Exists(Path.Combine(promoted, "input")));
            Assert.True(Directory.Exists(Path.Combine(promoted, "output")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(string Root, string TeamDir, string SessionDir)> WriteReopenableTeam(string mountsJson)
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-wiz-reopen-" + Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(root, ".orkeon", "forge", "veille");
        var teamDir = Path.Combine(root, "teams", "veille-docs");
        Directory.CreateDirectory(sessionDir);
        Directory.CreateDirectory(teamDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "session.json"),
            $$"""{"v":1,"slug":"veille","title":"Veille","format":"yaml","state":"Promoted","status":"Promoted","promotedTo":{{System.Text.Json.JsonSerializer.Serialize(teamDir)}}}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "blueprint.json"),
            """{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_read","file_write"]}],"tasks":[{"key":"t","description":"d","agent":"a","deliverable":"/output/rapport.md"}]}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(teamDir, "studio-team.json"),
            $$"""{"name":"Veille docs","description":"le besoin","profile":"Local","mounts":{{mountsJson}}}""", TestContext.Current.CancellationToken);
        return (root, teamDir, sessionDir);
    }

    private static ForgeSolutionSummary ReopenedSession(string sessionDir, string teamDir) => new()
    {
        Slug = "veille", State = "Promoted", Status = "Promoted",
        Directory = sessionDir, PromotedTo = teamDir,
    };

    /// <summary>
    /// D-07. «Modifier» seeds the rows from the sidecar's OWN spelling: a team-relative entry
    /// reads «inside the team», never red, and is what the re-adoption writes back as it is.
    /// </summary>
    [Fact]
    public async Task A_reopened_teams_relative_folders_read_as_inside_the_team()
    {
        var (root, teamDir, sessionDir) = await WriteReopenableTeam("""["./input:/workspace:ro","./output:/output:rw","/data/docs:/docs:ro"]""");
        try
        {
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"));
            processes.OutputToEmit.AddRange(
            [
                Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"SESSION","format":"yaml","resumed":true}""".Replace("SESSION", System.Text.Json.JsonSerializer.Serialize(sessionDir).Trim('"'), StringComparison.Ordinal)),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);

            await vm.ReopenTeamAsync(TeamCatalog.Describe(teamDir), ReopenedSession(sessionDir, teamDir));

            Assert.Equal(["./input:/workspace:ro", "./output:/output:rw", "/data/docs:/docs:ro"], vm.TeamMounts);
            var documents = vm.MountRows.Single(r => r.VirtualPath == "/workspace");
            Assert.True(documents.IsInsideTeam);
            Assert.False(documents.IsUndeclared);
            Assert.Equal("inside the team: input", documents.Folder);
            Assert.Equal("./input:/workspace:ro", documents.MountString);
            Assert.True(vm.MountRows.Single(r => r.VirtualPath == "/output").IsInsideTeam);
            // The real folder outside the team keeps its own verdict.
            var docs = vm.MountRows.Single(r => r.VirtualPath == "/docs");
            Assert.False(docs.IsInsideTeam);
            Assert.True(docs.IsUndeclared);
            Assert.Equal("/data/docs", docs.Folder);
            // Reopened: the read root is inside a team that EXISTS, so the trial has a folder.
            Assert.False(vm.TrialReadsInsideTeam);
            Assert.Equal(FolderPolicy.Later, vm.FolderPolicy);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// D-09 (P-2). The folder step 1 bound behind <c>/workspace</c> is what the trial reads:
    /// the compose and the resume both carry <c>--read</c> with it.
    /// </summary>
    [Fact]
    public async Task The_trial_reads_the_folder_bound_at_step_one()
    {
        var (vm, processes, _) = Build();
        FillStepOne(vm);
        Policy(vm, FolderPolicy.ExistingFolders).SelectCommand.Execute(null);
        vm.BindTeamMount("/workspace", new Orkeon.Studio.Core.FileSystem.MountDefinition
        {
            PhysicalPath = "/data/notes", VirtualPath = "/workspace",
            Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadOnly,
        });

        processes.OutputToEmit.AddRange([Out(SessionStarted), Out(ReadingWritingBlueprint()), Out(Paused)]);
        await Compose(vm);

        var compose = processes.Requests[0].Arguments.ToList();
        Assert.Equal("/data/notes", compose[compose.IndexOf("--read") + 1]);
        Assert.Contains("--dry", compose);

        processes.OutputToEmit.Clear();
        processes.OutputToEmit.Add(Out(Paused));
        await vm.TryTeamCommand.ExecuteAsync();

        Assert.Equal(["forge", "resume", "veille", "--events", "jsonl", "--read", "/data/notes"], processes.Requests[^1].Arguments);
        Assert.False(vm.TrialReadsInsideTeam);
    }

    /// <summary>
    /// D-09. A reopened team's <c>./input</c> resolves under the team folder, which exists —
    /// the re-try reads the documents dropped there since the adoption.
    /// </summary>
    [Fact]
    public async Task A_reopened_team_retries_against_its_own_input_folder()
    {
        var (root, teamDir, sessionDir) = await WriteReopenableTeam("""["./input:/workspace:ro","./output:/output:rw"]""");
        try
        {
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"));
            processes.OutputToEmit.Add(Out("""{"v":2,"seq":1,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""));

            await vm.ReopenTeamAsync(TeamCatalog.Describe(teamDir), ReopenedSession(sessionDir, teamDir));

            Assert.Equal(
                ["forge", "resume", "veille", "--events", "jsonl", "--read", Path.Combine(teamDir, "input")],
                processes.Requests[0].Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>D-07. «Recommencer» forgets the two answers and the chip behind them.</summary>
    [Fact]
    public async Task Restart_forgets_the_step_one_folders_and_the_policy()
    {
        var (vm, processes, _) = Build();
        FillStepOne(vm);
        Policy(vm, FolderPolicy.InsideTeam).SelectCommand.Execute(null);
        // A mount point the user named goes with the creation being abandoned too.
        vm.NewRootName = "archives";
        vm.AddNamedRootCommand.Execute(null);
        processes.OutputToEmit.AddRange([Out(SessionStarted), Out(ReadingWritingBlueprint()), Out(Paused)]);
        await Compose(vm);
        Assert.Equal(["./input:/workspace:ro", "./output:/output:rw", "./archives:/archives:ro"], vm.TeamMounts);
        Assert.True(vm.RestartCommand.CanExecute(null));

        vm.RestartCommand.Execute(null);

        Assert.Empty(vm.TeamMounts);
        Assert.Empty(vm.NamedRoots);
        Assert.Equal(FolderPolicy.Later, vm.FolderPolicy);
        Assert.True(Policy(vm, FolderPolicy.Later).IsSelected);
        Assert.False(vm.HasStepOneRows);
    }

    /// <summary>
    /// Owner review of 2026-09-19: the two canonical rows were a start, not a limit. A team
    /// addresses as many mount points as its need names, and step 1 takes them by name — a
    /// row each, answered like the canonical ones, kept across the compose.
    /// </summary>
    [Fact]
    public async Task Any_number_of_folders_can_be_named_at_step_one_and_they_survive_the_compose()
    {
        var (vm, processes, _) = Build();
        FillStepOne(vm);
        Policy(vm, FolderPolicy.ExistingFolders).SelectCommand.Execute(null);

        // Two more mount points, typed as the user types them: a slash, a capital, a space.
        vm.NewRootName = " /Factures ";
        Assert.True(vm.CanAddNamedRoot);
        vm.AddNamedRootCommand.Execute(null);
        vm.NewRootName = "rapports";
        vm.NewRootIsReadWrite = true;
        vm.AddNamedRootCommand.Execute(null);

        Assert.Equal("", vm.NewRootName);
        Assert.Equal(["/factures", "/rapports"], vm.NamedRoots);
        var rows = vm.StepOneRows;
        Assert.Equal(["/workspace", "/output", "/factures", "/rapports"], rows.Select(r => r.VirtualPath));
        var factures = rows[2];
        Assert.False(factures.HasTitle);
        Assert.False(factures.HasFolder);
        Assert.False(factures.IsReadWrite);
        Assert.True(factures.CanChooseFolder);
        Assert.True(factures.CanCreateInsideTeam);
        Assert.True(factures.IsDroppable);
        Assert.True(rows[3].IsReadWrite);
        Assert.True(vm.CanCompose);

        // Answered the two ways the canonical rows are: a real folder, a folder inside the team.
        vm.BindTeamMount("/factures", new Orkeon.Studio.Core.FileSystem.MountDefinition { PhysicalPath = "/data/factures", VirtualPath = "/factures", Rights = Orkeon.Studio.Core.FileSystem.MountRights.ReadOnly });
        vm.CreateInsideTeamCommand.Execute("/rapports");
        Assert.Equal(["/data/factures:/factures:ro", "./rapports:/rapports:rw"], vm.TeamMounts);
        Assert.True(vm.StepOneRows[3].IsInsideTeam);

        processes.OutputToEmit.AddRange([Out(SessionStarted), Out(ReadingWritingBlueprint()), Out(Paused)]);
        await Compose(vm);

        // Both answers survived the compose, next to the blueprint's own roots.
        Assert.Equal(2, vm.Step);
        Assert.Equal(["/data/factures:/factures:ro", "./rapports:/rapports:rw"], vm.TeamMounts);
        Assert.Contains(vm.MountRows, r => r.VirtualPath == "/factures" && r.Folder == "/data/factures");
        Assert.Contains(vm.MountRows, r => r.VirtualPath == "/rapports" && r.IsInsideTeam);
        Assert.Equal(["/factures", "/rapports"], vm.NamedRoots);
    }

    /// <summary>Under «Created inside the team» a newly named mount point is answered inside at once.</summary>
    [Fact]
    public void A_folder_named_under_inside_the_team_is_answered_inside_at_once()
    {
        var (vm, _, _) = Build();
        FillStepOne(vm);
        Policy(vm, FolderPolicy.InsideTeam).SelectCommand.Execute(null);

        vm.NewRootName = "archives";
        vm.AddNamedRootCommand.Execute(null);

        Assert.Equal(["./input:/workspace:ro", "./output:/output:rw", "./archives:/archives:ro"], vm.TeamMounts);
        Assert.All(vm.StepOneRows, r => Assert.True(r.IsInsideTeam));
        Assert.Equal("inside the team: archives", vm.StepOneRows[2].Folder);
        Assert.False(vm.HasUndeclaredTeamMounts);
    }

    /// <summary>
    /// A named mount point left unanswered is a question the Composer step still shows, and
    /// adoption answers it the way it answers a derived root: its own folder inside the team.
    /// </summary>
    [Fact]
    public async Task A_named_folder_left_unanswered_stays_a_row_and_is_created_inside_the_team_at_adoption()
    {
        var (vm, processes, _) = Build();
        FillStepOne(vm);
        Policy(vm, FolderPolicy.ExistingFolders).SelectCommand.Execute(null);
        vm.NewRootName = "rapports";
        vm.NewRootIsReadWrite = true;
        vm.AddNamedRootCommand.Execute(null);

        processes.OutputToEmit.AddRange([Out(SessionStarted), Out(ReadingWritingBlueprint()), Out(Paused)]);
        await Compose(vm);

        var row = Assert.Single(vm.MountRows, r => r.VirtualPath == "/rapports");
        Assert.False(row.HasFolder);
        Assert.True(row.CanCreateInsideTeam);
        Assert.Contains("./rapports:/rapports:rw", vm.SidecarMounts());
    }

    /// <summary>An empty, reserved, malformed or already-asked name cannot be added.</summary>
    [Fact]
    public void A_folder_name_that_is_empty_reserved_malformed_or_already_a_row_cannot_be_added()
    {
        var (vm, _, _) = Build();
        FillStepOne(vm);
        Policy(vm, FolderPolicy.ExistingFolders).SelectCommand.Execute(null);

        foreach (var name in new[] { "", "   ", "/", "crew", "llm-logs", "output", "Workspace", "mes factures", "a:b", "x/y" })
        {
            vm.NewRootName = name;
            Assert.False(vm.CanAddNamedRoot, name);
            Assert.False(vm.AddNamedRootCommand.CanExecute(null), name);
        }

        vm.NewRootName = "factures";
        vm.AddNamedRootCommand.Execute(null);
        vm.NewRootName = "/Factures/";
        Assert.False(vm.CanAddNamedRoot);
        Assert.Equal(["/factures"], vm.NamedRoots);
    }

    /// <summary>
    /// The ✕ of an unanswered named row forgets the mount point; the ✕ of its answer keeps
    /// the question; «Later» hides the canonical rows and never a named one (« Restart »
    /// forgets them all — asserted with the step-1 folders above).
    /// </summary>
    [Fact]
    public void Dropping_a_named_folder_forgets_it_and_later_keeps_it_visible()
    {
        var (vm, _, _) = Build();
        FillStepOne(vm);
        Policy(vm, FolderPolicy.ExistingFolders).SelectCommand.Execute(null);
        vm.NewRootName = "factures";
        vm.AddNamedRootCommand.Execute(null);
        vm.NewRootName = "rapports";
        vm.AddNamedRootCommand.Execute(null);
        vm.CreateInsideTeamCommand.Execute("/rapports");

        // The answer goes, the question stays.
        vm.RemoveTeamMountCommand.Execute("./rapports:/rapports:ro");
        Assert.Empty(vm.TeamMounts);
        Assert.Equal(["/factures", "/rapports"], vm.NamedRoots);
        Assert.Equal(4, vm.StepOneRows.Count);

        // The question goes.
        vm.RemoveDerivedMountCommand.Execute("/factures");
        Assert.Equal(["/rapports"], vm.NamedRoots);
        Assert.Equal(["/workspace", "/output", "/rapports"], vm.StepOneRows.Select(r => r.VirtualPath));

        // «Later» hides the two canonical rows, never a named one.
        Policy(vm, FolderPolicy.Later).SelectCommand.Execute(null);
        Assert.True(vm.HasStepOneRows);
        Assert.Equal(["/rapports"], vm.StepOneRows.Select(r => r.VirtualPath));
        Assert.Empty(vm.TeamMounts);
    }

    /// <summary>
    /// D-07. «Modifier» on a card opens ANOTHER creation: the step-1 answers of the one under
    /// way must not leak into the sidecar of the team the user came to edit — nor its policy,
    /// which would answer that team's new roots on its own.
    /// </summary>
    [Fact]
    public async Task Reopening_a_team_drops_the_step_one_folders_of_the_creation_under_way()
    {
        var (root, teamDir, sessionDir) = await WriteReopenableTeam("""["/data/docs:/docs:ro"]""");
        try
        {
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"));
            FillStepOne(vm);
            Policy(vm, FolderPolicy.InsideTeam).SelectCommand.Execute(null);
            Assert.Equal(["./input:/workspace:ro", "./output:/output:rw"], vm.TeamMounts);
            processes.OutputToEmit.Add(Out("""{"v":2,"seq":1,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""));

            await vm.ReopenTeamAsync(TeamCatalog.Describe(teamDir), ReopenedSession(sessionDir, teamDir));

            Assert.Equal(["/data/docs:/docs:ro"], vm.TeamMounts);
            Assert.Equal(FolderPolicy.Later, vm.FolderPolicy);
            // The reopened team's own derived roots were NOT answered by the stale policy.
            Assert.Equal(["/data/docs:/docs:ro", "./input:/workspace:ro", "./output:/output:rw"], vm.SidecarMounts());
            Assert.Contains(vm.MountRows, r => r is { VirtualPath: "/workspace", IsBound: false });
            // No step-1 folder, no --read: the argv is the one the golden test pins.
            Assert.Equal(["forge", "resume", "veille", "--events", "jsonl"], processes.Requests[0].Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── D-15: « Open the folder » ──

    /// <summary>
    /// The folder exists as soon as the engine answered — the session, which holds the generated
    /// crew/ — and there is none once the team is adopted: the tunnel ended on a blank wizard
    /// (STUDIO-20), and the team's folder is « Open » on its card. A reopened team is the other
    /// case, in the next test.
    /// </summary>
    [Fact]
    public async Task The_open_folder_button_targets_the_session_before_adoption_and_goes_quiet_after()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-wizard-{Guid.NewGuid():N}");
        var promoted = Path.Combine(root, "veille-docs");
        try
        {
            var opener = new RecordingShellOpener();
            var (vm, processes, _) = Build(teamsRoot: root, shellOpener: opener);
            Assert.True(vm.HasShellOpener);
            Assert.False(vm.CanOpenFolder);

            processes.OutputToEmit.AddRange(
            [
                Out(SessionStarted),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            FillStepOne(vm);
            await Compose(vm);

            Assert.True(vm.CanOpenFolder);
            Assert.True(vm.OpenFolderCommand.CanExecute(null));
            Assert.Equal("Opens the working session — the folder the assistant is writing in.", vm.OpenFolderTooltip);
            vm.OpenFolderCommand.Execute(null);
            Assert.Equal(["/ws/.orkeon/forge/veille"], opener.Opened);

            vm.TeamName = "Veille docs";
            processes.OutputToEmit.Clear();
            processes.OutputToEmit.Add(Out($$"""{"v":2,"seq":1,"ts":"t","kind":"promoted","path":{{System.Text.Json.JsonSerializer.Serialize(promoted)}},"launcher":"run.sh"}"""));
            await vm.SaveTeamCommand.ExecuteAsync();

            Assert.False(vm.CanOpenFolder);
            Assert.False(vm.OpenFolderCommand.CanExecute(null));
            vm.OpenFolderCommand.Execute(null);
            Assert.Equal(["/ws/.orkeon/forge/veille"], opener.Opened);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A team reopened from its card is the folder the button opens, and the tooltip says so.</summary>
    [Fact]
    public async Task The_open_folder_button_targets_the_reopened_team()
    {
        var (root, teamDir, sessionDir) = await WriteReopenableTeam("""["/data/docs:/docs:ro"]""");
        try
        {
            var opener = new RecordingShellOpener();
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"), shellOpener: opener);
            processes.OutputToEmit.Add(Out("""{"v":2,"seq":1,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""));

            await vm.ReopenTeamAsync(TeamCatalog.Describe(teamDir), ReopenedSession(sessionDir, teamDir));

            Assert.True(vm.CanOpenFolder);
            Assert.Equal("Opens the folder of the adopted team.", vm.OpenFolderTooltip);
            vm.OpenFolderCommand.Execute(null);
            Assert.Equal([teamDir], opener.Opened);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Without_a_session_there_is_no_folder_to_open()
    {
        var opener = new RecordingShellOpener();
        var (vm, _, _) = Build(shellOpener: opener);
        FillStepOne(vm);

        Assert.True(vm.HasShellOpener);
        Assert.False(vm.CanOpenFolder);
        Assert.False(vm.OpenFolderCommand.CanExecute(null));
        vm.OpenFolderCommand.Execute(null);
        Assert.Empty(opener.Opened);
    }

    [Fact]
    public async Task Without_a_shell_opener_the_button_is_absent()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange([Out(SessionStarted), Out(Paused)]);
        FillStepOne(vm);
        await Compose(vm);

        Assert.NotNull(vm.SessionDirectory);
        Assert.False(vm.HasShellOpener);
        Assert.False(vm.CanOpenFolder);
        Assert.False(vm.OpenFolderCommand.CanExecute(null));
    }

    // ── D-10, at the shell: the OS folder dialog that declares on the way (STUDIO-19) ──

    private static (MainWindowViewModel Shell, FakeAppSettingsStore Store, FakePathPicker Picker) Shell(
        FakeDirectoryProbe directories, string? teamsRoot = null,
        FakeProcessLauncher? forge = null, string? forgeWorkspace = null)
    {
        var store = new FakeAppSettingsStore();
        var picker = new FakePathPicker();
        var shell = new MainWindowViewModel(
            new StudioServices
            {
                SettingsStore = store,
                Directories = directories,
                TargetProbe = new FakeTargetProbe(),
                Picker = picker,
                ProcessRunner = new OrkeonProcessRunner(
                    new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
                ForgeClient = new ForgeClient(
                    forge ?? new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                ProfileStore = new InMemoryModelProfileStore(),
                LlmProbe = new FakeLlmEndpointProbe(),
                KeyStore = new FakeApiKeyStore(),
            },
            globalPathOverride: "/home/user/.config/Orkeon/appsettings.json",
            forgeWorkspace: forgeWorkspace ?? "/ws",
            teamsRoot: teamsRoot ?? "/nowhere/teams");
        return (shell, store, picker);
    }

    /// <summary>
    /// STUDIO-20, through the shell: an adoption ends the tunnel on a blank step 1, and the session
    /// listed under « Sessions in progress » a moment earlier is gone from My teams — the engine
    /// wrote it Promoted before saying so, and the shell re-read the catalogs on the adoption. The
    /// session stays on disk: it is what keeps « Modify » alive on the new card.
    /// </summary>
    [Fact]
    public async Task Adopting_ends_the_tunnel_and_the_session_leaves_my_teams()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-wizard-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "forge");
        var teams = Path.Combine(root, "teams");
        var sessionDir = Path.Combine(workspace, ".orkeon", "forge", "veille");
        var promoted = Path.Combine(teams, "ma-veille");
        try
        {
            var forge = new FakeProcessLauncher();
            var (shell, _, _) = Shell(new FakeDirectoryProbe(), teamsRoot: teams, forge: forge, forgeWorkspace: workspace);
            shell.Settings.Profiles.CommitEdit(
                new ModelProfile { Name = "Local", Provider = "Ollama", Model = "qwen2.5:14b", BaseUrl = "http://localhost:11434/v1" },
                previousName: null);
            shell.Settings.Profiles.StudioProfileName = "Local";
            var wizard = shell.CreateTeam;

            // The composition: the engine creates its session directory and parks it Ready.
            forge.WhileRunning = () => WriteEngineSession(sessionDir, "Ready", promotedTo: null);
            forge.OutputToEmit.AddRange(
            [
                Out($$"""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":{{System.Text.Json.JsonSerializer.Serialize(sessionDir)}},"format":"yaml","resumed":false}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            wizard.Need = "une veille documentaire";
            wizard.FrequencyChoices[1].SelectCommand.Execute(null);
            wizard.SourceChoices[0].SelectCommand.Execute(null);
            wizard.OutputChoices[0].SelectCommand.Execute(null);
            await wizard.ComposeCommand.ExecuteAsync();
            Assert.Equal(4, wizard.Step);
            shell.Teams.Refresh();
            Assert.Equal("Veille", Assert.Single(shell.Teams.InProgress).Title);
            Assert.Empty(shell.Teams.Teams);

            // The adoption: the engine writes the session Promoted before it says `promoted`
            // (ForgeCommand.PromoteAsync), and Studio re-reads the catalogs on the event.
            wizard.TeamName = "Ma veille";
            forge.WhileRunning = () => WriteEngineSession(sessionDir, "Promoted", promotedTo: promoted);
            forge.OutputToEmit.Clear();
            forge.OutputToEmit.AddRange(
            [
                Out($$"""{"v":2,"seq":1,"ts":"t","kind":"promoted","path":{{System.Text.Json.JsonSerializer.Serialize(promoted)}},"launcher":"run.sh"}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            await wizard.SaveTeamCommand.ExecuteAsync();

            Assert.Equal(1, wizard.Step);
            Assert.False(wizard.HasDraft);
            // No refresh from the test: the shell did it on the adoption.
            Assert.Empty(shell.Teams.InProgress);
            var card = Assert.Single(shell.Teams.Teams);
            Assert.Equal("Ma veille", card.Name);
            Assert.True(card.CanModify);
            Assert.True(File.Exists(Path.Combine(sessionDir, "session.json")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The same report, through the shell: the session « Resume » opened the wizard on is
    /// discarded under « Sessions in progress » — My teams stops listing it AND the wizard is
    /// a blank step 1 again, with no refresh and no gesture from the test in between.
    /// </summary>
    [Fact]
    public async Task Discarding_the_resumed_session_in_my_teams_resets_the_wizard_too()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-wizard-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "forge");
        var sessionDir = Path.Combine(workspace, ".orkeon", "forge", "veille");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "session.json"),
            """{"v":1,"slug":"veille","title":"Veille","format":"yaml","state":"Test","status":"Active"}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "blueprint.json"),
            """{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":[]}],"tasks":[{"key":"t","description":"d","agent":"a"}]}""", TestContext.Current.CancellationToken);
        try
        {
            var (shell, _, _) = Shell(new FakeDirectoryProbe(), teamsRoot: Path.Combine(root, "teams"), forgeWorkspace: workspace);
            shell.Teams.Refresh();
            var session = Assert.Single(shell.Teams.InProgress);

            // The dry pause resumes without an engine: the wizard is open on it at once.
            session.ResumeCommand.Execute(null);
            Assert.Equal("veille", shell.CreateTeam.SessionSlug);
            Assert.True(shell.CreateTeam.HasDraft);

            session.AskDeleteCommand.Execute(null);
            session.ConfirmDeleteCommand.Execute(null);

            Assert.False(Directory.Exists(sessionDir));
            Assert.Empty(shell.Teams.InProgress);
            Assert.Equal(1, shell.CreateTeam.Step);
            Assert.False(shell.CreateTeam.HasDraft);
            Assert.Null(shell.CreateTeam.SessionSlug);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The engine's own write of <c>session.json</c>, in the shape the catalog reads.</summary>
    private static void WriteEngineSession(string sessionDir, string status, string? promotedTo)
    {
        Directory.CreateDirectory(sessionDir);
        var promotedField = promotedTo is null
            ? ""
            : $$""","promotedTo":{{System.Text.Json.JsonSerializer.Serialize(promotedTo)}}""";
        File.WriteAllText(
            Path.Combine(sessionDir, "session.json"),
            $$"""{"v":1,"slug":"veille","title":"Veille","format":"yaml","state":"{{status}}","status":"{{status}}"{{promotedField}}}""");
    }

    private static string SelectFolderTitle => EnglishStudioStrings.Instance[StudioStringKeys.DialogSelectMountFolder];

    /// <summary>
    /// P-3. One gesture: the wizard's « Choose the folder… » under «existing folders» opens the
    /// OS folder dialog; the pick is declared in the settings under the row's rights, saved,
    /// and bound behind the row — and the status line says so. No in-app modal in between.
    /// </summary>
    [Fact]
    public void Picking_a_folder_from_the_wizard_declares_it_in_the_settings_saves_and_binds_it()
    {
        var (shell, store, picker) = Shell(new FakeDirectoryProbe("/data/factures"));
        picker.FolderToReturn = "/data/factures";
        var wizard = shell.CreateTeam;
        wizard.FolderPolicy = FolderPolicy.ExistingFolders;

        wizard.BindMountCommand.Execute("/output");

        // The OS dialog, not the declared list.
        Assert.Equal([SelectFolderTitle], picker.Prompts);
        Assert.False(shell.AllowedFolders.IsOpen);

        // Declared under the ROW's root, with an id of its own (VFS-90, D-01): the team names
        // the declaration, so the declaration carries the name the agents use.
        var declared = Assert.Single(shell.Config.Mounts.CurrentMountStrings);
        var entry = Orkeon.Studio.Core.FileSystem.MountDefinition.Parse(declared);
        Assert.NotNull(entry.Id);
        Assert.Equal("/data/factures:/output:rw", entry.WithoutId().ToMountString());
        // Written to the settings file — by the novice auto-save on the edit and by the
        // explicit save behind the pick; the second, identical write is what makes the
        // outcome true whatever the auto-save's timing.
        Assert.NotEmpty(store.SavedPaths);
        Assert.All(store.SavedPaths, path => Assert.Equal("/home/user/.config/Orkeon/appsettings.json", path));
        Assert.Contains("/data/factures:/output:rw", store.LastSavedJson, StringComparison.Ordinal);
        // And the team binds that very entry, verbatim — id included.
        Assert.Equal([declared], wizard.TeamMounts);
        var row = wizard.StepOneRows.Single(r => r.VirtualPath == "/output");
        Assert.Equal("/data/factures", row.Folder);
        Assert.False(row.IsUndeclared);
        Assert.Equal(entry.ShortId, row.ShortId);
        Assert.Equal("“factures” authorized and bound as /output", wizard.StatusMessage);
    }

    /// <summary>
    /// VFS-90 D-01: a folder the settings hold under ANOTHER root gets a second declaration
    /// under the row's root — the entry is authoritative, and a team names it rather than
    /// re-spelling it. Two entries on one folder, told apart by their ids.
    /// </summary>
    [Fact]
    public void A_folder_declared_under_another_root_gets_a_second_declaration_under_the_rows_root()
    {
        var (shell, store, picker) = Shell(new FakeDirectoryProbe("/data/factures"));
        picker.FolderToReturn = "/data/factures";
        shell.Config.Mounts.Load(["/data/factures:/factures:ro"]);
        var wizard = shell.CreateTeam;
        wizard.FolderPolicy = FolderPolicy.ExistingFolders;

        wizard.BindMountCommand.Execute("/output");

        var entries = shell.Config.Mounts.CurrentMountStrings
            .Select(Orkeon.Studio.Core.FileSystem.MountDefinition.Parse).ToList();
        Assert.Equal(["/data/factures:/factures:ro", "/data/factures:/output:rw"], entries.Select(e => e.WithoutId().ToMountString()));
        Assert.All(entries, e => Assert.NotNull(e.Id));
        Assert.NotEqual(entries[0].Id, entries[1].Id);
        Assert.NotEmpty(store.SavedPaths);
        Assert.Equal([entries[1].ToMountString()], wizard.TeamMounts);
        Assert.False(wizard.StepOneRows.Single(r => r.VirtualPath == "/output").IsUndeclared);
    }

    /// <summary>
    /// A folder the settings already hold under the row's root, with the row's rights, is
    /// reused — given an id if it had none — never declared twice; the status line says so.
    /// </summary>
    [Fact]
    public void A_folder_already_declared_under_the_rows_root_is_reused_not_declared_twice()
    {
        var (shell, store, picker) = Shell(new FakeDirectoryProbe("/data/factures"));
        picker.FolderToReturn = "/data/factures";
        shell.Config.Mounts.Load(["/data/factures:/output:rw"]);
        var wizard = shell.CreateTeam;
        wizard.FolderPolicy = FolderPolicy.ExistingFolders;

        wizard.BindMountCommand.Execute("/output");

        var declared = Assert.Single(shell.Config.Mounts.CurrentMountStrings);
        var entry = Orkeon.Studio.Core.FileSystem.MountDefinition.Parse(declared);
        Assert.NotNull(entry.Id);
        Assert.Equal("/data/factures:/output:rw", entry.WithoutId().ToMountString());
        Assert.NotEmpty(store.SavedPaths);
        Assert.Equal([declared], wizard.TeamMounts);
        Assert.Equal("“factures” was already authorized; the team now uses that entry", wizard.StatusMessage);
    }

    /// <summary>
    /// A folder inside the reopened team is the team's own: bound, never declared — the save
    /// relativizes it, and the settings never learn one team's private folders.
    /// </summary>
    [Fact]
    public async Task A_folder_inside_the_reopened_team_is_bound_without_being_declared()
    {
        var (root, teamDir, sessionDir) = await WriteReopenableTeam("""["/data/docs:/docs:ro"]""");
        try
        {
            var input = Path.Combine(teamDir, "input");
            var (shell, store, picker) = Shell(new FakeDirectoryProbe(teamDir, input), teamsRoot: Path.Combine(root, "teams"));
            picker.FolderToReturn = input;
            var wizard = shell.CreateTeam;
            await wizard.ReopenTeamAsync(TeamCatalog.Describe(teamDir), ReopenedSession(sessionDir, teamDir));

            wizard.PickFolderCommand.Execute("/workspace");

            Assert.Equal([SelectFolderTitle], picker.Prompts);
            Assert.Empty(shell.Config.Mounts.CurrentMountStrings);
            Assert.Empty(store.SavedPaths);
            Assert.Contains($"{input}:/workspace:ro", wizard.TeamMounts);
            var row = wizard.MountRows.Single(r => r.VirtualPath == "/workspace");
            Assert.True(row.IsInsideTeam);
            Assert.False(row.IsUndeclared);
            Assert.Equal("inside the team: input", row.Folder);
            // And the sidecar spelling is the relative one, wherever the team goes.
            Assert.Contains("./input:/workspace:ro", TeamMountPaths.RelativizeAll(teamDir, wizard.SidecarMounts()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A settings save the machine refuses does not lose the pick: the folder is bound, the
    /// editor's live list vouches for it, and the sentence is the only trace of the refusal.
    /// </summary>
    [Fact]
    public void A_refused_settings_save_still_binds_and_says_so()
    {
        var (shell, store, picker) = Shell(new FakeDirectoryProbe("/data/factures"));
        picker.FolderToReturn = "/data/factures";
        store.SaveFault = new IOException("disk full");
        var wizard = shell.CreateTeam;
        wizard.FolderPolicy = FolderPolicy.ExistingFolders;

        wizard.BindMountCommand.Execute("/workspace");

        var declared = Assert.Single(shell.Config.Mounts.CurrentMountStrings);
        Assert.Equal("/data/factures:/workspace:ro", Orkeon.Studio.Core.FileSystem.MountDefinition.Parse(declared).WithoutId().ToMountString());
        Assert.Empty(store.SavedPaths);
        Assert.Equal([declared], wizard.TeamMounts);
        Assert.False(wizard.StepOneRows.Single(r => r.VirtualPath == "/workspace").IsUndeclared);
        Assert.StartsWith("“factures” added, but the settings could not be saved — ", wizard.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("disk full", wizard.StatusMessage, StringComparison.Ordinal);
    }

    /// <summary>A cancelled OS dialog leaves everything as it was, and says nothing.</summary>
    [Fact]
    public void A_cancelled_os_dialog_declares_nothing_binds_nothing_and_says_nothing()
    {
        var (shell, store, picker) = Shell(new FakeDirectoryProbe("/data/factures"));
        picker.FolderToReturn = null;
        var wizard = shell.CreateTeam;
        wizard.FolderPolicy = FolderPolicy.ExistingFolders;
        var statusBefore = wizard.StatusMessage;

        wizard.BindMountCommand.Execute("/output");

        Assert.Single(picker.Prompts);
        Assert.Empty(shell.Config.Mounts.CurrentMountStrings);
        Assert.Empty(store.SavedPaths);
        Assert.Empty(wizard.TeamMounts);
        Assert.True(wizard.StepOneRows.Single(r => r.VirtualPath == "/output").CanChooseFolder);
        Assert.Equal(statusBefore, wizard.StatusMessage);
    }

    // ── STUDIO-13: the failures of "Compose the team" are said, with the technical part copyable ──

    private static ProcessOutputLine Err(string text) =>
        ProcessOutputLine.Now(ProcessOutputChannel.StandardError, text);

    /// <summary>
    /// The owner's screenshot: no CLI, a click, a spinner, then step 1 again with nothing but
    /// a grey truncated line. The card says the engine is missing, in the user's language,
    /// keeps the locator's own text raw, and the report a novice pastes carries it whole.
    /// </summary>
    [Fact]
    public async Task A_missing_engine_is_said_on_step_1_with_a_copyable_report()
    {
        var (vm, processes, _) = Build(cliInstalled: false);
        FillStepOne(vm);

        await Compose(vm);

        Assert.Empty(processes.Requests);   // nothing was ever spawned
        Assert.True(vm.HasFailure);
        Assert.Equal(WizardFailureKind.EngineMissing, vm.Failure!.Kind);
        Assert.Equal("The orkeon engine was not found on this machine.", vm.Failure.Headline);
        Assert.Contains("was not located on this machine", vm.Failure.Detail, StringComparison.Ordinal);
        Assert.Null(vm.Failure.ExitCode);
        Assert.True(vm.CanCopyFailureReport);
        Assert.Contains("was not located on this machine", vm.BuildFailureReport(), StringComparison.Ordinal);
        Assert.Contains("orkeon forge", vm.BuildFailureReport(), StringComparison.Ordinal);
        // The status line carries the sentence, not the locator's paragraph; the user stays
        // on step 1 and knows why; the ways out are the diagnostic and a retry.
        Assert.Equal(vm.Failure.Headline, vm.StatusMessage);
        Assert.Equal(1, vm.Step);
        Assert.False(vm.IsEngineRunning);
        Assert.True(vm.FailureOffersDiagnostic);
        Assert.False(vm.FailureOffersSettings);
        Assert.True(vm.FailureOffersRetry);
        Assert.Same(vm.ComposeCommand, vm.FailureRetryCommand);
    }

    /// <summary>
    /// Exit 1 with no line on either channel used to produce nothing at all: SyncFromModel
    /// only repainted the failed sentence on a `session.finished {failed}` that never came.
    /// </summary>
    [Fact]
    public async Task A_non_zero_exit_without_stderr_still_shows_a_failure_card()
    {
        var (vm, processes, _) = Build();
        processes.ExitCode = 1;
        FillStepOne(vm);

        await Compose(vm);

        Assert.True(vm.HasFailure);
        Assert.Equal(WizardFailureKind.EngineStopped, vm.Failure!.Kind);
        Assert.Equal("The engine stopped (exit code 1).", vm.Failure.Headline);
        Assert.Equal(1, vm.Failure.ExitCode);
        Assert.Equal("", vm.Failure.Stderr);
        // With nothing on stderr, the exit-code description is the technical detail.
        Assert.Contains("exit code 1", vm.Failure.Detail, StringComparison.Ordinal);
        Assert.Equal(vm.Failure.Headline, vm.StatusMessage);
        Assert.Contains("exit 1", vm.BuildFailureReport(), StringComparison.Ordinal);
        Assert.Equal(1, vm.Step);
    }

    [Fact]
    public async Task An_unrecoverable_engine_error_names_its_code_and_message()
    {
        var (vm, processes, _) = Build();
        processes.ExitCode = 2;
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"error","code":"FORGE-STAGE-FAILED","message":"the blueprint stage failed twice","recoverable":false}"""),
            Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"failed","exitCode":2}"""),
        ]);
        FillStepOne(vm);

        await Compose(vm);

        Assert.True(vm.HasFailure);
        Assert.Equal(WizardFailureKind.ConfigRefused, vm.Failure!.Kind);
        Assert.NotNull(vm.Failure.EngineError);
        Assert.Equal("FORGE-STAGE-FAILED", vm.Failure.EngineError.Code);
        Assert.Equal("the blueprint stage failed twice", vm.Failure.EngineError.Message);
        // The exit code joined the card the error raised, instead of a second card replacing it.
        Assert.Equal(2, vm.Failure.ExitCode);
        Assert.Contains("FORGE-STAGE-FAILED: the blueprint stage failed twice", vm.Failure.Detail, StringComparison.Ordinal);
        Assert.Contains("FORGE-STAGE-FAILED: the blueprint stage failed twice", vm.BuildFailureReport(), StringComparison.Ordinal);
        // The engine refused what the settings gave it: the way out is the settings screen.
        Assert.True(vm.FailureOffersSettings);
        Assert.False(vm.FailureOffersDiagnostic);
        // The status line still carries the engine's own words (review D3), untouched.
        Assert.Equal("FORGE-STAGE-FAILED: the blueprint stage failed twice", vm.StatusMessage);
    }

    /// <summary>
    /// The old status line kept the LAST stderr line only. The report keeps them all, in
    /// order, under the command line a terminal could replay and the exit code.
    /// </summary>
    [Fact]
    public async Task The_failure_report_carries_the_command_line_the_exit_code_and_the_whole_stderr()
    {
        var (vm, processes, _) = Build();
        processes.ExitCode = 2;
        processes.OutputToEmit.AddRange(
        [
            Err("orkeon forge: no LLM is configured (FORGE-LLM-UNAVAILABLE)"),
            Err("  run `orkeon init`, or pass --settings"),
            Err("  nothing was written"),
        ]);
        FillStepOne(vm);

        await Compose(vm);

        var report = vm.BuildFailureReport();
        Assert.Contains("orkeon forge", report, StringComparison.Ordinal);
        Assert.Contains("--dry", report, StringComparison.Ordinal);
        Assert.Contains("exit 2", report, StringComparison.Ordinal);
        Assert.Contains("no LLM is configured (FORGE-LLM-UNAVAILABLE)", report, StringComparison.Ordinal);
        Assert.Contains("run `orkeon init`, or pass --settings", report, StringComparison.Ordinal);
        Assert.Contains("nothing was written", report, StringComparison.Ordinal);
        Assert.Equal(WizardFailureKind.EngineStopped, vm.Failure!.Kind);
        Assert.Equal(3, vm.Failure.Stderr.Split(Environment.NewLine).Length);
        Assert.Equal(vm.EngineCommandLine, vm.Failure.CommandLine);
        // The status line keeps its habit — the last stderr line — and the card shows them all.
        Assert.Equal("  nothing was written", vm.StatusMessage);
        Assert.StartsWith("orkeon forge: no LLM is configured", vm.Failure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_compose_clears_the_previous_failure()
    {
        var (vm, processes, _) = Build();
        processes.ExitCode = 1;
        FillStepOne(vm);
        await Compose(vm);
        Assert.True(vm.HasFailure);

        processes.ExitCode = 0;
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
        ]);
        await Compose(vm);

        Assert.False(vm.HasFailure);
        Assert.Null(vm.Failure);
        Assert.Equal("", vm.BuildFailureReport());
        // A success status is never hidden behind a stale card (D-05).
        Assert.Equal("Stopped — you can pick it up again from My solutions.", vm.StatusMessage);
    }

    [Fact]
    public async Task A_refused_promotion_uses_the_same_failure_card()
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
            await Compose(vm);
            vm.TeamName = "Ma veille";
            Assert.False(vm.HasFailure);

            // The engine refuses out loud and exits non-zero, without a `promoted` event.
            processes.OutputToEmit.Clear();
            processes.OutputToEmit.Add(Err("orkeon forge promote: the destination already exists"));
            processes.ExitCode = 1;
            await vm.SaveTeamCommand.ExecuteAsync();

            Assert.Equal(4, vm.Step);
            Assert.True(vm.HasFailure);
            Assert.Equal(WizardFailureKind.PromoteRefused, vm.Failure!.Kind);
            Assert.Equal(1, vm.Failure.ExitCode);
            Assert.Equal("orkeon forge promote: the destination already exists", vm.Failure.Detail);
            Assert.Contains("forge promote veille --to", vm.Failure.CommandLine, StringComparison.Ordinal);
            Assert.Contains("the destination already exists", vm.BuildFailureReport(), StringComparison.Ordinal);
            // "Try again" at step 4 is the save itself; the status line keeps its sentence.
            Assert.True(vm.FailureOffersRetry);
            Assert.Same(vm.SaveTeamCommand, vm.FailureRetryCommand);
            Assert.Contains("refused the promotion", vm.StatusMessage, StringComparison.Ordinal);
            Assert.Equal(4, vm.Step);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// An exception out of the launch used to reach the command's FaultHandler — a MessageBox
    /// in the real app, nothing at all here. It is a failure like the others, on the card.
    /// </summary>
    [Fact]
    public async Task An_exception_during_the_launch_becomes_a_failure_card_rather_than_a_fault()
    {
        var (vm, processes, _) = Build();
        processes.Fault = new InvalidOperationException("A forge session is already running.");
        FillStepOne(vm);

        await Compose(vm);   // would have thrown out of the command before STUDIO-13

        Assert.True(vm.HasFailure);
        Assert.Equal(WizardFailureKind.Unknown, vm.Failure!.Kind);
        Assert.Equal("InvalidOperationException: A forge session is already running.", vm.Failure.Detail);
        Assert.Null(vm.Failure.ExitCode);
        Assert.False(vm.IsEngineRunning);
        Assert.True(vm.FailureOffersDiagnostic);
        Assert.Contains("A forge session is already running.", vm.BuildFailureReport(), StringComparison.Ordinal);
    }

    /// <summary>"Stop" is the user's own gesture; a stopped run is not a failure.</summary>
    [Fact]
    public async Task Stopping_the_engine_raises_no_failure_card()
    {
        var (vm, processes, _) = Build();
        processes.HonourCancellation = true;
        processes.WhileRunning = () => vm.StopCommand.Execute(null);
        FillStepOne(vm);

        await Compose(vm);

        Assert.False(vm.HasFailure);
        Assert.False(vm.IsEngineRunning);
    }

    // ── FORGE-09: « Modify » without a session — the engine rebuilds one from the team's folder ──

    /// <summary>
    /// A YAML team with no <c>crew/</c>-reading session: the promoted folder is all there is.
    /// Written the way the catalog reads it, with a sidecar to seed the adoption fields.
    /// </summary>
    private static async Task<(string Root, string TeamDir, string SessionDir)> WriteOrphanTeam()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-wiz-rebuild-" + Guid.NewGuid().ToString("N"));
        var teamDir = Path.Combine(root, "teams", "veille-docs");
        var sessionDir = Path.Combine(root, ".orkeon", "forge", "veille-docs");
        Directory.CreateDirectory(Path.Combine(teamDir, "crew"));
        await File.WriteAllTextAsync(Path.Combine(teamDir, "crew", "config.yaml"), "name: veille\ngoal: g\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(teamDir, "studio-team.json"),
            """{"name":"Veille docs","description":"le besoin d'origine","profile":"Local","schedule":"daily@07:30","mounts":["./output:/output:rw"]}""", TestContext.Current.CancellationToken);
        return (root, teamDir, sessionDir);
    }

    /// <summary>The engine's own writes during <c>forge reopen</c>: the rebuilt session, parked at the dry pause.</summary>
    private static void WriteRebuiltSession(string sessionDir, string teamDir)
    {
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "session.json"),
            $$"""{"v":1,"slug":"veille-docs","title":"veille","format":"yaml","state":"Test","status":"Active","promotedTo":{{System.Text.Json.JsonSerializer.Serialize(teamDir)}}}""");
        File.WriteAllText(Path.Combine(sessionDir, "blueprint.json"),
            """{"crew":{"name":"veille","goal":"g"},"agents":[{"key":"a","role":"Scanner","goal":"g","tools":["file_write"]}],"tasks":[{"key":"t","description":"d","expectedOutput":"e","agent":"a","deliverable":"/output/rapport.md"}],"rationale":"read back"}""");
    }

    /// <summary>
    /// The owner's case: « Modify » greyed out on an imported team, or one whose session was
    /// deleted. The wizard now runs <c>forge reopen</c> on the folder — one offline child, no
    /// resume — reads the rebuilt session off the stream, and opens the Composer at the dry
    /// pause: agents editable, the trial and the adoption on offer, the adoption fields seeded
    /// from the sidecar, and the re-adoption pinned to the same folder.
    /// </summary>
    [Fact]
    public async Task Modify_on_a_team_without_a_session_has_the_engine_rebuild_one_and_opens_the_composer()
    {
        var (root, teamDir, sessionDir) = await WriteOrphanTeam();
        try
        {
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"));
            processes.WhileRunning = () => WriteRebuiltSession(sessionDir, teamDir);
            processes.OutputToEmit.AddRange(
            [
                Out($$"""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille-docs","dir":{{System.Text.Json.JsonSerializer.Serialize(sessionDir)}},"format":"yaml","resumed":false}"""),
                Out($$"""{"v":2,"seq":2,"ts":"t","kind":"team.reopened","slug":"veille-docs","dir":{{System.Text.Json.JsonSerializer.Serialize(sessionDir)}},"path":{{System.Text.Json.JsonSerializer.Serialize(teamDir)}},"state":"test","rebuilt":true,"brief":"derived"}"""),
                Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
            ]);
            var team = TeamCatalog.Describe(teamDir);
            Assert.True(team.HasYamlCrew);

            await vm.ReopenTeamAsync(team, session: null);

            // One child, the reopen — no resume: the dry pause opens without an engine.
            Assert.Equal(["forge", "reopen", teamDir, "--events", "jsonl"], Assert.Single(processes.Requests).Arguments);
            Assert.False(vm.IsEngineRunning);
            Assert.False(vm.HasFailure);
            Assert.Equal(2, vm.Step);
            Assert.Equal(4, vm.MaxStep);
            Assert.Equal(teamDir, vm.ReopenedTeamPath);
            Assert.True(vm.CanTryTeam);
            Assert.True(vm.AdoptWithoutTrialCommand.CanExecute(null));
            Assert.True(vm.CanEditAgents);
            Assert.Equal("Scanner", Assert.Single(vm.Agents).Name);
            Assert.Equal("Veille docs", vm.TeamName);
            Assert.Equal("Local", vm.AdoptProfileName);
            Assert.Equal(1, vm.ScheduleChoice);
            Assert.Equal("07:30", vm.ScheduleTime);
            Assert.Contains("./output:/output:rw", vm.TeamMounts);

            // Kept as it is (the dry pause's offline answer), then re-adopted onto the SAME folder.
            processes.WhileRunning = null;
            processes.OutputToEmit.Clear();
            processes.OutputToEmit.Add(Out("""{"v":2,"seq":1,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""));
            await vm.AdoptWithoutTrialCommand.ExecuteAsync();
            Assert.Equal(["forge", "resume", "veille-docs", "--events", "jsonl", "--adopt"], processes.Requests[1].Arguments);
            Assert.True(vm.CanSaveTeam);

            processes.OutputToEmit.Clear();
            processes.OutputToEmit.Add(Out(
                """{"v":2,"seq":1,"ts":"t","kind":"promoted","path":PATH,"launcher":"run.sh","updated":true}"""
                    .Replace("PATH", System.Text.Json.JsonSerializer.Serialize(teamDir), StringComparison.Ordinal)));
            await vm.SaveTeamCommand.ExecuteAsync();

            var promote = processes.Requests[2].Arguments.ToList();
            Assert.Equal(teamDir, promote[promote.IndexOf("--to") + 1]);
            Assert.Equal("le besoin d'origine", TeamCatalog.Describe(teamDir).Description);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A rebuild the engine refuses (nothing it can read back) is a failure card like any
    /// other engine refusal — the engine's own code and words — and the wizard stays where it
    /// was: no reopened folder pinned, step 1 untouched.
    /// </summary>
    [Fact]
    public async Task A_refused_rebuild_shows_the_engines_refusal_and_pins_nothing()
    {
        var (root, teamDir, _) = await WriteOrphanTeam();
        try
        {
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"));
            processes.ExitCode = 1;
            processes.OutputToEmit.AddRange(
            [
                Out("""{"v":2,"seq":1,"ts":"t","kind":"error","code":"FORGE-TEAM-UNREADABLE","message":"holds no crew the forge can read back into a plan","recoverable":false}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"failed","exitCode":1}"""),
            ]);

            await vm.ReopenTeamAsync(TeamCatalog.Describe(teamDir), session: null);

            Assert.True(vm.HasFailure);
            Assert.Equal(WizardFailureKind.ConfigRefused, vm.Failure!.Kind);
            Assert.Contains("FORGE-TEAM-UNREADABLE", vm.Failure.Detail, StringComparison.Ordinal);
            Assert.Null(vm.ReopenedTeamPath);
            Assert.Equal(1, vm.Step);
            Assert.False(vm.IsEngineRunning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Owner report of 2026-09-21, « two clicks to reach the wizard »: with no session pointing
    /// at the team, the engine rebuilds one first and the screen only came forward once it had —
    /// a second or two of nothing, and the second click landed on a busy engine and vanished.
    /// The screen comes forward on the click; the rebuild shows as the engine working.
    /// </summary>
    [Fact]
    public async Task Modify_brings_the_wizard_forward_on_the_click_not_once_the_rebuild_is_over()
    {
        var (root, teamDir, sessionDir) = await WriteOrphanTeam();
        try
        {
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"));
            var activated = 0;
            var activatedBeforeAnyChild = 0;
            vm.SessionActivated += (_, _) =>
            {
                activated++;
                if (processes.Requests.Count == 0)
                    activatedBeforeAnyChild++;
            };
            processes.WhileRunning = () => WriteRebuiltSession(sessionDir, teamDir);
            processes.OutputToEmit.AddRange(
            [
                Out($$"""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille-docs","dir":{{System.Text.Json.JsonSerializer.Serialize(sessionDir)}},"format":"yaml","resumed":false}"""),
                Out($$"""{"v":2,"seq":2,"ts":"t","kind":"team.reopened","slug":"veille-docs","dir":{{System.Text.Json.JsonSerializer.Serialize(sessionDir)}},"path":{{System.Text.Json.JsonSerializer.Serialize(teamDir)}},"state":"test","rebuilt":true,"brief":"derived"}"""),
                Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
            ]);

            await vm.ReopenTeamAsync(TeamCatalog.Describe(teamDir), session: null);

            // Once, and before the engine was even asked — not a second time on arrival.
            Assert.Equal(1, activated);
            Assert.Equal(1, activatedBeforeAnyChild);
            Assert.Single(processes.Requests);
            Assert.Equal(2, vm.Step);
            Assert.Equal(teamDir, vm.ReopenedTeamPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The other half of the same report: the card's button is live whatever the wizard is
    /// doing, and a click onto a busy engine went nowhere. It is refused in words on the
    /// wizard's status line, the screen brought forward so the line is read, and the creation
    /// under way is left exactly as it was — no second child, nothing pinned, nothing stopped.
    /// </summary>
    [Fact]
    public async Task Modify_while_the_engine_is_busy_is_refused_in_words_not_dropped()
    {
        var (root, teamDir, _) = await WriteOrphanTeam();
        try
        {
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"));
            var activated = 0;
            vm.SessionActivated += (_, _) => activated++;
            processes.OutputToEmit.AddRange(
            [
                Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"stage.entered","stage":"blueprint","iteration":1}"""),
            ]);
            string? saidWhileBusy = null;
            var childrenWhileBusy = -1;
            processes.WhileRunning = () =>
            {
                var refused = vm.ReopenTeamAsync(TeamCatalog.Describe(teamDir), session: null);
                Assert.True(refused.IsCompletedSuccessfully);
                saidWhileBusy = vm.StatusMessage;
                childrenWhileBusy = processes.Requests.Count;
            };

            FillStepOne(vm);
            await Compose(vm);

            // Composing from step 1, with no title or slug from the engine yet, the creation
            // is named by the need the user typed.
            Assert.Equal(
                string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    EnglishStudioStrings.Instance[StudioStringKeys.WizardEngineBusy],
                    TeamCatalog.NormalizeName(vm.Need)),
                saidWhileBusy);
            Assert.Equal(1, childrenWhileBusy);
            // The compose's own activation, then the refusal's — the second is what moves the
            // screen so the status line is read.
            Assert.Equal(2, activated);
            Assert.Single(processes.Requests);
            Assert.Null(vm.ReopenedTeamPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ProcessOutputLine[] RebuiltStream(string sessionDir, string teamDir) =>
    [
        Out($$"""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille-docs","dir":{{System.Text.Json.JsonSerializer.Serialize(sessionDir)}},"format":"yaml","resumed":false}"""),
        Out($$"""{"v":2,"seq":2,"ts":"t","kind":"team.reopened","slug":"veille-docs","dir":{{System.Text.Json.JsonSerializer.Serialize(sessionDir)}},"path":{{System.Text.Json.JsonSerializer.Serialize(teamDir)}},"state":"test","rebuilt":true,"brief":"derived"}"""),
        Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""),
    ];

    /// <summary>
    /// The owner's « Modify » landing on step 1 with the rebuilt session sitting on disk, the
    /// second click finding it (2026-09-21). WPF resumes an await begun in an input handler at
    /// Send priority — above the Normal priority the reader thread's posts travel at — so the
    /// rebuild read the session off a model the events had not reached yet. A run now completes
    /// only once its epilogue has landed on the UI thread, and with it everything posted before.
    /// The queued dispatcher is that thread seen from the outside: nothing lands until drained.
    /// </summary>
    [Fact]
    public async Task A_run_completes_only_once_its_epilogue_has_landed_so_the_rebuild_reads_a_fed_model()
    {
        var (root, teamDir, sessionDir) = await WriteOrphanTeam();
        try
        {
            var ui = new QueuedUiDispatcher();
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"), dispatcher: ui);
            processes.WhileRunning = () => WriteRebuiltSession(sessionDir, teamDir);
            processes.OutputToEmit.AddRange(RebuiltStream(sessionDir, teamDir));

            var reopen = vm.ReopenTeamAsync(TeamCatalog.Describe(teamDir), session: null);

            // The child has run and exited; its events and the epilogue are queued, not landed
            // — and the reopen waits for them instead of reading an empty model.
            Assert.Single(processes.Requests);
            Assert.False(reopen.IsCompleted);
            Assert.True(ui.Pending > 0);
            Assert.Null(vm.SessionSlug);

            ui.Drain();
            await reopen;

            Assert.Equal(2, vm.Step);
            Assert.Equal(teamDir, vm.ReopenedTeamPath);
            Assert.Equal("veille-docs", vm.SessionSlug);
            Assert.False(vm.IsEngineRunning);
            Assert.False(vm.HasFailure);
            Assert.Equal(0, ui.Pending);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A clean exit that announces no session on the stream is not a step 1 with nothing said:
    /// the session the engine wrote is on the disk, where My teams reads it, and the wizard
    /// reads it there too.
    /// </summary>
    [Fact]
    public async Task A_rebuild_the_stream_did_not_announce_is_read_off_the_disk()
    {
        var (root, teamDir, sessionDir) = await WriteOrphanTeam();
        try
        {
            // The catalog reads the workspace the engine wrote into: the wizard's own.
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"), workspace: root);
            processes.WhileRunning = () => WriteRebuiltSession(sessionDir, teamDir);
            processes.OutputToEmit.Add(Out("""{"v":2,"seq":1,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""));

            await vm.ReopenTeamAsync(TeamCatalog.Describe(teamDir), session: null);

            Assert.False(vm.HasFailure);
            Assert.Equal(2, vm.Step);
            Assert.Equal(teamDir, vm.ReopenedTeamPath);
            Assert.Equal("veille-docs", vm.SessionSlug);
            Assert.True(vm.CanTryTeam);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>And when the disk has none either, the card says so — never a silent step 1.</summary>
    [Fact]
    public async Task A_rebuild_that_left_nothing_anywhere_says_so_on_the_card()
    {
        var (root, teamDir, _) = await WriteOrphanTeam();
        try
        {
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"));
            processes.OutputToEmit.Add(Out("""{"v":2,"seq":1,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""));

            await vm.ReopenTeamAsync(TeamCatalog.Describe(teamDir), session: null);

            Assert.True(vm.HasFailure);
            Assert.Equal(WizardFailureKind.Unknown, vm.Failure!.Kind);
            Assert.Contains("team.reopened", vm.Failure.Detail, StringComparison.Ordinal);
            Assert.Equal(1, vm.Step);
            Assert.Null(vm.ReopenedTeamPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

/// <summary>A question asked while the engine is not listening must not vanish silently.</summary>
public sealed class AskWithoutEngineTests
{
    [Fact]
    public void With_nobody_listening_the_local_bank_answers_rather_than_the_thread_going_quiet()
    {
        // The guarantee behind the owner's escalation — "I ask the assistant a question and
        // nothing happens" — used to be met by a notice explaining that
        // nothing had been sent. It is now met by an actual answer: with no forge session
        // listening, the keyword bank replies, which is the thing the notice was standing
        // in for. What must never happen is silence, and it still cannot.
        var chat = new ChatThreadViewModel();
        chat.SetContext(AssistantContext.WizardStep1);
        chat.OpenCommand.Execute(null);

        chat.Draft = "can it read PDF files in that folder?";
        chat.SendCommand.Execute(null);

        Assert.Equal(2, chat.Turns.Count);
        Assert.False(chat.Turns[0].IsBot);
        Assert.True(chat.Turns[1].IsBot);
        Assert.NotEmpty(chat.Turns[1].Body);
        Assert.Equal("", chat.Draft);   // sent, not kept: it went somewhere
    }

}
