using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Cli.Abstractions.Registry;

/// <summary>
/// A collection of commands provided to a runner.
/// </summary>
public interface IInteractiveCommandRegistry
{
    /// <summary>
    /// All commands offered by this registry. Enumeration order must be stable
    /// (consumed by <c>HelpCommand</c> for display).
    /// </summary>
    IEnumerable<IInteractiveCommand> Commands { get; }

    /// <summary>
    /// Optional command invoked when no command matches the user input. Used
    /// e.g. by the QA runner to treat any unrecognized line as a question.
    /// Default: <see langword="null"/> (unknown input → error message).
    /// </summary>
    IInteractiveCommand? Fallback => null;
}
