namespace Orkeon.Cli.Abstractions.Console;

/// <summary>One option of a <see cref="ConsoleChoiceRequest"/>.</summary>
/// <param name="Value">What the answer carries when this option is chosen.</param>
/// <param name="Label">What the option shows.</param>
public sealed record ConsoleChoice(string Value, string Label)
{
    /// <summary>A short text aligned in a column after the label.</summary>
    public string? Description { get; init; }

    /// <summary>A short text shown only while the option is selected.</summary>
    public string? Hint { get; init; }

    /// <summary>Whether this is the value in force, marked as such in the list.</summary>
    public bool IsCurrent { get; init; }

    /// <summary>
    /// A letter that selects this option without answering, so that a reply typed from habit (<c>n</c> then Enter)
    /// means what it says rather than confirming whatever the cursor stood on.
    /// </summary>
    public string? Shortcut { get; init; }
}

/// <summary>A second value a panel adjusts sideways, independently of the option selected.</summary>
/// <param name="Label">What the value is about.</param>
/// <param name="Values">The values, in the order the keys walk them.</param>
/// <param name="Value">The value the panel opens on.</param>
public sealed record ConsoleChoiceControl(string Label, IReadOnlyList<string> Values, string Value);

/// <summary>A choice among options, asked in place of a typed line.</summary>
public sealed record ConsoleChoiceRequest
{
    /// <summary>The options, at least one.</summary>
    public required IReadOnlyList<ConsoleChoice> Choices { get; init; }

    /// <summary>The heading of the panel.</summary>
    public string? Title { get; init; }

    /// <summary>A sentence under the heading.</summary>
    public string? Description { get; init; }

    /// <summary>A verbatim text set apart under a side bar: the command an approval asks about.</summary>
    public string? Detail { get; init; }

    /// <summary>The question put just above the options.</summary>
    public string? Question { get; init; }

    /// <summary>The line of keys under the options, in place of the default one.</summary>
    public string? Keys { get; init; }

    /// <summary>A value adjusted sideways, when the choice has one.</summary>
    public ConsoleChoiceControl? Control { get; init; }

    /// <summary>Whether Esc leaves the panel without an answer.</summary>
    public bool Cancellable { get; init; } = true;

    /// <summary>The value of the option selected on opening; failing that, the current one, then the first.</summary>
    public string? DefaultValue { get; init; }
}

/// <summary>What a <see cref="ConsoleChoiceRequest"/> was answered with.</summary>
/// <param name="Value">The value of the option chosen.</param>
/// <param name="ControlValue">The value of the control, when the request had one.</param>
public sealed record ConsoleChoiceAnswer(string Value, string? ControlValue);

/// <summary>
/// A console able to ask a choice with a panel. A console without this capability keeps the numbered lines a
/// typed answer selects from, so the caller tests for it and never for a kind of terminal.
/// </summary>
public interface IChoiceConsole
{
    /// <summary>
    /// Whether a panel can be asked right now. A decorator answers for what it wraps, and a replay answers false
    /// while it runs: its answers are the lines it reads.
    /// </summary>
    bool CanChoose { get; }

    /// <summary>
    /// Asks <paramref name="request"/> and completes with the answer, or with null when the panel was left
    /// without one. Cancelling <paramref name="cancellationToken"/> closes the panel.
    /// </summary>
    Task<ConsoleChoiceAnswer?> ChooseAsync(ConsoleChoiceRequest request, CancellationToken cancellationToken);
}
