using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Email;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-69 — what each account's row says of it: the state <c>orkeon email accounts</c> gives
/// the saved file, paired by name, read on arrival on the tab and after each save and never while
/// the user types; and the connection test, which only a click starts and which leaving the tab
/// stops. Every process is <see cref="FakeEmailCli"/>: nothing is spawned, nothing is connected to.
/// </summary>
public sealed partial class EmailSectionViewModelTests
{
    private const string SettingsFile = "/home/me/.config/Orkeon/appsettings.json";

    private const string EngineMissing = "The orkeon engine was not found on this machine.";

    private const string PasswordUnset =
        "The password of e-mail account 'work' is read from the environment variable WORK_MAIL_PASSWORD, which is not set.";

    private const string ReachableSentence = "E-mail account 'perso' is reachable: 12 folder(s), inbox 340 message(s), 3 unread.";

    // The engine lists the accounts sorted by name, in its own spelling: not the order of the file.
    private const string PersoReadyWorkNot = """
        [{"name":"perso","address":"me@gmail.com","provider":"Gmail","reads":"Imap","sends":"Smtp","rights":"Read","auth":"Password","default":true,"ready":true,"problem":null},
         {"name":"work","address":"me@example.com","provider":"Custom","reads":"Imap","sends":null,"rights":"Read","auth":"Password","default":false,"ready":false,
          "problem":"The password of e-mail account 'work' is read from the environment variable WORK_MAIL_PASSWORD, which is not set."}]
        """;

    private static EmailCliClient Client(FakeEmailCli cli, bool binaryPresent = true) =>
        new(new OrkeonProcessRunner(
            cli,
            new OrkeonBinaryLocator(binaryPresent ? FakeExecutableProbe.WithOrkeonInstalled() : new FakeExecutableProbe())));

    /// <summary>The section alone, over a scripted CLI; <paramref name="dirty"/> plays the document's unsaved edits.</summary>
    private static (EmailSectionViewModel Section, FakeEmailCli Cli) BuildWithCli(
        string json,
        FakeEmailCli? cli = null,
        bool binaryPresent = true,
        Func<bool>? dirty = null,
        IStudioStrings? strings = null,
        FakeApiKeyStore? keys = null)
    {
        var document = AppSettingsDocument.Parse(json);
        cli ??= new FakeEmailCli { AccountsOutput = PersoReadyWorkNot };
        var section = new EmailSectionViewModel(
            () => document,
            () => { },
            strings,
            () => SettingsFile,
            keyStore: keys ?? new FakeApiKeyStore(),
            engine: new EmailCliSeams { Cli = Client(cli, binaryPresent), IsDirty = dirty });
        return (section, cli);
    }

    /// <summary>The whole settings screen over a scripted CLI and an in-memory settings file.</summary>
    private static (SettingsScreenViewModel Screen, ConfigTabViewModel Config, FakeEmailCli Cli) BuildScreen(
        string mode, string json = Two, FakeEmailCli? cli = null, ManualUiDelay? delay = null)
    {
        cli ??= new FakeEmailCli { AccountsOutput = PersoReadyWorkNot };
        var config = new ConfigTabViewModel(
            new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                KeyStore = new FakeApiKeyStore(),
                EmailCli = Client(cli),
                EmailStatesDelay = delay,
            },
            globalPathOverride: SettingsFile);
        config.SetDocument(AppSettingsDocument.Parse(json), SettingsFile);
        var screen = new SettingsScreenViewModel(
            config,
            new ModelProfilesViewModel(new InMemoryModelProfileStore(), config.Llm),
            new UiModeViewModel(mode));
        return (screen, config, cli);
    }

    private static EmailAccountRowViewModel Row(EmailSectionViewModel section, string name) =>
        section.Accounts.Single(row => row.Name == name);

    // ── the state of each account ──

    [Fact]
    public async Task Each_row_says_what_the_engine_answered_for_its_account_on_the_saved_file()
    {
        var (section, cli) = BuildWithCli(Two);
        Assert.All(section.Accounts, row => Assert.False(row.HasState));

        await section.RefreshStatesAsync();

        // Always the file the tab writes: without --settings the verb would resolve its own.
        var request = Assert.Single(cli.Requests);
        Assert.Equal(["email", "accounts", "--json", "--settings", SettingsFile], request.Arguments);

        var perso = Row(section, "perso");
        Assert.Equal(EmailAccountReadiness.Ready, perso.StateKind);
        Assert.True(perso.HasState);
        Assert.True(perso.IsReady);
        Assert.Equal("Ready", perso.State);
        Assert.Null(perso.StateDetail);
        Assert.False(perso.HasStateDetail);

        var work = Row(section, "work");
        Assert.Equal(EmailAccountReadiness.NotReady, work.StateKind);
        Assert.False(work.IsReady);
        Assert.Equal("Not ready", work.State);
        // The engine's sentence, as printed: it names the variable, and nothing is read out of it.
        Assert.Equal(PasswordUnset, work.StateDetail);
        Assert.True(work.HasStateDetail);
    }

    [Fact]
    public async Task A_row_is_paired_with_its_state_by_name_whatever_the_case_and_whatever_the_order()
    {
        const string Shouting = """
            { "Orkeon": { "Tools": { "Email": { "Accounts": {
              "Work":  { "Provider": "Custom", "Address": "me@example.com", "Rights": "Read",
                         "Incoming": { "Host": "imap.example.com" }, "Auth": { "PasswordEnvVar": "WORK_MAIL_PASSWORD" } },
              "PERSO": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read",
                         "Auth": { "PasswordEnvVar": "GMAIL_APP_PASSWORD" } } } } } } }
            """;
        var (section, _) = BuildWithCli(Shouting);

        await section.RefreshStatesAsync();

        Assert.Equal(EmailAccountReadiness.NotReady, Row(section, "Work").StateKind);
        Assert.Equal(EmailAccountReadiness.Ready, Row(section, "PERSO").StateKind);
    }

    [Fact]
    public async Task An_account_the_engine_set_aside_says_so_without_repeating_what_the_form_already_says()
    {
        var cli = new FakeEmailCli
        {
            AccountsOutput = """[{"name":"perso","address":null,"provider":"?","reads":null,"sends":null,"rights":null,"auth":null,"default":true,"ready":false,"problem":"Rights is required"}]""",
        };
        var (section, _) = BuildWithCli(Unnamed, cli);

        await section.RefreshStatesAsync();

        var row = Assert.Single(section.Accounts);
        Assert.Equal(EmailAccountReadiness.SetAside, row.StateKind);
        Assert.Equal("Set aside", row.State);
        // The findings of the form (STUDIO-66) say why: the state does not say it a second time.
        Assert.Null(row.StateDetail);
        Assert.True(row.HasProblems);
    }

    [Fact]
    public async Task An_account_the_saved_file_does_not_hold_has_no_state()
    {
        var cli = new FakeEmailCli
        {
            AccountsOutput = """[{"name":"perso","provider":"Gmail","reads":"Imap","auth":"Password","default":true,"ready":true}]""",
        };
        var (section, _) = BuildWithCli(Two, cli);

        await section.RefreshStatesAsync();

        Assert.Equal(EmailAccountReadiness.Ready, Row(section, "perso").StateKind);
        var work = Row(section, "work");
        Assert.Equal(EmailAccountReadiness.None, work.StateKind);
        Assert.False(work.HasState);
        Assert.Null(work.State);
    }

    [Fact]
    public async Task Without_the_cli_the_tab_stays_usable_and_every_account_is_unknown_with_the_home_screens_sentence()
    {
        var (section, cli) = BuildWithCli(Two, binaryPresent: false);

        await section.RefreshStatesAsync();

        Assert.Empty(cli.Requests);
        Assert.All(section.Accounts, row =>
        {
            Assert.Equal(EmailAccountReadiness.Unknown, row.StateKind);
            Assert.Equal("Unknown — " + EngineMissing, row.State);
            Assert.Null(row.StateDetail);
        });

        // The form still edits.
        Row(section, "perso").DisplayName = "Me";
        Assert.Equal("Me", Row(section, "perso").DisplayName);
    }

    [Fact]
    public async Task A_listing_the_cli_refused_makes_every_account_unknown_with_what_the_cli_said()
    {
        var cli = new FakeEmailCli
        {
            AccountsOutput = "",
            AccountsExitCode = 1,
            AccountsError = "orkeon email accounts: Settings file not found: " + SettingsFile,
        };
        var (section, _) = BuildWithCli(Two, cli);

        await section.RefreshStatesAsync();

        Assert.All(section.Accounts, row =>
        {
            Assert.Equal(EmailAccountReadiness.Unknown, row.StateKind);
            Assert.Equal("Unknown — Settings file not found: " + SettingsFile, row.State);
        });
    }

    [Fact]
    public async Task An_answer_that_cannot_be_read_makes_every_account_unknown_rather_than_ready()
    {
        var (section, _) = BuildWithCli(Two, new FakeEmailCli { AccountsOutput = "perso — me@gmail.com" });

        await section.RefreshStatesAsync();

        Assert.All(section.Accounts, row => Assert.Equal(EmailAccountReadiness.Unknown, row.StateKind));
        Assert.StartsWith("Unknown — ", Row(section, "perso").State, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_section_built_without_a_client_reads_no_state_and_spawns_nothing()
    {
        // The seam's default is no client at all: a test that names none can never reach a binary.
        var (section, _, _) = Build(Two);

        await section.RefreshStatesAsync();

        Assert.All(section.Accounts, row => Assert.False(row.HasState));
        Assert.False(Row(section, "perso").TestCommand.CanExecute(null));
    }

    [Fact]
    public async Task Reading_the_states_never_connects_to_a_mailbox()
    {
        var (section, cli) = BuildWithCli(Two);

        await section.RefreshStatesAsync();
        await section.RefreshStatesAsync();

        Assert.Equal(2, cli.ListRuns);
        Assert.Equal(0, cli.CheckRuns);
    }

    [Fact]
    public async Task A_newer_reading_stops_the_one_in_flight_and_only_its_answer_shows()
    {
        var cli = new FakeEmailCli { AccountsOutput = PersoReadyWorkNot, HoldListings = true };
        var (section, _) = BuildWithCli(Two, cli);

        var first = section.RefreshStatesAsync();
        await cli.Parked.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        cli.HoldListings = false;
        cli.AccountsOutput = """[{"name":"perso","provider":"Gmail","reads":"Imap","auth":"Password","ready":false,"problem":"later"}]""";
        await section.RefreshStatesAsync();
        await first.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(1, cli.Cancelled);
        Assert.Equal(0, cli.Live);
        Assert.Equal("later", Row(section, "perso").StateDetail);
    }

    [Fact]
    public async Task The_states_survive_a_reload_of_the_rows_and_follow_a_renamed_account_only_once_saved()
    {
        var (section, _) = BuildWithCli(Two);
        await section.RefreshStatesAsync();

        section.Refresh();

        Assert.Equal(EmailAccountReadiness.Ready, Row(section, "perso").StateKind);

        var perso = Row(section, "perso");
        section.RenameAccountCommand.Execute(perso);
        perso.RenameText = "home";
        perso.ConfirmRenameCommand.Execute(null);

        // The saved file knows no "home" yet: the state it had belongs to the old name.
        Assert.Equal(EmailAccountReadiness.None, Row(section, "home").StateKind);
    }

    [Fact]
    public async Task A_language_switch_says_the_state_again_and_leaves_the_engines_sentence_alone()
    {
        var strings = new SwitchableStrings();
        var (section, _) = BuildWithCli(Two, strings: strings);
        await section.RefreshStatesAsync();
        var work = Row(section, "work");
        var raised = new List<string>();
        work.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        strings.SwitchToFrench();

        Assert.Equal(SwitchableStrings.FrenchNotReady, work.State);
        Assert.Equal(PasswordUnset, work.StateDetail);
        Assert.Contains(nameof(EmailAccountRowViewModel.State), raised);
    }

    [Fact]
    public async Task A_password_kept_reads_the_states_again()
    {
        // The variable is named already, so keeping its value writes nothing and saves nothing:
        // the state would otherwise go on saying the password is missing.
        var keys = new FakeApiKeyStore();
        var (section, cli) = BuildWithCli(Two, keys: keys);
        await section.RefreshStatesAsync();
        var work = Row(section, "work");

        work.Password!.KeyInput = "s3cret";
        cli.AccountsOutput = """[{"name":"work","provider":"Custom","reads":"Imap","auth":"Password","ready":true}]""";
        await work.Password.StoreAsync();
        await Polling.WaitUntilAsync(() => work.StateKind == EmailAccountReadiness.Ready);

        // One value kept is one question to the engine, however many times the line says it holds a key.
        Assert.Equal(2, cli.ListRuns);
        Assert.Equal("Ready", work.State);
    }

    // ── unsaved edits ──

    [Fact]
    public async Task While_the_document_has_unsaved_edits_the_states_are_those_of_the_saved_file_and_the_test_is_disabled()
    {
        var dirty = false;
        var (section, cli) = BuildWithCli(Two, dirty: () => dirty);
        await section.RefreshStatesAsync();
        var perso = Row(section, "perso");
        Assert.False(section.IsStateOfSavedFile);
        Assert.True(perso.TestCommand.CanExecute(null));

        var raised = new List<string>();
        section.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        var woken = 0;
        perso.TestCommand.CanExecuteChanged += (_, _) => woken++;
        dirty = true;
        section.RefreshSavedFile();

        Assert.True(section.IsStateOfSavedFile);
        Assert.Contains(nameof(EmailSectionViewModel.IsStateOfSavedFile), raised);
        Assert.False(perso.TestCommand.CanExecute(null));
        Assert.True(woken > 0);
        // The state shown is still the saved file's, and says so through the section's flag.
        Assert.Equal(EmailAccountReadiness.Ready, perso.StateKind);

        await perso.TestCommand.ExecuteAsync();
        Assert.Equal(0, cli.CheckRuns);

        dirty = false;
        section.RefreshSavedFile();

        Assert.False(section.IsStateOfSavedFile);
        Assert.True(perso.TestCommand.CanExecute(null));
    }

    // ── the connection test ──

    [Fact]
    public async Task Testing_says_it_is_connecting_then_the_verdict_with_the_engines_sentence()
    {
        var cli = new FakeEmailCli { AccountsOutput = PersoReadyWorkNot, CheckOutput = ReachableSentence, HoldChecks = true };
        var (section, _) = BuildWithCli(Two, cli);
        await section.RefreshStatesAsync();
        var perso = Row(section, "perso");
        Assert.False(perso.IsTesting);
        Assert.False(perso.HasLastCheck);

        var testing = perso.TestCommand.ExecuteAsync();
        await cli.Parked.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.True(perso.IsTesting);
        Assert.False(perso.HasLastCheck);
        // A second click cannot start a second connection.
        Assert.False(perso.TestCommand.CanExecute(null));
        Assert.Equal(["email", "check", "perso", "--events", "jsonl", "--settings", SettingsFile], cli.Requests[^1].Arguments);

        cli.Release();
        await testing.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.False(perso.IsTesting);
        Assert.True(perso.HasLastCheck);
        Assert.True(perso.LastCheckReachable);
        Assert.Equal(EmailCheckKind.Reachable, perso.LastCheck?.Kind);
        Assert.Equal("Reachable", perso.LastCheckHeadline);
        // The number of folders is the engine's to say: its sentence shows as printed.
        Assert.Equal(ReachableSentence, perso.LastCheckDetail);
        Assert.True(perso.TestCommand.CanExecute(null));
        Assert.Equal(0, cli.Live);
    }

    [Theory]
    [InlineData(1, "The password of e-mail account 'perso' is read from the environment variable GMAIL_APP_PASSWORD, which is not set.", "To fix on this machine")]
    [InlineData(2, "The server refused the credentials of e-mail account 'perso'.", "The server refused or did not answer")]
    [InlineData(2, "Could not reach imap.gmail.com:993.", "The server refused or did not answer")]
    public async Task A_refused_test_says_which_side_refused_and_shows_the_engines_sentence_without_the_verbs_prefix(
        int exitCode, string sentence, string headline)
    {
        var cli = new FakeEmailCli
        {
            AccountsOutput = PersoReadyWorkNot,
            CheckExitCode = exitCode,
            CheckError = "orkeon email check: " + sentence,
        };
        var (section, _) = BuildWithCli(Two, cli);
        var perso = Row(section, "perso");

        await perso.TestCommand.ExecuteAsync();

        Assert.True(perso.HasLastCheck);
        Assert.False(perso.LastCheckReachable);
        Assert.Equal(headline, perso.LastCheckHeadline);
        Assert.Equal(sentence, perso.LastCheckDetail);
    }

    [Fact]
    public async Task A_test_refused_for_want_of_a_sign_in_says_to_sign_in_from_here()
    {
        var cli = new FakeEmailCli
        {
            AccountsOutput = PersoReadyWorkNot,
            CheckExitCode = 1,
            CheckOutput = """{"v":1,"seq":1,"ts":"2026-10-09T10:00:00Z","kind":"error","code":"LoginRequired","message":"E-mail account 'perso' needs an OAuth sign-in: run `orkeon email login perso` in a terminal.","recoverable":false}""",
        };
        var (section, _) = BuildWithCli(Two, cli);
        var perso = Row(section, "perso");

        await perso.TestCommand.ExecuteAsync();

        // The engine's sentence sends to a terminal; the screen has the button, and says so.
        Assert.Equal("Sign in first", perso.LastCheckHeadline);
        Assert.StartsWith("E-mail account 'perso' needs an OAuth sign-in", perso.LastCheckDetail, StringComparison.Ordinal);
        Assert.True(perso.HasLastCheckHint);
        Assert.Equal("« Sign in », above, opens the sign-in; the test passes once the account is signed in.", perso.LastCheckHint);
    }

    [Fact]
    public async Task A_test_refused_for_want_of_a_secret_says_where_to_type_it()
    {
        var cli = new FakeEmailCli
        {
            AccountsOutput = PersoReadyWorkNot,
            CheckExitCode = 1,
            CheckOutput = """{"v":1,"seq":1,"ts":"2026-10-09T10:00:00Z","kind":"error","code":"CredentialMissing","message":"The password of e-mail account 'perso' is read from the environment variable GMAIL_APP_PASSWORD, which is not set.","recoverable":false}""",
        };
        var (section, _) = BuildWithCli(Two, cli);
        var perso = Row(section, "perso");

        await perso.TestCommand.ExecuteAsync();

        Assert.Equal("Store the password first", perso.LastCheckHeadline);
        Assert.Equal("Type it on the Sign-in tab and save it; the test passes once it is stored.", perso.LastCheckHint);
    }

    [Fact]
    public async Task Without_the_cli_a_test_says_it_could_not_connect_and_why()
    {
        var (section, cli) = BuildWithCli(Two, binaryPresent: false);
        var perso = Row(section, "perso");

        await perso.TestCommand.ExecuteAsync();

        Assert.Empty(cli.Requests);
        Assert.False(perso.LastCheckReachable);
        Assert.Equal("Could not connect", perso.LastCheckHeadline);
        Assert.Equal(EngineMissing, perso.LastCheckDetail);
    }

    [Fact]
    public async Task A_new_test_clears_the_verdict_of_the_one_before()
    {
        var cli = new FakeEmailCli { AccountsOutput = PersoReadyWorkNot, CheckOutput = ReachableSentence };
        var (section, _) = BuildWithCli(Two, cli);
        var perso = Row(section, "perso");
        await perso.TestCommand.ExecuteAsync();
        Assert.True(perso.HasLastCheck);

        cli.HoldChecks = true;
        var testing = perso.TestCommand.ExecuteAsync();
        await cli.Parked.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.False(perso.HasLastCheck);
        Assert.Null(perso.LastCheckHeadline);

        cli.Release();
        await testing.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_edit_of_the_account_clears_the_verdict_of_a_declaration_that_is_no_longer_the_one_tested()
    {
        var cli = new FakeEmailCli { AccountsOutput = PersoReadyWorkNot, CheckOutput = ReachableSentence };
        var (section, _) = BuildWithCli(Two, cli);
        var perso = Row(section, "perso");
        var work = Row(section, "work");
        await perso.TestCommand.ExecuteAsync();
        await work.TestCommand.ExecuteAsync();
        Assert.True(perso.LastCheckReachable);
        var raised = new List<string>();
        perso.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        perso.IncomingHost = "imap.example.org";

        // "Reachable" was said of another server: nothing is known of this one until it is tested.
        Assert.False(perso.HasLastCheck);
        Assert.Null(perso.LastCheckHeadline);
        Assert.Null(perso.LastCheckDetail);
        Assert.Contains(nameof(EmailAccountRowViewModel.HasLastCheck), raised);
        // The verdict of an account nobody touched stays.
        Assert.True(work.LastCheckReachable);
    }

    [Fact]
    public async Task A_rename_clears_the_verdict_of_the_name_that_was_tested()
    {
        var cli = new FakeEmailCli { AccountsOutput = PersoReadyWorkNot, CheckOutput = ReachableSentence };
        var (section, _) = BuildWithCli(Two, cli);
        var perso = Row(section, "perso");
        await perso.TestCommand.ExecuteAsync();
        Assert.True(perso.LastCheckReachable);

        section.RenameAccountCommand.Execute(perso);
        perso.RenameText = "home";
        perso.ConfirmRenameCommand.Execute(null);

        // The tokens and the secrets are filed under the name: "home" was never tested.
        Assert.Equal("home", perso.Name);
        Assert.False(perso.HasLastCheck);
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("rename")]
    [InlineData("remove")]
    public async Task Editing_renaming_or_removing_the_account_stops_its_test_in_flight_and_shows_no_verdict(string change)
    {
        var cli = new FakeEmailCli { AccountsOutput = PersoReadyWorkNot, CheckOutput = ReachableSentence, HoldChecks = true };
        var (section, _) = BuildWithCli(Two, cli);
        var perso = Row(section, "perso");
        var testing = perso.TestCommand.ExecuteAsync();
        await cli.Parked.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Change(section, perso, change);

        // Released after the change: a child that was not stopped by it would end on its verdict.
        cli.Release();
        await testing.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(1, cli.Cancelled);
        Assert.Equal(0, cli.Live);
        Assert.False(perso.IsTesting);
        Assert.False(perso.HasLastCheck);
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("rename")]
    public async Task A_verdict_that_arrives_after_the_account_moved_is_not_shown(string change)
    {
        // The child had answered already when it was stopped: its verdict is of the account as it was.
        var cli = new FakeEmailCli
        {
            AccountsOutput = PersoReadyWorkNot, CheckOutput = ReachableSentence, HoldChecks = true, DeafToStop = true,
        };
        var (section, _) = BuildWithCli(Two, cli);
        var perso = Row(section, "perso");
        var testing = perso.TestCommand.ExecuteAsync();
        await cli.Parked.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Change(section, perso, change);
        cli.Release();
        await testing.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.False(perso.IsTesting);
        Assert.False(perso.HasLastCheck);
        // The account as it now stands can be tested, on a click.
        Assert.True(perso.TestCommand.CanExecute(null));
    }

    /// <summary>Plays one of the three ways an account moves under a test in flight.</summary>
    private static void Change(EmailSectionViewModel section, EmailAccountRowViewModel row, string change)
    {
        switch (change)
        {
            case "edit":
                row.IncomingHost = "imap.example.org";
                break;
            case "rename":
                section.RenameAccountCommand.Execute(row);
                row.RenameText = "home";
                row.ConfirmRenameCommand.Execute(null);
                break;
            default:
                section.RemoveAccountCommand.Execute(row);
                row.ConfirmRemoveCommand.Execute(null);
                break;
        }
    }

    [Fact]
    public async Task Stopping_the_tabs_activity_stops_a_test_in_flight_and_leaves_no_child_and_no_verdict()
    {
        var cli = new FakeEmailCli { AccountsOutput = PersoReadyWorkNot, CheckOutput = ReachableSentence, HoldChecks = true };
        var (section, _) = BuildWithCli(Two, cli);
        var perso = Row(section, "perso");
        var work = Row(section, "work");
        var first = perso.TestCommand.ExecuteAsync();
        var second = work.TestCommand.ExecuteAsync();
        await Polling.WaitUntilAsync(() => cli.Live == 2);

        section.StopActivity();
        await Task.WhenAll(first, second).WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(2, cli.Cancelled);
        Assert.Equal(0, cli.Live);
        Assert.False(perso.IsTesting);
        Assert.False(work.IsTesting);
        // A test nobody waited for is no verdict on the account.
        Assert.False(perso.HasLastCheck);
        Assert.False(work.HasLastCheck);
        Assert.True(perso.TestCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_language_switch_says_the_verdict_again_and_leaves_the_engines_sentence_alone()
    {
        var strings = new SwitchableStrings();
        var cli = new FakeEmailCli { AccountsOutput = PersoReadyWorkNot, CheckOutput = ReachableSentence };
        var (section, _) = BuildWithCli(Two, cli, strings: strings);
        var perso = Row(section, "perso");
        await perso.TestCommand.ExecuteAsync();

        strings.SwitchToFrench();

        Assert.Equal(SwitchableStrings.FrenchReachable, perso.LastCheckHeadline);
        Assert.Equal(ReachableSentence, perso.LastCheckDetail);
    }

    // ── the screen: when the states are read, and what leaving stops ──

    [Fact]
    public void Arriving_on_the_mails_tab_reads_the_states_of_the_saved_file_and_no_other_tab_does()
    {
        var (screen, config, cli) = BuildScreen(UiModeViewModel.Novice);

        screen.ShowToolsCommand.Execute(null);
        screen.ShowFoldersCommand.Execute(null);
        Assert.Empty(cli.Requests);

        screen.ShowMailsCommand.Execute(null);

        var request = Assert.Single(cli.Requests);
        Assert.Equal(["email", "accounts", "--json", "--settings", SettingsFile], request.Arguments);
        Assert.Equal(EmailAccountReadiness.Ready, Row(config.Email, "perso").StateKind);
        Assert.Equal(EmailAccountReadiness.NotReady, Row(config.Email, "work").StateKind);
        Assert.Equal(0, cli.CheckRuns);
    }

    [Fact]
    public async Task A_save_reads_the_states_again_while_the_tab_shows_and_not_from_another_tab()
    {
        var (screen, config, cli) = BuildScreen(UiModeViewModel.Expert);

        Assert.True(await config.SaveAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, cli.ListRuns);

        screen.ShowMailsCommand.Execute(null);
        Assert.Equal(1, cli.ListRuns);

        cli.AccountsOutput = """[{"name":"perso","provider":"Gmail","reads":"Imap","auth":"Password","ready":false,"problem":"after the save"}]""";
        Assert.True(await config.SaveAsync(TestContext.Current.CancellationToken));

        Assert.Equal(2, cli.ListRuns);
        Assert.Equal("after the save", Row(config.Email, "perso").StateDetail);
    }

    [Fact]
    public void In_novice_mode_an_edit_saves_and_the_states_follow_without_a_stale_file_notice()
    {
        var (screen, config, cli) = BuildScreen(UiModeViewModel.Novice);
        screen.ShowMailsCommand.Execute(null);

        Row(config.Email, "perso").DisplayName = "Me";

        Assert.False(config.IsDirty);
        Assert.False(config.Email.IsStateOfSavedFile);
        Assert.Equal(2, cli.ListRuns);
        Assert.True(Row(config.Email, "perso").TestCommand.CanExecute(null));
    }

    [Fact]
    public void In_novice_mode_a_burst_of_keystrokes_reads_the_states_once_when_the_typing_pauses()
    {
        var delay = new ManualUiDelay();
        var (screen, config, cli) = BuildScreen(UiModeViewModel.Novice, delay: delay);

        // Arriving on the tab reads at once: nothing waits for a pause.
        screen.ShowMailsCommand.Execute(null);
        Assert.Equal(1, cli.ListRuns);
        Assert.Equal(0, delay.Pending);
        var perso = Row(config.Email, "perso");

        foreach (var typed in new[] { "M", "Me", "Me ", "Me a", "Me at", "Me at h", "Me at home" })
            perso.DisplayName = typed;

        // Every keystroke saved the file and none asked the engine: one pause waits, the last one's.
        Assert.False(config.IsDirty);
        Assert.Equal(1, cli.ListRuns);
        Assert.Equal(1, delay.Pending);
        Assert.All(delay.Requested, pause => Assert.Equal(EmailSectionViewModel.StatesPause, pause));

        cli.AccountsOutput = """[{"name":"perso","provider":"Gmail","reads":"Imap","auth":"Password","ready":false,"problem":"after the typing"}]""";
        delay.Elapse();

        Assert.Equal(2, cli.ListRuns);
        Assert.Equal(0, delay.Pending);
        Assert.Equal("after the typing", Row(config.Email, "perso").StateDetail);
    }

    [Fact]
    public void Coming_back_on_the_screen_reads_the_states_at_once_and_drops_the_reading_that_waited()
    {
        var delay = new ManualUiDelay();
        var (screen, config, cli) = BuildScreen(UiModeViewModel.Novice, delay: delay);
        screen.ShowMailsCommand.Execute(null);
        Row(config.Email, "perso").DisplayName = "Me";
        Assert.Equal(1, delay.Pending);

        screen.Enter();

        // Read now, of the file as saved: the reading that waited would say the same again.
        Assert.Equal(2, cli.ListRuns);
        Assert.Equal(0, delay.Pending);
        delay.Elapse();
        Assert.Equal(2, cli.ListRuns);
    }

    [Fact]
    public void Leaving_the_mails_tab_drops_the_reading_that_waited_for_the_typing_to_pause()
    {
        var delay = new ManualUiDelay();
        var (screen, config, cli) = BuildScreen(UiModeViewModel.Novice, delay: delay);
        screen.ShowMailsCommand.Execute(null);
        Row(config.Email, "perso").DisplayName = "Me";
        Assert.Equal(1, delay.Pending);

        screen.ShowModelCommand.Execute(null);

        // No child is started for a tab nobody looks at.
        Assert.Equal(0, delay.Pending);
        delay.Elapse();
        Assert.Equal(1, cli.ListRuns);
    }

    [Fact]
    public async Task In_novice_mode_a_first_password_kept_names_its_variable_saves_and_reads_the_states_once()
    {
        // The value kept, the name it writes into the file and the save that follows are one gesture.
        var delay = new ManualUiDelay();
        var (screen, config, cli) = BuildScreen(UiModeViewModel.Novice, Unnamed, delay: delay);
        screen.ShowMailsCommand.Execute(null);
        var perso = Row(config.Email, "perso");

        perso.Password!.KeyInput = "s3cret";
        await perso.Password.StoreAsync();

        Assert.Equal("EMAIL_PERSO_PASSWORD", perso.PasswordEnvVar);
        Assert.False(config.IsDirty);
        Assert.Equal(1, cli.ListRuns);
        Assert.Equal(1, delay.Pending);

        delay.Elapse();

        Assert.Equal(2, cli.ListRuns);
        Assert.Equal(0, delay.Pending);
    }

    [Fact]
    public async Task In_expert_mode_an_unsaved_edit_marks_the_states_and_disables_the_test_until_the_save()
    {
        var (screen, config, cli) = BuildScreen(UiModeViewModel.Expert);
        screen.ShowMailsCommand.Execute(null);
        var perso = Row(config.Email, "perso");

        perso.DisplayName = "Me";

        // Typing reads nothing: the saved file did not move.
        Assert.Equal(1, cli.ListRuns);
        Assert.True(config.IsDirty);
        Assert.True(config.Email.IsStateOfSavedFile);
        Assert.False(perso.TestCommand.CanExecute(null));
        Assert.Equal(EmailAccountReadiness.Ready, perso.StateKind);

        Assert.True(await config.SaveAsync(TestContext.Current.CancellationToken));

        Assert.False(config.Email.IsStateOfSavedFile);
        Assert.Equal(2, cli.ListRuns);
        Assert.True(Row(config.Email, "perso").TestCommand.CanExecute(null));
    }

    [Fact]
    public async Task Leaving_the_mails_tab_stops_a_test_in_flight()
    {
        var cli = new FakeEmailCli { AccountsOutput = PersoReadyWorkNot, CheckOutput = ReachableSentence, HoldChecks = true };
        var (screen, config, _) = BuildScreen(UiModeViewModel.Novice, cli: cli);
        screen.ShowMailsCommand.Execute(null);
        var perso = Row(config.Email, "perso");
        var testing = perso.TestCommand.ExecuteAsync();
        await cli.Parked.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        screen.ShowModelCommand.Execute(null);
        await testing.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(1, cli.Cancelled);
        Assert.Equal(0, cli.Live);
        Assert.False(perso.IsTesting);
        Assert.False(perso.HasLastCheck);
    }

    [Fact]
    public async Task Leaving_the_settings_screen_stops_a_test_in_flight_and_coming_back_reads_the_states_again()
    {
        var cli = new FakeEmailCli { AccountsOutput = PersoReadyWorkNot, CheckOutput = ReachableSentence, HoldChecks = true };
        var (screen, config, _) = BuildScreen(UiModeViewModel.Novice, cli: cli);
        screen.ShowMailsCommand.Execute(null);
        var testing = Row(config.Email, "perso").TestCommand.ExecuteAsync();
        await cli.Parked.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        // Another sidebar entry, or the window closing: the tab is still the showing one.
        screen.Leave();
        await testing.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(1, cli.Cancelled);
        Assert.Equal(0, cli.Live);
        Assert.True(screen.IsMailsTab);

        screen.Enter();

        Assert.Equal(2, cli.ListRuns);

        // Coming back onto another tab reads nothing.
        screen.ShowModelCommand.Execute(null);
        screen.Enter();
        Assert.Equal(2, cli.ListRuns);
    }

    [Fact]
    public void A_settings_tab_built_without_a_runner_reads_no_state_on_arrival()
    {
        var config = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
            KeyStore = new FakeApiKeyStore(),
        });
        config.SetDocument(AppSettingsDocument.Parse(Two));
        var screen = new SettingsScreenViewModel(
            config,
            new ModelProfilesViewModel(new InMemoryModelProfileStore(), config.Llm),
            new UiModeViewModel(UiModeViewModel.Novice));

        screen.ShowMailsCommand.Execute(null);

        Assert.All(config.Email.Accounts, row => Assert.False(row.HasState));
    }

    [Fact]
    public void A_settings_tab_given_the_windows_runner_asks_the_same_binary_for_the_states()
    {
        var cli = new FakeEmailCli { AccountsOutput = PersoReadyWorkNot };
        var config = new ConfigTabViewModel(
            new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                KeyStore = new FakeApiKeyStore(),
                ProcessRunner = new OrkeonProcessRunner(cli, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            },
            globalPathOverride: SettingsFile);
        config.SetDocument(AppSettingsDocument.Parse(Two), SettingsFile);
        var screen = new SettingsScreenViewModel(
            config,
            new ModelProfilesViewModel(new InMemoryModelProfileStore(), config.Llm),
            new UiModeViewModel(UiModeViewModel.Novice));

        screen.ShowMailsCommand.Execute(null);

        Assert.Equal(1, cli.ListRuns);
        Assert.Equal(Path.Combine("/opt/orkeon", "orkeon"), cli.Requests[0].FileName);
        Assert.Equal(EmailAccountReadiness.Ready, Row(config.Email, "perso").StateKind);
    }
}
