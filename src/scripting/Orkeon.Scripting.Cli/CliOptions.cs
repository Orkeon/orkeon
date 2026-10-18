using CommandLine;

namespace Orkeon.Scripting.Cli;

/// <summary>The base of every options type read by CommandLineParser: it collects the words no option or value took.</summary>
internal abstract class CliOptions
{
    /// <summary>The words that followed the last value the command takes; <see cref="CliGrammar"/> refuses them.</summary>
    [Value(99, Hidden = true)]
    public IEnumerable<string> Unexpected { get; set; } = [];
}
