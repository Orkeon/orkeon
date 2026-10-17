using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Drive;
using Orkeon.Studio.Core.Email;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.ViewModels.Common;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// One drive account of the Drive tab, on the model of <see cref="EmailAccountRowViewModel"/>:
/// every key of the account as a field — text for a value, a switch for each of the six rights —,
/// and what the run will say of the account as the file holds it (<see cref="Problems"/>). Every
/// edit writes the whole account in place; a field emptied removes its key, and a value the user
/// did not touch keeps the spelling the file had.
/// <para>
/// The OAuth client secret of a Google Drive account is typed here and never written: it goes to
/// the key store under a variable — the one the file names, else one derived from the account's
/// name (<see cref="DriveSecretNames"/>), which the first value kept writes into the file.
/// </para>
/// <para>
/// Whether the account is ready, and whether it connects, is the engine's to say: <see cref="State"/>
/// is its answer for the file as saved, <see cref="TestCommand"/> its connection check. Every drive
/// account signs in with OAuth2: <see cref="SignInCommand"/> opens the panel of <see cref="SignIn"/>,
/// which follows <c>orkeon drive login</c> — a code for OneDrive, an address for Google Drive —;
/// <see cref="SignOutCommand"/> asks first, then runs <c>orkeon drive logout</c>.
/// </para>
/// </summary>
public sealed class DriveAccountRowViewModel : ObservableObject
{
    private readonly DriveSectionViewModel _owner;
    private readonly IStudioStrings _strings;
    private DriveAccountDefinition _loaded;
    private IReadOnlyList<ValidationMessageViewModel> _problems = [];
    private string _name;
    private string? _provider;
    private DriveRights _rights;
    private string? _rightsRaw;
    private string _root;
    private string _clientId;
    private string _clientSecretEnvVar;
    private string _tenant;
    private bool? _allowPublicLinks;
    private string _maxUploadMegabytes;
    private int? _maxUploadMegabytesNumber;
    private string _timeoutSeconds;
    private int? _timeoutSecondsNumber;
    private bool _isRenaming;
    private string _activeTab = AccountTab;
    private bool _isConfirmingRemove;
    private string _renameText = "";
    private SecretRowViewModel? _clientSecret;
    private bool _clientSecretKept;
    private DriveAccountState? _state;
    private EmailCliFailure? _stateFailure;
    private EmailCheckOutcome? _lastCheck;
    private bool _isTesting;
    private CancellationTokenSource? _test;
    private EmailSignInViewModel? _signIn;
    private bool _isConfirmingSignOut;
    private string? _signOutFailure;

    internal DriveAccountRowViewModel(DriveAccountDefinition account, DriveSectionViewModel owner, IStudioStrings strings)
    {
        _owner = owner;
        _strings = strings;
        _loaded = account;
        _name = account.Name;
        _provider = account.Provider;
        _rights = account.Rights;
        _rightsRaw = account.RightsRaw;
        _root = account.Root ?? "";
        _clientId = account.ClientId ?? "";
        _clientSecretEnvVar = account.ClientSecretEnvVar ?? "";
        _tenant = account.Tenant ?? "";
        _allowPublicLinks = account.AllowPublicLinks;
        _maxUploadMegabytesNumber = account.MaxUploadMegabytes;
        _maxUploadMegabytes = NumberText(account.MaxUploadMegabytes);
        _timeoutSecondsNumber = account.TimeoutSeconds;
        _timeoutSeconds = NumberText(account.TimeoutSeconds);
        foreach (var pattern in account.AllowedRecipients)
            Recipients.Add(new DriveRecipientRowViewModel(this, pattern));

        AddRecipientCommand = new RelayCommand(() => Recipients.Add(new DriveRecipientRowViewModel(this, "")));
        RemoveRecipientCommand = new RelayCommand(
            parameter => { if (parameter is DriveRecipientRowViewModel line) RemoveRecipient(line); },
            parameter => parameter is DriveRecipientRowViewModel);
        ConfirmRenameCommand = new RelayCommand(ConfirmRename, () => _isRenaming && RenameRefusal is null);
        CancelRenameCommand = new RelayCommand(() => IsRenaming = false);
        ConfirmRemoveCommand = new RelayCommand(() => { if (_isConfirmingRemove) _owner.Remove(this); });
        CancelRemoveCommand = new RelayCommand(() => IsConfirmingRemove = false);
        TestCommand = new AsyncRelayCommand(TestAsync, () => _owner.CanTest);
        SignInCommand = new RelayCommand(BeginSignIn, () => CanSign && _signIn is not { IsWaiting: true });
        SignOutCommand = new RelayCommand(() => { if (CanSign) _owner.ArmSignOut(this); }, () => CanSign);
        ConfirmSignOutCommand = new AsyncRelayCommand(SignOutAsync, () => _isConfirmingSignOut);
        CancelSignOutCommand = new RelayCommand(() => IsConfirmingSignOut = false);
        ShowAccountTabCommand = new RelayCommand(() => ActiveTab = AccountTab);
        ShowRightsTabCommand = new RelayCommand(() => ActiveTab = RightsTab);
        ShowAuthTabCommand = new RelayCommand(() => ActiveTab = AuthTab);
        RefreshProblems();
    }

    // ── the three tabs of the form ──

    /// <summary>The tab of the account itself: its drive, its root folder, its upload limit.</summary>
    public const string AccountTab = "account";

    /// <summary>The tab of what an agent may do: the six rights, the recipients, the public links.</summary>
    public const string RightsTab = "rights";

    /// <summary>The tab of the sign-in: the client id, the client secret, the variable, the tenant.</summary>
    public const string AuthTab = "auth";

    /// <summary>The showing tab of the form; the head of the account stays above whichever shows.</summary>
    public string ActiveTab
    {
        get => _activeTab;
        set
        {
            var requested = value is RightsTab or AuthTab ? value : AccountTab;
            if (SetProperty(ref _activeTab, requested))
            {
                OnPropertiesChanged(nameof(IsAccountTab), nameof(IsRightsTab), nameof(IsAuthTab));
                _owner.TabMoved(this);
            }
        }
    }

    /// <summary>True while the account tab shows.</summary>
    public bool IsAccountTab => _activeTab == AccountTab;

    /// <summary>True while the rights tab shows.</summary>
    public bool IsRightsTab => _activeTab == RightsTab;

    /// <summary>True while the sign-in tab shows.</summary>
    public bool IsAuthTab => _activeTab == AuthTab;

    /// <summary>Shows the account tab.</summary>
    public RelayCommand ShowAccountTabCommand { get; }

    /// <summary>Shows the rights tab.</summary>
    public RelayCommand ShowRightsTabCommand { get; }

    /// <summary>Shows the sign-in tab.</summary>
    public RelayCommand ShowAuthTabCommand { get; }

    /// <summary>The name the file holds the account under: what an agent passes as <c>account</c>.</summary>
    public string Name => _name;

    /// <summary>
    /// The drive, in the engine's spelling; a text the engine does not know shows as written (no
    /// entry of the list then matches it), and null when the file names none.
    /// </summary>
    public string? Provider
    {
        get => Shown(DriveSection.Providers, _provider);
        set
        {
            var chosen = string.IsNullOrWhiteSpace(value) ? null : value;
            if (string.Equals(Provider, chosen, StringComparison.Ordinal))
                return;

            _provider = chosen;
            OnPropertyChanged();
            Commit();
        }
    }

    /// <summary><c>Root</c>: the folder the agents are confined to, as a path from the root of the drive.</summary>
    public string Root
    {
        get => _root;
        set => SetText(ref _root, value);
    }

    /// <summary>The root as the list shows it: the folder, or <c>/</c> for the whole drive.</summary>
    public string RootLine => _root.Trim() is { Length: > 0 } root ? root : "/";

    /// <summary><c>MaxUploadMegabytes</c>, as typed; empty leaves the engine's 100.</summary>
    public string MaxUploadMegabytes
    {
        get => _maxUploadMegabytes;
        set => SetNumber(ref _maxUploadMegabytes, ref _maxUploadMegabytesNumber, value);
    }

    /// <summary><c>TimeoutSeconds</c>, as typed (expert).</summary>
    public string TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => SetNumber(ref _timeoutSeconds, ref _timeoutSecondsNumber, value);
    }

    /// <summary>
    /// Whether a number field holds something that is no whole number. Such a text stays on
    /// screen and is not written: the document keeps the last number the field held.
    /// </summary>
    public bool HasUnreadNumber => Unread(_maxUploadMegabytes) || Unread(_timeoutSeconds);

    // ── the six rights ──

    /// <summary>List, search, read and download files.</summary>
    public bool CanRead
    {
        get => Has(DriveRights.Read);
        set => SetRight(DriveRights.Read, value);
    }

    /// <summary>Upload files, replace their content and create folders.</summary>
    public bool CanWrite
    {
        get => Has(DriveRights.Write);
        set => SetRight(DriveRights.Write, value);
    }

    /// <summary>Move and rename files and folders.</summary>
    public bool CanOrganize
    {
        get => Has(DriveRights.Organize);
        set => SetRight(DriveRights.Organize, value);
    }

    /// <summary>Share files with the allowed recipients, and create public links when allowed.</summary>
    public bool CanShare
    {
        get => Has(DriveRights.Share);
        set => SetRight(DriveRights.Share, value);
    }

    /// <summary>Move files and folders to the trash.</summary>
    public bool CanDelete
    {
        get => Has(DriveRights.Delete);
        set => SetRight(DriveRights.Delete, value);
    }

    /// <summary>Delete files and folders permanently.</summary>
    public bool CanPurge
    {
        get => Has(DriveRights.Purge);
        set => SetRight(DriveRights.Purge, value);
    }

    // ── sharing ──

    /// <summary><c>Share:AllowedRecipients</c>, one pattern per line; a blank line is a place to type and is not written.</summary>
    public ObservableCollection<DriveRecipientRowViewModel> Recipients { get; } = [];

    /// <summary>Adds a blank line to <see cref="Recipients"/>.</summary>
    public RelayCommand AddRecipientCommand { get; }

    /// <summary>Removes the line given as parameter.</summary>
    public RelayCommand RemoveRecipientCommand { get; }

    /// <summary>
    /// <c>Share:AllowPublicLinks</c>, as the engine will apply it: off unless the file says on.
    /// Off is the engine's default, so turning it off removes the key rather than spelling it.
    /// </summary>
    public bool AllowPublicLinks
    {
        get => _allowPublicLinks ?? false;
        set
        {
            if (AllowPublicLinks == value)
                return;

            _allowPublicLinks = value ? true : null;
            OnPropertyChanged();
            Commit();
        }
    }

    /// <summary>
    /// Whether the sharing rules show: as soon as the account may share, and always for the
    /// expert — no key is reachable through the raw file alone.
    /// </summary>
    public bool ShowRecipients => CanShare || _owner.IsExpert;

    /// <summary>Whether the account may share and lists nobody: the engine then refuses every recipient.</summary>
    public bool ShowShareClosed => CanShare && !Recipients.Any(line => !string.IsNullOrWhiteSpace(line.Pattern));

    // ── signing in ──

    /// <summary><c>Auth:ClientId</c>: the OAuth client id.</summary>
    public string ClientId
    {
        get => _clientId;
        set => SetText(ref _clientId, value);
    }

    /// <summary><c>Auth:ClientSecretEnvVar</c>: the <i>name</i> of the variable that holds the OAuth client secret.</summary>
    public string ClientSecretEnvVar
    {
        get => _clientSecretEnvVar;
        set => SetText(ref _clientSecretEnvVar, value);
    }

    /// <summary><c>Auth:Tenant</c>: the Microsoft tenant of a OneDrive account.</summary>
    public string Tenant
    {
        get => _tenant;
        set => SetText(ref _tenant, value);
    }

    // ── what the run will say ──

    /// <summary>
    /// What the run will say of the account as the file holds it: a key the engine does not know
    /// and a value it cannot read first, the engine's rules otherwise. Each is a warning — an
    /// incomplete account stops no save.
    /// </summary>
    public IReadOnlyList<ValidationMessageViewModel> Problems => _problems;

    /// <summary>Whether the run will set the account aside.</summary>
    public bool HasProblems => _problems.Count > 0;

    // ── what the engine makes of the account, and the connection test ──

    /// <summary>
    /// What <c>orkeon drive accounts</c> answered for the account as the file was last saved;
    /// <see cref="EmailAccountReadiness.None"/> until an answer names it.
    /// </summary>
    public EmailAccountReadiness StateKind
    {
        get
        {
            if (_stateFailure is not null)
                return EmailAccountReadiness.Unknown;

            return _state switch
            {
                null => EmailAccountReadiness.None,
                { IsSetAside: true } => EmailAccountReadiness.SetAside,
                { Ready: true } => EmailAccountReadiness.Ready,
                _ => EmailAccountReadiness.NotReady,
            };
        }
    }

    /// <summary>
    /// The state, localized: "Ready", "Not ready", "Set aside", or "Unknown — why" when the CLI
    /// is missing or failed; null while there is none.
    /// </summary>
    public string? State => StateKind switch
    {
        EmailAccountReadiness.Ready => _strings[StudioStringKeys.MailStateReady],
        EmailAccountReadiness.NotReady => _strings[StudioStringKeys.MailStateNotReady],
        EmailAccountReadiness.SetAside => _strings[StudioStringKeys.MailStateSetAside],
        EmailAccountReadiness.Unknown => _strings.Format(
            StudioStringKeys.MailStateUnknown, _owner.FailureText(_stateFailure!)),
        _ => null,
    };

    /// <summary>
    /// The engine's sentence on what a not-ready account lacks, in English, as printed — it names
    /// the variable or the sign-in to run. Null otherwise.
    /// </summary>
    public string? StateDetail =>
        StateKind == EmailAccountReadiness.NotReady && _state?.Problem is { Length: > 0 } problem ? problem : null;

    /// <summary>Whether the row has a state to show.</summary>
    public bool HasState => StateKind != EmailAccountReadiness.None;

    /// <summary>Whether <see cref="StateDetail"/> has something to say.</summary>
    public bool HasStateDetail => StateDetail is not null;

    /// <summary>Whether the engine found the account ready to connect.</summary>
    public bool IsReady => StateKind == EmailAccountReadiness.Ready;

    /// <summary>
    /// "Test the connection": runs <c>orkeon drive check</c> on the account as the saved file
    /// declares it — a real connection that finds the root folder and lists it. Only ever started
    /// here, by a click; disabled while the document holds unsaved edits.
    /// </summary>
    public AsyncRelayCommand TestCommand { get; }

    /// <summary>Whether a connection test is in flight.</summary>
    public bool IsTesting
    {
        get => _isTesting;
        private set => SetProperty(ref _isTesting, value);
    }

    /// <summary>
    /// The verdict of the last connection test; null before any, while one runs, after one that
    /// was stopped, and as soon as the account is edited or renamed.
    /// </summary>
    public EmailCheckOutcome? LastCheck
    {
        get => _lastCheck;
        private set
        {
            if (SetProperty(ref _lastCheck, value))
            {
                OnPropertiesChanged(
                    nameof(HasLastCheck), nameof(LastCheckReachable), nameof(LastCheckHeadline), nameof(LastCheckDetail),
                    nameof(LastCheckHint), nameof(HasLastCheckHint));
            }
        }
    }

    /// <summary>Whether a verdict shows.</summary>
    public bool HasLastCheck => _lastCheck is not null;

    /// <summary>Whether the last test connected.</summary>
    public bool LastCheckReachable => _lastCheck?.Kind == EmailCheckKind.Reachable;

    /// <summary>The verdict, localized; a refusal Studio answers with a gesture of its own names that gesture.</summary>
    public string? LastCheckHeadline => _lastCheck switch
    {
        null => null,
        { Code: EmailCheckCodes.LoginRequired } => _strings[StudioStringKeys.MailTestSignInFirst],
        { Code: EmailCheckCodes.CredentialMissing } => _strings[StudioStringKeys.DriveTestSecretFirst],
        { Kind: EmailCheckKind.Reachable } => _strings[StudioStringKeys.MailTestReachable],
        { Kind: EmailCheckKind.OperatorFixable } => _strings[StudioStringKeys.MailTestOperatorFixable],
        { Kind: EmailCheckKind.ServerOrNetwork } => _strings[StudioStringKeys.MailTestServerOrNetwork],
        _ => _strings[StudioStringKeys.MailTestFailed],
    };

    /// <summary>
    /// What to do from here, when the engine's sentence sends to a terminal: « Sign in » on the row,
    /// the client-secret field of the Sign-in tab. Null for every other verdict.
    /// </summary>
    public string? LastCheckHint => _lastCheck?.Code switch
    {
        EmailCheckCodes.LoginRequired => _strings[StudioStringKeys.MailTestSignInFirstHint],
        EmailCheckCodes.CredentialMissing => _strings[StudioStringKeys.DriveTestSecretFirstHint],
        _ => null,
    };

    /// <summary>Whether the verdict comes with a gesture of Studio's.</summary>
    public bool HasLastCheckHint => LastCheckHint is not null;

    /// <summary>
    /// The engine's sentence, in English, as printed: the root folder and what it holds on success,
    /// what was refused otherwise. Without a CLI, the sentence of the home screen.
    /// </summary>
    public string? LastCheckDetail => _lastCheck switch
    {
        null => null,
        { Kind: EmailCheckKind.Unavailable, ExitCode: null } => _owner.EngineMissingText,
        var outcome => outcome.Sentence,
    };

    // ── signing in and out ──

    /// <summary>Whether the account names a drive the engine knows: every one signs in with OAuth2.</summary>
    public bool ShowSignIn => DriveAccountRules.ProviderOf(_provider) is not null;

    /// <summary>
    /// "Sign in": runs <c>orkeon drive login</c> on the account as the saved file declares it, and
    /// opens the panel that says what to do meanwhile. Disabled while the document holds unsaved
    /// edits — the verb reads the file — and while a sign-in of this account waits.
    /// </summary>
    public RelayCommand SignInCommand { get; }

    /// <summary>The sign-in panel of the account: in flight, or ended and not closed yet; null otherwise.</summary>
    public EmailSignInViewModel? SignIn
    {
        get => _signIn;
        private set
        {
            if (!SetProperty(ref _signIn, value))
                return;

            OnPropertyChanged(nameof(HasSignIn));
            RefreshTest();
        }
    }

    /// <summary>Whether a sign-in panel shows.</summary>
    public bool HasSignIn => _signIn is not null;

    /// <summary>"Sign out": asks first — nothing is forgotten until <see cref="ConfirmSignOutCommand"/>.</summary>
    public RelayCommand SignOutCommand { get; }

    /// <summary>Whether the sign-out question shows.</summary>
    public bool IsConfirmingSignOut
    {
        get => _isConfirmingSignOut;
        private set
        {
            if (SetProperty(ref _isConfirmingSignOut, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    /// <summary>"Forget the stored tokens of {name}? The account will need a new sign-in.", localized.</summary>
    public string SignOutConfirmText =>
        _strings.Format(StudioStringKeys.MailSignOutConfirm, _name);

    /// <summary>Runs <c>orkeon drive logout</c> on the account, then reads the states again.</summary>
    public AsyncRelayCommand ConfirmSignOutCommand { get; }

    /// <summary>Closes the sign-out question; nothing was forgotten.</summary>
    public RelayCommand CancelSignOutCommand { get; }

    /// <summary>Why the last sign-out did not happen, as the verb said it; null after one that did, and before any.</summary>
    public string? SignOutFailure
    {
        get => _signOutFailure;
        private set
        {
            if (SetProperty(ref _signOutFailure, value))
                OnPropertyChanged(nameof(HasSignOutFailure));
        }
    }

    /// <summary>Whether a sign-out failure shows.</summary>
    public bool HasSignOutFailure => _signOutFailure is not null;

    // ── what each mode sees ──

    /// <summary>Whether the expert's fields show.</summary>
    public bool ShowExpertFields => _owner.IsExpert;

    // The expert's sign-in fields show when the drive calls for them, or when the file already
    // holds them: a key the file carries must stay reachable, and a key nothing uses — the tenant
    // of a Google Drive account — is noise, which the user reads as something to fill.

    /// <summary>Whether the name of the client-secret variable shows: the expert, on a Google Drive account — or a file that names one.</summary>
    public bool ShowClientSecretEnvVar =>
        _owner.IsExpert && (IsGoogleDrive || !string.IsNullOrWhiteSpace(_clientSecretEnvVar));

    /// <summary>Whether the Microsoft tenant shows: the expert, on a OneDrive account — or a file that names one.</summary>
    public bool ShowTenant =>
        _owner.IsExpert && (Provider == DriveSection.Values.OneDrive || !string.IsNullOrWhiteSpace(_tenant));

    // ── the client secret: typed here, kept outside any file ──

    /// <summary>
    /// The line the OAuth client secret is typed on: for a Google Drive account, the one drive the
    /// engine requires it for; null otherwise. It stores under the variable the file names, else
    /// under the one derived from the account's name — and the first value kept writes that name.
    /// </summary>
    public SecretRowViewModel? ClientSecret => _clientSecret;

    /// <summary>Whether the client-secret line shows.</summary>
    public bool ShowClientSecret => _clientSecret is not null;

    /// <summary>Whether the run will find the client secret: the file names a variable, and it holds a value.</summary>
    public bool ClientSecretStored => _clientSecretKept && !string.IsNullOrWhiteSpace(_clientSecretEnvVar);

    /// <summary>"Stored on this machine, outside any file" / "Not stored yet", localized — never the value.</summary>
    public string ClientSecretStatus =>
        _strings[ClientSecretStored ? StudioStringKeys.MailPasswordStored : StudioStringKeys.MailPasswordMissing];

    /// <summary>Where the client secret is kept, for the expert: "Kept in the variable X"; null for the novice, and while nothing is kept.</summary>
    public string? ClientSecretVariable =>
        _owner.IsExpert && ClientSecretStored && _clientSecret is not null
            ? _strings.Format(StudioStringKeys.MailSecretVariable, _clientSecret.EnvName)
            : null;

    /// <summary>The name the account's field holds for its variable, as typed.</summary>
    internal IEnumerable<string> SecretVariables => [_clientSecretEnvVar];

    // ── renaming and removing, in place of the action row ──

    /// <summary>Whether the rename editor shows.</summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        private set
        {
            if (SetProperty(ref _isRenaming, value))
                RefreshQuestion();
        }
    }

    /// <summary>The new name, as typed.</summary>
    public string RenameText
    {
        get => _renameText;
        set
        {
            if (SetProperty(ref _renameText, value ?? ""))
                RefreshQuestion();
        }
    }

    /// <summary>Why the typed name cannot be used, localized; null when it can, and while the editor is closed.</summary>
    public string? RenameRefusal => _isRenaming ? _owner.NameRefusal(_renameText.Trim(), this) : null;

    /// <summary>Whether the rename editor has a refusal to show.</summary>
    public bool HasRenameRefusal => RenameRefusal is not null;

    /// <summary>Moves the account under the typed name.</summary>
    public RelayCommand ConfirmRenameCommand { get; }

    /// <summary>Closes the rename editor; nothing moved.</summary>
    public RelayCommand CancelRenameCommand { get; }

    /// <summary>Whether the removal confirmation shows.</summary>
    public bool IsConfirmingRemove
    {
        get => _isConfirmingRemove;
        private set
        {
            if (SetProperty(ref _isConfirmingRemove, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    /// <summary>Removes the account.</summary>
    public RelayCommand ConfirmRemoveCommand { get; }

    /// <summary>Disarms the removal confirmation.</summary>
    public RelayCommand CancelRemoveCommand { get; }

    /// <summary>Whether the row shows its action row: no question is open.</summary>
    public bool IsIdle => !_isRenaming && !_isConfirmingRemove && !_isConfirmingSignOut;

    internal void BeginRename()
    {
        _renameText = _name;
        OnPropertyChanged(nameof(RenameText));
        IsRenaming = true;
    }

    internal void BeginRemove() => IsConfirmingRemove = true;

    internal void BeginSignOut()
    {
        SignOutFailure = null;
        IsConfirmingSignOut = true;
        ConfirmSignOutCommand.RaiseCanExecuteChanged();
    }

    internal void Disarm()
    {
        IsRenaming = false;
        IsConfirmingRemove = false;
        IsConfirmingSignOut = false;
    }

    /// <summary>The panel is done with — cancelled, closed, or stopped with the tab: it leaves the row.</summary>
    internal void SignInClosed(EmailSignInViewModel signIn)
    {
        if (ReferenceEquals(_signIn, signIn))
            SignIn = null;
    }

    /// <summary>The account moved under <paramref name="name"/> in the document: the row follows.</summary>
    internal void Renamed(string name)
    {
        ForgetTest();
        _name = name;
        _loaded = _loaded with { Name = name };
        OnPropertiesChanged(nameof(Name), nameof(SignOutConfirmText));
    }

    internal void RefreshMode()
    {
        OnPropertiesChanged(
            nameof(ShowExpertFields), nameof(ShowRecipients), nameof(ShowClientSecretEnvVar), nameof(ShowTenant),
            nameof(ClientSecretVariable));
    }

    /// <summary>
    /// Puts the secret line on the variable it stores under: the one the file names, else the one
    /// derived from the account's name, clear of what the accounts of the file already name. A
    /// line whose variable did not move is kept, with what is typed in it — and with what the
    /// store said of its variable: the store is asked when a line is built, and again only when a
    /// value is kept (<see cref="SecretKeptUnder"/>), never at an edit.
    /// </summary>
    internal void RefreshSecrets()
    {
        var taken = _owner.SecretVariables.ToList();
        var variable = IsGoogleDrive ? NamedOrDerived(_clientSecretEnvVar, DriveSecretNames.ClientSecretFor(_name, taken)) : null;
        if (!string.Equals(_clientSecret?.EnvName, variable, StringComparison.Ordinal))
        {
            var typed = _clientSecret?.KeyInput ?? "";
            if (_clientSecret is not null)
                _clientSecret.Stored -= OnSecretStored;

            _clientSecret = variable is null
                ? null
                : new SecretRowViewModel(variable, _strings[StudioStringKeys.MailClientSecret], _owner.KeyStore, _strings) { KeyInput = typed };
            if (_clientSecret is not null)
                _clientSecret.Stored += OnSecretStored;

            _clientSecretKept = _clientSecret is { HasKey: true };
            OnPropertiesChanged(nameof(ClientSecret), nameof(ShowClientSecret));
        }

        RefreshSecretStates();
    }

    /// <summary>
    /// A value was kept under <paramref name="variable"/>, by this account or by another that
    /// names the same one: the line that stores under it asks the store again, and says it.
    /// </summary>
    internal void SecretKeptUnder(string variable)
    {
        if (!string.Equals(_clientSecret?.EnvName, variable, StringComparison.Ordinal))
            return;

        _clientSecretKept = _clientSecret!.HasKey;
        RefreshSecretStates();
    }

    /// <summary>
    /// What the last reading of the saved file says of the account: the engine's answer for its
    /// name, or why no answer came back; neither when the file does not hold the account.
    /// </summary>
    internal void SetState(DriveAccountState? state, EmailCliFailure? failure)
    {
        _state = state;
        _stateFailure = failure;
        RefreshState();
    }

    /// <summary>
    /// The account moved — edited, renamed, removed: the test in flight is stopped, and neither
    /// its verdict nor the one that shows says anything of the account as it now stands.
    /// </summary>
    internal void ForgetTest()
    {
        var test = _test;
        _test = null;
        try
        {
            test?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The test ended between the edit and the cancel; nothing left to stop.
        }

        LastCheck = null;
    }

    /// <summary>The document was edited or saved, or a sign-in moved: the test, the sign-in and the sign-out ask again whether they may run.</summary>
    internal void RefreshTest()
    {
        TestCommand.RaiseCanExecuteChanged();
        SignInCommand.RaiseCanExecuteChanged();
        SignOutCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Reads again what the run will say of the account, as the document now holds it.</summary>
    internal void RefreshProblems()
    {
        _problems = [.. DriveAccountRules.Check(_owner.CurrentDocument, _name).Select(message => new ValidationMessageViewModel(message, _strings))];
        OnPropertiesChanged(nameof(Problems), nameof(HasProblems));
    }

    /// <summary>A language change: the findings, the labels and the refusal are said again.</summary>
    internal void RefreshTexts()
    {
        RefreshProblems();
        _clientSecret?.RefreshTexts();
        RefreshSecretStates();
        RefreshState();
        _signIn?.RefreshTexts();
        OnPropertiesChanged(
            nameof(RenameRefusal), nameof(HasRenameRefusal),
            nameof(LastCheckHeadline), nameof(LastCheckDetail), nameof(LastCheckHint), nameof(SignOutConfirmText));
    }

    /// <summary>A recipient line was typed in: the list is written again.</summary>
    internal void RecipientEdited() => Commit();

    /// <summary>
    /// The account as the fields hold it. A field left blank is null — an absent key —, a text
    /// the user did not change is the one the file held, and a number field that holds no number
    /// keeps its last one.
    /// </summary>
    internal DriveAccountDefinition ToDefinition() => new()
    {
        Name = _name,
        Provider = _provider,
        Rights = _rights,
        RightsRaw = _rightsRaw,
        Root = Text(_root, _loaded.Root),
        ClientId = Text(_clientId, _loaded.ClientId),
        ClientSecretEnvVar = Text(_clientSecretEnvVar, _loaded.ClientSecretEnvVar),
        Tenant = Text(_tenant, _loaded.Tenant),
        AllowedRecipients = [.. Recipients.Select(line => line.Pattern.Trim()).Where(pattern => pattern.Length > 0)],
        AllowPublicLinks = _allowPublicLinks,
        MaxUploadMegabytes = _maxUploadMegabytesNumber,
        TimeoutSeconds = _timeoutSecondsNumber,
    };

    private bool IsGoogleDrive => Provider == DriveSection.Values.GoogleDrive;

    private bool Has(DriveRights right) => (_rights & right) != DriveRights.None;

    private void SetRight(DriveRights right, bool granted, [CallerMemberName] string? propertyName = null)
    {
        if (Has(right) == granted)
            return;

        _rights = granted ? _rights | right : _rights & ~right;
        // A box was touched: what the file said and the engine could not read is replaced.
        _rightsRaw = null;
        OnPropertyChanged(propertyName);
        Commit();
    }

    private void SetText(ref string field, string? value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value ?? "", propertyName))
            Commit();
    }

    private void SetNumber(ref string text, ref int? number, string? value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref text, value ?? "", propertyName))
            return;

        OnPropertyChanged(nameof(HasUnreadNumber));
        var typed = text.Trim();
        if (typed.Length == 0)
            number = null;
        else if (int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            number = parsed;
        else
            return;

        Commit();
    }

    private void RemoveRecipient(DriveRecipientRowViewModel line)
    {
        if (Recipients.Remove(line))
            Commit();
    }

    private void ConfirmRename()
    {
        if (_isRenaming && _owner.Rename(this, _renameText.Trim()))
            IsRenaming = false;
    }

    private void RefreshQuestion()
    {
        OnPropertiesChanged(nameof(RenameRefusal), nameof(HasRenameRefusal), nameof(IsIdle));
        ConfirmRenameCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Writes the account, then publishes what the edit moved: what each mode sees.</summary>
    private void Commit()
    {
        ForgetTest();
        var definition = ToDefinition();
        _owner.Write(this);
        _loaded = definition;
        OnPropertiesChanged(
            nameof(RootLine), nameof(ShowRecipients), nameof(ShowShareClosed), nameof(ShowSignIn),
            nameof(ShowClientSecretEnvVar), nameof(ShowTenant));
        RefreshTest();
    }

    /// <summary>
    /// A value was kept. The first one names its variable in the file — the derived name, written
    /// like a name the expert typed; a name the file already holds is never written again.
    /// </summary>
    private void OnSecretStored(object? sender, EventArgs e)
    {
        if (sender is not SecretRowViewModel line)
            return;

        _owner.SecretStoredUnder(line.EnvName);
        if (ReferenceEquals(line, _clientSecret) && string.IsNullOrWhiteSpace(_clientSecretEnvVar))
            ClientSecretEnvVar = line.EnvName;

        // A value under a variable the file already names saves nothing: the engine is asked again,
        // once — with the save of a name just written, when there is one.
        _owner.SecretKept();
    }

    /// <summary>One connection test: "connecting" while the CLI runs, then its verdict — none when the test was stopped.</summary>
    private async Task TestAsync()
    {
        using var test = new CancellationTokenSource();
        _test = test;
        LastCheck = null;
        IsTesting = true;
        try
        {
            var outcome = await _owner.CheckAsync(_name, test.Token);
            // The account moved while the child answered: its verdict is of the account as it was.
            if (ReferenceEquals(_test, test))
                LastCheck = outcome;
        }
        finally
        {
            if (ReferenceEquals(_test, test))
                _test = null;

            IsTesting = false;
        }
    }

    /// <summary>Whether the account may be signed in or out now: a drive the engine knows, a CLI to ask, and nothing unsaved.</summary>
    private bool CanSign => ShowSignIn && _owner.CanTest;

    private void BeginSignIn()
    {
        if (!SignInCommand.CanExecute(null))
            return;

        // A panel that ended and was left open makes way for the new one.
        _signIn?.Abandon();
        SignOutFailure = null;
        SignIn = _owner.BeginSignIn(this);
    }

    /// <summary>
    /// The confirmed sign-out: the question closes, a sign-in of the account still waiting is
    /// stopped — its tokens would land after the ones being forgotten —, and the verb runs.
    /// </summary>
    private async Task SignOutAsync()
    {
        IsConfirmingSignOut = false;
        ConfirmSignOutCommand.RaiseCanExecuteChanged();
        _signIn?.Abandon();
        SignOutFailure = await _owner.SignOutAsync(_name) is { } failure ? _owner.FailureText(failure) : null;
    }

    private void RefreshState() =>
        OnPropertiesChanged(
            nameof(StateKind), nameof(State), nameof(StateDetail), nameof(HasState), nameof(HasStateDetail), nameof(IsReady));

    private void RefreshSecretStates() =>
        OnPropertiesChanged(nameof(ClientSecretStored), nameof(ClientSecretStatus), nameof(ClientSecretVariable));

    /// <summary>The engine's spelling of <paramref name="written"/> when it is one of <paramref name="names"/> but for the case, else the text as written.</summary>
    private static string? Shown(IReadOnlyList<string> names, string? written) =>
        written is null
            ? null
            : names.FirstOrDefault(name => string.Equals(name, written, StringComparison.OrdinalIgnoreCase)) ?? written;

    /// <summary>The text to write: the file's own when the field still shows it, else what was typed, trimmed, and null when blank.</summary>
    private static string? Text(string typed, string? loaded)
    {
        if (string.Equals(typed, loaded ?? "", StringComparison.Ordinal))
            return loaded;
        return string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();
    }

    /// <summary>The variable name as written when one was, else the derived one.</summary>
    private static string NamedOrDerived(string written, string derived) =>
        written.Trim() is { Length: > 0 } named ? named : derived;

    private static string NumberText(int? number) => number?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static bool Unread(string text) =>
        text.Trim() is { Length: > 0 } typed && !int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
}

/// <summary>
/// One line of <c>Share:AllowedRecipients</c>: an address, <c>*@domain</c> or <c>*</c>. A line
/// that is none of them says so on itself, and is written all the same — the run then says it too.
/// </summary>
public sealed class DriveRecipientRowViewModel : ObservableObject
{
    private readonly DriveAccountRowViewModel _owner;
    private string _pattern;

    internal DriveRecipientRowViewModel(DriveAccountRowViewModel owner, string pattern)
    {
        _owner = owner;
        _pattern = pattern;
    }

    /// <summary>The pattern, as typed.</summary>
    public string Pattern
    {
        get => _pattern;
        set
        {
            if (!SetProperty(ref _pattern, value ?? ""))
                return;

            OnPropertyChanged(nameof(IsInvalid));
            _owner.RecipientEdited();
        }
    }

    /// <summary>Whether the line holds something the engine reads as no recipient pattern.</summary>
    public bool IsInvalid => _pattern.Trim() is { Length: > 0 } pattern && !DriveAccountRules.IsRecipientPattern(pattern);
}
