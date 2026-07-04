using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Abstractions.Commands;

/// <summary>
/// Context passed to each command invocation. Carries the original input,
/// parsed arguments, console adapter, and DI scope.
/// </summary>
public sealed record CommandContext
{
    /// <summary>Raw user input line (untrimmed). Useful for fallback commands that re-parse themselves.</summary>
    public required string RawInput { get; init; }

    /// <summary>Tokens remaining after extracting the command name. Never null; may be empty.</summary>
    public required IReadOnlyList<string> Args { get; init; }

    /// <summary>Console adapter for I/O.</summary>
    public required IConsoleAdapter Console { get; init; }

    /// <summary>DI scope for the current iteration of the runner loop.</summary>
    public required IServiceProvider Scope { get; init; }
}
