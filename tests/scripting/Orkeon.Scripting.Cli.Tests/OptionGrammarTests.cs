using System.Globalization;
using CommandLine;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Commands.UseCases;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// What the shipped <c>orkeon</c> binary's parser really accepts for its sequence options,
/// pinned as behaviour rather than as wording.
/// <para>
/// These options are declared TWICE — once on <c>RunnerOptionsBase</c> (Orkeon.Hosting) and
/// once on the classes below — and only the classes below are ever handed to a parser
/// (<c>Program.ParseArguments&lt;RunCommandOptions&gt;</c>, <c>RagCommand.DispatchAsync</c>).
/// A fix applied to the shared base therefore changes text nobody reads: that is exactly how
/// "Repeatable." survived in `orkeon run --help` after being corrected in the docs, in
/// Studio's argument builder, in `forge promote` and in the base class itself. Asserting the
/// parser's answer is the only guard that cannot drift away from the user's experience.
/// </para>
/// </summary>
public sealed class OptionGrammarTests
{
    private static ParserResult<T> Parse<T>(params string[] args)
    {
        using var parser = new Parser(s =>
        {
            s.HelpWriter = null;
            s.CaseInsensitiveEnumValues = true;
        });
        return parser.ParseArguments<T>(args);
    }

    private static IReadOnlyList<string> ErrorTags<T>(ParserResult<T> result) =>
        result is NotParsed<T> notParsed
            ? [.. notParsed.Errors.Select(e => e.Tag.ToString())]
            : [];

    [Fact]
    public void Several_mounts_go_space_separated_after_one_flag()
    {
        var result = Parse<RunCommandOptions>("crew.yaml", "--mount", "a:/x:ro", "b:/y:rw");

        var parsed = Assert.IsType<Parsed<RunCommandOptions>>(result);
        Assert.Equal(["a:/x:ro", "b:/y:rw"], parsed.Value.Mounts);
    }

    /// <summary>
    /// The flag cannot be repeated. Every generator of an `orkeon run` command line depends on
    /// this — `forge promote`'s launchers, Studio's argument builder — and a help text saying
    /// "Repeatable." sends the user straight into it.
    /// </summary>
    [Fact]
    public void A_repeated_mount_flag_is_a_usage_error()
    {
        var result = Parse<RunCommandOptions>("crew.yaml", "--mount", "a:/x:ro", "--mount", "b:/y:rw");

        Assert.IsType<NotParsed<RunCommandOptions>>(result);
        Assert.Contains("RepeatedOptionError", ErrorTags(result));
    }

    /// <summary>VFS-90: <c>--mount-id</c> follows the same grammar as <c>--mount</c>, and is spelled by the shared constant.</summary>
    [Fact]
    public void The_same_rule_governs_mount_id()
    {
        var parsed = Assert.IsType<Parsed<RunCommandOptions>>(
            Parse<RunCommandOptions>("crew.yaml", "--mount-id", "01J9Z3K4M5N6P7Q8R9S0T1V2W3", "01J9Z3K4M5N6P7Q8R9S0T1V2W4"));
        Assert.Equal(["01J9Z3K4M5N6P7Q8R9S0T1V2W3", "01J9Z3K4M5N6P7Q8R9S0T1V2W4"], parsed.Value.MountIds);

        Assert.Contains(
            "RepeatedOptionError",
            ErrorTags(Parse<RunCommandOptions>("crew.yaml", "--mount-id", "01J9Z3K4M5N6P7Q8R9S0T1V2W3", "--mount-id", "01J9Z3K4M5N6P7Q8R9S0T1V2W4")));
        Assert.Equal("mount-id", Orkeon.Constants.Cli.RunOptionNames.MountId);
    }

    [Fact]
    public void Mount_id_is_carried_to_the_shared_runner_options()
    {
        var parsed = Assert.IsType<Parsed<RunCommandOptions>>(
            Parse<RunCommandOptions>("crew.yaml", "--mount-id", "01J9Z3K4M5N6P7Q8R9S0T1V2W3"));

        Assert.Equal(["01J9Z3K4M5N6P7Q8R9S0T1V2W3"], RunCommand.ToRunnerOptions(parsed.Value).MountIds);
    }

    /// <summary>
    /// STUDIO-50: <c>--llm-profile</c> takes one id, is spelled by the shared constant — the one
    /// Studio writes into a team's launchers — and reaches the shared runner, whose own grammar
    /// (<see cref="Orkeon.Hosting.RunnerOptionsBase"/>) reads it the same way.
    /// </summary>
    [Fact]
    public void Llm_profile_takes_one_id_and_is_carried_to_the_shared_runner_options()
    {
        var parsed = Assert.IsType<Parsed<RunCommandOptions>>(
            Parse<RunCommandOptions>("crew.yaml", "--llm-profile", "z-ai"));

        Assert.Equal("z-ai", parsed.Value.LlmProfile);
        Assert.Equal("z-ai", RunCommand.ToRunnerOptions(parsed.Value).LlmProfile);
        Assert.Equal("llm-profile", Orkeon.Constants.Cli.RunOptionNames.LlmProfile);
        Assert.Equal(
            "z-ai",
            Assert.IsType<Parsed<SharedRunnerOptions>>(Parse<SharedRunnerOptions>("--llm-profile", "z-ai")).Value.LlmProfile);
        Assert.Null(Assert.IsType<Parsed<RunCommandOptions>>(Parse<RunCommandOptions>("crew.yaml")).Value.LlmProfile);
    }

    /// <summary>The shared runner's option grammar, as a parser target.</summary>
    private sealed class SharedRunnerOptions : Orkeon.Hosting.RunnerOptionsBase;

    /// <summary>
    /// STUDIO-51, decision 7: a single-value option written <c>--option=value</c> — how the team
    /// launchers and Studio write one — is read by both grammars, cut at the first <c>=</c> and the
    /// rest kept as it is: a value starting with a dash stays the option's value, where
    /// <c>--initial-context "- x"</c> is an unknown option. Read the way <c>orkeon run</c> reads its
    /// arguments (<see cref="Orkeon.Hosting.RunnerArguments"/>), a line break stays too.
    /// </summary>
    [Fact]
    public void Both_grammars_read_a_single_value_option_attached_with_an_equals_sign()
    {
        string[] attached = ["--settings=C:\\a=b\\appsettings.json", "--llm-profile=z-ai", "--initial-context=- x = y"];

        var run = Assert.IsType<Parsed<RunCommandOptions>>(Parse<RunCommandOptions>(["crew.yaml", .. attached])).Value;
        var shared = Assert.IsType<Parsed<SharedRunnerOptions>>(Parse<SharedRunnerOptions>(attached)).Value;
        using var parser = new Parser(s => s.HelpWriter = null);
        var bullets = Assert.IsType<Parsed<RunCommandOptions>>(
            Orkeon.Hosting.RunnerArguments.Parse<RunCommandOptions>(parser, ["crew.yaml", "--initial-context=- un\n- deux"])).Value;
        Assert.Equal("- un\n- deux", bullets.InitialContext);

        foreach (var (settings, profile, context) in new[] { (run.SettingsPath, run.LlmProfile, run.InitialContext), (shared.SettingsPath, shared.LlmProfile, shared.InitialContext) })
        {
            Assert.Equal("C:\\a=b\\appsettings.json", settings);
            Assert.Equal("z-ai", profile);
            Assert.Equal("- x = y", context);
        }

        Assert.Contains("UnknownOptionError", ErrorTags(Parse<SharedRunnerOptions>("--initial-context", "- x")));
    }

    /// <summary>
    /// STUDIO-51: <c>orkeon run</c> reads its arguments through
    /// <see cref="Orkeon.Hosting.RunnerArguments"/>, which sets aside only an attached value
    /// CommandLineParser refuses alone. Over a corpus of what the runner is handed today — the shapes
    /// the docs show, the launchers and Studio write, and what the grammar refuses —, both readings
    /// give the same options, every one of them, or the same errors: nothing that parses today parses
    /// otherwise.
    /// </summary>
    [Theory]
    [InlineData("crew.yaml")]
    [InlineData("crew.yaml", "--settings", "/etc/orkeon/appsettings.json")]
    [InlineData("crew.yaml", "-s", "appsettings.json")]
    [InlineData("crew.yaml", "--settings=/etc/orkeon/appsettings.json")]
    [InlineData("crew.yaml", "--mount", "a:/x:ro", "\"C:\\src\\\":/workspace:ro", "\"/data/odd:name\":/data:ro")]
    [InlineData("crew.yaml", "--mount-id", "01J9Z3K4M5N6P7Q8R9S0T1V2W3", "01J9Z3K4M5N6P7Q8R9S0T1V2W4")]
    [InlineData("crew.yaml", "--var", "TOPIC=quantum computing", "DEPTH=3", "--initial-context", "Focus on 2026 papers")]
    [InlineData("crew.yaml", "-V", "a=1", "--initial-context=- puce")]
    [InlineData("crew.yaml", "--initial-context", "Premier essai\nDeuxième ligne")]
    [InlineData("crew.yaml", "--initial-context", " commence par une espace")]
    [InlineData("crew.yaml", "--initial-context=Économie 日本語 🚀 = 10%")]
    [InlineData("crew.yaml", "--verbose", "2", "--llm-log", "--llm-log-path", "/var/log/orkeon", "--validate")]
    [InlineData("crew.yaml", "-v", "1", "--llm-log-path=/var/log/orkeon")]
    [InlineData("crew.ork.ts", "--inputs", "{\"topic\":\"rag\"}", "--inputs-file", "/crews/inputs.json")]
    [InlineData("crew.ork.ts", "--inputs={\"a\":\"b=c\"}", "--memory-limit-mb", "200")]
    [InlineData("crew.yaml", "--events", "jsonl", "--stream", "--client", "studio")]
    [InlineData("crew.yaml", "--events", "jsonl", "--client=studio")]
    [InlineData("crew.yaml", "--llm-profile", "z-ai", "--allow-external-mounts")]
    [InlineData("--list-tools")]
    [InlineData("crew.yaml", "--initial-context=")]
    [InlineData("crew.yaml", "--initial-context", "- puce")]
    [InlineData("crew.yaml", "--mount", "a:/x:ro", "--mount", "b:/y:rw")]
    [InlineData("crew.yaml", "--nope")]
    [InlineData("crew.yaml", "--settings")]
    [InlineData("crew.yaml", "--verbose", "deux")]
    public void What_the_parser_reads_alone_the_runner_reads_the_same(params string[] arguments)
    {
        static Parser RunParser() => new(s =>
        {
            s.HelpWriter = null;
            s.CaseInsensitiveEnumValues = true;
        });

        using var stock = RunParser();
        using var runner = RunParser();

        Assert.Equal(
            Describe(stock.ParseArguments<RunCommandOptions>(arguments)),
            Describe(Orkeon.Hosting.RunnerArguments.Parse<RunCommandOptions>(runner, arguments)));
    }

    /// <summary>A parse as text: every option's value, or every error.</summary>
    private static string Describe(ParserResult<RunCommandOptions> result) => result switch
    {
        Parsed<RunCommandOptions> parsed => string.Join('\n', typeof(RunCommandOptions).GetProperties()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={Text(property.GetValue(parsed.Value))}")),
        NotParsed<RunCommandOptions> refused => "refused: " + string.Join(", ", refused.Errors.Select(e => e.Tag)),
        _ => "?",
    };

    private static string Text(object? value) => value switch
    {
        null => "<null>",
        string text => text,
        IEnumerable<string> values => "[" + string.Join('|', values) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    [Fact]
    public void The_same_rule_governs_var()
    {
        Assert.IsType<Parsed<RunCommandOptions>>(
            Parse<RunCommandOptions>("crew.yaml", "--var", "a=1", "b=2"));

        Assert.Contains(
            "RepeatedOptionError",
            ErrorTags(Parse<RunCommandOptions>("crew.yaml", "--var", "a=1", "--var", "b=2")));
    }

    /// <summary>
    /// `--source` is REQUIRED on `rag ingest` and takes several values, so the help text is the
    /// only thing telling the user how to pass a second one.
    /// </summary>
    [Fact]
    public void The_same_rule_governs_the_rag_ingest_sources()
    {
        var parsed = Assert.IsType<Parsed<RagIngestCommandOptions>>(
            Parse<RagIngestCommandOptions>("-c", "docs", "--source", "./a/**/*.md", "./b/**/*.md"));
        Assert.Equal(["./a/**/*.md", "./b/**/*.md"], parsed.Value.Sources);

        Assert.Contains(
            "RepeatedOptionError",
            ErrorTags(Parse<RagIngestCommandOptions>(
                "-c", "docs", "--source", "./a/**/*.md", "--source", "./b/**/*.md")));
    }

    /// <summary>
    /// The help text has to say the rule the parser enforces. This is the assertion that
    /// would have failed while `orkeon run --help` still advertised "Repeatable.".
    /// </summary>
    [Theory]
    [InlineData(typeof(RunCommandOptions))]
    [InlineData(typeof(RagIngestCommandOptions))]
    [InlineData(typeof(RagSearchCommandOptions))]
    [InlineData(typeof(RagEvalCommandOptions))]
    public void No_parsed_option_calls_itself_repeatable(Type optionsType)
    {
        var offenders = optionsType.GetProperties()
            .SelectMany(p => p.GetCustomAttributes(typeof(OptionAttribute), inherit: true)
                .Cast<OptionAttribute>()
                .Where(o => o.HelpText.Contains("Repeatable", StringComparison.OrdinalIgnoreCase))
                .Select(o => o.LongName))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{optionsType.Name} advertises as repeatable: {string.Join(", ", offenders)} — "
            + "the parser answers RepeatedOptionError. Say the values go space-separated after one flag.");
    }

    /// <summary>
    /// CH-04: the forms the docs (<c>docs/reference/cli.md</c>), Studio, the smokes and
    /// <c>.github/workflows/rag-eval.yml</c> write, per options type, and the refusals.
    /// </summary>
    private static IEnumerable<(Type Type, string[] Args)> Corpus()
    {
        (Type, string[]) Row<T>(params string[] args) => (typeof(T), args);

        return
        [
            Row<DoctorCommandOptions>(),
            Row<DoctorCommandOptions>("--json"),
            Row<DoctorCommandOptions>("--nope"),
            Row<InitCommandOptions>("--provider", "ollama", "--model", "llama3.2", "--no-probe"),
            Row<InitCommandOptions>("-p", "custom", "-u", "http://x", "-m", "m", "--api-key-env=MY_KEY", "--path=/tmp/a b", "-f"),
            Row<InitCommandOptions>("--provider"),
            Row<LlmProbeCommandOptions>("-p", "openai", "-m", "gpt", "--api-version=2024-10-01"),
            Row<LlmProbeCommandOptions>("--model", "x"),
            Row<LlmModelsCommandOptions>("-p", "ollama", "--filter", "llama*"),
            Row<LlmModelsCommandOptions>("-p", "ollama", "--json", "--filter=gpt-*"),
            Row<RagIngestCommandOptions>("-c", "docs", "--source", "./a/**/*.md", "./b/*.md", "--chunking", "fixed", "--reindex"),
            Row<RagIngestCommandOptions>("-c", "docs"),
            Row<RagSearchCommandOptions>("what is x?", "-c", "docs", "--top-n", "3"),
            Row<RagSearchCommandOptions>("-c", "docs", "-v", "1", "--mount", "a:/x:ro", "b:/y:rw", "what?"),
            Row<RagSearchCommandOptions>("-c", "docs"),
            Row<RagEvalCommandOptions>("--dataset", "examples/rag/eval/golden.yaml", "--compare", "fast,balanced,quality,corrective,adaptive", "--offline"),
            Row<RagEvalCommandOptions>("--dataset", "g.yaml", "--profile", "balanced", "--offline", "--reindex", "--min-recall", "0.8", "--min-mrr", "0.7"),
            Row<RagEvalCommandOptions>("--dataset=g.yaml", "-k", "3"),
            Row<RagEvalCommandOptions>("--offline"),
            Row<EmailAccountsCommandOptions>("--json", "-s", "a.json"),
            Row<EmailAccountsCommandOptions>("--settings=a.json"),
            Row<EmailLoginCommandOptions>("hotmail", "--events", "jsonl"),
            Row<EmailLoginCommandOptions>("hotmail", "--events=jsonl"),
            Row<EmailLoginCommandOptions>(),
            Row<EmailLogoutCommandOptions>("hotmail"),
            Row<EmailCheckCommandOptions>("hotmail", "--events", "jsonl", "--settings", "s.json"),
            Row<DriveAccountsCommandOptions>("--json", "-s", "a.json"),
            Row<DriveAccountsCommandOptions>("--settings=a.json"),
            Row<DriveLoginCommandOptions>("pro", "--events", "jsonl"),
            Row<DriveLoginCommandOptions>("pro", "--events=jsonl"),
            Row<DriveLoginCommandOptions>(),
            Row<DriveLogoutCommandOptions>("pro"),
            Row<DriveCheckCommandOptions>("pro", "--events", "jsonl", "--settings", "s.json"),
            Row<UseCasesSearchOptions>("summarize my emails every morning"),
            Row<UseCasesSearchOptions>("je veux", "un résumé", "--top", "3", "--lang", "fr"),
            Row<UseCasesListOptions>("--category", "finance-trading", "--process", "parallel"),
            Row<UseCasesShowOptions>("03-email-pipeline", "--crew"),
            Row<UseCasesExportOptions>("01-research-assistant", "--to", "./research", "--lang", "fr"),
            Row<UseCasesExportOptions>("01-research-assistant", "--to=./x"),
            Row<McpServeCommandOptions>("--settings", "s.json", "--tools", "a,b"),
            Row<McpServeCommandOptions>("--tools=a,b"),
            Row<McpServeCommandOptions>("--nope"),
            Row<RunCommandOptions>("crew.yaml", "--settings=/etc/orkeon/appsettings.json", "--events", "jsonl", "--client=studio"),
        ];
    }

    /// <summary>The intended changes: a word no option or position takes is refused, where the parser alone drops it.</summary>
    private static IEnumerable<(Type Type, string[] Args)> IntendedChanges() =>
    [
        (typeof(RunCommandOptions), ["crew.yaml", "--validate", "extra"]),
        (typeof(RagSearchCommandOptions), ["question", "-c", "docs", "extra"]),
        (typeof(EmailLoginCommandOptions), ["hotmail", "extra"]),
        (typeof(DriveCheckCommandOptions), ["pro", "extra"]),
        (typeof(UseCasesShowOptions), ["03-email-pipeline", "extra"]),
    ];

    private static Parser Lenient() => new(s =>
    {
        s.HelpWriter = null;
        s.CaseInsensitiveEnumValues = true;
    });

    private static string Read(Type type, string[] args, bool asTheCommand)
    {
        object? value;
        if (asTheCommand)
        {
            using var commandParser = CliGrammar.NewParser();
            var reading = (CliReading)typeof(CliGrammar).GetMethods()
                .Single(m => m.Name == "Read" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
                .MakeGenericMethod(type).Invoke(null, [commandParser, args])!;
            if (reading.Options is null)
                return "refused: " + string.Join(", ", reading.Errors.Select(e => e.Tag));
            if (reading.Unexpected.Count > 0)
                return "refused: unexpected " + string.Join(", ", reading.Unexpected);
            value = reading.Options;
        }
        else
        {
            using var parser = Lenient();
            var result = typeof(Parser).GetMethods()
                .First(m => m.Name == "ParseArguments" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1
                    && m.GetParameters() is [{ ParameterType: var p }] && p == typeof(IEnumerable<string>))
                .MakeGenericMethod(type).Invoke(parser, [args])!;
            var resultType = result.GetType();
            if (resultType.Name.StartsWith("NotParsed", StringComparison.Ordinal))
            {
                var errors = (IEnumerable<Error>)resultType.GetProperty("Errors")!.GetValue(result)!;
                return "refused: " + string.Join(", ", errors.Select(e => e.Tag));
            }

            value = resultType.GetProperty("Value")!.GetValue(result)!;
        }

        return string.Join('\n', type.GetProperties()
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && p.GetMethod!.IsPublic)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.GetValue(value) switch
            {
                null => "<null>",
                string text => text,
                System.Collections.IEnumerable items => "[" + string.Join('|', items.Cast<object>()) + "]",
                var other => Convert.ToString(other, CultureInfo.InvariantCulture),
            }}"));
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public void ShouldReadEveryOptionTypeAsTheParserAlone_ExceptTheIntendedChanges()
    {
        foreach (var (type, args) in Corpus())
            Assert.Equal(Read(type, args, asTheCommand: false), Read(type, args, asTheCommand: true));

        foreach (var (type, args) in IntendedChanges())
        {
            Assert.DoesNotContain("refused", Read(type, args, asTheCommand: false), StringComparison.Ordinal);
            Assert.StartsWith("refused", Read(type, args, asTheCommand: true), StringComparison.Ordinal);
        }

        // F3: forge, typings and settings belong to batch F3.
        static bool OfF3(Type t) =>
            t.Namespace?.EndsWith(".Forge", StringComparison.Ordinal) == true
            || t.Name.StartsWith("Settings", StringComparison.Ordinal)
            || t.Name.StartsWith("Typings", StringComparison.Ordinal);

        var carriers = typeof(Program).Assembly.GetTypes()
            .Where(t => !OfF3(t) && t != typeof(CliOptions) && t.GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Any(p => p.IsDefined(typeof(OptionAttribute), false) || p.IsDefined(typeof(ValueAttribute), false)))
            .ToList();
        var covered = Corpus().Select(c => c.Type).ToHashSet();

        Assert.NotEmpty(carriers);
        Assert.Empty(carriers.Where(t => !typeof(CliOptions).IsAssignableFrom(t)).Select(t => t.Name));
        Assert.Empty(carriers.Where(t => !t.IsAbstract && !covered.Contains(t)).Select(t => t.Name));
    }
}
