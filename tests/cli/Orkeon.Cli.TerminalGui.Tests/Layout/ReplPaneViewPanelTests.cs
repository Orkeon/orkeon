using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Tests.Doubles;
using Orkeon.Cli.TerminalGui.Tests.Fixtures;
using Orkeon.Tests.Shared.Timing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class ReplPaneViewPanelTests
{
    private static ReplPaneView CreatePane() => new(new TerminalGuiOptions(), InlineDispatcher.Instance);

    private static ConsoleChoiceRequest TwoChoices(bool cancellable = true) => new()
    {
        Choices = [new("a", "A"), new("b", "B")],
        Cancellable = cancellable,
    };

    [Fact]
    public void An_open_panel_takes_the_place_of_the_prompt()
    {
        // Arrange
        using var pane = CreatePane();

        // Act
        _ = pane.ChooseAsync(TwoChoices(), CancellationToken.None);

        // Assert
        Assert.False(pane.Input.Visible);
    }

    [Fact]
    public async Task Enter_answers_the_option_selected()
    {
        // Arrange
        using var pane = CreatePane();
        var answer = pane.ChooseAsync(TwoChoices(), CancellationToken.None);
        pane.RaiseKeyDown(Key.CursorDown);

        // Act
        pane.RaiseKeyDown(Key.Enter);

        // Assert
        Assert.Equal("b", (await answer)!.Value);
    }

    [Fact]
    public async Task Esc_leaves_the_panel_without_an_answer()
    {
        // Arrange
        using var pane = CreatePane();
        var answer = pane.ChooseAsync(TwoChoices(), CancellationToken.None);

        // Act
        pane.RaiseKeyDown(Key.Esc);

        // Assert
        Assert.Null(await answer);
    }

    [Fact]
    public async Task A_cancelled_request_closes_its_panel()
    {
        // Arrange
        using var pane = CreatePane();
        using var cts = new CancellationTokenSource();
        var answer = pane.ChooseAsync(TwoChoices(), cts.Token);

        // Act
        await cts.CancelAsync();

        // Assert
        Assert.Null(await answer);
        Assert.Null(pane.OpenPanelModel);
    }

    [Fact]
    public async Task The_prompt_comes_back_with_its_draft_when_the_panel_closes()
    {
        // Arrange
        using var pane = CreatePane();
        pane.CurrentInput = "my draft";
        var answer = pane.ChooseAsync(TwoChoices(), CancellationToken.None);

        // Act
        pane.RaiseKeyDown(Key.Enter);
        await answer;

        // Assert
        Assert.Equal((true, "my draft"), (pane.Input.Visible, pane.CurrentInput));
    }

    [Fact]
    public void Esc_does_not_close_a_panel_that_cannot_be_left()
    {
        // Arrange
        using var pane = CreatePane();
        _ = pane.ChooseAsync(TwoChoices(cancellable: false), CancellationToken.None);

        // Act
        pane.RaiseKeyDown(Key.Esc);

        // Assert
        Assert.NotNull(pane.OpenPanelModel);
    }

    [Fact]
    public void Esc_Esc_interrupts_behind_a_panel_that_cannot_be_left()
    {
        // Arrange
        using var pane = CreatePane();
        _ = pane.ChooseAsync(TwoChoices(cancellable: false), CancellationToken.None);
        var interrupts = 0;
        pane.InterruptRequested += (_, _) => interrupts++;
        pane.RaiseKeyDown(Key.Esc);

        // Act
        pane.RaiseKeyDown(Key.Esc);

        // Assert
        Assert.Equal(1, interrupts);
    }

    [Fact]
    public void A_digit_moves_the_selection_and_does_not_answer()
    {
        // Arrange
        using var pane = CreatePane();
        _ = pane.ChooseAsync(TwoChoices(), CancellationToken.None);

        // Act
        pane.RaiseKeyDown(Key.D2);

        // Assert
        Assert.Equal(1, pane.OpenPanelModel!.Selected);
    }

    [Fact]
    public void The_newest_panel_has_the_keys()
    {
        // Arrange
        using var pane = CreatePane();
        _ = pane.ChooseAsync(TwoChoices(), CancellationToken.None);
        var newest = new ConsoleChoiceRequest { Choices = [new("c", "C")] };

        // Act
        _ = pane.ChooseAsync(newest, CancellationToken.None);

        // Assert
        Assert.Equal("c", pane.OpenPanelModel!.Answer.Value);
    }

    [Fact]
    public void A_question_opens_a_panel()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(1);

        // Act
        pane.RaiseApprovalsChanged();

        // Assert
        Assert.Equal("n", pane.OpenPanelModel!.Answer.Value);
    }

    [Fact]
    public void A_question_passes_before_a_panel_already_open()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        var choice = new ConsoleChoiceRequest { Choices = [new("c", "C")] };
        _ = pane.ChooseAsync(choice, CancellationToken.None);
        approvals.Ask(1);

        // Act
        pane.RaiseApprovalsChanged();

        // Assert
        Assert.Equal("n", pane.OpenPanelModel!.Answer.Value);
    }

    [Fact]
    public async Task The_panel_under_a_question_gets_the_keys_back_once_it_is_answered()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        var choice = pane.ChooseAsync(new ConsoleChoiceRequest { Choices = [new("c", "C")] }, CancellationToken.None);
        approvals.Ask(1);
        pane.RaiseApprovalsChanged();
        pane.RaiseKeyDown(Key.Enter);

        // Act
        pane.RaiseKeyDown(Key.Enter);

        // Assert
        Assert.Equal("c", (await choice)!.Value);
    }

    [Fact]
    public void Answering_a_question_settles_it_by_its_id()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(7);
        pane.RaiseApprovalsChanged();
        pane.RaiseKeyDown(new Key('s'));

        // Act
        pane.RaiseKeyDown(Key.Enter);

        // Assert
        Assert.Equal([(7L, "s")], approvals.Answers);
    }

    [Fact]
    public void Esc_on_a_question_denies_it()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(7);
        pane.RaiseApprovalsChanged();

        // Act
        pane.RaiseKeyDown(Key.Esc);

        // Assert
        Assert.Equal([(7L, "n")], approvals.Answers);
    }

    [Fact]
    public void Enter_straight_on_a_question_denies_it()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(7);
        pane.RaiseApprovalsChanged();

        // Act
        pane.RaiseKeyDown(Key.Enter);

        // Assert
        Assert.Equal([(7L, "n")], approvals.Answers);
    }

    [Fact]
    public async Task An_answer_on_a_panel_does_not_reach_a_read_open_on_the_prompt()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        var read = pane.ReadLineAsync(CancellationToken.None);
        approvals.Ask(1);
        pane.RaiseApprovalsChanged();
        pane.RaiseKeyDown(Key.Enter);
        pane.CurrentInput = "typed";

        // Act
        pane.RaiseKeyDown(Key.Enter);

        // Assert
        Assert.Equal("typed", await read);
    }

    [Fact]
    public async Task An_answer_on_a_panel_is_not_kept_for_a_later_read()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(1);
        pane.RaiseApprovalsChanged();
        pane.RaiseKeyDown(Key.Enter);
        var read = pane.ReadLineAsync(CancellationToken.None);

        // Act
        pane.CancelPendingRead();

        // Assert
        Assert.Null(await read);
    }

    [Fact]
    public void A_question_answered_does_not_open_again_before_the_channel_has_read_it()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(1);
        pane.RaiseApprovalsChanged();
        pane.RaiseKeyDown(Key.Enter);

        // Act
        pane.RaiseApprovalsChanged();

        // Assert
        Assert.Null(pane.OpenPanelModel);
    }

    [Fact]
    public void The_next_question_opens_its_own_panel()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(1);
        pane.RaiseApprovalsChanged();
        pane.RaiseKeyDown(Key.Enter);
        approvals.ReplaceQuestion(2);

        // Act
        pane.RaiseApprovalsChanged();

        // Assert
        Assert.NotNull(pane.OpenPanelModel);
    }

    [Fact]
    public void The_status_line_leaves_a_question_to_its_panel()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(1);

        // Act
        pane.RaiseApprovalsChanged();

        // Assert
        Assert.True(pane.StatusLine.QuestionOnPanel);
    }

    [Fact]
    public void The_status_line_asks_again_once_the_question_is_handed_to_the_prompt()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(1);
        pane.RaiseApprovalsChanged();

        // Act
        pane.RaiseKeyDown(new Key('/'));

        // Assert
        Assert.False(pane.StatusLine.QuestionOnPanel);
    }

    [Fact]
    public void A_question_withdrawn_closes_its_panel()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(1);
        pane.RaiseApprovalsChanged();
        approvals.WithdrawQuestions();

        // Act
        pane.RaiseApprovalsChanged();

        // Assert
        Assert.Null(pane.OpenPanelModel);
    }

    [Fact]
    public void A_slash_hands_the_question_to_the_prompt_with_the_slash_typed()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(1);
        pane.RaiseApprovalsChanged();

        // Act
        pane.RaiseKeyDown(new Key('/'));

        // Assert
        Assert.Equal((null, "/"), (pane.OpenPanelModel, pane.CurrentInput));
    }

    [Fact]
    public void A_question_handed_to_the_prompt_stays_there()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(1);
        pane.RaiseApprovalsChanged();
        pane.RaiseKeyDown(new Key('/'));

        // Act
        pane.RaiseApprovalsChanged();

        // Assert
        Assert.Null(pane.OpenPanelModel);
    }

    [Fact]
    public async Task Disposing_the_pane_ends_the_requests_still_open()
    {
        // Arrange
        using var pane = CreatePane();
        var answer = pane.ChooseAsync(TwoChoices(), CancellationToken.None);

        // Act
        pane.Dispose();

        // Assert
        Assert.Null(await answer);
    }

    [Fact]
    public void A_letter_no_option_carries_hands_the_question_to_the_prompt()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(1);
        pane.RaiseApprovalsChanged();

        // Act
        pane.RaiseKeyDown(new Key('l'));

        // Assert
        Assert.Equal((null, "l"), (pane.OpenPanelModel, pane.CurrentInput));
    }

    [Fact]
    public void The_letters_typed_before_the_one_no_option_carries_follow_to_the_prompt()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(1);
        pane.RaiseApprovalsChanged();
        pane.RaiseKeyDown(new Key('s'));

        // Act
        pane.RaiseKeyDown(new Key('e'));

        // Assert
        Assert.Equal((null, "se"), (pane.OpenPanelModel, pane.CurrentInput));
    }

    [Fact]
    public void A_word_finished_on_a_panel_does_not_answer_with_its_first_letter()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(1);
        pane.RaiseApprovalsChanged();
        pane.RaiseKeyDown(new Key('s'));
        pane.RaiseKeyDown(new Key('e'));

        // Act
        pane.RaiseKeyDown(Key.Enter);

        // Assert
        Assert.Empty(approvals.Answers);
    }

    [Fact]
    public void A_digit_no_option_carries_hands_the_question_to_the_prompt()
    {
        // Arrange
        using var approvals = new ApprovalPaneFixture();
        var pane = approvals.Pane;
        approvals.Ask(1);
        pane.RaiseApprovalsChanged();

        // Act
        pane.RaiseKeyDown(new Key('7'));

        // Assert
        Assert.Equal((null, "7"), (pane.OpenPanelModel, pane.CurrentInput));
    }

    [Fact]
    public void A_letter_no_option_carries_leaves_the_panel_of_a_script_open()
    {
        // Arrange
        using var pane = CreatePane();
        _ = pane.ChooseAsync(TwoChoices(), CancellationToken.None);

        // Act
        pane.RaiseKeyDown(new Key('z'));

        // Assert
        Assert.NotNull(pane.OpenPanelModel);
    }

    [Fact]
    public async Task A_cancelled_request_ends_though_the_loop_no_longer_runs()
    {
        // Arrange
        using var pane = new ReplPaneView(new TerminalGuiOptions(), new FakeUiDispatcher());
        using var cts = new CancellationTokenSource();
        var answer = pane.ChooseAsync(TwoChoices(), cts.Token);

        // Act
        await cts.CancelAsync();

        // Assert
        Assert.Null(await answer.WaitAsync(HangGuards.EnginePromise, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Stopping_the_reads_ends_the_requests_still_open()
    {
        // Arrange
        using var pane = new ReplPaneView(new TerminalGuiOptions(), new FakeUiDispatcher());
        var answer = pane.ChooseAsync(TwoChoices(), CancellationToken.None);

        // Act
        pane.CancelPendingRead();

        // Assert
        Assert.Null(await answer.WaitAsync(HangGuards.EnginePromise, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_request_made_after_the_pane_is_disposed_ends_at_once()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Dispose();

        // Act
        var answer = pane.ChooseAsync(TwoChoices(), CancellationToken.None);

        // Assert
        Assert.Null(await answer.WaitAsync(HangGuards.EnginePromise, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void An_Esc_before_a_panel_does_not_arm_its_interrupt()
    {
        // Arrange
        using var pane = CreatePane();
        var interrupts = 0;
        pane.InterruptRequested += (_, _) => interrupts++;
        pane.RaiseKeyDown(Key.Esc);
        _ = pane.ChooseAsync(TwoChoices(cancellable: false), CancellationToken.None);

        // Act
        pane.RaiseKeyDown(Key.Esc);

        // Assert
        Assert.Equal(0, interrupts);
    }
}
