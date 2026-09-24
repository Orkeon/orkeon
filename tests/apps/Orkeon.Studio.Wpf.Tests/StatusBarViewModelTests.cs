using System.Globalization;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The bar at the foot of the window (STUDIO-34): one group per activity that runs — Run, Test,
/// the assistant — each only while it runs, none hidden, none merged (DD-2). What each test pins
/// is one thing the owner asked to see without changing screen: what runs, what it spends, and
/// the tools at work — or one thing the bar must refuse to invent.
/// </summary>
public sealed class StatusBarViewModelTests
{
    private const string Crew = "/crews/veille.yaml";

    private const string RunStarted =
        """{"v":2,"seq":1,"ts":"2026-09-24T10:29:00Z","kind":"run.started","target":"/crews/veille.yaml"}""";

    private const string TaskStarted =
        """{"v":2,"seq":2,"ts":"2026-09-24T10:29:01Z","kind":"task.started","taskId":"t1","agentRole":"analyst"}""";

    private const string FullMeter =
        """{"v":2,"seq":3,"ts":"2026-09-24T10:29:05Z","kind":"cost.updated","tokens":1500,"promptTokens":1200,"completionTokens":300,"cacheHitTokens":800,"cacheMissTokens":400,"cost":0.0123,"currency":"USD","costSource":"vendor","model":"deepseek-chat","provider":"deepseek"}""";

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static ProcessOutputLine Event(string json) =>
        ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    /// <summary>A launcher aimed at a crew file, over a child process the test scripts.</summary>
    private static (LaunchTabViewModel Tab, FakeProcessLauncher Process) Launcher(TimeProvider? clock = null)
    {
        var process = new FakeProcessLauncher();
        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                process, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = new FakeTargetProbe().WithFile(Crew).WithFile("/crews/other.yaml"),
            Directories = new FakeDirectoryProbe(),
            SettingsStore = new FakeAppSettingsStore(),
            Clock = clock,
        });
        tab.Target.Select(Crew);
        return (tab, process);
    }

    private static ModelProfilesViewModel Profiles()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { }, new FakeLlmEndpointProbe());
        return new ModelProfilesViewModel(new InMemoryModelProfileStore(), llm, probe: new FakeLlmEndpointProbe());
    }

    private static UiModeViewModel Expert() => new(UiModeViewModel.Expert);

    private static ComposeProgress Composing(
        bool engineRunning = true, bool waitingOnUser = false, long? tokensRemaining = 40_000) =>
        new(Stage: "blueprint", EngineRunning: engineRunning, WaitingOnUser: waitingOnUser, Finished: false,
            Narration: null, FileCount: 0, ValidationOk: null, PromptTokens: 1840, CompletionTokens: 620,
            Estimated: false, TokensRemaining: tokensRemaining);

    [Fact]
    public void Nothing_runs_so_the_bar_holds_no_group()
    {
        var (launch, _) = Launcher();
        var (trial, _) = Launcher();
        var bar = new StatusBarViewModel(new StatusBarSources
        {
            Launch = launch,
            Test = trial,
            Atelier = new ComposeProgressViewModel(),
        });

        Assert.True(bar.IsAtRest);
        Assert.False(bar.Launch.IsActive);
        Assert.False(bar.Test.IsActive);
        Assert.False(bar.Atelier.IsActive);
    }

    [Fact]
    public async Task A_run_puts_its_group_on_the_bar_and_the_bar_returns_to_rest_at_its_end()
    {
        var (launch, process) = Launcher();
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch });
        (bool Active, bool AtRest, string State, string? Up, string? Down)? during = null;
        process.WhileRunning = () =>
        {
            process.Emit(Event(RunStarted));
            process.Emit(Event(FullMeter));
            during = (bar.Launch.IsActive, bar.IsAtRest, bar.Launch.StateText, bar.Launch.TokensUp, bar.Launch.TokensDown);
        };

        await launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal((true, false, "running", N(1200), N(300)), during);
        Assert.False(bar.Launch.IsActive);
        Assert.True(bar.IsAtRest);
    }

    /// <summary>The acceptance criterion: a trial started on the Test screen during a run has a group of its own.</summary>
    [Fact]
    public async Task A_launch_and_a_trial_at_once_give_two_groups()
    {
        var (launch, launchProcess) = Launcher();
        var (trial, trialProcess) = Launcher();
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch, Test = trial });
        (bool Launch, bool Test, bool AtRest)? both = null;
        Task? trialRun = null;
        trialProcess.WhileRunning = () =>
        {
            trialProcess.Emit(Event(RunStarted));
            both = (bar.Launch.IsActive, bar.Test.IsActive, bar.IsAtRest);
        };
        launchProcess.WhileRunning = () =>
        {
            launchProcess.Emit(Event(RunStarted));
            trialRun = trial.RunAsync(TestContext.Current.CancellationToken);
        };

        await launch.RunAsync(TestContext.Current.CancellationToken);
        await trialRun!;

        Assert.Equal((true, true, false), both);
        Assert.True(bar.IsAtRest);
    }

    [Fact]
    public async Task A_run_waiting_for_an_answer_says_so_until_the_answer_lands()
    {
        var (launch, process) = Launcher();
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch });
        (bool Waiting, string State, string Tone)? asking = null;
        (bool Waiting, string State, string Tone)? answered = null;
        process.WhileRunning = () =>
        {
            process.Emit(Event(RunStarted));
            process.Emit(Event(
                """{"v":2,"seq":2,"ts":"2026-09-24T10:29:10Z","correlationId":"q-1","kind":"input.needed","inputKind":"text","prompt":"Which folder?"}"""));
            asking = (bar.Launch.IsWaiting, bar.Launch.StateText, bar.Launch.Tone);

            launch.Progress.AnswerText = "/docs";
            launch.Progress.AnswerCommand.Execute(null);
            answered = (bar.Launch.IsWaiting, bar.Launch.StateText, bar.Launch.Tone);
        };

        await launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal((true, "waiting for an answer", "waiting"), asking);
        Assert.Equal((false, "running", "running"), answered);
    }

    [Theory]
    [InlineData(true, "succeeded", "ok")]
    [InlineData(false, "failed", "fail")]
    public async Task A_run_that_reported_its_end_says_how_it_ended_until_the_process_exits(
        bool success, string state, string tone)
    {
        var (launch, process) = Launcher();
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch });
        (string State, string Tone)? ended = null;
        process.WhileRunning = () =>
        {
            process.Emit(Event(RunStarted));
            process.Emit(Event(
                $$"""{"v":2,"seq":2,"ts":"2026-09-24T10:30:00Z","kind":"run.finished","success":{{(success ? "true" : "false")}},"exitCode":0,"durationMs":60000}"""));
            ended = (bar.Launch.StateText, bar.Launch.Tone);
        };

        await launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal((state, tone), ended);
        Assert.False(bar.Launch.IsActive);
    }

    /// <summary>
    /// D-03: the novice reads the state and the two meters; the expert reads everything. The
    /// switch moves the bar at once — a mode change that waits for the next event is one the
    /// user sees as broken.
    /// </summary>
    [Fact]
    public async Task Novice_mode_shows_the_state_and_the_meters_and_expert_mode_shows_everything()
    {
        var mode = new UiModeViewModel();
        var (launch, process) = Launcher(new StubTimeProvider { Now = new DateTimeOffset(2026, 9, 24, 10, 30, 0, TimeSpan.Zero) });
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch, Mode = mode });
        var raised = new List<string?>();
        (bool Up, bool Down, bool Details)? novice = null;
        (bool Details, string Line, bool Raised)? expert = null;
        process.WhileRunning = () =>
        {
            process.Emit(Event(RunStarted));
            process.Emit(Event(TaskStarted));
            process.Emit(Event(FullMeter));
            process.Emit(Event(
                """{"v":2,"seq":4,"ts":"2026-09-24T10:29:30Z","correlationId":"k-1","kind":"tool.called","toolName":"pdf_reader"}"""));
            novice = (bar.Launch.ShowsTokensUp, bar.Launch.ShowsTokensDown, bar.Launch.ShowsDetails);

            bar.Launch.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            mode.SetExpertCommand.Execute(null);
            expert = (bar.Launch.ShowsDetails, bar.Launch.Details,
                raised.Contains(nameof(StatusBarRunGroupViewModel.ShowsDetails)));
        };

        await launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal((true, true, false), novice);
        Assert.Equal(
            (true,
             $"veille · analyst · 1 min 0 s · cache 67 % · {N(800)} tokens · {0.0123m.ToString("0.######", CultureInfo.CurrentCulture)} USD · 1 tool(s): pdf_reader · deepseek · deepseek-chat",
             true),
            expert);
    }

    /// <summary>
    /// During a run the group names the model the meter reported (STUDIO-29), never a profile it
    /// guessed: a team can run on another profile than the default.
    /// </summary>
    [Fact]
    public async Task The_group_names_the_model_and_the_provider_the_meter_reported()
    {
        var (launch, process) = Launcher();
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch, Mode = Expert() });
        string? reported = null;
        process.WhileRunning = () =>
        {
            process.Emit(Event(RunStarted));
            process.Emit(Event(FullMeter));
            reported = bar.Launch.ReportedModel;
        };

        await launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal("deepseek · deepseek-chat", reported);
    }

    [Fact]
    public async Task Parallel_tools_show_their_count_and_the_first_name_with_the_whole_list_on_hover()
    {
        var (launch, process) = Launcher();
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch, Mode = Expert() });
        (string? Tools, string? List)? two = null;
        string? one = null;
        string? none = "unread";
        process.WhileRunning = () =>
        {
            process.Emit(Event(RunStarted));
            process.Emit(Event(
                """{"v":2,"seq":2,"ts":"2026-09-24T10:29:10Z","correlationId":"k-1","kind":"tool.called","toolName":"pdf_reader"}"""));
            process.Emit(Event(
                """{"v":2,"seq":3,"ts":"2026-09-24T10:29:12Z","correlationId":"k-2","kind":"tool.called","toolName":"web_search"}"""));
            two = (bar.Launch.Tools, bar.Launch.ToolsDetail);

            // The first call comes back first: the one still at work is the one named.
            process.Emit(Event(
                """{"v":2,"seq":4,"ts":"2026-09-24T10:29:20Z","correlationId":"k-1","kind":"tool.returned","toolName":"pdf_reader","success":true}"""));
            one = bar.Launch.Tools;

            process.Emit(Event(
                """{"v":2,"seq":5,"ts":"2026-09-24T10:29:25Z","correlationId":"k-2","kind":"tool.returned","toolName":"web_search","success":true}"""));
            none = bar.Launch.Tools;
        };

        await launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal("2 tool(s): pdf_reader", two?.Tools);
        Assert.Contains("pdf_reader", two?.List, StringComparison.Ordinal);
        Assert.Contains("web_search", two?.List, StringComparison.Ordinal);
        Assert.Equal("1 tool(s): web_search", one);
        Assert.Null(none);
    }

    [Fact]
    public async Task A_delegation_is_counted_until_the_delegate_comes_back()
    {
        var (launch, process) = Launcher();
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch, Mode = Expert() });
        (string? Count, string? List)? handedOver = null;
        string? back = "unread";
        process.WhileRunning = () =>
        {
            process.Emit(Event(RunStarted));
            process.Emit(Event(
                """{"v":2,"seq":2,"ts":"2026-09-24T10:29:10Z","correlationId":"d-1","kind":"delegation.started","toRole":"Writer"}"""));
            handedOver = (bar.Launch.Delegations, bar.Launch.DelegationsDetail);

            process.Emit(Event(
                """{"v":2,"seq":3,"ts":"2026-09-24T10:29:40Z","correlationId":"d-1","kind":"tool.returned","toolName":"delegate_work","success":true}"""));
            back = bar.Launch.Delegations;
        };

        await launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal("1 delegation(s)", handedOver?.Count);
        Assert.Contains("Writer", handedOver?.List, StringComparison.Ordinal);
        Assert.Null(back);
    }

    /// <summary>
    /// No figure is invented: a meter that carries no split shows no ↑/↓, a vendor that billed
    /// nothing shows no cost, a run with no tool at work shows no tool — absent, never a zero.
    /// </summary>
    [Fact]
    public async Task A_measure_the_run_did_not_report_stays_off_the_bar()
    {
        var (launch, process) = Launcher(new StubTimeProvider { Now = new DateTimeOffset(2026, 9, 24, 10, 30, 0, TimeSpan.Zero) });
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch, Mode = Expert() });
        string?[]? unmeasured = null;
        (bool ShowsUp, bool ShowsDown, string Details)? shown = null;
        process.WhileRunning = () =>
        {
            process.Emit(Event(RunStarted));
            // An older meter: the total only — no split, no charge, no model.
            process.Emit(Event("""{"v":2,"seq":2,"ts":"2026-09-24T10:29:05Z","kind":"cost.updated","tokens":1500}"""));
            var group = bar.Launch;
            unmeasured =
            [
                group.TokensUp, group.TokensDown, group.Cache, group.BilledCost,
                group.Tools, group.Delegations, group.ReportedModel, group.CurrentTask,
            ];
            shown = (group.ShowsTokensUp, group.ShowsTokensDown, group.Details);
        };

        await launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(unmeasured);
        Assert.All(unmeasured, Assert.Null);
        // What WAS measured: the team the run is for, and how long it has been going.
        Assert.Equal((false, false, "veille · 1 min 0 s"), shown);
    }

    /// <summary>
    /// The elapsed time moves between events — the run says nothing while a model thinks — so
    /// the bar refreshes it on its own beat, and keeps no beat at rest.
    /// </summary>
    [Fact]
    public async Task The_elapsed_time_moves_on_the_beat_without_an_event()
    {
        var clock = new StubTimeProvider { Now = new DateTimeOffset(2026, 9, 24, 10, 30, 0, TimeSpan.Zero) };
        var ticker = new ManualUiTicker();
        var (launch, process) = Launcher(clock);
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch, Mode = Expert(), Ticker = ticker });
        var raised = new List<string?>();
        bool? beatingDuringRun = null;
        string? before = null;
        bool? silentWithoutBeat = null;
        (bool Raised, string? Duration)? afterBeat = null;
        process.WhileRunning = () =>
        {
            process.Emit(Event(RunStarted));
            beatingDuringRun = ticker.IsRunning;
            before = bar.Launch.Duration;

            bar.Launch.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            clock.Now = clock.Now.AddSeconds(30);
            silentWithoutBeat = !raised.Contains(nameof(StatusBarRunGroupViewModel.Duration));

            ticker.Tick();
            afterBeat = (raised.Contains(nameof(StatusBarRunGroupViewModel.Duration)), bar.Launch.Duration);
        };

        Assert.False(ticker.IsRunning);

        await launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(beatingDuringRun);
        Assert.Equal(TimeSpan.FromSeconds(1), ticker.Interval);
        Assert.Equal("1 min 0 s", before);
        Assert.True(silentWithoutBeat);
        Assert.Equal((true, "1 min 30 s"), afterBeat);
        Assert.False(ticker.IsRunning);
    }

    [Fact]
    public async Task The_group_keeps_the_team_it_started_with_when_the_launcher_is_aimed_elsewhere()
    {
        var (launch, process) = Launcher();
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch, Mode = Expert() });
        string? team = null;
        process.WhileRunning = () =>
        {
            process.Emit(Event(RunStarted));
            launch.Target.Select("/crews/other.yaml");
            team = bar.Launch.Team;
        };

        await launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal("veille", team);
    }

    /// <summary>A token delta arrives per token: the bar shows no generated text, so it moves nothing.</summary>
    [Fact]
    public async Task A_token_delta_moves_nothing_on_the_bar()
    {
        var (launch, process) = Launcher();
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch, Mode = Expert() });
        int? raisedByTheDelta = null;
        process.WhileRunning = () =>
        {
            process.Emit(Event(RunStarted));
            var raised = 0;
            bar.Launch.PropertyChanged += (_, _) => raised++;
            process.Emit(Event("""{"v":2,"seq":2,"ts":"2026-09-24T10:29:02Z","kind":"llm.delta","text":"Hel"}"""));
            raisedByTheDelta = raised;
        };

        await launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, raisedByTheDelta);
    }

    [Fact]
    public void The_assistant_group_follows_a_composition()
    {
        var card = new ComposeProgressViewModel();
        var bar = new StatusBarViewModel(new StatusBarSources { Atelier = card, Mode = Expert() });

        card.Update(Composing());

        Assert.True(bar.Atelier.IsActive);
        Assert.False(bar.IsAtRest);
        Assert.Equal("I am composing the team…", bar.Atelier.Stage);
        Assert.Equal(N(1840), bar.Atelier.TokensUp);
        Assert.Equal(N(620), bar.Atelier.TokensDown);
        Assert.True(bar.Atelier.ShowsTokensUp);
        Assert.True(bar.Atelier.ShowsTokensDown);
        Assert.Equal($"{N(40_000)} tokens left", bar.Atelier.BudgetLeft);
        Assert.True(bar.Atelier.ShowsBudgetLeft);

        card.Update(Composing(waitingOnUser: true));

        Assert.True(bar.Atelier.IsWaiting);
        Assert.Equal("I am waiting for your answer.", bar.Atelier.Stage);

        card.Update(Composing(engineRunning: false));

        Assert.False(bar.Atelier.IsActive);
        Assert.True(bar.IsAtRest);
    }

    [Fact]
    public void The_assistant_group_shows_its_budget_to_the_expert_only_and_only_when_the_session_has_one()
    {
        var mode = new UiModeViewModel();
        var card = new ComposeProgressViewModel();
        var bar = new StatusBarViewModel(new StatusBarSources { Atelier = card, Mode = mode });

        card.Update(Composing());
        Assert.False(bar.Atelier.ShowsBudgetLeft);
        Assert.True(bar.Atelier.ShowsTokensUp);

        mode.SetExpertCommand.Execute(null);
        Assert.True(bar.Atelier.ShowsBudgetLeft);

        card.Update(Composing(tokensRemaining: null));
        Assert.Null(bar.Atelier.BudgetLeft);
        Assert.False(bar.Atelier.ShowsBudgetLeft);
    }

    /// <summary>D-04: a click on a group opens its activity's screen; the Test screen is the expert's.</summary>
    [Fact]
    public void A_click_on_a_group_asks_for_the_screen_of_its_activity()
    {
        var mode = Expert();
        var bar = new StatusBarViewModel(new StatusBarSources { Mode = mode });
        var asked = new List<StatusBarActivity>();
        bar.OpenRequested += (_, e) => asked.Add(e.Activity);

        bar.Launch.OpenCommand.Execute(null);
        bar.Test.OpenCommand.Execute(null);
        bar.Atelier.OpenCommand.Execute(null);

        Assert.Equal([StatusBarActivity.Launch, StatusBarActivity.Test, StatusBarActivity.Atelier], asked);

        var requeried = false;
        bar.Test.OpenCommand.CanExecuteChanged += (_, _) => requeried = true;
        mode.SetNoviceCommand.Execute(null);

        // The novice has no Test screen to land on: the group stays on the bar, and says so by
        // not answering the click rather than by a gesture that goes nowhere.
        Assert.True(requeried);
        Assert.False(bar.Test.OpenCommand.CanExecute(null));
        Assert.True(bar.Launch.OpenCommand.CanExecute(null));
        Assert.True(bar.Atelier.OpenCommand.CanExecute(null));
    }

    /// <summary>
    /// At rest the expert reads the default profile — there is no «active» one: each team picks
    /// its own, and during a run only the meter's model is true.
    /// </summary>
    [Fact]
    public async Task At_rest_the_expert_bar_names_the_default_profile_and_a_run_takes_its_place()
    {
        var profiles = Profiles();
        var mode = Expert();
        var (launch, process) = Launcher();
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch, Profiles = profiles, Mode = mode });

        Assert.Null(bar.Profile);
        Assert.False(bar.ShowsProfile);

        profiles.CommitEdit(
            new ModelProfile { Name = "Cloud", Provider = "DeepSeek", Model = "deepseek-chat" },
            previousName: null);

        Assert.Equal("DeepSeek · deepseek-chat", bar.Profile);
        Assert.Contains("Cloud", bar.ProfileTip, StringComparison.Ordinal);
        Assert.True(bar.ShowsProfile);

        bool? duringRun = null;
        process.WhileRunning = () =>
        {
            process.Emit(Event(RunStarted));
            duringRun = bar.ShowsProfile;
        };
        await launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.False(duringRun);
        Assert.True(bar.ShowsProfile);

        mode.SetNoviceCommand.Execute(null);
        Assert.False(bar.ShowsProfile);
    }

    /// <summary>
    /// The Balance segment is STUDIO-35's (see <c>ProviderBalanceTests</c>): a bar built without
    /// the balances — a probe and what it read — has no segment, whatever the profiles cover.
    /// </summary>
    [Fact]
    public void The_balance_segment_stays_off_a_bar_built_without_the_balances()
    {
        var profiles = Profiles();
        var bar = new StatusBarViewModel(new StatusBarSources { Profiles = profiles });

        profiles.CommitEdit(
            new ModelProfile { Name = "Cloud", Provider = "DeepSeek", Model = "deepseek-chat", BaseUrl = "https://api.deepseek.com" },
            previousName: null);

        Assert.False(bar.Balance.HasBalance);
        Assert.Empty(bar.Balance.Items);
        Assert.Null(bar.Balance.Detail);
    }

    [Fact]
    public async Task The_state_is_said_again_in_the_new_language_after_a_switch()
    {
        var strings = new SwitchableStrings();
        var (launch, process) = Launcher();
        var bar = new StatusBarViewModel(new StatusBarSources { Launch = launch, Strings = strings });
        var raised = new List<string?>();
        string? french = null;
        process.WhileRunning = () =>
        {
            process.Emit(Event(RunStarted));
            bar.Launch.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            strings.SwitchToFrench();
            french = bar.Launch.StateText;
        };

        await launch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Contains(nameof(StatusBarRunGroupViewModel.StateText), raised);
        Assert.Equal("en cours", french);
    }

    /// <summary>A port that flips from the English defaults to a one-line French table.</summary>
    private sealed class SwitchableStrings : IStudioStrings
    {
        private bool _french;

        public string this[string key] =>
            _french && key == StudioStringKeys.RunBadgeRunning ? "en cours" : EnglishStudioStrings.Instance[key];

        public event EventHandler? CultureChanged;

        public void SwitchToFrench()
        {
            _french = true;
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
