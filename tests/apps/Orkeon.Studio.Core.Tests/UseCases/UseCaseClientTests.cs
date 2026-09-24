using System.Text.Json.Nodes;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Tests.Doubles;
using Orkeon.Studio.Core.UseCases;

namespace Orkeon.Studio.Core.Tests.UseCases;

/// <summary>
/// The use-case client over scripted children (STUDIO-39): the argv it composes, the catalogue it
/// reads off <c>usecases list</c>, and the session it keeps open for the suggestions — queries down
/// stdin, answers matched by correlation id, one process for many queries. No real binary.
/// </summary>
public sealed class UseCaseClientTests
{
    private static readonly string InstallDirectory = Path.Combine("/", "opt", "orkeon");
    private static readonly string BinaryPath = Path.Combine(InstallDirectory, "orkeon");

    private static OrkeonBinaryLocator Locator(bool installed = true)
    {
        var executables = new FakeExecutableProbe { BaseDirectory = InstallDirectory };
        if (installed)
            executables.WithFile(BinaryPath);
        return new OrkeonBinaryLocator(executables);
    }

    /// <summary>A session child that opens, then answers every query with <paramref name="answer"/>.</summary>
    private static ConversingProcessLauncher Session(Func<string, string, IEnumerable<string>> answer)
    {
        var processes = new ConversingProcessLauncher
        {
            Reply = line => answer(UseCaseLines.CorrelationIdOf(line), UseCaseLines.TextOf(line)),
        };
        processes.Opening.Add(UseCaseLines.Ready(105));
        return processes;
    }

    [Fact]
    public void The_argv_follows_the_cli_grammar()
    {
        Assert.Equal(["usecases", "list", "--events", "jsonl"], UseCaseArgumentsBuilder.BuildList());
        Assert.Equal(["usecases", "search", "--events", "jsonl"], UseCaseArgumentsBuilder.BuildSession());
    }

    [Fact]
    public async Task The_catalogue_is_read_off_the_list_line_with_every_field_the_gallery_filters_on()
    {
        var processes = new FakeProcessLauncher().WithStandardOutput(UseCaseLines.Catalog(
            UseCaseLines.Sheet("03-email-pipeline", "01-enterprise", titleFr: "Tri et réponse aux e-mails",
                problemFr: "Trier mes e-mails", problemZh: "整理我的邮件", tags: ["email"]),
            UseCaseLines.Sheet("31-algo-trading", "03-finance-trading", process: "hierarchical",
                requiresNetwork: true, requiresKeys: ["ORKEON_TAVILY_API_KEY"], importable: false)));
        using var client = new UseCaseClient(processes, Locator());

        var result = await client.ListAsync(TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        var catalog = Assert.IsType<UseCaseCatalog>(result.Catalog);
        Assert.Equal(2, catalog.Count);
        var email = catalog.UseCases[0];
        Assert.Equal("03-email-pipeline", email.Id);
        Assert.Equal("01-enterprise", email.Category);
        Assert.Equal("Tri et réponse aux e-mails", email.TitleIn("fr"));
        Assert.Equal("整理我的邮件", email.ProblemIn("zh"));
        Assert.Equal(["email"], email.Tags);
        Assert.True(email.Importable);
        var trading = catalog.Find("31-algo-trading")!;
        Assert.Equal("hierarchical", trading.Process);
        Assert.True(trading.RequiresNetwork);
        Assert.Equal(["ORKEON_TAVILY_API_KEY"], trading.RequiresKeys);
        Assert.False(trading.Importable);

        var request = Assert.Single(processes.Requests);
        Assert.Equal(BinaryPath, request.FileName);
        Assert.Equal(["usecases", "list", "--events", "jsonl"], request.Arguments);
    }

    [Fact]
    public async Task A_missing_binary_is_the_engine_missing_failure_and_nothing_is_spawned()
    {
        var processes = new FakeProcessLauncher();
        using var client = new UseCaseClient(processes, Locator(installed: false));

        var list = await client.ListAsync(TestContext.Current.CancellationToken);
        var search = await client.SearchAsync("mes factures", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UseCaseFailureKind.EngineMissing, list.Failure!.Kind);
        Assert.Contains("orkeon", list.Failure.Detail, StringComparison.Ordinal);
        Assert.Equal(UseCaseFailureKind.EngineMissing, search.Failure!.Kind);
        Assert.Empty(processes.Requests);
    }

    [Fact]
    public async Task An_error_line_is_a_refusal_in_the_clis_own_words()
    {
        var processes = new FakeProcessLauncher { ExitCode = 1 }
            .WithStandardOutput(UseCaseLines.Error("USECASES-OPTION-INVALID", "--lang must be one of fr, en", recoverable: false));
        using var client = new UseCaseClient(processes, Locator());

        var result = await client.ListAsync(TestContext.Current.CancellationToken);

        Assert.Null(result.Catalog);
        Assert.Equal(UseCaseFailureKind.Refused, result.Failure!.Kind);
        Assert.Equal("USECASES-OPTION-INVALID", result.Failure.Code);
        Assert.Equal("USECASES-OPTION-INVALID: --lang must be one of fr, en", result.Failure.Detail);
    }

    [Fact]
    public async Task A_run_that_answers_no_catalogue_says_so_with_its_stderr_and_exit_code()
    {
        var processes = new FakeProcessLauncher { ExitCode = 1 }
            .WithStandardError("Verb 'usecases' is not recognized.");
        using var client = new UseCaseClient(processes, Locator());

        var result = await client.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UseCaseFailureKind.Stopped, result.Failure!.Kind);
        Assert.Equal("Verb 'usecases' is not recognized.", result.Failure.Detail);
        Assert.Equal(1, result.Failure.ExitCode);
    }

    [Fact]
    public async Task Queries_go_down_one_session_and_each_answer_comes_back_by_its_correlation_id()
    {
        var processes = Session((correlationId, text) =>
        [
            UseCaseLines.Results(correlationId, text,
                UseCaseLines.Result(1, text.Contains("factures", StringComparison.Ordinal) ? "40-invoice-processing" : "03-email-pipeline", "terms", "factures", "mes")),
        ]);
        using var client = new UseCaseClient(processes, Locator());

        var first = await client.SearchAsync("mes factures", cancellationToken: TestContext.Current.CancellationToken);
        var second = await client.SearchAsync("mes mails", top: 3, language: "fr", cancellationToken: TestContext.Current.CancellationToken);

        // One child for both: the model loads once per session, never once per keystroke.
        var request = Assert.Single(processes.Requests);
        Assert.Equal(["usecases", "search", "--events", "jsonl"], request.Arguments);
        Assert.True(client.IsSessionOpen);

        Assert.Equal(2, processes.InputLines.Count);
        var query = JsonNode.Parse(processes.InputLines[0])!;
        Assert.Equal("usecases.query", query["kind"]!.GetValue<string>());
        Assert.Equal("mes factures", query["text"]!.GetValue<string>());
        Assert.Equal(UseCaseClient.DefaultTop, query["top"]!.GetValue<int>());
        Assert.Null(query["lang"]);   // the CLI reads the language from the text
        var named = JsonNode.Parse(processes.InputLines[1])!;
        Assert.Equal("fr", named["lang"]!.GetValue<string>());
        Assert.Equal(3, named["top"]!.GetValue<int>());
        Assert.NotEqual(query["correlationId"]!.GetValue<string>(), named["correlationId"]!.GetValue<string>());

        var match = Assert.Single(first.Answer!.Matches);
        Assert.Equal("40-invoice-processing", match.Id);
        Assert.Equal(UseCaseMatchReason.Terms, match.Reason);
        Assert.Equal(["factures", "mes"], match.Terms);
        Assert.Equal("mes factures", first.Answer.Query);
        Assert.Equal("03-email-pipeline", Assert.Single(second.Answer!.Matches).Id);
    }

    [Fact]
    public async Task A_query_answered_by_an_error_line_is_a_refusal_and_the_session_goes_on()
    {
        var calls = 0;
        var processes = Session((correlationId, text) => ++calls == 1
            ? [UseCaseLines.Error("USECASES-QUERY-INVALID", "a query needs a non-empty \"text\".", correlationId)]
            : [UseCaseLines.Results(correlationId, text, UseCaseLines.Result(1, "06-competitive-intelligence", "terms", "veille"))]);
        using var client = new UseCaseClient(processes, Locator());

        var refused = await client.SearchAsync("veille", cancellationToken: TestContext.Current.CancellationToken);
        var answered = await client.SearchAsync("veille", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UseCaseFailureKind.Refused, refused.Failure!.Kind);
        Assert.Equal("USECASES-QUERY-INVALID", refused.Failure.Code);
        Assert.Equal("06-competitive-intelligence", Assert.Single(answered.Answer!.Matches).Id);
        Assert.Single(processes.Requests);
    }

    [Fact]
    public async Task Closing_the_session_closes_its_stdin_and_the_next_query_opens_a_new_one()
    {
        var processes = Session((correlationId, text) => [UseCaseLines.Results(correlationId, text)]);
        using var client = new UseCaseClient(processes, Locator());
        await client.SearchAsync("veille", cancellationToken: TestContext.Current.CancellationToken);

        client.CloseSession();

        Assert.Equal(1, processes.ClosedInputs);
        Assert.False(client.IsSessionOpen);

        var again = await client.SearchAsync("veille", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(again.Answer);
        Assert.Equal(2, processes.Requests.Count);
    }

    [Fact]
    public async Task A_session_that_crashes_fails_its_query_and_the_next_one_opens_a_fresh_session()
    {
        var processes = Session((correlationId, text) => [UseCaseLines.Results(correlationId, text)]);
        using var client = new UseCaseClient(processes, Locator());
        await client.SearchAsync("veille", cancellationToken: TestContext.Current.CancellationToken);
        var answered = processes.Reply;
        processes.Reply = _ => [];   // the next query is never answered…
        var lost = client.SearchAsync("factures", cancellationToken: TestContext.Current.CancellationToken);

        processes.Crash(exitCode: 2);   // …because the child dies first

        var result = await lost;
        Assert.Equal(UseCaseFailureKind.Stopped, result.Failure!.Kind);
        Assert.Equal(2, result.Failure.ExitCode);

        processes.Reply = answered;
        var fresh = await client.SearchAsync("veille", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(fresh.Answer);
        Assert.Equal(2, processes.Requests.Count);
    }

    [Fact]
    public async Task A_session_that_never_opens_is_not_reopened_by_every_query()
    {
        // An engine older than STUDIO-38: no session mode, a refusal on stderr, exit 1.
        var processes = new ConversingProcessLauncher { StaysOpen = false, ExitCode = 1 };
        processes.OpeningErrors.Add("Verb 'usecases' is not recognized.");
        using var client = new UseCaseClient(processes, Locator());

        var first = await client.SearchAsync("veille", cancellationToken: TestContext.Current.CancellationToken);
        var second = await client.SearchAsync("veille concurrentielle", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UseCaseFailureKind.Stopped, first.Failure!.Kind);
        Assert.Equal("Verb 'usecases' is not recognized.", first.Failure.Detail);
        Assert.Equal(first.Failure, second.Failure);
        Assert.Single(processes.Requests);
    }

    [Fact]
    public async Task A_catalogue_that_loads_lets_a_session_that_could_not_open_be_tried_again()
    {
        var executables = new FakeExecutableProbe { BaseDirectory = InstallDirectory };
        // One scripted child for both verbs: its opening carries the ready line a session
        // speaks and the catalogue line a list prints — each reader takes its own.
        var processes = Session((correlationId, text) => [UseCaseLines.Results(correlationId, text)]);
        processes.Opening.Add(UseCaseLines.Catalog());
        using var client = new UseCaseClient(processes, new OrkeonBinaryLocator(executables));

        var before = await client.SearchAsync("veille", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(UseCaseFailureKind.EngineMissing, before.Failure!.Kind);

        // The CLI got installed meanwhile; the gallery's reload proves it.
        executables.WithFile(BinaryPath);
        processes.StaysOpen = false;
        var reloaded = await client.ListAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded.Catalog);

        processes.StaysOpen = true;
        var after = await client.SearchAsync("veille", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(after.Answer);
    }

    [Fact]
    public async Task Cancelling_abandons_the_wait_and_leaves_the_session_open()
    {
        var processes = Session((_, _) => []);   // an answer that never comes
        using var client = new UseCaseClient(processes, Locator());
        using var cancellation = new CancellationTokenSource();

        var waiting = client.SearchAsync("veille", cancellationToken: cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.True(client.IsSessionOpen);
    }
}
