using CommandLine;
using Orkeon.Scripting.Cli.Commands;

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
}
