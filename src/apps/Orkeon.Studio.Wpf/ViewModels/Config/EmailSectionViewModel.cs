using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Email;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// What the E-mail section needs to talk to the CLI (STUDIO-69). <see cref="Cli"/> says whether
/// each account is ready and tests its connection; <see cref="IsDirty"/> says whether the document
/// holds edits the file does not, and <see cref="Dispatcher"/> is where an answer of the CLI lands.
/// <see cref="StatesDelay"/> is the pause <see cref="EmailSectionViewModel.RefreshStatesSoon"/>
/// waits — none when null, and it must be the section's own: a superseded pause is dropped with
/// <c>CancelPending</c>. <see cref="SignIn"/> is what a sign-in panel needs beside the CLI.
/// </summary>
public sealed record EmailCliSeams
{
    /// <summary>The CLI the states are read and the connections tested on; none reads nothing.</summary>
    public EmailCliClient? Cli { get; init; }

    /// <summary>Whether the document holds edits the file does not; never, when null.</summary>
    public Func<bool>? IsDirty { get; init; }

    /// <summary>Where an answer of the CLI lands; immediate when null.</summary>
    public IUiDispatcher? Dispatcher { get; init; }

    /// <summary>The seams of the sign-in panels.</summary>
    public EmailSignInServices? SignIn { get; init; }

    /// <summary>The pause the saved file's states wait to be read again; none when null.</summary>
    public IUiDelay? StatesDelay { get; init; }
}

/// <summary>
/// The <c>Orkeon:Tools:Email</c> form of the E-mail tab (STUDIO-67, both modes): one row per
/// account, the account a call uses when it names none and, for the expert, the two settings of
/// the section. Every edit writes in place through <see cref="EmailSection"/>, so a key Studio
/// does not model survives, and nothing reaches the file as an empty string, an empty object or
/// an empty list — the section itself goes when its last key does.
/// <para>
/// An account is added through a small box that asks for its name, its provider and its
/// address, and writes only once validated: in novice mode every edit saves the file, and an
/// account without a name or an address must not reach it between two keystrokes. A rename and
/// a removal ask first, on the row, and say what they leave on the machine.
/// </para>
/// <para>
/// Whether an account is ready is not Studio's to compute (STUDIO-69): it depends on the
/// environment and the token store of the <c>orkeon</c> process. <see cref="RefreshStatesAsync"/>
/// asks <c>orkeon email accounts</c> about the file as saved, at once — on arrival on the tab —,
/// and <see cref="RefreshStatesSoon"/> asks once what moved has settled: the novice's file is
/// saved at every keystroke, and a secret kept is a value, a name and a save, so the engine is
/// asked when the saves pause, never once per key. Each row's test asks
/// <c>orkeon email check</c>, on a click only. <see cref="StopActivity"/> stops whatever of the
/// two still runs, and drops the reading that waited.
/// </para>
/// <para>
/// An OAuth account is signed in and out from its row (STUDIO-70): the section starts
/// <c>orkeon email login</c>, keeps the panels of the sign-ins still open — one per account —,
/// beats their countdown, and stops them with the rest of its activity.
/// </para>
/// </summary>
public sealed class EmailSectionViewModel : DocumentSectionViewModel
{
    /// <summary>The provider a new account's box opens on: the preset a first account most often uses.</summary>
    private const string NewAccountProviderDefault = EmailSection.Values.Gmail;

    /// <summary>How long the saves must pause before the states of the saved file are read again.</summary>
    internal static readonly TimeSpan StatesPause = TimeSpan.FromMilliseconds(600);

    private readonly IUiDelay _statesDelay;
    private readonly IStudioStrings _strings;
    private readonly Func<string?> _settingsPath;
    private readonly IPathPicker _picker;
    private readonly IApiKeyStore _keyStore;
    private readonly EmailCliClient? _cli;
    private readonly Func<bool> _isDirty;
    private readonly IUiDispatcher _dispatcher;
    private readonly EmailSignInServices _signInServices;
    private readonly Lock _activity = new();
    private readonly List<Running> _calls = [];
    private readonly List<EmailSignInViewModel> _signIns = [];
    private bool _beating;
    private EmailAccountsResult? _states;
    private int _reading;
    private EmailAccountRowViewModel? _selectedAccount;
    private bool _isExpert;
    private bool _isAdding;
    private bool _isSectionOpen;
    private bool _syncingChoices;
    private string _newAccountName = "";
    private string _newAccountProvider = NewAccountProviderDefault;
    private string _newAccountAddress = "";

    /// <summary>
    /// Binds the form to the <c>Orkeon:Tools:Email</c> section of the document.
    /// <paramref name="settingsPath"/> gives the file the document is saved to, for the line that
    /// says where the accounts go; <paramref name="picker"/> browses for the token folder;
    /// <paramref name="keyStore"/> keeps the passwords and the client secrets typed in the form,
    /// the store of the model keys — the real environment when null. <paramref name="engine"/> is
    /// what the section needs to talk to the CLI (STUDIO-69): a section built without one reads
    /// no state and tests nothing.
    /// </summary>
    public EmailSectionViewModel(
        Func<AppSettingsDocument> document,
        Action onChanged,
        IStudioStrings? strings = null,
        Func<string?>? settingsPath = null,
        IPathPicker? picker = null,
        IApiKeyStore? keyStore = null,
        EmailCliSeams? engine = null)
        : base(document, onChanged)
    {
        _statesDelay = engine?.StatesDelay ?? ImmediateUiDelay.Instance;
        _signInServices = engine?.SignIn ?? new EmailSignInServices();
        _cli = engine?.Cli;
        _isDirty = engine?.IsDirty ?? (() => false);
        _dispatcher = engine?.Dispatcher ?? ImmediateUiDispatcher.Instance;
        _strings = strings ?? EnglishStudioStrings.Instance;
        _settingsPath = settingsPath ?? (() => null);
        _picker = picker ?? NullPathPicker.Instance;
        _keyStore = keyStore ?? new EnvironmentApiKeyStore();

        ProviderChoices = [.. EmailSection.Providers.Select(provider => new EmailChoiceViewModel(provider, () => ProviderLabel(provider)))];
        AuthMethodChoices = [Preset(), .. EmailSection.AuthMethods.Select(method => new EmailChoiceViewModel(method, () => AuthMethodLabel(method)))];
        IncomingProtocolChoices = WithPreset(EmailSection.IncomingProtocols);
        OutgoingProtocolChoices = WithPreset(EmailSection.OutgoingProtocols);
        SecurityChoices = WithPreset(EmailSection.Securities);

        BeginAddCommand = new RelayCommand(BeginAdd);
        ConfirmAddCommand = new RelayCommand(ConfirmAdd, () => CanConfirmAdd);
        CancelAddCommand = new RelayCommand(() => IsAdding = false);
        RenameAccountCommand = new RelayCommand(
            parameter => { if (parameter is EmailAccountRowViewModel row) Arm(row, r => r.BeginRename()); },
            parameter => parameter is EmailAccountRowViewModel);
        RemoveAccountCommand = new RelayCommand(
            parameter => { if (parameter is EmailAccountRowViewModel row) Arm(row, r => r.BeginRemove()); },
            parameter => parameter is EmailAccountRowViewModel);
        BrowseCredentialsDirectoryCommand = new RelayCommand(BrowseCredentialsDirectory);
        ToggleSectionCommand = new RelayCommand(() => IsSectionOpen = !IsSectionOpen);

        // The rows do not listen: the owner of the list refreshes what they say (STUDIO-56).
        _strings.CultureChanged += (_, _) => RefreshTexts();
        Reload();
    }

    private EmailSection Section => Document.Email;

    /// <summary>The document the rows read their findings from.</summary>
    internal AppSettingsDocument CurrentDocument => Document;

    /// <summary>Where the rows keep a password or a client secret: never the document.</summary>
    internal IApiKeyStore KeyStore => _keyStore;

    /// <summary>
    /// Every variable the accounts name for a secret, as their fields hold them: what a name
    /// derived for another account must stay clear of.
    /// </summary>
    internal IEnumerable<string> SecretVariables => Accounts.SelectMany(row => row.SecretVariables);

    /// <inheritdoc />
    public override bool Exists => Document.GetNode(EmailSection.SectionPath) is not null;

    /// <summary>The account rows, in document order.</summary>
    public ObservableCollection<EmailAccountRowViewModel> Accounts { get; } = [];

    /// <summary>Whether at least one account is declared.</summary>
    public bool HasAccounts => Accounts.Count > 0;

    /// <summary>The account whose form shows; null when none is declared.</summary>
    public EmailAccountRowViewModel? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                OnPropertyChanged(nameof(HasSelectedAccount));
                RefreshTabGates();
            }
        }
    }

    /// <summary>Whether an account's form shows.</summary>
    public bool HasSelectedAccount => _selectedAccount is not null;

    // The tab of the selected account's form, as four gates of the section: the capture catalogue
    // reads its gates from the shell down, and a path through SelectedAccount resolves to nothing
    // while no account is selected — these do, and say false.

    /// <summary>Whether the selected account's form is on its account tab.</summary>
    public bool ShowsAccountTab => _selectedAccount?.IsAccountTab == true;

    /// <summary>Whether the selected account's form is on its rights tab.</summary>
    public bool ShowsRightsTab => _selectedAccount?.IsRightsTab == true;

    /// <summary>Whether the selected account's form is on its sign-in tab.</summary>
    public bool ShowsAuthTab => _selectedAccount?.IsAuthTab == true;

    /// <summary>Whether the selected account's form is on its servers tab.</summary>
    public bool ShowsServersTab => _selectedAccount?.IsServersTab == true;

    /// <summary>The selected account's form moved to another tab, or the selection moved.</summary>
    internal void TabMoved(EmailAccountRowViewModel row)
    {
        if (ReferenceEquals(row, _selectedAccount))
            RefreshTabGates();
    }

    private void RefreshTabGates() =>
        OnPropertiesChanged(nameof(ShowsAccountTab), nameof(ShowsRightsTab), nameof(ShowsAuthTab), nameof(ShowsServersTab));

    /// <summary>
    /// Whether the expert's fields show: the protocols, ports and securities, the user name, the
    /// names of the secret variables, the tenant, the quotas, and the settings of the section.
    /// The settings screen sets it from the window's mode switch.
    /// </summary>
    public bool IsExpert
    {
        get => _isExpert;
        set
        {
            if (!SetProperty(ref _isExpert, value))
                return;

            foreach (var row in Accounts)
                row.RefreshMode();
        }
    }

    /// <summary>
    /// The line at the head of the tab (D-06): the file the accounts are written to, which is the
    /// only file that carries them — a run reads one settings file, and a team launched on its
    /// own does not see these accounts.
    /// </summary>
    public string SettingsFileLine => _strings.Format(
        StudioStringKeys.MailSettingsFile,
        _settingsPath() is { Length: > 0 } path ? path : AppSettingsDocument.FileName);

    /// <summary>The providers, each with what choosing it means.</summary>
    public IReadOnlyList<EmailChoiceViewModel> ProviderChoices { get; }

    /// <summary>The sign-in methods, after the entry that leaves the choice to the preset.</summary>
    public IReadOnlyList<EmailChoiceViewModel> AuthMethodChoices { get; }

    /// <summary>The reading protocols, in the engine's spelling, after the preset entry.</summary>
    public IReadOnlyList<EmailChoiceViewModel> IncomingProtocolChoices { get; }

    /// <summary>The sending protocols, in the engine's spelling, after the preset entry.</summary>
    public IReadOnlyList<EmailChoiceViewModel> OutgoingProtocolChoices { get; }

    /// <summary>The transport securities, in the engine's spelling, after the preset entry.</summary>
    public IReadOnlyList<EmailChoiceViewModel> SecurityChoices { get; }

    /// <summary>
    /// What <see cref="DefaultAccount"/> may name: the entry that names none, then the declared
    /// accounts — and the name the file holds when it is none of them, so that it shows.
    /// </summary>
    public ObservableCollection<EmailChoiceViewModel> DefaultAccountChoices { get; } = [];

    /// <summary>
    /// The account a call uses when it names none, in the spelling of the account it designates;
    /// null when the file names none. Choosing the first entry removes the key.
    /// </summary>
    public string? DefaultAccount
    {
        get
        {
            if (Section.DefaultAccount is not { Length: > 0 } named)
                return null;

            return Section.AccountNames.FirstOrDefault(name => string.Equals(name, named, StringComparison.OrdinalIgnoreCase)) ?? named;
        }
        set
        {
            // A list that loses its selected entry while it is rebuilt pushes a null back: not an edit.
            if (_syncingChoices)
                return;

            SetValue(DefaultAccount, string.IsNullOrWhiteSpace(value) ? null : value, v =>
            {
                Section.DefaultAccount = v;
                Prune();
            });
        }
    }

    /// <summary>The watermark of <see cref="DefaultAccount"/>: with a single account the engine uses that one.</summary>
    public string? DefaultAccountPlaceholder => Section.AccountNames is [var only] ? only : null;

    /// <summary>
    /// Whether a message the prompt-injection detector rejects has its body withheld (expert).
    /// Off is the engine's default, so off removes the key and only on is written.
    /// </summary>
    public bool WithholdRejected
    {
        get => Section.WithholdRejected ?? false;
        set => SetValue(WithholdRejected, value, v =>
        {
            Section.WithholdRejected = v ? true : null;
            Prune();
            RefreshProblems();
        });
    }

    /// <summary>
    /// The physical folder that holds the sign-in tokens (expert); empty leaves the engine's own,
    /// <c>credentials</c> beside the user's settings file. A relative path is resolved from the
    /// file that names it.
    /// </summary>
    public string CredentialsDirectory
    {
        get => Section.CredentialsDirectory ?? "";
        set => SetValue(CredentialsDirectory, value ?? "", v =>
        {
            Section.CredentialsDirectory = v;
            Prune();
        });
    }

    /// <summary>Opens the folder dialog for <see cref="CredentialsDirectory"/>.</summary>
    public RelayCommand BrowseCredentialsDirectoryCommand { get; }

    /// <summary>
    /// Whether the card of the two settings of the section is unfolded (expert). It sits under
    /// the list of accounts, since it is about none of them, and folded: the form of the
    /// selected account is what the tab is opened for.
    /// </summary>
    public bool IsSectionOpen
    {
        get => _isSectionOpen;
        set => SetProperty(ref _isSectionOpen, value);
    }

    /// <summary>Unfolds or folds the card of the section's settings.</summary>
    public RelayCommand ToggleSectionCommand { get; }

    // ── the box that adds an account ──

    /// <summary>Opens the box that asks for a new account's name, provider and address.</summary>
    public RelayCommand BeginAddCommand { get; }

    /// <summary>Writes the account the box describes, and shows its form.</summary>
    public RelayCommand ConfirmAddCommand { get; }

    /// <summary>Closes the box; nothing was written.</summary>
    public RelayCommand CancelAddCommand { get; }

    /// <summary>Whether the box is open.</summary>
    public bool IsAdding
    {
        get => _isAdding;
        private set => SetProperty(ref _isAdding, value);
    }

    /// <summary>The name of the account to add: what an agent passes as <c>account</c>.</summary>
    public string NewAccountName
    {
        get => _newAccountName;
        set
        {
            if (SetProperty(ref _newAccountName, value ?? ""))
                RefreshAddBox();
        }
    }

    /// <summary>The provider of the account to add, one of <see cref="ProviderChoices"/>.</summary>
    public string NewAccountProvider
    {
        get => _newAccountProvider;
        set
        {
            // A list never clears the provider: an account is added under one of the three.
            if (!string.IsNullOrWhiteSpace(value))
                SetProperty(ref _newAccountProvider, value);
        }
    }

    /// <summary>The address of the account to add.</summary>
    public string NewAccountAddress
    {
        get => _newAccountAddress;
        set
        {
            if (SetProperty(ref _newAccountAddress, value ?? ""))
                RefreshAddBox();
        }
    }

    /// <summary>Why the name typed in the box cannot be used, localized; null while it is empty or usable.</summary>
    public string? AddRefusal => _newAccountName.Trim() is { Length: > 0 } name ? NameRefusal(name, renamed: null) : null;

    /// <summary>Whether the box has a refusal to show.</summary>
    public bool HasAddRefusal => AddRefusal is not null;

    private bool CanConfirmAdd =>
        _isAdding
        && NameRefusal(_newAccountName.Trim(), renamed: null) is null
        && !string.IsNullOrWhiteSpace(_newAccountAddress);

    // ── renaming and removing ──

    /// <summary>Opens the rename editor of the row given as parameter; renames nothing on its own.</summary>
    public RelayCommand RenameAccountCommand { get; }

    /// <summary>Arms the removal confirmation of the row given as parameter; removes nothing on its own.</summary>
    public RelayCommand RemoveAccountCommand { get; }

    // ── the state of each account, and its connection test (STUDIO-69) ──

    /// <summary>
    /// Whether the document holds edits the file does not: the states shown are then those of the
    /// file as saved, and the connection test — which reads that file too — waits for the save.
    /// </summary>
    public bool IsStateOfSavedFile => _isDirty();

    /// <summary>
    /// Asks <c>orkeon email accounts</c> what the engine makes of each account of the saved file,
    /// and says it on the rows. No network is involved, and no connection is ever tested here. A
    /// reading still in flight is stopped, and one that waited for a pause is dropped: only the
    /// newest answer shows.
    /// </summary>
    public async Task RefreshStatesAsync()
    {
        _statesDelay.CancelPending();
        if (_cli is null || _settingsPath() is not { Length: > 0 } path)
            return;

        var reading = Interlocked.Increment(ref _reading);
        Stop(listing: true);

        var result = await RunAsync(listing: true, token => _cli.ListAsync(path, token));
        if (reading != Volatile.Read(ref _reading) || result.Failure?.Kind == EmailCliFailureKind.Cancelled)
            return;

        _dispatcher.Post(() =>
        {
            _states = result;
            ApplyStates();
        });
    }

    /// <summary>
    /// The saved file moved, or a secret was kept: the states are read again once
    /// <see cref="StatesPause"/> passed without another call — a burst of saves is one question
    /// to the engine, asked when it ends.
    /// </summary>
    public void RefreshStatesSoon()
    {
        if (_cli is null)
            return;

        _statesDelay.CancelPending();
        _statesDelay.After(StatesPause, () => _ = RefreshStatesAsync());
    }

    /// <summary>
    /// Stops what the tab still has running — a reading of the states, a connection test, a
    /// sign-out, a sign-in waiting on the person: called when the tab is left and when the window
    /// closes, so that no <c>orkeon</c> process outlives the screen that asked for it — and none
    /// starts for it later: the reading that waited for a pause is dropped. Every
    /// token has fired when this returns; a sign-in, which would wait for ever, has its standard
    /// input closed by then too (STUDIO-70).
    /// </summary>
    public void StopActivity()
    {
        _statesDelay.CancelPending();
        Interlocked.Increment(ref _reading);
        Stop(listing: null);
        StopSignIns();
    }

    /// <summary>The document was edited or saved: the flag and what it gates are said again.</summary>
    internal void RefreshSavedFile()
    {
        OnPropertyChanged(nameof(IsStateOfSavedFile));
        foreach (var row in Accounts)
            row.RefreshTest();
    }

    /// <summary>Whether a row may start a connection test: a client to ask, a file to name, and nothing unsaved.</summary>
    internal bool CanTest => _cli is not null && _settingsPath() is { Length: > 0 } && !_isDirty();

    /// <summary>
    /// Runs <c>orkeon email check</c> on <paramref name="account"/>, as the saved file declares
    /// it; null when the test was stopped — with the tab's activity, or by <paramref name="stop"/>,
    /// the row's own —, or could not be asked for.
    /// </summary>
    internal async Task<EmailCheckOutcome?> CheckAsync(string account, CancellationToken stop)
    {
        if (_cli is null || _settingsPath() is not { Length: > 0 } path)
            return null;

        var outcome = await RunAsync(listing: false, token => _cli.CheckAsync(account, path, token), stop);
        return outcome.Kind == EmailCheckKind.Cancelled ? null : outcome;
    }

    /// <summary>Whether a sign-in panel is open on one of the rows: in flight, or ended and not closed yet.</summary>
    public bool IsSigningIn => _signIns.Count > 0;

    /// <summary>
    /// Starts <c>orkeon email login</c> on <paramref name="row"/>'s account, as the saved file
    /// declares it, and returns the panel that follows it; null when it may not start — no client,
    /// no file, or edits the file does not hold.
    /// </summary>
    internal EmailSignInViewModel? BeginSignIn(EmailAccountRowViewModel row)
    {
        if (_cli is null || _settingsPath() is not { Length: > 0 } path || _isDirty())
            return null;

        var signIn = new EmailSignInViewModel(
            row.Name, this, new EmailSignInEnvironment(_cli, path, _strings, _dispatcher, _signInServices),
            moved: () =>
            {
                row.RefreshTest();
                SyncBeat();
            },
            closed: ended =>
            {
                _signIns.Remove(ended);
                row.SignInClosed(ended);
                SyncBeat();
                OnPropertyChanged(nameof(IsSigningIn));
            });
        _signIns.Add(signIn);
        OnPropertyChanged(nameof(IsSigningIn));
        signIn.Start();
        return signIn;
    }

    /// <summary>
    /// Runs <c>orkeon email logout</c> on <paramref name="account"/>, then reads the states again;
    /// null when the tokens are forgotten — or the call was stopped —, why they are not otherwise.
    /// </summary>
    internal async Task<EmailCliFailure?> SignOutAsync(string account)
    {
        if (_cli is null || _settingsPath() is not { Length: > 0 } path)
            return null;

        var failure = await RunAsync(listing: false, token => _cli.LogoutAsync(account, path, token));
        if (failure is null)
            await RefreshStatesAsync();

        return failure?.Kind == EmailCliFailureKind.Cancelled ? null : failure;
    }

    /// <summary>Arms the sign-out question of <paramref name="row"/>, and disarms every other question of the tab.</summary>
    internal void ArmSignOut(EmailAccountRowViewModel row) => Arm(row, r => r.BeginSignOut());

    /// <summary>Stops every sign-in still open and takes its panel off its row.</summary>
    private void StopSignIns()
    {
        foreach (var signIn in _signIns.ToArray())
            signIn.Abandon();
    }

    /// <summary>Keeps the one-second beat while a device code counts down, and only then.</summary>
    private void SyncBeat()
    {
        var needed = _signIns.Exists(signIn => signIn.CountsDown);
        if (needed == _beating)
            return;

        _beating = needed;
        var ticker = _signInServices.Ticker ?? NullUiTicker.Instance;
        if (needed)
            ticker.StartBeat(TimeSpan.FromSeconds(1), TickSignIns);
        else
            ticker.StopBeat();
    }

    private void TickSignIns()
    {
        foreach (var signIn in _signIns.ToArray())
            signIn.Tick();
    }

    /// <summary>A value was kept under <paramref name="variable"/>: every line that stores under it says so.</summary>
    internal void SecretStoredUnder(string variable)
    {
        foreach (var row in Accounts)
            row.SecretKeptUnder(variable);
    }

    /// <summary>A secret was kept: the saved file did not move, yet what the engine finds for the account did.</summary>
    internal void SecretKept() => RefreshStatesSoon();

    /// <summary>
    /// Why nothing could be read, for the row: the sentence of the home screen when the CLI is
    /// not on this machine — localized —, else what the CLI said, as printed.
    /// </summary>
    internal string FailureText(EmailCliFailure failure) => failure.Kind == EmailCliFailureKind.EngineMissing
        ? EngineMissingText
        : failure.Reason;

    /// <summary>The sentence the create-a-team screen — the one Studio opens on — says of a missing CLI.</summary>
    internal string EngineMissingText => _strings[StudioStringKeys.WizardFailureEngineMissing];

    /// <summary>
    /// Runs one CLI call under a token <see cref="Stop"/> can fire — and <paramref name="stop"/>
    /// with it, when the caller has a reason of its own to stop —, and forgets the token when the
    /// call ends.
    /// </summary>
    private async Task<T> RunAsync<T>(bool listing, Func<CancellationToken, Task<T>> call, CancellationToken stop = default)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var entry = new Running(cancellation, listing);
        lock (_activity)
            _calls.Add(entry);

        try
        {
            return await call(cancellation.Token);
        }
        finally
        {
            lock (_activity)
                _calls.Remove(entry);
        }
    }

    /// <summary>Stops the readings (<see langword="true"/>), or everything that runs (<see langword="null"/>).</summary>
    private void Stop(bool? listing)
    {
        Running[] stopped;
        lock (_activity)
            stopped = [.. _calls.Where(call => listing is null || call.Listing == listing)];

        foreach (var call in stopped)
        {
            try
            {
                call.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The call ended between the snapshot and the cancel; nothing left to stop.
            }
        }
    }

    /// <summary>Pairs each row with the engine's answer for its name — compared without case, as the engine does.</summary>
    private void ApplyStates()
    {
        foreach (var row in Accounts)
            row.SetState(_states?.Find(row.Name), _states?.Failure);
    }

    /// <summary>One CLI call in flight: its token, and whether it is a reading of the states.</summary>
    private sealed record Running(CancellationTokenSource Cancellation, bool Listing);

    /// <inheritdoc />
    public override void Refresh()
    {
        Reload();
        base.Refresh();
    }

    /// <inheritdoc />
    protected override void OnSectionChanged() =>
        OnPropertiesChanged(nameof(Exists), nameof(HasAccounts), nameof(DefaultAccount), nameof(DefaultAccountPlaceholder));

    /// <summary>Re-reads the line that names the file, after the save location moved.</summary>
    internal void RefreshSettingsFile() => OnPropertyChanged(nameof(SettingsFileLine));

    /// <summary>
    /// Why <paramref name="name"/> cannot name an account, localized, or null when it can: a name
    /// the engine refuses, or one another account answers to — names are compared without regard
    /// to case, as the engine does. <paramref name="renamed"/> is the row being renamed, whose own
    /// name is free to it.
    /// </summary>
    internal string? NameRefusal(string name, EmailAccountRowViewModel? renamed)
    {
        if (!EmailAccountRules.IsValidName(name))
            return _strings[StudioStringKeys.MailNameInvalid];

        var taken = Section.AccountNames.Any(other =>
            !string.Equals(other, renamed?.Name, StringComparison.Ordinal)
            && string.Equals(other, name, StringComparison.OrdinalIgnoreCase));

        return taken ? _strings[StudioStringKeys.MailNameTaken] : null;
    }

    /// <summary>Writes the row's fields in place, says again what the run will make of every account, and marks the document.</summary>
    internal void Write(EmailAccountRowViewModel row)
    {
        Section.SetAccount(row.ToDefinition());
        RefreshProblems();
        RefreshSecrets();
        NotifyDocumentChanged();
    }

    /// <summary>Moves the row's account under <paramref name="newName"/>; false when the name is refused.</summary>
    internal bool Rename(EmailAccountRowViewModel row, string newName)
    {
        if (NameRefusal(newName, row) is not null)
            return false;

        if (string.Equals(row.Name, newName, StringComparison.Ordinal))
            return true;

        // The sign-in in flight is the old name's: its tokens would be filed where no run looks.
        row.SignIn?.Abandon();
        Section.RenameAccount(row.Name, newName);
        row.Renamed(newName);
        // The saved file still holds the old name: the state it had is not this account's.
        ApplyStates();
        SyncDefaultAccountChoices();
        RefreshProblems();
        RefreshSecrets();
        NotifyDocumentChanged();
        return true;
    }

    /// <summary>Removes the row's account and everything under it, and shows a neighbour's form.</summary>
    internal void Remove(EmailAccountRowViewModel row)
    {
        var index = Accounts.IndexOf(row);
        row.SignIn?.Abandon();
        row.ForgetTest();
        Section.RemoveAccount(row.Name);
        Prune();
        Accounts.Remove(row);
        SelectedAccount = Accounts.Count == 0 ? null : Accounts[Math.Min(Math.Max(index, 0), Accounts.Count - 1)];
        SyncDefaultAccountChoices();
        RefreshProblems();
        RefreshSecrets();
        NotifyDocumentChanged();
    }

    private void Reload()
    {
        var selected = _selectedAccount?.Name;
        // The rows are rebuilt: a panel has no row to show under any more.
        StopSignIns();
        Accounts.Clear();
        foreach (var name in Section.AccountNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            if (Section.GetAccount(name) is { } account)
                Accounts.Add(new EmailAccountRowViewModel(account, this, _strings));
        }

        RefreshSecrets();
        ApplyStates();
        SelectedAccount = Accounts.FirstOrDefault(row => string.Equals(row.Name, selected, StringComparison.Ordinal))
            ?? Accounts.FirstOrDefault();
        IsAdding = false;
        SyncDefaultAccountChoices();
    }

    private void BeginAdd()
    {
        foreach (var row in Accounts)
            row.Disarm();

        _newAccountName = "";
        _newAccountAddress = "";
        _newAccountProvider = NewAccountProviderDefault;
        IsAdding = true;
        OnPropertiesChanged(nameof(NewAccountName), nameof(NewAccountAddress), nameof(NewAccountProvider));
        RefreshAddBox();
    }

    private void ConfirmAdd()
    {
        if (!CanConfirmAdd)
            return;

        var name = _newAccountName.Trim();
        Section.SetAccount(new EmailAccountDefinition
        {
            Name = name,
            Provider = _newAccountProvider,
            Address = _newAccountAddress.Trim(),
        });
        if (Section.GetAccount(name) is not { } written)
            return;

        var row = new EmailAccountRowViewModel(written, this, _strings);
        Accounts.Add(row);
        ApplyStates();
        SelectedAccount = row;
        IsAdding = false;
        SyncDefaultAccountChoices();
        RefreshProblems();
        RefreshSecrets();
        NotifyDocumentChanged();
    }

    private void RefreshAddBox()
    {
        OnPropertiesChanged(nameof(AddRefusal), nameof(HasAddRefusal));
        ConfirmAddCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Arms one row's question and disarms every other, the add box included: one question at a time.</summary>
    private void Arm(EmailAccountRowViewModel row, Action<EmailAccountRowViewModel> question)
    {
        IsAdding = false;
        foreach (var other in Accounts)
            other.Disarm();

        question(row);
    }

    private void BrowseCredentialsDirectory()
    {
        var picked = _picker.PickFolder(
            _strings[StudioStringKeys.MailCredentialsDirectoryPick],
            CredentialsDirectory is { Length: > 0 } current ? current : null);

        if (picked is { Length: > 0 })
            CredentialsDirectory = picked;
    }

    private void RefreshProblems()
    {
        foreach (var row in Accounts)
            row.RefreshProblems();
    }

    /// <summary>The secret lines are built once every row is in the list: a derived name depends on what the others name.</summary>
    private void RefreshSecrets()
    {
        foreach (var row in Accounts)
            row.RefreshSecrets();
    }

    private void RefreshTexts()
    {
        foreach (var choice in ProviderChoices
                     .Concat(AuthMethodChoices)
                     .Concat(IncomingProtocolChoices)
                     .Concat(OutgoingProtocolChoices)
                     .Concat(SecurityChoices)
                     .Concat(DefaultAccountChoices))
        {
            choice.RefreshLabel();
        }

        foreach (var row in Accounts)
            row.RefreshTexts();

        OnPropertiesChanged(nameof(SettingsFileLine), nameof(AddRefusal));
    }

    /// <summary>
    /// Rebuilds the entries of the default-account list in place. A list that loses its selected
    /// entry writes a null back to what it is bound to: that write is not the user's, so it is
    /// ignored while the entries move, and the selection is published again afterwards.
    /// </summary>
    private void SyncDefaultAccountChoices()
    {
        _syncingChoices = true;
        try
        {
            DefaultAccountChoices.Clear();
            DefaultAccountChoices.Add(Preset());
            foreach (var name in Section.AccountNames)
                DefaultAccountChoices.Add(new EmailChoiceViewModel(name, () => name));

            if (DefaultAccount is { } named && !DefaultAccountChoices.Any(choice => string.Equals(choice.Value, named, StringComparison.Ordinal)))
                DefaultAccountChoices.Add(new EmailChoiceViewModel(named, () => named));
        }
        finally
        {
            _syncingChoices = false;
        }

        OnPropertiesChanged(nameof(DefaultAccount), nameof(DefaultAccountPlaceholder));
    }

    /// <summary>
    /// Removes the section once its last key is gone, and the objects above it that it emptied:
    /// an empty object is a value to the engine, never "nothing", and Studio writes none.
    /// </summary>
    private void Prune()
    {
        var path = EmailSection.SectionPath;
        while (path.Length > 0 && Document.GetNode(path) is JsonObject { Count: 0 })
        {
            Document.Remove(path);
            var separator = path.LastIndexOf(':');
            path = separator < 0 ? "" : path[..separator];
        }
    }

    /// <summary>A provider as the list names it: the preset, with what choosing it means.</summary>
    private string ProviderLabel(string provider) => provider switch
    {
        EmailSection.Values.Gmail => _strings[StudioStringKeys.MailProviderGmail],
        EmailSection.Values.Outlook => _strings[StudioStringKeys.MailProviderOutlook],
        EmailSection.Values.Custom => _strings[StudioStringKeys.MailProviderCustom],
        _ => provider,
    };

    private string AuthMethodLabel(string method) => method switch
    {
        EmailSection.Values.Password => _strings[StudioStringKeys.MailAuthPassword],
        EmailSection.Values.OAuth2 => _strings[StudioStringKeys.MailAuthOAuth2],
        _ => method,
    };

    private EmailChoiceViewModel Preset() => new("", () => _strings[StudioStringKeys.MailChoicePreset]);

    private List<EmailChoiceViewModel> WithPreset(IReadOnlyList<string> values) =>
        [Preset(), .. values.Select(value => new EmailChoiceViewModel(value, () => value))];
}

/// <summary>
/// One entry of a list of the E-mail form: the value the file holds — empty for the entry that
/// removes the key and leaves the choice to the preset — and what the list shows for it. The
/// label is read live, so a language change re-emits it without the list being rebuilt (a list
/// rebuilt under a selection loses it).
/// </summary>
public sealed class EmailChoiceViewModel : ObservableObject
{
    private readonly Func<string> _label;

    internal EmailChoiceViewModel(string value, Func<string> label)
    {
        Value = value;
        _label = label;
    }

    /// <summary>The value written to the file, in the engine's spelling; empty writes none.</summary>
    public string Value { get; }

    /// <summary>What the list shows.</summary>
    public string Label => _label();

    internal void RefreshLabel() => OnPropertyChanged(nameof(Label));
}
