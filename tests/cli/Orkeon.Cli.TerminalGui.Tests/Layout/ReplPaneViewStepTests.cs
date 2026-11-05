using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.Input;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class ReplPaneViewStepTests
{
    private static ReplPaneView CreatePane() => new(new TerminalGuiOptions(), InlineDispatcher.Instance);

    private static ToolCallStarted Read(long id, string? thread = null) => new(id, "file_read", thread) { Subject = "a.cs" };

    [Fact]
    public void A_call_that_begins_opens_the_step_area()
    {
        // Arrange
        using var pane = CreatePane();

        // Act
        pane.Started(Read(1));

        // Assert
        Assert.Equal(2, pane.StepRows);
    }

    [Fact]
    public void The_step_area_is_capped()
    {
        // Arrange
        using var pane = CreatePane();

        // Act
        for (var id = 1; id <= 9; id++)
            pane.Started(Read(id));

        // Assert
        Assert.Equal(5, pane.StepRows);
    }

    [Fact]
    public void A_call_of_another_thread_leaves_the_area_of_the_screen_shown()
    {
        // Arrange
        using var pane = CreatePane();

        // Act
        pane.Started(Read(1, "t1"));

        // Assert
        Assert.Equal(0, pane.StepRows);
    }

    [Fact]
    public void Text_after_the_calls_ended_folds_the_step_before_it()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Started(Read(1));
        pane.Finished(new ToolCallFinished(1, null, Succeeded: true));

        // Act
        pane.AppendOutput("next\n");

        // Assert
        Assert.Equal("  Read 1 file\nnext\n", pane.DisplayedText);
    }

    [Fact]
    public void Text_while_a_call_runs_leaves_the_step_open()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Started(Read(1));

        // Act
        pane.AppendOutput("next\n");

        // Assert
        Assert.Equal(2, pane.StepRows);
    }

    [Fact]
    public void A_folded_step_closes_the_area()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Started(Read(1));
        pane.Finished(new ToolCallFinished(1, null, Succeeded: true));

        // Act
        pane.AppendOutput("next\n");

        // Assert
        Assert.Equal(0, pane.StepRows);
    }

    [Fact]
    public void A_blank_write_does_not_end_a_step()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Started(Read(1));
        pane.Finished(new ToolCallFinished(1, null, Succeeded: true));

        // Act
        pane.AppendOutput("\n");

        // Assert
        Assert.Equal(2, pane.StepRows);
    }

    [Fact]
    public void The_end_of_a_turn_folds_the_step_then_runs_what_follows()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Started(Read(1));
        pane.Finished(new ToolCallFinished(1, null, Succeeded: true));
        var seen = "";

        // Act
        pane.AfterSteps(() => seen = pane.DisplayedText);

        // Assert
        Assert.Equal("  Read 1 file\n", seen);
    }

    [Fact]
    public void A_fold_reports_what_it_counted()
    {
        // Arrange
        using var pane = CreatePane();
        IReadOnlyDictionary<string, int>? reported = null;
        pane.StepFolded += counts => reported = counts;
        pane.Started(Read(1));
        pane.Finished(new ToolCallFinished(1, null, Succeeded: true));

        // Act
        pane.AfterSteps(() => { });

        // Assert
        Assert.Equal(1, reported!["file_read"]);
    }

    [Fact]
    public void Ctrl_o_on_an_empty_prompt_lists_the_calls_of_the_last_step()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Started(Read(1));
        pane.Finished(new ToolCallFinished(1, null, Succeeded: true));
        pane.AfterSteps(() => { });

        // Act
        pane.RaiseKeyDown(Key.O.WithCtrl);

        // Assert
        Assert.Contains("Read a.cs", pane.DisplayedText);
    }

    [Fact]
    public void Ctrl_o_with_text_in_the_prompt_stays_a_new_line()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Started(Read(1));
        pane.Finished(new ToolCallFinished(1, null, Succeeded: true));
        pane.AfterSteps(() => { });
        pane.CurrentInput = "draft";

        // Act
        pane.RaiseKeyDown(Key.O.WithCtrl);

        // Assert
        Assert.DoesNotContain("Read a.cs", pane.DisplayedText);
    }

    [Fact]
    public void A_cleared_screen_forgets_its_step()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Started(Read(1));

        // Act
        pane.Clear();

        // Assert
        Assert.Equal(0, pane.StepRows);
    }

    [Fact]
    public void A_panel_takes_the_room_of_the_step_area()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Started(Read(1));

        // Act
        _ = pane.ChooseAsync(new ConsoleChoiceRequest { Choices = [new("a", "A")] }, CancellationToken.None);

        // Assert
        Assert.Equal(0, pane.StepRows);
    }

    [Fact]
    public async Task The_step_area_comes_back_when_the_panel_closes()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Started(Read(1));
        var answer = pane.ChooseAsync(new ConsoleChoiceRequest { Choices = [new("a", "A")] }, CancellationToken.None);

        // Act
        pane.RaiseKeyDown(Key.Enter);
        await answer;

        // Assert
        Assert.Equal(2, pane.StepRows);
    }

    [Fact]
    public void A_change_shows_in_the_transcript_as_the_call_ends()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Started(new ToolCallStarted(1, "edit_file", null) { Subject = "a.cs" });
        var diff = new ToolCallDiff("a.cs", LineDiff.Compute("old\n", "new\n"));

        // Act
        pane.Finished(new ToolCallFinished(1, null, true) { Diff = diff });

        // Assert
        Assert.Contains("+ new", pane.DisplayedText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_does_not_end_the_step()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Started(new ToolCallStarted(1, "edit_file", null) { Subject = "a.cs" });
        var diff = new ToolCallDiff("a.cs", LineDiff.Compute("old\n", "new\n"));

        // Act
        pane.Finished(new ToolCallFinished(1, null, true) { Diff = diff });

        // Assert
        Assert.True(pane.StepRows > 0);
    }
}
