using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-67 — the E-mail form: one row per account of the file, every field written in place
/// through <see cref="EmailSection"/>, an account that only reaches the file once its box is
/// validated, a rename and a removal that ask first, and what the run will say of each account
/// said on its row.
/// </summary>
public sealed partial class EmailSectionViewModelTests
{
    private const string Accounts = "Orkeon:Tools:Email:Accounts";

    private const string Gmail = """
        { "Orkeon": { "Tools": { "Email": { "Accounts": { "perso": {
          "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read, Organize",
          "Auth": { "PasswordEnvVar": "GMAIL_APP_PASSWORD" } } } } } } }
        """;

    private const string Two = """
        { "Orkeon": { "Tools": { "Email": { "DefaultAccount": "perso", "Accounts": {
          "perso": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read",
                     "Auth": { "PasswordEnvVar": "GMAIL_APP_PASSWORD" } },
          "work": { "Provider": "Custom", "Address": "me@example.com", "Rights": "Read",
                    "Incoming": { "Host": "imap.example.com" },
                    "Auth": { "PasswordEnvVar": "WORK_MAIL_PASSWORD" } } } } } } }
        """;

    private const string Unnamed = """
        { "Orkeon": { "Tools": { "Email": { "Accounts": { "perso": {
          "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read" } } } } } }
        """;

    private const string Stored = "Stored on this machine, outside any file";

    private const string NotStored = "Not stored yet";

    private static (EmailSectionViewModel Section, AppSettingsDocument Document, Func<int> Changes) Build(
        string json = "{}", IStudioStrings? strings = null, FakeApiKeyStore? keys = null)
    {
        var document = AppSettingsDocument.Parse(json);
        var changes = 0;
        var section = new EmailSectionViewModel(() => document, () => changes++, strings, keyStore: keys ?? new FakeApiKeyStore());
        return (section, document, () => changes);
    }

    /// <summary>A port that flips between the English defaults and one French sentence.</summary>
    private sealed class SwitchableStrings : IStudioStrings
    {
        public const string FrenchRights = "Un compte e-mail n'accorde aucun droit.";
        public const string FrenchPreset = "Laisser le préréglage";
        public const string FrenchNotStored = "Pas encore mémorisé";
        public const string FrenchNotReady = "Pas prêt";
        public const string FrenchReachable = "Joignable";

        private bool _french;

        public string this[string key] => (_french, key) switch
        {
            (true, StudioStringKeys.MailChoicePreset) => FrenchPreset,
            (true, StudioStringKeys.MailPasswordMissing) => FrenchNotStored,
            (true, StudioStringKeys.MailStateNotReady) => FrenchNotReady,
            (true, StudioStringKeys.MailTestReachable) => FrenchReachable,
            (true, _) when key == ValidationMessageKey(ValidationCodes.EmailRights) => FrenchRights,
            _ => EnglishStudioStrings.Instance[key],
        };

        public event EventHandler? CultureChanged;

        public void SwitchToFrench()
        {
            _french = true;
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string ValidationMessageKey(string code) => "Studio.Diagnostics.Code." + code;

    // ── adding ──

    [Fact]
    public void Adding_writes_nothing_until_the_box_is_validated_and_then_a_named_addressed_account()
    {
        var (section, document, changes) = Build();
        Assert.False(section.HasAccounts);
        Assert.False(section.Exists);

        section.BeginAddCommand.Execute(null);
        Assert.True(section.IsAdding);
        Assert.Equal("Gmail", section.NewAccountProvider);
        Assert.False(section.ConfirmAddCommand.CanExecute(null));
        section.NewAccountName = "perso";
        Assert.False(section.ConfirmAddCommand.CanExecute(null));
        section.NewAccountAddress = " me@gmail.com ";

        Assert.Equal(0, changes());
        Assert.Null(document.GetNode("Orkeon"));
        Assert.Empty(section.Accounts);

        Assert.True(section.ConfirmAddCommand.CanExecute(null));
        section.ConfirmAddCommand.Execute(null);

        Assert.False(section.IsAdding);
        Assert.Equal(1, changes());
        var row = Assert.Single(section.Accounts);
        Assert.Same(row, section.SelectedAccount);
        Assert.Equal("perso", row.Name);
        Assert.Equal("Gmail", document.GetString($"{Accounts}:perso:Provider"));
        Assert.Equal("me@gmail.com", document.GetString($"{Accounts}:perso:Address"));
        var json = document.ToJson();
        Assert.DoesNotContain("\"\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("{}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("[]", json, StringComparison.Ordinal);
        // It lacks its rights, which the row says as the run will.
        Assert.Contains(row.Problems, problem => problem.Code == ValidationCodes.EmailRights);
    }

    [Fact]
    public void A_cancelled_box_writes_nothing_and_a_reopened_one_starts_blank()
    {
        var (section, document, changes) = Build();
        section.BeginAddCommand.Execute(null);
        section.NewAccountName = "perso";
        section.NewAccountProvider = "Outlook";
        section.NewAccountAddress = "me@outlook.com";

        section.CancelAddCommand.Execute(null);

        Assert.False(section.IsAdding);
        Assert.Equal(0, changes());
        Assert.Null(document.GetNode("Orkeon"));

        section.BeginAddCommand.Execute(null);
        Assert.Equal("", section.NewAccountName);
        Assert.Equal("", section.NewAccountAddress);
        Assert.Equal("Gmail", section.NewAccountProvider);
    }

    [Fact]
    public void The_box_refuses_an_unusable_name_and_a_taken_one_and_says_why()
    {
        var (section, _, changes) = Build(Gmail);
        section.BeginAddCommand.Execute(null);
        section.NewAccountAddress = "other@gmail.com";
        Assert.Null(section.AddRefusal);

        section.NewAccountName = "my account";
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.MailNameInvalid], section.AddRefusal);
        Assert.True(section.HasAddRefusal);
        Assert.False(section.ConfirmAddCommand.CanExecute(null));

        // The engine compares account names without regard to case.
        section.NewAccountName = "PERSO";
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.MailNameTaken], section.AddRefusal);
        Assert.False(section.ConfirmAddCommand.CanExecute(null));
        section.ConfirmAddCommand.Execute(null);
        Assert.Single(section.Accounts);
        Assert.Equal(0, changes());

        section.NewAccountName = "second";
        Assert.Null(section.AddRefusal);
        Assert.True(section.ConfirmAddCommand.CanExecute(null));
    }

    // ── the rights ──

    [Fact]
    public void Ticking_rights_writes_them_as_the_engine_reads_them_and_unticking_them_all_removes_the_key()
    {
        var (section, document, _) = Build("""{ "Orkeon": { "Tools": { "Email": { "Accounts": { "perso": { "Address": "me@gmail.com" } } } } } }""");
        var row = Assert.Single(section.Accounts);
        Assert.False(row.CanRead);

        row.CanOrganize = true;
        row.CanRead = true;

        Assert.Equal("Read, Organize", document.GetString($"{Accounts}:perso:Rights"));
        Assert.True(row.CanRead);
        Assert.False(row.CanSend);

        row.CanRead = false;
        row.CanOrganize = false;

        Assert.Null(document.GetNode($"{Accounts}:perso:Rights"));
    }

    [Fact]
    public void Rights_the_engine_cannot_read_stay_as_written_until_a_box_is_touched()
    {
        var (section, document, _) = Build("""
            { "Orkeon": { "Tools": { "Email": { "Accounts": { "perso": {
              "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read, Reply",
              "Auth": { "PasswordEnvVar": "GMAIL_APP_PASSWORD" } } } } } } }
            """);
        var row = Assert.Single(section.Accounts);
        Assert.False(row.CanRead);
        Assert.Contains(row.Problems, problem => problem.Code == ValidationCodes.EmailValue);

        row.DisplayName = "Me";

        Assert.Equal("Read, Reply", document.GetString($"{Accounts}:perso:Rights"));
        Assert.True(row.HasProblems);

        row.CanDraft = true;

        Assert.Equal("Draft", document.GetString($"{Accounts}:perso:Rights"));
        Assert.False(row.HasProblems);
    }

    // ── the fields ──

    [Fact]
    public void Changing_the_provider_keeps_what_was_typed_and_moves_the_watermarks()
    {
        var (section, document, _) = Build(Gmail);
        var row = Assert.Single(section.Accounts);
        Assert.Equal("Gmail", row.Provider);
        Assert.Equal("imap.gmail.com", row.Effective.IncomingHost);
        Assert.Equal("993", row.IncomingPortPlaceholder);
        Assert.Equal("465", row.OutgoingPortPlaceholder);
        Assert.Equal("Password", row.Effective.AuthMethod);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.MailAuthPassword], row.AuthMethodPlaceholder);
        Assert.Equal("me@gmail.com", row.Effective.Username);

        row.DisplayName = "Me";
        row.OutgoingSecurity = "StartTls";
        Assert.Equal("587", row.OutgoingPortPlaceholder);
        var raised = new List<string>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        row.Provider = "Outlook";

        Assert.Equal("Outlook", document.GetString($"{Accounts}:perso:Provider"));
        Assert.Equal("Me", document.GetString($"{Accounts}:perso:DisplayName"));
        Assert.Equal("me@gmail.com", document.GetString($"{Accounts}:perso:Address"));
        Assert.Equal("StartTls", document.GetString($"{Accounts}:perso:Outgoing:Security"));
        Assert.Equal("GMAIL_APP_PASSWORD", document.GetString($"{Accounts}:perso:Auth:PasswordEnvVar"));
        Assert.Equal("Read, Organize", document.GetString($"{Accounts}:perso:Rights"));
        Assert.Equal("Graph", row.Effective.IncomingProtocol);
        Assert.Equal("OAuth2", row.Effective.AuthMethod);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.MailAuthOAuth2], row.AuthMethodPlaceholder);
        Assert.Null(row.IncomingPortPlaceholder);
        Assert.Contains(nameof(EmailAccountRowViewModel.Effective), raised);
        Assert.Contains(nameof(EmailAccountRowViewModel.IncomingPortPlaceholder), raised);
    }

    [Fact]
    public void A_spelling_the_file_holds_is_shown_as_the_engine_reads_it_and_kept_as_written()
    {
        var (section, document, _) = Build("""
            { "Orkeon": { "Tools": { "Email": { "Accounts": { "perso": {
              "provider": "gmail", "Address": "me@gmail.com", "Rights": "read,ORGANIZE",
              "Incoming": { "Port": "993" }, "Auth": { "PasswordEnvVar": "GMAIL_APP_PASSWORD" } } } } } } }
            """);
        var row = Assert.Single(section.Accounts);
        Assert.Equal("Gmail", row.Provider);
        Assert.True(row.CanRead);
        Assert.True(row.CanOrganize);
        Assert.Equal("993", row.IncomingPort);
        Assert.False(row.HasProblems);

        row.DisplayName = "Me";

        Assert.Equal("gmail", document.GetString($"{Accounts}:perso:provider"));
        Assert.Null(document.GetNode($"{Accounts}:perso:Provider"));
        Assert.Equal("read,ORGANIZE", document.GetString($"{Accounts}:perso:Rights"));
        Assert.Contains("\"Port\": \"993\"", document.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_field_writes_its_key_and_clearing_it_removes_the_key_and_the_object_it_emptied()
    {
        var (section, document, changes) = Build(Gmail);
        var row = Assert.Single(section.Accounts);

        row.DisplayName = " Me ";
        row.IncomingProtocol = "Pop3";
        row.IncomingHost = "pop.example.com";
        row.IncomingPort = "995";
        row.IncomingSecurity = "SslOnConnect";
        row.OutgoingProtocol = "Smtp";
        row.OutgoingHost = "smtp.example.com";
        row.OutgoingPort = "587";
        row.OutgoingSecurity = "StartTls";
        row.AuthMethod = "OAuth2";
        row.Username = "me";
        row.PasswordEnvVar = "OTHER_PASSWORD";
        row.ClientId = "client-id";
        row.ClientSecretEnvVar = "GMAIL_CLIENT_SECRET";
        row.Tenant = "common";
        row.TimeoutSeconds = "30";
        row.SaveSentCopy = true;
        row.MaxRecipients = "5";
        row.MaxPerHour = "20";

        const string account = Accounts + ":perso";
        Assert.Equal("Me", document.GetString($"{account}:DisplayName"));
        Assert.Equal("Pop3", document.GetString($"{account}:Incoming:Protocol"));
        Assert.Equal("pop.example.com", document.GetString($"{account}:Incoming:Host"));
        Assert.Equal(995, document.GetInt32($"{account}:Incoming:Port"));
        Assert.Equal("SslOnConnect", document.GetString($"{account}:Incoming:Security"));
        Assert.Equal("Smtp", document.GetString($"{account}:Outgoing:Protocol"));
        Assert.Equal("smtp.example.com", document.GetString($"{account}:Outgoing:Host"));
        Assert.Equal(587, document.GetInt32($"{account}:Outgoing:Port"));
        Assert.Equal("StartTls", document.GetString($"{account}:Outgoing:Security"));
        Assert.Equal("OAuth2", document.GetString($"{account}:Auth:Method"));
        Assert.Equal("me", document.GetString($"{account}:Auth:Username"));
        Assert.Equal("OTHER_PASSWORD", document.GetString($"{account}:Auth:PasswordEnvVar"));
        Assert.Equal("client-id", document.GetString($"{account}:Auth:ClientId"));
        Assert.Equal("GMAIL_CLIENT_SECRET", document.GetString($"{account}:Auth:ClientSecretEnvVar"));
        Assert.Equal("common", document.GetString($"{account}:Auth:Tenant"));
        Assert.Equal(30, document.GetInt32($"{account}:TimeoutSeconds"));
        Assert.True(document.Email.GetAccount("perso")!.SaveSentCopy);
        Assert.Equal(5, document.GetInt32($"{account}:Send:MaxRecipients"));
        Assert.Equal(20, document.GetInt32($"{account}:Send:MaxPerHour"));
        Assert.Equal(19, changes());

        row.DisplayName = "";
        row.IncomingProtocol = "";
        row.IncomingHost = " ";
        row.IncomingPort = "";
        row.IncomingSecurity = null;
        row.OutgoingProtocol = "";
        row.OutgoingHost = "";
        row.OutgoingPort = "";
        row.OutgoingSecurity = "";
        row.AuthMethod = "";
        row.Username = "";
        row.PasswordEnvVar = "";
        row.ClientId = "";
        row.ClientSecretEnvVar = "";
        row.Tenant = "";
        row.TimeoutSeconds = "";
        row.SaveSentCopy = false;
        row.MaxRecipients = "";
        row.MaxPerHour = "";

        Assert.Null(document.GetNode($"{account}:DisplayName"));
        Assert.Null(document.GetNode($"{account}:Incoming"));
        Assert.Null(document.GetNode($"{account}:Outgoing"));
        Assert.Null(document.GetNode($"{account}:Auth"));
        Assert.Null(document.GetNode($"{account}:Send"));
        Assert.Null(document.GetNode($"{account}:TimeoutSeconds"));
        Assert.Null(document.GetNode($"{account}:SaveSentCopy"));
        Assert.Null(row.AuthMethod);
        Assert.Null(row.IncomingProtocol);
        var json = document.ToJson();
        Assert.DoesNotContain("\"\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("{}", json, StringComparison.Ordinal);
        Assert.Equal("Gmail", document.GetString($"{account}:Provider"));
        Assert.Equal("me@gmail.com", document.GetString($"{account}:Address"));
    }

    [Fact]
    public void A_number_field_that_holds_no_whole_number_is_said_and_not_written()
    {
        var (section, document, changes) = Build(Gmail);
        var row = Assert.Single(section.Accounts);
        row.IncomingPort = "1993";
        Assert.False(row.HasUnreadNumber);

        row.IncomingPort = "19x3";

        Assert.True(row.HasUnreadNumber);
        Assert.Equal("19x3", row.IncomingPort);
        Assert.Equal(1993, document.GetInt32($"{Accounts}:perso:Incoming:Port"));
        Assert.Equal(1, changes());

        row.IncomingPort = "";

        Assert.False(row.HasUnreadNumber);
        Assert.Null(document.GetNode($"{Accounts}:perso:Incoming"));
    }

    [Fact]
    public void The_sent_copy_switch_shows_what_the_engine_will_do_and_writes_only_what_differs_from_it()
    {
        var (section, document, _) = Build(Two);
        var gmail = section.Accounts[0];
        var custom = section.Accounts[1];
        Assert.False(gmail.SaveSentCopy);
        Assert.False(custom.SaveSentCopy);

        // A custom account that sends through SMTP files its sent mail unless told otherwise.
        custom.OutgoingHost = "smtp.example.com";
        Assert.True(custom.SaveSentCopy);
        Assert.Null(document.GetNode($"{Accounts}:work:SaveSentCopy"));

        custom.SaveSentCopy = false;
        Assert.False(document.Email.GetAccount("work")!.SaveSentCopy);

        custom.SaveSentCopy = true;
        Assert.Null(document.GetNode($"{Accounts}:work:SaveSentCopy"));
    }

    [Fact]
    public void Every_edit_marks_the_document_and_raises_the_edit_the_novice_save_listens_to()
    {
        var tab = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
        });
        tab.SetDocument(AppSettingsDocument.Parse(Gmail));
        var edits = 0;
        tab.DocumentEdited += (_, _) => edits++;
        var row = Assert.Single(tab.Email.Accounts);
        Assert.False(tab.IsDirty);

        row.DisplayName = "Me";
        row.CanSend = true;
        tab.Email.DefaultAccount = "perso";

        Assert.True(tab.IsDirty);
        Assert.Equal(3, edits);
        Assert.Contains("\"DisplayName\": \"Me\"", tab.RawJson, StringComparison.Ordinal);
        // An incomplete account is a warning: it stops no save (STUDIO-66).
        Assert.False(tab.HasBlockingErrors);
    }

    [Fact]
    public void In_novice_mode_an_account_is_saved_edit_by_edit_and_an_incomplete_one_stops_no_save()
    {
        var store = new FakeAppSettingsStore();
        var path = Path.Combine(Path.GetTempPath(), "orkeon-novice", "appsettings.json");
        var tab = new ConfigTabViewModel(
            new StudioServices { SettingsStore = store, Directories = new FakeDirectoryProbe() },
            globalPathOverride: path);
        _ = new SettingsScreenViewModel(
            tab,
            new ModelProfilesViewModel(new InMemoryModelProfileStore(), tab.Llm),
            new UiModeViewModel("novice"));
        var email = tab.Email;

        email.BeginAddCommand.Execute(null);
        email.NewAccountName = "perso";
        email.NewAccountAddress = "me@gmail.com";
        Assert.Empty(store.SavedPaths);

        email.ConfirmAddCommand.Execute(null);

        // The account lacks its rights and its password: two warnings, and the file is saved all the same.
        Assert.Equal([path], store.SavedPaths);
        var row = email.SelectedAccount!;
        Assert.True(row.HasProblems);

        row.CanRead = true;
        row.CanDraft = true;

        Assert.Equal(3, store.SavedPaths.Count);
        var saved = store.LastSavedJson!;
        Assert.Contains("\"Rights\": \"Read, Draft\"", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\"\"", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("{}", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("[]", saved, StringComparison.Ordinal);

        // Nor does it stop another setting from being saved.
        Assert.True(row.HasProblems);
        tab.Rag.Profile = "quality";
        Assert.Equal(4, store.SavedPaths.Count);
        Assert.False(tab.IsDirty);
    }

    // ── renaming and removing ──

    [Fact]
    public void A_rename_asks_first_then_moves_the_account_and_the_default_follows()
    {
        var (section, document, changes) = Build(Two);
        var row = section.Accounts[0];
        Assert.True(row.IsIdle);

        Assert.True(section.RenameAccountCommand.CanExecute(row));
        section.RenameAccountCommand.Execute(row);

        Assert.True(row.IsRenaming);
        Assert.False(row.IsIdle);
        Assert.Equal("perso", row.RenameText);
        row.RenameText = "home";
        Assert.Equal(0, changes());
        Assert.NotNull(document.GetNode($"{Accounts}:perso"));

        row.ConfirmRenameCommand.Execute(null);

        Assert.False(row.IsRenaming);
        Assert.Equal("home", row.Name);
        Assert.Null(document.GetNode($"{Accounts}:perso"));
        Assert.Equal("me@gmail.com", document.GetString($"{Accounts}:home:Address"));
        Assert.Equal("home", document.GetString("Orkeon:Tools:Email:DefaultAccount"));
        Assert.Equal("home", section.DefaultAccount);
        Assert.Equal(["home", "work"], document.Email.AccountNames);
        Assert.Equal(1, changes());

        // The renamed row still writes under its new name.
        row.DisplayName = "Me";
        Assert.Equal("Me", document.GetString($"{Accounts}:home:DisplayName"));
    }

    [Fact]
    public void A_rename_to_a_taken_or_unusable_name_is_refused_in_the_box_with_the_reason()
    {
        var (section, document, changes) = Build(Two);
        var row = section.Accounts[0];
        section.RenameAccountCommand.Execute(row);
        Assert.Null(row.RenameRefusal);

        row.RenameText = "Work";
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.MailNameTaken], row.RenameRefusal);
        Assert.True(row.HasRenameRefusal);
        Assert.False(row.ConfirmRenameCommand.CanExecute(null));
        row.ConfirmRenameCommand.Execute(null);
        Assert.True(row.IsRenaming);
        Assert.Equal("perso", row.Name);

        row.RenameText = "-perso";
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.MailNameInvalid], row.RenameRefusal);

        row.CancelRenameCommand.Execute(null);
        Assert.False(row.IsRenaming);
        Assert.Equal(0, changes());
        Assert.Equal(["perso", "work"], document.Email.AccountNames);

        // Respelling a name in another case is the same account, not a taken name.
        section.RenameAccountCommand.Execute(row);
        row.RenameText = "Perso";
        Assert.Null(row.RenameRefusal);
        row.ConfirmRenameCommand.Execute(null);
        Assert.Equal(["Perso", "work"], document.Email.AccountNames);
    }

    [Fact]
    public void A_removal_asks_first_then_drops_the_account_and_the_default_that_named_it()
    {
        var (section, document, changes) = Build(Two);
        var perso = section.Accounts[0];
        var work = section.Accounts[1];
        section.RenameAccountCommand.Execute(work);

        Assert.True(section.RemoveAccountCommand.CanExecute(perso));
        section.RemoveAccountCommand.Execute(perso);

        // One question at a time: arming a row disarms every other.
        Assert.True(perso.IsConfirmingRemove);
        Assert.False(work.IsRenaming);
        Assert.Equal(0, changes());
        Assert.Equal(2, section.Accounts.Count);

        perso.CancelRemoveCommand.Execute(null);
        Assert.False(perso.IsConfirmingRemove);
        Assert.NotNull(document.GetNode($"{Accounts}:perso"));

        section.RemoveAccountCommand.Execute(perso);
        perso.ConfirmRemoveCommand.Execute(null);

        Assert.Equal(["work"], section.Accounts.Select(account => account.Name));
        Assert.Same(work, section.SelectedAccount);
        Assert.Null(document.GetNode($"{Accounts}:perso"));
        Assert.Null(document.GetNode("Orkeon:Tools:Email:DefaultAccount"));
        Assert.Null(section.DefaultAccount);
        Assert.Equal(1, changes());

        section.RemoveAccountCommand.Execute(work);
        work.ConfirmRemoveCommand.Execute(null);

        Assert.False(section.HasAccounts);
        Assert.Null(section.SelectedAccount);
        // The section goes with its last key, and the objects it emptied above it: no empty object stays.
        Assert.False(section.Exists);
        Assert.Null(document.GetNode("Orkeon"));
    }

    [Fact]
    public void Removing_the_last_account_leaves_what_the_file_holds_beside_the_section()
    {
        var (section, document, _) = Build("""
            { "Orkeon": { "FileSystem": { "Mounts": [ "/data:/docs:ro" ] }, "Tools": { "Shell": { "AllowInterpreters": true },
              "Email": { "Accounts": { "perso": { "Provider": "Gmail", "Address": "me@gmail.com" } } } } } }
            """);
        var row = Assert.Single(section.Accounts);

        section.RemoveAccountCommand.Execute(row);
        row.ConfirmRemoveCommand.Execute(null);

        Assert.Null(document.GetNode("Orkeon:Tools:Email"));
        Assert.True(document.GetBoolean("Orkeon:Tools:Shell:AllowInterpreters"));
        Assert.Equal(["/data:/docs:ro"], document.GetStringArray("Orkeon:FileSystem:Mounts"));
    }

    // ── the recipients ──

    [Fact]
    public void An_account_that_sends_lists_its_recipients_one_per_line_and_says_when_nobody_can_receive()
    {
        var (section, document, changes) = Build(Gmail);
        var row = Assert.Single(section.Accounts);
        Assert.False(row.ShowRecipients);
        Assert.False(row.ShowSendClosed);

        row.CanSend = true;

        Assert.True(row.ShowRecipients);
        Assert.True(row.ShowSendClosed);
        Assert.Empty(row.Recipients);
        var before = changes();

        // A blank line is a place to type, not a recipient: nothing reaches the file.
        row.AddRecipientCommand.Execute(null);
        var first = Assert.Single(row.Recipients);
        Assert.Equal(before, changes());
        Assert.True(row.ShowSendClosed);
        Assert.False(first.IsInvalid);

        first.Pattern = "*@example.com";
        row.AddRecipientCommand.Execute(null);
        var second = row.Recipients[1];
        second.Pattern = "everyone at example";

        Assert.False(row.ShowSendClosed);
        Assert.False(first.IsInvalid);
        Assert.True(second.IsInvalid);
        Assert.Equal(["*@example.com", "everyone at example"], document.GetStringArray($"{Accounts}:perso:Send:AllowedRecipients"));
        Assert.Contains(row.Problems, problem => problem.Code == ValidationCodes.EmailSend);

        Assert.True(row.RemoveRecipientCommand.CanExecute(second));
        row.RemoveRecipientCommand.Execute(second);

        Assert.Equal(["*@example.com"], document.GetStringArray($"{Accounts}:perso:Send:AllowedRecipients"));
        Assert.False(row.HasProblems);

        row.RemoveRecipientCommand.Execute(first);

        Assert.Null(document.GetNode($"{Accounts}:perso:Send"));
        Assert.True(row.ShowSendClosed);
    }

    [Fact]
    public void The_recipients_of_the_file_come_back_one_per_line()
    {
        var (section, _, _) = Build("""
            { "Orkeon": { "Tools": { "Email": { "Accounts": { "perso": {
              "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read, Send",
              "Send": { "AllowedRecipients": [ "boss@example.com", "*" ] },
              "Auth": { "PasswordEnvVar": "GMAIL_APP_PASSWORD" } } } } } } }
            """);
        var row = Assert.Single(section.Accounts);

        Assert.Equal(["boss@example.com", "*"], row.Recipients.Select(line => line.Pattern));
        Assert.True(row.CanSend);
        Assert.False(row.ShowSendClosed);
    }

    // ── what the file holds beyond the form ──

    [Fact]
    public void A_key_the_engine_does_not_know_is_said_on_the_row_and_survives_an_edit()
    {
        var (section, document, _) = Build("""
            { "Orkeon": { "Tools": { "Email": { "Accounts": { "perso": {
              "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Note": "mine",
              "Auth": { "PasswordEnvVar": "GMAIL_APP_PASSWORD", "Hint": "app password" } } } } } } }
            """);
        var row = Assert.Single(section.Accounts);
        Assert.True(row.HasProblems);
        Assert.All(row.Problems, problem => Assert.Equal(ValidationCodes.EmailKey, problem.Code));
        Assert.Equal(2, row.Problems.Count);

        row.DisplayName = "Me";
        row.CanOrganize = true;

        Assert.Equal("mine", document.GetString($"{Accounts}:perso:Note"));
        Assert.Equal("app password", document.GetString($"{Accounts}:perso:Auth:Hint"));
        Assert.Equal("Me", document.GetString($"{Accounts}:perso:DisplayName"));
        Assert.Equal(2, row.Problems.Count);
    }

    /// <summary>
    /// A value the engine cannot read shows as an empty field. Editing another field must not
    /// take it out of the file: the account would pass from "set aside" to "valid with the
    /// preset's port" without anybody having asked, and its finding would vanish on its own.
    /// </summary>
    [Fact]
    public void A_value_the_engine_cannot_read_keeps_its_place_and_its_finding_when_another_field_is_edited()
    {
        var (section, document, _) = Build("""
            { "Orkeon": { "Tools": { "Email": { "Accounts": { "perso": {
              "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read",
              "TimeoutSeconds": "soon", "SaveSentCopy": "maybe", "Incoming": { "Port": "abc" },
              "Send": { "MaxRecipients": "x" }, "Auth": { "PasswordEnvVar": "GMAIL_APP_PASSWORD" } } } } } } }
            """);
        var row = Assert.Single(section.Accounts);
        Assert.Equal("", row.IncomingPort);
        Assert.Equal(4, row.Problems.Count);
        Assert.All(row.Problems, problem => Assert.Equal(ValidationCodes.EmailValue, problem.Code));

        row.DisplayName = "Me";
        row.CanOrganize = true;

        Assert.Equal("abc", document.GetString($"{Accounts}:perso:Incoming:Port"));
        Assert.Equal("soon", document.GetString($"{Accounts}:perso:TimeoutSeconds"));
        Assert.Equal("maybe", document.GetString($"{Accounts}:perso:SaveSentCopy"));
        Assert.Equal("x", document.GetString($"{Accounts}:perso:Send:MaxRecipients"));
        Assert.Equal(4, row.Problems.Count);
        Assert.All(row.Problems, problem => Assert.Equal(ValidationCodes.EmailValue, problem.Code));

        // The field itself is the way out: a port that reads replaces the one that did not.
        row.IncomingPort = "993";

        Assert.Equal(993, document.GetInt32($"{Accounts}:perso:Incoming:Port"));
        Assert.Equal(3, row.Problems.Count);
    }

    [Fact]
    public void A_problem_reads_as_the_run_will_say_it_and_goes_once_the_account_is_complete()
    {
        var (section, _, _) = Build("""{ "Orkeon": { "Tools": { "Email": { "Accounts": { "perso": { "Provider": "Gmail", "Address": "me@gmail.com" } } } } } }""");
        var row = Assert.Single(section.Accounts);

        Assert.Equal(
            [ValidationCodes.EmailRights, ValidationCodes.EmailAuth],
            row.Problems.Select(problem => problem.Code));
        Assert.Equal(
            EnglishStudioStrings.Instance[ValidationMessageKey(ValidationCodes.EmailRights)],
            row.Problems[0].FriendlyText);

        row.CanRead = true;
        row.PasswordEnvVar = "GMAIL_APP_PASSWORD";

        Assert.False(row.HasProblems);
        Assert.Empty(row.Problems);
    }

    // ── the section ──

    [Fact]
    public void The_default_account_is_one_of_the_declared_ones_and_clearing_it_removes_the_key()
    {
        var (section, document, changes) = Build(Two);
        Assert.Equal("perso", section.DefaultAccount);
        Assert.Equal(["", "perso", "work"], section.DefaultAccountChoices.Select(choice => choice.Value));
        Assert.Null(section.DefaultAccountPlaceholder);

        section.DefaultAccount = "work";
        Assert.Equal("work", document.GetString("Orkeon:Tools:Email:DefaultAccount"));

        section.DefaultAccount = "";
        Assert.Null(document.GetNode("Orkeon:Tools:Email:DefaultAccount"));
        Assert.Null(section.DefaultAccount);
        Assert.Equal(2, changes());

        // With one account the engine needs no default: the watermark names it.
        var (single, _, _) = Build(Gmail);
        Assert.Null(single.DefaultAccount);
        Assert.Equal("perso", single.DefaultAccountPlaceholder);
    }

    [Fact]
    public void The_screening_switch_and_the_token_folder_are_written_and_removed_with_their_objects()
    {
        var picker = new FakePathPicker { FolderToReturn = "/srv/orkeon/tokens" };
        var document = AppSettingsDocument.Parse(Gmail);
        var section = new EmailSectionViewModel(() => document, () => { }, picker: picker);
        Assert.False(section.WithholdRejected);
        Assert.Equal("", section.CredentialsDirectory);

        section.WithholdRejected = true;
        section.BrowseCredentialsDirectoryCommand.Execute(null);

        Assert.True(document.Email.WithholdRejected);
        Assert.Equal("/srv/orkeon/tokens", document.GetString("Orkeon:Tools:Email:CredentialsDirectory"));
        Assert.Equal("/srv/orkeon/tokens", section.CredentialsDirectory);

        section.WithholdRejected = false;
        section.CredentialsDirectory = "";

        Assert.Null(document.GetNode("Orkeon:Tools:Email:Screening"));
        Assert.Null(document.GetNode("Orkeon:Tools:Email:CredentialsDirectory"));

        // A cancelled dialog changes nothing.
        picker.FolderToReturn = null;
        section.BrowseCredentialsDirectoryCommand.Execute(null);
        Assert.Equal("", section.CredentialsDirectory);
    }

    [Fact]
    public void An_unreadable_screening_switch_is_said_on_every_row_until_it_is_set()
    {
        var (section, _, _) = Build("""
            { "Orkeon": { "Tools": { "Email": { "Screening": { "WithholdRejected": "maybe" }, "Accounts": {
              "perso": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read",
                         "Auth": { "PasswordEnvVar": "GMAIL_APP_PASSWORD" } } } } } } }
            """);
        var row = Assert.Single(section.Accounts);
        Assert.Contains(row.Problems, problem => problem.Code == ValidationCodes.EmailScreening);

        section.WithholdRejected = true;

        Assert.False(row.HasProblems);
    }

    [Fact]
    public void The_tab_says_which_file_the_accounts_are_written_to()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var path = Path.Combine(Path.GetTempPath(), "orkeon-user", "appsettings.json");
        var section = new EmailSectionViewModel(() => document, () => { }, settingsPath: () => path);

        Assert.Equal(
            $"Accounts are written to {path}. A team launched on another settings file does not see them.",
            section.SettingsFileLine);

        // The editor's own file: the line follows the location the expert points the save at.
        var global = Path.Combine(Path.GetTempPath(), "orkeon-global", "appsettings.json");
        var tab = new ConfigTabViewModel(
            new StudioServices { SettingsStore = new FakeAppSettingsStore(), Directories = new FakeDirectoryProbe() },
            globalPathOverride: global);
        Assert.Contains(global, tab.Email.SettingsFileLine, StringComparison.Ordinal);
        var raised = new List<string>();
        tab.Email.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        tab.Location.UseCustomPath(path);

        Assert.Contains(path, tab.Email.SettingsFileLine, StringComparison.Ordinal);
        Assert.Contains(nameof(EmailSectionViewModel.SettingsFileLine), raised);
    }

    // ── the two modes ──

    [Fact]
    public void The_novice_sees_the_essentials_and_the_expert_every_field()
    {
        var (section, _, _) = Build(Two);
        var gmail = section.Accounts[0];
        var custom = section.Accounts[1];
        Assert.False(section.IsExpert);

        Assert.False(gmail.ShowExpertFields);
        Assert.False(gmail.ShowHosts);
        Assert.False(gmail.ShowClientId);
        Assert.False(gmail.ShowRecipients);
        // The password is typed in both modes; the variable it is kept in is the expert's to read.
        Assert.True(gmail.ShowPassword);
        // A custom account has no preset: its servers are the novice's to give.
        Assert.True(custom.ShowHosts);
        Assert.False(custom.ShowExpertFields);

        gmail.AuthMethod = "OAuth2";
        Assert.True(gmail.ShowClientId);
        Assert.False(gmail.ShowPassword);
        gmail.AuthMethod = "";

        var raised = new List<string>();
        gmail.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        section.IsExpert = true;

        Assert.True(gmail.ShowExpertFields);
        Assert.True(gmail.ShowHosts);
        Assert.True(gmail.ShowClientId);
        // No key is the raw file's alone: the expert reaches the sending rules of an account that does not send yet.
        Assert.True(gmail.ShowRecipients);
        Assert.False(gmail.ShowSendClosed);
        Assert.True(gmail.ShowPassword);
        Assert.Contains(nameof(EmailAccountRowViewModel.ShowExpertFields), raised);
        Assert.Contains(nameof(EmailAccountRowViewModel.ShowHosts), raised);
    }

    // ── the secrets (STUDIO-68) ──

    [Fact]
    public async Task A_password_typed_on_an_account_that_names_no_variable_writes_the_derived_name_and_keeps_the_value_out_of_the_file()
    {
        const string secret = "abcd efgh ijkl mnop";
        var keys = new FakeApiKeyStore();
        var (section, document, changes) = Build(Unnamed, keys: keys);
        var row = Assert.Single(section.Accounts);
        var password = row.Password;
        Assert.NotNull(password);
        Assert.Equal("EMAIL_PERSO_PASSWORD", password.EnvName);
        Assert.Equal(NotStored, row.PasswordStatus);
        Assert.False(row.PasswordStored);
        Assert.True(row.HasProblems);
        Assert.Null(document.GetNode($"{Accounts}:perso:Auth"));
        var raised = new List<string>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        password.KeyInput = secret;
        Assert.Equal(0, changes());
        await password.StoreAsync();

        Assert.Equal("EMAIL_PERSO_PASSWORD", document.GetString($"{Accounts}:perso:Auth:PasswordEnvVar"));
        Assert.Equal(secret, keys.Saved["EMAIL_PERSO_PASSWORD"]);
        Assert.DoesNotContain(secret, document.ToJson(), StringComparison.Ordinal);
        Assert.Equal(1, changes());
        Assert.Equal("", password.KeyInput);
        Assert.True(row.PasswordStored);
        Assert.Equal(Stored, row.PasswordStatus);
        Assert.Contains(nameof(EmailAccountRowViewModel.PasswordStatus), raised);
        Assert.Contains(nameof(EmailAccountRowViewModel.PasswordStored), raised);
        // The line is the same one, and the expert's field shows the name that was written.
        Assert.Same(password, row.Password);
        Assert.Equal("EMAIL_PERSO_PASSWORD", row.PasswordEnvVar);
        // The account now names its password: the run has nothing left to say of it.
        Assert.False(row.HasProblems);
    }

    [Fact]
    public async Task A_password_typed_on_an_account_that_names_a_variable_changes_the_value_alone()
    {
        var keys = new FakeApiKeyStore();
        keys.Stage("GMAIL_APP_PASSWORD", "the old one");
        var (section, document, changes) = Build(Gmail, keys: keys);
        var row = Assert.Single(section.Accounts);
        var before = document.ToJson();
        Assert.Equal("GMAIL_APP_PASSWORD", row.Password!.EnvName);
        Assert.Equal(Stored, row.PasswordStatus);

        row.Password.KeyInput = "the new one";
        await row.Password.StoreAsync();

        Assert.Equal("the new one", keys.Saved["GMAIL_APP_PASSWORD"]);
        Assert.Equal(["GMAIL_APP_PASSWORD"], keys.Saved.Keys);
        Assert.Equal(before, document.ToJson());
        Assert.Equal(0, changes());
        Assert.Equal(Stored, row.PasswordStatus);
    }

    [Fact]
    public async Task Renaming_an_account_leaves_its_password_in_the_variable_the_file_names()
    {
        var keys = new FakeApiKeyStore();
        var (section, document, _) = Build(Unnamed, keys: keys);
        var row = Assert.Single(section.Accounts);
        // Before a password is kept nothing is written, and the line follows the account's name.
        section.RenameAccountCommand.Execute(row);
        row.RenameText = "home";
        row.ConfirmRenameCommand.Execute(null);
        Assert.Equal("EMAIL_HOME_PASSWORD", row.Password!.EnvName);
        row.Password.KeyInput = "s3cret";
        await row.Password.StoreAsync();

        section.RenameAccountCommand.Execute(row);
        row.RenameText = "work";
        row.ConfirmRenameCommand.Execute(null);

        Assert.Equal("work", row.Name);
        Assert.Equal("EMAIL_HOME_PASSWORD", document.GetString($"{Accounts}:work:Auth:PasswordEnvVar"));
        Assert.Equal("EMAIL_HOME_PASSWORD", row.Password.EnvName);
        Assert.Equal(["EMAIL_HOME_PASSWORD"], keys.Saved.Keys);
        Assert.Equal(Stored, row.PasswordStatus);
    }

    [Fact]
    public async Task Two_accounts_that_spell_the_same_variable_keep_their_passwords_apart()
    {
        var keys = new FakeApiKeyStore();
        var (section, document, _) = Build("""
            { "Orkeon": { "Tools": { "Email": { "Accounts": {
              "a.b": { "Provider": "Gmail", "Address": "one@gmail.com", "Rights": "Read" },
              "a-b": { "Provider": "Gmail", "Address": "two@gmail.com", "Rights": "Read" } } } } } }
            """, keys: keys);
        var first = section.Accounts[0];
        var second = section.Accounts[1];
        var raised = new List<string>();
        second.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        first.Password!.KeyInput = "one";
        await first.Password.StoreAsync();

        // The first took the name: the second's line moves to the next one, for everyone to see.
        Assert.Equal("EMAIL_A_B_PASSWORD_2", second.Password!.EnvName);
        Assert.Contains(nameof(EmailAccountRowViewModel.Password), raised);
        Assert.Equal(NotStored, second.PasswordStatus);

        second.Password.KeyInput = "two";
        await second.Password.StoreAsync();

        Assert.Equal("EMAIL_A_B_PASSWORD", document.GetString($"{Accounts}:a.b:Auth:PasswordEnvVar"));
        Assert.Equal("EMAIL_A_B_PASSWORD_2", document.GetString($"{Accounts}:a-b:Auth:PasswordEnvVar"));
        Assert.Equal(("one", "two"), (keys.Saved["EMAIL_A_B_PASSWORD"], keys.Saved["EMAIL_A_B_PASSWORD_2"]));
    }

    [Fact]
    public async Task The_client_secret_is_asked_of_a_gmail_account_that_signs_in_with_oauth2_and_of_no_other()
    {
        const string secret = "GOCSPX-desktop-secret";
        var keys = new FakeApiKeyStore();
        var (section, document, _) = Build(Two, keys: keys);
        var gmail = section.Accounts[0];
        var custom = section.Accounts[1];
        Assert.Null(gmail.ClientSecret);
        Assert.False(gmail.ShowClientSecret);
        Assert.Null(custom.ClientSecret);
        Assert.NotNull(custom.Password);
        var raised = new List<string>();
        gmail.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        gmail.AuthMethod = "OAuth2";

        // OAuth2 asks for no password, and Google authenticates a desktop application by its secret.
        Assert.Null(gmail.Password);
        Assert.False(gmail.ShowPassword);
        Assert.True(gmail.ShowClientSecret);
        Assert.Equal("EMAIL_PERSO_CLIENT_SECRET", gmail.ClientSecret!.EnvName);
        Assert.Equal(NotStored, gmail.ClientSecretStatus);
        Assert.Contains(nameof(EmailAccountRowViewModel.ClientSecret), raised);
        Assert.Contains(nameof(EmailAccountRowViewModel.ShowClientSecret), raised);

        gmail.ClientSecret.KeyInput = secret;
        await gmail.ClientSecret.StoreAsync();

        Assert.Equal("EMAIL_PERSO_CLIENT_SECRET", document.GetString($"{Accounts}:perso:Auth:ClientSecretEnvVar"));
        Assert.Equal(secret, keys.Saved["EMAIL_PERSO_CLIENT_SECRET"]);
        Assert.DoesNotContain(secret, document.ToJson(), StringComparison.Ordinal);
        Assert.True(gmail.ClientSecretStored);
        Assert.Equal(Stored, gmail.ClientSecretStatus);
        // The password's own variable is untouched: it was named before, and stays named.
        Assert.Equal("GMAIL_APP_PASSWORD", document.GetString($"{Accounts}:perso:Auth:PasswordEnvVar"));

        // Outlook signs in with OAuth2 and no client secret: neither line.
        gmail.Provider = "Outlook";
        Assert.Null(gmail.ClientSecret);
        Assert.False(gmail.ShowClientSecret);
        Assert.Null(gmail.Password);
    }

    [Fact]
    public async Task A_store_that_cannot_keep_the_password_says_so_on_its_line()
    {
        var keys = new FakeApiKeyStore { PersistFailure = new InvalidOperationException("registry access denied") };
        var (section, document, _) = Build(Unnamed, keys: keys);
        var row = Assert.Single(section.Accounts);

        row.Password!.KeyInput = "s3cret";
        await row.Password.StoreAsync();

        Assert.True(row.Password.HasStoreError);
        Assert.Contains("registry access denied", row.Password.StoreError, StringComparison.Ordinal);
        // In place for this session all the same, so the file names it.
        Assert.Equal("EMAIL_PERSO_PASSWORD", document.GetString($"{Accounts}:perso:Auth:PasswordEnvVar"));
        Assert.Equal(Stored, row.PasswordStatus);
    }

    [Fact]
    public void The_password_line_follows_the_variable_the_expert_names_and_only_the_expert_reads_its_name()
    {
        var keys = new FakeApiKeyStore();
        keys.Stage("GMAIL_APP_PASSWORD", "kept");
        // A value left under the derived name by an account removed since: nothing the file names.
        keys.Stage("EMAIL_PERSO_PASSWORD", "left behind");
        var (section, document, _) = Build(Gmail, keys: keys);
        var row = Assert.Single(section.Accounts);
        Assert.Equal(Stored, row.PasswordStatus);
        // The novice reads that it is kept, not where.
        Assert.Null(row.PasswordVariable);

        section.IsExpert = true;
        Assert.Equal("Kept in the variable GMAIL_APP_PASSWORD", row.PasswordVariable);

        row.PasswordEnvVar = "WORK_MAIL_PASSWORD";

        Assert.Equal("WORK_MAIL_PASSWORD", row.Password!.EnvName);
        Assert.Equal(NotStored, row.PasswordStatus);
        Assert.Null(row.PasswordVariable);

        // The field emptied: the key leaves the file, and the line goes back to the derived name —
        // which the run does not read until a password typed here writes it.
        row.PasswordEnvVar = "";

        Assert.Null(document.GetNode($"{Accounts}:perso:Auth"));
        Assert.Equal("EMAIL_PERSO_PASSWORD", row.Password.EnvName);
        Assert.False(row.PasswordStored);
        Assert.Equal(NotStored, row.PasswordStatus);
        Assert.Equal(["EMAIL_PERSO_PASSWORD", "GMAIL_APP_PASSWORD"], keys.Saved.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_settings_screen_keeps_e_mail_secrets_in_the_store_it_keeps_the_model_keys_in()
    {
        var keys = new FakeApiKeyStore();
        var tab = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
            KeyStore = keys,
        });
        tab.SetDocument(AppSettingsDocument.Parse(Unnamed));
        var row = Assert.Single(tab.Email.Accounts);

        row.Password!.KeyInput = "s3cret";
        await row.Password.StoreAsync();

        Assert.Equal("s3cret", keys.Saved["EMAIL_PERSO_PASSWORD"]);
        Assert.True(tab.IsDirty);
        Assert.Contains("\"PasswordEnvVar\": \"EMAIL_PERSO_PASSWORD\"", tab.RawJson, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", tab.RawJson, StringComparison.Ordinal);
    }

    // ── another file, another language ──

    [Fact]
    public void Replacing_the_document_rebuilds_the_rows()
    {
        var tab = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
        });
        Assert.Empty(tab.Email.Accounts);
        Assert.Null(tab.Email.SelectedAccount);

        tab.SetDocument(AppSettingsDocument.Parse(Two));

        Assert.Equal(["perso", "work"], tab.Email.Accounts.Select(account => account.Name));
        Assert.Same(tab.Email.Accounts[0], tab.Email.SelectedAccount);
        Assert.True(tab.Email.HasAccounts);
        Assert.True(tab.Email.Exists);
        Assert.Equal("perso", tab.Email.DefaultAccount);

        tab.NewDocument();

        Assert.Empty(tab.Email.Accounts);
        Assert.Null(tab.Email.SelectedAccount);
        Assert.False(tab.Email.Exists);
    }

    [Fact]
    public void A_language_change_refreshes_what_the_rows_say_without_the_rows_listening()
    {
        var strings = new SwitchableStrings();
        var (section, _, _) = Build(
            """{ "Orkeon": { "Tools": { "Email": { "Accounts": { "perso": { "Provider": "Gmail", "Address": "me@gmail.com", "Auth": { "PasswordEnvVar": "GMAIL_APP_PASSWORD" } } } } } } }""",
            strings);
        var row = Assert.Single(section.Accounts);
        var english = Assert.Single(row.Problems).FriendlyText;
        var raised = new List<string>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        var sectionRaised = new List<string>();
        section.PropertyChanged += (_, e) => sectionRaised.Add(e.PropertyName ?? "");
        // A list rebuilt under a selection loses it: the entries stay, their labels are said again.
        var choices = section.AuthMethodChoices;
        var presetRaised = new List<string>();
        choices[0].PropertyChanged += (_, e) => presetRaised.Add(e.PropertyName ?? "");

        strings.SwitchToFrench();

        Assert.NotEqual(english, Assert.Single(row.Problems).FriendlyText);
        Assert.Equal(SwitchableStrings.FrenchRights, row.Problems[0].FriendlyText);
        Assert.Contains(nameof(EmailAccountRowViewModel.Problems), raised);
        Assert.Equal(SwitchableStrings.FrenchNotStored, row.PasswordStatus);
        Assert.Contains(nameof(EmailAccountRowViewModel.PasswordStatus), raised);
        Assert.Contains(nameof(EmailSectionViewModel.SettingsFileLine), sectionRaised);
        Assert.Same(choices, section.AuthMethodChoices);
        Assert.Equal(SwitchableStrings.FrenchPreset, choices[0].Label);
        Assert.Contains(nameof(EmailChoiceViewModel.Label), presetRaised);
    }

    [Fact]
    public void The_choices_name_the_engines_values_and_offer_the_way_back_to_the_preset()
    {
        var (section, _, _) = Build();
        var preset = EnglishStudioStrings.Instance[StudioStringKeys.MailChoicePreset];

        Assert.Equal(EmailSection.Providers, section.ProviderChoices.Select(choice => choice.Value));
        Assert.Equal(["", .. EmailSection.AuthMethods], section.AuthMethodChoices.Select(choice => choice.Value));
        Assert.Equal(["", .. EmailSection.IncomingProtocols], section.IncomingProtocolChoices.Select(choice => choice.Value));
        Assert.Equal(["", .. EmailSection.OutgoingProtocols], section.OutgoingProtocolChoices.Select(choice => choice.Value));
        Assert.Equal(["", .. EmailSection.Securities], section.SecurityChoices.Select(choice => choice.Value));
        Assert.Equal(preset, section.AuthMethodChoices[0].Label);
        Assert.Equal(preset, section.SecurityChoices[0].Label);
        Assert.All(section.ProviderChoices, choice => Assert.False(string.IsNullOrWhiteSpace(choice.Label)));
        Assert.Equal(
            section.ProviderChoices.Count,
            section.ProviderChoices.Select(choice => choice.Label).Distinct(StringComparer.Ordinal).Count());
    }
}
