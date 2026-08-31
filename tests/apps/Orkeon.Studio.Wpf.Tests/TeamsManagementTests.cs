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

            var teams = new TeamsViewModel(teamsRoot: root, loadSessions: () => []);
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

            var teams = new TeamsViewModel(teamsRoot: root, loadSessions: () => [.. sessions]);
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
    public void Deleting_a_session_directory_that_is_already_gone_is_refused_not_thrown()
    {
        var missing = Path.Combine(Path.GetTempPath(), "orkeon-absent-" + Guid.NewGuid().ToString("N"));

        Assert.False(ForgeSessionCatalog.Delete(missing));
    }

    [Fact]
    public void An_empty_screen_offers_both_ways_out()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-teams-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var teams = new TeamsViewModel(teamsRoot: root, loadSessions: () => []);

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
            new Orkeon.Studio.Core.Forge.ForgeClient(
                processes ?? new Doubles.FakeProcessLauncher(),
                new Orkeon.Studio.Core.Process.OrkeonBinaryLocator(
                    Doubles.FakeExecutableProbe.WithOrkeonInstalled())),
            dispatcher: null,
            strings: null,
            workspaceDirectory: "/ws",
            teamsRoot: "/teams");
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
