using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Drive;
using Orkeon.Studio.Core.Email;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The Drive tab, on the model of the E-mail one: the rows write <c>Orkeon:Tools:Drive</c> in
/// place, an account is added through a box and renamed or removed after a question, the client
/// secret of a Google Drive account goes to the key store and only its variable to the file, each
/// row says what <c>orkeon drive accounts</c> makes of its account, tests it with
/// <c>orkeon drive check</c>, and signs it in with <c>orkeon drive login</c> — a page and a code
/// for OneDrive, an address and a paste field for Google Drive — in the panel the E-mail tab
/// shares. Every process is <see cref="ScriptedOrkeonCli"/>: nothing is spawned.
/// </summary>
public sealed class DriveSectionViewModelTests
{
    private const string SettingsFile = "/home/me/.config/Orkeon/appsettings.json";

    private const string UserCode = "FAKE-C0DE";

    private const string DevicePage = "https://microsoft.com/devicelogin";

    private const string AuthorizationPage = "https://accounts.google.com/o/oauth2/v2/auth?client_id=client-456&state=state-1";

    private const string Redirect = "http://127.0.0.1:53124/?code=auth-code-1&state=state-1";

    private const string NoCode = "The pasted address carries no authorization code: it may be cut short.";

    private const string Two = """
        { "Llm": { "Provider": "Ollama" }, "Orkeon": { "Tools": { "Drive": { "Accounts": {
          "perso": { "Provider": "GoogleDrive", "Rights": "Read, Write", "Root": "Orkeon/agents",
                     "Auth": { "ClientId": "client-456", "ClientSecretEnvVar": "DRIVE_PERSO_CLIENT_SECRET" }, "Note": "mine" },
          "office": { "Provider": "OneDrive", "Rights": "Read", "Auth": { "ClientId": "client-123" } } } } } } }
        """;

    private const string DeviceCodeLine =
        $$"""{"v":2,"seq":1,"ts":"2026-10-17T09:00:00Z","kind":"drive.login.device_code","verification_uri":"{{DevicePage}}","user_code":"{{UserCode}}","expires_in":900}""";

    private const string AuthorizationLine =
        $$"""{"v":2,"seq":1,"ts":"2026-10-17T09:00:00Z","kind":"drive.login.authorization_url","authorization_uri":"{{AuthorizationPage}}"}""";

    private static string CompletedLine(string account) =>
        $$"""{"v":2,"seq":3,"ts":"2026-10-17T09:00:05Z","kind":"drive.login.completed","account":"{{account}}"}""";

    private static string RejectedLine(string message) =>
        $$"""{"v":2,"seq":2,"ts":"2026-10-17T09:00:03Z","kind":"drive.login.redirect_rejected","message":"{{message}}"}""";

    private static string ErrorLine(string code, string message) =>
        $$"""{"v":2,"seq":2,"ts":"2026-10-17T09:00:05Z","kind":"error","code":"{{code}}","message":"{{message}}","recoverable":false}""";

    /// <summary>What <c>drive accounts</c> answers: the engine's order, sorted by name.</summary>
    private static string Accounts(bool officeReady = false, bool persoReady = true) => $$"""
        [{"name":"office","provider":"OneDrive","rights":"Read","root":"/","default":false,"ready":{{(officeReady ? "true" : "false")}},"problem":{{(officeReady ? "null" : "\"Drive account 'office' is not signed in: run `orkeon drive login office`.\"")}}},
         {"name":"perso","provider":"GoogleDrive","rights":"Read, Write","root":"/Orkeon/agents","default":false,"ready":{{(persoReady ? "true" : "false")}},"problem":null}]
        """.ReplaceLineEndings("");

    private sealed class Fixture
    {
        public DriveSectionViewModel Section { get; set; } = null!;
        public AppSettingsDocument Document { get; set; } = null!;
        public ScriptedOrkeonCli Cli { get; init; } = new();
        public FakeApiKeyStore Keys { get; } = new();
        public StubTimeProvider Clock { get; } = new();
        public ManualUiTicker Ticker { get; } = new();
        public RecordingBrowserOpener Browser { get; } = new();
        public InMemoryClipboardService Clipboard { get; } = new();
        public int Changes { get; set; }

        /// <summary>Plays the document's unsaved edits.</summary>
        public bool Dirty { get; set; }

        public DriveAccountRowViewModel Row(string name) => Section.Accounts.Single(row => row.Name == name);

        public int Runs(string subVerb) => Cli.Requests.Count(request => request.Arguments is ["drive", var verb, ..] && verb == subVerb);
    }

    private static Fixture Build(string json = Two, ScriptedOrkeonCli? cli = null, bool withCli = true, bool binaryPresent = true)
    {
        var fixture = new Fixture { Cli = cli ?? new ScriptedOrkeonCli().Answer("drive accounts", 0, Accounts()) };
        fixture.Document = AppSettingsDocument.Parse(json);
        fixture.Section = new DriveSectionViewModel(
            () => fixture.Document,
            () => fixture.Changes++,
            settingsPath: () => SettingsFile,
            keyStore: fixture.Keys,
            engine: withCli
                ? new DriveCliSeams
                {
                    Cli = new DriveCliClient(new OrkeonProcessRunner(
                        fixture.Cli,
                        new OrkeonBinaryLocator(binaryPresent ? FakeExecutableProbe.WithOrkeonInstalled() : new FakeExecutableProbe()))),
                    IsDirty = () => fixture.Dirty,
                    SignIn = new EmailSignInServices
                    {
                        Browser = fixture.Browser,
                        Clipboard = fixture.Clipboard,
                        Ticker = fixture.Ticker,
                        Clock = fixture.Clock,
                    },
                }
                : null);
        return fixture;
    }

    private static Task Guarded(Task task) => task.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

    // ── the rows and the file ──

    [Fact]
    public void Each_account_of_the_file_is_a_row_in_document_order_and_the_first_one_shows()
    {
        var fixture = Build();

        Assert.Equal(["perso", "office"], fixture.Section.Accounts.Select(row => row.Name));
        Assert.Same(fixture.Row("perso"), fixture.Section.SelectedAccount);
        Assert.True(fixture.Section.ShowsAccountTab);
        Assert.Equal(DriveSection.Values.GoogleDrive, fixture.Row("perso").Provider);
        Assert.Equal("Orkeon/agents", fixture.Row("perso").RootLine);
        Assert.Equal("/", fixture.Row("office").RootLine);
        Assert.True(fixture.Row("perso").CanRead);
        Assert.True(fixture.Row("perso").CanWrite);
        Assert.False(fixture.Row("perso").CanShare);
        Assert.Equal($"Accounts are written to {SettingsFile}. A team launched on another settings file does not see them.", fixture.Section.SettingsFileLine);
    }

    [Fact]
    public void An_edit_writes_the_account_in_place_and_keeps_what_studio_does_not_model()
    {
        var fixture = Build();
        var perso = fixture.Row("perso");

        perso.CanShare = true;
        perso.Root = "  Shared/agents ";
        perso.Recipients.Add(new DriveRecipientRowViewModel(perso, ""));
        perso.Recipients[0].Pattern = "*@example.com";
        perso.AllowPublicLinks = true;
        perso.MaxUploadMegabytes = "250";

        var written = fixture.Document.Drive.GetAccount("perso")!;
        Assert.Equal(DriveRights.Read | DriveRights.Write | DriveRights.Share, written.Rights);
        Assert.Equal("Shared/agents", written.Root);
        Assert.Equal(["*@example.com"], written.AllowedRecipients);
        Assert.True(written.AllowPublicLinks);
        Assert.Equal(250, written.MaxUploadMegabytes);
        Assert.Equal("mine", fixture.Document.GetString("Orkeon:Tools:Drive:Accounts:perso:Note"));
        Assert.True(fixture.Changes > 0);

        // Off is the engine's default: turning the public links off again removes the key.
        perso.AllowPublicLinks = false;
        Assert.Null(fixture.Document.GetNode("Orkeon:Tools:Drive:Accounts:perso:Share:AllowPublicLinks"));
    }

    [Fact]
    public void A_number_field_that_holds_no_whole_number_says_so_and_is_not_written()
    {
        var fixture = Build();
        var perso = fixture.Row("perso");
        perso.MaxUploadMegabytes = "500";

        perso.MaxUploadMegabytes = "lots";

        Assert.True(perso.HasUnreadNumber);
        Assert.Equal(500, fixture.Document.Drive.GetAccount("perso")!.MaxUploadMegabytes);

        perso.MaxUploadMegabytes = "";
        Assert.False(perso.HasUnreadNumber);
        Assert.Null(fixture.Document.GetNode("Orkeon:Tools:Drive:Accounts:perso:MaxUploadMegabytes"));
    }

    [Fact]
    public void An_account_that_may_share_with_nobody_says_so_and_a_line_that_is_no_pattern_says_it_on_itself()
    {
        var fixture = Build();
        var office = fixture.Row("office");
        Assert.False(office.ShowRecipients);

        office.CanShare = true;

        Assert.True(office.ShowRecipients);
        Assert.True(office.ShowShareClosed);

        office.AddRecipientCommand.Execute(null);
        office.Recipients[0].Pattern = "nobody";
        Assert.True(office.Recipients[0].IsInvalid);
        Assert.False(office.ShowShareClosed);
        Assert.Contains(office.Problems, problem => problem.Code == ValidationCodes.DriveShare);

        office.RemoveRecipientCommand.Execute(office.Recipients[0]);
        Assert.Empty(fixture.Document.Drive.GetAccount("office")!.AllowedRecipients);
    }

    [Fact]
    public void What_the_run_will_say_follows_every_edit()
    {
        var fixture = Build();
        var office = fixture.Row("office");
        Assert.False(office.HasProblems);

        office.CanRead = false;

        Assert.Equal([ValidationCodes.DriveRights], office.Problems.Select(problem => problem.Code));

        office.CanRead = true;
        office.Tenant = "my tenant";
        Assert.Equal([ValidationCodes.DriveAuth], office.Problems.Select(problem => problem.Code));
    }

    [Fact]
    public void The_expert_sees_the_variable_of_a_google_secret_and_the_tenant_of_a_onedrive_account_and_the_novice_neither()
    {
        var fixture = Build();

        Assert.False(fixture.Row("perso").ShowClientSecretEnvVar);
        Assert.False(fixture.Row("office").ShowTenant);

        fixture.Section.IsExpert = true;

        Assert.True(fixture.Row("perso").ShowClientSecretEnvVar);
        Assert.False(fixture.Row("perso").ShowTenant);
        Assert.True(fixture.Row("office").ShowTenant);
        Assert.False(fixture.Row("office").ShowClientSecretEnvVar);
        Assert.True(fixture.Row("office").ShowRecipients);
    }

    // ── the box that adds an account ──

    [Fact]
    public void Adding_writes_nothing_until_the_name_is_usable_then_the_account_with_its_drive()
    {
        var fixture = Build();
        fixture.Section.BeginAddCommand.Execute(null);
        Assert.True(fixture.Section.IsAdding);
        Assert.Equal(DriveSection.Values.GoogleDrive, fixture.Section.NewAccountProvider);
        Assert.False(fixture.Section.ConfirmAddCommand.CanExecute(null));

        fixture.Section.NewAccountName = "PERSO";
        Assert.True(fixture.Section.HasAddRefusal);
        Assert.False(fixture.Section.ConfirmAddCommand.CanExecute(null));

        fixture.Section.NewAccountName = "-bad";
        Assert.True(fixture.Section.HasAddRefusal);

        fixture.Section.NewAccountName = " team ";
        fixture.Section.NewAccountProvider = DriveSection.Values.OneDrive;
        Assert.False(fixture.Section.HasAddRefusal);
        fixture.Section.ConfirmAddCommand.Execute(null);

        Assert.False(fixture.Section.IsAdding);
        Assert.Equal(["perso", "office", "team"], fixture.Document.Drive.AccountNames);
        Assert.Equal(DriveSection.Values.OneDrive, fixture.Document.Drive.GetAccount("team")!.Provider);
        Assert.Same(fixture.Row("team"), fixture.Section.SelectedAccount);
        // Not usable yet, and said: it grants no right and names no client.
        Assert.Equal([ValidationCodes.DriveRights, ValidationCodes.DriveAuth], fixture.Row("team").Problems.Select(problem => problem.Code));
    }

    [Fact]
    public void Renaming_moves_the_account_and_the_default_that_named_it()
    {
        var fixture = Build();
        fixture.Section.DefaultAccount = "office";
        var office = fixture.Row("office");

        fixture.Section.RenameAccountCommand.Execute(office);
        Assert.True(office.IsRenaming);
        office.RenameText = "Perso";
        Assert.True(office.HasRenameRefusal);
        office.RenameText = "work";
        office.ConfirmRenameCommand.Execute(null);

        Assert.Equal(["perso", "work"], fixture.Document.Drive.AccountNames);
        Assert.Equal("work", fixture.Section.DefaultAccount);
        Assert.Equal("work", office.Name);
        Assert.False(office.IsRenaming);
    }

    [Fact]
    public void Removing_asks_first_then_takes_the_account_and_shows_a_neighbour()
    {
        var fixture = Build();
        var perso = fixture.Row("perso");

        fixture.Section.RemoveAccountCommand.Execute(perso);
        Assert.True(perso.IsConfirmingRemove);
        Assert.Equal(2, fixture.Document.Drive.AccountNames.Count);

        perso.ConfirmRemoveCommand.Execute(null);

        Assert.Equal(["office"], fixture.Document.Drive.AccountNames);
        Assert.Same(fixture.Row("office"), fixture.Section.SelectedAccount);

        fixture.Section.RemoveAccountCommand.Execute(fixture.Row("office"));
        fixture.Row("office").ConfirmRemoveCommand.Execute(null);
        Assert.Null(fixture.Document.GetNode("Orkeon:Tools:Drive"));
        Assert.False(fixture.Section.HasSelectedAccount);
    }

    [Fact]
    public void The_settings_of_the_section_write_their_keys_and_remove_them_when_cleared()
    {
        var fixture = Build();

        fixture.Section.WithholdRejected = true;
        fixture.Section.CredentialsDirectory = "/srv/tokens";
        Assert.True(fixture.Document.Drive.WithholdRejected);
        Assert.Equal("/srv/tokens", fixture.Document.Drive.CredentialsDirectory);

        fixture.Section.WithholdRejected = false;
        fixture.Section.CredentialsDirectory = "";
        Assert.Null(fixture.Document.GetNode("Orkeon:Tools:Drive:Screening"));
        Assert.Null(fixture.Document.GetNode("Orkeon:Tools:Drive:CredentialsDirectory"));
    }

    // ── the client secret ──

    [Fact]
    public void A_google_account_types_its_client_secret_here_and_the_file_holds_only_the_name_of_its_variable()
    {
        var fixture = Build("""
            { "Orkeon": { "Tools": { "Drive": { "Accounts": {
              "perso": { "Provider": "GoogleDrive", "Rights": "Read", "Auth": { "ClientId": "client-456" } },
              "office": { "Provider": "OneDrive", "Rights": "Read", "Auth": { "ClientId": "client-123" } } } } } } }
            """);
        var perso = fixture.Row("perso");
        Assert.Null(fixture.Row("office").ClientSecret);
        Assert.Equal("DRIVE_PERSO_CLIENT_SECRET", perso.ClientSecret!.EnvName);
        Assert.False(perso.ClientSecretStored);
        Assert.Contains(perso.Problems, problem => problem.Code == ValidationCodes.DriveAuth);

        perso.ClientSecret.KeyInput = "GOCSPX-secret";
        perso.ClientSecret.StoreCommand.Execute(null);

        Assert.Equal("GOCSPX-secret", fixture.Keys.Saved["DRIVE_PERSO_CLIENT_SECRET"]);
        Assert.Equal("DRIVE_PERSO_CLIENT_SECRET", fixture.Document.Drive.GetAccount("perso")!.ClientSecretEnvVar);
        Assert.DoesNotContain("GOCSPX", fixture.Document.ToJson(), StringComparison.Ordinal);
        Assert.True(perso.ClientSecretStored);
        Assert.False(perso.HasProblems);
    }

    [Fact]
    public void Turning_an_account_to_google_drive_offers_the_secret_line_and_turning_it_back_takes_it_away()
    {
        var fixture = Build();
        var office = fixture.Row("office");

        office.Provider = DriveSection.Values.GoogleDrive;
        Assert.True(office.ShowClientSecret);
        Assert.Equal("DRIVE_OFFICE_CLIENT_SECRET", office.ClientSecret!.EnvName);

        office.Provider = DriveSection.Values.OneDrive;
        Assert.False(office.ShowClientSecret);
    }

    // ── what the engine makes of each account ──

    [Fact]
    public async Task Arriving_reads_the_states_of_the_saved_file_and_pairs_them_by_name()
    {
        var fixture = Build();

        await Guarded(fixture.Section.RefreshStatesAsync());

        Assert.Equal(["drive", "accounts", "--json", "--settings", SettingsFile], Assert.Single(fixture.Cli.Requests).Arguments);
        Assert.True(fixture.Row("perso").IsReady);
        Assert.Equal("Ready", fixture.Row("perso").State);
        Assert.Equal(EmailAccountReadiness.NotReady, fixture.Row("office").StateKind);
        Assert.Equal("Drive account 'office' is not signed in: run `orkeon drive login office`.", fixture.Row("office").StateDetail);
    }

    [Fact]
    public async Task An_account_the_engine_set_aside_says_so_and_a_missing_cli_makes_every_state_unknown()
    {
        var setAside = Build(cli: new ScriptedOrkeonCli().Answer("drive accounts", 0,
            """[{"name":"perso","provider":"?","default":false,"ready":false,"problem":"Provider is required"}]"""));
        var missing = Build(binaryPresent: false);

        await Guarded(setAside.Section.RefreshStatesAsync());
        await Guarded(missing.Section.RefreshStatesAsync());

        Assert.Equal(EmailAccountReadiness.SetAside, setAside.Row("perso").StateKind);
        Assert.Null(setAside.Row("perso").StateDetail);
        Assert.Equal(EmailAccountReadiness.None, setAside.Row("office").StateKind);
        Assert.Equal(EmailAccountReadiness.Unknown, missing.Row("perso").StateKind);
        Assert.StartsWith("Unknown — ", missing.Row("perso").State, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_section_built_without_a_client_reads_nothing_tests_nothing_and_signs_nothing_in()
    {
        var fixture = Build(withCli: false);

        await Guarded(fixture.Section.RefreshStatesAsync());

        Assert.Empty(fixture.Cli.Requests);
        Assert.False(fixture.Row("perso").TestCommand.CanExecute(null));
        Assert.False(fixture.Row("perso").SignInCommand.CanExecute(null));
    }

    // ── the connection test ──

    [Fact]
    public async Task Testing_runs_the_check_on_the_saved_file_and_shows_the_engines_sentence()
    {
        const string Summary = "Drive account 'perso' is reachable: root folder /Orkeon/agents ('agents') holds 3 item(s).";
        var cli = new ScriptedOrkeonCli()
            .Answer("drive accounts", 0, Accounts())
            .Answer("drive check", 0, $$"""{"v":1,"seq":1,"kind":"drive.check.completed","account":"perso","root_path":"/Orkeon/agents","root_name":"agents","items":3,"more":false,"summary":"{{Summary}}"}""");
        var fixture = Build(cli: cli);
        var perso = fixture.Row("perso");

        await Guarded(perso.TestCommand.ExecuteAsync());

        Assert.Equal(["drive", "check", "perso", "--events", "jsonl", "--settings", SettingsFile], fixture.Cli.Requests[^1].Arguments);
        Assert.True(perso.LastCheckReachable);
        Assert.Equal("Reachable", perso.LastCheckHeadline);
        Assert.Equal(Summary, perso.LastCheckDetail);

        // The verdict was of the account as it stood: an edit clears it.
        perso.Root = "Elsewhere";
        Assert.False(perso.HasLastCheck);
    }

    [Fact]
    public async Task A_test_refused_for_want_of_the_client_secret_says_where_to_type_it()
    {
        var cli = new ScriptedOrkeonCli()
            .Answer("drive accounts", 0, Accounts())
            .Answer("drive check", 1, ErrorLine(EmailCheckCodes.CredentialMissing, "The client secret of drive account 'perso' is not set."));
        var fixture = Build(cli: cli);
        var perso = fixture.Row("perso");

        await Guarded(perso.TestCommand.ExecuteAsync());

        Assert.Equal("Store the client secret first", perso.LastCheckHeadline);
        Assert.Equal("Type it on the Sign-in tab and save it; the test passes once it is stored.", perso.LastCheckHint);
        Assert.Equal("The client secret of drive account 'perso' is not set.", perso.LastCheckDetail);
    }

    [Fact]
    public void While_the_document_has_unsaved_edits_nothing_that_reads_the_file_can_start()
    {
        var fixture = Build();
        var office = fixture.Row("office");

        fixture.Dirty = true;
        fixture.Section.RefreshSavedFile();

        Assert.True(fixture.Section.IsStateOfSavedFile);
        Assert.False(office.TestCommand.CanExecute(null));
        Assert.False(office.SignInCommand.CanExecute(null));
        Assert.False(office.SignOutCommand.CanExecute(null));
        office.SignInCommand.Execute(null);
        Assert.Null(office.SignIn);
    }

    // ── signing in and out ──

    [Fact]
    public void Every_account_of_a_known_drive_is_offered_the_sign_in_and_one_without_a_drive_is_not()
    {
        var fixture = Build();
        Assert.True(fixture.Row("perso").ShowSignIn);
        Assert.True(fixture.Row("office").ShowSignIn);

        fixture.Row("office").Provider = null;

        Assert.False(fixture.Row("office").ShowSignIn);
        Assert.False(fixture.Row("office").SignInCommand.CanExecute(null));
    }

    [Fact]
    public void A_onedrive_sign_in_shows_the_page_and_the_code_and_counts_the_code_down()
    {
        var cli = new ScriptedOrkeonCli().Answer("drive accounts", 0, Accounts()).Converse("drive login", [DeviceCodeLine], _ => []);
        var fixture = Build(cli: cli);

        fixture.Row("office").SignInCommand.Execute(null);
        var signIn = fixture.Row("office").SignIn!;

        Assert.Equal(["drive", "login", "office", "--events", "jsonl", "--settings", SettingsFile], Assert.Single(fixture.Cli.Requests).Arguments);
        Assert.True(fixture.Section.IsSigningIn);
        Assert.Equal(EmailSignInPhase.DeviceCode, signIn.Phase);
        Assert.Equal(UserCode, signIn.UserCode);
        Assert.Equal(DevicePage, signIn.Address);
        Assert.Equal("The code expires in 15:00", signIn.ExpiresInText);
        Assert.True(fixture.Ticker.IsRunning);
        Assert.Empty(fixture.Browser.Opened);

        fixture.Clock.Now += TimeSpan.FromMinutes(15);
        fixture.Ticker.Tick();

        Assert.True(signIn.IsExpired);
        Assert.True(signIn.CanRestart);
        Assert.Equal(1, fixture.Cli.StoppedConversations);
    }

    [Fact]
    public async Task A_google_sign_in_shows_the_address_takes_the_pasted_one_says_why_one_is_rejected_and_reads_the_states_once_signed_in()
    {
        var cli = new ScriptedOrkeonCli().Answer("drive accounts", 0, Accounts(persoReady: false));
        cli.Converse("drive login", [AuthorizationLine], line =>
        {
            if (line != Redirect)
                return [RejectedLine(NoCode)];

            cli.Answer("drive accounts", 0, Accounts(persoReady: true));
            return [CompletedLine("perso")];
        });
        var fixture = Build(cli: cli);
        await Guarded(fixture.Section.RefreshStatesAsync());
        Assert.False(fixture.Row("perso").IsReady);

        fixture.Row("perso").SignInCommand.Execute(null);
        var signIn = fixture.Row("perso").SignIn!;

        Assert.True(signIn.IsAuthorizationUrl);
        Assert.Equal(AuthorizationPage, signIn.Address);
        Assert.True(signIn.CanOpen);
        signIn.CopyLinkCommand.Execute(null);
        Assert.Equal(AuthorizationPage, fixture.Clipboard.LastText);
        signIn.OpenCommand.Execute(null);
        Assert.Equal([AuthorizationPage], fixture.Browser.Opened.Select(uri => uri.AbsoluteUri));

        signIn.PastedRedirect = "http://127.0.0.1:53124/?state=state-1";
        signIn.SubmitRedirectCommand.Execute(null);
        Assert.True(signIn.RedirectRejected);
        Assert.Equal(NoCode, signIn.RedirectRejectedDetail);
        Assert.Equal("http://127.0.0.1:53124/?state=state-1", signIn.PastedRedirect);
        Assert.True(signIn.IsWaiting);

        signIn.PastedRedirect = Redirect;
        signIn.SubmitRedirectCommand.Execute(null);
        await Guarded(signIn.Ended);

        Assert.True(signIn.IsCompleted);
        Assert.True(fixture.Row("perso").IsReady);
    }

    [Fact]
    public async Task A_refused_sign_in_shows_the_verbs_sentence_and_is_not_tried_again()
    {
        var cli = new ScriptedOrkeonCli()
            .Answer("drive accounts", 0, Accounts())
            .Answer("drive login", 1, ErrorLine(EmailCheckCodes.CredentialMissing, "The client secret of drive account 'perso' is not set."));
        var fixture = Build(cli: cli);

        fixture.Row("perso").SignInCommand.Execute(null);
        var signIn = fixture.Row("perso").SignIn!;
        await Guarded(signIn.Ended);

        Assert.True(signIn.IsFailed);
        Assert.Equal("The client secret of drive account 'perso' is not set.", signIn.FailureDetail);
        Assert.False(signIn.CanRestart);
        Assert.Equal(1, fixture.Runs("login"));
    }

    [Fact]
    public async Task Signing_out_asks_first_then_forgets_the_tokens_and_reads_the_states_again()
    {
        var fixture = Build(cli: new ScriptedOrkeonCli().Answer("drive accounts", 0, Accounts()).Answer("drive logout", 0));
        var office = fixture.Row("office");

        office.SignOutCommand.Execute(null);
        Assert.True(office.IsConfirmingSignOut);
        Assert.Equal("Forget the stored tokens of office? The account will need a new sign-in.", office.SignOutConfirmText);
        Assert.Equal(0, fixture.Runs("logout"));

        await Guarded(office.ConfirmSignOutCommand.ExecuteAsync());

        Assert.Equal(["drive", "logout", "office", "--settings", SettingsFile], fixture.Cli.Requests.Single(r => r.Arguments[1] == "logout").Arguments);
        Assert.Equal(1, fixture.Runs("accounts"));
        Assert.False(office.HasSignOutFailure);
    }

    [Fact]
    public void Stopping_the_tab_stops_a_sign_in_that_waits_and_takes_its_panel_away()
    {
        var cli = new ScriptedOrkeonCli().Answer("drive accounts", 0, Accounts()).Converse("drive login", [DeviceCodeLine], _ => []);
        var fixture = Build(cli: cli);
        fixture.Row("office").SignInCommand.Execute(null);
        Assert.Equal(1, fixture.Cli.LiveConversations);

        fixture.Section.StopActivity();

        Assert.False(fixture.Row("office").HasSignIn);
        Assert.False(fixture.Section.IsSigningIn);
        Assert.False(fixture.Ticker.IsRunning);
        Assert.Equal(1, fixture.Cli.StoppedConversations);
    }

    // ── the settings screen ──

    [Fact]
    public async Task The_drive_tab_reads_the_states_on_arrival_and_stops_its_activity_when_left()
    {
        var cli = new ScriptedOrkeonCli().Answer("drive accounts", 0, Accounts()).Converse("drive login", [DeviceCodeLine], _ => []);
        var config = new ConfigTabViewModel(
            new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                KeyStore = new FakeApiKeyStore(),
                DriveCli = new DriveCliClient(new OrkeonProcessRunner(cli, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled()))),
                Clipboard = new InMemoryClipboardService(),
            },
            globalPathOverride: SettingsFile);
        config.SetDocument(AppSettingsDocument.Parse(Two), SettingsFile);
        var screen = new SettingsScreenViewModel(
            config,
            new ModelProfilesViewModel(new InMemoryModelProfileStore(), config.Llm),
            new UiModeViewModel(UiModeViewModel.Novice));

        Assert.False(screen.IsDriveTab);
        screen.ShowDriveCommand.Execute(null);
        Assert.True(screen.IsDriveTab);
        await Polling.WaitUntilAsync(() => config.Drive.Accounts.All(row => row.HasState));
        Assert.True(config.Drive.Accounts.Single(row => row.Name == "perso").IsReady);

        config.Drive.Accounts.Single(row => row.Name == "office").SignInCommand.Execute(null);
        Assert.True(config.Drive.IsSigningIn);

        screen.ShowMailsCommand.Execute(null);

        Assert.False(config.Drive.IsSigningIn);
        Assert.Equal(1, cli.StoppedConversations);
    }

    [Fact]
    public void The_mode_switch_reaches_the_drive_tab_and_a_new_document_rebuilds_its_rows()
    {
        var config = new ConfigTabViewModel(
            new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                KeyStore = new FakeApiKeyStore(),
            },
            globalPathOverride: SettingsFile);
        config.SetDocument(AppSettingsDocument.Parse(Two), SettingsFile);
        var mode = new UiModeViewModel(UiModeViewModel.Novice);
        _ = new SettingsScreenViewModel(config, new ModelProfilesViewModel(new InMemoryModelProfileStore(), config.Llm), mode);

        Assert.False(config.Drive.IsExpert);
        mode.Mode = UiModeViewModel.Expert;
        Assert.True(config.Drive.IsExpert);

        config.SetDocument(AppSettingsDocument.CreateEmpty(), SettingsFile);
        Assert.False(config.Drive.HasAccounts);
        Assert.False(config.Drive.HasSelectedAccount);
    }
}
