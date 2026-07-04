using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Registry;
using Orkeon.ConsoleApp.Registries;

namespace Orkeon.ConsoleApp.Runners;

/// <summary>
/// Top-level interactive runner for the Orkeon console application. Dispatches
/// to agent / crew / task / qa / demo commands.
/// </summary>
internal sealed class MainMenuRunner : InteractiveRunnerBase
{
    public MainMenuRunner(
        DefaultCommandRegistry defaults,
        MainMenuCommandRegistry specific,
        IConsoleAdapter console,
        ILogger<MainMenuRunner> logger,
        IServiceProvider services)
        : base(defaults, specific, console, logger, services)
    {
    }

    protected override string Banner => """

==================================
   Orkeon Console Application
==================================

Type 'help' to see available commands.

""";

    protected override string Prompt => "orkeon> ";
}
