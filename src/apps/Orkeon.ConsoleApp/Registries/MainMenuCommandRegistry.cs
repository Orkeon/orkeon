using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Registry;
using Orkeon.ConsoleApp.Commands;

namespace Orkeon.ConsoleApp.Registries;

/// <summary>
/// Registry for the main menu runner. Auto-collects every <see cref="IMainMenuCommand"/>
/// registered in DI and exposes them sorted alphabetically by <c>Name</c>.
/// </summary>
internal sealed class MainMenuCommandRegistry : IInteractiveCommandRegistry
{
    private readonly IReadOnlyList<IInteractiveCommand> _commands;

    public MainMenuCommandRegistry(IEnumerable<IMainMenuCommand> commands)
    {
        _commands = commands
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IEnumerable<IInteractiveCommand> Commands => _commands;
    public IInteractiveCommand? Fallback => null;
}
