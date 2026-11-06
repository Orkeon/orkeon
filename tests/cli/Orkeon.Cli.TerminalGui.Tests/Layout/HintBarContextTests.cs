using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class HintBarContextTests
{
    [Theory]
    [InlineData(10, PaletteRole.Dim)]
    [InlineData(79, PaletteRole.Dim)]
    [InlineData(80, PaletteRole.Alert)]
    [InlineData(95, PaletteRole.Error)]
    public void The_context_is_dim_until_it_is_worth_a_look(int percent, PaletteRole role)
    {
        // Act
        var segments = HintBarModel.Segments(GlyphSet.Unicode, "default", false, false, contextPercent: percent);

        // Assert
        Assert.Equal(role, Assert.Single(segments, s => s.Text == $"context {percent}%").Role);
    }

    [Fact]
    public void An_unknown_context_takes_no_room()
    {
        // Act
        var segments = HintBarModel.Segments(GlyphSet.Unicode, "default", false, false);

        // Assert
        Assert.DoesNotContain("context", RowSegment.Concat(segments), StringComparison.Ordinal);
    }

    [Fact]
    public void The_tasks_list_keeps_the_bar_to_its_own_keys()
    {
        // Act
        var segments = HintBarModel.Segments(GlyphSet.Unicode, "default", false, true, taskListFocused: true, contextPercent: 50);

        // Assert
        Assert.DoesNotContain("context", RowSegment.Concat(segments), StringComparison.Ordinal);
    }

    [Fact]
    public void At_sixty_columns_the_context_outranks_the_cycle_hint()
    {
        // Act
        var bar = RowSegment.Concat(HintBarModel.Segments(GlyphSet.Unicode, "bypassPermissions", false, false, contextPercent: 97));

        // Assert
        Assert.Contains("context 97%", bar[..60], StringComparison.Ordinal);
    }

    [Fact]
    public void With_room_the_context_leaves_the_cycle_hint_in_the_bar()
    {
        // Act
        var bar = RowSegment.Concat(HintBarModel.Segments(GlyphSet.Unicode, "plan", false, false, contextPercent: 10));

        // Assert
        Assert.Equal("  ⏵⏵ plan mode on · context 10% · (shift+tab to cycle) · ctrl+g logs", bar);
    }
}
