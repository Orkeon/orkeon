using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Registry;
using Orkeon.Cli.Commands;

namespace Orkeon.Cli.Registry;

/// <summary>
/// Singleton registry providing the universal commands (help, exit, clear).
/// Each runner pairs its specific registry with this one.
/// </summary>
public sealed class DefaultCommandRegistry : IInteractiveCommandRegistry
{
    private readonly IReadOnlyList<IInteractiveCommand> _commands;

    public DefaultCommandRegistry(HelpCommand help, ExitCommand exit, ClearCommand clear)
        : this(help, exit, clear, Array.Empty<IInteractiveCommand>())
    {
    }

    /// <summary>
    /// The universal commands, then the host's own built-ins (the REPL's <c>replay</c>), offered by every runner.
    /// </summary>
    /// <param name="help">The <c>help</c> command.</param>
    /// <param name="exit">The <c>exit</c> command.</param>
    /// <param name="clear">The <c>clear</c> command.</param>
    /// <param name="additional">The host's built-ins, after the three.</param>
    public DefaultCommandRegistry(HelpCommand help, ExitCommand exit, ClearCommand clear, IEnumerable<IInteractiveCommand> additional)
    {
        ArgumentNullException.ThrowIfNull(additional);
        _commands = [help, exit, clear, .. additional];
    }

    public IEnumerable<IInteractiveCommand> Commands => _commands;
    public IInteractiveCommand? Fallback => null;
}
