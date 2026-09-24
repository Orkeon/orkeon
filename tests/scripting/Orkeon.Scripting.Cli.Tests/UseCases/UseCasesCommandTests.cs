using System.Text.Json;
using Orkeon.Constants.Protocol;
using Orkeon.Scripting.Cli.Commands.UseCases;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.UseCases;

/// <summary>
/// <c>orkeon usecases search | list | show</c> driven in-process (STUDIO-38 D-06, D-07). The
/// grammar and the embedded catalogue run for real; the search tests swap the catalogue for the
/// five-sheet fixture and the model for a hand-written double through the options' seams.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class UseCasesCommandTests
{
    // --- list ---

    [Fact]
    public async Task List_filters_by_category()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["usecases", "list", "--category", "03-finance-trading", "--events", "jsonl"]);

        Assert.Equal(Program.ExitOk, exit);
        var catalog = SingleEvent(console.Stdout, UseCaseEventKinds.Catalog);
        var categories = catalog.GetProperty("useCases").EnumerateArray()
            .Select(u => u.GetProperty("category").GetString())
            .ToList();
        Assert.Equal(15, catalog.GetProperty("count").GetInt32());
        Assert.Equal(15, categories.Count);
        Assert.All(categories, category => Assert.Equal("03-finance-trading", category));
    }

    [Fact]
    public async Task List_takes_a_category_by_its_name_alone()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["usecases", "list", "--category", "Finance-Trading"]);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("31-algo-trading", console.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("01-research-assistant", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_refuses_an_unknown_category_and_names_the_known_ones()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["usecases", "list", "--category", "gardening"]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("gardening", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("01-enterprise", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_filters_by_process_and_tag_together()
    {
        using var console = new TestConsole();

        var exit = await UseCasesCommand.ExecuteListAsync(new UseCasesListOptions
        {
            Process = "parallel",
            Tag = "security",
            Events = "jsonl",
            Catalog = UseCaseFixtures.Catalog(),
        });

        Assert.Equal(Program.ExitOk, exit);
        var ids = SingleEvent(console.Stdout, UseCaseEventKinds.Catalog).GetProperty("useCases").EnumerateArray()
            .Select(u => u.GetProperty("id").GetString());
        Assert.Equal([UseCaseFixtures.FraudAlerts], ids);
    }

    [Fact]
    public async Task List_shows_each_title_in_the_asked_language()
    {
        using var console = new TestConsole();

        var exit = await UseCasesCommand.ExecuteListAsync(new UseCasesListOptions
        {
            Language = "de",
            Catalog = UseCaseFixtures.Catalog(),
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("Tägliche E-Mail-Zusammenfassung", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("5 use cases", console.Stdout, StringComparison.Ordinal);
    }

    // --- show ---

    [Fact]
    public async Task Show_returns_the_sheet()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["usecases", "show", "01-research-assistant"]);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("01-research-assistant", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("sequential", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("csv_reader", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("config.yaml", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("data/ev-sales-by-region.csv", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("./data:/data:ro", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_in_events_mode_carries_the_sheet_and_its_files()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["usecases", "show", "01-research-assistant", "--events", "jsonl"]);

        Assert.Equal(Program.ExitOk, exit);
        var sheet = SingleEvent(console.Stdout, UseCaseEventKinds.Sheet);
        Assert.Equal("01-research-assistant", sheet.GetProperty("id").GetString());
        Assert.Equal("sequential", sheet.GetProperty("process").GetString());
        Assert.True(sheet.GetProperty("importable").GetBoolean());
        Assert.Contains(sheet.GetProperty("files").EnumerateArray(), f => f.GetProperty("path").GetString() == "config.yaml");
        Assert.False(sheet.TryGetProperty("crew", out _));
    }

    [Fact]
    public async Task Show_with_crew_prints_the_embedded_crew_file()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["usecases", "show", "31-algo-trading", "--crew"]);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("main.ork.ts", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("orkeon-example:", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("reference only", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_of_an_unknown_id_is_a_typed_error()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["usecases", "show", "99-no-such-example"]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains(UseCaseErrorCodes.UnknownId, console.Stderr, StringComparison.Ordinal);
        Assert.Contains("99-no-such-example", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_of_an_unknown_id_in_events_mode_is_a_typed_error_event()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["usecases", "show", "99-no-such-example", "--events", "jsonl"]);

        Assert.Equal(Program.ExitScriptError, exit);
        var error = SingleEvent(console.Stdout, UseCaseEventKinds.Error);
        Assert.Equal(UseCaseErrorCodes.UnknownId, error.GetProperty("code").GetString());
        Assert.False(error.GetProperty("recoverable").GetBoolean());
    }

    // --- search, one shot ---

    [Fact]
    public async Task Search_prints_the_ranked_matches_and_the_mode()
    {
        using var console = new TestConsole();

        var exit = await UseCasesCommand.ExecuteSearchAsync(Search(["facture", "fournisseur"], language: "fr"));

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("1. 02-invoice-matching", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("Rapprochement des factures fournisseurs", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("terms: facture, fournisseur", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("BM25", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_in_events_mode_gives_id_score_reason_and_mode()
    {
        using var console = new TestConsole();

        var exit = await UseCasesCommand.ExecuteSearchAsync(Search(["email", "summary"], language: "en", events: true));

        Assert.Equal(Program.ExitOk, exit);
        var results = SingleEvent(console.Stdout, UseCaseEventKinds.Results);
        Assert.Equal("hybrid", results.GetProperty("mode").GetString());
        Assert.Equal("en", results.GetProperty("lang").GetString());
        var best = results.GetProperty("results")[0];
        Assert.Equal(UseCaseFixtures.MailDigest, best.GetProperty("id").GetString());
        Assert.Equal("terms+meaning", best.GetProperty("reason").GetString());
        Assert.True(best.GetProperty("score").GetDouble() > 0);
        Assert.True(best.GetProperty("similarity").GetDouble() > 0.9);
        Assert.Equal(1, best.GetProperty("rank").GetInt32());
    }

    [Fact]
    public async Task Search_reports_it_fell_back_to_terms_when_the_model_is_missing()
    {
        using var console = new TestConsole();
        var options = Search(["email", "summary"], language: "en");
        options.LoadModel = () => throw new UseCaseModelUnavailableException("model files not found");

        var exit = await UseCasesCommand.ExecuteSearchAsync(options);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("terms only", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("model files not found", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("1. 01-daily-mail-digest", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_in_events_mode_reports_the_fall_back_on_the_answer()
    {
        using var console = new TestConsole();
        var options = Search(["email"], language: "en", events: true);
        options.LoadModel = () => throw new UseCaseModelUnavailableException("model files not found");

        var exit = await UseCasesCommand.ExecuteSearchAsync(options);

        Assert.Equal(Program.ExitOk, exit);
        var results = SingleEvent(console.Stdout, UseCaseEventKinds.Results);
        Assert.Equal("bm25", results.GetProperty("mode").GetString());
        Assert.Contains("model files not found", results.GetProperty("degraded").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_says_so_when_nothing_matches()
    {
        using var console = new TestConsole();

        var exit = await UseCasesCommand.ExecuteSearchAsync(Search(["zzzz"], language: "fr"));

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("No use case matches", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_without_text_needs_the_events_mode()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["usecases", "search"]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--events jsonl", console.Stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--lang", "it")]
    [InlineData("--top", "0")]
    [InlineData("--events", "xml")]
    public async Task Search_refuses_an_option_value_it_cannot_honour(string option, string value)
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["usecases", "search", "invoice", option, value]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains(option, console.Stderr, StringComparison.Ordinal);
    }

    /// <summary>The acceptance line of the fiche, against the real catalogue: it runs offline and answers.</summary>
    [Fact]
    public async Task Search_runs_offline_on_the_embedded_catalogue()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(
            ["usecases", "search", "je veux un résumé de mes mails chaque matin", "--events", "jsonl"]);

        Assert.Equal(Program.ExitOk, exit);
        var results = SingleEvent(console.Stdout, UseCaseEventKinds.Results);
        Assert.Equal("fr", results.GetProperty("lang").GetString());
        Assert.Equal("detected", results.GetProperty("langSource").GetString());
    }

    // --- search, session mode ---

    [Fact]
    public async Task Session_mode_answers_two_queries_without_reloading_the_model()
    {
        var loads = 0;
        using var model = new FakeKeywordEmbeddingProvider();
        using var console = new TestConsole(stdin: Lines(
            """{"kind":"usecases.query","correlationId":"q1","text":"email digest","lang":"en"}""",
            """{"kind":"usecases.query","correlationId":"q2","text":"supplier invoice","lang":"en","top":1}"""));
        var options = Search([], language: null, events: true);
        options.LoadModel = () =>
        {
            loads++;
            return model;
        };

        var exit = await UseCasesCommand.ExecuteSearchAsync(options);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal(1, loads);
        var events = Events(console.Stdout);
        Assert.Equal(UseCaseEventKinds.Ready, events[0].GetProperty("kind").GetString());
        var answers = events.Where(e => e.GetProperty("kind").GetString() == UseCaseEventKinds.Results).ToList();
        Assert.Equal(["q1", "q2"], answers.Select(a => a.GetProperty("correlationId").GetString()));
        Assert.Equal(UseCaseFixtures.MailDigest, answers[0].GetProperty("results")[0].GetProperty("id").GetString());
        Assert.Equal(UseCaseFixtures.InvoiceMatching, answers[1].GetProperty("results")[0].GetProperty("id").GetString());
        Assert.Equal(1, answers[1].GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task Session_mode_announces_the_catalogue_and_the_mode_of_each_language()
    {
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await UseCasesCommand.ExecuteSearchAsync(Search([], language: null, events: true));

        Assert.Equal(Program.ExitOk, exit);
        var ready = SingleEvent(console.Stdout, UseCaseEventKinds.Ready);
        Assert.Equal(5, ready.GetProperty("count").GetInt32());
        Assert.Equal("hybrid", ready.GetProperty("modes").GetProperty("en").GetString());
        Assert.Equal(["fr", "en", "es", "de", "zh-Hans"], ready.GetProperty("languages").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public async Task Session_mode_answers_a_bad_query_with_an_error_and_keeps_going()
    {
        using var console = new TestConsole(stdin: Lines(
            """{"kind":"usecases.query","correlationId":"q1"}""",
            """{"kind":"usecases.query","correlationId":"q2","text":"invoice","lang":"klingon"}""",
            """{"kind":"usecases.query","correlationId":"q3","text":"invoice","lang":"en"}"""));

        var exit = await UseCasesCommand.ExecuteSearchAsync(Search([], language: null, events: true));

        Assert.Equal(Program.ExitOk, exit);
        var events = Events(console.Stdout);
        var errors = events.Where(e => e.GetProperty("kind").GetString() == UseCaseEventKinds.Error).ToList();
        Assert.Equal(["q1", "q2"], errors.Select(e => e.GetProperty("correlationId").GetString()));
        Assert.All(errors, e => Assert.Equal(UseCaseErrorCodes.QueryInvalid, e.GetProperty("code").GetString()));
        Assert.All(errors, e => Assert.True(e.GetProperty("recoverable").GetBoolean()));
        var answer = Assert.Single(events, e => e.GetProperty("kind").GetString() == UseCaseEventKinds.Results);
        Assert.Equal("q3", answer.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Session_mode_skips_lines_that_are_not_queries()
    {
        using var console = new TestConsole(stdin: Lines(
            "not json at all",
            """{"kind":"input.given","value":"yes"}""",
            "[1, 2, 3]",
            string.Empty,
            """{"kind":"usecases.query","correlationId":"q1","text":"invoice","lang":"en"}"""));

        var exit = await UseCasesCommand.ExecuteSearchAsync(Search([], language: null, events: true));

        Assert.Equal(Program.ExitOk, exit);
        var kinds = Events(console.Stdout).Select(e => e.GetProperty("kind").GetString());
        Assert.Equal([UseCaseEventKinds.Ready, UseCaseEventKinds.Results], kinds);
    }

    [Fact]
    public async Task Session_mode_ends_cleanly_when_stdin_closes()
    {
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await UseCasesCommand.ExecuteSearchAsync(Search([], language: null, events: true));

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal(string.Empty, console.Stderr);
    }

    [Fact]
    public async Task Every_kind_the_verb_emits_is_declared_in_the_shared_vocabulary()
    {
        using var console = new TestConsole(stdin: Lines(
            """{"kind":"usecases.query","correlationId":"q1","text":"invoice","lang":"en"}""",
            """{"kind":"usecases.query","correlationId":"q2"}"""));

        await UseCasesCommand.ExecuteSearchAsync(Search([], language: null, events: true));

        Assert.All(Events(console.Stdout), e => Assert.Contains(e.GetProperty("kind").GetString(), UseCaseEventKinds.All));
    }

    // --- helpers ---

    private static UseCasesSearchOptions Search(string[] words, string? language, bool events = false) =>
        new()
        {
            Words = words,
            Language = language,
            Events = events ? "jsonl" : null,
            Catalog = UseCaseFixtures.Catalog(),
            Policy = new UseCaseSearchPolicy(new Dictionary<string, UseCaseSearchMode>(StringComparer.Ordinal)
            {
                ["en"] = UseCaseSearchMode.Hybrid,
            }),
            LoadModel = () => new FakeKeywordEmbeddingProvider(),
        };

    private static string Lines(params string[] lines) => string.Join('\n', lines) + "\n";

    private static List<JsonElement> Events(string stdout) =>
        [.. stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonElement.Parse(line))];

    private static JsonElement SingleEvent(string stdout, string kind) =>
        Assert.Single(Events(stdout), e => e.GetProperty("kind").GetString() == kind);
}
