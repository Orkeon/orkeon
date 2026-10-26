using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Refonte R15: a question of the assistant's lives in the panel, its answers as buttons — the
/// recommended one first and focused — and never as a card of the main column. The engine sends
/// free text; the options are the bullets it lists under its question.
/// </summary>
public class ChatQuestionTests
{
    private const string FormatQuestion =
        "Merci. **Quelle forme pour le rapport ?**\n- Markdown, une page : le plus lisible\n- Plus court\n- Tableau (par défaut)";

    [Fact]
    public void ShouldReadTheListedOptions_RecommendedFirst_WhenTheBulletsFollowTheQuestion()
    {
        var options = ChatReplyReader.Read(FormatQuestion);

        Assert.Equal(["Tableau", "Markdown, une page", "Plus court"], options.Select(o => o.Label));
        Assert.True(options[0].IsRecommended);
        Assert.False(options[1].IsRecommended);
    }

    [Fact]
    public void ShouldReadNoOption_WhenTheBulletsAreNotAnswersToAQuestion()
    {
        // Criteria to confirm come before the question: they are not its answers.
        Assert.Empty(ChatReplyReader.Read(
            "Je retiens ces critères :\n- Le résumé cite ses sources\n- Une page au plus\nÇa vous va ?"));
        // One bullet too long to be an answer.
        Assert.Empty(ChatReplyReader.Read(
            "Lequel ?\n- Court\n- " + new string('x', 80)));
        // A single bullet is not a choice.
        Assert.Empty(ChatReplyReader.Read("Lequel ?\n- Court"));
        Assert.Empty(ChatReplyReader.Read("Quel format voulez-vous ?"));
    }

    [Fact]
    public void ShouldOfferTheOptionsAndSkip_InThePanel_WhenTheEngineAsks()
    {
        var chat = new ChatThreadViewModel();
        chat.StartSession();
        chat.CloseCommand.Execute(null);
        var focused = 0;
        chat.FocusRepliesRequested += (_, _) => focused++;

        chat.AddAssistantTurn(FormatQuestion);

        var question = chat.Question!;
        Assert.Null(question.Text);
        Assert.True(question.OffersChoices);
        Assert.Equal(["Tableau", "Markdown, une page", "Plus court", "I don't know"], question.Replies.Select(r => r.Label));
        // The panel opens itself, the main column no longer carries the question, and the
        // recommended answer takes the focus.
        Assert.True(chat.IsOpen);
        Assert.False(chat.IsLive);
        Assert.Equal(1, focused);
        Assert.Same(question, chat.Question);

        question.Replies[1].ReplyCommand.Execute(null);

        Assert.Contains(chat.Turns, turn => turn.IsUser && turn.Body == "Markdown, une page");
    }

    [Fact]
    public void ShouldAnswerInTheComposer_WhenTheQuestionListsNoOption()
    {
        var chat = new ChatThreadViewModel();
        chat.StartSession();
        var replies = 0;
        var input = 0;
        chat.FocusRepliesRequested += (_, _) => replies++;
        chat.FocusInputRequested += (_, _) => input++;

        chat.AddAssistantTurn("Quel format voulez-vous pour le rapport ?");

        var question = chat.Question!;
        Assert.False(question.OffersChoices);
        Assert.Same(chat.SkipCommand, Assert.Single(question.Replies).Command);
        Assert.Equal(0, replies);

        chat.RequestAnswerFocus();
        Assert.Equal(1, input);
        Assert.Equal(0, replies);
    }

    [Fact]
    public void ShouldWinOverTheEngine_AndRewordWithoutReopening_WhenTheOwnerAsks()
    {
        var chat = new ChatThreadViewModel();
        var yes = new ViewModels.Mvvm.RelayCommand(() => { });
        var asked = new ChatQuestion("Des fichiers ?", "", [new ChatReply("Oui", yes, isRecommended: true)]);

        chat.SetQuestion(asked);
        Assert.Same(asked, chat.Question);
        Assert.True(chat.IsOpen);

        chat.CloseCommand.Execute(null);
        chat.SetQuestion(asked with { Text = "Files?" }, announce: false);
        Assert.False(chat.IsOpen);

        chat.SetQuestion(null);
        Assert.Null(chat.Question);
        Assert.False(chat.HasQuestion);
    }
}
