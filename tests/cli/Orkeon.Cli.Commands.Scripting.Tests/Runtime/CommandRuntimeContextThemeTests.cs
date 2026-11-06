using Jint;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

public sealed class CommandRuntimeContextThemeTests
{
    private static Engine NewEngine() => new(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);

    private static CommandRuntimeContext Context(Engine engine, IConsoleAdapter console)
        => new(engine, console, new CommandMeta("theme", "theme"), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("light", ConsoleTheme.Light)]
    [InlineData("none", ConsoleTheme.None)]
    [InlineData("dark", ConsoleTheme.Dark)]
    public void SetTheme_hands_the_palette_to_the_console(string name, ConsoleTheme expected)
    {
        // Arrange
        using var engine = NewEngine();
        var console = new ScriptedThemeConsole();
        var ctx = Context(engine, console);

        // Act
        ctx.setTheme(name);

        // Assert
        Assert.Equal(expected, console.Theme);
    }

    [Fact]
    public void SetTheme_refuses_an_unknown_palette()
    {
        // Arrange
        using var engine = NewEngine();
        var ctx = Context(engine, new ScriptedThemeConsole());

        // Act
        var refused = Record.Exception(() => ctx.setTheme("sepia"));

        // Assert
        Assert.IsType<ArgumentException>(refused);
    }

    [Fact]
    public void SetTheme_answers_false_on_a_console_without_palettes()
    {
        // Arrange
        using var engine = NewEngine();
        var ctx = Context(engine, new ScriptedBlockConsole());

        // Act
        var applied = ctx.setTheme("light");

        // Assert
        Assert.False(applied);
    }

    [Fact]
    public void Theme_names_the_palette_of_the_console()
    {
        // Arrange
        using var engine = NewEngine();
        var console = new ScriptedThemeConsole();
        console.SetTheme(ConsoleTheme.Light);
        var ctx = Context(engine, console);

        // Act
        var name = ctx.theme;

        // Assert
        Assert.Equal("light", name);
    }

    [Fact]
    public void Theme_is_null_on_a_console_without_palettes()
    {
        // Arrange
        using var engine = NewEngine();
        var ctx = Context(engine, new ScriptedBlockConsole());

        // Act
        var name = ctx.theme;

        // Assert
        Assert.Null(name);
    }

    [Fact]
    public void A_decorator_over_a_console_without_palettes_reads_as_having_none()
    {
        // Arrange
        using var engine = NewEngine();
        var console = new ScriptedThemeConsole { HasPalette = false };
        var ctx = Context(engine, console);

        // Act
        var changed = ctx.setTheme("light");

        // Assert
        Assert.Equal((false, null, ConsoleTheme.Dark), (changed, ctx.theme, console.Theme));
    }
}
