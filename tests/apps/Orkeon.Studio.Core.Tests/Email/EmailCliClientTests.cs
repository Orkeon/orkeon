using Orkeon.Studio.Core.Email;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Email;

/// <summary>
/// STUDIO-69 — the Studio side of <c>orkeon email accounts</c> and <c>orkeon email check</c>:
/// the argv always names the settings file, the answer is read off standard output alone, and
/// whatever the process did — no binary, a refusal, an answer that is no JSON — comes back as a
/// typed result, never as an exception. Everything runs on <see cref="FakeProcessLauncher"/>.
/// </summary>
public sealed class EmailCliClientTests
{
    private static readonly string InstallDirectory = Path.Combine("/", "opt", "orkeon");
    private static readonly string BinaryPath = Path.Combine(InstallDirectory, "orkeon");
    private static readonly string Settings = Path.Combine("/", "home", "me", "my settings", "appsettings.json");

    // What `accounts --json` prints: indented, one property per line, nulls written.
    private static readonly string[] TwoAccounts =
    [
        "[",
        "  {",
        "    \"name\": \"hotmail\",",
        "    \"address\": \"me@hotmail.com\",",
        "    \"provider\": \"Outlook\",",
        "    \"reads\": \"Graph\",",
        "    \"sends\": \"Graph\",",
        "    \"rights\": \"Read, Send\",",
        "    \"auth\": \"OAuth2\",",
        "    \"default\": false,",
        "    \"ready\": false,",
        "    \"problem\": \"E-mail account 'hotmail' is not signed in: run `orkeon email login hotmail`.\"",
        "  },",
        "  {",
        "    \"name\": \"perso\",",
        "    \"address\": \"me@gmail.com\",",
        "    \"provider\": \"Gmail\",",
        "    \"reads\": \"Imap\",",
        "    \"sends\": null,",
        "    \"rights\": \"Read\",",
        "    \"auth\": \"Password\",",
        "    \"default\": true,",
        "    \"ready\": true,",
        "    \"problem\": null",
        "  }",
        "]",
    ];

    private static EmailCliClient CreateClient(FakeProcessLauncher launcher, bool binaryPresent = true)
    {
        var probe = new FakeExecutableProbe { BaseDirectory = InstallDirectory };
        if (binaryPresent)
            probe.WithFile(BinaryPath);

        return new EmailCliClient(new OrkeonProcessRunner(launcher, new OrkeonBinaryLocator(probe, ["orkeon"])));
    }

    // ── the list ──

    [Fact]
    public async Task Listing_asks_for_the_json_form_of_the_named_settings_file_and_reads_every_account()
    {
        var launcher = new FakeProcessLauncher().WithStandardOutput(TwoAccounts);

        var result = await CreateClient(launcher).ListAsync(Settings, TestContext.Current.CancellationToken);

        var request = Assert.Single(launcher.Requests);
        Assert.Equal(BinaryPath, request.FileName);
        // The path travels as one argv slot, spaces included, and is always named: without it the
        // verb resolves from Studio's own working directory and may read another file.
        Assert.Equal(["email", "accounts", "--json", "--settings", Settings], request.Arguments);

        Assert.Null(result.Failure);
        Assert.Equal(2, result.Accounts.Count);

        var hotmail = result.Accounts[0];
        Assert.Equal("hotmail", hotmail.Name);
        Assert.False(hotmail.Ready);
        Assert.Equal("E-mail account 'hotmail' is not signed in: run `orkeon email login hotmail`.", hotmail.Problem);
        Assert.Equal("Graph", hotmail.Reads);
        Assert.Equal("Graph", hotmail.Sends);
        Assert.Equal("OAuth2", hotmail.Auth);
        Assert.False(hotmail.IsDefault);
        Assert.False(hotmail.IsSetAside);

        var perso = result.Accounts[1];
        Assert.Equal("perso", perso.Name);
        Assert.True(perso.Ready);
        Assert.Null(perso.Problem);
        Assert.Equal("Imap", perso.Reads);
        Assert.Null(perso.Sends);
        Assert.Equal("Password", perso.Auth);
        Assert.True(perso.IsDefault);
    }

    [Fact]
    public async Task An_empty_array_is_no_account_and_no_failure()
    {
        var launcher = new FakeProcessLauncher().WithStandardOutput("[]");

        var result = await CreateClient(launcher).ListAsync(Settings, TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.Empty(result.Accounts);
    }

    [Fact]
    public async Task Log_lines_on_standard_error_do_not_disturb_the_answer()
    {
        // The verb says which file it read, and its logger writes there too: a bracket in a log
        // line must never be taken for the start of the answer.
        var launcher = new FakeProcessLauncher()
            .WithStandardError("Using settings: " + Settings, "warn: Orkeon.Tools.Email[0] account [broken] was set aside")
            .WithStandardOutput(TwoAccounts)
            .WithStandardError("info: done ]");

        var result = await CreateClient(launcher).ListAsync(Settings, TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.Equal(["hotmail", "perso"], result.Accounts.Select(account => account.Name));
    }

    [Fact]
    public async Task An_account_the_engine_set_aside_is_told_apart_from_one_that_is_not_ready()
    {
        var launcher = new FakeProcessLauncher().WithStandardOutput(
            """[{"name":"broken","address":null,"provider":"?","reads":null,"sends":null,"rights":null,"auth":null,"default":false,"ready":false,"problem":"Rights is required"}]""");

        var result = await CreateClient(launcher).ListAsync(Settings, TestContext.Current.CancellationToken);

        var broken = Assert.Single(result.Accounts);
        Assert.True(broken.IsSetAside);
        Assert.False(broken.Ready);
        Assert.Equal("Rights is required", broken.Problem);
    }

    [Fact]
    public async Task An_unknown_property_is_ignored_and_a_missing_one_takes_its_default()
    {
        // A CLI one version ahead adds a field; one version behind lacks one. Neither breaks the list.
        var launcher = new FakeProcessLauncher().WithStandardOutput(
            """[{"name":"perso","ready":true,"quota":{"used":12},"lastSeen":"2026-10-06"},{"name":"work"},{"ready":true},"stray",{"name":"  "}]""");

        var result = await CreateClient(launcher).ListAsync(Settings, TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.Equal(2, result.Accounts.Count);
        Assert.True(result.Accounts[0].Ready);

        var work = result.Accounts[1];
        Assert.Equal("work", work.Name);
        Assert.False(work.Ready);
        Assert.Null(work.Problem);
        Assert.Null(work.Reads);
        Assert.Null(work.Sends);
        Assert.Null(work.Auth);
        Assert.False(work.IsDefault);
        Assert.False(work.IsSetAside);
    }

    [Fact]
    public async Task An_account_is_found_by_its_name_whatever_the_case()
    {
        var launcher = new FakeProcessLauncher().WithStandardOutput(TwoAccounts);

        var result = await CreateClient(launcher).ListAsync(Settings, TestContext.Current.CancellationToken);

        Assert.Equal("perso", result.Find("PERSO")?.Name);
        Assert.Null(result.Find("work"));
    }

    [Fact]
    public async Task An_answer_that_is_not_json_is_a_typed_failure_with_its_reason()
    {
        var launcher = new FakeProcessLauncher().WithStandardOutput("perso (default) — me@gmail.com", "  ready");

        var result = await CreateClient(launcher).ListAsync(Settings, TestContext.Current.CancellationToken);

        Assert.Empty(result.Accounts);
        Assert.NotNull(result.Failure);
        Assert.Equal(EmailCliFailureKind.Unreadable, result.Failure.Kind);
        Assert.Contains("JSON", result.Failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_json_is_a_typed_failure_too()
    {
        var launcher = new FakeProcessLauncher().WithStandardOutput("""[{"name":"perso",]""");

        var result = await CreateClient(launcher).ListAsync(Settings, TestContext.Current.CancellationToken);

        Assert.Empty(result.Accounts);
        Assert.Equal(EmailCliFailureKind.Unreadable, result.Failure?.Kind);
    }

    [Fact]
    public async Task A_non_zero_exit_code_is_a_typed_failure_that_carries_what_the_verb_said()
    {
        var launcher = new FakeProcessLauncher { ExitCode = 1 }
            .WithStandardError("Using settings: " + Settings, "orkeon email accounts: Settings file not found: " + Settings);

        var result = await CreateClient(launcher).ListAsync(Settings, TestContext.Current.CancellationToken);

        Assert.Empty(result.Accounts);
        Assert.NotNull(result.Failure);
        Assert.Equal(EmailCliFailureKind.Stopped, result.Failure.Kind);
        Assert.Equal(1, result.Failure.ExitCode);
        Assert.Equal("Settings file not found: " + Settings, result.Failure.Reason);
    }

    [Fact]
    public async Task A_non_zero_exit_code_that_said_nothing_still_has_a_reason()
    {
        var launcher = new FakeProcessLauncher { ExitCode = 42 };

        var result = await CreateClient(launcher).ListAsync(Settings, TestContext.Current.CancellationToken);

        Assert.Equal(EmailCliFailureKind.Stopped, result.Failure?.Kind);
        Assert.Contains("42", result.Failure!.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_binary_is_a_typed_failure_and_spawns_nothing()
    {
        var launcher = new FakeProcessLauncher();

        var result = await CreateClient(launcher, binaryPresent: false).ListAsync(Settings, TestContext.Current.CancellationToken);

        Assert.Empty(result.Accounts);
        Assert.NotNull(result.Failure);
        Assert.Equal(EmailCliFailureKind.EngineMissing, result.Failure.Kind);
        Assert.Contains("orkeon", result.Failure.Reason, StringComparison.Ordinal);
        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task A_cancelled_listing_says_so_instead_of_throwing()
    {
        var launcher = new FakeProcessLauncher { RunsUntilCancelled = true };
        using var cts = new CancellationTokenSource();

        var listing = CreateClient(launcher).ListAsync(Settings, cts.Token);
        await cts.CancelAsync();
        var result = await listing;

        Assert.Equal(EmailCliFailureKind.Cancelled, result.Failure?.Kind);
    }

    // ── the connection check ──

    [Fact]
    public async Task Checking_names_the_account_and_the_settings_file_and_a_zero_exit_is_reachable_with_the_engines_sentence()
    {
        const string Sentence = "E-mail account 'perso' is reachable: 12 folder(s), inbox 340 message(s), 3 unread.";
        var launcher = new FakeProcessLauncher()
            .WithStandardError("Using settings: " + Settings)
            .WithStandardOutput(Sentence);

        var outcome = await CreateClient(launcher).CheckAsync("perso", Settings, TestContext.Current.CancellationToken);

        var request = Assert.Single(launcher.Requests);
        Assert.Equal(["email", "check", "perso", "--events", "jsonl", "--settings", Settings], request.Arguments);
        Assert.Equal(EmailCheckKind.Reachable, outcome.Kind);
        // The sentence is the engine's, as printed: nothing is read out of it.
        Assert.Equal(Sentence, outcome.Sentence);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact]
    public async Task Exit_code_one_is_a_refusal_this_machine_can_fix_and_the_message_loses_the_verbs_prefix()
    {
        var launcher = new FakeProcessLauncher { ExitCode = 1 }.WithStandardError(
            "Using settings: " + Settings,
            "orkeon email check: E-mail account 'perso' has no password: the environment variable GMAIL_APP_PASSWORD is not set.");

        var outcome = await CreateClient(launcher).CheckAsync("perso", Settings, TestContext.Current.CancellationToken);

        Assert.Equal(EmailCheckKind.OperatorFixable, outcome.Kind);
        Assert.Equal("E-mail account 'perso' has no password: the environment variable GMAIL_APP_PASSWORD is not set.", outcome.Sentence);
        Assert.Equal(1, outcome.ExitCode);
    }

    [Fact]
    public async Task An_error_event_gives_the_refusal_its_code_and_its_message()
    {
        var launcher = new FakeProcessLauncher { ExitCode = 1 }
            .WithStandardError("Using settings: " + Settings)
            .WithStandardOutput(
                """{"v":1,"seq":1,"ts":"2026-10-09T10:00:00Z","kind":"error","code":"LoginRequired","message":"E-mail account 'perso' needs an OAuth sign-in: run `orkeon email login perso` in a terminal.","recoverable":false}""");

        var outcome = await CreateClient(launcher).CheckAsync("perso", Settings, TestContext.Current.CancellationToken);

        Assert.Equal(EmailCheckKind.OperatorFixable, outcome.Kind);
        Assert.Equal(EmailCheckCodes.LoginRequired, outcome.Code);
        Assert.Equal("E-mail account 'perso' needs an OAuth sign-in: run `orkeon email login perso` in a terminal.", outcome.Sentence);
        Assert.Equal(1, outcome.ExitCode);
    }

    [Fact]
    public async Task A_completed_event_is_reachable_with_the_engines_summary_and_no_code()
    {
        var launcher = new FakeProcessLauncher()
            .WithStandardError("Using settings: " + Settings)
            .WithStandardOutput(
                """{"v":1,"seq":1,"ts":"2026-10-09T10:00:00Z","kind":"email.check.completed","account":"perso","folders":12,"inbox_total":340,"inbox_unread":3,"summary":"E-mail account 'perso' is reachable: 12 folder(s), inbox 340 message(s), 3 unread."}""");

        var outcome = await CreateClient(launcher).CheckAsync("perso", Settings, TestContext.Current.CancellationToken);

        Assert.Equal(EmailCheckKind.Reachable, outcome.Kind);
        Assert.Null(outcome.Code);
        Assert.Equal("E-mail account 'perso' is reachable: 12 folder(s), inbox 340 message(s), 3 unread.", outcome.Sentence);
    }

    [Fact]
    public async Task Exit_code_two_is_the_server_or_the_network_and_the_message_loses_the_verbs_prefix()
    {
        var launcher = new FakeProcessLauncher { ExitCode = 2 }.WithStandardError(
            "Using settings: " + Settings,
            "orkeon email check: The server refused the credentials of e-mail account 'perso'.");

        var outcome = await CreateClient(launcher).CheckAsync("perso", Settings, TestContext.Current.CancellationToken);

        Assert.Equal(EmailCheckKind.ServerOrNetwork, outcome.Kind);
        Assert.Equal("The server refused the credentials of e-mail account 'perso'.", outcome.Sentence);
        Assert.Equal(2, outcome.ExitCode);
    }

    [Fact]
    public async Task A_refusal_that_printed_no_message_still_has_a_sentence()
    {
        var launcher = new FakeProcessLauncher { ExitCode = 2 };

        var outcome = await CreateClient(launcher).CheckAsync("perso", Settings, TestContext.Current.CancellationToken);

        Assert.Equal(EmailCheckKind.ServerOrNetwork, outcome.Kind);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Sentence));
    }

    [Fact]
    public async Task An_exit_code_the_verb_does_not_document_is_no_verdict_on_the_account()
    {
        var launcher = new FakeProcessLauncher { ExitCode = 42 };

        var outcome = await CreateClient(launcher).CheckAsync("perso", Settings, TestContext.Current.CancellationToken);

        Assert.Equal(EmailCheckKind.Unavailable, outcome.Kind);
        Assert.Contains("42", outcome.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_binary_the_check_is_unavailable_and_spawns_nothing()
    {
        var launcher = new FakeProcessLauncher();

        var outcome = await CreateClient(launcher, binaryPresent: false).CheckAsync("perso", Settings, TestContext.Current.CancellationToken);

        Assert.Equal(EmailCheckKind.Unavailable, outcome.Kind);
        Assert.Contains("orkeon", outcome.Sentence, StringComparison.Ordinal);
        Assert.Null(outcome.ExitCode);
        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task Cancelling_a_check_stops_the_child_and_says_so_instead_of_throwing()
    {
        // The fake child only ends when its token fires: a check that did not hand its token to
        // the launcher would never come back, and the test's own token would end the wait.
        var launcher = new FakeProcessLauncher { RunsUntilCancelled = true };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var check = CreateClient(launcher).CheckAsync("perso", Settings, cts.Token);
        Assert.False(check.IsCompleted);
        await cts.CancelAsync();
        var outcome = await check;

        Assert.Equal(EmailCheckKind.Cancelled, outcome.Kind);
        Assert.Equal(1, launcher.StartCount);
    }

    // ── the sign-in (STUDIO-70) ──

    private const string Redirect = "http://127.0.0.1:53124/?code=auth-code-1&state=state-1";

    private const string DeviceCodeLine =
        """{"v":2,"seq":1,"ts":"2026-10-07T09:00:00Z","kind":"email.login.device_code","verification_uri":"https://microsoft.com/devicelogin","user_code":"WXYZ-1234","expires_in":900}""";

    private const string AuthorizationLine =
        """{"v":2,"seq":1,"ts":"2026-10-07T09:00:00Z","kind":"email.login.authorization_url","authorization_uri":"https://accounts.google.com/o/oauth2/v2/auth?client_id=c&state=state-1"}""";

    private static string CompletedLine(int seq, string account) =>
        $$"""{"v":2,"seq":{{seq}},"ts":"2026-10-07T09:00:05Z","kind":"email.login.completed","account":"{{account}}"}""";

    private static string RejectedLine(int seq, string message) =>
        $$"""{"v":2,"seq":{{seq}},"ts":"2026-10-07T09:00:03Z","kind":"email.login.redirect_rejected","message":"{{message}}"}""";

    private static string ErrorLine(int seq, string code, string message) =>
        $$"""{"v":2,"seq":{{seq}},"ts":"2026-10-07T09:00:05Z","kind":"error","code":"{{code}}","message":"{{message}}","recoverable":false}""";

    private static EmailCliClient CreateClient(IProcessLauncher launcher)
    {
        var probe = new FakeExecutableProbe { BaseDirectory = InstallDirectory }.WithFile(BinaryPath);
        return new EmailCliClient(new OrkeonProcessRunner(launcher, new OrkeonBinaryLocator(probe, ["orkeon"])));
    }

    [Fact]
    public async Task Signing_in_asks_for_events_on_the_named_settings_file_and_reads_the_device_code_then_the_completion()
    {
        var launcher = new FakeProcessLauncher()
            .WithStandardError("Using settings: " + Settings)
            .WithStandardOutput(DeviceCodeLine, CompletedLine(2, "hotmail"));
        var steps = new List<EmailLoginStep>();

        var ended = await CreateClient(launcher).LoginAsync("hotmail", Settings, steps.Add, cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(launcher.Requests);
        Assert.Equal(["email", "login", "hotmail", "--events", "jsonl", "--settings", Settings], request.Arguments);
        // The verb reads its standard input to learn that Studio is still there: it is always piped.
        Assert.NotNull(request.OnInputReady);

        var code = Assert.Single(steps);
        Assert.Equal(EmailLoginStepKind.DeviceCode, code.Kind);
        Assert.Equal("https://microsoft.com/devicelogin", code.Address);
        Assert.Equal("WXYZ-1234", code.UserCode);
        Assert.Equal(TimeSpan.FromMinutes(15), code.ExpiresIn);

        Assert.Equal(EmailLoginStepKind.Completed, ended.Kind);
        Assert.Null(ended.Failure);
    }

    [Fact]
    public async Task A_step_is_handed_over_while_the_verb_still_waits_and_the_pasted_address_goes_to_its_standard_input()
    {
        // The child speaks its address, then lives until it is answered: a sign-in is a conversation.
        var launcher = new ConversingProcessLauncher { Reply = line => line == Redirect ? [CompletedLine(2, "perso")] : [] };
        launcher.Opening.Add(AuthorizationLine);
        var steps = new List<EmailLoginStep>();
        var input = new EmailLoginInput();

        var login = CreateClient(launcher).LoginAsync("perso", Settings, steps.Add, input, TestContext.Current.CancellationToken);

        var shown = Assert.Single(steps);
        Assert.Equal(EmailLoginStepKind.AuthorizationUrl, shown.Kind);
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth?client_id=c&state=state-1", shown.Address);
        Assert.Null(shown.UserCode);
        Assert.False(login.IsCompleted);

        // As pasted from a browser: spaces around it, and never more than its one line.
        Assert.True(input.PasteRedirect("  " + Redirect + "\r\n"));

        var ended = await login;
        Assert.Equal([Redirect], launcher.InputLines);
        Assert.Equal(EmailLoginStepKind.Completed, ended.Kind);
        // The dialogue is over once the verb said so: its input is closed, which is how it is let go.
        Assert.Equal(1, launcher.ClosedInputs);
        Assert.False(input.PasteRedirect(Redirect));
    }

    [Fact]
    public async Task An_address_the_verb_rejects_is_a_step_with_its_sentence_and_the_sign_in_goes_on()
    {
        const string NoCode = "The pasted address carries no authorization code: it may be cut short.";
        var launcher = new ConversingProcessLauncher
        {
            Reply = line => line == Redirect ? [CompletedLine(3, "perso")] : [RejectedLine(2, NoCode)],
        };
        launcher.Opening.Add(AuthorizationLine);
        var steps = new List<EmailLoginStep>();
        var input = new EmailLoginInput();

        var login = CreateClient(launcher).LoginAsync("perso", Settings, steps.Add, input, TestContext.Current.CancellationToken);
        Assert.True(input.PasteRedirect("http://127.0.0.1:53124/?state=state-1"));

        // Said while the verb still waits: the rejection ends nothing, and its input stays open.
        Assert.Equal([EmailLoginStepKind.AuthorizationUrl, EmailLoginStepKind.RedirectRejected], steps.Select(step => step.Kind));
        Assert.Equal(NoCode, steps[1].Message);
        Assert.Null(steps[1].Address);
        Assert.False(login.IsCompleted);
        Assert.Equal(0, launcher.ClosedInputs);

        Assert.True(input.PasteRedirect(Redirect));
        var ended = await login;

        Assert.Equal(EmailLoginStepKind.Completed, ended.Kind);
        Assert.Equal(2, steps.Count);
    }

    [Fact]
    public void An_address_pasted_before_any_sign_in_reads_it_goes_nowhere()
    {
        var input = new EmailLoginInput();

        Assert.False(input.PasteRedirect(Redirect));
        Assert.False(input.PasteRedirect("   "));
    }

    [Fact]
    public async Task A_line_that_is_no_event_or_an_event_of_another_vocabulary_is_ignored()
    {
        var launcher = new FakeProcessLauncher().WithStandardOutput(
            "To sign e-mail account 'hotmail' in, open https://microsoft.com/devicelogin",
            """{"v":2,"seq":1,"kind":"usecases.ready","count":3}""",
            """{"kind":"email.login.device_code"}""",
            DeviceCodeLine,
            "{ not json",
            CompletedLine(3, "hotmail"));
        var steps = new List<EmailLoginStep>();

        var ended = await CreateClient(launcher).LoginAsync("hotmail", Settings, steps.Add, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([EmailLoginStepKind.DeviceCode], steps.Select(step => step.Kind));
        Assert.Equal(EmailLoginStepKind.Completed, ended.Kind);
    }

    [Fact]
    public async Task A_refusal_of_the_verb_ends_the_sign_in_with_its_code_and_its_message_as_printed()
    {
        const string Message = "The provider issued no refresh token, so the sign-in would expire within the hour.";
        var launcher = new FakeProcessLauncher { ExitCode = 1 }
            .WithStandardOutput(DeviceCodeLine, ErrorLine(2, "LoginRequired", Message));
        var steps = new List<EmailLoginStep>();

        var ended = await CreateClient(launcher).LoginAsync("hotmail", Settings, steps.Add, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(steps);
        Assert.Equal(EmailLoginStepKind.Failed, ended.Kind);
        Assert.NotNull(ended.Failure);
        Assert.Equal(EmailCliFailureKind.Refused, ended.Failure.Kind);
        Assert.Equal("LoginRequired", ended.Failure.Code);
        Assert.Equal(Message, ended.Failure.Reason);
        Assert.Equal(1, ended.Failure.ExitCode);
    }

    [Fact]
    public async Task A_verb_that_stopped_without_an_event_is_a_failure_that_carries_what_it_said()
    {
        var launcher = new FakeProcessLauncher { ExitCode = 2 }.WithStandardError(
            "Using settings: " + Settings,
            "orkeon email login: unexpected error [System.Net.Http.HttpRequestException]: No route to host");

        var ended = await CreateClient(launcher).LoginAsync("hotmail", Settings, _ => { }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(EmailLoginStepKind.Failed, ended.Kind);
        Assert.Equal(EmailCliFailureKind.Stopped, ended.Failure?.Kind);
        Assert.Equal("unexpected error [System.Net.Http.HttpRequestException]: No route to host", ended.Failure!.Reason);
        Assert.Equal(2, ended.Failure.ExitCode);
    }

    [Fact]
    public async Task A_verb_that_ended_well_without_saying_it_completed_is_not_taken_for_a_sign_in()
    {
        // A CLI that predates the event mode prints its sentences and exits 0: nothing proves the tokens are stored.
        var launcher = new FakeProcessLauncher().WithStandardOutput("Signed in: the tokens of e-mail account 'hotmail' are stored.");

        var ended = await CreateClient(launcher).LoginAsync("hotmail", Settings, _ => { }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(EmailLoginStepKind.Failed, ended.Kind);
        Assert.Equal(EmailCliFailureKind.Unreadable, ended.Failure?.Kind);
        Assert.Contains("email.login.completed", ended.Failure!.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_binary_the_sign_in_fails_as_engine_missing_and_spawns_nothing()
    {
        var launcher = new FakeProcessLauncher();

        var ended = await CreateClient(launcher, binaryPresent: false)
            .LoginAsync("hotmail", Settings, _ => { }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(EmailLoginStepKind.Failed, ended.Kind);
        Assert.Equal(EmailCliFailureKind.EngineMissing, ended.Failure?.Kind);
        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task Cancelling_a_sign_in_closes_the_verbs_input_at_once_stops_the_child_and_says_so_instead_of_throwing()
    {
        // The verb waits without a deadline: only its token, or the end of its input, ends it.
        var launcher = new FakeProcessLauncher { RunsUntilCancelled = true }.WithStandardOutput(AuthorizationLine);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var input = new EmailLoginInput();

        var login = CreateClient(launcher).LoginAsync("perso", Settings, _ => { }, input, cts.Token);
        Assert.False(login.IsCompleted);
        Assert.False(launcher.InputClosed);

        // Cancel, not CancelAsync: the input is closed before the call returns, on the caller's
        // thread — what a window that is closing can still rely on.
#pragma warning disable CA1849, S6966 // the synchronous call is the behaviour under test
        cts.Cancel();
#pragma warning restore CA1849, S6966
        Assert.True(launcher.InputClosed);

        var ended = await login;
        Assert.Equal(EmailLoginStepKind.Failed, ended.Kind);
        Assert.Equal(EmailCliFailureKind.Cancelled, ended.Failure?.Kind);
        Assert.False(input.PasteRedirect(Redirect));
        Assert.Equal(1, launcher.StartCount);
    }

    [Fact]
    public async Task A_step_never_prints_the_device_code_nor_the_address_of_itself()
    {
        var launcher = new FakeProcessLauncher().WithStandardOutput(DeviceCodeLine, CompletedLine(2, "hotmail"));
        var steps = new List<EmailLoginStep>();

        await CreateClient(launcher).LoginAsync("hotmail", Settings, steps.Add, cancellationToken: TestContext.Current.CancellationToken);

        // A record prints its members by default: this one must not, a device code is a secret while it lives.
        var printed = Assert.Single(steps).ToString();
        Assert.DoesNotContain("WXYZ-1234", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("devicelogin", printed, StringComparison.Ordinal);
        Assert.Contains("DeviceCode", printed, StringComparison.Ordinal);
    }

    // ── the sign-out (STUDIO-70) ──

    [Fact]
    public async Task Signing_out_names_the_account_and_the_settings_file_and_a_zero_exit_is_no_failure()
    {
        var launcher = new FakeProcessLauncher()
            .WithStandardError("Using settings: " + Settings)
            .WithStandardOutput("Signed out: the tokens of e-mail account 'hotmail' are deleted.");

        var failure = await CreateClient(launcher).LogoutAsync("hotmail", Settings, TestContext.Current.CancellationToken);

        var request = Assert.Single(launcher.Requests);
        Assert.Equal(["email", "logout", "hotmail", "--settings", Settings], request.Arguments);
        Assert.Null(failure);
    }

    [Fact]
    public async Task A_refused_sign_out_carries_what_the_verb_said_without_its_prefix()
    {
        var launcher = new FakeProcessLauncher { ExitCode = 1 }.WithStandardError(
            "Using settings: " + Settings,
            "orkeon email logout: E-mail account 'perso' signs in with a password (Auth:PasswordEnvVar); there are no tokens to forget.");

        var failure = await CreateClient(launcher).LogoutAsync("perso", Settings, TestContext.Current.CancellationToken);

        Assert.NotNull(failure);
        Assert.Equal(EmailCliFailureKind.Stopped, failure.Kind);
        Assert.Equal("E-mail account 'perso' signs in with a password (Auth:PasswordEnvVar); there are no tokens to forget.", failure.Reason);
        Assert.Equal(1, failure.ExitCode);
    }

    [Fact]
    public async Task Without_a_binary_the_sign_out_fails_as_engine_missing_and_spawns_nothing()
    {
        var launcher = new FakeProcessLauncher();

        var failure = await CreateClient(launcher, binaryPresent: false).LogoutAsync("hotmail", Settings, TestContext.Current.CancellationToken);

        Assert.Equal(EmailCliFailureKind.EngineMissing, failure?.Kind);
        Assert.Equal(0, launcher.StartCount);
    }
}
