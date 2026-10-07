using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.ViewModels.Common;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// One e-mail account of the E-mail tab (STUDIO-67): every key of the account as a field — text
/// for a value, a switch for each of the six rights —, what a field left blank is worth
/// (<see cref="Effective"/>, the watermarks), and what the run will say of the account as the
/// file holds it (<see cref="Problems"/>). Every edit writes the whole account in place; a field
/// emptied removes its key, and a value the user did not touch keeps the spelling the file had.
/// <para>
/// The password and the client secret are typed here and never written (STUDIO-68): each goes
/// to the key store under a variable — the one the file names, else one derived from the
/// account's name, which the first value kept writes into the file. The engine reads the
/// variable; the file only ever holds its <i>name</i>.
/// </para>
/// </summary>
public sealed class EmailAccountRowViewModel : ObservableObject
{
    // The engine's spellings the form branches on, as EmailSection lists them.
    internal const string Custom = "Custom";
    internal const string Gmail = "Gmail";
    internal const string Outlook = "Outlook";
    internal const string PasswordMethod = "Password";
    internal const string OAuth2Method = "OAuth2";

    private readonly EmailSectionViewModel _owner;
    private readonly IStudioStrings _strings;
    private EmailAccountDefinition _loaded;
    private EmailAccountEffective _effective;
    private IReadOnlyList<ValidationMessageViewModel> _problems = [];
    private string _name;
    private string? _provider;
    private string _address;
    private string _displayName;
    private EmailRight _rights;
    private string? _rightsRaw;
    private string? _incomingProtocol;
    private string _incomingHost;
    private string _incomingPort;
    private int? _incomingPortNumber;
    private string? _incomingSecurity;
    private string? _outgoingProtocol;
    private string _outgoingHost;
    private string _outgoingPort;
    private int? _outgoingPortNumber;
    private string? _outgoingSecurity;
    private string? _authMethod;
    private string _username;
    private string _passwordEnvVar;
    private string _clientId;
    private string _clientSecretEnvVar;
    private string _tenant;
    private string _timeoutSeconds;
    private int? _timeoutSecondsNumber;
    private bool? _saveSentCopy;
    private string _maxRecipients;
    private int? _maxRecipientsNumber;
    private string _maxPerHour;
    private int? _maxPerHourNumber;
    private bool _isRenaming;
    private bool _isConfirmingRemove;
    private string _renameText = "";
    private SecretRowViewModel? _password;
    private SecretRowViewModel? _clientSecret;

    internal EmailAccountRowViewModel(EmailAccountDefinition account, EmailSectionViewModel owner, IStudioStrings strings)
    {
        _owner = owner;
        _strings = strings;
        _loaded = account;
        _name = account.Name;
        _provider = account.Provider;
        _address = account.Address ?? "";
        _displayName = account.DisplayName ?? "";
        _rights = account.Rights;
        _rightsRaw = account.RightsRaw;
        _incomingProtocol = account.IncomingProtocol;
        _incomingHost = account.IncomingHost ?? "";
        _incomingPortNumber = account.IncomingPort;
        _incomingPort = NumberText(account.IncomingPort);
        _incomingSecurity = account.IncomingSecurity;
        _outgoingProtocol = account.OutgoingProtocol;
        _outgoingHost = account.OutgoingHost ?? "";
        _outgoingPortNumber = account.OutgoingPort;
        _outgoingPort = NumberText(account.OutgoingPort);
        _outgoingSecurity = account.OutgoingSecurity;
        _authMethod = account.AuthMethod;
        _username = account.Username ?? "";
        _passwordEnvVar = account.PasswordEnvVar ?? "";
        _clientId = account.ClientId ?? "";
        _clientSecretEnvVar = account.ClientSecretEnvVar ?? "";
        _tenant = account.Tenant ?? "";
        _timeoutSecondsNumber = account.TimeoutSeconds;
        _timeoutSeconds = NumberText(account.TimeoutSeconds);
        _saveSentCopy = account.SaveSentCopy;
        _maxRecipientsNumber = account.MaxRecipients;
        _maxRecipients = NumberText(account.MaxRecipients);
        _maxPerHourNumber = account.MaxPerHour;
        _maxPerHour = NumberText(account.MaxPerHour);
        foreach (var pattern in account.AllowedRecipients)
            Recipients.Add(new EmailRecipientRowViewModel(this, pattern));

        _effective = EmailAccountEffective.Of(account);
        AddRecipientCommand = new RelayCommand(() => Recipients.Add(new EmailRecipientRowViewModel(this, "")));
        RemoveRecipientCommand = new RelayCommand(
            parameter => { if (parameter is EmailRecipientRowViewModel line) RemoveRecipient(line); },
            parameter => parameter is EmailRecipientRowViewModel);
        ConfirmRenameCommand = new RelayCommand(ConfirmRename, () => _isRenaming && RenameRefusal is null);
        CancelRenameCommand = new RelayCommand(() => IsRenaming = false);
        ConfirmRemoveCommand = new RelayCommand(() => { if (_isConfirmingRemove) _owner.Remove(this); });
        CancelRemoveCommand = new RelayCommand(() => IsConfirmingRemove = false);
        RefreshProblems();
    }

    /// <summary>The name the file holds the account under: what an agent passes as <c>account</c>.</summary>
    public string Name => _name;

    /// <summary>
    /// The provider preset, in the engine's spelling; a text the engine does not know shows as
    /// written (no entry of the list then matches it), and null when the file names none — the
    /// engine then reads <c>Custom</c>.
    /// </summary>
    public string? Provider
    {
        get => Shown(EmailSection.Providers, _provider);
        set => SetChoice(ref _provider, EmailSection.Providers, value);
    }

    /// <summary>The address, also the sender of every message the account sends.</summary>
    public string Address
    {
        get => _address;
        set => SetText(ref _address, value);
    }

    /// <summary>The name shown beside the address.</summary>
    public string DisplayName
    {
        get => _displayName;
        set => SetText(ref _displayName, value);
    }

    // ── the six rights ──

    /// <summary>List folders, search, read messages and save attachments.</summary>
    public bool CanRead
    {
        get => Has(EmailRight.Read);
        set => SetRight(EmailRight.Read, value);
    }

    /// <summary>Create and rename folders, move messages, set marks.</summary>
    public bool CanOrganize
    {
        get => Has(EmailRight.Organize);
        set => SetRight(EmailRight.Organize, value);
    }

    /// <summary>Save drafts without sending them.</summary>
    public bool CanDraft
    {
        get => Has(EmailRight.Draft);
        set => SetRight(EmailRight.Draft, value);
    }

    /// <summary>Send messages, to the allowed recipients only.</summary>
    public bool CanSend
    {
        get => Has(EmailRight.Send);
        set => SetRight(EmailRight.Send, value);
    }

    /// <summary>Move messages to the trash.</summary>
    public bool CanDelete
    {
        get => Has(EmailRight.Delete);
        set => SetRight(EmailRight.Delete, value);
    }

    /// <summary>Delete messages permanently.</summary>
    public bool CanPurge
    {
        get => Has(EmailRight.Purge);
        set => SetRight(EmailRight.Purge, value);
    }

    // ── the reading side ──

    /// <summary><c>Incoming:Protocol</c>, or null to leave it to the preset.</summary>
    public string? IncomingProtocol
    {
        get => Shown(EmailSection.IncomingProtocols, _incomingProtocol);
        set => SetChoice(ref _incomingProtocol, EmailSection.IncomingProtocols, value);
    }

    /// <summary><c>Incoming:Host</c>: the server mail is read from.</summary>
    public string IncomingHost
    {
        get => _incomingHost;
        set => SetText(ref _incomingHost, value);
    }

    /// <summary><c>Incoming:Port</c>, as typed.</summary>
    public string IncomingPort
    {
        get => _incomingPort;
        set => SetNumber(ref _incomingPort, ref _incomingPortNumber, value);
    }

    /// <summary>The watermark of <see cref="IncomingPort"/>: the port the engine uses when the field is empty.</summary>
    public string? IncomingPortPlaceholder => _effective.IncomingPort?.ToString(CultureInfo.InvariantCulture);

    /// <summary><c>Incoming:Security</c>, or null to leave it to the preset.</summary>
    public string? IncomingSecurity
    {
        get => Shown(EmailSection.Securities, _incomingSecurity);
        set => SetChoice(ref _incomingSecurity, EmailSection.Securities, value);
    }

    // ── the sending side ──

    /// <summary><c>Outgoing:Protocol</c>, or null to leave it to the preset.</summary>
    public string? OutgoingProtocol
    {
        get => Shown(EmailSection.OutgoingProtocols, _outgoingProtocol);
        set => SetChoice(ref _outgoingProtocol, EmailSection.OutgoingProtocols, value);
    }

    /// <summary><c>Outgoing:Host</c>: the server mail is sent through; without one a Custom account does not send.</summary>
    public string OutgoingHost
    {
        get => _outgoingHost;
        set => SetText(ref _outgoingHost, value);
    }

    /// <summary><c>Outgoing:Port</c>, as typed.</summary>
    public string OutgoingPort
    {
        get => _outgoingPort;
        set => SetNumber(ref _outgoingPort, ref _outgoingPortNumber, value);
    }

    /// <summary>The watermark of <see cref="OutgoingPort"/>.</summary>
    public string? OutgoingPortPlaceholder => _effective.OutgoingPort?.ToString(CultureInfo.InvariantCulture);

    /// <summary><c>Outgoing:Security</c>, or null to leave it to the preset.</summary>
    public string? OutgoingSecurity
    {
        get => Shown(EmailSection.Securities, _outgoingSecurity);
        set => SetChoice(ref _outgoingSecurity, EmailSection.Securities, value);
    }

    // ── signing in ──

    /// <summary><c>Auth:Method</c>, or null to leave it to the preset.</summary>
    public string? AuthMethod
    {
        get => Shown(EmailSection.AuthMethods, _authMethod);
        set => SetChoice(ref _authMethod, EmailSection.AuthMethods, value);
    }

    /// <summary>The watermark of <see cref="AuthMethod"/>: the method the engine uses when the list names none, as the list spells it.</summary>
    public string AuthMethodPlaceholder =>
        _owner.AuthMethodChoices.FirstOrDefault(choice => choice.Value == _effective.AuthMethod)?.Label ?? _effective.AuthMethod;

    /// <summary><c>Auth:Username</c>; empty, the engine signs in with the address.</summary>
    public string Username
    {
        get => _username;
        set => SetText(ref _username, value);
    }

    /// <summary><c>Auth:PasswordEnvVar</c>: the <i>name</i> of the variable that holds the password.</summary>
    public string PasswordEnvVar
    {
        get => _passwordEnvVar;
        set => SetText(ref _passwordEnvVar, value);
    }

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

    /// <summary><c>Auth:Tenant</c>: the Microsoft tenant of an Outlook account.</summary>
    public string Tenant
    {
        get => _tenant;
        set => SetText(ref _tenant, value);
    }

    // ── the rest ──

    /// <summary><c>TimeoutSeconds</c>, as typed.</summary>
    public string TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => SetNumber(ref _timeoutSeconds, ref _timeoutSecondsNumber, value);
    }

    /// <summary>
    /// Whether a sent message is filed in the Sent folder, as the engine will do it: the account's
    /// own choice, else the preset's. A plain switch — setting it back to what the preset does
    /// removes the key rather than spelling a value the engine already applies.
    /// </summary>
    public bool SaveSentCopy
    {
        get => _effective.SaveSentCopy;
        set
        {
            if (_effective.SaveSentCopy == value)
                return;

            var preset = EmailAccountEffective.Of(ToDefinition() with { SaveSentCopy = null }).SaveSentCopy;
            _saveSentCopy = value == preset ? null : value;
            Commit();
        }
    }

    /// <summary><c>Send:MaxRecipients</c>, as typed.</summary>
    public string MaxRecipients
    {
        get => _maxRecipients;
        set => SetNumber(ref _maxRecipients, ref _maxRecipientsNumber, value);
    }

    /// <summary><c>Send:MaxPerHour</c>, as typed.</summary>
    public string MaxPerHour
    {
        get => _maxPerHour;
        set => SetNumber(ref _maxPerHour, ref _maxPerHourNumber, value);
    }

    /// <summary>
    /// Whether a number field holds something that is no whole number. Such a text stays on
    /// screen and is not written: the document keeps the last number the field held.
    /// </summary>
    public bool HasUnreadNumber =>
        Unread(_incomingPort) || Unread(_outgoingPort) || Unread(_timeoutSeconds) || Unread(_maxRecipients) || Unread(_maxPerHour);

    // ── the recipients ──

    /// <summary><c>Send:AllowedRecipients</c>, one pattern per line; a blank line is a place to type and is not written.</summary>
    public ObservableCollection<EmailRecipientRowViewModel> Recipients { get; } = [];

    /// <summary>Adds a blank line to <see cref="Recipients"/>.</summary>
    public RelayCommand AddRecipientCommand { get; }

    /// <summary>Removes the line given as parameter.</summary>
    public RelayCommand RemoveRecipientCommand { get; }

    // ── what a blank field is worth, and what the run will say ──

    /// <summary>What the engine resolves for the fields left blank: the watermarks of the form.</summary>
    public EmailAccountEffective Effective => _effective;

    /// <summary>
    /// What the run will say of the account as the file holds it (STUDIO-66): a key the engine
    /// does not know and a value it cannot read first, the engine's rules otherwise. Each is a
    /// warning — an incomplete account stops no save.
    /// </summary>
    public IReadOnlyList<ValidationMessageViewModel> Problems => _problems;

    /// <summary>Whether the run will set the account aside.</summary>
    public bool HasProblems => _problems.Count > 0;

    // ── what each mode sees ──

    /// <summary>Whether the expert's fields show.</summary>
    public bool ShowExpertFields => _owner.IsExpert;

    /// <summary>
    /// Whether the two hosts show: always for the expert, and for everyone when no preset names
    /// them — a Custom account has no server until its hosts are given.
    /// </summary>
    public bool ShowHosts => _owner.IsExpert || Provider is not (Gmail or Outlook);

    /// <summary>Whether the OAuth client id shows: for the expert, and for everyone once the account signs in with OAuth2.</summary>
    public bool ShowClientId => _owner.IsExpert || _effective.AuthMethod == OAuth2Method;

    /// <summary>
    /// Whether the sending rules show: as soon as the account may send, and always for the
    /// expert — no key is reachable through the raw file alone.
    /// </summary>
    public bool ShowRecipients => CanSend || _owner.IsExpert;

    /// <summary>Whether the account may send and lists nobody: the engine then refuses every recipient.</summary>
    public bool ShowSendClosed => CanSend && !Recipients.Any(line => !string.IsNullOrWhiteSpace(line.Pattern));

    // ── the secrets: typed here, kept outside any file ──

    /// <summary>
    /// The line the password is typed on, for an account that signs in with one; null otherwise.
    /// It stores under the variable the file names, else under the one derived from the account's
    /// name — and the first value kept writes that name into the file.
    /// </summary>
    public SecretRowViewModel? Password => _password;

    /// <summary>Whether the password line shows.</summary>
    public bool ShowPassword => _password is not null;

    /// <summary>Whether the run will find the password: the file names a variable, and it holds a value.</summary>
    public bool PasswordStored => IsStored(_password, _passwordEnvVar);

    /// <summary>"Stored on this machine, outside any file" / "Not stored yet", localized — never the value.</summary>
    public string PasswordStatus => StoredText(PasswordStored);

    /// <summary>Where the password is kept, for the expert: "Kept in the variable X"; null for the novice, and while nothing is kept.</summary>
    public string? PasswordVariable => VariableLine(_password, PasswordStored);

    /// <summary>
    /// The line the OAuth client secret is typed on: for a Gmail account that signs in with
    /// OAuth2, the one case the engine requires it in; null otherwise.
    /// </summary>
    public SecretRowViewModel? ClientSecret => _clientSecret;

    /// <summary>Whether the client-secret line shows.</summary>
    public bool ShowClientSecret => _clientSecret is not null;

    /// <summary>Whether the run will find the client secret.</summary>
    public bool ClientSecretStored => IsStored(_clientSecret, _clientSecretEnvVar);

    /// <summary>The state of the client secret, as <see cref="PasswordStatus"/>.</summary>
    public string ClientSecretStatus => StoredText(ClientSecretStored);

    /// <summary>Where the client secret is kept, as <see cref="PasswordVariable"/>.</summary>
    public string? ClientSecretVariable => VariableLine(_clientSecret, ClientSecretStored);

    /// <summary>The names the account's fields hold for its two variables, as typed.</summary>
    internal IEnumerable<string> SecretVariables => [_passwordEnvVar, _clientSecretEnvVar];

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

    /// <summary>Whether the row shows its action row: neither question is open.</summary>
    public bool IsIdle => !_isRenaming && !_isConfirmingRemove;

    internal void BeginRename()
    {
        _renameText = _name;
        OnPropertyChanged(nameof(RenameText));
        IsRenaming = true;
    }

    internal void BeginRemove() => IsConfirmingRemove = true;

    internal void Disarm()
    {
        IsRenaming = false;
        IsConfirmingRemove = false;
    }

    /// <summary>The account moved under <paramref name="name"/> in the document: the row follows.</summary>
    internal void Renamed(string name)
    {
        _name = name;
        _loaded = _loaded with { Name = name };
        OnPropertyChanged(nameof(Name));
    }

    internal void RefreshMode() =>
        OnPropertiesChanged(
            nameof(ShowExpertFields), nameof(ShowHosts), nameof(ShowClientId), nameof(ShowRecipients),
            nameof(PasswordVariable), nameof(ClientSecretVariable));

    /// <summary>
    /// Puts each secret line on the variable it stores under: the one the file names, else the one
    /// derived from the account's name, clear of what the accounts of the file already name. Called
    /// by the owner of the list after every edit — another account naming a variable can move this
    /// one's derived name. A line whose variable did not move is kept, with what is typed in it.
    /// </summary>
    internal void RefreshSecrets()
    {
        var taken = _owner.SecretVariables.ToList();
        var password = _effective.AuthMethod == PasswordMethod;
        var clientSecret = Provider == Gmail && _effective.AuthMethod == OAuth2Method;

        if (PutSecret(ref _password, password, _passwordEnvVar, EmailSecretNames.PasswordFor(_name, taken), StudioStringKeys.MailPassword))
            OnPropertiesChanged(nameof(Password), nameof(ShowPassword));
        if (PutSecret(ref _clientSecret, clientSecret, _clientSecretEnvVar, EmailSecretNames.ClientSecretFor(_name, taken), StudioStringKeys.MailClientSecret))
            OnPropertiesChanged(nameof(ClientSecret), nameof(ShowClientSecret));

        RefreshSecretStates();
    }

    /// <summary>Reads again what the run will say of the account, as the document now holds it.</summary>
    internal void RefreshProblems()
    {
        _problems = [.. EmailAccountRules.Check(_owner.CurrentDocument, _name).Select(message => new ValidationMessageViewModel(message, _strings))];
        OnPropertiesChanged(nameof(Problems), nameof(HasProblems));
    }

    /// <summary>A language change: the findings and the refusal are said again.</summary>
    internal void RefreshTexts()
    {
        RefreshProblems();
        _password?.RefreshTexts();
        _clientSecret?.RefreshTexts();
        RefreshSecretStates();
        OnPropertiesChanged(nameof(RenameRefusal), nameof(HasRenameRefusal), nameof(AuthMethodPlaceholder));
    }

    /// <summary>A recipient line was typed in: the list is written again.</summary>
    internal void RecipientEdited() => Commit();

    /// <summary>
    /// The account as the fields hold it. A field left blank is null — an absent key —, a text
    /// the user did not change is the one the file held, and a number field that holds no number
    /// keeps its last one.
    /// </summary>
    internal EmailAccountDefinition ToDefinition() => new()
    {
        Name = _name,
        Provider = _provider,
        Address = Text(_address, _loaded.Address),
        DisplayName = Text(_displayName, _loaded.DisplayName),
        Rights = _rights,
        RightsRaw = _rightsRaw,
        TimeoutSeconds = _timeoutSecondsNumber,
        SaveSentCopy = _saveSentCopy,
        IncomingProtocol = _incomingProtocol,
        IncomingHost = Text(_incomingHost, _loaded.IncomingHost),
        IncomingPort = _incomingPortNumber,
        IncomingSecurity = _incomingSecurity,
        OutgoingProtocol = _outgoingProtocol,
        OutgoingHost = Text(_outgoingHost, _loaded.OutgoingHost),
        OutgoingPort = _outgoingPortNumber,
        OutgoingSecurity = _outgoingSecurity,
        AuthMethod = _authMethod,
        Username = Text(_username, _loaded.Username),
        PasswordEnvVar = Text(_passwordEnvVar, _loaded.PasswordEnvVar),
        ClientId = Text(_clientId, _loaded.ClientId),
        ClientSecretEnvVar = Text(_clientSecretEnvVar, _loaded.ClientSecretEnvVar),
        Tenant = Text(_tenant, _loaded.Tenant),
        AllowedRecipients = [.. Recipients.Select(line => line.Pattern.Trim()).Where(pattern => pattern.Length > 0)],
        MaxRecipients = _maxRecipientsNumber,
        MaxPerHour = _maxPerHourNumber,
    };

    private bool Has(EmailRight right) => (_rights & right) != EmailRight.None;

    private void SetRight(EmailRight right, bool granted, [CallerMemberName] string? propertyName = null)
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

    /// <summary>A list's choice: empty leaves the key out, and choosing what already shows changes nothing.</summary>
    private void SetChoice(ref string? field, IReadOnlyList<string> names, string? value, [CallerMemberName] string? propertyName = null)
    {
        var chosen = string.IsNullOrWhiteSpace(value) ? null : value;
        if (string.Equals(Shown(names, field), chosen, StringComparison.Ordinal))
            return;

        field = chosen;
        OnPropertyChanged(propertyName);
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

    private void RemoveRecipient(EmailRecipientRowViewModel line)
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

    /// <summary>Writes the account, then publishes what the edit moved: the watermarks and what each mode sees.</summary>
    private void Commit()
    {
        var definition = ToDefinition();
        _effective = EmailAccountEffective.Of(definition);
        _owner.Write(this);
        _loaded = definition;
        OnPropertiesChanged(
            nameof(Effective), nameof(IncomingPortPlaceholder), nameof(OutgoingPortPlaceholder), nameof(AuthMethodPlaceholder), nameof(SaveSentCopy),
            nameof(ShowHosts), nameof(ShowClientId), nameof(ShowRecipients), nameof(ShowSendClosed));
    }

    /// <summary>
    /// Builds, moves or drops one secret line; true when the line is another object than before.
    /// <paramref name="written"/> is the name the field holds, <paramref name="derived"/> the one
    /// used while it is empty.
    /// </summary>
    private bool PutSecret(ref SecretRowViewModel? line, bool asked, string written, string derived, string labelKey)
    {
        var variable = !asked ? null
            : written.Trim() is { Length: > 0 } named ? named
            : derived;
        if (string.Equals(line?.EnvName, variable, StringComparison.Ordinal))
            return false;

        var typed = line?.KeyInput ?? "";
        if (line is not null)
            line.PropertyChanged -= OnSecretChanged;

        line = variable is null ? null : new SecretRowViewModel(variable, _strings[labelKey], _owner.KeyStore, _strings) { KeyInput = typed };
        if (line is not null)
            line.PropertyChanged += OnSecretChanged;

        return true;
    }

    /// <summary>
    /// A value was kept. The first one names its variable in the file — the derived name, written
    /// like a name the expert typed; a name the file already holds is never written again, so
    /// neither a new password nor a renamed account moves the variable.
    /// </summary>
    private void OnSecretChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SecretRowViewModel.HasKey) || sender is not SecretRowViewModel { HasKey: true } line)
            return;

        if (ReferenceEquals(line, _password) && string.IsNullOrWhiteSpace(_passwordEnvVar))
            PasswordEnvVar = line.EnvName;
        else if (ReferenceEquals(line, _clientSecret) && string.IsNullOrWhiteSpace(_clientSecretEnvVar))
            ClientSecretEnvVar = line.EnvName;
        else
            RefreshSecretStates();
    }

    private void RefreshSecretStates() =>
        OnPropertiesChanged(
            nameof(PasswordStored), nameof(PasswordStatus), nameof(PasswordVariable),
            nameof(ClientSecretStored), nameof(ClientSecretStatus), nameof(ClientSecretVariable));

    /// <summary>
    /// Whether the run will find the secret: a value under a variable the file names. A value left
    /// under the derived name by an account removed since is not one the run reads.
    /// </summary>
    private static bool IsStored(SecretRowViewModel? line, string written) =>
        line is { HasKey: true } && !string.IsNullOrWhiteSpace(written);

    private string StoredText(bool stored) =>
        _strings[stored ? StudioStringKeys.MailPasswordStored : StudioStringKeys.MailPasswordMissing];

    private string? VariableLine(SecretRowViewModel? line, bool stored) =>
        _owner.IsExpert && stored && line is not null
            ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.MailSecretVariable], line.EnvName)
            : null;

    /// <summary>The engine's spelling of <paramref name="written"/> when it is one of <paramref name="names"/> but for the case, else the text as written.</summary>
    private static string? Shown(IReadOnlyList<string> names, string? written) =>
        written is null
            ? null
            : names.FirstOrDefault(name => string.Equals(name, written, StringComparison.OrdinalIgnoreCase)) ?? written;

    /// <summary>The text to write: the file's own when the field still shows it, else what was typed, trimmed, and null when blank.</summary>
    private static string? Text(string typed, string? loaded) =>
        string.Equals(typed, loaded ?? "", StringComparison.Ordinal)
            ? loaded
            : string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();

    private static string NumberText(int? number) => number?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static bool Unread(string text) =>
        text.Trim() is { Length: > 0 } typed && !int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
}

/// <summary>
/// One line of <c>Send:AllowedRecipients</c>: an address, <c>*@domain</c> or <c>*</c>. A line
/// that is none of them says so on itself, and is written all the same — the run then says it too.
/// </summary>
public sealed class EmailRecipientRowViewModel : ObservableObject
{
    private readonly EmailAccountRowViewModel _owner;
    private string _pattern;

    internal EmailRecipientRowViewModel(EmailAccountRowViewModel owner, string pattern)
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
    public bool IsInvalid => _pattern.Trim() is { Length: > 0 } pattern && !EmailAccountRules.IsRecipientPattern(pattern);
}
