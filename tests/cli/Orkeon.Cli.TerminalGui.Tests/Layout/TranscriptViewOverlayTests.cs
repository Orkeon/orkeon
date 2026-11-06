using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

[Collection(TerminalGuiApplicationCollection.Name)]
public sealed class TranscriptViewOverlayTests
{
    private static TranscriptView ViewWith(params TranscriptLine[] prompts)
    {
        var view = new TranscriptView(1000) { Frame = new System.Drawing.Rectangle(0, 0, 40, 5) };
        view.Layout();
        foreach (var prompt in prompts)
        {
            view.AppendStyled([prompt]);
            view.AppendLines(Enumerable.Range(0, 20).Select(i => $"answer {i}"));
        }
        return view;
    }

    private static TranscriptLine Prompt(string text) => new(text) { RowRole = PaletteRole.UserBar };

    [Fact]
    public void At_the_tail_the_view_draws_no_way_back()
    {
        // Arrange
        using var view = ViewWith(Prompt("first"));

        // Act
        var chip = view.ChipText;

        // Assert
        Assert.Null(chip);
    }

    [Fact]
    public void Scrolled_up_the_view_offers_the_way_back()
    {
        // Arrange
        using var view = ViewWith(Prompt("first"));

        // Act
        view.Viewport = view.Viewport with { Y = 3 };

        // Assert
        Assert.Equal("↓ more below (click or ctrl+end)", view.ChipText);
    }

    [Fact]
    public void Coming_back_to_the_end_removes_the_way_back()
    {
        // Arrange
        using var view = ViewWith(Prompt("first"));
        view.Viewport = view.Viewport with { Y = 3 };

        // Act
        view.MoveEnd();

        // Assert
        Assert.Null(view.ChipText);
    }

    [Fact]
    public void Scrolled_past_a_prompt_the_view_recalls_it()
    {
        // Arrange
        using var view = ViewWith(Prompt("first"), Prompt("second"));

        // Act
        view.Viewport = view.Viewport with { Y = 30 };

        // Assert
        Assert.Equal("second", view.PromptHeader);
    }

    [Fact]
    public void A_prompt_still_on_screen_is_not_recalled()
    {
        // Arrange
        using var view = ViewWith(Prompt("first"));
        view.Viewport = view.Viewport with { Y = 3 };

        // Act
        view.Viewport = view.Viewport with { Y = 0 };

        // Assert
        Assert.Null(view.PromptHeader);
    }

    [Fact]
    public void Following_the_tail_the_view_recalls_no_prompt()
    {
        // Arrange
        using var view = ViewWith(Prompt("first"));

        // Act
        var header = view.PromptHeader;

        // Assert
        Assert.Null(header);
    }

    private static TranscriptView ScrolledUp(int width)
    {
        var view = new TranscriptView(1000) { Frame = new System.Drawing.Rectangle(0, 0, width, 5) };
        view.Layout();
        view.AppendLines(Enumerable.Range(0, 20).Select(i => $"answer {i}"));
        view.Viewport = view.Viewport with { Y = 3 };
        return view;
    }

    private static Terminal.Gui.Input.Mouse Press(int x, int y) => new()
    {
        Flags = Terminal.Gui.Input.MouseFlags.LeftButtonPressed,
        Position = new System.Drawing.Point(x, y),
        ScreenPosition = new System.Drawing.Point(x, 10 + y),
    };

    [Fact]
    public void A_click_right_of_the_way_back_stays_where_it_is()
    {
        // Arrange
        using var view = ScrolledUp(40);

        // Act
        view.NewMouseEvent(Press(view.Viewport.Width - 1, 4));

        // Assert
        Assert.NotNull(view.ChipText);
    }

    [Fact]
    public void A_click_on_the_way_back_returns_to_the_tail()
    {
        // Arrange
        using var view = ScrolledUp(40);

        // Act
        view.NewMouseEvent(Press(20, 4));

        // Assert
        Assert.Null(view.ChipText);
    }

    [Theory]
    [InlineData(20, 0)]
    [InlineData(20, 19)]
    [InlineData(40, 0)]
    public void A_click_where_the_way_back_is_not_drawn_stays_where_it_is(int width, int column)
    {
        // Arrange
        using var view = ScrolledUp(width);

        // Act
        view.NewMouseEvent(Press(column, 4));

        // Assert
        Assert.NotNull(view.ChipText);
    }
}
