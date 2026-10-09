using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Email;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-70 — an OAuth e-mail account signs in and out from the E-mail tab. Studio runs no OAuth
/// flow: <c>orkeon email login --events jsonl</c> does, and the panel shows what the person has to
/// do meanwhile — the page and the code of a device sign-in, counted down; the link of a browser
/// sign-in, and a field for the address the browser ended on. Nothing is opened but on a click,
/// and nothing but an <c>https</c> address; a sign-in that ends well reads the states again; a
/// refusal shows as printed and is never tried again; cancelling, leaving the tab or closing the
/// window stops the child. Every process is <see cref="ScriptedOrkeonCli"/>: nothing is spawned.
/// </summary>
public sealed class EmailSignInViewModelTests
{
    private const string SettingsFile = "/home/me/.config/Orkeon/appsettings.json";

    // A made-up code: a real one is a secret while it lives.
    private const string UserCode = "FAKE-C0DE";

    private const string DevicePage = "https://microsoft.com/devicelogin";

    private const string AuthorizationPage =
        "https://accounts.google.com/o/oauth2/v2/auth?client_id=client-456&redirect_uri=http%3A%2F%2F127.0.0.1%3A53124%2F&state=state-1";

    private const string Redirect = "http://127.0.0.1:53124/?code=auth-code-1&state=state-1";

    // The browser's address without its code, as a careless copy leaves it.
    private const string CutShort = "http://127.0.0.1:53124/?state=state-1";

    private const string NoCode = "The pasted address carries no authorization code: it may be cut short.";

    private const string NotAnAddress = "The pasted text is not an address.";

    private const string NoRefreshToken = "The provider issued no refresh token, so the sign-in would expire within the hour.";

    private const string CodeExpired = "The sign-in code expired before the sign-in completed; run the login again.";

    private const string Three = """
        { "Orkeon": { "Tools": { "Email": { "Accounts": {
          "hotmail": { "Provider": "Outlook", "Address": "me@hotmail.com", "Rights": "Read",
                       "Auth": { "ClientId": "client-123" } },
          "perso": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read",
                     "Auth": { "ClientId": "client-456", "ClientSecretEnvVar": "GMAIL_CLIENT_SECRET" } },
          "work": { "Provider": "Custom", "Address": "me@example.com", "Rights": "Read",
                    "Incoming": { "Host": "imap.example.com" },
                    "Auth": { "PasswordEnvVar": "WORK_MAIL_PASSWORD" } } } } } } }
        """;

    private const string DeviceCodeLine =
        $$"""{"v":2,"seq":1,"ts":"2026-10-07T09:00:00Z","kind":"email.login.device_code","verification_uri":"{{DevicePage}}","user_code":"{{UserCode}}","expires_in":900}""";

    private static string AuthorizationLine(string address) =>
        $$"""{"v":2,"seq":1,"ts":"2026-10-07T09:00:00Z","kind":"email.login.authorization_url","authorization_uri":{{System.Text.Json.JsonSerializer.Serialize(address)}}}""";

    private static string CompletedLine(string account) =>
        $$"""{"v":2,"seq":2,"ts":"2026-10-07T09:00:05Z","kind":"email.login.completed","account":"{{account}}"}""";

    private static string RejectedLine(string message) =>
        $$"""{"v":2,"seq":2,"ts":"2026-10-07T09:00:03Z","kind":"email.login.redirect_rejected","message":"{{message}}"}""";

    private static string ErrorLine(string code, string message) =>
        $$"""{"v":2,"seq":2,"ts":"2026-10-07T09:00:05Z","kind":"error","code":"{{code}}","message":"{{message}}","recoverable":false}""";

    /// <summary>What <c>email accounts</c> answers: the two OAuth accounts signed in or not, the password one ready.</summary>
    private static string Accounts(bool hotmailReady = false, bool persoReady = false) => $$"""
        [{"name":"hotmail","address":"me@hotmail.com","provider":"Outlook","reads":"Graph","sends":"Graph","rights":"Read","auth":"OAuth2","default":false,"ready":{{(hotmailReady ? "true" : "false")}},"problem":{{(hotmailReady ? "null" : "\"E-mail account 'hotmail' is not signed in: run `orkeon email login hotmail`.\"")}}},
         {"name":"perso","address":"me@gmail.com","provider":"Gmail","reads":"Imap","sends":"Smtp","rights":"Read","auth":"OAuth2","default":false,"ready":{{(persoReady ? "true" : "false")}},"problem":{{(persoReady ? "null" : "\"E-mail account 'perso' is not signed in: run `orkeon email login perso`.\"")}}},
         {"name":"work","address":"me@example.com","provider":"Custom","reads":"Imap","sends":null,"rights":"Read","auth":"Password","default":false,"ready":true,"problem":null}]
        """.ReplaceLineEndings("");

    /// <summary>The seams of one test: the section over a scripted CLI, a clock and a beat the test moves.</summary>
    private sealed class Fixture
    {
        public EmailSectionViewModel Section { get; set; } = null!;
        public ScriptedOrkeonCli Cli { get; init; } = null!;
        public StubTimeProvider Clock { get; } = new();
        public ManualUiTicker Ticker { get; } = new();
        public RecordingBrowserOpener Browser { get; } = new();
        public InMemoryClipboardService Clipboard { get; } = new();

        /// <summary>Plays the document's unsaved edits.</summary>
        public bool Dirty { get; set; }

        public EmailAccountRowViewModel Row(string name) => Section.Accounts.Single(row => row.Name == name);

        public int Runs(string subVerb) => Cli.Requests.Count(request => request.Arguments is ["email", var verb, ..] && verb == subVerb);

        /// <summary>Clicks "Sign in" on <paramref name="name"/> and returns the panel that opened.</summary>
        public EmailSignInViewModel SignIn(string name)
        {
            Row(name).SignInCommand.Execute(null);
            return Row(name).SignIn ?? throw new InvalidOperationException($"No sign-in panel opened for '{name}'.");
        }
    }

    private static OrkeonProcessRunner Runner(ScriptedOrkeonCli cli) =>
        new(cli, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled()));

    /// <summary>A CLI whose sign-in is the device one: it says the page and the code, then waits.</summary>
    private static ScriptedOrkeonCli DeviceCli() =>
        new ScriptedOrkeonCli().Answer("email accounts", 0, Accounts()).Converse("email login", [DeviceCodeLine], _ => []);

    /// <summary>A CLI whose sign-in is the browser one: it says the address, and completes when the redirect is pasted.</summary>
    private static ScriptedOrkeonCli BrowserCli(string address = AuthorizationPage)
    {
        var cli = new ScriptedOrkeonCli().Answer("email accounts", 0, Accounts());
        return cli.Converse("email login", [AuthorizationLine(address)], line =>
        {
            if (line != Redirect)
                return [];

            // The tokens are stored: from here on the engine finds the account ready.
            cli.Answer("email accounts", 0, Accounts(persoReady: true));
            return [CompletedLine("perso")];
        });
    }

    /// <summary>A browser sign-in that judges what is pasted, as the verb does: the redirect completes it, anything else is rejected with its reason.</summary>
    private static ScriptedOrkeonCli JudgingBrowserCli()
    {
        var cli = new ScriptedOrkeonCli().Answer("email accounts", 0, Accounts());
        return cli.Converse("email login", [AuthorizationLine(AuthorizationPage)], line =>
        {
            if (line != Redirect)
                return [RejectedLine(line.StartsWith("http", StringComparison.Ordinal) ? NoCode : NotAnAddress)];

            cli.Answer("email accounts", 0, Accounts(persoReady: true));
            return [CompletedLine("perso")];
        });
    }

    private static Fixture Build(
        ScriptedOrkeonCli cli, IStudioStrings? strings = null, bool withBrowser = true, IUiDispatcher? dispatcher = null)
    {
        var document = AppSettingsDocument.Parse(Three);
        var fixture = new Fixture { Cli = cli };
        fixture.Section = new EmailSectionViewModel(
            () => document,
            () => { },
            strings,
            () => SettingsFile,
            keyStore: new FakeApiKeyStore(),
            engine: new EmailCliSeams
            {
                Cli = new EmailCliClient(Runner(cli)),
                IsDirty = () => fixture.Dirty,
                Dispatcher = dispatcher,
                SignIn = new EmailSignInServices
                {
                    Browser = withBrowser ? fixture.Browser : null,
                    Clipboard = fixture.Clipboard,
                    Ticker = fixture.Ticker,
                    Clock = fixture.Clock,
                },
            });
        return fixture;
    }

    /// <summary>The whole settings screen over the scripted CLI and an in-memory settings file.</summary>
    private static (SettingsScreenViewModel Screen, ConfigTabViewModel Config, FakeAppSettingsStore Store) BuildScreen(ScriptedOrkeonCli cli)
    {
        var store = new FakeAppSettingsStore();
        var config = new ConfigTabViewModel(
            new StudioServices
            {
                SettingsStore = store,
                Directories = new FakeDirectoryProbe(),
                KeyStore = new FakeApiKeyStore(),
                EmailCli = new EmailCliClient(Runner(cli)),
                Clipboard = new InMemoryClipboardService(),
            },
            globalPathOverride: SettingsFile);
        config.SetDocument(AppSettingsDocument.Parse(Three), SettingsFile);
        var screen = new SettingsScreenViewModel(
            config,
            new ModelProfilesViewModel(new InMemoryModelProfileStore(), config.Llm),
            new UiModeViewModel(UiModeViewModel.Novice));
        return (screen, config, store);
    }

    private static Task Guarded(Task task) => task.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

    // ── who may sign in, and when ──

    [Fact]
    public void Signing_in_and_out_is_offered_to_an_account_that_signs_in_with_oauth_and_to_no_other()
    {
        var fixture = Build(DeviceCli());

        // Outlook is OAuth2 by its preset, the Gmail account by its client id; the custom one has a password.
        Assert.True(fixture.Row("hotmail").ShowSignIn);
        Assert.True(fixture.Row("perso").ShowSignIn);
        Assert.False(fixture.Row("work").ShowSignIn);

        Assert.True(fixture.Row("hotmail").SignInCommand.CanExecute(null));
        Assert.True(fixture.Row("hotmail").SignOutCommand.CanExecute(null));
        Assert.False(fixture.Row("work").SignInCommand.CanExecute(null));
        Assert.False(fixture.Row("work").SignOutCommand.CanExecute(null));

        fixture.Row("work").SignInCommand.Execute(null);
        Assert.Null(fixture.Row("work").SignIn);
        Assert.Equal(0, fixture.Runs("login"));
    }

    [Fact]
    public void An_account_that_turns_to_oauth_in_the_form_is_offered_the_sign_in_and_one_that_leaves_it_loses_it()
    {
        var fixture = Build(DeviceCli());
        var perso = fixture.Row("perso");
        var raised = new List<string>();
        perso.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        perso.AuthMethod = "Password";

        Assert.False(perso.ShowSignIn);
        Assert.Contains(nameof(EmailAccountRowViewModel.ShowSignIn), raised);
    }

    [Fact]
    public void While_the_document_has_unsaved_edits_neither_the_sign_in_nor_the_sign_out_can_start()
    {
        // The verb reads the file as saved: an account edited and not saved would sign another one in.
        var fixture = Build(DeviceCli());
        var hotmail = fixture.Row("hotmail");

        fixture.Dirty = true;
        fixture.Section.RefreshSavedFile();

        Assert.True(hotmail.ShowSignIn);
        Assert.False(hotmail.SignInCommand.CanExecute(null));
        Assert.False(hotmail.SignOutCommand.CanExecute(null));
        hotmail.SignInCommand.Execute(null);
        Assert.Null(hotmail.SignIn);
        Assert.Equal(0, fixture.Runs("login"));

        fixture.Dirty = false;
        var woken = 0;
        hotmail.SignInCommand.CanExecuteChanged += (_, _) => woken++;
        fixture.Section.RefreshSavedFile();

        Assert.True(hotmail.SignInCommand.CanExecute(null));
        Assert.True(woken > 0);
    }

    [Fact]
    public void A_section_built_without_a_client_offers_no_sign_in_to_start()
    {
        var document = AppSettingsDocument.Parse(Three);
        var section = new EmailSectionViewModel(() => document, () => { }, settingsPath: () => SettingsFile, keyStore: new FakeApiKeyStore());

        var hotmail = section.Accounts.Single(row => row.Name == "hotmail");

        Assert.False(hotmail.SignInCommand.CanExecute(null));
        Assert.False(hotmail.SignOutCommand.CanExecute(null));
    }

    // ── the device sign-in ──

    [Fact]
    public void The_device_sign_in_runs_the_verb_in_events_on_the_settings_file_and_shows_the_page_and_the_code()
    {
        var fixture = Build(DeviceCli());

        var signIn = fixture.SignIn("hotmail");

        // Always the file the tab writes: the tokens are filed beside it.
        var request = Assert.Single(fixture.Cli.Requests);
        Assert.Equal(["email", "login", "hotmail", "--events", "jsonl", "--settings", SettingsFile], request.Arguments);

        Assert.True(fixture.Row("hotmail").HasSignIn);
        Assert.Equal(EmailSignInPhase.DeviceCode, signIn.Phase);
        Assert.True(signIn.IsDeviceCode);
        Assert.True(signIn.IsWaiting);
        Assert.Equal(UserCode, signIn.UserCode);
        // The address shows in clear, as the verb wrote it, before anything can open it.
        Assert.Equal(DevicePage, signIn.Address);
        Assert.True(signIn.HasAddress);
        Assert.Equal($"Open {DevicePage} and enter this code:", signIn.DeviceCodeLine);
        Assert.False(signIn.IsAuthorizationUrl);
        // Nothing was opened: the panel only says what to do.
        Assert.Empty(fixture.Browser.Opened);
    }

    [Fact]
    public void The_device_code_counts_down_on_the_injected_clock_one_beat_at_a_time()
    {
        var fixture = Build(DeviceCli());

        var signIn = fixture.SignIn("hotmail");

        Assert.True(signIn.HasExpiry);
        Assert.Equal("The code expires in 15:00", signIn.ExpiresInText);
        Assert.True(fixture.Ticker.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(1), fixture.Ticker.Interval);

        var raised = new List<string>();
        signIn.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        fixture.Clock.Now += TimeSpan.FromSeconds(61);
        fixture.Ticker.Tick();

        Assert.Equal("The code expires in 13:59", signIn.ExpiresInText);
        Assert.Contains(nameof(EmailSignInViewModel.ExpiresInText), raised);
        Assert.True(signIn.IsDeviceCode);

        fixture.Clock.Now += TimeSpan.FromMinutes(13) + TimeSpan.FromSeconds(54);
        fixture.Ticker.Tick();
        Assert.Equal("The code expires in 0:05", signIn.ExpiresInText);
    }

    [Fact]
    public async Task When_the_code_expires_the_panel_says_so_stops_the_child_and_offers_to_start_again()
    {
        var fixture = Build(DeviceCli());
        var signIn = fixture.SignIn("hotmail");
        Assert.False(signIn.RestartCommand.CanExecute(null));
        var waiting = signIn.Ended;

        fixture.Clock.Now += TimeSpan.FromMinutes(15);
        fixture.Ticker.Tick();

        Assert.Equal(EmailSignInPhase.Expired, signIn.Phase);
        Assert.True(signIn.IsExpired);
        Assert.False(signIn.IsWaiting);
        // The code is worth nothing any more: it leaves the screen with its countdown.
        Assert.Null(signIn.UserCode);
        Assert.False(signIn.HasExpiry);
        Assert.True(signIn.RestartCommand.CanExecute(null));
        await Guarded(waiting);
        Assert.Equal(1, fixture.Cli.StoppedConversations);
        Assert.Equal(0, fixture.Cli.LiveConversations);
        // The panel stays: it is where "start again" is.
        Assert.Same(signIn, fixture.Row("hotmail").SignIn);

        signIn.RestartCommand.Execute(null);

        Assert.Equal(2, fixture.Runs("login"));
        Assert.Equal(EmailSignInPhase.DeviceCode, signIn.Phase);
        Assert.Equal(UserCode, signIn.UserCode);
        Assert.Equal("The code expires in 15:00", signIn.ExpiresInText);
        Assert.Equal(1, fixture.Cli.LiveConversations);
    }

    [Fact]
    public async Task A_failure_that_lands_before_the_last_beat_of_an_expired_code_offers_to_start_again_and_starts_nothing_itself()
    {
        // The verb and the countdown end on the same deadline: here the verb's word comes first.
        var fixture = Build(DeviceCli());
        var signIn = fixture.SignIn("hotmail");
        var raised = new List<string>();
        signIn.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        var woken = 0;
        signIn.RestartCommand.CanExecuteChanged += (_, _) => woken++;

        fixture.Clock.Now += TimeSpan.FromMinutes(15);
        fixture.Cli.Say("email login", ErrorLine("LoginRequired", CodeExpired));
        await Guarded(signIn.Ended);

        Assert.Equal(EmailSignInPhase.Failed, signIn.Phase);
        Assert.Equal(CodeExpired, signIn.FailureDetail);
        Assert.Null(signIn.UserCode);
        // A code was on screen: a new one is one click away, as when the countdown ends first.
        Assert.True(signIn.CanRestart);
        Assert.True(signIn.RestartCommand.CanExecute(null));
        Assert.Contains(nameof(EmailSignInViewModel.CanRestart), raised);
        Assert.True(woken > 0);

        // The beat that would have said "expired" changes nothing, and nothing starts by itself.
        fixture.Ticker.Tick();
        Assert.Equal(EmailSignInPhase.Failed, signIn.Phase);
        Assert.Equal(CodeExpired, signIn.FailureDetail);
        Assert.Equal(1, fixture.Runs("login"));

        signIn.RestartCommand.Execute(null);

        Assert.Equal(2, fixture.Runs("login"));
        Assert.Equal(EmailSignInPhase.DeviceCode, signIn.Phase);
        Assert.Equal(UserCode, signIn.UserCode);
        Assert.Null(signIn.FailureDetail);
        Assert.Equal(1, fixture.Cli.LiveConversations);
    }

    [Fact]
    public async Task A_failure_that_lands_after_the_last_beat_of_an_expired_code_leaves_the_panel_on_expired()
    {
        // The interface thread is behind the child: the verb's last word waits in the queue while the beat runs.
        var dispatcher = new QueuedUiDispatcher();
        var fixture = Build(DeviceCli(), dispatcher: dispatcher);
        var signIn = fixture.SignIn("hotmail");
        dispatcher.Drain();
        Assert.Equal(EmailSignInPhase.DeviceCode, signIn.Phase);

        fixture.Clock.Now += TimeSpan.FromMinutes(15);
        fixture.Cli.Say("email login", ErrorLine("LoginRequired", CodeExpired));
        await Polling.WaitUntilAsync(() => dispatcher.Pending > 0);
        fixture.Ticker.Tick();
        Assert.Equal(EmailSignInPhase.Expired, signIn.Phase);

        dispatcher.Drain();
        await Guarded(signIn.Ended);

        Assert.Equal(EmailSignInPhase.Expired, signIn.Phase);
        Assert.Null(signIn.FailureDetail);
        Assert.True(signIn.CanRestart);
        Assert.True(signIn.RestartCommand.CanExecute(null));
        Assert.Equal(1, fixture.Runs("login"));
    }

    [Fact]
    public async Task A_device_sign_in_that_completes_says_so_reads_the_states_again_and_the_account_turns_ready()
    {
        var fixture = Build(DeviceCli());
        await fixture.Section.RefreshStatesAsync();
        Assert.Equal(EmailAccountReadiness.NotReady, fixture.Row("hotmail").StateKind);
        var signIn = fixture.SignIn("hotmail");

        // The person typed the code on the provider's page: the verb stores the tokens and says so.
        fixture.Cli.Answer("email accounts", 0, Accounts(hotmailReady: true));
        fixture.Cli.Say("email login", CompletedLine("hotmail"));
        await Guarded(signIn.Ended);

        Assert.Equal(EmailSignInPhase.Completed, signIn.Phase);
        Assert.True(signIn.IsCompleted);
        Assert.False(signIn.IsWaiting);
        Assert.Null(signIn.UserCode);
        Assert.Equal(2, fixture.Runs("accounts"));
        Assert.Equal(EmailAccountReadiness.Ready, fixture.Row("hotmail").StateKind);
        Assert.Equal(0, fixture.Cli.LiveConversations);
        Assert.Equal(0, fixture.Cli.StoppedConversations);
        Assert.False(fixture.Ticker.IsRunning);

        // "Close" is all that is left to do with the panel.
        signIn.CancelCommand.Execute(null);
        Assert.Null(fixture.Row("hotmail").SignIn);
        Assert.False(fixture.Row("hotmail").HasSignIn);
    }

    // ── the browser sign-in ──

    [Fact]
    public async Task The_browser_sign_in_shows_the_link_and_the_paste_field_and_the_pasted_address_completes_it()
    {
        var fixture = Build(BrowserCli());
        await fixture.Section.RefreshStatesAsync();

        var signIn = fixture.SignIn("perso");

        Assert.Equal(EmailSignInPhase.AuthorizationUrl, signIn.Phase);
        Assert.True(signIn.IsAuthorizationUrl);
        Assert.Equal(AuthorizationPage, signIn.Address);
        Assert.Null(signIn.UserCode);
        Assert.False(signIn.HasExpiry);
        Assert.False(fixture.Ticker.IsRunning);
        // An empty field sends nothing.
        Assert.False(signIn.SubmitRedirectCommand.CanExecute(null));

        signIn.PastedRedirect = "  " + Redirect + "  ";
        Assert.True(signIn.SubmitRedirectCommand.CanExecute(null));
        signIn.SubmitRedirectCommand.Execute(null);
        await Guarded(signIn.Ended);

        Assert.Equal([Redirect], fixture.Cli.InputLines);
        Assert.Equal(EmailSignInPhase.Completed, signIn.Phase);
        Assert.Equal("", signIn.PastedRedirect);
        Assert.False(signIn.RedirectNotSent);
        Assert.Equal(EmailAccountReadiness.Ready, fixture.Row("perso").StateKind);
        Assert.Equal(0, fixture.Cli.LiveConversations);
    }

    [Fact]
    public void An_address_that_does_not_end_the_sign_in_leaves_it_waiting()
    {
        var fixture = Build(BrowserCli());
        var signIn = fixture.SignIn("perso");

        signIn.PastedRedirect = "https://example.com/not-the-redirect";
        signIn.SubmitRedirectCommand.Execute(null);

        // The verb reads on: Studio judges nothing of what was pasted.
        Assert.Equal(["https://example.com/not-the-redirect"], fixture.Cli.InputLines);
        Assert.Equal(EmailSignInPhase.AuthorizationUrl, signIn.Phase);
        Assert.Equal(1, fixture.Cli.LiveConversations);
    }

    [Fact]
    public async Task An_address_the_verb_rejects_stays_in_the_field_with_the_verbs_sentence_and_the_panel_keeps_waiting()
    {
        var fixture = Build(JudgingBrowserCli());
        await fixture.Section.RefreshStatesAsync();
        var signIn = fixture.SignIn("perso");
        Assert.False(signIn.RedirectRejected);
        Assert.Null(signIn.RedirectRejectedDetail);
        var raised = new List<string>();
        signIn.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        signIn.PastedRedirect = CutShort;
        signIn.SubmitRedirectCommand.Execute(null);

        // The verb judged it, and said why: its sentence shows as printed, and the address is there to be corrected.
        Assert.Equal([CutShort], fixture.Cli.InputLines);
        Assert.True(signIn.RedirectRejected);
        Assert.Equal(NoCode, signIn.RedirectRejectedDetail);
        Assert.Equal(CutShort, signIn.PastedRedirect);
        Assert.Contains(nameof(EmailSignInViewModel.RedirectRejected), raised);
        Assert.Contains(nameof(EmailSignInViewModel.RedirectRejectedDetail), raised);
        // A rejected address is not a failed sign-in: the child lives, and the panel still waits on it.
        Assert.Equal(EmailSignInPhase.AuthorizationUrl, signIn.Phase);
        Assert.True(signIn.IsWaiting);
        Assert.False(signIn.IsFailed);
        Assert.Null(signIn.FailureDetail);
        Assert.False(signIn.RedirectNotSent);
        Assert.Equal(1, fixture.Cli.LiveConversations);
        Assert.True(signIn.SubmitRedirectCommand.CanExecute(null));

        // Corrected in place: the notice is of the address sent, and stays while the next one is typed.
        signIn.PastedRedirect = Redirect;
        Assert.True(signIn.RedirectRejected);
        signIn.SubmitRedirectCommand.Execute(null);
        await Guarded(signIn.Ended);

        Assert.Equal([CutShort, Redirect], fixture.Cli.InputLines);
        Assert.Equal(EmailSignInPhase.Completed, signIn.Phase);
        Assert.False(signIn.RedirectRejected);
        Assert.Null(signIn.RedirectRejectedDetail);
        Assert.Equal("", signIn.PastedRedirect);
        Assert.Equal(EmailAccountReadiness.Ready, fixture.Row("perso").StateKind);
    }

    [Fact]
    public void A_rejected_address_comes_back_in_the_field_it_had_left_and_the_notice_leaves_with_the_next_one_sent()
    {
        // The interface thread is behind the child: what the verb says lands when the queue is drained.
        var dispatcher = new QueuedUiDispatcher();
        var fixture = Build(JudgingBrowserCli(), dispatcher: dispatcher);
        var signIn = fixture.SignIn("perso");
        dispatcher.Drain();

        signIn.PastedRedirect = "not an address";
        signIn.SubmitRedirectCommand.Execute(null);

        // Handed to the verb, whose answer has not landed: the field is empty, as for an address it takes.
        Assert.Equal("", signIn.PastedRedirect);
        Assert.False(signIn.RedirectRejected);

        dispatcher.Drain();

        Assert.True(signIn.RedirectRejected);
        Assert.Equal(NotAnAddress, signIn.RedirectRejectedDetail);
        Assert.Equal("not an address", signIn.PastedRedirect);
        Assert.Equal(EmailSignInPhase.AuthorizationUrl, signIn.Phase);

        // Sent again: the notice was of the previous address, and leaves before the verb answers this one.
        signIn.PastedRedirect = CutShort;
        signIn.SubmitRedirectCommand.Execute(null);

        Assert.False(signIn.RedirectRejected);
        Assert.Null(signIn.RedirectRejectedDetail);
        Assert.Equal("", signIn.PastedRedirect);

        dispatcher.Drain();

        Assert.True(signIn.RedirectRejected);
        Assert.Equal(NoCode, signIn.RedirectRejectedDetail);
        Assert.Equal(CutShort, signIn.PastedRedirect);
        Assert.True(signIn.IsWaiting);
    }

    [Fact]
    public void A_rejection_does_not_write_over_what_was_typed_since()
    {
        var dispatcher = new QueuedUiDispatcher();
        var fixture = Build(JudgingBrowserCli(), dispatcher: dispatcher);
        var signIn = fixture.SignIn("perso");
        dispatcher.Drain();

        signIn.PastedRedirect = CutShort;
        signIn.SubmitRedirectCommand.Execute(null);
        signIn.PastedRedirect = "http://127.0.0.1:53124/?co";
        dispatcher.Drain();

        Assert.True(signIn.RedirectRejected);
        Assert.Equal("http://127.0.0.1:53124/?co", signIn.PastedRedirect);
    }

    [Fact]
    public void Leaving_the_panel_forgets_the_rejection_and_the_address_it_was_of()
    {
        var fixture = Build(JudgingBrowserCli());
        var signIn = fixture.SignIn("perso");
        signIn.PastedRedirect = CutShort;
        signIn.SubmitRedirectCommand.Execute(null);
        Assert.True(signIn.RedirectRejected);

        signIn.CancelCommand.Execute(null);

        Assert.False(signIn.RedirectRejected);
        Assert.Null(signIn.RedirectRejectedDetail);
        Assert.Equal("", signIn.PastedRedirect);
        Assert.Equal(0, fixture.Cli.LiveConversations);
    }

    [Fact]
    public void An_address_the_verb_no_longer_reads_stays_in_the_field_and_the_panel_says_it_was_not_sent()
    {
        // The interface thread is behind the child: what the verb says lands when the queue is drained.
        var dispatcher = new QueuedUiDispatcher();
        var fixture = Build(BrowserCli(), dispatcher: dispatcher);
        var signIn = fixture.SignIn("perso");
        dispatcher.Drain();
        Assert.Equal(EmailSignInPhase.AuthorizationUrl, signIn.Phase);
        Assert.False(signIn.RedirectNotSent);

        // The child left on a refusal, and its last word has not landed yet: the field still shows.
        fixture.Cli.Say("email login", ErrorLine("LoginRequired", NoRefreshToken));
        var raised = new List<string>();
        signIn.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        signIn.PastedRedirect = Redirect;
        signIn.SubmitRedirectCommand.Execute(null);

        // Nothing read it: clearing the field would say it was taken.
        Assert.Empty(fixture.Cli.InputLines);
        Assert.Equal(Redirect, signIn.PastedRedirect);
        Assert.True(signIn.RedirectNotSent);
        Assert.Contains(nameof(EmailSignInViewModel.RedirectNotSent), raised);

        // The notice is of the address that was not sent: it leaves when the field is typed in again.
        signIn.PastedRedirect = Redirect + "&again";
        Assert.False(signIn.RedirectNotSent);
    }

    // ── copying and opening the address ──

    [Fact]
    public void Open_hands_the_received_address_to_the_browser_port_on_a_click_and_copy_puts_it_on_the_clipboard()
    {
        var fixture = Build(BrowserCli());
        var signIn = fixture.SignIn("perso");
        Assert.Empty(fixture.Browser.Opened);
        Assert.True(signIn.CanOpen);
        Assert.True(signIn.OpenCommand.CanExecute(null));

        signIn.OpenCommand.Execute(null);

        Assert.Equal([new Uri(AuthorizationPage)], fixture.Browser.Opened);

        signIn.CopyLinkCommand.Execute(null);

        Assert.Equal(AuthorizationPage, fixture.Clipboard.LastText);
    }

    [Fact]
    public void The_page_of_a_device_sign_in_opens_the_same_way_and_the_code_is_never_what_is_copied()
    {
        var fixture = Build(DeviceCli());
        var signIn = fixture.SignIn("hotmail");

        signIn.OpenCommand.Execute(null);
        signIn.CopyLinkCommand.Execute(null);

        Assert.Equal([new Uri(DevicePage)], fixture.Browser.Opened);
        Assert.Equal(DevicePage, fixture.Clipboard.LastText);
    }

    /// <summary>
    /// The address comes from the output of a process. What a shell does with a string depends on
    /// what the string is: a path runs a program, another scheme starts whatever handles it. So
    /// only an absolute https address is ever handed over — anything else shows, in clear, and
    /// can be copied, and that is all.
    /// </summary>
    [Theory]
    [InlineData("http://accounts.google.com/o/oauth2/v2/auth?client_id=c")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData(@"\\server\share\run.exe")]
    [InlineData("/usr/bin/xcalc")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("accounts.google.com/o/oauth2/v2/auth")]
    [InlineData("HTTPS:calc.exe")]
    public void An_address_that_is_not_https_is_shown_and_can_be_copied_but_is_never_opened(string address)
    {
        var fixture = Build(BrowserCli(address));
        var signIn = fixture.SignIn("perso");

        Assert.Equal(address, signIn.Address);
        Assert.False(signIn.CanOpen);
        Assert.False(signIn.OpenCommand.CanExecute(null));
        Assert.True(signIn.CopyLinkCommand.CanExecute(null));

        // A binding that ignored the guard would still open nothing.
        signIn.OpenCommand.Execute(null);

        Assert.Empty(fixture.Browser.Opened);
    }

    [Fact]
    public void An_https_address_is_recognised_whatever_the_case_of_its_scheme()
    {
        var fixture = Build(BrowserCli("HTTPS://accounts.google.com/o/oauth2/v2/auth?client_id=c"));
        var signIn = fixture.SignIn("perso");

        signIn.OpenCommand.Execute(null);

        var opened = Assert.Single(fixture.Browser.Opened);
        Assert.Equal(Uri.UriSchemeHttps, opened.Scheme);
        Assert.Equal("accounts.google.com", opened.Host);
    }

    [Fact]
    public void Without_a_browser_port_the_address_can_only_be_copied()
    {
        var fixture = Build(BrowserCli(), withBrowser: false);
        var signIn = fixture.SignIn("perso");

        Assert.False(signIn.CanOpen);
        Assert.False(signIn.OpenCommand.CanExecute(null));
        Assert.True(signIn.CopyLinkCommand.CanExecute(null));
    }

    // ── a refusal ──

    [Theory]
    [InlineData("LoginRequired", NoRefreshToken)]
    [InlineData("CredentialMissing", "The client secret of e-mail account 'perso' is read from the environment variable GMAIL_CLIENT_SECRET, which is not set.")]
    public async Task A_refusal_of_the_verb_shows_as_printed_and_is_never_tried_again(string code, string message)
    {
        var cli = new ScriptedOrkeonCli()
            .Answer("email accounts", 0, Accounts())
            .Converse("email login", [ErrorLine(code, message)], _ => []);
        var fixture = Build(cli);

        var signIn = fixture.SignIn("perso");
        await Guarded(signIn.Ended);

        Assert.Equal(EmailSignInPhase.Failed, signIn.Phase);
        Assert.True(signIn.IsFailed);
        Assert.False(signIn.IsWaiting);
        // The engine's sentence, in English, as printed: nothing is read out of it.
        Assert.Equal(message, signIn.FailureDetail);
        Assert.Null(signIn.UserCode);

        // Not tried again: one run, however long the panel stays, and no "start again" either.
        fixture.Clock.Now += TimeSpan.FromHours(1);
        fixture.Ticker.Tick();
        Assert.Equal(1, fixture.Runs("login"));
        Assert.False(signIn.CanRestart);
        Assert.False(signIn.RestartCommand.CanExecute(null));
        Assert.Equal(0, fixture.Cli.LiveConversations);
        // Nothing was stored, so nothing changed for the engine: the states are not read again.
        Assert.Equal(0, fixture.Runs("accounts"));
        // The row may ask again, on a click.
        Assert.True(fixture.Row("perso").SignInCommand.CanExecute(null));
    }

    [Fact]
    public async Task Without_the_cli_the_panel_says_the_home_screens_sentence()
    {
        var cli = DeviceCli();
        var document = AppSettingsDocument.Parse(Three);
        var section = new EmailSectionViewModel(
            () => document, () => { }, settingsPath: () => SettingsFile, keyStore: new FakeApiKeyStore(),
            engine: new EmailCliSeams
            {
                Cli = new EmailCliClient(new OrkeonProcessRunner(cli, new OrkeonBinaryLocator(new FakeExecutableProbe()))),
            });
        var hotmail = section.Accounts.Single(row => row.Name == "hotmail");

        hotmail.SignInCommand.Execute(null);
        await Guarded(hotmail.SignIn!.Ended);

        Assert.Equal(EmailSignInPhase.Failed, hotmail.SignIn.Phase);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.WizardFailureEngineMissing], hotmail.SignIn.FailureDetail);
        Assert.Empty(cli.Requests);
    }

    // ── stopping ──

    [Fact]
    public async Task Cancel_stops_the_child_and_closes_the_panel_without_a_verdict()
    {
        var fixture = Build(BrowserCli());
        var signIn = fixture.SignIn("perso");
        var waiting = signIn.Ended;
        Assert.Equal(1, fixture.Cli.LiveConversations);

        signIn.CancelCommand.Execute(null);

        Assert.Null(fixture.Row("perso").SignIn);
        await Guarded(waiting);
        Assert.Equal(1, fixture.Cli.StoppedConversations);
        Assert.Equal(0, fixture.Cli.LiveConversations);
        Assert.False(signIn.IsFailed);
        Assert.True(fixture.Row("perso").SignInCommand.CanExecute(null));
    }

    [Fact]
    public void While_a_sign_in_waits_the_same_account_cannot_start_a_second_one()
    {
        var fixture = Build(DeviceCli());
        var signIn = fixture.SignIn("hotmail");

        Assert.False(fixture.Row("hotmail").SignInCommand.CanExecute(null));
        fixture.Row("hotmail").SignInCommand.Execute(null);

        Assert.Same(signIn, fixture.Row("hotmail").SignIn);
        Assert.Equal(1, fixture.Runs("login"));
        // Another account is another child.
        Assert.True(fixture.Row("perso").SignInCommand.CanExecute(null));
    }

    [Fact]
    public async Task Stopping_the_tabs_activity_stops_every_sign_in_in_flight_and_closes_its_panel()
    {
        var fixture = Build(DeviceCli());
        var first = fixture.SignIn("hotmail");
        var second = fixture.SignIn("perso");
        var waiting = Task.WhenAll(first.Ended, second.Ended);
        Assert.Equal(2, fixture.Cli.LiveConversations);

        fixture.Section.StopActivity();

        Assert.Null(fixture.Row("hotmail").SignIn);
        Assert.Null(fixture.Row("perso").SignIn);
        Assert.False(fixture.Ticker.IsRunning);
        await Guarded(waiting);
        Assert.Equal(2, fixture.Cli.StoppedConversations);
        Assert.Equal(0, fixture.Cli.LiveConversations);
    }

    [Fact]
    public async Task Leaving_the_mails_tab_stops_a_sign_in_in_flight()
    {
        var cli = BrowserCli();
        var (screen, config, _) = BuildScreen(cli);
        screen.ShowMailsCommand.Execute(null);
        var perso = config.Email.Accounts.Single(row => row.Name == "perso");
        perso.SignInCommand.Execute(null);
        var waiting = perso.SignIn!.Ended;
        Assert.Equal(1, cli.LiveConversations);

        screen.ShowModelCommand.Execute(null);

        Assert.Null(perso.SignIn);
        await Guarded(waiting);
        Assert.Equal(1, cli.StoppedConversations);
        Assert.Equal(0, cli.LiveConversations);
    }

    [Fact]
    public async Task Leaving_the_settings_screen_or_closing_the_window_stops_a_sign_in_in_flight()
    {
        var cli = BrowserCli();
        var (screen, config, _) = BuildScreen(cli);
        screen.ShowMailsCommand.Execute(null);
        var perso = config.Email.Accounts.Single(row => row.Name == "perso");
        perso.SignInCommand.Execute(null);
        var waiting = perso.SignIn!.Ended;

        // Another sidebar entry, or the window's Closed: the same call, on the caller's thread.
        screen.Leave();

        // Before anything is awaited: what a closing window can rely on is that the child's
        // token fired — and with it its standard input closed — when Leave returned.
        Assert.Null(perso.SignIn);
        await Guarded(waiting);
        Assert.Equal(1, cli.StoppedConversations);
        Assert.Equal(0, cli.LiveConversations);
    }

    // ── a device code is a secret while it lives ──

    [Fact]
    public async Task The_device_code_reaches_neither_the_file_nor_the_status_line_nor_the_clipboard()
    {
        var cli = DeviceCli();
        var (screen, config, store) = BuildScreen(cli);
        screen.ShowMailsCommand.Execute(null);
        var hotmail = config.Email.Accounts.Single(row => row.Name == "hotmail");
        hotmail.SignInCommand.Execute(null);
        var signIn = hotmail.SignIn!;
        Assert.Equal(UserCode, signIn.UserCode);

        signIn.CopyLinkCommand.Execute(null);
        Assert.True(await config.SaveAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain(UserCode, config.RawJson, StringComparison.Ordinal);
        Assert.DoesNotContain(UserCode, config.StatusMessage ?? "", StringComparison.Ordinal);
        Assert.All(store.Files.Values, text => Assert.DoesNotContain(UserCode, text, StringComparison.Ordinal));
        // The code never travels on the command line either: an argv is what a history keeps.
        Assert.All(cli.Requests, request => Assert.DoesNotContain(request.Arguments, argument => argument.Contains(UserCode, StringComparison.Ordinal)));
        Assert.DoesNotContain(UserCode, signIn.ToString() ?? "", StringComparison.Ordinal);

        signIn.CancelCommand.Execute(null);
        Assert.Null(signIn.UserCode);
    }

    // ── signing out ──

    [Fact]
    public async Task Signing_out_asks_first_then_forgets_the_tokens_of_the_settings_file_and_reads_the_states_again()
    {
        var cli = new ScriptedOrkeonCli()
            .Answer("email accounts", 0, Accounts(hotmailReady: true))
            .Answer("email logout", 0, "Signed out: the tokens of e-mail account 'hotmail' are deleted.");
        var fixture = Build(cli);
        await fixture.Section.RefreshStatesAsync();
        var hotmail = fixture.Row("hotmail");
        Assert.True(hotmail.IsReady);

        hotmail.SignOutCommand.Execute(null);

        // A question, and nothing run yet.
        Assert.True(hotmail.IsConfirmingSignOut);
        Assert.False(hotmail.IsIdle);
        Assert.Equal("Forget the stored tokens of hotmail? The account will need a new sign-in.", hotmail.SignOutConfirmText);
        Assert.Equal(0, fixture.Runs("logout"));

        cli.Answer("email accounts", 0, Accounts());
        await Guarded(hotmail.ConfirmSignOutCommand.ExecuteAsync());

        var request = Assert.Single(cli.Requests, r => r.Arguments is ["email", "logout", ..]);
        Assert.Equal(["email", "logout", "hotmail", "--settings", SettingsFile], request.Arguments);
        Assert.False(hotmail.IsConfirmingSignOut);
        Assert.False(hotmail.HasSignOutFailure);
        Assert.Equal(2, fixture.Runs("accounts"));
        Assert.Equal(EmailAccountReadiness.NotReady, hotmail.StateKind);
    }

    [Fact]
    public void A_sign_out_that_is_not_confirmed_forgets_nothing()
    {
        var fixture = Build(DeviceCli());
        var hotmail = fixture.Row("hotmail");

        hotmail.SignOutCommand.Execute(null);
        hotmail.CancelSignOutCommand.Execute(null);

        Assert.False(hotmail.IsConfirmingSignOut);
        Assert.True(hotmail.IsIdle);
        Assert.Equal(0, fixture.Runs("logout"));
    }

    [Fact]
    public async Task A_sign_out_the_verb_refused_says_so_and_leaves_the_states_alone()
    {
        var cli = new ScriptedOrkeonCli()
            .Answer("email accounts", 0, Accounts(hotmailReady: true))
            .Answer("email logout", 1);
        var fixture = Build(cli);
        var hotmail = fixture.Row("hotmail");

        hotmail.SignOutCommand.Execute(null);
        await Guarded(hotmail.ConfirmSignOutCommand.ExecuteAsync());

        Assert.True(hotmail.HasSignOutFailure);
        Assert.False(string.IsNullOrWhiteSpace(hotmail.SignOutFailure));
        Assert.False(hotmail.IsConfirmingSignOut);
        Assert.Equal(0, fixture.Runs("accounts"));
    }

    [Fact]
    public void Arming_the_sign_out_question_disarms_the_other_questions_of_the_tab()
    {
        var fixture = Build(DeviceCli());
        var hotmail = fixture.Row("hotmail");
        fixture.Section.RemoveAccountCommand.Execute(fixture.Row("perso"));
        Assert.True(fixture.Row("perso").IsConfirmingRemove);

        hotmail.SignOutCommand.Execute(null);

        Assert.True(hotmail.IsConfirmingSignOut);
        Assert.False(fixture.Row("perso").IsConfirmingRemove);

        fixture.Section.RenameAccountCommand.Execute(hotmail);
        Assert.False(hotmail.IsConfirmingSignOut);
    }

    // ── the language ──

    [Fact]
    public void A_language_switch_says_the_panel_again_and_leaves_the_address_and_the_code_alone()
    {
        var strings = new SwitchableStrings();
        var fixture = Build(DeviceCli(), strings);
        var signIn = fixture.SignIn("hotmail");
        fixture.Row("hotmail").SignOutCommand.Execute(null);
        var raised = new List<string>();
        signIn.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        strings.SwitchToFrench();

        Assert.Equal($"Ouvrez {DevicePage} et saisissez ce code :", signIn.DeviceCodeLine);
        Assert.Equal("Le code expire dans 15:00", signIn.ExpiresInText);
        Assert.Equal(UserCode, signIn.UserCode);
        Assert.Equal(DevicePage, signIn.Address);
        Assert.Contains(nameof(EmailSignInViewModel.DeviceCodeLine), raised);
        Assert.Contains(nameof(EmailSignInViewModel.ExpiresInText), raised);
        Assert.Equal("Oublier les jetons de hotmail ?", fixture.Row("hotmail").SignOutConfirmText);
    }

    /// <summary>English, then French for the three sentences the panel fabricates.</summary>
    private sealed class SwitchableStrings : IStudioStrings
    {
        private bool _french;

        public string this[string key] => (_french, key) switch
        {
            (true, StudioStringKeys.MailDeviceCode) => "Ouvrez {0} et saisissez ce code :",
            (true, StudioStringKeys.MailDeviceCodeExpiresIn) => "Le code expire dans {0}",
            (true, StudioStringKeys.MailSignOutConfirm) => "Oublier les jetons de {0} ?",
            _ => EnglishStudioStrings.Instance[key],
        };

        public event EventHandler? CultureChanged;

        public void SwitchToFrench()
        {
            _french = true;
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
