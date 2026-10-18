using System.Reflection;
using CommandLine;

namespace Orkeon.Hosting;

/// <summary>
/// How a runner reads its argument vector: CommandLineParser, every single-value option written
/// <c>--option=value</c> read with its value as written (STUDIO-51). The team launchers and
/// Orkeon Studio write a single-value option that way — <c>--option value</c> reads a value
/// starting with <c>-</c> as the next option, and an initial context is often a bullet list —,
/// but CommandLineParser 2.9.1 reads an attached value only when it holds no line break and
/// starts with no space (its tokenizer matches <c>^([^=]+)=([^ ].*)$</c>): a context of two
/// bullet lines reached the runner by no spelling at all.
/// </summary>
public static class RunnerArguments
{
    /// <summary>What stands in for a value CommandLineParser would refuse while it parses.</summary>
    private const string StandIn = "orkeon-attached-value";

    private const string MissingValueVerb = "missing-value";

    /// <summary>
    /// Parses <paramref name="arguments"/> into <typeparamref name="TOptions"/> with
    /// <paramref name="parser"/>. An attached value CommandLineParser would refuse — it holds a
    /// line break or starts with a space — of a single-value option is set aside, a stand-in parsed
    /// in its place, and put back on the option's property once parsed; every other argument
    /// reaches CommandLineParser as it is, so what it reads alone it reads the same. An empty
    /// attached value stays refused, and so does a one-value option followed by a bare <c>--</c>
    /// (the parser refuses a line made of an option it does not know).
    /// </summary>
    /// <param name="parser">The parser, with the runner's settings.</param>
    /// <param name="arguments">The arguments after the program's name.</param>
    public static ParserResult<TOptions> Parse<TOptions>(Parser parser, IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(arguments);

        var (parsed, setAside, missing) = SetAside(typeof(TOptions), arguments, parser.Settings.EnableDashDash);
        if (missing)
            return parser.ParseArguments<TOptions>(["--" + MissingValueVerb]);

        var result = parser.ParseArguments<TOptions>(parsed);
        if (result is Parsed<TOptions> options)
            PutBack(options.Value!, setAside);

        return result;
    }

    /// <summary>Parses a command with sub-commands: the options type the first argument chooses among <paramref name="verbs"/>.</summary>
    /// <param name="parser">The parser, with the runner's settings.</param>
    /// <param name="args">The arguments after the program's name.</param>
    /// <param name="verbs">The options types, each marked with a verb.</param>
    public static ParserResult<object> Parse(Parser parser, IEnumerable<string> args, params Type[] verbs)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(verbs);

        var argv = args.ToList();
        var chosen = argv.Count == 0 ? null : verbs.FirstOrDefault(verb => verb.GetCustomAttribute<VerbAttribute>() is { } attribute
            && (string.Equals(attribute.Name, argv[0], StringComparison.Ordinal)
                || attribute.Aliases.Contains(argv[0], StringComparer.Ordinal)));
        if (chosen is null)
            return parser.ParseArguments(argv, verbs);

        var (parsed, setAside, missing) = SetAside(chosen, argv, parser.Settings.EnableDashDash);
        if (missing)
            return parser.ParseArguments([MissingValueVerb, "--value"], typeof(MissingValueOptions));

        var result = parser.ParseArguments(parsed, verbs);
        if (result is Parsed<object> options && chosen.IsInstanceOfType(options.Value))
            PutBack(options.Value, setAside);

        return result;
    }

    private static (List<string> Parsed, Dictionary<PropertyInfo, string> SetAside, bool Missing) SetAside(
        Type optionsType, IEnumerable<string> arguments, bool enableDashDash)
    {
        var oneValueOptions = optionsType.GetProperties()
            .Where(property => property.CanWrite && IsOneValue(property.PropertyType))
            .Select(property => (Property: property, Option: property.GetCustomAttribute<OptionAttribute>(inherit: true)))
            .Where(option => option.Option is not null)
            .ToList();
        var oneValueWords = oneValueOptions
            .SelectMany(option => new[] { "--" + option.Option!.LongName, "-" + option.Option.ShortName })
            .Where(word => word.Length > 2 || (word.Length == 2 && word[1] != '-'))
            .ToHashSet(StringComparer.Ordinal);
        var singleValueOptions = oneValueOptions
            .Where(option => option.Property.PropertyType == typeof(string) && option.Option!.LongName is { Length: > 0 })
            .ToDictionary(option => option.Option!.LongName, option => option.Property, StringComparer.Ordinal);

        var setAside = new Dictionary<PropertyInfo, string>();
        var parsed = new List<string>();
        var afterDashDash = false;
        string? previous = null;
        foreach (var argument in arguments)
        {
            if (afterDashDash)
            {
                parsed.Add(argument);
                continue;
            }

            if (enableDashDash && argument == "--")
            {
                afterDashDash = true;
                if (previous is not null)
                    return (parsed, setAside, true);
            }

            previous = oneValueWords.Contains(argument) ? argument : null;

            var equals = argument.StartsWith("--", StringComparison.Ordinal) ? argument.IndexOf('=', StringComparison.Ordinal) : -1;
            if (equals > 2
                && argument[(equals + 1)..] is { Length: > 0 } value
                && (value[0] == ' ' || value.Contains('\n', StringComparison.Ordinal))
                && singleValueOptions.TryGetValue(argument[2..equals], out var property))
            {
                setAside[property] = value;
                parsed.Add(argument[..(equals + 1)] + StandIn);
                continue;
            }

            parsed.Add(argument);
        }

        return (parsed, setAside, false);
    }

    private static bool IsOneValue(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying != typeof(bool)
            && (underlying == typeof(string) || !typeof(System.Collections.IEnumerable).IsAssignableFrom(underlying));
    }

    private static void PutBack(object options, Dictionary<PropertyInfo, string> setAside)
    {
        foreach (var (property, value) in setAside)
        {
            if (string.Equals(property.GetValue(options) as string, StandIn, StringComparison.Ordinal))
                property.SetValue(options, value);
        }
    }

    /// <summary>
    /// CommandLineParser reads a one-value option left without its value as unset, and its refusals
    /// cannot be built outside it: a sequence option left empty is the refusal it raises for a missing value.
    /// </summary>
    [Verb(MissingValueVerb)]
    private sealed class MissingValueOptions
    {
        [Option("value")]
        public IEnumerable<string> Value { get; set; } = [];
    }
}
