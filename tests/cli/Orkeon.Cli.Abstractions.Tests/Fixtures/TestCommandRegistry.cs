using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Registry;

namespace Orkeon.Cli.Abstractions.Tests.Fixtures;

/// <summary>Simple in-memory registry for tests.</summary>
public sealed class TestCommandRegistry : IInteractiveCommandRegistry
{
    public TestCommandRegistry(params IInteractiveCommand[] commands)
    {
        Commands = commands;
        Fallback = null;
    }

    public static TestCommandRegistry WithFallback(IInteractiveCommand fallback, params IInteractiveCommand[] commands)
        => new(commands) { Fallback = fallback };

    public IEnumerable<IInteractiveCommand> Commands { get; }
    public IInteractiveCommand? Fallback { get; init; }
}
