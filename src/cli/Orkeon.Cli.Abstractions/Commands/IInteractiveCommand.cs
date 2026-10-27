namespace Orkeon.Cli.Abstractions.Commands;

/// <summary>
/// A command executable from an interactive REPL prompt.
/// </summary>
/// <remarks>
/// Invariants:
/// <list type="bullet">
///   <item><description><see cref="Name"/> is non-empty and contains no '\n' or '\t'.</description></item>
///   <item><description><see cref="Aliases"/> does not contain <see cref="Name"/>.</description></item>
///   <item><description>Within a single registry, no two commands share a <see cref="Name"/> or aliasing token.</description></item>
/// </list>
/// </remarks>
public interface IInteractiveCommand
{
    /// <summary>Primary identifier of the command (case-insensitive at resolution).</summary>
    string Name { get; }

    /// <summary>Alternate identifiers. May be empty. Must not contain <see cref="Name"/>.</summary>
    IReadOnlyList<string> Aliases { get; }

    /// <summary>Single-line description shown by the help command.</summary>
    string Description { get; }

    /// <summary>
    /// Optional group the help command lists this command under (<c>[group]</c> block). <see langword="null"/>
    /// (the default) leaves the grouping to the first token of <see cref="Name"/>. A default interface member:
    /// existing commands compile unchanged.
    /// </summary>
    string? Group => null;

    /// <summary>Execute the command. Returns a <see cref="CommandResult"/> instructing the runner to continue or exit.</summary>
    Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken);
}
