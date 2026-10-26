using System.Windows.Input;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// A question of the assistant's, in the panel (refonte R15): the bubble that asks it and its
/// answers as buttons, the recommended one first. Never a card of the main column.
/// </summary>
/// <param name="Text">What the bubble asks; null when the assistant's last bubble already asks it.</param>
/// <param name="Detail">A second line, quieter; empty when there is none.</param>
/// <param name="Replies">The answers, recommended first.</param>
/// <param name="Reveal">The link that brings the work on screen, when the answer leads there (R13).</param>
public sealed record ChatQuestion(string? Text, string Detail, IReadOnlyList<ChatReply> Replies, ICommand? Reveal = null)
{
    /// <summary>Whether the bubble carries its own sentence.</summary>
    public bool HasText => Text is { Length: > 0 };

    /// <summary>Whether the bubble carries a second line.</summary>
    public bool HasDetail => Detail.Length > 0;

    /// <summary>Whether the link to the work shows.</summary>
    public bool HasReveal => Reveal is not null;

    /// <summary>
    /// Whether the question offers choices of its own: the focus then goes to the recommended
    /// one. A question whose one answer is « Skip this question » is answered in the composer.
    /// </summary>
    public bool OffersChoices { get; init; } = true;
}

/// <summary>One answer of a <see cref="ChatQuestion"/>: a secondary button (R4, R15).</summary>
public sealed class ChatReply
{
    /// <summary>Builds the answer over the command it runs.</summary>
    /// <param name="label">The button's text.</param>
    /// <param name="command">What it does: the card's own command, or the answer sent to the engine.</param>
    /// <param name="isRecommended">Whether it is the recommended answer, first and focused.</param>
    public ChatReply(string label, ICommand command, bool isRecommended = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        Label = label;
        IsRecommended = isRecommended;
        Command = command;
        ReplyCommand = new RelayCommand(() => command.Execute(null), () => command.CanExecute(null));
        command.CanExecuteChanged += (_, _) => ReplyCommand.RaiseCanExecuteChanged();
    }

    /// <summary>The button's text.</summary>
    public string Label { get; }

    /// <summary>Whether it is the recommended answer.</summary>
    public bool IsRecommended { get; }

    /// <summary>The command it runs, as the owner gave it.</summary>
    public ICommand Command { get; }

    /// <summary>The button's command: the owner's, run as is.</summary>
    public RelayCommand ReplyCommand { get; }
}

/// <summary>
/// Reads the answers a question of the engine lists. The engine sends free text: its interview
/// asks one question at a time, with a default, in light Markdown, and lists the options as
/// bullets under the question when it offers some. Those bullets are the answers; a question
/// that lists none has no answer to read, and nothing is invented for it.
/// </summary>
internal static class ChatReplyReader
{
    private const int MaxOptions = 6;
    private const int MaxLabel = 60;

    /// <summary>The marks that name the default, in the five languages the assistant speaks.</summary>
    private static readonly string[] RecommendedMarks =
    [
        "(par défaut)", "(recommandé)", "(default)", "(recommended)", "(por defecto)", "(recomendado)",
        "(standard)", "(empfohlen)", "（默认）", "（推荐）", "(默认)", "(推荐)",
    ];

    private static readonly string[] Cuts = [" — ", " – ", " : ", ": ", "："];

    /// <summary>
    /// The options of the last bullet list that follows a line ending with a question mark —
    /// the recommended one first. Empty when the message asks no such question, or when one
    /// bullet is too long to be an answer (a list of criteria to confirm is not a choice).
    /// </summary>
    public static IReadOnlyList<(string Label, bool IsRecommended)> Read(string message)
    {
        var lines = message.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        List<string>? found = null;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsBullet(lines[i]) || !FollowsQuestion(lines, i))
                continue;

            var block = new List<string>();
            while (i < lines.Length && IsBullet(lines[i]))
                block.Add(lines[i++].Trim());
            found = block;
        }

        if (found is null || found.Count < 2 || found.Count > MaxOptions)
            return [];

        var options = new List<(string Label, bool IsRecommended)>();
        foreach (var line in found)
        {
            var (label, recommended) = Label(line);
            if (label.Length == 0 || label.Length > MaxLabel)
                return [];
            options.Add((label, recommended));
        }

        var first = options.FindIndex(option => option.IsRecommended);
        if (first > 0)
        {
            var recommended = options[first];
            options.RemoveAt(first);
            options.Insert(0, recommended);
        }

        return options;
    }

    private static bool IsBullet(string line)
    {
        var text = line.TrimStart();
        if (text.StartsWith("- ", StringComparison.Ordinal) || text.StartsWith("* ", StringComparison.Ordinal)
            || text.StartsWith("• ", StringComparison.Ordinal))
            return true;

        var digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
            digits++;
        return digits is > 0 and < 3 && text.Length > digits + 1
            && text[digits] is '.' or ')' && text[digits + 1] == ' ';
    }

    /// <summary>Whether the nearest line above, past blank ones, asks a question.</summary>
    private static bool FollowsQuestion(string[] lines, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var text = Plain(lines[i]).TrimEnd(' ', ':');
            if (text.Length == 0)
                continue;

            return text.EndsWith('?') || text.EndsWith('？');
        }

        return false;
    }

    private static (string Label, bool IsRecommended) Label(string bullet)
    {
        var text = bullet.TrimStart('-', '*', '•', ' ');
        var digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
            digits++;
        if (digits > 0 && digits + 1 < text.Length && text[digits] is '.' or ')')
            text = text[(digits + 1)..];

        text = Plain(text);
        var recommended = false;
        foreach (var mark in RecommendedMarks)
        {
            var at = text.IndexOf(mark, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                continue;

            recommended = true;
            text = text.Remove(at, mark.Length);
        }

        foreach (var at in Cuts.Select(cut => text.IndexOf(cut, StringComparison.Ordinal)).Where(at => at > 0))
            text = text[..at];

        return (text.Trim().TrimEnd('.', ';', ',', '?', ' '), recommended);
    }

    private static string Plain(string text) =>
        text.Replace("**", "", StringComparison.Ordinal)
            .Replace("__", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal)
            .Trim();
}
