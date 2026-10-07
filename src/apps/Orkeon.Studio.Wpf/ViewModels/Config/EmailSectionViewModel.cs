using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

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
/// </summary>
public sealed class EmailSectionViewModel : DocumentSectionViewModel
{
    /// <summary>The provider a new account's box opens on: the preset a first account most often uses.</summary>
    private const string NewAccountProviderDefault = EmailAccountRowViewModel.Gmail;

    private readonly IStudioStrings _strings;
    private readonly Func<string?> _settingsPath;
    private readonly IPathPicker _picker;
    private EmailAccountRowViewModel? _selectedAccount;
    private bool _isExpert;
    private bool _isAdding;
    private bool _syncingChoices;
    private string _newAccountName = "";
    private string _newAccountProvider = NewAccountProviderDefault;
    private string _newAccountAddress = "";

    /// <summary>
    /// Binds the form to the <c>Orkeon:Tools:Email</c> section of the document.
    /// <paramref name="settingsPath"/> gives the file the document is saved to, for the line that
    /// says where the accounts go; <paramref name="picker"/> browses for the token folder.
    /// </summary>
    public EmailSectionViewModel(
        Func<AppSettingsDocument> document,
        Action onChanged,
        IStudioStrings? strings = null,
        Func<string?>? settingsPath = null,
        IPathPicker? picker = null)
        : base(document, onChanged)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        _settingsPath = settingsPath ?? (() => null);
        _picker = picker ?? NullPathPicker.Instance;

        ProviderChoices = [.. EmailSection.Providers.Select(provider => new EmailChoiceViewModel(provider, () => ProviderLabel(provider)))];
        AuthMethodChoices = [Preset(), .. EmailSection.AuthMethods.Select(method => new EmailChoiceViewModel(method, () => AuthMethodLabel(method)))];
        IncomingProtocolChoices = WithPreset(EmailSection.IncomingProtocols);
        OutgoingProtocolChoices = WithPreset(EmailSection.OutgoingProtocols);
        SecurityChoices = WithPreset(EmailSection.Securities);

        BeginAddCommand = new RelayCommand(BeginAdd);
        ConfirmAddCommand = new RelayCommand(ConfirmAdd, () => CanConfirmAdd);
        CancelAddCommand = new RelayCommand(() => IsAdding = false);
        RenameAccountCommand = new RelayCommand(
            parameter => { if (parameter is EmailAccountRowViewModel row) Arm(row, rename: true); },
            parameter => parameter is EmailAccountRowViewModel);
        RemoveAccountCommand = new RelayCommand(
            parameter => { if (parameter is EmailAccountRowViewModel row) Arm(row, rename: false); },
            parameter => parameter is EmailAccountRowViewModel);
        BrowseCredentialsDirectoryCommand = new RelayCommand(BrowseCredentialsDirectory);

        // The rows do not listen: the owner of the list refreshes what they say (STUDIO-56).
        _strings.CultureChanged += (_, _) => RefreshTexts();
        Reload();
    }

    private EmailSection Section => Document.Email;

    /// <summary>The document the rows read their findings from.</summary>
    internal AppSettingsDocument CurrentDocument => Document;

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
                OnPropertyChanged(nameof(HasSelectedAccount));
        }
    }

    /// <summary>Whether an account's form shows.</summary>
    public bool HasSelectedAccount => _selectedAccount is not null;

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
    public string SettingsFileLine => string.Format(
        CultureInfo.CurrentCulture,
        _strings[StudioStringKeys.MailSettingsFile],
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
        NotifyDocumentChanged();
    }

    /// <summary>Moves the row's account under <paramref name="newName"/>; false when the name is refused.</summary>
    internal bool Rename(EmailAccountRowViewModel row, string newName)
    {
        if (NameRefusal(newName, row) is not null)
            return false;

        if (string.Equals(row.Name, newName, StringComparison.Ordinal))
            return true;

        Section.RenameAccount(row.Name, newName);
        row.Renamed(newName);
        SyncDefaultAccountChoices();
        RefreshProblems();
        NotifyDocumentChanged();
        return true;
    }

    /// <summary>Removes the row's account and everything under it, and shows a neighbour's form.</summary>
    internal void Remove(EmailAccountRowViewModel row)
    {
        var index = Accounts.IndexOf(row);
        Section.RemoveAccount(row.Name);
        Prune();
        Accounts.Remove(row);
        SelectedAccount = Accounts.Count == 0 ? null : Accounts[Math.Min(Math.Max(index, 0), Accounts.Count - 1)];
        SyncDefaultAccountChoices();
        RefreshProblems();
        NotifyDocumentChanged();
    }

    private void Reload()
    {
        var selected = _selectedAccount?.Name;
        Accounts.Clear();
        foreach (var name in Section.AccountNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            if (Section.GetAccount(name) is { } account)
                Accounts.Add(new EmailAccountRowViewModel(account, this, _strings));
        }

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
        SelectedAccount = row;
        IsAdding = false;
        SyncDefaultAccountChoices();
        RefreshProblems();
        NotifyDocumentChanged();
    }

    private void RefreshAddBox()
    {
        OnPropertiesChanged(nameof(AddRefusal), nameof(HasAddRefusal));
        ConfirmAddCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Arms one row's question and disarms every other, the add box included: one question at a time.</summary>
    private void Arm(EmailAccountRowViewModel row, bool rename)
    {
        IsAdding = false;
        foreach (var other in Accounts)
            other.Disarm();

        if (rename)
            row.BeginRename();
        else
            row.BeginRemove();
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
        EmailAccountRowViewModel.Gmail => _strings[StudioStringKeys.MailProviderGmail],
        EmailAccountRowViewModel.Outlook => _strings[StudioStringKeys.MailProviderOutlook],
        EmailAccountRowViewModel.Custom => _strings[StudioStringKeys.MailProviderCustom],
        _ => provider,
    };

    private string AuthMethodLabel(string method) => method switch
    {
        EmailAccountRowViewModel.Password => _strings[StudioStringKeys.MailAuthPassword],
        EmailAccountRowViewModel.OAuth2 => _strings[StudioStringKeys.MailAuthOAuth2],
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
