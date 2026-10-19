namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Pure composition of the status bar under the prompt:
/// <c>  ⏵⏵ bypass permissions on (shift+tab to cycle) · ctrl+g logs · ↓ tasks        model</c>.
/// The bar is CONTEXTUAL: the interrupt entry appears only while a turn is in flight and the
/// tasks entry only while a task exists — a hint for an action that would do nothing trains
/// the eye to skip the bar.
/// </summary>
public static class HintBarModel
{
    /// <summary>Widest witness text, in columns.</summary>
    public const int WitnessMaxWidth = 24;

    private const string NoModel = "no model";

    /// <summary>The witness text: the model's name, cut to <see cref="WitnessMaxWidth"/> columns with the glyph set's ellipsis, or "no model".</summary>
    public static string Witness(ModelWitnessInfo? info, GlyphSet glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        var model = info?.Model;
        if (string.IsNullOrWhiteSpace(model)) return NoModel;
        return TerminalText.Truncate(model, WitnessMaxWidth, glyphs.Ellipsis);
    }

    /// <summary>The witness colour: green available, amber reconnecting, red unavailable.</summary>
    public static Terminal.Gui.Drawing.Scheme WitnessScheme(Orkeon.Cli.Abstractions.ModelAvailability availability) => availability switch
    {
        Orkeon.Cli.Abstractions.ModelAvailability.Available => SchemeFactory.Ok(),
        Orkeon.Cli.Abstractions.ModelAvailability.Reconnecting => SchemeFactory.Warn(),
        _ => SchemeFactory.Error(),
    };

    /// <summary>Indent of the status bar, in columns.</summary>
    internal const string Indent = "  ";

    /// <summary>
    /// The left part of the status bar, in display order, separated by a dim <c> · </c>.
    /// From the field: the permission mode as a readable label with its cycle hint (yellow
    /// when permissive, dim otherwise), the contextual hints (dim), and <c>↓ tasks</c> (dim)
    /// only while the list has a task other than <c>main</c>. From the focused list:
    /// <c>↑/↓ select · enter view</c> and nothing else — a hint for an action that would do
    /// nothing trains the eye to skip the bar.
    /// </summary>
    /// <param name="glyphs">The glyph set (chevrons, arrows, dot).</param>
    /// <param name="permissionMode">The session's permission mode.</param>
    /// <param name="commandRunning">True while the prompt's turn runs.</param>
    /// <param name="tasksAvailable">True while the tasks list has a task other than <c>main</c>.</param>
    /// <param name="taskListFocused">True while the tasks list has focus.</param>
    internal static IReadOnlyList<RowSegment> Segments(
        GlyphSet glyphs,
        string? permissionMode,
        bool commandRunning,
        bool tasksAvailable,
        bool taskListFocused = false)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        var entries = new List<RowSegment>(4);
        if (taskListFocused)
        {
            entries.Add(new RowSegment($"{glyphs.Up}/{glyphs.Down} select", PaletteRole.Dim));
            entries.Add(new RowSegment("enter view", PaletteRole.Dim));
        }
        else
        {
            var mode = string.IsNullOrWhiteSpace(permissionMode) ? "default" : permissionMode.Trim();
            entries.Add(new RowSegment(
                $"{glyphs.Chevrons} {PostureLabel(mode)} (shift+tab to cycle)",
                IsLoudPosture(mode) ? PaletteRole.Alert : PaletteRole.Dim));
            if (commandRunning) entries.Add(new RowSegment("esc esc to interrupt", PaletteRole.Dim));
            entries.Add(new RowSegment("ctrl+g logs", PaletteRole.Dim));
            if (tasksAvailable) entries.Add(new RowSegment($"{glyphs.Down} tasks", PaletteRole.Dim));
        }

        var segments = new List<RowSegment>(entries.Count * 2) { new(Indent, PaletteRole.Normal) };
        for (var i = 0; i < entries.Count; i++)
        {
            if (i > 0) segments.Add(new RowSegment($" {glyphs.Dot} ", PaletteRole.Dim));
            segments.Add(entries[i]);
        }
        return segments;
    }

    /// <summary>
    /// The readable label of a permission mode: <c>bypass permissions on</c>,
    /// <c>accept edits on</c>, <c>plan mode on</c>, <c>default permissions</c>,
    /// <c>don't ask on</c>; an unknown mode reads as its own name.
    /// </summary>
    internal static string PostureLabel(string? permissionMode) => (permissionMode ?? string.Empty).Trim() switch
    {
        "" or "default" => "default permissions",
        "acceptEdits" => "accept edits on",
        "plan" => "plan mode on",
        "bypassPermissions" => "bypass permissions on",
        "dontAsk" => "don't ask on",
        var other => other,
    };

    /// <summary>
    /// True when the mode is permissive and the label takes the alert (yellow) role: the
    /// modes that remove the approval step for some or all actions — <c>bypassPermissions</c>,
    /// <c>dontAsk</c>, and <c>acceptEdits</c> (file edits go through unasked). <c>default</c>
    /// and <c>plan</c> stay dim: the colour exists to make a relaxed mode impossible to miss.
    /// </summary>
    public static bool IsLoudPosture(string permissionMode) => (permissionMode ?? "").Trim() switch
    {
        "bypassPermissions" or "dontAsk" or "acceptEdits" => true,
        _ => false,
    };
}
