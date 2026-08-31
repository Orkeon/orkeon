using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// What the assistant SAID is catalogue text and follows a language switch; what the user
/// TYPED is theirs and does not. The line between the two is the whole rule, and every
/// bubble the assistant produces has to sit on the right side of it.
/// </summary>
public sealed class ChatRetranslationTests
{
    private sealed class SwitchableStrings : Orkeon.Studio.Core.Localization.IStudioStrings
    {
        private string _prefix = "en:";

        public string this[string key] => _prefix + key;

        public event EventHandler? CultureChanged;

        public void Switch(string prefix)
        {
            _prefix = prefix;
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static ChatThreadViewModel Thread(SwitchableStrings strings)
    {
        var chat = new ChatThreadViewModel(strings);
        chat.Bind(
            facts: () => [], brief: () => "", briefChips: () => [],
            profileName: () => null, askEngine: _ => false);
        return chat;
    }

    [Fact]
    public void An_answer_from_the_local_bank_is_rewritten_by_a_language_switch()
    {
        var strings = new SwitchableStrings();
        var chat = Thread(strings);
        chat.OpenCommand.Execute(null);

        chat.Draft = "how much does this cost?";
        chat.SendCommand.Execute(null);

        var answer = chat.Turns.Last(t => t.IsBot);
        Assert.StartsWith("en:", answer.Body, StringComparison.Ordinal);

        strings.Switch("fr:");

        // The bank's answer came from the catalogue, so it moves with it — storing the
        // resolved sentence would leave the reply in the language it was asked in.
        Assert.StartsWith("fr:", answer.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_closing_bubble_is_rewritten_and_what_the_user_typed_is_not()
    {
        var strings = new SwitchableStrings();
        var chat = Thread(strings);

        chat.StartSession();
        chat.AddAssistantTurn("Where does this folder live?");
        chat.Draft = "un dossier";
        chat.SendCommand.Execute(null);
        chat.BriefAccepted();

        var closing = Assert.Single(chat.Turns, t => t.IsClosing);
        var typed = chat.Turns.First(t => !t.IsBot);
        Assert.StartsWith("en:", closing.Body, StringComparison.Ordinal);

        strings.Switch("fr:");

        Assert.StartsWith("fr:", closing.Body, StringComparison.Ordinal);
        Assert.Equal("un dossier", typed.Body);
    }
}
