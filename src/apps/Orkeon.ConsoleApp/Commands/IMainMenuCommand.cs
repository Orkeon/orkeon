using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.ConsoleApp.Commands;

/// <summary>
/// Marker interface for commands that belong to the main menu runner. Used by
/// <c>MainMenuCommandRegistry</c> to discover all main-menu commands via DI
/// (<c>IEnumerable&lt;IMainMenuCommand&gt;</c>) without having to modify the
/// registry constructor each time a new command is added.
/// </summary>
internal interface IMainMenuCommand : IInteractiveCommand
{
}
