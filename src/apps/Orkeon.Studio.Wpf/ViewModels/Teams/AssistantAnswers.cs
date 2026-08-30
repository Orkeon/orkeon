using System.Text.RegularExpressions;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>Where the thread is mounted — it decides what a question with no keyword gets back.</summary>
public enum AssistantContext
{
    /// <summary>The wizard, step 1 — describing the work.</summary>
    WizardStep1,

    /// <summary>The wizard, step 2 — the proposed composition.</summary>
    WizardStep2,

    /// <summary>The wizard, step 3 — the trial.</summary>
    WizardStep3,

    /// <summary>The wizard, step 4 — adoption.</summary>
    WizardStep4,

    /// <summary>The Run screen, following a live execution.</summary>
    Run,

    /// <summary>The History screen, looking back at one.</summary>
    History,
}

/// <summary>
/// The local half of the assistant's answers (30/08 mock, T-06). A free question goes to
/// the engine whenever a forge session is listening — that is the real answer and it wins.
/// This is what answers when nobody is: before the composition starts, and on Run and
/// History, where there is no session at all and the thread would otherwise be mute.
/// <para>
/// Six keyword rules, then a fallback anchored to where the user stands. The patterns are
/// catalogued per culture, because the words someone types to ask about cost or folders
/// are not the same in French and in Chinese.
/// </para>
/// </summary>
public sealed class AssistantAnswers(IStudioStrings strings)
{
    private static readonly (string Rule, string Answer)[] Bank =
    [
        (StudioStringKeys.ChatRuleFolders,  StudioStringKeys.ChatAnswerFolders),
        (StudioStringKeys.ChatRuleCost,     StudioStringKeys.ChatAnswerCost),
        (StudioStringKeys.ChatRuleDuration, StudioStringKeys.ChatAnswerDuration),
        (StudioStringKeys.ChatRuleError,    StudioStringKeys.ChatAnswerError),
        (StudioStringKeys.ChatRuleSchedule, StudioStringKeys.ChatAnswerSchedule),
        (StudioStringKeys.ChatRulePrivacy,  StudioStringKeys.ChatAnswerPrivacy),
    ];

    private readonly IStudioStrings _strings = strings;

    /// <summary>The first rule the question matches, else the answer for where the user stands.</summary>
    public string Answer(string question, AssistantContext context)
    {
        var asked = (question ?? "").Trim();

        foreach (var (rule, answer) in Bank)
        {
            if (Matches(asked, _strings[rule]))
                return _strings[answer];
        }

        return _strings[Fallback(context)];
    }

    /// <summary>The primer a thread with nothing in it shows, per screen.</summary>
    public string Primer(AssistantContext context) => _strings[context switch
    {
        AssistantContext.Run => StudioStringKeys.ChatEmptyRun,
        AssistantContext.History => StudioStringKeys.ChatEmptyHistory,
        _ => StudioStringKeys.ChatEmptyCreate,
    }];

    private static string Fallback(AssistantContext context) => context switch
    {
        AssistantContext.WizardStep2 => StudioStringKeys.ChatReplyStep2,
        AssistantContext.WizardStep3 => StudioStringKeys.ChatReplyStep3,
        AssistantContext.WizardStep4 => StudioStringKeys.ChatReplyStep4,
        AssistantContext.Run => StudioStringKeys.ChatReplyRun,
        AssistantContext.History => StudioStringKeys.ChatReplyHistory,
        _ => StudioStringKeys.ChatReplyStep1,
    };

    private static bool Matches(string question, string pattern)
    {
        if (question.Length == 0 || pattern.Length == 0)
            return false;

        try
        {
            // A malformed pattern is a catalogue mistake, not a reason to swallow the
            // question: it simply does not match, and the fallback answers instead.
            return Regex.IsMatch(
                question, pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(200));
        }
        catch (Exception exception) when (exception is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
