using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The preset picker of spec §4.2, over the same catalogue as <c>orkeon init</c>. Applying a preset
/// pre-fills the <c>Llm</c> form; the user then adjusts it before saving. Nothing is written until
/// <see cref="ApplyCommand"/> runs, and the plan's guidance (API key handling above all) is surfaced
/// rather than applied silently.
/// </summary>
public sealed class PresetSelectionViewModel : ObservableObject
{
    private readonly Func<AppSettingsDocument> _document;
    private readonly Action _onApplied;
    private readonly IStudioStrings _strings;
    private IReadOnlyList<LlmPresetInfo> _catalog;
    private LlmPresetInfo? _selectedPreset;
    private string? _baseUrl;
    private string? _model;
    private string? _apiKey;
    private string? _apiKeyEnv;
    private string? _errorMessage;

    /// <summary>Binds the picker to the document it will write into once applied.</summary>
    public PresetSelectionViewModel(
        Func<AppSettingsDocument> document,
        Action onApplied,
        IStudioStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(onApplied);

        _document = document;
        _onApplied = onApplied;
        _strings = strings ?? EnglishStudioStrings.Instance;
        _strings.CultureChanged += (_, _) => RefreshCulture();
        _catalog = LlmPresets.CatalogFor(_strings);
        _selectedPreset = _catalog.Count > 0 ? _catalog[0] : null;

        ApplyCommand = new RelayCommand(() => Apply(), () => SelectedPreset is not null);
    }

    /// <summary>The five presets offered, identical to the <c>orkeon init</c> list.</summary>
    public IReadOnlyList<LlmPresetInfo> Catalog => _catalog;

    /// <summary>Writes the selected preset into the document.</summary>
    public RelayCommand ApplyCommand { get; }

    /// <summary>Lines explaining what the last applied preset did, notably about the API key.</summary>
    public ObservableCollection<string> Guidance { get; } = [];

    /// <summary>The preset about to be applied.</summary>
    public LlmPresetInfo? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!SetProperty(ref _selectedPreset, value))
                return;

            // The overrides are seeded from the preset so the boxes show what will be written.
            BaseUrl = value?.DefaultBaseUrl;
            Model = value?.DefaultModel;
            ErrorMessage = null;
            OnPropertiesChanged(nameof(RequiresApiKey), nameof(Description));
            ApplyCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The endpoint that will be written, pre-filled from the preset.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "Mirrors LlmPresetOverrides.BaseUrl in Core: a user-typed form value on its way to "
                        + "a JSON string field, which must be carried and reported even when malformed.")]
    public string? BaseUrl
    {
        get => _baseUrl;
        set => SetProperty(ref _baseUrl, value);
    }

    /// <summary>The model that will be written, pre-filled from the preset.</summary>
    public string? Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    /// <summary>An inline API key. Leaving it empty is what routes the key to the environment variable.</summary>
    public string? ApiKey
    {
        get => _apiKey;
        set => SetProperty(ref _apiKey, value);
    }

    /// <summary>An alternative environment variable name; empty means <c>ORKEON_Llm__ApiKey</c>.</summary>
    public string? ApiKeyEnv
    {
        get => _apiKeyEnv;
        set => SetProperty(ref _apiKeyEnv, value);
    }

    /// <summary>Why the last apply was refused, when it was.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>Whether the selected preset needs a key at all.</summary>
    public bool RequiresApiKey => SelectedPreset?.RequiresApiKey ?? false;

    /// <summary>The one-line description of the selected preset.</summary>
    public string Description => SelectedPreset?.Description ?? "";

    /// <summary>
    /// Builds the plan and applies it, or records why it could not be built (the <c>custom</c> preset
    /// needs both a base URL and a model).
    /// </summary>
    /// <returns><see langword="true"/> when the document was modified.</returns>
    public bool Apply()
    {
        if (SelectedPreset is not { } preset)
            return false;

        var overrides = new LlmPresetOverrides
        {
            BaseUrl = BaseUrl,
            Model = Model,
            ApiKey = ApiKey,
            ApiKeyEnv = ApiKeyEnv,
        };

        if (!LlmPresets.TryCreatePlan(preset.Name, overrides, _strings, out var plan, out var error))
        {
            ErrorMessage = error;
            Guidance.Clear();
            return false;
        }

        ErrorMessage = null;
        LlmPresets.Apply(_document(), plan);

        Guidance.Clear();
        foreach (var line in LlmPresets.Guidance(plan, _strings))
            Guidance.Add(line);

        _onApplied();
        return true;
    }

    /// <summary>
    /// Rebuilds the catalogue in the new culture and re-selects the same preset by name, so a
    /// language switch never resets what the user picked (STUDIO-11).
    /// </summary>
    private void RefreshCulture()
    {
        var selectedName = _selectedPreset?.Name;

        _catalog = LlmPresets.CatalogFor(_strings);
        _selectedPreset = selectedName is null
            ? null
            : _catalog.FirstOrDefault(p => string.Equals(p.Name, selectedName, StringComparison.Ordinal));

        OnPropertiesChanged(nameof(Catalog), nameof(SelectedPreset), nameof(Description), nameof(RequiresApiKey));
        ApplyCommand.RaiseCanExecuteChanged();
    }
}
