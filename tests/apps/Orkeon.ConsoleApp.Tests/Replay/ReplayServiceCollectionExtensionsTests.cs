using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Registry;
using Orkeon.ConsoleApp.Replay;
using Orkeon.ConsoleApp.Services;
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Domain.FileSystem;

namespace Orkeon.ConsoleApp.Tests.Replay;

/// <summary>
/// The replay's registrations over the console the REPL chose. With stdin a terminal the console is the line
/// editor (or the TUI), which takes the completion, which reads the universal commands, <c>/replay</c> among them:
/// resolving the console must not come back to the console being built (#212).
/// </summary>
public sealed class ReplayServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildTerminalRepl()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<HelpCommand>();
        services.AddSingleton<ExitCommand>();
        services.AddSingleton<ClearCommand>();
        services.AddSingleton(new ScriptCommandRegistry([]));
        services.AddSingleton(sp => new DefaultCommandRegistry(
            sp.GetRequiredService<HelpCommand>(),
            sp.GetRequiredService<ExitCommand>(),
            sp.GetRequiredService<ClearCommand>()));
        // Program's choice when stdin is a terminal: the line editor, completed by the host's assist.
        services.AddSingleton<IConsoleAdapter, LineEditingConsoleAdapter>();
        services.AddSingleton<IReplInputAssist, ReplInputAssist>();
        services.AddReplReplay(string.Empty);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ShouldResolveTheConsole_WhenTheLineEditorTakesTheCompletion()
    {
        using var provider = BuildTerminalRepl();

        var console = provider.GetRequiredService<IConsoleAdapter>();

        Assert.IsType<ReplayConsoleAdapter>(console);
        Assert.Same(console, provider.GetRequiredService<ReplayConsoleAdapter>());
    }

    [Fact]
    public void ShouldOfferReplayAmongTheUniversalCommands_WhenTheConsoleIsResolvedFirst()
    {
        using var provider = BuildTerminalRepl();
        provider.GetRequiredService<IConsoleAdapter>();

        var names = provider.GetRequiredService<DefaultCommandRegistry>().Commands.Select(c => c.Name).ToList();

        Assert.Equal(["help", "exit", "clear", "replay"], names);
    }
}
