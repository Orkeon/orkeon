using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.UseCases;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-39: step 1's use-case gallery over a scripted <c>orkeon usecases</c> — the catalogue the
/// CLI answers, filtered here; a chosen case filling the need in the UI's language and attached as
/// the reference; the suggestions under the need, asked once the typing pauses, of one session
/// that lives as long as the wizard; and, without the CLI, the « engine not found » card.
/// </summary>
public partial class CreateTeamWizardTests
{
    private static readonly FakeUseCase EmailTriage = new(
        "03-email-pipeline",
        "01-enterprise",
        "sequential",
        Texts("Tri et réponse aux e-mails", "Email triage and replies", "邮件分拣与回复"),
        Texts("Trier mes e-mails et préparer les réponses", "Sort my emails and draft the replies", "整理我的邮件并起草回复"));

    private static readonly FakeUseCase CompetitiveWatch = new(
        "06-competitive-intelligence",
        "01-enterprise",
        "sequential",
        Texts("Veille concurrentielle", "Competitive intelligence", "竞争情报"),
        Texts("Surveiller ce que publient mes concurrents", "Watch what my competitors publish", "关注竞争对手发布的内容"),
        RequiresNetwork: true);

    private static readonly FakeUseCase SourcedAnswers = new(
        "16-interactive-qa",
        "01-enterprise",
        "sequential",
        Texts("Réponses avec sources", "Answers with sources", "附来源的回答"),
        Texts("Répondre à une question en citant le web", "Answer a question, citing the web", "引用网络来回答问题"),
        RequiresNetwork: true,
        RequiresKeys: ["ORKEON_TAVILY_API_KEY"]);

    private static readonly FakeUseCase TradingRoom = new(
        "31-algo-trading",
        "03-finance-trading",
        "hierarchical",
        Texts("Salle des marchés simulée", "Simulated trading room", "模拟交易室"),
        Texts("Tester une stratégie de trading sur un marché simulé", "Try a trading strategy on a simulated market", "在模拟市场上测试交易策略"),
        RequiresNetwork: true,
        Importable: false);

    private static readonly FakeUseCase Tutor = new(
        "56-adaptive-tutor",
        "05-education",
        "sequential",
        Texts("Tutorat personnalisé", "Personal tutoring", "个性化辅导"),
        Texts("Accompagner un élève à son rythme", "Coach a student at their own pace", "按学生的节奏辅导"));

    private static readonly FakeUseCase SmartHome = new(
        "86-smart-home-a2a",
        "08-iot-smart-systems",
        "parallel",
        Texts("Maison connectée coordonnée", "Coordinated smart home", "协同智能家居"),
        Texts("Faire travailler ensemble les appareils de la maison", "Make the devices of the house work together", "让家中设备协同工作"),
        RequiresNetwork: true);

    private static Dictionary<string, string> Texts(string fr, string en, string zh) =>
        new(StringComparer.Ordinal) { ["fr"] = fr, ["en"] = en, ["zh-Hans"] = zh };

    private static FakeUseCaseCli UseCaseCli()
    {
        var cli = new FakeUseCaseCli();
        cli.Catalog.AddRange([EmailTriage, CompetitiveWatch, SourcedAnswers, TradingRoom, Tutor, SmartHome]);
        return cli;
    }

    /// <summary>The wizard over <paramref name="cli"/>, speaking <paramref name="language"/> (Studio's spelling: <c>zh</c>).</summary>
    private static CreateTeamViewModel WizardWithGallery(
        FakeUseCaseCli cli,
        string language = "fr",
        ManualUiDelay? delay = null,
        bool cliInstalled = true,
        Func<string>? uiLanguage = null,
        IStudioStrings? strings = null)
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { }, new FakeLlmEndpointProbe());
        var profiles = new ModelProfilesViewModel(new InMemoryModelProfileStore(), llm, probe: new FakeLlmEndpointProbe());
        var locator = new OrkeonBinaryLocator(cliInstalled
            ? FakeExecutableProbe.WithOrkeonInstalled()
            : new FakeExecutableProbe("/opt/orkeon"));

        return new CreateTeamViewModel(
            profiles,
            new CreateTeamDependencies
            {
                Client = new Orkeon.Studio.Core.Forge.ForgeClient(new FakeProcessLauncher(), locator),
                WorkspaceDirectory = "/ws",
                TeamsRoot = "/teams",
                UseCases = new UseCaseClient(cli, locator),
                SuggestionDelay = delay,
                UiLanguage = uiLanguage ?? (() => language),
                Strings = strings,
            });
    }

    [Fact]
    public async Task The_gallery_shows_the_catalogue_the_cli_answers_and_the_link_says_how_many()
    {
        var cli = UseCaseCli();
        var vm = WizardWithGallery(cli);
        Assert.Equal("Browse the use cases", vm.BrowseUseCasesLabel);

        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Browse the use cases (6)", vm.BrowseUseCasesLabel);
        Assert.True(vm.CanBrowseUseCases);
        Assert.Equal(["usecases", "list", "--events", "jsonl"], Assert.Single(cli.Requests).Arguments);

        vm.BrowseUseCasesCommand.Execute(null);

        Assert.True(vm.Gallery.IsOpen);
        Assert.Equal(6, vm.Gallery.Cards.Count);
        Assert.Equal("6 of 6 use cases", vm.Gallery.CountLabel);
        var card = vm.Gallery.Cards[0];
        Assert.Equal("Tri et réponse aux e-mails", card.Title);
        Assert.Equal("Trier mes e-mails et préparer les réponses", card.Problem);
        Assert.Equal("Step by step", card.ProcessLabel);
        Assert.Equal("Enterprise", card.CategoryLabel);
        // Loaded once: opening the panel again reads nothing more.
        Assert.Equal(1, cli.ListRuns);
    }

    [Fact]
    public async Task The_gallery_filters_by_category()
    {
        var vm = WizardWithGallery(UseCaseCli());
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);
        vm.BrowseUseCasesCommand.Execute(null);

        Assert.Equal(
            ["All", "Enterprise", "Finance and trading", "Education", "IoT and smart systems"],
            vm.Gallery.Categories.Select(chip => chip.Label));
        Assert.True(vm.Gallery.Categories[0].IsSelected);

        vm.Gallery.Categories.Single(chip => chip.Key == "03-finance-trading").SelectCommand.Execute(null);

        Assert.Equal(["31-algo-trading"], vm.Gallery.Cards.Select(card => card.Id));
        Assert.True(vm.Gallery.Categories.Single(chip => chip.Key == "03-finance-trading").IsSelected);
        Assert.False(vm.Gallery.Categories[0].IsSelected);

        vm.Gallery.Categories.Single(chip => chip.Key == "01-enterprise").SelectCommand.Execute(null);

        Assert.Equal(["03-email-pipeline", "06-competitive-intelligence", "16-interactive-qa"], vm.Gallery.Cards.Select(card => card.Id));

        vm.Gallery.Categories[0].SelectCommand.Execute(null);

        Assert.Equal(6, vm.Gallery.Cards.Count);
    }

    [Fact]
    public async Task The_gallery_filters_by_process_by_web_access_and_by_third_party_key()
    {
        var vm = WizardWithGallery(UseCaseCli());
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);
        vm.BrowseUseCasesCommand.Execute(null);

        Assert.Equal(["Any", "Step by step", "With a lead", "In parallel"], vm.Gallery.Processes.Select(chip => chip.Label));
        vm.Gallery.Processes.Single(chip => chip.Key == "parallel").SelectCommand.Execute(null);
        Assert.Equal(["86-smart-home-a2a"], vm.Gallery.Cards.Select(card => card.Id));
        vm.Gallery.Processes[0].SelectCommand.Execute(null);

        vm.Gallery.WithoutWeb = true;
        Assert.Equal(["03-email-pipeline", "56-adaptive-tutor"], vm.Gallery.Cards.Select(card => card.Id));
        vm.Gallery.WithoutWeb = false;

        vm.Gallery.WithoutKeys = true;
        Assert.DoesNotContain(vm.Gallery.Cards, card => card.Id == "16-interactive-qa");
        Assert.Equal(5, vm.Gallery.Cards.Count);

        vm.Gallery.Query = "zzz";
        Assert.True(vm.Gallery.HasNoMatch);
        vm.Gallery.ClearFiltersCommand.Execute(null);
        Assert.Equal(6, vm.Gallery.Cards.Count);
        Assert.False(vm.Gallery.WithoutKeys);
    }

    [Fact]
    public async Task The_search_box_finds_a_card_by_the_start_of_its_words_accents_aside()
    {
        var vm = WizardWithGallery(UseCaseCli());
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);
        vm.BrowseUseCasesCommand.Execute(null);

        vm.Gallery.Query = "veille";
        Assert.Equal(["06-competitive-intelligence"], vm.Gallery.Cards.Select(card => card.Id));

        vm.Gallery.Query = "reponse";   // «réponse», typed without its accent
        Assert.Equal(["03-email-pipeline", "16-interactive-qa"], vm.Gallery.Cards.Select(card => card.Id));

        vm.Gallery.Query = "tri e-mail";   // every word must start a word of the card
        Assert.Equal(["03-email-pipeline"], vm.Gallery.Cards.Select(card => card.Id));
    }

    [Fact]
    public async Task Choosing_a_case_fills_the_need_in_the_ui_language_and_attaches_the_reference()
    {
        var vm = WizardWithGallery(UseCaseCli(), language: "fr");
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);
        vm.Need = "quelque chose que j'avais tapé";
        vm.BrowseUseCasesCommand.Execute(null);

        vm.Gallery.Cards.Single(card => card.Id == "06-competitive-intelligence").ChooseCommand.Execute(null);

        Assert.Equal("Surveiller ce que publient mes concurrents", vm.Need);
        Assert.Equal("06-competitive-intelligence", vm.ReferenceUseCaseId);
        Assert.True(vm.HasReferenceUseCase);
        Assert.Equal("Inspired by: Veille concurrentielle", vm.ReferenceUseCaseLabel);
        Assert.False(vm.Gallery.IsOpen);
    }

    [Fact]
    public async Task Choosing_a_case_in_chinese_fills_the_need_in_simplified_chinese()
    {
        // Studio's language switch says «zh»; the catalogue says «zh-Hans».
        var vm = WizardWithGallery(UseCaseCli(), language: "zh");
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);
        vm.BrowseUseCasesCommand.Execute(null);

        Assert.Equal("邮件分拣与回复", vm.Gallery.Cards[0].Title);

        vm.Gallery.Cards[0].ChooseCommand.Execute(null);

        Assert.Equal("整理我的邮件并起草回复", vm.Need);
        Assert.Equal("03-email-pipeline", vm.ReferenceUseCaseId);
        Assert.Equal("Inspired by: 邮件分拣与回复", vm.ReferenceUseCaseLabel);
    }

    [Fact]
    public async Task Removing_the_chip_detaches_the_reference_and_keeps_the_need()
    {
        var vm = WizardWithGallery(UseCaseCli());
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);
        vm.BrowseUseCasesCommand.Execute(null);
        vm.Gallery.Cards[0].ChooseCommand.Execute(null);
        vm.Need += " — chaque lundi";
        Assert.True(vm.RemoveReferenceUseCaseCommand.CanExecute(null));

        vm.RemoveReferenceUseCaseCommand.Execute(null);

        Assert.Null(vm.ReferenceUseCaseId);
        Assert.False(vm.HasReferenceUseCase);
        Assert.Equal("Trier mes e-mails et préparer les réponses — chaque lundi", vm.Need);
        Assert.False(vm.RemoveReferenceUseCaseCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_reference_only_case_stays_browsable_and_usable_as_a_reference()
    {
        var vm = WizardWithGallery(UseCaseCli());
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);
        vm.BrowseUseCasesCommand.Execute(null);
        var trading = vm.Gallery.Cards.Single(card => card.Id == "31-algo-trading");

        Assert.True(trading.IsReferenceOnly);
        Assert.False(vm.Gallery.Cards.Single(card => card.Id == "03-email-pipeline").IsReferenceOnly);

        trading.ChooseCommand.Execute(null);

        Assert.Equal("31-algo-trading", vm.ReferenceUseCaseId);
        Assert.Equal("Tester une stratégie de trading sur un marché simulé", vm.Need);
    }

    [Fact]
    public async Task Suggestions_ask_the_cli_once_the_typing_pauses_never_on_every_keystroke()
    {
        var cli = UseCaseCli();
        cli.Answer = _ => [new FakeUseCaseResult("03-email-pipeline", "terms", "trier", "mes")];
        var delay = new ManualUiDelay();
        var vm = WizardWithGallery(cli, delay: delay);
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);

        foreach (var typed in (string[])["t", "tr", "tri", "trie", "trier", "trier mes e-mails"])
            vm.Need = typed;

        // Six keystrokes, one pause pending, not one query sent.
        Assert.Equal(1, delay.Pending);
        Assert.Empty(cli.Queries);
        Assert.All(delay.Requested, pause => Assert.Equal(CreateTeamViewModel.SuggestionPause, pause));

        delay.Elapse();

        Assert.Equal(["trier mes e-mails"], cli.Queries);
        Assert.True(vm.HasCloseUseCases);
        Assert.Equal("1 close use case", vm.CloseUseCasesLabel);
        Assert.Equal("Tri et réponse aux e-mails", vm.CloseUseCasesTitles);
    }

    [Fact]
    public async Task A_suggestion_needs_a_term_few_cases_share_so_a_plain_sentence_suggests_nothing()
    {
        var cli = UseCaseCli();
        // «mes» is in two sheets of six — not distinctive; «trier» is in one.
        cli.Answer = text => text.StartsWith("trier", StringComparison.Ordinal)
            ? [new FakeUseCaseResult("03-email-pipeline", "terms", "trier", "mes"), new FakeUseCaseResult("06-competitive-intelligence", "terms", "mes")]
            : [new FakeUseCaseResult("06-competitive-intelligence", "terms", "mes"), new FakeUseCaseResult("56-adaptive-tutor", "meaning")];
        var vm = WizardWithGallery(cli);
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);

        vm.Need = "trier mes documents";
        Assert.Equal(["03-email-pipeline"], vm.CloseUseCases.Select(card => card.Id));

        vm.Need = "voici mes idées";
        Assert.False(vm.HasCloseUseCases);
    }

    [Fact]
    public async Task The_close_cases_open_the_gallery_on_themselves()
    {
        var cli = UseCaseCli();
        cli.Answer = _ => [new FakeUseCaseResult("06-competitive-intelligence", "terms", "concurrents"), new FakeUseCaseResult("03-email-pipeline", "terms", "trier")];
        var vm = WizardWithGallery(cli);
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);
        vm.Need = "trier ce que publient mes concurrents";

        vm.ShowCloseUseCasesCommand.Execute(null);

        Assert.True(vm.Gallery.IsOpen);
        Assert.True(vm.Gallery.SuggestedOnly);
        Assert.Equal("Close to your need (2)", vm.Gallery.SuggestedFilterLabel);
        Assert.Equal(["06-competitive-intelligence", "03-email-pipeline"], vm.Gallery.Cards.Select(card => card.Id));

        vm.Gallery.SuggestedOnly = false;
        Assert.Equal(6, vm.Gallery.Cards.Count);
    }

    [Fact]
    public async Task One_session_serves_every_pause_and_closing_the_wizards_session_ends_it()
    {
        var cli = UseCaseCli();
        cli.Answer = _ => [new FakeUseCaseResult("06-competitive-intelligence", "terms", "veille")];
        var vm = WizardWithGallery(cli);
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);

        vm.Need = "une veille";
        vm.Need = "une veille des brevets";
        vm.Need = "une veille des brevets chaque matin";

        Assert.Equal(3, cli.Queries.Count);
        Assert.Equal(1, cli.SessionRuns);
        Assert.Equal(["usecases", "search", "--events", "jsonl"], cli.Requests.Last().Arguments);

        vm.CloseUseCaseSession();

        Assert.Equal(1, cli.ClosedSessions);
    }

    [Fact]
    public async Task A_need_a_case_wrote_is_not_searched_and_the_hint_comes_back_with_the_chip_gone()
    {
        var cli = UseCaseCli();
        cli.Answer = _ => [new FakeUseCaseResult("06-competitive-intelligence", "terms", "concurrents"), new FakeUseCaseResult("03-email-pipeline", "terms", "trier")];
        var vm = WizardWithGallery(cli);
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);
        vm.BrowseUseCasesCommand.Execute(null);

        vm.Gallery.Cards[0].ChooseCommand.Execute(null);

        Assert.Empty(cli.Queries);
        Assert.False(vm.HasCloseUseCases);

        vm.RemoveReferenceUseCaseCommand.Execute(null);

        Assert.Equal(["Trier mes e-mails et préparer les réponses"], cli.Queries);
        Assert.Equal(2, vm.CloseUseCases.Count);
    }

    [Fact]
    public void Without_the_cli_the_gallery_shows_the_engine_missing_card()
    {
        var cli = UseCaseCli();
        var vm = WizardWithGallery(cli, cliInstalled: false);
        var diagnosticOpened = 0;
        vm.OpenDiagnosticRequested += (_, _) => diagnosticOpened++;

        vm.BrowseUseCasesCommand.Execute(null);

        Assert.True(vm.Gallery.IsOpen);
        Assert.True(vm.Gallery.HasFailure);
        Assert.True(vm.Gallery.IsEngineMissing);
        Assert.Equal(WizardFailureKind.EngineMissing, vm.Gallery.Failure!.Kind);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.WizardFailureEngineMissing], vm.Gallery.Failure.Headline);
        Assert.Contains("orkeon", vm.Gallery.Failure.Detail, StringComparison.Ordinal);
        Assert.False(vm.Gallery.HasCards);
        Assert.Empty(cli.Requests);   // no binary, nothing spawned — and no Studio-side catalogue (D-05)
        Assert.Equal("Browse the use cases", vm.BrowseUseCasesLabel);

        Assert.True(vm.Gallery.FailureOffersDiagnostic);
        vm.Gallery.OpenDiagnosticCommand.Execute(null);

        Assert.Equal(1, diagnosticOpened);
        Assert.False(vm.Gallery.IsOpen);
    }

    [Fact]
    public void Without_a_use_case_client_the_wizard_offers_no_gallery_and_suggests_nothing()
    {
        var (vm, processes, _) = Build();

        vm.Need = "une veille documentaire";

        Assert.False(vm.CanBrowseUseCases);
        Assert.False(vm.BrowseUseCasesCommand.CanExecute(null));
        Assert.False(vm.HasCloseUseCases);
        Assert.Empty(processes.Requests);
    }

    [Fact]
    public async Task A_language_switch_rereads_the_cards_the_chip_and_the_category_chips()
    {
        var strings = new SwitchingStrings();
        var language = "fr";
        var vm = WizardWithGallery(UseCaseCli(), uiLanguage: () => language, strings: strings);
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);
        vm.BrowseUseCasesCommand.Execute(null);
        vm.Gallery.Cards[1].ChooseCommand.Execute(null);
        vm.BrowseUseCasesCommand.Execute(null);

        language = "zh";
        strings.Switch("zh:");

        Assert.Equal("邮件分拣与回复", vm.Gallery.Cards[0].Title);
        Assert.StartsWith("zh:", vm.Gallery.Categories[0].Label, StringComparison.Ordinal);
        Assert.Contains("竞争情报", vm.ReferenceUseCaseLabel, StringComparison.Ordinal);
        // What the chosen case wrote is the user's need now: a switch leaves it alone.
        Assert.Equal("Surveiller ce que publient mes concurrents", vm.Need);
    }

    [Fact]
    public async Task The_window_reads_the_catalogue_at_start_up_through_its_own_runner_in_its_own_language()
    {
        var cli = UseCaseCli();
        var shell = new Orkeon.Studio.Wpf.ViewModels.Shell.MainWindowViewModel(
            new Orkeon.Studio.Wpf.ViewModels.Services.StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                TargetProbe = new FakeTargetProbe(),
                Picker = new FakePathPicker(),
                // The runner the doctor and the launcher share: the gallery reads THAT binary's catalogue.
                ProcessRunner = new OrkeonProcessRunner(cli, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
            },
            new Orkeon.Studio.Wpf.ViewModels.Services.StudioUiPreferences { InitialLanguage = "zh" },
            globalPathOverride: "/home/user/.config/Orkeon/appsettings.json");

        await shell.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, cli.ListRuns);
        Assert.Equal("Browse the use cases (6)", shell.CreateTeam.BrowseUseCasesLabel);

        shell.CreateTeam.BrowseUseCasesCommand.Execute(null);
        shell.CreateTeam.Gallery.Cards[0].ChooseCommand.Execute(null);

        Assert.Equal("整理我的邮件并起草回复", shell.CreateTeam.Need);
    }

    /// <summary>The English catalogue under a prefix that a switch changes — the retranslation seam.</summary>
    private sealed class SwitchingStrings : IStudioStrings
    {
        private string _prefix = "";

        public string this[string key] => _prefix + EnglishStudioStrings.Instance[key];

        public event EventHandler? CultureChanged;

        public void Switch(string prefix)
        {
            _prefix = prefix;
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
