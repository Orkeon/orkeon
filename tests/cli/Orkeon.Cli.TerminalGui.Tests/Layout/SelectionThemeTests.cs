using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.Drawing;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

/// <summary>The selection follows the palette in use: <see cref="SchemeFactory.Theme"/> is static, hence the collection.</summary>
[Collection(TerminalGuiApplicationCollection.Name)]
public sealed class SelectionThemeTests : IDisposable
{
    private readonly ConsoleTheme _theme = SchemeFactory.Theme;

    public void Dispose() => SchemeFactory.Theme = _theme;

    [Fact]
    public void In_the_light_palette_a_selection_stands_out_from_a_white_terminal()
    {
        // Arrange
        SchemeFactory.Theme = ConsoleTheme.Light;

        // Act
        var selection = MouseClipboardTextView.SelectionAttribute;

        // Assert
        Assert.Equal(new Color(ColorName16.Black), selection.Background);
    }

    [Fact]
    public void In_the_dark_palette_a_selection_stands_out_from_a_black_terminal()
    {
        // Arrange
        SchemeFactory.Theme = ConsoleTheme.Dark;

        // Act
        var selection = MouseClipboardTextView.SelectionAttribute;

        // Assert
        Assert.Equal(new Color(ColorName16.White), selection.Background);
    }
}
