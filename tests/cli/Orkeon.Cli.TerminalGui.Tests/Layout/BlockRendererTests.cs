using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public class BlockRendererTests
{
    [Fact]
    public void A_final_line_break_opens_no_line()
    {
        // Act
        var lines = BlockRenderer.Render(ConsoleBlockKind.Markdown, "a\nb\n");

        // Assert
        Assert.Equal(["a", "b"], lines.Select(l => l.Text));
    }

    [Theory]
    [InlineData("+added", PaletteRole.DiffAdded)]
    [InlineData("-removed", PaletteRole.DiffRemoved)]
    public void A_changed_diff_line_is_painted_over_its_whole_row(string line, PaletteRole role)
    {
        // Act
        var lines = BlockRenderer.Render(ConsoleBlockKind.Diff, line);

        // Assert
        Assert.Equal(role, lines[0].RowRole);
    }

    [Theory]
    [InlineData("+++ b/file.cs")]
    [InlineData("--- a/file.cs")]
    [InlineData("diff --git a/file.cs b/file.cs")]
    public void A_diff_header_is_dim_and_not_read_as_a_change(string line)
    {
        // Act
        var lines = BlockRenderer.Render(ConsoleBlockKind.Diff, line);

        // Assert
        Assert.Null(lines[0].RowRole);
        Assert.Equal(PaletteRole.Dim, lines[0].Runs[0].Role);
    }

    [Fact]
    public void Markdown_keeps_its_source_as_it_is_for_now()
    {
        // Act
        var lines = BlockRenderer.Render(ConsoleBlockKind.Markdown, "+ item");

        // Assert
        Assert.Null(lines[0].RowRole);
        Assert.Empty(lines[0].Runs);
    }
}
