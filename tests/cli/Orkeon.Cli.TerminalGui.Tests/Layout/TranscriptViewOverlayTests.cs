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
}
