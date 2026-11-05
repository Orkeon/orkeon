using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class InlinePanelModelTests
{
    private static ConsoleChoice[] Choices(int count)
        => [.. Enumerable.Range(1, count).Select(i => new ConsoleChoice($"v{i}", $"Option {i}"))];

    private static InlinePanelModel Model(ConsoleChoiceRequest request) => new(request, GlyphSet.Ascii);

    private static List<string> Texts(InlinePanelModel model, int width = 60, int maxRows = 40)
        => [.. model.Rows(width, maxRows).Select(RowSegment.Concat)];

    [Fact]
    public void A_label_has_the_whole_row_when_no_option_has_a_description()
    {
        // Arrange
        var label = new string('x', 55);
        var model = Model(new ConsoleChoiceRequest { Choices = [new("a", label)] });

        // Act
        var rows = Texts(model);

        // Assert
        Assert.Contains(rows, r => r.Contains(label, StringComparison.Ordinal));
    }

    [Fact]
    public void A_panel_without_options_is_refused()
    {
        // Arrange
        var request = new ConsoleChoiceRequest { Choices = [] };

        // Act
        var error = Record.Exception(() => Model(request));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    [Fact]
    public void It_opens_on_the_default_value_before_the_current_one()
    {
        // Arrange
        var request = new ConsoleChoiceRequest
        {
            Choices = [new("a", "A") { IsCurrent = true }, new("b", "B")],
            DefaultValue = "b",
        };

        // Act
        var model = Model(request);

        // Assert
        Assert.Equal(1, model.Selected);
    }

    [Fact]
    public void It_opens_on_the_current_value_without_a_default()
    {
        // Arrange
        var request = new ConsoleChoiceRequest
        {
            Choices = [new("a", "A"), new("b", "B") { IsCurrent = true }],
        };

        // Act
        var model = Model(request);

        // Assert
        Assert.Equal(1, model.Selected);
    }

    [Fact]
    public void Moving_up_from_the_first_option_wraps_to_the_last()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest { Choices = Choices(3) });

        // Act
        model.Move(-1);

        // Assert
        Assert.Equal(2, model.Selected);
    }

    [Fact]
    public void A_digit_selects_its_option_without_answering()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest { Choices = Choices(3) });

        // Act
        model.SelectNumber(2);

        // Assert
        Assert.Equal("v2", model.Answer.Value);
    }

    [Fact]
    public void A_digit_no_option_carries_changes_nothing()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest { Choices = Choices(3) });

        // Act
        model.SelectNumber(9);

        // Assert
        Assert.Equal(0, model.Selected);
    }

    [Fact]
    public void A_shortcut_selects_its_option_whatever_its_case()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest
        {
            Choices = [new("y", "Yes") { Shortcut = "y" }, new("n", "No") { Shortcut = "n" }],
        });

        // Act
        model.SelectShortcut("N");

        // Assert
        Assert.Equal("n", model.Answer.Value);
    }

    [Fact]
    public void The_answer_carries_the_value_the_control_was_walked_to()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest
        {
            Choices = Choices(1),
            Control = new ConsoleChoiceControl("effort", ["low", "high"], "high"),
        });

        // Act
        model.AdjustControl(1);

        // Assert
        Assert.Equal("low", model.Answer.ControlValue);
    }

    [Fact]
    public void The_selected_option_carries_the_cursor_and_its_hint()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest
        {
            Choices = [new("a", "A"), new("b", "B") { Hint = "the second" }],
            DefaultValue = "b",
        });

        // Act
        var rows = Texts(model);

        // Assert
        Assert.Contains("> 2. B - the second", rows);
    }

    [Fact]
    public void The_hint_of_an_option_not_selected_stays_hidden()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest
        {
            Choices = [new("a", "A"), new("b", "B") { Hint = "the second" }],
        });

        // Act
        var rows = Texts(model);

        // Assert
        Assert.Contains("  2. B", rows);
    }

    [Fact]
    public void The_current_value_is_marked_and_painted_as_accepted()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest
        {
            Choices = [new("a", "A"), new("b", "B") { IsCurrent = true }],
            DefaultValue = "a",
        });

        // Act
        var row = model.Rows(60, 40).Single(r => RowSegment.Concat(r).StartsWith("  2.", StringComparison.Ordinal));

        // Assert
        Assert.Equal(("B", PaletteRole.Ok), (row[1].Text, row[1].Role));
    }

    [Fact]
    public void Descriptions_start_on_the_same_column()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest
        {
            Choices = [new("a", "Short") { Description = "first" }, new("b", "Much longer") { Description = "second" }],
        });

        // Act
        var rows = Texts(model);

        // Assert
        var first = rows.Single(r => r.EndsWith("first", StringComparison.Ordinal));
        var second = rows.Single(r => r.EndsWith("second", StringComparison.Ordinal));
        Assert.Equal(first.IndexOf("first", StringComparison.Ordinal), second.IndexOf("second", StringComparison.Ordinal));
    }

    [Fact]
    public void A_long_list_shows_a_window_and_says_how_many_it_leaves_out()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest { Choices = Choices(13) });

        // Act
        var rows = Texts(model);

        // Assert
        Assert.Contains("  ... +3", rows);
    }

    [Fact]
    public void The_window_marks_that_more_options_follow()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest { Choices = Choices(13) });

        // Act
        var rows = Texts(model);

        // Assert
        Assert.Contains("v 10. Option 10", rows);
    }

    [Fact]
    public void The_window_follows_the_selection_to_the_end_of_the_list()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest { Choices = Choices(13) });
        model.Move(-1);

        // Act
        var rows = Texts(model);

        // Assert
        Assert.Contains("> 13. Option 13", rows);
    }

    [Fact]
    public void A_short_terminal_shrinks_the_list_before_dropping_the_heading()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest { Title = "Pick", Choices = Choices(13) });

        // Act
        var rows = Texts(model, maxRows: 12);

        // Assert
        Assert.Equal(12, rows.Count);
        Assert.Contains("Pick", rows);
    }

    [Fact]
    public void A_terminal_too_short_for_the_chrome_keeps_the_selected_option()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest { Title = "Pick", Choices = Choices(13), DefaultValue = "v7" });

        // Act
        var rows = Texts(model, maxRows: 3);

        // Assert
        Assert.Contains(">  7. Option 7", rows);
    }

    [Fact]
    public void A_detail_stands_behind_a_side_bar_and_wraps()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest { Detail = "abcdefgh", Choices = Choices(1) });

        // Act
        var rows = Texts(model, width: 6);

        // Assert
        Assert.Equal(2, rows.Count(r => r.StartsWith("| ", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_long_detail_is_cut_and_says_how_many_rows_it_leaves_out()
    {
        // Arrange
        var detail = string.Join('\n', Enumerable.Range(1, 9));
        var model = Model(new ConsoleChoiceRequest { Detail = detail, Choices = Choices(1) });

        // Act
        var rows = Texts(model);

        // Assert
        Assert.Contains("| ... +3", rows);
    }

    [Fact]
    public void A_panel_that_cannot_be_left_does_not_offer_Esc()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest { Choices = Choices(1), Cancellable = false });

        // Act
        var rows = Texts(model);

        // Assert
        Assert.Equal("Enter to confirm", rows[^1]);
    }

    [Fact]
    public void The_keys_of_the_request_replace_the_default_line()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest { Choices = Choices(1), Keys = "s for this session" });

        // Act
        var rows = Texts(model);

        // Assert
        Assert.Equal("s for this session", rows[^1]);
    }

    [Fact]
    public void The_control_shows_its_value_and_its_keys()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest
        {
            Choices = Choices(1),
            Control = new ConsoleChoiceControl("effort", ["low", "high"], "high"),
        });

        // Act
        var rows = Texts(model);

        // Assert
        Assert.Contains("* effort high  </> to adjust", rows);
    }

    [Fact]
    public void The_rule_fills_the_width()
    {
        // Arrange
        var model = Model(new ConsoleChoiceRequest { Choices = Choices(1) });

        // Act
        var rows = Texts(model, width: 12);

        // Assert
        Assert.Equal(new string('-', 12), rows[0]);
    }
}
