using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.ViewModels.Mounts;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Common;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The Config tab (spec §4): typed forms for the known sections, a read-only view of the raw JSON,
/// the mount editor, the presets, the save location and the validation that gates the save.
/// <para>
/// Every field writes into a single live <see cref="AppSettingsDocument"/>, so keys Studio knows
/// nothing about survive a load / edit / save cycle untouched.
/// </para>
/// </summary>
public sealed class ConfigTabViewModel : ObservableObject
{
    private readonly IAppSettingsStore _store;
    private readonly AppSettingsValidator _validator;
    private readonly IStudioStrings _strings;
    private AppSettingsDocument _document;
    private string _rawJson = "";
    private bool _isDirty;
    private string? _statusMessage;
    private string? _loadedPath;

    /// <summary>Builds the tab over the given seams; every one of them has an in-memory double in the tests.</summary>
    public ConfigTabViewModel(
        IAppSettingsStore? store = null,
        IDirectoryProbe? directories = null,
        IPathPicker? picker = null,
        OrkeonProcessRunner? processRunner = null,
        IUiDispatcher? dispatcher = null,
        string? globalPathOverride = null,
        ILlmEndpointProbe? llmProbe = null,
        IStudioStrings? strings = null)
    {
        _store = store ?? PhysicalAppSettingsStore.Instance;
        _validator = new AppSettingsValidator(directories);
        _strings = strings ?? EnglishStudioStrings.Instance;
        _document = AppSettingsDocument.CreateEmpty();

        Picker = picker ?? NullPathPicker.Instance;

        Llm = new LlmSectionViewModel(() => _document, MarkDirty, llmProbe, dispatcher, _strings);
        RateLimiting = new RateLimitingSectionViewModel(() => _document, MarkDirty);
        Rag = new RagSectionViewModel(() => _document, MarkDirty);
        Logging = new LoggingSectionViewModel(() => _document, MarkDirty);
        LlmLogging = new LlmLoggingSectionViewModel(() => _document, MarkDirty);

        Mounts = new MountsEditorViewModel(directories, Picker, requireAtLeastOne: true, _strings);
        Mounts.Changed += OnMountsChanged;

        Location = new SettingsLocationViewModel(Picker, globalPathOverride, _strings);
        Diagnostic = new DiagnosticViewModel(
            processRunner ?? OrkeonProcessRunner.ForCurrentMachine(),
            dispatcher,
            _strings);

        // Hot language switch (STUDIO-11): the tab lives as long as the window, so the
        // subscription needs no teardown.
        _strings.CultureChanged += (_, _) => OnPropertyChanged(nameof(ValidationSummary));

        NewCommand = new RelayCommand(NewDocument);
        OpenCommand = new AsyncRelayCommand(OpenAsync);
        LoadFromLocationCommand = new AsyncRelayCommand(() => LoadAsync(Location.EffectivePath ?? ""));
        SaveCommand = new AsyncRelayCommand(() => SaveAsync(), () => Location.CanSave);
        ValidateCommand = new RelayCommand(() => Validate());

        RefreshRawJson();
        Validate();
    }

    /// <summary>The browse dialogs, shared with the mount editor.</summary>
    public IPathPicker Picker { get; }

    /// <summary>The <c>Llm</c> form.</summary>
    public LlmSectionViewModel Llm { get; }

    /// <summary>The <c>RateLimiting</c> form.</summary>
    public RateLimitingSectionViewModel RateLimiting { get; }

    /// <summary>The <c>Orkeon:Rag</c> form.</summary>
    public RagSectionViewModel Rag { get; }

    /// <summary>The <c>Logging:LogLevel</c> form.</summary>
    public LoggingSectionViewModel Logging { get; }

    /// <summary>The <c>LlmLogging</c> form.</summary>
    public LlmLoggingSectionViewModel LlmLogging { get; }

    /// <summary>The <c>Orkeon:FileSystem:Mounts</c> editor (spec §4.5).</summary>
    public MountsEditorViewModel Mounts { get; }


    /// <summary>The save-location picker and resolution chain (spec §4.3).</summary>
    public SettingsLocationViewModel Location { get; }

    /// <summary>The <c>orkeon doctor</c> panel (spec §4.4).</summary>
    public DiagnosticViewModel Diagnostic { get; }

    /// <summary>Starts a new, empty document.</summary>
    public RelayCommand NewCommand { get; }

    /// <summary>Opens a file chosen in the browser.</summary>
    public AsyncRelayCommand OpenCommand { get; }

    /// <summary>Loads the file the location picker currently points at.</summary>
    public AsyncRelayCommand LoadFromLocationCommand { get; }

    /// <summary>Validates, then writes the document to the selected location.</summary>
    public AsyncRelayCommand SaveCommand { get; }

    /// <summary>Re-runs the validation without saving.</summary>
    public RelayCommand ValidateCommand { get; }

    /// <summary>The findings of the last validation, blocking or not.</summary>
    public ObservableCollection<ValidationMessageViewModel> ValidationMessages { get; } = [];

    /// <summary>The document as it would be written: the raw view of spec §4.1, read-only by design.</summary>
    public string RawJson
    {
        get => _rawJson;
        private set => SetProperty(ref _rawJson, value);
    }

    /// <summary>Whether there are unsaved edits.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    /// <summary>The outcome of the last load, save or validation.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>The file the document was loaded from, when it came from one.</summary>
    public string? LoadedPath
    {
        get => _loadedPath;
        private set => SetProperty(ref _loadedPath, value);
    }

    /// <summary>Whether a save must be refused: the validation found at least one error.</summary>
    public bool HasBlockingErrors => ValidationMessages.Any(m => m.IsError);

    /// <summary>
    /// Whether the WIN-01 warning applies: no <c>Llm</c> section, so the runtime will silently
    /// degrade to the echo provider (spec §4.4).
    /// </summary>
    public bool HasLlmWarning => ValidationMessages.Any(m => m.Code == ValidationCodes.LlmSectionMissing);

    /// <summary>The document currently being edited.</summary>
    public AppSettingsDocument Document => _document;

    /// <summary>Replaces the edited document, e.g. after a load or a "new file".</summary>
    public void SetDocument(AppSettingsDocument document, string? loadedPath = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        _document = document;
        LoadedPath = loadedPath;

        // The forms read through a delegate, so swapping the document is enough; they only need to
        // re-publish every bound property.
        Llm.Refresh();
        RateLimiting.Refresh();
        Rag.Refresh();
        Logging.Refresh();
        LlmLogging.Refresh();
        Mounts.Load(_document.Mounts.RawEntries);

        IsDirty = false;
        RefreshRawJson();
        Validate();
    }

    /// <summary>Discards the current document and starts from an empty one.</summary>
    public void NewDocument()
    {
        SetDocument(AppSettingsDocument.CreateEmpty());
        StatusMessage = _strings[StudioStringKeys.ConfigNewDocument];
    }

    /// <summary>Opens a file picked in the browser and points the save location at it.</summary>
    public async Task OpenAsync()
    {
        var picked = Picker.PickFile(
            _strings[StudioStringKeys.DialogOpenAppSettings],
            _strings[StudioStringKeys.DialogFilterJson],
            Location.EffectivePath);

        if (picked is { Length: > 0 })
            await LoadAsync(picked);
    }

    /// <summary>Loads a document from <paramref name="path"/>.</summary>
    /// <returns><see langword="true"/> when the file was loaded.</returns>
    public async Task<bool> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = _strings[StudioStringKeys.ConfigNoFileSelected];
            return false;
        }

        var (document, error) = await _store.TryLoadAsync(path, cancellationToken);
        if (document is null)
        {
            StatusMessage = error;
            return false;
        }

        SetDocument(document, path);
        Location.UseCustomPath(path);
        StatusMessage = string.Format(CultureInfo.InvariantCulture, _strings[StudioStringKeys.ConfigLoaded], path);
        return true;
    }

    /// <summary>
    /// Validates and, when nothing blocks, writes the document to <see cref="SettingsLocationViewModel.EffectivePath"/>.
    /// Errors block the save; warnings — WIN-01 included — do not.
    /// </summary>
    /// <returns><see langword="true"/> when the file was written.</returns>
    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        FlushMountsToDocument();

        // Validated as a save, not as an edit: a settings file with no mount cannot start a run
        // on its own, so writing one is blocked even though the editor tolerates the empty list
        // while the user is still working.
        Validate(ValidationScope.Saving);

        if (HasBlockingErrors)
        {
            StatusMessage = string.Format(
                CultureInfo.InvariantCulture,
                _strings[StudioStringKeys.ConfigNotSavedErrors],
                ValidationMessages.Count(m => m.IsError));
            return false;
        }

        if (Location.EffectivePath is not { Length: > 0 } path)
        {
            StatusMessage = _strings[StudioStringKeys.ConfigNotSavedNoDestination];
            return false;
        }

        await _store.SaveAsync(_document, path, cancellationToken);

        LoadedPath = path;
        IsDirty = false;
        StatusMessage = HasLlmWarning
            ? string.Format(
                CultureInfo.InvariantCulture,
                _strings[StudioStringKeys.ConfigSavedLlmWarning],
                path,
                ValidationCodes.LlmSectionMissing)
            : string.Format(CultureInfo.InvariantCulture, _strings[StudioStringKeys.ConfigSaved], path);

        return true;
    }

    /// <summary>Re-runs the whole validation, mounts included, and republishes the message list.</summary>
    /// <param name="scope">
    /// Why the document is being validated. <see cref="ValidationScope.Saving"/> — what
    /// <see cref="SaveAsync"/> passes — turns findings the editor tolerates into blocking errors.
    /// </param>
    public IReadOnlyList<ValidationMessage> Validate(ValidationScope scope = ValidationScope.Editing)
    {
        FlushMountsToDocument();

        var messages = _validator.Validate(_document, scope);

        ValidationMessages.Clear();
        foreach (var message in messages)
            ValidationMessages.Add(new ValidationMessageViewModel(message));

        OnPropertiesChanged(nameof(HasBlockingErrors), nameof(HasLlmWarning), nameof(ValidationSummary));
        SaveCommand.RaiseCanExecuteChanged();
        return messages;
    }

    /// <summary>The one-line verdict shown next to the Save button.</summary>
    public string ValidationSummary
    {
        get
        {
            var errors = ValidationMessages.Count(m => m.IsError);
            var warnings = ValidationMessages.Count(m => m.Severity == ValidationSeverity.Warning);

            return errors == 0 && warnings == 0
                ? _strings[StudioStringKeys.ConfigNoProblem]
                : string.Format(
                    CultureInfo.InvariantCulture,
                    _strings[StudioStringKeys.ConfigErrorsWarnings], errors, warnings);
        }
    }

    private void OnMountsChanged(object? sender, EventArgs e)
    {
        FlushMountsToDocument();
        MarkDirty();
    }

    private void FlushMountsToDocument()
    {
        var entries = Mounts.ToRawEntries();
        if (entries.Count == 0)
            _document.Mounts.Remove();
        else
            _document.Mounts.SetRaw(entries);

        RefreshRawJson();
    }

    private void MarkDirty()
    {
        IsDirty = true;
        RefreshRawJson();
    }

    private void RefreshRawJson() => RawJson = _document.ToJson();
}
