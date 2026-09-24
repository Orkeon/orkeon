using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.UseCases;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>One card of the use-case gallery: a sheet of the catalogue, read in the UI's language.</summary>
public sealed class UseCaseCardViewModel
{
    internal UseCaseCardViewModel(UseCase useCase, string language, IStudioStrings strings, Action<UseCase> choose)
    {
        UseCase = useCase;
        Title = useCase.TitleIn(language) is { Length: > 0 } title ? title : useCase.Id;
        Problem = useCase.ProblemIn(language);
        ProcessLabel = UseCaseLabels.Process(useCase.Process, strings);
        CategoryLabel = UseCaseLabels.Category(useCase.Category, strings);
        ChooseCommand = new RelayCommand(() => choose(useCase));
    }

    /// <summary>The sheet behind the card.</summary>
    internal UseCase UseCase { get; }

    /// <summary>The use case's id — expert mono, and the reference STUDIO-40 hands the engine.</summary>
    public string Id => UseCase.Id;

    /// <summary>The title in the UI's language.</summary>
    public string Title { get; }

    /// <summary>The problem it solves, in the UI's language — what choosing it writes into the need.</summary>
    public string Problem { get; }

    /// <summary>Whether a problem is written at all.</summary>
    public bool HasProblem => Problem.Length > 0;

    /// <summary>The orchestration mode as the engine spells it — expert mono.</summary>
    public string Process => UseCase.Process;

    /// <summary>The same mode in plain words — the badge.</summary>
    public string ProcessLabel { get; }

    /// <summary>The category, localized.</summary>
    public string CategoryLabel { get; }

    /// <summary>
    /// Whether the case is reference-only (DC-3): browsable and usable as a reference (D-06), not
    /// importable as it is — the badge says so.
    /// </summary>
    public bool IsReferenceOnly => !UseCase.Importable;

    /// <summary>« Start from this case »: fills the need and attaches the reference.</summary>
    public RelayCommand ChooseCommand { get; }
}

/// <summary>
/// The labels the catalogue's own values are shown under: the nine category folders and the six
/// processes, in plain words. A value the catalogue adds later still shows — its folder's words,
/// or the engine's spelling — rather than nothing.
/// </summary>
internal static class UseCaseLabels
{
    /// <summary>By category name, its number dropped: a renumbered folder keeps its label.</summary>
    private static readonly Dictionary<string, string> CategoryKeys = new(StringComparer.Ordinal)
    {
        ["enterprise"] = StudioStringKeys.WizardGalleryCategoryEnterprise,
        ["science-research"] = StudioStringKeys.WizardGalleryCategoryScience,
        ["finance-trading"] = StudioStringKeys.WizardGalleryCategoryFinance,
        ["health-wellness"] = StudioStringKeys.WizardGalleryCategoryHealth,
        ["education"] = StudioStringKeys.WizardGalleryCategoryEducation,
        ["engineering-devops"] = StudioStringKeys.WizardGalleryCategoryEngineering,
        ["creative-media"] = StudioStringKeys.WizardGalleryCategoryCreative,
        ["iot-smart-systems"] = StudioStringKeys.WizardGalleryCategoryIot,
        ["experimental"] = StudioStringKeys.WizardGalleryCategoryExperimental,
    };

    /// <summary>The six modes of <c>ProcessType</c>, in the order the chips show them.</summary>
    private static readonly (string Process, string Key)[] ProcessKeys =
    [
        ("sequential", StudioStringKeys.WizardGalleryProcessSequential),
        ("hierarchical", StudioStringKeys.WizardGalleryProcessHierarchical),
        ("parallel", StudioStringKeys.WizardGalleryProcessParallel),
        ("consensual", StudioStringKeys.WizardGalleryProcessConsensual),
        ("graph", StudioStringKeys.WizardGalleryProcessGraph),
        ("autonomous", StudioStringKeys.WizardGalleryProcessAutonomous),
    ];

    public static string Category(string folder, IStudioStrings strings) =>
        CategoryKeys.TryGetValue(Name(folder), out var key) ? strings[key] : Capitalized(Name(folder).Replace('-', ' '));

    public static string Process(string process, IStudioStrings strings) =>
        ProcessKeys.FirstOrDefault(entry => string.Equals(entry.Process, process, StringComparison.OrdinalIgnoreCase)).Key is { } key
            ? strings[key]
            : process;

    /// <summary>Where a process sorts among the chips: the enum's order, an unknown one last.</summary>
    public static int ProcessOrder(string process)
    {
        var index = Array.FindIndex(ProcessKeys, entry => string.Equals(entry.Process, process, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>A category folder without its number: <c>03-finance-trading</c> is <c>finance-trading</c>.</summary>
    private static string Name(string folder)
    {
        var dash = folder.IndexOf('-', StringComparison.Ordinal);
        return dash > 0 && folder[..dash].All(char.IsAsciiDigit) ? folder[(dash + 1)..] : folder;
    }

    private static string Capitalized(string words) =>
        words.Length == 0 ? words : string.Concat(words[..1].ToUpperInvariant(), words.AsSpan(1));
}

/// <summary>
/// The use-case gallery (STUDIO-39, D-02): a side panel over the window, opened from step 1 of the
/// wizard, over the catalogue <c>orkeon usecases list</c> answers — searched and filtered here, by
/// category, by process, without web access, without a third-party key, and on the use cases the
/// need's suggestions found. Choosing a card is the wizard's business: it fills the need and
/// attaches the reference.
/// <para>
/// No fallback on Studio's side (D-05): the catalogue comes from the CLI like every other answer
/// of the wizard, and without the CLI the panel shows the same « engine not found » card the
/// wizard shows when composing fails — composing would fail the same way anyway.
/// </para>
/// </summary>
public sealed class UseCaseGalleryViewModel : ObservableObject
{
    private readonly UseCaseClient? _client;
    private readonly IUiDispatcher _dispatcher;
    private readonly IStudioStrings _strings;
    private readonly Func<string> _language;
    private readonly Action<UseCase> _choose;
    private readonly Action _openDiagnostic;
    private UseCaseCatalog? _catalog;
    /// <summary>The read in flight, so a second caller waits for it rather than for nothing.</summary>
    private Task? _load;
    private UseCaseFailure? _failureSource;
    private WizardFailure? _failure;
    private bool _isOpen;
    private bool _isLoading;
    private string _query = "";
    private string? _category;
    private string? _process;
    private bool _withoutWeb;
    private bool _withoutKeys;
    private bool _suggestedOnly;
    private IReadOnlyList<string> _suggested = [];
    private IReadOnlyList<UseCaseCardViewModel> _cards = [];
    private IReadOnlyList<WizardChoice> _categories = [];
    private IReadOnlyList<WizardChoice> _processes = [];

    /// <summary>
    /// Builds the panel. <paramref name="language"/> answers the catalogue code of the UI's
    /// language (<c>zh-Hans</c>, never Studio's <c>zh</c>); <paramref name="choose"/> is the
    /// wizard's answer to a chosen card.
    /// </summary>
    internal UseCaseGalleryViewModel(
        UseCaseClient? client,
        IUiDispatcher dispatcher,
        IStudioStrings strings,
        Func<string> language,
        Action<UseCase> choose,
        Action openDiagnostic)
    {
        _client = client;
        _dispatcher = dispatcher;
        _strings = strings;
        _language = language;
        _choose = choose;
        _openDiagnostic = openDiagnostic;

        CloseCommand = new RelayCommand(Close);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        RetryCommand = new AsyncRelayCommand(() => LoadAsync(), () => !_isLoading && _catalog is null);
        OpenDiagnosticCommand = new RelayCommand(() =>
        {
            Close();
            _openDiagnostic();
        });

        // Every label here is catalogue text in the UI's language, or a chip label: a switch of
        // language re-reads them all, filters and selection kept.
        _strings.CultureChanged += (_, _) => Rebuild();
    }

    // ── the panel ──

    /// <summary>Whether the panel is up.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    /// <summary>Whether <c>usecases list</c> is running.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
                RetryCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The catalogue, once the CLI answered it; null before, and when it could not.</summary>
    internal UseCaseCatalog? Catalog
    {
        get => _catalog;
        private set
        {
            if (!SetProperty(ref _catalog, value))
                return;

            OnPropertiesChanged(nameof(HasCatalog), nameof(Count));
            RetryCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Whether the catalogue is loaded.</summary>
    public bool HasCatalog => _catalog is not null;

    /// <summary>How many use cases the catalogue holds; 0 until it is loaded.</summary>
    public int Count => _catalog?.Count ?? 0;

    /// <summary>
    /// Why there is no catalogue: the wizard's « engine not found » card without the CLI (D-05),
    /// the CLI's own words otherwise. Null while nothing failed.
    /// </summary>
    public WizardFailure? Failure
    {
        get => _failure;
        private set
        {
            if (SetProperty(ref _failure, value))
                OnPropertiesChanged(nameof(HasFailure), nameof(IsEngineMissing), nameof(FailureOffersDiagnostic));
        }
    }

    /// <summary>Whether the failure card shows.</summary>
    public bool HasFailure => _failure is not null;

    /// <summary>Whether it is the missing-engine card.</summary>
    public bool IsEngineMissing => _failure?.Kind == WizardFailureKind.EngineMissing;

    /// <summary>Whether the card offers the diagnostic — the doctor names what is missing.</summary>
    public bool FailureOffersDiagnostic =>
        _failure?.Kind is WizardFailureKind.EngineMissing or WizardFailureKind.EngineStopped;

    /// <summary>Closes the panel; the filters stay for the next opening.</summary>
    public RelayCommand CloseCommand { get; }

    /// <summary>Reads the catalogue again — the failure card's « Try again ».</summary>
    public AsyncRelayCommand RetryCommand { get; }

    /// <summary>The failure card's « Open the diagnostic »: the panel closes, the shell shows the doctor.</summary>
    public RelayCommand OpenDiagnosticCommand { get; }

    // ── search and filters ──

    /// <summary>The search box: every word must start a word of the card, accents and case aside.</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? ""))
                Refilter();
        }
    }

    /// <summary>« All », then one chip per category of the catalogue, in its order.</summary>
    public IReadOnlyList<WizardChoice> Categories
    {
        get => _categories;
        private set => SetProperty(ref _categories, value);
    }

    /// <summary>« Any », then one chip per process the catalogue uses.</summary>
    public IReadOnlyList<WizardChoice> Processes
    {
        get => _processes;
        private set => SetProperty(ref _processes, value);
    }

    /// <summary>« Without web access »: only the cases none of whose tools reaches the network.</summary>
    public bool WithoutWeb
    {
        get => _withoutWeb;
        set
        {
            if (SetProperty(ref _withoutWeb, value))
                Refilter();
        }
    }

    /// <summary>« Without a third-party key »: only the cases that need no key besides the model's.</summary>
    public bool WithoutKeys
    {
        get => _withoutKeys;
        set
        {
            if (SetProperty(ref _withoutKeys, value))
                Refilter();
        }
    }

    /// <summary>Only the use cases close to the need, best first — how « N close use cases » opens the panel.</summary>
    public bool SuggestedOnly
    {
        get => _suggestedOnly;
        set
        {
            if (SetProperty(ref _suggestedOnly, value && _suggested.Count > 0))
                Refilter();
        }
    }

    /// <summary>Whether the need has close use cases — the suggestions filter shows only then.</summary>
    public bool HasSuggestions => _suggested.Count > 0;

    /// <summary>« Close to your need (N) ».</summary>
    public string SuggestedFilterLabel => string.Format(
        CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardGallerySuggested], _suggested.Count);

    /// <summary>Clears the search and every filter.</summary>
    public RelayCommand ClearFiltersCommand { get; }

    // ── the cards ──

    /// <summary>The cards the search and the filters keep.</summary>
    public IReadOnlyList<UseCaseCardViewModel> Cards
    {
        get => _cards;
        private set
        {
            if (!SetProperty(ref _cards, value))
                return;

            OnPropertiesChanged(nameof(HasCards), nameof(HasNoMatch), nameof(CountLabel));
        }
    }

    /// <summary>Whether any card is kept.</summary>
    public bool HasCards => _cards.Count > 0;

    /// <summary>Whether the filters kept nothing of a loaded catalogue — the empty state and its way out.</summary>
    public bool HasNoMatch => _catalog is not null && _cards.Count == 0;

    /// <summary>« 12 of 105 use cases ».</summary>
    public string CountLabel => _catalog is null
        ? ""
        : string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardGalleryCount], _cards.Count, _catalog.Count);

    // ── the wizard's side ──

    /// <summary>
    /// Opens the panel, on the suggestions when <paramref name="suggestedOnly"/> and there are
    /// some, and reads the catalogue if it is not there yet (or failed last time).
    /// </summary>
    internal void Open(bool suggestedOnly)
    {
        SuggestedOnly = suggestedOnly;
        IsOpen = true;
        if (_catalog is null)
            _ = LoadAsync();
    }

    /// <summary>Closes the panel.</summary>
    internal void Close() => IsOpen = false;

    /// <summary>The ids the need's suggestions found, best first (none clears the filter).</summary>
    internal void Suggest(IReadOnlyList<string> ids)
    {
        _suggested = ids;
        if (_suggestedOnly && ids.Count == 0)
        {
            _suggestedOnly = false;
            OnPropertyChanged(nameof(SuggestedOnly));
        }

        OnPropertiesChanged(nameof(HasSuggestions), nameof(SuggestedFilterLabel));
        if (_suggestedOnly)
            Refilter();
    }

    /// <summary>
    /// Runs <c>usecases list</c> once: nothing when the catalogue is there, the read under way when
    /// there is one, a new read after a failure. The result lands on the UI thread before the task
    /// completes. Called on the UI thread.
    /// </summary>
    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null || _catalog is not null)
            return Task.CompletedTask;

        if (_isLoading && _load is { } inFlight)
            return inFlight;

        var load = ReadAsync(_client, cancellationToken);
        // A read the fakes of the tests finish on the spot has nothing to join.
        if (!load.IsCompleted)
            _load = load;
        return load;
    }

    [SuppressMessage("Design", "CA1031", Justification =
        "The gallery's fault barrier: whatever stops the read — an exception out of the launch "
        + "included — is a card in the panel, never a dead task or a message box.")]
    private async Task ReadAsync(UseCaseClient client, CancellationToken cancellationToken)
    {
        IsLoading = true;
        _failureSource = null;
        Failure = null;

        UseCaseCatalogResult result;
        try
        {
            result = await client.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The window is closing: nothing to say, nothing to show.
            await PostAndAwaitAsync(() =>
            {
                _load = null;
                IsLoading = false;
            }).ConfigureAwait(false);
            return;
        }
        catch (Exception exception)
        {
            result = new UseCaseCatalogResult
            {
                Failure = new UseCaseFailure(UseCaseFailureKind.Stopped, $"{exception.GetType().Name}: {exception.Message}"),
            };
        }

        await PostAndAwaitAsync(() => Apply(result)).ConfigureAwait(false);
    }

    private void Apply(UseCaseCatalogResult result)
    {
        _load = null;
        IsLoading = false;
        if (result.Catalog is { } catalog)
        {
            Catalog = catalog;
            Rebuild();
            return;
        }

        _failureSource = result.Failure ?? new UseCaseFailure(UseCaseFailureKind.Stopped, "no catalogue");
        Failure = ToFailure(_failureSource);
    }

    /// <summary>The card of a failure, in the family's words — the headline follows the language, the detail never does.</summary>
    private WizardFailure ToFailure(UseCaseFailure failure) => new(
        failure.Kind switch
        {
            UseCaseFailureKind.EngineMissing => WizardFailureKind.EngineMissing,
            UseCaseFailureKind.Refused => WizardFailureKind.ConfigRefused,
            _ => WizardFailureKind.EngineStopped,
        },
        _strings[failure.Kind == UseCaseFailureKind.EngineMissing
            ? StudioStringKeys.WizardFailureEngineMissing
            : StudioStringKeys.WizardGalleryUnreadable],
        failure.Detail,
        CommandLineDisplay.Format(UseCaseArgumentsBuilder.BuildList()),
        failure.ExitCode,
        Stderr: "",
        EngineError: null);

    /// <summary>Chips, cards and labels again, in the language in force.</summary>
    private void Rebuild()
    {
        if (_failureSource is { } source)
            Failure = ToFailure(source);

        if (_catalog is { } catalog)
        {
            Categories =
            [
                new WizardChoice("", _strings[StudioStringKeys.WizardGalleryAllCategories], _ => SelectCategory(null)),
                .. catalog.UseCases
                    .Select(useCase => useCase.Category)
                    .Distinct(StringComparer.Ordinal)
                    .Select(category => new WizardChoice(category, UseCaseLabels.Category(category, _strings), _ => SelectCategory(category))),
            ];
            Processes =
            [
                new WizardChoice("", _strings[StudioStringKeys.WizardGalleryAnyProcess], _ => SelectProcess(null)),
                .. catalog.UseCases
                    .Select(useCase => useCase.Process)
                    .Where(process => process.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(UseCaseLabels.ProcessOrder)
                    .Select(process => new WizardChoice(process, UseCaseLabels.Process(process, _strings), _ => SelectProcess(process))),
            ];
            SyncChips();
        }

        Refilter();
        OnPropertiesChanged(nameof(SuggestedFilterLabel), nameof(CountLabel));
    }

    private void SelectCategory(string? category)
    {
        _category = category;
        SyncChips();
        Refilter();
    }

    private void SelectProcess(string? process)
    {
        _process = process;
        SyncChips();
        Refilter();
    }

    private void SyncChips()
    {
        foreach (var chip in _categories)
            chip.IsSelected = string.Equals(chip.Key, _category ?? "", StringComparison.Ordinal);
        foreach (var chip in _processes)
            chip.IsSelected = string.Equals(chip.Key, _process ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearFilters()
    {
        _query = "";
        _category = null;
        _process = null;
        _withoutWeb = false;
        _withoutKeys = false;
        _suggestedOnly = false;
        OnPropertiesChanged(nameof(Query), nameof(WithoutWeb), nameof(WithoutKeys), nameof(SuggestedOnly));
        SyncChips();
        Refilter();
    }

    private void Refilter()
    {
        if (_catalog is not { } catalog)
        {
            Cards = [];
            return;
        }

        var language = _language();
        var words = UseCaseTerms.Tokenize(_query);
        var candidates = _suggestedOnly
            ? _suggested.Select(catalog.Find).OfType<UseCase>()
            : catalog.UseCases;

        Cards =
        [
            .. candidates
                .Where(useCase => Passes(useCase, language, words))
                .Select(useCase => new UseCaseCardViewModel(useCase, language, _strings, _choose)),
        ];
    }

    private bool Passes(UseCase useCase, string language, IReadOnlyList<string> words)
    {
        if (_category is { } category && !string.Equals(useCase.Category, category, StringComparison.Ordinal))
            return false;
        if (_process is { } process && !string.Equals(useCase.Process, process, StringComparison.OrdinalIgnoreCase))
            return false;
        if (_withoutWeb && useCase.RequiresNetwork)
            return false;
        if (_withoutKeys && useCase.RequiresKeys.Count > 0)
            return false;
        if (words.Count == 0)
            return true;

        // What a reader of the card can see, in the UI's language, plus its tags, tools and id —
        // folded the CLI's way, so an accent typed or not finds the same card.
        var haystack = UseCaseTerms.Tokenize(string.Join(
            ' ',
            [
                useCase.TitleIn(language),
                useCase.ProblemIn(language),
                UseCaseLabels.Category(useCase.Category, _strings),
                .. useCase.Tags,
                .. useCase.Tools,
                useCase.Id,
            ]));
        return words.All(word => haystack.Any(term => term.StartsWith(word, StringComparison.Ordinal)));
    }

    /// <summary>Posts <paramref name="action"/> and completes once it ran on the UI thread.</summary>
    private Task PostAndAwaitAsync(Action action)
    {
        var landed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.Post(() =>
        {
            try
            {
                action();
            }
            finally
            {
                landed.TrySetResult();
            }
        });
        return landed.Task;
    }
}
