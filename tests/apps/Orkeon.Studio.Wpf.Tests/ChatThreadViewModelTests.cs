using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The conversation with the assistant (30/08 mock, T-01 → T-08). The delay seam runs every
/// beat inline, so a whole interview plays out synchronously and the suite never waits on a
/// wall clock.
/// </summary>
public class ChatThreadViewModelTests
{
    private static ProcessOutputLine Out(string json) =>
        ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    private static (CreateTeamViewModel Vm, FakeProcessLauncher Processes) Build()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { }, new FakeLlmEndpointProbe());
        var profiles = new ModelProfilesViewModel(new InMemoryModelProfileStore(), llm, probe: new FakeLlmEndpointProbe());
        profiles.CommitEdit(
            new ModelProfile { Name = "Local", Provider = "Ollama", Model = "qwen2.5:14b", BaseUrl = "http://localhost:11434/v1" },
            previousName: null);
        profiles.StudioProfileName = "Local";

        var processes = new FakeProcessLauncher();
        var vm = new CreateTeamViewModel(
            profiles,
            new CreateTeamDependencies
            {
                Client = new ForgeClient(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                WorkspaceDirectory = "/ws",
                TeamsRoot = "/teams",
            });
        vm.Need = "une veille documentaire";
        vm.FrequencyChoices[1].SelectCommand.Execute(null);
        vm.SourceChoices[0].SelectCommand.Execute(null);
        vm.OutputChoices[0].SelectCommand.Execute(null);
        return (vm, processes);
    }

    private static void Answer(ChatThreadViewModel chat, string text)
    {
        chat.Draft = text;
        chat.SendCommand.Execute(null);
    }

    private static string Assistant(string text) =>
        $$"""{"v":2,"seq":3,"ts":"t","kind":"assistant.message","text":"{{text}}"}""";

    private const string BriefReady =
        """{"v":2,"seq":9,"ts":"t","kind":"brief.ready","brief":{"goal":"g"}}""";

    /// <summary>
    /// The inversion. This test used to assert the opposite — that the click asked three
    /// questions and the engine heard nothing until the third answer — which is exactly
    /// the defect: the user was interviewed twice, once by a local script and once by the
    /// model, on two different surfaces.
    /// </summary>
    [Fact]
    public async Task Composing_starts_the_engine_and_the_questions_come_off_the_wire()
    {
        var (vm, processes) = Build();
        var open = false;
        string? asked = null;
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(Assistant("Where does this folder live?")));
            // Read while the child is alive: that is when the brief stage is blocking.
            open = vm.Chat.IsOpen;
            asked = vm.Chat.Turns.Single(t => t.IsBot).Body;
        };

        await vm.ComposeCommand.ExecuteAsync();

        // The click IS the run, and the brief carries the wizard's own words — nothing
        // is spliced in from a questionnaire that no longer exists.
        var request = Assert.Single(processes.Requests);
        Assert.Contains("une veille documentaire", request.Arguments[1], StringComparison.Ordinal);
        Assert.True(open);

        // The model's question is a bubble in the thread and nowhere else.
        Assert.Equal("Where does this folder live?", asked);
    }

    [Fact]
    public async Task An_assistant_turn_with_nothing_after_it_is_the_assistant_waiting()
    {
        var (vm, processes) = Build();
        bool asking = false, busy = true;
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(Assistant("Which folder?")));
            asking = vm.Chat.IsAsking;
            busy = vm.Chat.IsBusy;
        };

        await vm.ComposeCommand.ExecuteAsync();

        // No event says "your turn": the brief stage emits and then blocks on stdin, so
        // the local mirror is "the last bubble is the assistant's and nothing is in flight".
        Assert.True(asking);
        Assert.False(busy);
    }

    [Fact]
    public async Task An_answer_travels_down_stdin_as_a_user_message()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(Assistant("Which folder?")));
            Answer(vm.Chat, "Documents/Comptes-rendus");
        };

        await vm.ComposeCommand.ExecuteAsync();

        Assert.Contains(
            processes.InputLines,
            l => l.Contains("\"kind\":\"user.message\"", StringComparison.Ordinal)
              && l.Contains("Documents/Comptes-rendus", StringComparison.Ordinal));
        Assert.Contains(vm.Chat.Turns, t => !t.IsBot && t.Body == "Documents/Comptes-rendus");
    }

    /// <summary>
    /// However many the model wants: the pack asks it for three to five, the stage allows
    /// twenty-four, and Studio counts none of them.
    /// </summary>
    [Fact]
    public async Task The_engine_asks_as_many_questions_as_it_likes()
    {
        var (vm, processes) = Build();
        var script = new Queue<string>(["Q2", "Q3", "Q4", "Q5"]);
        processes.OnInputLine = _ =>
        {
            if (script.Count > 0)
                processes.Emit(Out(Assistant(script.Dequeue())));
        };
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(Assistant("Q1")));
            for (var i = 0; i < 4; i++)
                Answer(vm.Chat, $"réponse {i}");
        };

        await vm.ComposeCommand.ExecuteAsync();

        // Five questions, four answers — plus the farewell the dying child leaves behind.
        Assert.Equal(5, vm.Chat.Turns.Count(t => t.IsBot && t.Body.Length > 0 && t.Body.StartsWith('Q')));
        Assert.Equal(4, vm.Chat.Turns.Count(t => !t.IsBot));
    }

    [Fact]
    public async Task brief_ready_closes_the_interview_and_hands_the_column_back()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(Assistant("Which folder?")));
            Answer(vm.Chat, "un dossier");
            processes.Emit(Out(BriefReady));
        };

        await vm.ComposeCommand.ExecuteAsync();

        Assert.True(vm.Chat.IsDone);
        Assert.False(vm.Chat.IsAsking);
        Assert.False(vm.Chat.IsOpen);
        Assert.Single(vm.Chat.Turns, t => t.IsClosing);
    }

    /// <summary>
    /// Past brief.ready the engine is composing a blueprint and no longer reads stdin, so
    /// a message arriving there is narration. Treating it as a question would leave the
    /// composer claiming "thinking" for the rest of the run.
    /// </summary>
    [Fact]
    public async Task A_turn_arriving_after_the_brief_is_not_a_question()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(BriefReady));
            processes.Emit(Out(Assistant("Composing the roles…")));
        };

        await vm.ComposeCommand.ExecuteAsync();

        Assert.True(vm.Chat.IsDone);
        Assert.False(vm.Chat.IsAsking);
    }

    [Fact]
    public async Task Stopping_keeps_every_turn_and_restarts_nothing()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () => processes.Emit(Out(Assistant("Which folder?")));

        await vm.ComposeCommand.ExecuteAsync();
        var before = vm.Chat.Turns.Count;
        vm.Chat.StopCommand.Execute(null);


        Assert.Equal(before, vm.Chat.Turns.Count);
        Assert.True(vm.Chat.IsOpen);
        Assert.False(vm.Chat.IsBusy);
    }

    [Fact]
    public async Task An_assistant_turn_arriving_on_a_closed_thread_is_counted_as_unread()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () => processes.Emit(Out(BriefReady));

        await vm.ComposeCommand.ExecuteAsync();

        // brief.ready closed the thread behind the interview.
        Assert.False(vm.Chat.IsOpen);
        Assert.Equal(0, vm.Chat.UnreadCount);

        vm.Chat.AddAssistantTurn("Une précision, s'il vous plaît.");
        Assert.Equal(1, vm.Chat.UnreadCount);
        Assert.True(vm.Chat.HasUnread);

        vm.Chat.OpenCommand.Execute(null);
        Assert.Equal(0, vm.Chat.UnreadCount);
        Assert.False(vm.Chat.HasUnread);
    }

    /// <summary>
    /// The recap is the wizard's own fields now: the three interview rows went with the
    /// interview, and what the model learns it keeps in its own brief.
    /// </summary>
    [Fact]
    public void The_recap_reads_the_form_and_marks_what_is_still_missing()
    {
        var (vm, _) = Build();

        var facts = vm.Chat.Facts;
        Assert.Contains(facts, f => f.IsKnown && f.Value.Contains("veille", StringComparison.Ordinal));
        Assert.All(facts, f => Assert.NotEqual(0, f.Value.Length));
    }

    [Fact]
    public async Task A_typed_instruction_joins_the_recap_as_soon_as_it_is_typed()
    {
        var (vm, _) = Build();
        await vm.ComposeCommand.ExecuteAsync();

        Assert.DoesNotContain(vm.Chat.Facts, f => f.Value == "ne jamais citer les brouillons");

        vm.ComposeNotes.Consigne = "ne jamais citer les brouillons";

        Assert.Contains(vm.Chat.Facts, f => f.Value == "ne jamais citer les brouillons" && f.IsKnown);
    }

    [Fact]
    public void A_free_question_with_no_engine_listening_is_answered_from_the_bank()
    {
        var (vm, _) = Build();
        vm.Chat.OpenCommand.Execute(null);

        // English catalogue in the tests, so the English keywords are what matches — the
        // rules are catalogued per culture precisely because they cannot be shared.
        Answer(vm.Chat, "how much does this cost?");

        // No forge session is running, so the local bank answers rather than the thread
        // staying mute — and it answers the question that was actually asked.
        var reply = vm.Chat.Turns.Last();
        Assert.True(reply.IsBot);
        Assert.Contains("Ollama", reply.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_free_question_with_no_keyword_falls_back_to_where_the_user_stands()
    {
        var chat = new ChatThreadViewModel();
        chat.SetContext(AssistantContext.History);
        chat.OpenCommand.Execute(null);

        Answer(chat, "et alors ?");

        Assert.Contains("history", chat.Turns.Last().Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_primer_is_different_on_every_screen_the_thread_is_mounted_on()
    {
        var chat = new ChatThreadViewModel();

        chat.SetContext(AssistantContext.WizardStep1);
        var create = chat.Primer;
        chat.SetContext(AssistantContext.Run);
        var run = chat.Primer;
        chat.SetContext(AssistantContext.History);
        var history = chat.Primer;

        Assert.Equal(3, new HashSet<string>([create, run, history], StringComparer.Ordinal).Count);
    }

    [Fact]
    public void The_window_hands_the_same_thread_to_every_screen_that_mounts_it()
    {
        var shell = new MainWindowViewModel(
            new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                TargetProbe = new FakeTargetProbe(),
                Picker = new FakePathPicker(),
            },
            teamsRoot: Path.Combine(Path.GetTempPath(), "orkeon-chat-" + Guid.NewGuid().ToString("N")));

        // One instance for the window (T-01). A per-screen thread would lose its history
        // on the first tab change, which is the whole reason it is a window-lifetime object.
        Assert.Same(shell.Chat, shell.CreateTeam.Chat);

        // The Run and Test screens run over SEPARATE launchers — the trial is a rehearsal with no
        // history of its own — and neither owns a conversation: they reach the window's one
        // through the ancestor, so there is nothing here for a second copy to hide in.
        Assert.NotSame(shell.Launch, shell.Test.Launcher);
        Assert.Null(typeof(Orkeon.Studio.Wpf.ViewModels.Launch.LaunchTabViewModel).GetProperty("Chat"));

        // And it is genuinely shared state: what one screen says, the other screen holds.
        shell.Chat.AddAssistantTurn("une précision");
        Assert.Single(shell.CreateTeam.Chat.Turns);
    }

    [Fact]
    public async Task A_restart_takes_the_conversation_with_it()
    {
        var (vm, _) = Build();
        await vm.ComposeCommand.ExecuteAsync();
        Answer(vm.Chat, "un dossier");
        Assert.NotEmpty(vm.Chat.Turns);

        vm.RestartCommand.Execute(null);

        Assert.Empty(vm.Chat.Turns);
        Assert.False(vm.Chat.IsStarted);
        Assert.True(vm.Chat.IsEmpty);
    }
}

/// <summary>
/// The hot language switch reaches the conversation too (recette §9: no text left in
/// the old language). A bubble the assistant said from the catalogue is rewritten; a bubble
/// the user typed is theirs and stays exactly as typed.
/// </summary>
public sealed class ChatCatalogueSwitchTests
{
    private sealed class SwitchableStrings : Orkeon.Studio.Core.Localization.IStudioStrings
    {
        public string Prefix { get; set; } = "en:";

        public string this[string key] => Prefix + key;

        public event EventHandler? CultureChanged;

        public void Switch(string prefix)
        {
            Prefix = prefix;
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Fact]
    public void A_language_switch_rewrites_what_the_assistant_said_and_leaves_what_the_user_typed()
    {
        var strings = new SwitchableStrings();
        var chat = new ChatThreadViewModel(strings);
        chat.Bind(
            facts: () => [],
            brief: () => "",
            briefChips: () => [],
            profileName: () => null,
            askEngine: _ => false);

        chat.StartSession();
        chat.AddAssistantTurn("Where does this folder live?");
        chat.Draft = "Documents/Comptes-rendus";
        chat.SendCommand.Execute(null);
        chat.BriefAccepted();

        var asked = chat.Turns[0];
        var typed = chat.Turns[1];
        var closing = Assert.Single(chat.Turns, t => t.IsClosing);
        Assert.StartsWith("en:", closing.Body, StringComparison.Ordinal);

        strings.Switch("fr:");

        // Studio's own words follow the catalogue.
        Assert.StartsWith("fr:", closing.Body, StringComparison.Ordinal);
        Assert.StartsWith("fr:", chat.Placeholder, StringComparison.Ordinal);
        Assert.StartsWith("fr:", chat.Primer, StringComparison.Ordinal);

        // What the user typed is theirs — and so is what the MODEL said. An LLM sentence
        // has no key, and inventing one would rewrite a stranger's words.
        Assert.Equal("Documents/Comptes-rendus", typed.Body);
        Assert.Equal("Where does this folder live?", asked.Body);
    }
}

/// <summary>
/// The two state-machine holes a review found: a message typed at the wrong instant used to
/// take the whole composition with it, and a Stop used to leave quick replies on the table
/// for a question that was never asked.
/// </summary>
public sealed class ChatThreadEdgeTests
{
    /// <summary>
    /// A delay that holds its callbacks until told. The default inline one plays every beat
    /// at once, which is what makes the suite fast — but it also means there is never a
    /// pause to interrupt, and «Stop pressed mid-thought» is exactly a pause being
    /// interrupted. This is the seam that makes that instant reachable.
    /// </summary>
    private sealed class ManualDelay : Orkeon.Studio.Wpf.ViewModels.Mvvm.IUiDelay
    {
        private readonly List<Action> _pending = [];

        public void After(TimeSpan delay, Action action) => _pending.Add(action);

        public void CancelPending() => _pending.Clear();

        /// <summary>Fires everything queued, in order.</summary>
        public void Elapse()
        {
            var due = _pending.ToList();
            _pending.Clear();
            foreach (var action in due)
                action();
        }

        public int Pending => _pending.Count;
    }

    private static ChatThreadViewModel Thread(
        Orkeon.Studio.Wpf.ViewModels.Mvvm.IUiDelay? delay = null,
        Func<string, bool>? askEngine = null)
    {
        var chat = new ChatThreadViewModel(strings: null, delay: delay);
        chat.Bind(
            facts: () => [], brief: () => "", briefChips: () => [],
            profileName: () => null, askEngine: askEngine ?? (_ => false));
        return chat;
    }

    private static void Answer(ChatThreadViewModel chat, string text)
    {
        chat.Draft = text;
        chat.SendCommand.Execute(null);
    }

    /// <summary>
    /// Send used to call CancelPending, and the callback pending at that exact moment was
    /// the one handing the column back. The guard that defended against it is gone because
    /// the cancel is gone: the property is now structural, which is worth pinning.
    /// </summary>
    [Fact]
    public void A_message_typed_during_the_closing_pause_does_not_cancel_the_handback()
    {
        var delay = new ManualDelay();
        var chat = Thread(delay);

        chat.StartSession();
        chat.AddAssistantTurn("Which folder?");
        chat.BriefAccepted();

        Assert.True(chat.IsOpen);          // the closing pause is still pending
        Answer(chat, "une dernière chose");
        delay.Elapse();

        Assert.False(chat.IsOpen);
    }

    [Fact]
    public void Stopping_mid_thought_leaves_no_question_on_the_table()
    {
        var delay = new ManualDelay();
        var chat = Thread(delay, askEngine: _ => true);

        chat.StartSession();
        chat.AddAssistantTurn("Which folder?");
        Assert.True(chat.IsAsking);

        Answer(chat, "un dossier");        // answered; the assistant starts thinking
        Assert.True(chat.IsBusy);
        Assert.False(chat.IsAsking);

        // Stop lands HERE, in the pause: nothing is in flight and no bubble is owed an
        // answer, so the composer must not claim one is.
        chat.StopCommand.Execute(null);

        Assert.False(chat.IsBusy);
        Assert.False(chat.IsAsking);
        Assert.DoesNotContain(chat.Turns, t => t.IsClosing);
    }

    /// <summary>This is now the DEFINITION of IsAsking, so it is the test that matters most.</summary>
    [Fact]
    public void A_question_is_only_on_the_table_once_its_bubble_exists()
    {
        var chat = Thread();

        Assert.False(chat.IsAsking);
        chat.StartSession();

        // Started, but the engine has not said anything yet: nobody is waiting on the user.
        Assert.False(chat.IsAsking);
        Assert.True(chat.IsBusy);

        chat.AddAssistantTurn("Which folder?");
        Assert.True(chat.IsAsking);
        Assert.Single(chat.Turns);
    }
}

/// <summary>
/// The pencil on the pinned brief card. It shared <c>CloseCommand</c> with the header's cross,
/// so «Edit» was a second «dismiss»: the panel went away and the brief stayed exactly as
/// unreachable as it had been. The intent it was supposed to carry —
/// <c>EditBriefRequested</c> — was raised by every close and listened to by nobody.
/// </summary>
public sealed class ChatEditBriefTests
{
    [Fact]
    public void Edit_closes_the_thread_and_asks_to_go_back_to_the_form()
    {
        var chat = new ChatThreadViewModel();
        chat.StartSession();
        var asked = 0;
        chat.EditBriefRequested += (_, _) => asked++;

        chat.EditBriefCommand.Execute(null);

        Assert.False(chat.IsOpen);
        Assert.Equal(1, asked);
    }

    [Fact]
    public void Dismissing_the_panel_is_not_a_request_to_edit()
    {
        var chat = new ChatThreadViewModel();
        chat.StartSession();
        var asked = 0;
        chat.EditBriefRequested += (_, _) => asked++;

        chat.CloseCommand.Execute(null);

        Assert.False(chat.IsOpen);
        Assert.Equal(0, asked);
    }
}

/// <summary>
/// What the screen says when the engine leaves before the brief is accepted. It used to say
/// nothing at all: the spinner stopped, the assistant's last question stayed on screen, and
/// the composer went on writing into a closed pipe.
/// </summary>
public sealed class EngineDepartureTests
{
    private static ProcessOutputLine Out(string json) =>
        ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    private static string Assistant(string text) =>
        $$"""{"v":2,"seq":3,"ts":"t","kind":"assistant.message","text":"{{text}}"}""";

    private const string BriefReady =
        """{"v":2,"seq":9,"ts":"t","kind":"brief.ready","brief":{"goal":"g"}}""";

    private static (CreateTeamViewModel Vm, FakeProcessLauncher Processes) Build()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { }, new FakeLlmEndpointProbe());
        var profiles = new ModelProfilesViewModel(new InMemoryModelProfileStore(), llm, probe: new FakeLlmEndpointProbe());
        profiles.CommitEdit(
            new ModelProfile { Name = "Local", Provider = "Ollama", Model = "qwen2.5:14b", BaseUrl = "http://localhost:11434/v1" },
            previousName: null);
        profiles.StudioProfileName = "Local";

        var processes = new FakeProcessLauncher();
        var vm = new CreateTeamViewModel(
            profiles,
            new CreateTeamDependencies
            {
                Client = new ForgeClient(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                WorkspaceDirectory = "/ws",
                TeamsRoot = "/teams",
            });
        vm.Need = "une veille documentaire";
        vm.FrequencyChoices[1].SelectCommand.Execute(null);
        vm.SourceChoices[0].SelectCommand.Execute(null);
        vm.OutputChoices[0].SelectCommand.Execute(null);
        return (vm, processes);
    }

    [Fact]
    public async Task An_engine_that_dies_before_the_brief_says_so_in_the_thread()
    {
        var (vm, processes) = Build();
        processes.ExitCode = 2;
        processes.WhileRunning = () => processes.Emit(Out(Assistant("Which folder?")));

        await vm.ComposeCommand.ExecuteAsync();

        Assert.False(vm.Chat.IsBusy);
        Assert.False(vm.Chat.IsStarted);
        Assert.False(vm.Chat.IsLive);       // the strip stops claiming someone is waiting
        Assert.False(vm.Chat.IsAsking);     // and the composer stops offering to answer
        Assert.NotEqual("Which folder?", vm.Chat.Turns[^1].Body);
    }

    [Fact]
    public async Task An_engine_that_finished_its_brief_leaves_the_thread_alone()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(Assistant("Which folder?")));
            processes.Emit(Out(BriefReady));
        };

        await vm.ComposeCommand.ExecuteAsync();

        // It left because it was done, not because it broke: nothing to apologise for.
        Assert.True(vm.Chat.IsDone);
        Assert.Single(vm.Chat.Turns, t => t.IsClosing);
        Assert.Equal(2, vm.Chat.Turns.Count);
    }

    [Fact]
    public async Task The_hint_under_a_disabled_compose_button_says_the_assistant_is_working()
    {
        var (vm, processes) = Build();
        var hint = "";
        processes.WhileRunning = () => hint = vm.Step1Hint;

        var resting = vm.Step1Hint;
        await vm.ComposeCommand.ExecuteAsync();

        Assert.NotEqual(resting, hint);
        Assert.NotEmpty(hint);
    }
}
