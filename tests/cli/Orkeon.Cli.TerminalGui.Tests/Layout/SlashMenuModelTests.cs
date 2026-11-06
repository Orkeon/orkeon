using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class SlashMenuModelTests
{
    private static readonly ReplCommandEntry[] Catalog =
    [
        new("review", "Reviews the diff"),
        new("model", "Picks a model") { Aliases = ["m"] },
        new("remodel", "Starts over"),
        new("mode", "Picks a mode"),
    ];

    private static SlashMenuModel Filtered(string query = "", IReadOnlyList<ReplCommandEntry>? catalog = null)
    {
        var model = new SlashMenuModel(catalog ?? Catalog, GlyphSet.Ascii);
        model.Filter(query);
        return model;
    }

    private static List<string> Texts(SlashMenuModel model, int width = 60, int maxRows = 20)
        => [.. model.Rows(width, maxRows).Select(RowSegment.Concat)];

    private static ReplCommandEntry[] Many(int count)
        => [.. Enumerable.Range(0, count).Select(i => new ReplCommandEntry($"c{i:D2}", $"Command {i}"))];

    public static TheoryData<string, string?> Queries => new()
    {
        { "/", "" },
        { "/mo", "mo" },
        { "hello", null },
        { "", null },
        { "/model gpt", null },
        { "/a\nb", null },
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public void The_query_is_the_word_after_a_leading_slash(string text, string? expected)
    {
        // Arrange
        // (the text is the case)

        // Act
        var query = SlashMenuModel.QueryOf(text);

        // Assert
        Assert.Equal(expected, query);
    }

    [Fact]
    public void An_empty_query_keeps_every_command_by_name()
    {
        // Arrange
        var model = new SlashMenuModel(Catalog, GlyphSet.Ascii);

        // Act
        model.Filter("");

        // Assert
        Assert.Equal(["/mode", "/model", "/remodel", "/review"], Texts(model).Select(r => r[2..].Split(' ')[0]));
    }

    [Fact]
    public void A_name_that_starts_with_the_query_passes_before_one_that_holds_it()
    {
        // Arrange
        var model = new SlashMenuModel(Catalog, GlyphSet.Ascii);

        // Act
        model.Filter("mod");

        // Assert
        Assert.Equal(["/mode", "/model", "/remodel"], Texts(model).Select(r => r[2..].Split(' ')[0]));
    }

    [Fact]
    public void An_alias_finds_its_command()
    {
        // Arrange
        var catalog = new[] { new ReplCommandEntry("exit", "Leaves") { Aliases = ["quit"] } };
        var model = new SlashMenuModel(catalog, GlyphSet.Ascii);

        // Act
        model.Filter("qu");

        // Assert
        Assert.Equal("exit", model.SelectedName);
    }

    [Fact]
    public void A_query_nothing_answers_keeps_no_command()
    {
        // Arrange
        var model = new SlashMenuModel(Catalog, GlyphSet.Ascii);

        // Act
        model.Filter("zzz");

        // Assert
        Assert.Equal((0, null), (model.MatchCount, model.SelectedName));
    }

    [Fact]
    public void A_new_query_selects_the_best_match_again()
    {
        // Arrange
        var model = Filtered();
        model.Move(2);

        // Act
        model.Filter("rev");

        // Assert
        Assert.Equal("review", model.SelectedName);
    }

    [Fact]
    public void Moving_past_the_last_command_comes_back_to_the_first()
    {
        // Arrange
        var model = Filtered("mod");

        // Act
        model.Move(3);

        // Assert
        Assert.Equal("mode", model.SelectedName);
    }

    [Fact]
    public void Moving_with_no_command_kept_does_nothing()
    {
        // Arrange
        var model = Filtered("zzz");

        // Act
        model.Move(1);

        // Assert
        Assert.Equal(0, model.Selected);
    }

    [Fact]
    public void The_row_selected_carries_the_cursor()
    {
        // Arrange
        var model = Filtered("rev");

        // Act
        var rows = Texts(model);

        // Assert
        Assert.StartsWith($"{GlyphSet.Ascii.Prompt} /review", rows[0], StringComparison.Ordinal);
    }

    [Fact]
    public void An_alias_shows_beside_the_name()
    {
        // Arrange
        var model = Filtered("model");

        // Act
        var rows = Texts(model);

        // Assert
        Assert.Contains("/model (m)", rows[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Descriptions_start_on_one_column()
    {
        // Arrange
        var model = Filtered("mod");

        // Act
        var rows = Texts(model);

        // Assert
        Assert.Equal(rows[0].IndexOf("Picks a mode", StringComparison.Ordinal), rows[1].IndexOf("Picks a model", StringComparison.Ordinal));
    }

    [Fact]
    public void A_long_description_stays_on_its_row()
    {
        // Arrange
        var model = Filtered(catalog: [new("c", new string('x', 200))]);

        // Act
        var rows = Texts(model, width: 40);

        // Assert
        Assert.Equal(40, TerminalText.Columns(rows[0]));
    }

    [Fact]
    public void Only_the_first_line_of_a_description_is_shown()
    {
        // Arrange
        var model = Filtered(catalog: [new("c", "first\nsecond")]);

        // Act
        var rows = Texts(model);

        // Assert
        Assert.DoesNotContain("second", rows[0], StringComparison.Ordinal);
    }

    [Fact]
    public void The_menu_shows_at_most_eight_commands()
    {
        // Arrange
        var model = Filtered(catalog: Many(30));

        // Act
        var rows = model.Rows(60, 20);

        // Assert
        Assert.Equal(8, rows.Count);
    }

    [Fact]
    public void The_menu_shrinks_to_the_rows_it_is_given()
    {
        // Arrange
        var model = Filtered(catalog: Many(30));

        // Act
        var rows = model.Rows(60, 3);

        // Assert
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void The_window_follows_the_selection()
    {
        // Arrange
        var model = Filtered(catalog: Many(30));
        model.Move(-1);

        // Act
        var rows = Texts(model);

        // Assert
        Assert.Contains("/c29", rows[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_mark_says_more_commands_follow()
    {
        // Arrange
        var model = Filtered(catalog: Many(30));

        // Act
        var rows = Texts(model);

        // Assert
        Assert.StartsWith(GlyphSet.Ascii.Down, rows[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_mark_says_more_commands_precede()
    {
        // Arrange
        var model = Filtered(catalog: Many(30));
        model.Move(15);

        // Act
        var rows = Texts(model);

        // Assert
        Assert.StartsWith(GlyphSet.Ascii.Up, rows[0], StringComparison.Ordinal);
    }

    [Fact]
    public void No_room_paints_no_row()
    {
        // Arrange
        var model = Filtered();

        // Act
        var rows = model.Rows(0, 5);

        // Assert
        Assert.Empty(rows);
    }
}
