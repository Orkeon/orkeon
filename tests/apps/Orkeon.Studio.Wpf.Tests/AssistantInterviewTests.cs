using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The three questions « Composer l'équipe » asks before the engine (30/08 mock, T-02).
/// <para>
/// This suite exists because of a defect it would have caught on day one. The interview
/// builds its resource keys by interpolation — eleven per question, one shape — and the
/// catalogue rename walked straight past them: a text sweep cannot see a key that is
/// assembled. Every question then resolved to a MISS, and a miss returns the key, so the
/// assistant asked «Vm_Chat_Q1_Body» three times and nothing threw, nothing failed, and
/// every existing test still passed because they all asserted on structure, never on words.
/// </para>
/// <para>
/// So these tests read the words back. A question whose text is its own key is the failure,
/// and it is the one that has to be impossible to ship twice.
/// </para>
/// </summary>
public sealed class AssistantInterviewTests
{
    private static IReadOnlyList<AssistantQuestion> Interview() =>
        AssistantInterview.Build(EnglishStudioStrings.Instance);

    [Fact]
    public void The_interview_asks_exactly_three_questions()
    {
        Assert.Equal(3, Interview().Count);
    }

    [Fact]
    public void No_question_shows_a_resource_key_where_its_words_should_be()
    {
        foreach (var (question, index) in Interview().Select((q, i) => (q, i)))
        {
            foreach (var (part, text) in new[]
            {
                ("Fact", question.Fact), ("Body", question.Body),
                ("Detail", question.Detail), ("Placeholder", question.Placeholder),
            })
            {
                Assert.False(string.IsNullOrWhiteSpace(text), $"Q{index + 1} {part} is empty");
                Assert.DoesNotContain("Studio.", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Vm_", text, StringComparison.Ordinal);
            }

            // The hint is genuinely optional — Q2 has none in the mock — but an ABSENT hint
            // must read as no line at all, never as the identifier that was not found.
            Assert.DoesNotContain("Studio.", question.Hint, StringComparison.Ordinal);
            Assert.DoesNotContain("Vm_", question.Hint, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_quick_reply_carries_words_on_the_chip_and_words_in_what_it_records()
    {
        foreach (var (question, index) in Interview().Select((q, i) => (q, i)))
        {
            Assert.Equal(3, question.Chips.Count);
            foreach (var chip in question.Chips)
            {
                Assert.False(string.IsNullOrWhiteSpace(chip.Label), $"Q{index + 1}: a chip has no label");
                Assert.False(string.IsNullOrWhiteSpace(chip.Value), $"Q{index + 1}: a chip records nothing");
                Assert.DoesNotContain("Studio.", chip.Label, StringComparison.Ordinal);
                Assert.DoesNotContain("Studio.", chip.Value, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_questions_are_the_three_the_mock_asks_and_they_are_distinct()
    {
        var interview = Interview();

        Assert.Contains("folder", interview[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("when", interview[1].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never", interview[2].Body, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(3, interview.Select(q => q.Body).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, interview.Select(q => q.Fact).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_question_bubble_shows_the_question_and_not_the_key_it_came_from()
    {
        // The bubble stores the question's INDEX rather than its sentence, so a language
        // switch rewrites it — which means the bubble resolves the key on every read, and
        // is a second place the same defect could live.
        var chat = new ChatThreadViewModel();
        chat.Bind(
            facts: () => [], brief: () => "", briefChips: () => [],
            profileName: () => null, askEngine: _ => false, onInterviewComplete: _ => { });

        chat.StartInterview();

        var bubble = Assert.Single(chat.Turns);
        Assert.True(bubble.IsBot);
        Assert.Equal(Interview()[0].Body, bubble.Body);
        Assert.Equal(Interview()[0].Detail, bubble.Detail);
        Assert.DoesNotContain("Studio.", bubble.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_with_no_hint_shows_no_hint_line_at_all()
    {
        var chat = new ChatThreadViewModel();
        chat.Bind(
            facts: () => [], brief: () => "", briefChips: () => [],
            profileName: () => null, askEngine: _ => false, onInterviewComplete: _ => { });

        chat.StartInterview();
        chat.Draft = "Documents";
        chat.SendCommand.Execute(null);

        // Q2 carries no hint in the mock; the bubble must fall silent, not print a key.
        var second = chat.Turns.Last(t => t.IsBot);
        Assert.False(second.HasHint);
        Assert.Equal("", second.Hint);
    }
}

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
            profileName: () => null, askEngine: _ => false, onInterviewComplete: _ => { });
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

        chat.StartInterview();
        foreach (var said in new[] { "un dossier", "vendredi", "rien" })
        {
            chat.Draft = said;
            chat.SendCommand.Execute(null);
        }

        var closing = Assert.Single(chat.Turns, t => t.IsClosing);
        var typed = chat.Turns.First(t => !t.IsBot);
        Assert.StartsWith("en:", closing.Body, StringComparison.Ordinal);

        strings.Switch("fr:");

        Assert.StartsWith("fr:", closing.Body, StringComparison.Ordinal);
        Assert.Equal("un dossier", typed.Body);
    }
}
