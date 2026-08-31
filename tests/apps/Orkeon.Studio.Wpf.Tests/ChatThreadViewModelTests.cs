using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
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
            new ForgeClient(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            dispatcher: null,
            strings: null,
            workspaceDirectory: "/ws",
            teamsRoot: "/teams");
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

    [Fact]
    public async Task Composing_asks_three_questions_before_the_engine_hears_anything()
    {
        var (vm, processes) = Build();

        await vm.ComposeCommand.ExecuteAsync();

        // The whole point of T-02: the click opens the conversation, it does not start a run.
        Assert.Empty(processes.Requests);
        Assert.True(vm.Chat.IsOpen);
        Assert.True(vm.Chat.IsAsking);
        Assert.Equal(0, vm.Chat.PendingQuestionIndex);

        Answer(vm.Chat, "Documents/Comptes-rendus");
        Assert.Empty(processes.Requests);
        Assert.Equal(1, vm.Chat.PendingQuestionIndex);

        Answer(vm.Chat, "Vendredi 17 h");
        Assert.Empty(processes.Requests);

        Answer(vm.Chat, "Lecture seule du dossier");
        if (vm.PendingCompose is { } pending)
            await pending;

        // Third answer in, and only now: one run, one brief, carrying all three answers.
        var request = Assert.Single(processes.Requests);
        var brief = request.Arguments[1];
        Assert.Contains("Documents/Comptes-rendus", brief, StringComparison.Ordinal);
        Assert.Contains("Vendredi 17 h", brief, StringComparison.Ordinal);
        Assert.Contains("Lecture seule du dossier", brief, StringComparison.Ordinal);
        Assert.Contains("une veille documentaire", brief, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_closing_bubble_is_said_once_and_the_column_goes_back_to_the_wizard()
    {
        var (vm, _) = Build();
        await vm.ComposeCommand.ExecuteAsync();

        Answer(vm.Chat, "un dossier");
        Answer(vm.Chat, "vendredi");
        Answer(vm.Chat, "rien");
        if (vm.PendingCompose is { } pending)
            await pending;

        Assert.True(vm.Chat.IsDone);
        Assert.False(vm.Chat.IsAsking);
        Assert.False(vm.Chat.IsOpen);
        Assert.Single(vm.Chat.Turns, t => t.IsClosing);
    }

    [Fact]
    public async Task A_quick_reply_records_its_value_not_its_label()
    {
        var (vm, processes) = Build();
        await vm.ComposeCommand.ExecuteAsync();

        // « Choisir un dossier… » is an invitation; the fact it records is « Dossier à choisir ».
        var chip = vm.Chat.Chips[1];
        Assert.NotEqual(chip.Label, chip.Value);
        vm.Chat.PickChipCommand.Execute(chip.Value);

        Answer(vm.Chat, "vendredi");
        Answer(vm.Chat, "rien");
        if (vm.PendingCompose is { } pending)
            await pending;

        var brief = Assert.Single(processes.Requests).Arguments[1];
        Assert.Contains(chip.Value, brief, StringComparison.Ordinal);
        Assert.DoesNotContain(chip.Label, brief, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stopping_keeps_every_turn_and_restarts_nothing()
    {
        var (vm, processes) = Build();
        await vm.ComposeCommand.ExecuteAsync();
        Answer(vm.Chat, "un dossier");

        var before = vm.Chat.Turns.Count;
        vm.Chat.StopCommand.Execute(null);

        Assert.Equal(before, vm.Chat.Turns.Count);
        Assert.True(vm.Chat.IsOpen);
        Assert.False(vm.Chat.IsBusy);
        Assert.Empty(processes.Requests);
    }

    [Fact]
    public async Task An_assistant_turn_arriving_on_a_closed_thread_is_counted_as_unread()
    {
        var (vm, _) = Build();
        await vm.ComposeCommand.ExecuteAsync();
        Answer(vm.Chat, "un dossier");
        Answer(vm.Chat, "vendredi");
        Answer(vm.Chat, "rien");
        if (vm.PendingCompose is { } pending)
            await pending;

        // The interview ended and closed the thread behind it.
        Assert.False(vm.Chat.IsOpen);
        Assert.Equal(0, vm.Chat.UnreadCount);

        vm.Chat.AddAssistantTurn("Une précision, s'il vous plaît.");
        Assert.Equal(1, vm.Chat.UnreadCount);
        Assert.True(vm.Chat.HasUnread);

        vm.Chat.OpenCommand.Execute(null);
        Assert.Equal(0, vm.Chat.UnreadCount);
        Assert.False(vm.Chat.HasUnread);
    }

    [Fact]
    public async Task The_recap_fills_in_as_the_answers_arrive()
    {
        var (vm, _) = Build();
        await vm.ComposeCommand.ExecuteAsync();

        var before = vm.Chat.Facts.Count(f => f.IsKnown);
        Answer(vm.Chat, "Documents/Comptes-rendus");

        var after = vm.Chat.Facts;
        Assert.Equal(before + 1, after.Count(f => f.IsKnown));
        Assert.Contains(after, f => f.Value == "Documents/Comptes-rendus" && f.IsKnown);
        // A fact nobody has answered reads as pending, not as an empty line.
        Assert.Contains(after, f => !f.IsKnown && f.Value.Length > 0);
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
            settingsStore: new FakeAppSettingsStore(),
            directories: new FakeDirectoryProbe(),
            targetProbe: new FakeTargetProbe(),
            picker: new FakePathPicker(),
            teamsRoot: Path.Combine(Path.GetTempPath(), "orkeon-chat-" + Guid.NewGuid().ToString("N")));

        // One instance for the window (T-01). A per-screen thread would lose its history
        // on the first tab change, which is the whole reason it is a window-lifetime object.
        Assert.Same(shell.Chat, shell.CreateTeam.Chat);

        // Exécuter and Tester run over SEPARATE launchers — the trial is a rehearsal with no
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
/// The hot language switch reaches the conversation too (recette §9: « aucun texte resté
/// en français »). A bubble the assistant said from the catalogue is rewritten; a bubble
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
            askEngine: _ => false,
            onInterviewComplete: _ => { });

        chat.StartInterview();
        chat.Draft = "Documents/Comptes-rendus";
        chat.SendCommand.Execute(null);

        var question = chat.Turns[0];
        var typed = chat.Turns[1];
        Assert.StartsWith("en:", question.Body, StringComparison.Ordinal);
        Assert.Equal("Documents/Comptes-rendus", typed.Body);

        strings.Switch("fr:");

        Assert.StartsWith("fr:", question.Body, StringComparison.Ordinal);
        Assert.Equal("Documents/Comptes-rendus", typed.Body);   // the user's words, untouched
        Assert.StartsWith("fr:", chat.Placeholder, StringComparison.Ordinal);
        Assert.StartsWith("fr:", chat.Primer, StringComparison.Ordinal);
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
        Action<IReadOnlyList<string>>? onDone = null,
        Orkeon.Studio.Wpf.ViewModels.Mvvm.IUiDelay? delay = null)
    {
        var chat = new ChatThreadViewModel(strings: null, delay: delay);
        chat.Bind(
            facts: () => [], brief: () => "", briefChips: () => [],
            profileName: () => null, askEngine: _ => false,
            onInterviewComplete: onDone ?? (_ => { }));
        return chat;
    }

    private static void Answer(ChatThreadViewModel chat, string text)
    {
        chat.Draft = text;
        chat.SendCommand.Execute(null);
    }

    [Fact]
    public void A_message_typed_during_the_closing_pause_cannot_cancel_the_hand_over()
    {
        // Send cancels the pending delays — and the delay pending at that exact moment is
        // the one that starts the engine. The thread would sit «done» for ever.
        IReadOnlyList<string>? handed = null;
        var chat = Thread(a => handed = a);

        chat.StartInterview();
        Answer(chat, "un dossier");
        Answer(chat, "vendredi");
        Answer(chat, "rien");

        Assert.NotNull(handed);
        Assert.Equal(3, handed!.Count);
        Assert.Equal("rien", handed[2]);
    }

    [Fact]
    public void Stopping_mid_thought_takes_the_unasked_question_with_it()
    {
        var delay = new ManualDelay();
        var chat = Thread(delay: delay);

        chat.StartInterview();
        delay.Elapse();                       // the first question is asked
        Assert.True(chat.IsAsking);

        Answer(chat, "un dossier");           // answered; the assistant starts thinking
        Assert.True(chat.IsBusy);
        Assert.False(chat.IsAsking);

        // Stop lands HERE, in the pause. The counter already points at question 2, but its
        // bubble is what makes it a question — and it was never pushed. Offering its three
        // quick replies would be an answer box for a question nobody asked.
        chat.StopCommand.Execute(null);

        Assert.False(chat.IsBusy);
        Assert.False(chat.IsAsking);
        Assert.Empty(chat.Chips);
        Assert.DoesNotContain(chat.Turns, t => t.IsClosing);
    }

    [Fact]
    public void A_message_typed_in_the_closing_pause_is_kept_and_the_engine_still_starts()
    {
        var delay = new ManualDelay();
        IReadOnlyList<string>? handed = null;
        var chat = Thread(a => handed = a, delay);

        chat.StartInterview();
        delay.Elapse();
        Answer(chat, "un dossier"); delay.Elapse();
        Answer(chat, "vendredi");   delay.Elapse();
        Answer(chat, "rien");       delay.Elapse();   // closing bubble, hand-over now pending

        Assert.Null(handed);
        chat.Draft = "une dernière chose";
        chat.SendCommand.Execute(null);

        // The send is refused rather than swallowed: the draft is still there to send a
        // second later, and the hand-over it would have cancelled still happens.
        Assert.Equal("une dernière chose", chat.Draft);
        delay.Elapse();
        Assert.NotNull(handed);
        Assert.Equal(3, handed!.Count);
    }

    [Fact]
    public void A_question_is_only_on_the_table_once_its_bubble_exists()
    {
        var chat = Thread();

        Assert.False(chat.IsAsking);
        chat.StartInterview();

        // The inline delay plays every beat at once, so the first bubble is already pushed.
        Assert.True(chat.IsAsking);
        Assert.Equal(3, chat.Chips.Count);
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
        chat.StartInterview();
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
        chat.StartInterview();
        var asked = 0;
        chat.EditBriefRequested += (_, _) => asked++;

        chat.CloseCommand.Execute(null);

        Assert.False(chat.IsOpen);
        Assert.Equal(0, asked);
    }
}
