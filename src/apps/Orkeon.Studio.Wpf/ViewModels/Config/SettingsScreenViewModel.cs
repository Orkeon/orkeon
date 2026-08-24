using System.ComponentModel;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The unified "Réglages" screen (design v3): one nav entry, four inner tabs. Novice sees the
/// two that matter — the AI model and the authorized folders; "Limites &amp; journaux" and
/// "JSON brut" are expert-only, and a switch back to novice while one of them is showing
/// falls back to the model tab rather than leaving a blank screen.
/// </summary>
public sealed class SettingsScreenViewModel : ObservableObject
{
    /// <summary>Tab names, as stored in <see cref="ActiveTab"/>.</summary>
    public const string ModelTab = "model";

    /// <summary>The mounts tab.</summary>
    public const string FoldersTab = "folders";

    /// <summary>Rate limits, RAG index and logging (expert).</summary>
    public const string LimitsTab = "limits";

    /// <summary>The settings file itself: location, resolution chain, raw JSON (expert).</summary>
    public const string JsonTab = "json";

    private readonly UiModeViewModel _mode;
    private string _activeTab = ModelTab;

    /// <summary>Builds the screen over the settings editor and the profile tab.</summary>
    public SettingsScreenViewModel(ConfigTabViewModel config, ModelProfilesViewModel profiles, UiModeViewModel mode)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(mode);

        Config = config;
        Profiles = profiles;
        _mode = mode;
        _mode.PropertyChanged += OnModeChanged;

        // Novice auto-save (audit 07/16): the novice screen shows no Save button, so every
        // edit saves the document. Listening to each edit rather than the dirty transition
        // matters: a save refused by validation leaves the flag up, and the next edit must
        // still try again. The expert keeps the explicit cycle.
        Config.DocumentEdited += (_, _) =>
        {
            if (_mode.IsNovice && Config.SaveCommand.CanExecute(null))
                Config.SaveCommand.Execute(null);
        };

        ShowModelCommand = new RelayCommand(() => ActiveTab = ModelTab);
        ShowFoldersCommand = new RelayCommand(() => ActiveTab = FoldersTab);
        ShowLimitsCommand = new RelayCommand(() => ActiveTab = LimitsTab);
        ShowJsonCommand = new RelayCommand(() => ActiveTab = JsonTab);
    }

    /// <summary>The settings-document editor the tabs render.</summary>
    public ConfigTabViewModel Config { get; }

    /// <summary>The model-profiles tab content.</summary>
    public ModelProfilesViewModel Profiles { get; }

    /// <summary>The window-wide mode switch, for the view's expert/novice visibilities.</summary>
    public UiModeViewModel Mode => _mode;

    /// <summary>The showing tab; an expert tab requested in novice mode falls back to the model tab.</summary>
    public string ActiveTab
    {
        get => _activeTab;
        set
        {
            var requested = value is FoldersTab or LimitsTab or JsonTab ? value : ModelTab;
            if (_mode.IsNovice && requested is LimitsTab or JsonTab)
                requested = ModelTab;

            if (SetProperty(ref _activeTab, requested))
                OnPropertiesChanged(nameof(IsModelTab), nameof(IsFoldersTab), nameof(IsLimitsTab), nameof(IsJsonTab));
        }
    }

    /// <summary>True while the "Modèle d'IA" tab shows.</summary>
    public bool IsModelTab => _activeTab == ModelTab;

    /// <summary>True while the "Dossiers autorisés" tab shows.</summary>
    public bool IsFoldersTab => _activeTab == FoldersTab;

    /// <summary>True while the expert "Limites &amp; journaux" tab shows.</summary>
    public bool IsLimitsTab => _activeTab == LimitsTab;

    /// <summary>True while the expert "JSON brut" tab shows.</summary>
    public bool IsJsonTab => _activeTab == JsonTab;

    /// <summary>Shows the model tab.</summary>
    public RelayCommand ShowModelCommand { get; }

    /// <summary>Shows the folders tab.</summary>
    public RelayCommand ShowFoldersCommand { get; }

    /// <summary>Shows the limits tab.</summary>
    public RelayCommand ShowLimitsCommand { get; }

    /// <summary>Shows the file tab.</summary>
    public RelayCommand ShowJsonCommand { get; }

    private void OnModeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(UiModeViewModel.IsNovice) or nameof(UiModeViewModel.Mode)))
            return;

        if (_mode.IsNovice && (IsLimitsTab || IsJsonTab))
            ActiveTab = ModelTab;
    }
}
