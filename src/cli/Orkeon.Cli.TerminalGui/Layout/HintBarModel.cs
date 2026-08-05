namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Pure composition of the bottom hint bar:
/// <c> ▶▶ default (shift+tab to cycle) · esc to interrupt · ctrl+g logs · ↓ to manage    /rc</c>.
/// The bar is CONTEXTUAL, like the reference's: the interrupt and agents entries only
/// appear while a turn is in flight — a hint for an action that would do nothing trains
/// the eye to skip the bar.
/// </summary>
public static class HintBarModel
{
    /// <summary>Right-aligned config witness. Green when a usable LLM config was loaded.</summary>
    public const string ConfigWitness = "/rc";

    /// <summary>
    /// The left segments, in display order. The posture segment is first and carries its
    /// cycle hint; the rest are plain action hints joined by the glyph dot.
    /// </summary>
    public static IReadOnlyList<string> LeftSegments(
        string permissionMode,
        bool commandRunning,
        bool agentsPaneAvailable)
    {
        // agentsPaneAvailable is currently unused: the agents pane is informational
        // (not focusable — a focusable read-only pane stole the prompt focus on first
        // live launch), so the bar advertises no "manage" action it cannot honour.
        // The parameter stays so wiring management later is an implementation, not a
        // signature change.
        _ = agentsPaneAvailable;
        var mode = string.IsNullOrWhiteSpace(permissionMode) ? "default" : permissionMode.Trim();
        var segments = new List<string>(3) { $"{mode} (shift+tab to cycle)" };
        if (commandRunning) segments.Add("esc to interrupt");
        segments.Add("ctrl+g logs");
        return segments;
    }

    /// <summary>
    /// True when the posture deserves the loud (magenta) styling: the modes that
    /// remove or bypass the approval step. `default`/`plan` stay quiet — the loud color
    /// exists to make a relaxed posture impossible to miss, not to decorate the bar.
    /// </summary>
    public static bool IsLoudPosture(string permissionMode) => (permissionMode ?? "").Trim() switch
    {
        "bypassPermissions" or "dontAsk" or "acceptEdits" => true,
        _ => false,
    };

    /// <summary>Joins the segments for a plain single-attribute rendering.</summary>
    public static string ComposeLeft(GlyphSet glyphs, IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(segments);
        return $" {glyphs.Chevrons} {string.Join($" {glyphs.Dot} ", segments)}";
    }
}
