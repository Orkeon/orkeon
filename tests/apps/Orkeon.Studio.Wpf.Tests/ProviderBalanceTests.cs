using System.Globalization;
using Orkeon.Constants.Llm;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The provider balance (STUDIO-35): the Balance segment of the status bar, the chip of each
/// profile row and the line of the profile editor, over one hand-written probe that counts what
/// it is asked. Each test pins one decision of the fiche: nothing is read without a trigger and
/// every trigger reads (D-02), an account is read once however many profiles share it (D-01), a
/// threshold crossed takes the warning tone (D-03), a provider that does not expose its balance
/// offers its console (D-01), no key is ever shown, and nothing read is written down (D-05).
/// </summary>
public sealed class ProviderBalanceTests
{
    private const string Crew = "/crews/veille.yaml";
    private const string DeepSeekKeyVariable = "DEEPSEEK_API_KEY";
    private const string Key = "sk-live-0123456789";

    private const string RunStarted =
        """{"v":2,"seq":1,"ts":"2026-09-24T10:29:00Z","kind":"run.started","target":"/crews/veille.yaml"}""";

    private static ModelProfile DeepSeek(string name, string model = "deepseek-chat") => new()
    {
        Name = name,
        Provider = "DeepSeek",
        Model = model,
        BaseUrl = LlmProviderEndpoints.DeepSeek,
        KeyEnvName = DeepSeekKeyVariable,
    };

    private static ModelProfile OpenAI(string name) => new()
    {
        Name = name,
        Provider = "OpenAI",
        Model = "gpt-4.1",
        BaseUrl = LlmProviderEndpoints.OpenAI,
        KeyEnvName = "OPENAI_API_KEY",
    };

    private static string Amount(decimal value, string currency) =>
        $"{value.ToString("N2", CultureInfo.CurrentCulture)} {currency}";

    private static ProcessOutputLine Event(string json) =>
        ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    private static (LaunchTabViewModel Tab, FakeProcessLauncher Process) Launcher()
    {
        var process = new FakeProcessLauncher();
        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                process, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = new FakeTargetProbe().WithFile(Crew),
            Directories = new FakeDirectoryProbe(),
            SettingsStore = new FakeAppSettingsStore(),
        });
        tab.Target.Select(Crew);
        return (tab, process);
    }

    private static ComposeProgress Composing(bool engineRunning) =>
        new(Stage: "blueprint", EngineRunning: engineRunning, WaitingOnUser: false, Finished: !engineRunning,
            Narration: null, FileCount: 0, ValidationOk: null, PromptTokens: 1840, CompletionTokens: 620,
            Estimated: false);

    // ── the triggers (D-02) ──

    [Fact]
    public void Nothing_is_read_until_a_trigger_asks()
    {
        var rig = new Rig();
        var bar = rig.Bar();

        rig.Add(DeepSeek("Rapide"));
        rig.Profiles.StudioProfileName = "Rapide";
        rig.Profiles.Profiles[0].DuplicateCommand.Execute(null);
        rig.Profiles.BeginEdit(rig.Profiles.Set.Profiles[0]);
        rig.Profiles.CancelEdit();

        // Covered, and said so — but not read: nothing asked for it yet.
        Assert.Empty(rig.Probe.Requests);
        Assert.False(rig.Ticker.IsRunning);
        var unread = Assert.Single(bar.Balance.Items);
        Assert.True(unread.IsUnread);
        Assert.Equal("DeepSeek", unread.Text);

        // A click is a trigger.
        unread.ClickCommand.Execute(null);

        Assert.Single(rig.Probe.Requests);
        Assert.Equal($"DeepSeek {Amount(110m, "CNY")}", Assert.Single(bar.Balance.Items).Text);
    }

    [Fact]
    public async Task The_window_reads_the_balance_once_at_startup()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"orkeon-balance-{Guid.NewGuid():N}")).FullName;
        try
        {
            // A team naming a third profile: the teams' accounts are covered too (D-01).
            var team = Path.Combine(root, "teams", "veille");
            Directory.CreateDirectory(team);
            TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = "Veille", Profile = "Routeur" });

            var store = new InMemoryModelProfileStore();
            await store.SaveAsync(new ModelProfileSet
            {
                Profiles =
                [
                    DeepSeek("Rapide"),
                    DeepSeek("Raisonneur", "deepseek-reasoner"),
                    new ModelProfile { Name = "Routeur", BaseUrl = LlmProviderEndpoints.OpenRouter, KeyEnvName = "OPENROUTER_API_KEY" },
                    new ModelProfile { Name = "Local", BaseUrl = "http://localhost:11434/v1" },
                ],
                DefaultProfile = "Rapide",
                StudioProfile = "Raisonneur",
            }, TestContext.Current.CancellationToken);

            var probe = new FakeProviderBalanceProbe();
            var keys = new FakeApiKeyStore();
            keys.Save(DeepSeekKeyVariable, Key);
            keys.Save("OPENROUTER_API_KEY", "sk-or-key");
            var window = new MainWindowViewModel(
                new StudioServices
                {
                    SettingsStore = new FakeAppSettingsStore(),
                    Directories = new FakeDirectoryProbe(),
                    TargetProbe = new FakeTargetProbe(),
                    Picker = new FakePathPicker(),
                    ProcessRunner = new OrkeonProcessRunner(
                        new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                    HistoryStore = new FakeLaunchHistoryStore(),
                    ProfileStore = store,
                    KeyStore = keys,
                    BalanceProbe = probe,
                },
                globalPathOverride: "/home/user/.config/Orkeon/appsettings.json",
                forgeWorkspace: Path.Combine(root, "forge"),
                teamsRoot: Path.Combine(root, "teams"));

            Assert.Empty(probe.Requests);

            await window.InitializeAsync(TestContext.Current.CancellationToken);

            // Two accounts: DeepSeek (the default and the assistant share it) and OpenRouter
            // (the team's). The local runtime has none, and the startup read asks each once.
            Assert.Equal(
                [LlmProviderEndpoints.DeepSeek, LlmProviderEndpoints.OpenRouter],
                probe.Requests.Select(request => request.BaseUrl));
            Assert.Equal(
                [$"DeepSeek {Amount(110m, "CNY")}", $"OpenRouter {Amount(110m, "CNY")}"],
                window.StatusBar.Balance.Items.Select(item => item.Text));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_run_a_trial_and_a_composition_each_read_the_balance_again_when_they_end()
    {
        var rig = new Rig();
        rig.Add(DeepSeek("Rapide"));
        var (launch, launchProcess) = Launcher();
        var (trial, trialProcess) = Launcher();
        var atelier = new ComposeProgressViewModel();
        var bar = rig.Bar(launch, trial, atelier);
        int? readWhileRunning = null;
        launchProcess.WhileRunning = () =>
        {
            launchProcess.Emit(Event(RunStarted));
            readWhileRunning = rig.Probe.Requests.Count;
        };
        trialProcess.WhileRunning = () => trialProcess.Emit(Event(RunStarted));

        await launch.RunAsync(TestContext.Current.CancellationToken);

        // Nothing is read while the run spends — its meters are the group's (STUDIO-34) — and
        // the balance is read again when it ends.
        Assert.Equal(0, readWhileRunning);
        Assert.Single(rig.Probe.Requests);

        await trial.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, rig.Probe.Requests.Count);

        atelier.Update(Composing(engineRunning: true));
        Assert.Equal(2, rig.Probe.Requests.Count);
        atelier.Update(Composing(engineRunning: false));
        Assert.Equal(3, rig.Probe.Requests.Count);
        Assert.True(bar.IsAtRest);
    }

    [Fact]
    public async Task A_read_under_way_is_joined_rather_than_asked_twice()
    {
        var rig = new Rig();
        rig.Add(DeepSeek("Rapide"));
        var bar = rig.Bar();
        rig.Probe.Gate = new TaskCompletionSource();

        var startup = bar.Balance.RefreshAsync(TestContext.Current.CancellationToken);
        var reading = Assert.Single(bar.Balance.Items);
        Assert.False(reading.ClickCommand.CanExecute(null));
        Assert.Contains("Reading the balance", bar.Balance.Detail, StringComparison.Ordinal);

        var click = bar.Balance.RefreshAsync(TestContext.Current.CancellationToken);
        rig.Probe.Gate.SetResult();
        await Task.WhenAll(startup, click);

        Assert.Single(rig.Probe.Requests);
        Assert.True(Assert.Single(bar.Balance.Items).ClickCommand.CanExecute(null));
    }

    [Fact]
    public void The_automatic_reading_is_off_by_default_and_follows_the_studio_settings()
    {
        var rig = new Rig();
        rig.Add(DeepSeek("Rapide"));
        _ = rig.Bar();

        Assert.False(rig.Ticker.IsRunning);
        Assert.Null(rig.Studio.SelectedRefresh.Minutes);

        rig.Studio.SelectedRefresh = rig.Studio.RefreshChoices.Single(choice => choice.Minutes == 15);

        Assert.True(rig.Ticker.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(15), rig.Ticker.Interval);
        Assert.Equal(15, Assert.Single(rig.Persisted).BalanceRefreshMinutes);

        rig.Ticker.Tick();
        Assert.Single(rig.Probe.Requests);

        rig.Studio.SelectedRefresh = rig.Studio.RefreshChoices[0];

        Assert.False(rig.Ticker.IsRunning);
        Assert.Null(rig.Persisted[^1].BalanceRefreshMinutes);
    }

    // ── the accounts (D-01) ──

    [Fact]
    public async Task Two_profiles_sharing_a_provider_and_a_key_cost_one_request()
    {
        var rig = new Rig();
        rig.Add(DeepSeek("Rapide"), DeepSeek("Raisonneur", "deepseek-reasoner"));
        rig.Profiles.StudioProfileName = "Raisonneur";
        rig.TeamProfiles.Add("Rapide");
        var bar = rig.Bar();

        await bar.Balance.RefreshAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(rig.Probe.Requests);
        Assert.Equal(Key, request.ApiKey);
        var entry = Assert.Single(bar.Balance.Items);
        Assert.Contains("(Rapide, Raisonneur)", entry.Line, StringComparison.Ordinal);
        // Each row of the account shows the same reading.
        Assert.All(rig.Profiles.Profiles, row => Assert.Equal(Amount(110m, "CNY"), row.Balance));
    }

    [Fact]
    public async Task A_provider_that_does_not_expose_its_balance_offers_its_console()
    {
        var rig = new Rig();
        rig.Add(OpenAI("Cloud"));
        rig.Probe.Answer = FakeProviderBalanceProbe.Without(
            ProviderBalanceStatus.NotExposed, "OpenAI serves no balance: its Costs API reports spend, and to an admin key only.");
        var bar = rig.Bar();

        await bar.Balance.RefreshAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(bar.Balance.Items);
        Assert.True(entry.OffersConsole);
        Assert.Equal("OpenAI", entry.Text);
        Assert.Contains("click to open its console", entry.Line, StringComparison.Ordinal);

        entry.ClickCommand.Execute(null);

        Assert.Equal(["https://platform.openai.com/api-keys"], rig.Opener.Opened);
        Assert.Single(rig.Probe.Requests);
        // No amount, so no chip on the row.
        Assert.False(Assert.Single(rig.Profiles.Profiles).HasBalance);
    }

    [Fact]
    public async Task A_refused_key_is_said_quietly_and_a_click_reads_again()
    {
        var rig = new Rig();
        rig.Add(DeepSeek("Rapide"));
        rig.Probe.Answer = FakeProviderBalanceProbe.Without(
            ProviderBalanceStatus.AuthenticationRefused, "The provider refused the key (401 Unauthorized).");
        var bar = rig.Bar();

        await bar.Balance.RefreshAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(bar.Balance.Items);
        Assert.True(entry.IsFaint);
        Assert.Equal("DeepSeek · key refused", entry.Text);
        Assert.Contains("401 Unauthorized", bar.Balance.Detail, StringComparison.Ordinal);

        entry.ClickCommand.Execute(null);

        Assert.Equal(2, rig.Probe.Requests.Count);
        Assert.Empty(rig.Opener.Opened);
    }

    // ── the threshold (D-03) ──

    [Fact]
    public async Task A_balance_under_its_threshold_takes_the_warning_tone()
    {
        var rig = new Rig();
        rig.Add(DeepSeek("Rapide"));
        var bar = rig.Bar();
        await bar.Balance.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.False(bar.Balance.IsWarning);

        rig.Studio.Thresholds.Single(row => row.Provider == LlmProviderKeys.DeepSeek).AmountText = "200";

        // The tone moves with the threshold, without a second request.
        Assert.True(bar.Balance.IsWarning);
        Assert.True(Assert.Single(bar.Balance.Items).IsWarning);
        Assert.True(Assert.Single(rig.Profiles.Profiles).IsBalanceLow);
        Assert.Contains("under your alert threshold of 200", bar.Balance.Detail, StringComparison.Ordinal);
        Assert.Single(rig.Probe.Requests);

        rig.Studio.Thresholds.Single(row => row.Provider == LlmProviderKeys.DeepSeek).AmountText = "50";

        Assert.False(bar.Balance.IsWarning);
        Assert.False(Assert.Single(rig.Profiles.Profiles).IsBalanceLow);
    }

    [Fact]
    public void A_threshold_is_typed_in_either_decimal_separator_and_a_text_that_is_no_amount_changes_nothing()
    {
        var rig = new Rig();
        var deepseek = rig.Studio.Thresholds.Single(row => row.Provider == LlmProviderKeys.DeepSeek);

        Assert.Equal(
            [LlmProviderKeys.DeepSeek, LlmProviderKeys.Kimi, LlmProviderKeys.OpenRouter],
            rig.Studio.Thresholds.Select(row => row.Provider));
        Assert.Equal("not read yet this session", deepseek.Hint);

        var announced = new List<string?>();
        rig.Studio.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        deepseek.AmountText = "12,50";
        Assert.Equal(12.50m, rig.Balances.ThresholdOf(LlmProviderKeys.DeepSeek));
        Assert.Equal(12.50m, rig.Persisted[^1].BalanceThresholds[LlmProviderKeys.DeepSeek]);
        Assert.Same(rig.Persisted[^1], rig.Studio.Current);
        Assert.Contains(nameof(StudioSettingsViewModel.Current), announced);

        deepseek.AmountText = "douze";
        Assert.True(deepseek.IsInvalid);
        Assert.Equal(12.50m, rig.Balances.ThresholdOf(LlmProviderKeys.DeepSeek));

        deepseek.AmountText = "";
        Assert.False(deepseek.IsInvalid);
        Assert.Null(rig.Balances.ThresholdOf(LlmProviderKeys.DeepSeek));
        Assert.Empty(rig.Persisted[^1].BalanceThresholds);
    }

    // ── no key shown, nothing written down ──

    /// <summary>
    /// The probe must present the key — and nothing on screen may repeat it, even from a probe
    /// that echoes it back: the readings are masked where they are filed.
    /// </summary>
    [Fact]
    public async Task No_key_is_ever_shown_even_by_a_probe_that_repeats_it()
    {
        var rig = new Rig();
        rig.Add(DeepSeek("Rapide"));
        rig.Probe.Answer = request => FakeProviderBalanceProbe.Without(
            ProviderBalanceStatus.AuthenticationRefused, $"The provider refused {request.ApiKey}.")(request);
        var bar = rig.Bar();

        await bar.Balance.RefreshAsync(TestContext.Current.CancellationToken);
        rig.Profiles.BeginEdit(rig.Profiles.Set.Profiles[0]);
        await rig.Profiles.Editor!.ReadBalanceAsync(TestContext.Current.CancellationToken);

        Assert.All(rig.Probe.Requests, request => Assert.Equal(Key, request.ApiKey));
        string?[] shown =
        [
            bar.Balance.Detail,
            .. bar.Balance.Items.SelectMany(item => new[] { item.Text, item.Line }),
            .. rig.Profiles.Profiles.SelectMany(row => new[] { row.Balance, row.BalanceTip }),
            rig.Profiles.Editor.BalanceResult,
            rig.Profiles.Editor.BalanceDetail,
            rig.Balances.Of(ProviderBalanceAccount.For(DeepSeek("Rapide")))?.Detail,
        ];
        Assert.All(shown, text => Assert.DoesNotContain(Key, text ?? "", StringComparison.Ordinal));
        Assert.Contains("***", rig.Profiles.Editor.BalanceDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reading_is_never_written_down()
    {
        var rig = new Rig();
        rig.Add(DeepSeek("Rapide"));
        var bar = rig.Bar();

        await bar.Balance.RefreshAsync(TestContext.Current.CancellationToken);

        // Nothing was persisted by the read, and what Settings › Studio writes holds no amount.
        Assert.Empty(rig.Persisted);
        var file = UiPreferencesDocument.Parse(null);
        file.SetStudio(rig.Balances.Settings);
        Assert.DoesNotContain("110", file.ToJson(), StringComparison.Ordinal);

        // A new window over the same settings starts with nothing read.
        var next = new BalanceReadings(rig.Probe, rig.Keys, rig.Balances.Settings);
        Assert.Null(next.Of(ProviderBalanceAccount.For(DeepSeek("Rapide"))));
    }

    // ── the profile rows and the editor (D-04) ──

    [Fact]
    public async Task The_profile_editor_reads_the_balance_beside_the_connection_test_and_the_row_learns_it()
    {
        var rig = new Rig();
        rig.Add(DeepSeek("Rapide"), OpenAI("Cloud"));
        rig.Profiles.BeginEdit(rig.Profiles.Set.Profiles[0]);
        var editor = rig.Profiles.Editor!;

        Assert.True(editor.ShowBalanceRow);
        Assert.Null(editor.BalanceResult);

        await editor.ReadBalanceAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(rig.Probe.Requests);
        Assert.Equal<(string?, string?)>((LlmProviderEndpoints.DeepSeek, Key), (request.BaseUrl, request.ApiKey));
        Assert.StartsWith($"{Amount(110m, "CNY")} available", editor.BalanceResult, StringComparison.Ordinal);
        Assert.Equal(Amount(110m, "CNY"), rig.Profiles.Profiles[0].Balance);
        Assert.False(rig.Profiles.Profiles[1].HasBalance);

        // Opened again, the editor shows what this session read.
        rig.Profiles.CancelEdit();
        rig.Profiles.BeginEdit(rig.Profiles.Set.Profiles[0]);
        Assert.StartsWith($"{Amount(110m, "CNY")} available", rig.Profiles.Editor!.BalanceResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_editor_refuses_to_read_without_a_key_and_offers_the_console_where_the_balance_is_not_exposed()
    {
        var rig = new Rig();
        rig.Add(OpenAI("Cloud"));
        rig.Profiles.BeginEdit(rig.Profiles.Set.Profiles[0]);
        var editor = rig.Profiles.Editor!;

        await editor.ReadBalanceAsync(TestContext.Current.CancellationToken);

        Assert.Empty(rig.Probe.Requests);
        Assert.Equal("API key missing — remember it first", editor.BalanceResult);

        rig.Probe.Answer = FakeProviderBalanceProbe.Without(ProviderBalanceStatus.NotExposed, "OpenAI serves no balance.");
        editor.ApiKeyInput = "sk-typed";
        await editor.ReadBalanceAsync(TestContext.Current.CancellationToken);

        Assert.Equal("sk-typed", Assert.Single(rig.Probe.Requests).ApiKey);
        Assert.True(editor.BalanceOffersConsole);
        editor.OpenBalanceConsoleCommand.Execute(null);
        Assert.Equal(["https://platform.openai.com/api-keys"], rig.Opener.Opened);

        // A key typed and not remembered is not the one a run presents: its reading stays the editor's.
        Assert.Null(rig.Balances.Of(ProviderBalanceAccount.For(OpenAI("Cloud"))));
    }

    [Fact]
    public void A_local_runtime_has_no_balance_line()
    {
        var rig = new Rig();
        rig.Add(new ModelProfile { Name = "Local", Provider = "Ollama", Model = "qwen3:8b", BaseUrl = "http://localhost:11434/v1" });

        rig.Profiles.BeginEdit(rig.Profiles.Set.Profiles[0]);

        Assert.False(rig.Profiles.Editor!.ShowBalanceRow);
    }

    // ── Settings › Studio ──

    [Fact]
    public void The_studio_tab_is_open_to_both_modes()
    {
        var rig = new Rig();
        var mode = new UiModeViewModel();
        var screen = new SettingsScreenViewModel(
            new ConfigTabViewModel(new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
            }),
            rig.Profiles,
            mode,
            studio: rig.Studio);

        screen.ShowStudioCommand.Execute(null);
        Assert.True(screen.IsStudioTab);

        mode.SetExpertCommand.Execute(null);
        mode.SetNoviceCommand.Execute(null);
        Assert.True(screen.IsStudioTab);
        Assert.Same(rig.Studio, screen.Studio);
    }

    /// <summary>Everything the balance reads through: one probe, one key store, one set of profiles, one Settings › Studio.</summary>
    private sealed class Rig
    {
        public Rig()
        {
            Keys.Save(DeepSeekKeyVariable, Key);
            Balances = new BalanceReadings(Probe, Keys);
            var document = AppSettingsDocument.CreateEmpty();
            var llm = new LlmSectionViewModel(() => document, () => { }, new FakeLlmEndpointProbe());
            Profiles = new ModelProfilesViewModel(
                new InMemoryModelProfileStore(), llm, probe: new FakeLlmEndpointProbe(), keyStore: Keys,
                balances: Balances, shellOpener: Opener);
            Studio = new StudioSettingsViewModel(Balances, Persisted.Add);
        }

        public FakeProviderBalanceProbe Probe { get; } = new();

        public FakeApiKeyStore Keys { get; } = new();

        public ManualUiTicker Ticker { get; } = new();

        public RecordingShellOpener Opener { get; } = new();

        public List<StudioSettings> Persisted { get; } = [];

        public List<string?> TeamProfiles { get; } = [];

        public BalanceReadings Balances { get; }

        public ModelProfilesViewModel Profiles { get; }

        public StudioSettingsViewModel Studio { get; }

        public StatusBarViewModel Bar(
            LaunchTabViewModel? launch = null,
            LaunchTabViewModel? trial = null,
            ComposeProgressViewModel? atelier = null) => new(new StatusBarSources
        {
            Launch = launch,
            Test = trial,
            Atelier = atelier,
            Profiles = Profiles,
            Balances = Balances,
            TeamProfiles = () => TeamProfiles,
            BalanceTicker = Ticker,
            ShellOpener = Opener,
        });

        /// <summary>Adds profiles the way the editor saves them; the first becomes the default.</summary>
        public void Add(params ModelProfile[] profiles)
        {
            foreach (var profile in profiles)
                Profiles.CommitEdit(profile, previousName: null);
        }
    }
}
