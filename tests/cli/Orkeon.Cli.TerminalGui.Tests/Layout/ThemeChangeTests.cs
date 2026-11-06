using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Console;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Tests.Doubles;
using Terminal.Gui.ViewBase;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

/// <summary>A palette changed while the console runs: <see cref="SchemeFactory.Theme"/> is static, hence the collection.</summary>
[Collection(TerminalGuiApplicationCollection.Name)]
public sealed class ThemeChangeTests : IDisposable
{
    private readonly ConsoleTheme _theme = SchemeFactory.Theme;

    public void Dispose() => SchemeFactory.Theme = _theme;

    [Fact]
    public void A_palette_applied_becomes_the_one_in_force()
    {
        // Arrange
        SchemeFactory.Theme = ConsoleTheme.Dark;
        using var pane = new ReplPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);

        // Act
        pane.ApplyTheme(ConsoleTheme.Light);

        // Assert
        Assert.Equal(ConsoleTheme.Light, SchemeFactory.Theme);
    }

    [Fact]
    public void A_palette_applied_repaints_the_status_line()
    {
        // Arrange
        SchemeFactory.Theme = ConsoleTheme.Dark;
        var darkAccent = SchemeFactory.Accent().Normal;
        using var pane = new ReplPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);

        // Act
        pane.ApplyTheme(ConsoleTheme.Light);

        // Assert
        var painted = pane.StatusLine.SubViews.First().GetScheme().Normal;
        Assert.NotEqual(darkAccent, painted);
        Assert.Equal(SchemeFactory.Accent().Normal, painted);
    }

    [Fact]
    public void A_palette_applied_changes_nothing_until_the_ui_thread_runs()
    {
        // Arrange
        SchemeFactory.Theme = ConsoleTheme.Dark;
        using var pane = new ReplPaneView(new TerminalGuiOptions(), new FakeUiDispatcher());

        // Act
        pane.ApplyTheme(ConsoleTheme.Light);

        // Assert
        Assert.Equal(ConsoleTheme.Dark, SchemeFactory.Theme);
    }

    [Fact]
    public void A_console_nobody_asked_a_palette_of_reads_the_one_in_force()
    {
        // Arrange
        SchemeFactory.Theme = ConsoleTheme.Light;
        using var pane = new ReplPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);
        var console = new TerminalGuiConsoleAdapter(pane);

        // Act
        var theme = console.Theme;

        // Assert
        Assert.Equal(ConsoleTheme.Light, theme);
    }

    [Fact]
    public void The_console_applies_the_palette_set_on_it()
    {
        // Arrange
        SchemeFactory.Theme = ConsoleTheme.Dark;
        using var pane = new ReplPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);
        var console = new TerminalGuiConsoleAdapter(pane);

        // Act
        console.SetTheme(ConsoleTheme.None);

        // Assert
        Assert.Equal(ConsoleTheme.None, SchemeFactory.Theme);
    }

    [Fact]
    public void A_start_with_a_setting_starts_in_that_palette()
    {
        // Arrange
        SchemeFactory.Theme = ConsoleTheme.Dark;

        // Act
        SchemeFactory.StartIn(ConsoleTheme.Light);

        // Assert
        Assert.Equal(ConsoleTheme.Light, SchemeFactory.Theme);
    }

    [Fact]
    public void A_start_without_a_setting_starts_as_the_environment_asks()
    {
        // Arrange
        var expected = SchemeFactory.ResolveTheme(null, Environment.GetEnvironmentVariable("NO_COLOR"));
        SchemeFactory.Theme = expected == ConsoleTheme.Light ? ConsoleTheme.Dark : ConsoleTheme.Light;

        // Act
        SchemeFactory.StartIn(null);

        // Assert
        Assert.Equal(expected, SchemeFactory.Theme);
    }
}
