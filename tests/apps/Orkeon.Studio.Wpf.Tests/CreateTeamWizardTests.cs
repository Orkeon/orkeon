using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The "Créer une équipe" wizard over a scripted engine child: the stream in, the steps
/// out — no real binary, no LLM, the inline dispatcher. The protocol reading itself is
/// pinned in <c>Orkeon.Studio.Core.Tests</c>; these tests pin the wizard's behaviour.
/// </summary>
public class CreateTeamWizardTests
{
    private static ProcessOutputLine Out(string json) =>
        ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    private static (CreateTeamViewModel Vm, FakeProcessLauncher Processes, ModelProfilesViewModel Profiles) Build(
        string? teamsRoot = null,
        bool withAssistant = true)
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
            teamsRoot: teamsRoot ?? "/teams");
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
            // wizard's buttons come from the options and "refine" sends the consigne first.
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

            card.DeleteCommand.Execute(null);
            Assert.True(teams.IsEmpty);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
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
}
