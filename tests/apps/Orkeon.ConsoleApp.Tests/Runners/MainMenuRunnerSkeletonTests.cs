using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;
using Orkeon.ConsoleApp.Commands;
using Orkeon.ConsoleApp.Registries;
using Orkeon.ConsoleApp.Runners;

namespace Orkeon.ConsoleApp.Tests.Runners;

public sealed class MainMenuRunnerSkeletonTests : IDisposable
{
    private readonly TestConsoleAdapter _console = new();

    public void Dispose() => _console.Dispose();

    private MainMenuRunner Build(IEnumerable<IMainMenuCommand>? commands = null)
    {
        var defaults = new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());
        var registry = new MainMenuCommandRegistry(commands ?? Array.Empty<IMainMenuCommand>());
        var sp = new ServiceCollection().BuildServiceProvider();
        return new MainMenuRunner(
            defaults,
            registry,
            _console,
            NullLogger<MainMenuRunner>.Instance,
            sp);
    }

    [Fact]
    public void MainMenuRunner_constructor_resolves_dependencies()
    {
        var runner = Build();
        Assert.NotNull(runner);
    }

    [Fact]
    public void MainMenuRunner_banner_contains_orkeon_header()
    {
        var runner = Build();
        var bannerProp = typeof(MainMenuRunner).GetProperty("Banner",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var banner = (string?)bannerProp!.GetValue(runner);

        Assert.NotNull(banner);
        Assert.Contains("Orkeon Console Application", banner!);
    }

    [Fact]
    public void MainMenuRunner_prompt_is_orkeon_arrow()
    {
        var runner = Build();
        var promptProp = typeof(MainMenuRunner).GetProperty("Prompt",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var prompt = (string?)promptProp!.GetValue(runner);

        Assert.Equal("orkeon> ", prompt);
    }

    [Fact]
    public void MainMenuCommandRegistry_skeleton_has_no_commands_no_fallback()
    {
        var reg = new MainMenuCommandRegistry(Array.Empty<IMainMenuCommand>());

        Assert.Empty(reg.Commands);
        Assert.Null(reg.Fallback);
    }
}
