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
    {
        _commands = new IInteractiveCommand[] { help, exit, clear };
    }

    public IEnumerable<IInteractiveCommand> Commands => _commands;
    public IInteractiveCommand? Fallback => null;
}
