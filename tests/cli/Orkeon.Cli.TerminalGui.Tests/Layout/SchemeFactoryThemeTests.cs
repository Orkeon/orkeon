using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.Drawing;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class SchemeFactoryThemeTests
{
    public static TheoryData<ConsoleTheme?, string?, ConsoleTheme> StartCases => new()
    {
        { null, null, ConsoleTheme.Dark },
        { null, "", ConsoleTheme.Dark },
        { null, "1", ConsoleTheme.None },
        { ConsoleTheme.Light, "1", ConsoleTheme.Light },
        { ConsoleTheme.None, null, ConsoleTheme.None },
    };

    [Theory]
    [MemberData(nameof(StartCases))]
    public void A_setting_wins_over_NO_COLOR_and_the_dark_palette_is_the_default(ConsoleTheme? setting, string? noColor, ConsoleTheme expected)
    {
        // Act
        var theme = SchemeFactory.ResolveTheme(setting, noColor);

        // Assert
        Assert.Equal(expected, theme);
    }

    [Theory]
    [InlineData(PaletteRole.Dim)]
    [InlineData(PaletteRole.Accent)]
    [InlineData(PaletteRole.UserBar)]
    [InlineData(PaletteRole.AddedWord)]
    public void No_palette_leaves_every_role_in_the_terminal_colours(PaletteRole role)
    {
        // Act
        var colours = (SchemeFactory.Foreground(role, true, ConsoleTheme.None), SchemeFactory.Background(role, true, ConsoleTheme.None));

        // Assert
        Assert.Equal((Color.None, Color.None), colours);
    }

    [Fact]
    public void No_palette_keeps_the_emphasis()
    {
        // Act
        var attribute = SchemeFactory.Attr(PaletteRole.Accent, true, ConsoleTheme.None, bold: true, italic: false);

        // Assert
        Assert.Equal(TextStyle.Bold, attribute.Style);
    }

    [Theory]
    [InlineData(PaletteRole.UserBar, true)]
    [InlineData(PaletteRole.DiffAdded, true)]
    [InlineData(PaletteRole.DiffRemoved, true)]
    [InlineData(PaletteRole.AddedWord, true)]
    [InlineData(PaletteRole.RemovedWord, true)]
    [InlineData(PaletteRole.UserBar, false)]
    [InlineData(PaletteRole.DiffAdded, false)]
    [InlineData(PaletteRole.DiffRemoved, false)]
    [InlineData(PaletteRole.AddedWord, false)]
    [InlineData(PaletteRole.RemovedWord, false)]
    public void The_light_palette_paints_a_painted_role_with_a_foreground_of_its_own(PaletteRole role, bool trueColor)
    {
        // Act
        var (fg, bg) = (SchemeFactory.Foreground(role, trueColor, ConsoleTheme.Light), SchemeFactory.Background(role, trueColor, ConsoleTheme.Light));

        // Assert
        Assert.NotEqual(bg, fg);
        Assert.NotEqual(Color.None, bg);
    }

    [Fact]
    public void The_light_palette_lays_a_dark_text_on_a_light_added_line()
    {
        // Act
        var bg = SchemeFactory.Background(PaletteRole.DiffAdded, true, ConsoleTheme.Light);

        // Assert
        Assert.Equal(new Color(0xd8, 0xf5, 0xdd), bg);
    }

    [Fact]
    public void Without_a_palette_named_the_colours_are_the_dark_ones()
    {
        // Act
        var bg = SchemeFactory.Background(PaletteRole.DiffAdded, true);

        // Assert
        Assert.Equal(SchemeFactory.Background(PaletteRole.DiffAdded, true, ConsoleTheme.Dark), bg);
    }

    [Fact]
    public void A_painted_row_in_no_palette_paints_nothing_under_a_plain_char()
    {
        // Act
        var cell = SchemeFactory.Cell(PaletteRole.DiffAdded, PaletteRole.Normal, true, ConsoleTheme.None, false, false);

        // Assert
        Assert.Equal((Color.None, Color.None), (cell.Foreground, cell.Background));
    }
}
