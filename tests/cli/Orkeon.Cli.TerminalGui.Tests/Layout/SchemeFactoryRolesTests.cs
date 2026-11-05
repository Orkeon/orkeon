using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.Drawing;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public class SchemeFactoryRolesTests
{
    [Theory]
    [InlineData(PaletteRole.Normal)]
    [InlineData(PaletteRole.Dim)]
    [InlineData(PaletteRole.Accent)]
    [InlineData(PaletteRole.Ok)]
    [InlineData(PaletteRole.Alert)]
    [InlineData(PaletteRole.Error)]
    [InlineData(PaletteRole.Code)]
    [InlineData(PaletteRole.FileRef)]
    [InlineData(PaletteRole.PanelRule)]
    public void A_layout_role_keeps_the_background_of_the_terminal(PaletteRole role)
    {
        // Act
        var attribute = SchemeFactory.Attr(role, trueColor: true, bold: false, italic: false);

        // Assert
        Assert.False(SchemeFactory.HasBackground(role));
        Assert.Equal(Color.None, attribute.Background);
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
    public void A_content_role_sets_both_colours_so_a_light_terminal_reads_it_too(PaletteRole role, bool trueColor)
    {
        // Act
        var attribute = SchemeFactory.Attr(role, trueColor, bold: false, italic: false);

        // Assert
        Assert.True(SchemeFactory.HasBackground(role));
        Assert.NotEqual(Color.None, attribute.Background);
        Assert.NotEqual(Color.None, attribute.Foreground);
    }

    [Fact]
    public void Plain_text_on_a_painted_row_takes_both_colours_of_the_row()
    {
        // Act
        var cell = SchemeFactory.Cell(PaletteRole.UserBar, PaletteRole.Normal, trueColor: true, bold: false, italic: false);

        // Assert
        Assert.Equal(SchemeFactory.Attr(PaletteRole.UserBar, true, false, false), cell);
    }

    [Fact]
    public void A_dim_run_on_a_painted_row_keeps_its_colour_over_the_background_of_the_row()
    {
        // Act
        var cell = SchemeFactory.Cell(PaletteRole.DiffAdded, PaletteRole.Dim, trueColor: true, bold: false, italic: false);

        // Assert
        Assert.Equal(SchemeFactory.Foreground(PaletteRole.Dim, true), cell.Foreground);
        Assert.Equal(SchemeFactory.Background(PaletteRole.DiffAdded, true), cell.Background);
    }

    [Fact]
    public void A_changed_word_on_a_painted_row_keeps_its_own_background()
    {
        // Act
        var cell = SchemeFactory.Cell(PaletteRole.DiffAdded, PaletteRole.AddedWord, trueColor: true, bold: false, italic: false);

        // Assert
        Assert.Equal(SchemeFactory.Background(PaletteRole.AddedWord, true), cell.Background);
    }

    [Fact]
    public void A_run_on_an_unpainted_row_stays_on_the_background_of_the_terminal()
    {
        // Act
        var cell = SchemeFactory.Cell(null, PaletteRole.Code, trueColor: true, bold: true, italic: false);

        // Assert
        Assert.Equal(Color.None, cell.Background);
        Assert.True(cell.Style.HasFlag(TextStyle.Bold));
    }
}
