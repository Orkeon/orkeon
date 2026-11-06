namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>A run of a line's text, as a char range, with the role and emphasis it is painted in.</summary>
internal readonly record struct TranscriptRun(int Start, int Length, PaletteRole Role, bool Bold = false, bool Italic = false)
{
    /// <summary>The first char past the run.</summary>
    public int End => Start + Length;
}

/// <summary>
/// A line of the transcript with its display. The text stays the one source for the wrap, the selection and
/// the trim: everything else here only changes how that text is painted or what a copy returns.
/// </summary>
/// <param name="Text">The text shown, on one line.</param>
internal sealed record TranscriptLine(string Text)
{
    /// <summary>The styled runs, sorted and not overlapping. Text outside any run is <see cref="PaletteRole.Normal"/>.</summary>
    public IReadOnlyList<TranscriptRun> Runs { get; init; } = [];

    /// <summary>A role painted over the whole row, past the end of the text too (a prompt's bar, a diff's line).</summary>
    public PaletteRole? RowRole { get; init; }

    /// <summary>Blank columns before the rows after the first, so wrapped text stays aligned under its text column.</summary>
    public int HangingIndent { get; init; }

    /// <summary>What a copy of the whole line returns instead of <see cref="Text"/>; a part of the line copies what is shown.</summary>
    public string? CopySource { get; init; }

    /// <summary>A line of the source with no row of its own (a fence's opening): a copy that goes past this line returns it first.</summary>
    public string? CopyLead { get; init; }

    /// <summary>A line painted in one role.</summary>
    public static TranscriptLine InRole(string text, PaletteRole role)
        => new(text) { Runs = text.Length == 0 ? [] : [new TranscriptRun(0, text.Length, role)] };
}
