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
        Assert.Equal(["email", "check", "perso", "--settings", Settings], request.Arguments);
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
}
