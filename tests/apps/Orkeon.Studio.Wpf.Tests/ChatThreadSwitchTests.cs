using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The two threads the panel holds (UX lot 06, 077): a bank answer still due lands in the thread
/// it was asked in, a restart of the wizard leaves the Run thread on show alone, and the answers
/// a question lists go back to the engine with the agent keys it named.
/// </summary>
public sealed class ChatThreadSwitchTests
{
    private static ChatThreadViewModel Thread(ManualUiDelay delay, Func<string, bool>? askEngine = null, Func<string, string>? names = null)
    {
        var chat = new ChatThreadViewModel(strings: null, delay: delay);
        chat.Bind(() => [], () => "", () => [], () => null, askEngine ?? (_ => false), nameResolver: names);
        return chat;
    }

    private static void Answer(ChatThreadViewModel chat, string text)
    {
        chat.Draft = text;
        chat.SendCommand.Execute(null);
    }

    [Fact]
    public void ShouldLandTheBankAnswerInTheRunThread_WhenTheUserSwitchedToTheWizardMeanwhile()
    {
        var delay = new ManualUiDelay();
        var chat = Thread(delay);
        chat.SetContext(AssistantContext.WizardStep2);
        chat.AddAssistantTurn("Which folder?");
        chat.SetContext(AssistantContext.Run);
        Answer(chat, "how do I run a team?");
        var runTurns = chat.Turns.Count;

        chat.SetContext(AssistantContext.WizardStep2);
        delay.Elapse();

        Assert.Single(chat.Turns);
        Assert.Equal("Which folder?", chat.Turns[0].Body);
        Assert.False(chat.IsBusy);

        chat.SetContext(AssistantContext.Run);

        Assert.Equal(runTurns + 1, chat.Turns.Count);
        Assert.True(chat.Turns[^1].IsBot);
        Assert.False(chat.IsBusy);
    }

    [Fact]
    public void ShouldKeepTheRunThread_WhenTheWizardRestartsWhileRunIsOnShow()
    {
        var delay = new ManualUiDelay();
        var chat = Thread(delay);
        chat.StartSession();
        chat.AddAssistantTurn("Which folder?");
        chat.SetContext(AssistantContext.Run);
        Answer(chat, "how do I run a team?");
        delay.Elapse();
        var runTurns = chat.Turns.ToList();

        chat.Reset();

        Assert.Equal(runTurns, chat.Turns);
        Assert.False(chat.IsStarted);
        chat.SetContext(AssistantContext.WizardStep1);
        Assert.Empty(chat.Turns);
    }

    [Fact]
    public void ShouldSendTheAgentKeyBack_WhenAReplyNamesAnAgentShownByItsRole()
    {
        var asked = new List<string>();
        var chat = Thread(
            new ManualUiDelay(),
            askEngine: said => { asked.Add(said); return true; },
            names: text => text.Replace("veilleur-tri", "Veilleur", StringComparison.Ordinal));
        chat.StartSession();

        chat.AddAssistantTurn("Qui lit les sources ?\n- veilleur-tri\n- redacteur");

        var question = chat.Question!;
        Assert.Equal("Veilleur", question.Replies[0].Label);
        question.Replies[0].ReplyCommand.Execute(null);

        Assert.Equal(["veilleur-tri"], asked);
        Assert.Contains(chat.Turns, turn => turn.IsUser && turn.Body == "Veilleur");
    }
}
