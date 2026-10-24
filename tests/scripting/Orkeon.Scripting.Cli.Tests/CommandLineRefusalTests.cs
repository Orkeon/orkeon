using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// CH-04 / CH-07: what every CommandLineParser command does with a word it does not expect, a
/// usage error and a help asked for. Each command is driven through the entry the shipped binary
/// uses; nothing is built or written before the refusal.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class CommandLineRefusalTests
{
    private const string Extra = "surplus";

    private const string Crew =
        """
        name: smoke-crew
        goal: Say hello deterministically
        process: sequential

        agents:
          greeter:
            role: "Greeter"
            goal: "Greet"
            backstory: "A minimal agent for smoke testing."

        tasks:
          greet:
            description: "Say hello."
            expected_output: "A greeting."
            agent: greeter
        """;

    // F3 (forge, typings, settings) is batch F3's; usecases search takes a sequence of words, so an
    // extra word is part of the text and has no refusal row.
    private static readonly string[] OutOfScopeVerbs = ["forge", "typings", "settings"];

    private static readonly string[] RowNames =
    [
        "run", "quickstart", "init", "doctor", "llm probe", "llm models", "rag ingest", "rag search", "rag eval",
        "email accounts", "email login", "email logout", "email check",
        "drive accounts", "drive login", "drive logout", "drive check",
        "usecases list", "usecases show", "usecases export", "mcp serve",
    ];

    public static TheoryData<string> Rows { get; } = new(RowNames);

    [Fact]
    [Trait("BR", "CH-04")]
    public async Task ShouldRefuseTheExtraWord_WhenRunIsGivenOne()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        var settings = scratch.WriteFile("s.json", "{}");
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(console, ["run", crew, "--validate", "-s", settings, "--allow-external-mounts", "extra"]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("unexpected argument 'extra'", console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("VALIDATION OK", console.Stdout, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    [Trait("BR", "CH-04")]
    public async Task ShouldRefuseTheExtraWordBeforeAnyWork_WhenACommandIsGivenOne(string row)
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteFile("s.json", "{}");
        var crew = scratch.WriteScript("crew.yaml", Crew);
        var target = Path.Combine(scratch.Root, "created");
        using var catalogue = new CatalogueServer();

        string[] argv = row switch
        {
            "run" => ["run", crew, "--validate", "-s", settings, "--allow-external-mounts", Extra],
            "quickstart" => ["quickstart", "--to", target, Extra],
            "init" => ["init", "--provider", "none", "--path", target, Extra],
            "doctor" => ["doctor", "--json", Extra],
            "llm probe" => ["llm", "probe", "-p", "ollama", Extra],
            "llm models" => ["llm", "models", "-p", "openai", "-u", catalogue.BaseUrl, Extra],
            "rag ingest" => ["rag", "ingest", "-c", "docs", "--source", "a.md", "-s", settings, Extra],
            "rag search" => ["rag", "search", "question", "-c", "docs", "-s", settings, Extra],
            "rag eval" => ["rag", "eval", "-d", "g.yaml", "-s", settings, Extra],
            "email accounts" => ["email", "accounts", "-s", settings, Extra],
            "email login" => ["email", "login", "perso", "-s", settings, Extra],
            "email logout" => ["email", "logout", "perso", "-s", settings, Extra],
            "email check" => ["email", "check", "perso", "-s", settings, Extra],
            "drive accounts" => ["drive", "accounts", "-s", settings, Extra],
            "drive login" => ["drive", "login", "pro", "-s", settings, Extra],
            "drive logout" => ["drive", "logout", "pro", "-s", settings, Extra],
            "drive check" => ["drive", "check", "pro", "-s", settings, Extra],
            "usecases list" => ["usecases", "list", Extra],
            "usecases show" => ["usecases", "show", "03-email-pipeline", Extra],
            "usecases export" => ["usecases", "export", "01-research-assistant", "--to", target, Extra],
            "mcp serve" => ["mcp", "serve", "-s", settings, Extra],
            _ => throw new InvalidOperationException(row),
        };
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await Program.DispatchAsync(console, argv);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains($"unexpected argument '{Extra}'", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(console.Stdout);
        Assert.Empty(catalogue.Authorizations);
        Assert.False(Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any(), "the refusal wrote files");
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public void ShouldHaveARowForEveryVerb_WhenTheVerbsAreListed()
    {
        var covered = RowNames.Select(r => r.Split(' ')[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = Program.KnownVerbs
            .Where(v => !OutOfScopeVerbs.Contains(v, StringComparer.OrdinalIgnoreCase))
            .Where(v => !covered.Contains(v))
            .ToList();

        Assert.True(missing.Count == 0, "verbs without a refusal row: " + string.Join(", ", missing));
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public async Task ShouldRefuseTheExtraWord_WhenUseCasesExportIsGivenOne()
    {
        using var scratch = new ScriptScratch();
        var to = Path.Combine(scratch.Root, "mon");
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(console, ["usecases", "export", "01-research-assistant", "--to", to, "équipe"]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("équipe", console.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(to));
        Assert.False(Directory.Exists(Path.Combine(scratch.Root, "mon équipe")));
    }

    [Theory]
    [InlineData("usecases", "- veille des prix")]
    [Trait("BR", "CH-04")]
    public async Task ShouldSearchTheText_WhenItFollowsADoubleDash(string verb, string text)
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteFile("s.json", "{}");
        string[] argv = verb == "rag"
            ? ["rag", "search", "-c", "docs", "-s", settings, "--", text]
            : ["usecases", "search", "--", text];
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(console, argv);

        Assert.DoesNotContain("is unknown", console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("unexpected argument", console.Stderr, StringComparison.Ordinal);
        if (verb == "usecases")
        {
            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains("veille", console.Stdout, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("doctor --json --x", "x", "--json")]
    [InlineData("run crew.yaml --events jsonl --x", "x", "--events")]
    [InlineData("rag", "verb", "ingest")]
    [InlineData("rag eval", "dataset", "--dataset")]
    [Trait("BR", "CH-07")]
    public async Task ShouldWriteNothingOnStdout_WhenAnOptionIsUnknown(string line, string error, string usage)
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(console, line.Split(' '));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Empty(console.Stdout);
        Assert.Contains(error, console.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(usage, console.Stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rag --help", "ingest")]
    [InlineData("rag help search", "--collection")]
    [InlineData("--version", ".")]
    [Trait("BR", "CH-07")]
    public async Task ShouldExitWithZeroOnStdout_WhenHelpIsAsked(string line, string expected)
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(console, line.Split(' '));

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains(expected, console.Stdout, StringComparison.Ordinal);
        Assert.Empty(console.Stderr);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("rag")]
    [Trait("BR", "CH-04")]
    public async Task ShouldRefuseTheLine_WhenAnOptionBeforeADoubleDashHasNoValue(string verb)
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        string[] argv = verb == "run"
            ? ["run", crew, "--settings", "--", "extra"]
            : ["rag", "search", "-c", "docs", "-s", "--", "what?"];
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await Program.DispatchAsync(console, argv);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Empty(console.Stdout);
        Assert.NotEmpty(console.Stderr);
    }
}
