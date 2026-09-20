using System.ComponentModel;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The unified Settings screen (design v3): one nav entry, six inner tabs. Novice sees the
/// three that matter — the AI model, the authorized folders and the tools (their keys and
/// what each one needs, STUDIO-21); the limits-and-logs tab, the MCP servers and the raw-JSON
/// tab are expert-only, and a switch back to novice while one of them is showing falls back
/// to the model tab rather than leaving a blank screen.
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

    /// <summary>The tools: their keys, the catalogue, the shell allow-list (STUDIO-21).</summary>
    public const string ToolsTab = "tools";

    /// <summary>The MCP servers a run connects (expert, STUDIO-21).</summary>
    public const string McpTab = "mcp";

    private readonly UiModeViewModel _mode;
    private string _activeTab = ModelTab;

    /// <summary>
    /// Builds the screen over the settings editor and the profile tab. <paramref name="teamFolders"/>
    /// is the read-only « Team folders » section of the folders tab (STUDIO-14); left out, the
    /// section lists nothing — the shell passes one over the team catalog. <paramref name="tools"/>
    /// is the Tools tab's own content (STUDIO-21); left out, the tab is built over the environment
    /// key store.
    /// </summary>
    public SettingsScreenViewModel(
        ConfigTabViewModel config,
        ModelProfilesViewModel profiles,
        UiModeViewModel mode,
        TeamFoldersViewModel? teamFolders = null,
        ToolsSettingsViewModel? tools = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(mode);

        Config = config;
        Profiles = profiles;
        TeamFolders = teamFolders ?? new TeamFoldersViewModel(() => []);
        Tools = tools ?? new ToolsSettingsViewModel();
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
        // The folders tab re-reads the teams' own folders on arrival: the section is a view
        // over the sidecars, and a team adopted or deleted since the last visit must show.
        ShowFoldersCommand = new RelayCommand(() =>
        {
            TeamFolders.Refresh();
            ActiveTab = FoldersTab;
        });
        ShowLimitsCommand = new RelayCommand(() => ActiveTab = LimitsTab);
        ShowJsonCommand = new RelayCommand(() => ActiveTab = JsonTab);
        ShowToolsCommand = new RelayCommand(() => ActiveTab = ToolsTab);
        ShowMcpCommand = new RelayCommand(() => ActiveTab = McpTab);
    }

    /// <summary>The settings-document editor the tabs render.</summary>
    public ConfigTabViewModel Config { get; }

    /// <summary>The model-profiles tab content.</summary>
    public ModelProfilesViewModel Profiles { get; }

    /// <summary>
    /// The read-only « Team folders » section of the folders tab (STUDIO-14, D-13): the
    /// folders each adopted team keeps inside itself, vouched for by that alone and never
    /// written to the settings file. Shown in both modes, under the declared folders.
    /// </summary>
    public TeamFoldersViewModel TeamFolders { get; }

    /// <summary>The Tools tab: the keys the tools need and the catalogue of what a run exposes (STUDIO-21).</summary>
    public ToolsSettingsViewModel Tools { get; }

    /// <summary>The window-wide mode switch, for the view's expert/novice visibilities.</summary>
    public UiModeViewModel Mode => _mode;

    /// <summary>The showing tab; an expert tab requested in novice mode falls back to the model tab.</summary>
    public string ActiveTab
    {
        get => _activeTab;
        set
        {
            var requested = value is FoldersTab or LimitsTab or JsonTab or ToolsTab or McpTab ? value : ModelTab;
            if (_mode.IsNovice && requested is LimitsTab or JsonTab or McpTab)
                requested = ModelTab;

            if (SetProperty(ref _activeTab, requested))
            {
                OnPropertiesChanged(
                    nameof(IsModelTab), nameof(IsFoldersTab), nameof(IsLimitsTab), nameof(IsJsonTab),
                    nameof(IsToolsTab), nameof(IsMcpTab));
            }
        }
    }

    /// <summary>True while the AI-model tab shows.</summary>
    public bool IsModelTab => _activeTab == ModelTab;

    /// <summary>True while the allowed-folders tab shows.</summary>
    public bool IsFoldersTab => _activeTab == FoldersTab;

    /// <summary>True while the expert limits-and-logs tab shows.</summary>
    public bool IsLimitsTab => _activeTab == LimitsTab;

    /// <summary>True while the expert raw-JSON tab shows.</summary>
    public bool IsJsonTab => _activeTab == JsonTab;

    /// <summary>True while the tools tab shows.</summary>
    public bool IsToolsTab => _activeTab == ToolsTab;

    /// <summary>True while the expert MCP tab shows.</summary>
    public bool IsMcpTab => _activeTab == McpTab;

    /// <summary>Shows the model tab.</summary>
    public RelayCommand ShowModelCommand { get; }

    /// <summary>Shows the folders tab.</summary>
    public RelayCommand ShowFoldersCommand { get; }

    /// <summary>Shows the limits tab.</summary>
    public RelayCommand ShowLimitsCommand { get; }

    /// <summary>Shows the file tab.</summary>
    public RelayCommand ShowJsonCommand { get; }

    /// <summary>Shows the tools tab.</summary>
    public RelayCommand ShowToolsCommand { get; }

    /// <summary>Shows the MCP tab.</summary>
    public RelayCommand ShowMcpCommand { get; }

    private void OnModeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(UiModeViewModel.IsNovice) or nameof(UiModeViewModel.Mode)))
            return;

        if (_mode.IsNovice && (IsLimitsTab || IsJsonTab || IsMcpTab))
            ActiveTab = ModelTab;
    }
}
