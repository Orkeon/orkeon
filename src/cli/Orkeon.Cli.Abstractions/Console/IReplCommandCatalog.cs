namespace Orkeon.Cli.Abstractions.Console;

/// <summary>One command a REPL can list: what it runs by, and what it says it does.</summary>
/// <param name="Name">The command's name, without the leading <c>/</c>.</param>
/// <param name="Description">What the command does, for one row of a menu.</param>
public sealed record ReplCommandEntry(string Name, string Description)
{
    /// <summary>The other names that run it.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

/// <summary>
/// Optional capability of an <see cref="IReplInputAssist"/>: the commands with their descriptions, for an input
/// able to show a menu of them. A source without it keeps Tab completion alone.
/// </summary>
public interface IReplCommandCatalog
{
    /// <summary>Every command available now. Read when a menu opens, on the UI thread: it must be cheap and must not throw.</summary>
    IReadOnlyList<ReplCommandEntry> ListCommands();
}
