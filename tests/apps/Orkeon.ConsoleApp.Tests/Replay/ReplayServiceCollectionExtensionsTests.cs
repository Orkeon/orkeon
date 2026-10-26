using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Registry;
using Orkeon.ConsoleApp.Replay;
using Orkeon.ConsoleApp.Services;
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.Timing;

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

    /// <summary>
    /// Resolves the console under a hang guard, not a bound: the cycle of #212 never returns (the container
    /// waits on itself), and a test that freezes the run says less than one that fails. The provider is not
    /// disposed on that path: its disposal would wait on the same resolution.
    /// </summary>
    private static async Task<IConsoleAdapter> ResolveConsoleAsync(ServiceProvider provider)
    {
        var ct = TestContext.Current.CancellationToken;
        var resolution = Task.Run(() => provider.GetRequiredService<IConsoleAdapter>(), ct);
        var first = await Task.WhenAny(resolution, Task.Delay(Polling.DefaultTimeout, ct));
        Assert.True(first == resolution, "resolving the terminal console did not return: the console resolves itself (#212)");
        return await resolution;
    }

    [Fact]
    public async Task ShouldResolveTheConsole_WhenTheLineEditorTakesTheCompletion()
    {
        var provider = BuildTerminalRepl();

        var console = await ResolveConsoleAsync(provider);
        try
        {
            Assert.IsType<ReplayConsoleAdapter>(console);
            Assert.Same(console, provider.GetRequiredService<ReplayConsoleAdapter>());
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task ShouldOfferReplayAmongTheUniversalCommands_WhenTheConsoleIsResolvedFirst()
    {
        var provider = BuildTerminalRepl();

        await ResolveConsoleAsync(provider);
        try
        {
            var names = provider.GetRequiredService<DefaultCommandRegistry>().Commands.Select(c => c.Name).ToList();

            Assert.Equal(["help", "exit", "clear", "replay"], names);
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }
}
