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

    /// <summary>
    /// Parses <paramref name="arguments"/> into <typeparamref name="TOptions"/> with
    /// <paramref name="parser"/>. An attached value CommandLineParser would refuse — it holds a
    /// line break or starts with a space — of a single-value option is set aside, a stand-in parsed
    /// in its place, and put back on the option's property once parsed; every other argument
    /// reaches CommandLineParser as it is, so what it reads alone it reads the same. An empty
    /// attached value stays refused.
    /// </summary>
    /// <param name="parser">The parser, with the runner's settings.</param>
    /// <param name="arguments">The arguments after the program's name.</param>
    public static ParserResult<TOptions> Parse<TOptions>(Parser parser, IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(arguments);

        var singleValueOptions = typeof(TOptions).GetProperties()
            .Where(property => property.PropertyType == typeof(string) && property.CanWrite)
            .Select(property => (Property: property, Option: property.GetCustomAttribute<OptionAttribute>(inherit: true)))
            .Where(option => option.Option?.LongName is { Length: > 0 })
            .ToDictionary(option => option.Option!.LongName, option => option.Property, StringComparer.Ordinal);

        var setAside = new Dictionary<PropertyInfo, string>();
        var parsed = new List<string>();
        foreach (var argument in arguments)
        {
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

        var result = parser.ParseArguments<TOptions>(parsed);
        if (result is Parsed<TOptions> options)
        {
            foreach (var (property, value) in setAside)
            {
                if (string.Equals(property.GetValue(options.Value) as string, StandIn, StringComparison.Ordinal))
                    property.SetValue(options.Value, value);
            }
        }

        return result;
    }
}
