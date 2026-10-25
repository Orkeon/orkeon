using System.Collections.Specialized;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The Settings classeur (refonte lot 9, R14 grouped variant): each tab says its state under its
/// title — what it holds, in the warning tone when something there needs fixing, the green dot
/// when the settings file was checked and holds. The marks are the StepTab states' names
/// (Empty, Done, Expected, Warning, Verified), read from what the sections already hold. Since
/// lot 11 the Models group holds three tabs: the AI model, the RAG's model and the API keys.
/// </summary>
public sealed partial class SettingsScreenViewModel
{
    private const string EmptyState = "Empty";
    private const string DoneState = "Done";
    private const string WarningState = "Warning";
    private const string VerifiedState = "Verified";
    private const string ExpectedState = "Expected";

    private readonly IStudioStrings _strings;

    /// <summary>« 2 models · Local by default », or what is still to choose.</summary>
    public string ModelTabSubtitle =>
        Profiles.IsEmpty
            ? _strings[StudioStringKeys.SettingsTabModelEmpty]
            : Profiles.HasStudioProfile
                ? _strings.Format(StudioStringKeys.SettingsTabModelCount, Profiles.Profiles.Count,
                    Profiles.DefaultProfileName ?? Profiles.StudioProfileName ?? string.Empty)
                : _strings[StudioStringKeys.SettingsTabModelNoAssistant];

    /// <summary>Warning while the assistant has no model; done once it has one.</summary>
    public string ModelTabState => Profiles.HasStudioProfile ? DoneState : WarningState;

    /// <summary>The model the document search calls, as its picker names it.</summary>
    public string RagTabSubtitle => Profiles.SelectedRagProfile?.Label ?? string.Empty;

    /// <summary>Done once the picker names the RAG's model (the default one unless another is chosen), empty before.</summary>
    public string RagTabState => Profiles.SelectedRagProfile is null ? EmptyState : DoneState;

    /// <summary>The key variables whose value is in place.</summary>
    private int StoredKeyCount => Profiles.Secrets.Count(secret => secret.HasKey);

    /// <summary>« 2 keys remembered », or nothing to provide.</summary>
    public string KeysTabSubtitle =>
        Profiles.HasSecrets
            ? _strings.Format(StudioStringKeys.SettingsTabKeysCount, StoredKeyCount)
            : _strings[StudioStringKeys.SettingsTabKeysNone];

    /// <summary>The green dot when every key is in place, the amber dot while one is missing, empty without keys.</summary>
    public string KeysTabState =>
        !Profiles.HasSecrets ? EmptyState : StoredKeyCount == Profiles.Secrets.Count ? VerifiedState : ExpectedState;

    /// <summary>The fiches whose row carries a finding of the validation.</summary>
    private int FolderProblemCount => Config.Mounts.Mounts.Count(mount => mount.HasRowProblem);

    /// <summary>« 3 folders allowed », or how many are to fix.</summary>
    public string FoldersTabSubtitle =>
        FolderProblemCount is > 0 and var problems
            ? _strings.Format(StudioStringKeys.SettingsTabFoldersProblems, problems)
            : Config.Mounts.Mounts.Count == 0
                ? _strings[StudioStringKeys.SettingsTabFoldersEmpty]
                : _strings.Format(StudioStringKeys.SettingsTabFoldersCount, Config.Mounts.Mounts.Count);

    /// <summary>Warning on a folder to fix, done with folders allowed, empty without.</summary>
    public string FoldersTabState =>
        FolderProblemCount > 0 ? WarningState : Config.Mounts.Mounts.Count == 0 ? EmptyState : DoneState;

    /// <summary>« 119 tools ».</summary>
    public string ToolsTabSubtitle => _strings.Format(StudioStringKeys.SettingsTabToolsCount, Tools.ToolCount);

    /// <summary>« 2 accounts », or none.</summary>
    public string MailsTabSubtitle => AccountsSubtitle(Config.Email.Accounts.Count);

    /// <summary>Done with an account, empty without.</summary>
    public string MailsTabState => Config.Email.HasAccounts ? DoneState : EmptyState;

    /// <summary>« 1 account », or none.</summary>
    public string DriveTabSubtitle => AccountsSubtitle(Config.Drive.Accounts.Count);

    /// <summary>Done with an account, empty without.</summary>
    public string DriveTabState => Config.Drive.HasAccounts ? DoneState : EmptyState;

    /// <summary>« 2 servers », or none.</summary>
    public string McpTabSubtitle =>
        Config.Mcp.HasServers
            ? _strings.Format(StudioStringKeys.SettingsTabMcpCount, Config.Mcp.Servers.Count)
            : _strings[StudioStringKeys.SettingsTabMcpEmpty];

    /// <summary>Done with a server, empty without.</summary>
    public string McpTabState => Config.Mcp.HasServers ? DoneState : EmptyState;

    /// <summary>What the Studio tab is about.</summary>
    public string StudioTabSubtitle => _strings[StudioStringKeys.SettingsTabStudioSub];

    /// <summary>What the limits tab is about.</summary>
    public string LimitsTabSubtitle => _strings[StudioStringKeys.SettingsTabLimitsSub];

    private int FileErrorCount => Config.ValidationMessages.Count(message => message.IsError);

    /// <summary>The settings file's state: « Valid file », or how many errors are to fix.</summary>
    public string FileTabSubtitle =>
        FileErrorCount is > 0 and var errors
            ? _strings.Format(StudioStringKeys.SettingsTabFileErrors, errors)
            : _strings[StudioStringKeys.SettingsTabFileValid];

    /// <summary>Warning on a blocking error, the green dot otherwise.</summary>
    public string FileTabState => Config.HasBlockingErrors ? WarningState : VerifiedState;

    private string AccountsSubtitle(int count) =>
        count == 0
            ? _strings[StudioStringKeys.SettingsTabAccountsEmpty]
            : _strings.Format(StudioStringKeys.SettingsTabAccountsCount, count);

    /// <summary>Follows every source the tabs read: the lists, the validation and the language.</summary>
    private void InitializeTabStates()
    {
        NotifyCollectionChangedEventHandler collectionChanged = (_, _) => RefreshTabStates();
        Profiles.Profiles.CollectionChanged += collectionChanged;
        // A key remembered changes the keys tab: the rows are rebuilt with the profiles, so each
        // new row is followed as it comes.
        EventHandler keyStored = (_, _) => RefreshTabStates();
        Profiles.Secrets.CollectionChanged += (_, e) =>
        {
            foreach (var row in e.NewItems?.OfType<SecretRowViewModel>() ?? [])
                row.Stored += keyStored;
            RefreshTabStates();
        };
        foreach (var row in Profiles.Secrets)
            row.Stored += keyStored;
        Config.Mounts.Mounts.CollectionChanged += collectionChanged;
        Config.ValidationMessages.CollectionChanged += collectionChanged;
        Config.Mounts.ValidationMessages.CollectionChanged += collectionChanged;
        Config.Email.Accounts.CollectionChanged += collectionChanged;
        Config.Drive.Accounts.CollectionChanged += collectionChanged;
        Config.Mcp.Servers.CollectionChanged += collectionChanged;
        Profiles.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ModelProfilesViewModel.HasStudioProfile) or nameof(ModelProfilesViewModel.StudioProfileName)
                or nameof(ModelProfilesViewModel.IsEmpty) or nameof(ModelProfilesViewModel.DefaultProfileName)
                or nameof(ModelProfilesViewModel.SelectedRagProfile) or nameof(ModelProfilesViewModel.HasSecrets))
            {
                RefreshTabStates();
            }
        };
        Config.Mounts.PropertyChanged += (_, _) => RefreshTabStates();
        _strings.CultureChanged += (_, _) => RefreshTabStates();
    }

    private void RefreshTabStates() =>
        OnPropertiesChanged(
            nameof(ModelTabSubtitle), nameof(ModelTabState),
            nameof(RagTabSubtitle), nameof(RagTabState),
            nameof(KeysTabSubtitle), nameof(KeysTabState),
            nameof(FoldersTabSubtitle), nameof(FoldersTabState),
            nameof(ToolsTabSubtitle),
            nameof(MailsTabSubtitle), nameof(MailsTabState),
            nameof(DriveTabSubtitle), nameof(DriveTabState),
            nameof(McpTabSubtitle), nameof(McpTabState),
            nameof(StudioTabSubtitle), nameof(LimitsTabSubtitle),
            nameof(FileTabSubtitle), nameof(FileTabState));
}
