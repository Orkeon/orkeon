using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// One bubble of the thread.
/// <para>
/// A turn the assistant SAID from the catalogue — an interview question, the closing
/// bubble — keeps the key it was said from, not the sentence: switching language has to
/// rewrite the conversation that is already on screen, or half the thread stays in the
/// language it started in. What the USER typed is stored verbatim and never translated:
/// it is their words, not the catalogue's.
/// </para>
/// </summary>
public sealed class ChatTurnViewModel : ObservableObject
{
    private readonly IStudioStrings _strings;
    private readonly string _body;
    private readonly string _detail;
    private readonly string _hint;
    private readonly int? _questionIndex;

    internal ChatTurnViewModel(
        IStudioStrings strings,
        bool isBot,
        string body,
        string detail = "",
        string hint = "",
        int? answerIndex = null,
        int? questionIndex = null,
        bool isClosing = false)
    {
        _strings = strings;
        _body = body;
        _detail = detail;
        _hint = hint;
        _questionIndex = questionIndex;
        IsBot = isBot;
        AnswerIndex = answerIndex;
        IsClosing = isClosing;
    }

    /// <summary>Whether the assistant said it — the side, the avatar and the corner all follow.</summary>
    public bool IsBot { get; }

    /// <summary>Whether the user said it. Bound directly, because XAML has no «not».</summary>
    public bool IsUser => !IsBot;

    /// <summary>The bubble's first line — the question, or what the user typed.</summary>
    public string Body => Catalogued("Body") ?? _body;

    /// <summary>The paragraph under it; empty when the turn has none.</summary>
    public string Detail => Catalogued("Detail") ?? _detail;

    /// <summary>The faint line under that — Novice only; empty when the turn has none.</summary>
    public string Hint => Catalogued("Hint") ?? _hint;

    /// <summary>Whether a detail line exists.</summary>
    public bool HasDetail => Detail.Length > 0;

    /// <summary>Whether a hint line exists.</summary>
    public bool HasHint => Hint.Length > 0;

    /// <summary>
    /// Which interview question this turn answers, when it answers one. The recap reads it:
    /// a fact is «known» because some turn carries its index, not because a counter says so.
    /// </summary>
    public int? AnswerIndex { get; }

    /// <summary>The closing bubble — success ground, said once, when the brief is complete.</summary>
    public bool IsClosing { get; }

    /// <summary>Re-reads whatever this turn takes from the catalogue, after a language switch.</summary>
    internal void Retranslate() =>
        OnPropertiesChanged(nameof(Body), nameof(Detail), nameof(Hint), nameof(HasDetail), nameof(HasHint));

    private string? Catalogued(string part)
    {
        if (_questionIndex is { } index)
            return _strings[$"Vm_Chat_Q{index + 1}_{part}"];

        if (!IsClosing)
            return null;

        return part switch
        {
            "Body" => _strings[StudioStringKeys.ChatWrapBody],
            "Detail" => _strings[StudioStringKeys.ChatWrapDetail],
            _ => "",
        };
    }
}

/// <summary>One line of « Ce que j'ai retenu »: a label, what is known, and whether it is.</summary>
/// <param name="Label">What the fact is called.</param>
/// <param name="Value">What is known — or the «pending» word when nothing is.</param>
/// <param name="IsKnown">Drives the check / question-mark icon and the italic.</param>
public sealed record ChatRecapFact(string Label, string Value, bool IsKnown);
