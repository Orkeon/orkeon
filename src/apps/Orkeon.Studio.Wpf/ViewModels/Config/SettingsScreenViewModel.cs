using System.ComponentModel;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The unified Settings screen (design v3): one nav entry, eleven inner tabs in four groups
/// (refonte lot 11). Novice sees the ones that matter — the AI model, the API keys, the
/// authorized folders, the tools (their keys and what each one needs, STUDIO-21), the e-mail
/// and Drive accounts (STUDIO-67) and Studio itself (STUDIO-35); the RAG's model, the
/// limits-and-logs tab, the MCP servers and the raw-JSON tab are expert-only, and a switch back
/// to novice while one of them is showing falls back to the model tab rather than leaving a
/// blank screen.
/// </summary>
public sealed partial class SettingsScreenViewModel : ObservableObject
{
    /// <summary>Tab names, as stored in <see cref="ActiveTab"/>.</summary>
    public const string ModelTab = "model";

    /// <summary>The model the document search calls, Orkeon:Rag:LlmProfile (expert, refonte lot 11).</summary>
    public const string RagTab = "rag";

    /// <summary>The API keys the model settings resolve, kept in the user environment (refonte lot 11).</summary>
    public const string KeysTab = "keys";

    /// <summary>The mounts tab.</summary>
    public const string FoldersTab = "folders";

    /// <summary>Rate limits, RAG index and logging (expert).</summary>
    public const string LimitsTab = "limits";

    /// <summary>The settings file itself: location, resolution chain, raw JSON (expert).</summary>
    public const string JsonTab = "json";

    /// <summary>The tools: their keys, the catalogue, the shell allow-list (STUDIO-21).</summary>
    public const string ToolsTab = "tools";

    /// <summary>The keys the tools need, a section of the Access group of its own.</summary>
    public const string ToolKeysTab = "toolkeys";

    /// <summary>The shell allow-list, expert only, a section of the Access group of its own.</summary>
    public const string ShellTab = "shell";

    /// <summary>The MCP servers a run connects (expert, STUDIO-21).</summary>
    public const string McpTab = "mcp";

    /// <summary>How Studio itself behaves on this machine (STUDIO-35): the balance, and what STUDIO-32 adds.</summary>
    public const string StudioTab = "studio";

    /// <summary>The e-mail accounts the mail tools use (STUDIO-67); both modes, the expert sees every field.</summary>
    public const string MailsTab = "mails";

    /// <summary>The Drive tab: the cloud drive accounts the drive tools use.</summary>
    public const string DriveTab = "drive";

    private readonly UiModeViewModel _mode;
    private string _activeTab = ModelTab;
    private Action? _pendingLeave;

    /// <summary>
    /// Builds the screen over the settings editor and the profile tab. <paramref name="teamFolders"/>
    /// is the read-only « Team folders » section of the folders tab (STUDIO-14); left out, the
    /// section lists nothing — the shell passes one over the team catalog. <paramref name="tools"/>
    /// is the Tools tab's own content (STUDIO-21); left out, the tab is built over the environment
    /// key store. <paramref name="studio"/> is the « Studio » tab (STUDIO-35); left out, its
    /// choices last for the session only.
    /// </summary>
    public SettingsScreenViewModel(
        ConfigTabViewModel config,
        ModelProfilesViewModel profiles,
        UiModeViewModel mode,
        TeamFoldersViewModel? teamFolders = null,
        ToolsSettingsViewModel? tools = null,
        StudioSettingsViewModel? studio = null,
        IStudioStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(mode);

        Config = config;
        Profiles = profiles;
        TeamFolders = teamFolders ?? new TeamFoldersViewModel(() => []);
        Tools = tools ?? new ToolsSettingsViewModel();
        Studio = studio ?? new StudioSettingsViewModel();
        _mode = mode;
        _mode.PropertyChanged += OnModeChanged;
        _strings = strings ?? EnglishStudioStrings.Instance;
        InitializeTabStates();
        // The E-mail form is in both modes and shows more to the expert (STUDIO-67).
        Config.Email.IsExpert = _mode.IsExpert;
        Config.Drive.IsExpert = _mode.IsExpert;

        // Novice auto-save (audit 07/16): the novice screen shows no Save button, so every
        // edit saves the document. Listening to each edit rather than the dirty transition
        // matters: a save refused by validation leaves the flag up, and the next edit must
        // still try again. The expert keeps the explicit cycle.
        Config.DocumentEdited += (_, _) =>
        {
            if (_mode.IsNovice && Config.SaveCommand.CanExecute(null))
                Config.SaveCommand.Execute(null);
        };

        // The file as saved is what the engine reads: a save moves what it says of the e-mail
        // accounts, so the tab asks again — while it shows, and never from another tab (STUDIO-69).
        // Once the saves pause: the novice's file is saved at every keystroke, and a child per
        // key would be started only to be stopped by the next.
        Config.Saved += (_, _) =>
        {
            if (IsMailsTab)
                Config.Email.RefreshStatesSoon();
            else if (IsDriveTab)
                Config.Drive.RefreshStatesSoon();
        };

        ShowModelCommand = new RelayCommand(() => ActiveTab = ModelTab);
        ShowRagCommand = new RelayCommand(() => ActiveTab = RagTab);
        ShowKeysCommand = new RelayCommand(() => ActiveTab = KeysTab);
        // The folders tab re-reads the teams' own folders on arrival (ActiveTab): the section is
        // a view over the sidecars, and a team adopted or deleted since the last visit must show.
        // Asked again while it shows, it reads them again.
        ShowFoldersCommand = new RelayCommand(() =>
        {
            if (IsFoldersTab)
                TeamFolders.Refresh();
            else
                ActiveTab = FoldersTab;
        });
        ShowLimitsCommand = new RelayCommand(() => ActiveTab = LimitsTab);
        ShowJsonCommand = new RelayCommand(() => ActiveTab = JsonTab);
        ShowToolsCommand = new RelayCommand(() => ActiveTab = ToolsTab);
        ShowToolKeysCommand = new RelayCommand(() => ActiveTab = ToolKeysTab);
        ShowShellCommand = new RelayCommand(() => ActiveTab = ShellTab);
        ShowMcpCommand = new RelayCommand(() => ActiveTab = McpTab);
        ShowStudioCommand = new RelayCommand(() => ActiveTab = StudioTab);
        ShowMailsCommand = new RelayCommand(() => ActiveTab = MailsTab);
        ShowDriveCommand = new RelayCommand(() => ActiveTab = DriveTab);

        // Lot 5: leaving with unsaved edits asks first; the three answers end the question.
        SaveAndLeaveCommand = new AsyncRelayCommand(async () =>
        {
            if (await Config.SaveAsync())
                ProceedLeave();
        });
        DiscardAndLeaveCommand = new RelayCommand(() =>
        {
            Config.Discard();
            ProceedLeave();
        });
        StayCommand = new RelayCommand(() => SetPendingLeave(null));
        // Saved or cancelled from the bar while the question is open: nothing is left to ask.
        Config.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConfigTabViewModel.HasPendingChanges) && !Config.HasPendingChanges && _pendingLeave is not null)
                ProceedLeave();
        };
    }

    /// <summary>True while the screen asks what to do with the unsaved edits before leaving.</summary>
    public bool IsAskingToLeave => _pendingLeave is not null;

    /// <summary>« Save and leave »: saves, then goes where the user was heading; a refused save stays.</summary>
    public AsyncRelayCommand SaveAndLeaveCommand { get; }

    /// <summary>« Leave without saving »: drops the edits, then goes on.</summary>
    public RelayCommand DiscardAndLeaveCommand { get; }

    /// <summary>« Stay »: closes the question and keeps the edits on screen.</summary>
    public RelayCommand StayCommand { get; }

    /// <summary>
    /// Asks to leave the screen (lot 5): true when nothing is pending, so the caller goes on; false
    /// when the edits are not saved - the screen asks, and <paramref name="proceed"/> runs once the
    /// user saved or dropped them.
    /// </summary>
    public bool TryLeave(Action proceed)
    {
        ArgumentNullException.ThrowIfNull(proceed);

        if (!Config.HasPendingChanges)
            return true;

        SetPendingLeave(proceed);
        return false;
    }

    /// <summary>Shows one section, the way its entry in the side navigation does (the Diagnostic's actions).</summary>
    public void ShowSection(string tab)
    {
        if (tab == FoldersTab)
            ShowFoldersCommand.Execute(null);
        else
            ActiveTab = tab;
    }

    private void ProceedLeave()
    {
        var proceed = _pendingLeave;
        SetPendingLeave(null);
        proceed?.Invoke();
    }

    private void SetPendingLeave(Action? proceed)
    {
        _pendingLeave = proceed;
        OnPropertyChanged(nameof(IsAskingToLeave));
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

    /// <summary>The Studio tab: how Studio itself behaves — the provider balance first (STUDIO-35).</summary>
    public StudioSettingsViewModel Studio { get; }

    /// <summary>The window-wide mode switch, for the view's expert/novice visibilities.</summary>
    public UiModeViewModel Mode => _mode;

    /// <summary>The showing tab; an expert tab requested in novice mode falls back to the model tab.</summary>
    public string ActiveTab
    {
        get => _activeTab;
        set
        {
            var requested = value is RagTab or KeysTab or FoldersTab or LimitsTab or JsonTab or ToolsTab or ToolKeysTab or ShellTab or McpTab or StudioTab or MailsTab or DriveTab ? value : ModelTab;
            if (_mode.IsNovice && requested is RagTab or LimitsTab or JsonTab or McpTab or ShellTab)
                requested = ModelTab;

            var wasMails = IsMailsTab;
            var wasDrive = IsDriveTab;
            if (SetProperty(ref _activeTab, requested))
            {
                OnPropertiesChanged(
                    nameof(IsModelTab), nameof(IsRagTab), nameof(IsKeysTab), nameof(IsFoldersTab), nameof(IsLimitsTab), nameof(IsJsonTab),
                    nameof(IsToolsTab), nameof(IsToolKeysTab), nameof(IsShellTab), nameof(IsMcpTab), nameof(IsStudioTab), nameof(IsMailsTab), nameof(IsDriveTab));
                // The classeur selects a tab through ActiveTab itself, not through its command.
                if (IsFoldersTab)
                    TeamFolders.Refresh();

                // The e-mail tab asks the engine about its accounts on arrival, and stops what it
                // was running when it is left: a connection test never outlives its tab (STUDIO-69).
                if (IsMailsTab)
                    _ = Config.Email.RefreshStatesAsync();
                else if (wasMails)
                    Config.Email.StopActivity();

                // The Drive tab does the same with its accounts.
                if (IsDriveTab)
                    _ = Config.Drive.RefreshStatesAsync();
                else if (wasDrive)
                    Config.Drive.StopActivity();
            }
        }
    }

    /// <summary>True while the AI-model tab shows.</summary>
    public bool IsModelTab => _activeTab == ModelTab;

    /// <summary>True while the expert RAG-model tab shows.</summary>
    public bool IsRagTab => _activeTab == RagTab;

    /// <summary>True while the API-keys tab shows.</summary>
    public bool IsKeysTab => _activeTab == KeysTab;

    /// <summary>True while the allowed-folders tab shows.</summary>
    public bool IsFoldersTab => _activeTab == FoldersTab;

    /// <summary>True while the expert limits-and-logs tab shows.</summary>
    public bool IsLimitsTab => _activeTab == LimitsTab;

    /// <summary>True while the expert raw-JSON tab shows.</summary>
    public bool IsJsonTab => _activeTab == JsonTab;

    /// <summary>True while the tools tab shows.</summary>
    public bool IsToolsTab => _activeTab == ToolsTab;

    /// <summary>Whether the tool keys section is the one shown.</summary>
    public bool IsToolKeysTab => _activeTab == ToolKeysTab;

    /// <summary>Whether the shell commands section is the one shown.</summary>
    public bool IsShellTab => _activeTab == ShellTab;

    /// <summary>True while the expert MCP tab shows.</summary>
    public bool IsMcpTab => _activeTab == McpTab;

    /// <summary>True while the Studio tab shows.</summary>
    public bool IsStudioTab => _activeTab == StudioTab;

    /// <summary>True while the e-mail tab shows.</summary>
    public bool IsMailsTab => _activeTab == MailsTab;

    /// <summary>True while the Drive tab shows.</summary>
    public bool IsDriveTab => _activeTab == DriveTab;

    /// <summary>Shows the model tab.</summary>
    public RelayCommand ShowModelCommand { get; }

    /// <summary>Shows the RAG-model tab.</summary>
    public RelayCommand ShowRagCommand { get; }

    /// <summary>Shows the API-keys tab.</summary>
    public RelayCommand ShowKeysCommand { get; }

    /// <summary>Shows the folders tab.</summary>
    public RelayCommand ShowFoldersCommand { get; }

    /// <summary>Shows the limits tab.</summary>
    public RelayCommand ShowLimitsCommand { get; }

    /// <summary>Shows the file tab.</summary>
    public RelayCommand ShowJsonCommand { get; }

    /// <summary>Shows the tools tab.</summary>
    public RelayCommand ShowToolsCommand { get; }

    /// <summary>Shows the tool keys section.</summary>
    public RelayCommand ShowToolKeysCommand { get; }

    /// <summary>Shows the shell commands section (expert).</summary>
    public RelayCommand ShowShellCommand { get; }

    /// <summary>Shows the MCP tab.</summary>
    public RelayCommand ShowMcpCommand { get; }

    /// <summary>Shows the Studio tab.</summary>
    public RelayCommand ShowStudioCommand { get; }

    /// <summary>Shows the e-mail tab.</summary>
    public RelayCommand ShowMailsCommand { get; }

    /// <summary>Shows the Drive tab.</summary>
    public RelayCommand ShowDriveCommand { get; }

    /// <summary>
    /// The screen shows again — its sidebar entry was chosen: the e-mail tab, when it is the one
    /// showing, reads the states of its accounts again (STUDIO-69).
    /// </summary>
    public void Enter()
    {
        if (IsMailsTab)
            _ = Config.Email.RefreshStatesAsync();
        else if (IsDriveTab)
            _ = Config.Drive.RefreshStatesAsync();
    }

    /// <summary>
    /// The screen is left — another sidebar entry, or the window closing: whatever the e-mail tab
    /// still has running is stopped, so that no <c>orkeon</c> process outlives it (STUDIO-69).
    /// </summary>
    public void Leave()
    {
        Config.Email.StopActivity();
        Config.Drive.StopActivity();
    }

    private void OnModeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(UiModeViewModel.IsNovice) or nameof(UiModeViewModel.Mode)))
            return;

        Config.Email.IsExpert = _mode.IsExpert;
        Config.Drive.IsExpert = _mode.IsExpert;
        if (_mode.IsNovice && (IsRagTab || IsLimitsTab || IsJsonTab || IsMcpTab || IsShellTab))
            ActiveTab = ModelTab;
    }
}
