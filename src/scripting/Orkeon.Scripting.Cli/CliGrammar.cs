using CommandLine;
using CommandLine.Text;
using Orkeon.Hosting;

namespace Orkeon.Scripting.Cli;

/// <summary>
/// What <see cref="CliGrammar"/> read from an argument vector, with nothing written anywhere:
/// the parsed <see cref="Options"/> when the line is accepted; otherwise the parser's
/// <see cref="Errors"/> (a usage error, a help or a version asked for), or the <see cref="Unexpected"/>
/// words the hidden value collected (<see cref="Options"/> is then set too).
/// </summary>
/// <param name="Options">The options read; null when the parser refused the line.</param>
/// <param name="Errors">The parser's errors; empty when it parsed.</param>
/// <param name="Unexpected">The words no option or value took; empty when none.</param>
/// <param name="Help">Builds the help or usage text of the command read.</param>
internal sealed record CliReading(
    CliOptions? Options,
    IReadOnlyList<Error> Errors,
    IReadOnlyList<string> Unexpected,
    Func<string> Help)
{
    /// <summary>True when the line is accepted: parsed, with no word left over.</summary>
    public bool Accepted => Options is not null && Unexpected.Count == 0;

    /// <summary>True when the parser refused the line only because a help or a version was asked for.</summary>
    public bool Asked => Options is null && (Errors.IsHelp() || Errors.IsVersion());
}

/// <summary>
/// The one entry through which every CommandLineParser command reads its arguments: an option written
/// <c>--option=value</c> as written, what follows <c>--</c> positional, a word no option took refused
/// before any work, a usage error on stderr, a help or version on the command's help output.
/// </summary>
internal static class CliGrammar
{
    private const int HelpWidth = 80;

    /// <summary>Reads a command with a single options type; writes nothing.</summary>
    public static CliReading Read<TOptions>(Parser parser, string[] args)
        where TOptions : CliOptions
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(args);

        var result = RunnerArguments.Parse<TOptions>(parser, args);
        return Reading(result, result is Parsed<TOptions> parsed ? parsed.Value : null);
    }

    /// <summary>Reads a command with sub-commands, the first argument choosing among <paramref name="verbs"/>; writes nothing.</summary>
    public static CliReading Read(Parser parser, string[] args, params Type[] verbs)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(args);

        var result = RunnerArguments.Parse(parser, args, verbs);
        return Reading(result, result is Parsed<object> { Value: CliOptions options } ? options : null);
    }

    /// <summary>Reads a command with a single options type and runs <paramref name="run"/> on it.</summary>
    public static Task<int> DispatchAsync<TOptions>(string command, string[] args, Func<TOptions, Task<int>> run, bool helpOnStderr = false)
        where TOptions : CliOptions
    {
        using var parser = NewParser();
        var reading = Read<TOptions>(parser, args);
        return reading.Accepted ? run((TOptions)reading.Options!) : Task.FromResult(Refuse(command, reading, helpOnStderr));
    }

    /// <summary>Reads a command with sub-commands, the verb choosing among <paramref name="verbs"/>.</summary>
    public static Task<int> DispatchAsync(string command, string[] args, Type[] verbs, Func<CliOptions, Task<int>> run, bool helpOnStderr = false)
    {
        using var parser = NewParser();
        var reading = Read(parser, args, verbs);
        return reading.Accepted ? run(reading.Options!) : Task.FromResult(Refuse(command, reading, helpOnStderr));
    }

    /// <summary>The parser with the settings every command shares.</summary>
    internal static Parser NewParser() => new(s =>
    {
        s.HelpWriter = null;
        s.CaseInsensitiveEnumValues = true;
        s.EnableDashDash = true;
    });

    private static CliReading Reading<T>(ParserResult<T> result, CliOptions? options) => new(
        options,
        result is NotParsed<T> notParsed ? notParsed.Errors.ToList() : [],
        options?.Unexpected.ToList() ?? [],
        () => HelpText.AutoBuild(result, HelpWidth));

    private static int Refuse(string command, CliReading reading, bool helpOnStderr)
    {
        if (reading.Options is not null)
        {
            Console.Error.WriteLine($"orkeon {command}: unexpected argument '{reading.Unexpected[0]}'");
            return Program.ExitScriptError;
        }

        (reading.Asked && !helpOnStderr ? Console.Out : Console.Error).WriteLine(reading.Help());
        return reading.Asked ? Program.ExitOk : Program.ExitScriptError;
    }
}
