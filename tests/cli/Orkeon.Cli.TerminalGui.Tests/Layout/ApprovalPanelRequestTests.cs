using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class ApprovalPanelRequestTests
{
    [Fact]
    public void The_options_answer_the_lines_the_channel_reads()
    {
        // Arrange
        var approval = new PendingToolApproval("run_command", "{}", null, null);

        // Act
        var request = ApprovalPanelRequest.For(approval, 1, GlyphSet.Ascii);

        // Assert
        Assert.Equal(["y", "s", "n"], request.Choices.Select(c => c.Value));
    }

    [Fact]
    public void Each_option_can_be_reached_by_the_letter_it_answers()
    {
        // Arrange
        var approval = new PendingToolApproval("run_command", "{}", null, null);

        // Act
        var request = ApprovalPanelRequest.For(approval, 1, GlyphSet.Ascii);

        // Assert
        Assert.All(request.Choices, c => Assert.Equal(c.Value, c.Shortcut));
    }

    [Fact]
    public void A_background_task_is_named_in_the_title()
    {
        // Arrange
        var approval = new PendingToolApproval("run_command", "{}", "t1", "dev-batches");

        // Act
        var request = ApprovalPanelRequest.For(approval, 1, GlyphSet.Ascii);

        // Assert
        Assert.Equal("run_command - dev-batches (t1)", request.Title);
    }

    [Fact]
    public void The_arguments_stand_as_the_detail_and_the_reason_as_the_description()
    {
        // Arrange
        var approval = new PendingToolApproval("run_command", "{\"c\":1}", null, null) { Reason = "not allow-listed" };

        // Act
        var request = ApprovalPanelRequest.For(approval, 1, GlyphSet.Ascii);

        // Assert
        Assert.Equal(("{\"c\":1}", "not allow-listed"), (request.Detail, request.Description));
    }

    [Fact]
    public void The_question_counts_the_others_waiting()
    {
        // Arrange
        var approval = new PendingToolApproval("run_command", "{}", null, null);

        // Act
        var request = ApprovalPanelRequest.For(approval, 3, GlyphSet.Ascii);

        // Assert
        Assert.Equal("Allow this call? (3 waiting)", request.Question);
    }
}
