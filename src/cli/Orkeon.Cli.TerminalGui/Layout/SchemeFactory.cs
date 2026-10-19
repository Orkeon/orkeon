using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// The semantic colour roles of the layout. Main text carries no colour (<see cref="Normal"/>),
/// secondary information is <see cref="Dim"/>, and a line holds at most one vivid role.
/// </summary>
public enum PaletteRole
{
    /// <summary>The terminal's own foreground: the transcript, task names, activity phrases.</summary>
    Normal,

    /// <summary>Bright black: separators, hints, placeholders, metrics.</summary>
    Dim,

    /// <summary>The banner accent: the prompt marker, the spinner, the recipient label, the list cursor.</summary>
    Accent,

    /// <summary>Green: a running task, an available model.</summary>
    Ok,

    /// <summary>Yellow: a permissive permission mode, a reconnecting model.</summary>
    Alert,

    /// <summary>Red: a failed or cancelled task, an unavailable model.</summary>
    Error,
}

/// <summary>
/// Builds the <see cref="Scheme"/>s and attributes of the layout from the semantic
/// <see cref="PaletteRole"/>s. Every attribute keeps the terminal's own background
/// (<see cref="Color.None"/>) — the layout no longer paints a fixed background over the
/// user's theme.
/// </summary>
/// <remarks>
/// Colour depth: a terminal that announces 24-bit colour through <c>COLORTERM</c>
/// (<c>truecolor</c> or <c>24bit</c>) gets the sampled RGB palette; any other terminal gets
/// the nearest named ANSI colour, so a 16-colour terminal shows a deliberate palette
/// instead of Terminal.Gui's downsampling guesses.
/// </remarks>
public static class SchemeFactory
{
    /// <summary>The terminal's announcement of its colour depth (not in the Orkeon variables catalog: a terminal standard).</summary>
    private const string ColorTermVariable = "COLORTERM";

    private static readonly Color Default = Color.None;

    // Selection keeps explicit colours: a reversed pair of "terminal defaults" would be invisible.
    private static readonly Color SelectionFg = new(ColorName16.Black);
    private static readonly Color SelectionBg = new(ColorName16.White);

    private static readonly bool TrueColorTerminal = IsTrueColor(ReadColorTerm());

    /// <summary>True when <c>COLORTERM</c> announces 24-bit colour (<c>truecolor</c> or <c>24bit</c>).</summary>
    public static bool IsTrueColor(string? colorTerm)
    {
        var value = (colorTerm ?? string.Empty).Trim();
        return value.Equals("truecolor", StringComparison.OrdinalIgnoreCase)
            || value.Equals("24bit", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The foreground colour of <paramref name="role"/> at the given colour depth.</summary>
    public static Color Foreground(PaletteRole role, bool trueColor) => role switch
    {
        PaletteRole.Dim => trueColor ? new Color(0x8a, 0x8a, 0x8a) : new Color(ColorName16.DarkGray),
        PaletteRole.Accent => trueColor ? new Color(0xe8, 0x65, 0x4a) : new Color(ColorName16.BrightRed),
        PaletteRole.Ok => trueColor ? new Color(0x3f, 0xb9, 0x50) : new Color(ColorName16.Green),
        PaletteRole.Alert => trueColor ? new Color(0xd4, 0xa7, 0x2c) : new Color(ColorName16.Yellow),
        PaletteRole.Error => trueColor ? new Color(0xf8, 0x51, 0x49) : new Color(ColorName16.Red),
        _ => Default,
    };

    /// <summary>The attribute of <paramref name="role"/> on the terminal's background, at this terminal's depth.</summary>
    public static Attribute Attr(PaletteRole role, bool bold = false, bool italic = false)
        => Attr(role, TrueColorTerminal, bold, italic);

    /// <summary>The attribute of <paramref name="role"/> on the terminal's background, at an explicit depth.</summary>
    public static Attribute Attr(PaletteRole role, bool trueColor, bool bold, bool italic)
    {
        var fg = Foreground(role, trueColor);
        var bg = Default;
        var style = (bold ? TextStyle.Bold : TextStyle.None) | (italic ? TextStyle.Italic : TextStyle.None);
        return new Attribute(in fg, in bg, in style);
    }

    /// <summary>Standard pane scheme: the terminal's own colours, no fixed background.</summary>
    public static Scheme Pane() => Uniform(Attr(PaletteRole.Normal));

    /// <summary>Editable input scheme: the terminal's own colours, focused or not — no coloured field.</summary>
    public static Scheme Editable() => Uniform(Attr(PaletteRole.Normal));

    /// <summary>Main text: the terminal's foreground.</summary>
    public static Scheme Normal() => Uniform(Attr(PaletteRole.Normal));

    /// <summary>Secondary text: separators, placeholders, hints, parenthesised metrics.</summary>
    public static Scheme Dim() => Uniform(Attr(PaletteRole.Dim));

    /// <summary>The banner accent: the spinner glyph and verb, the recipient label.</summary>
    public static Scheme Accent() => Uniform(Attr(PaletteRole.Accent));

    /// <summary>Green: a running task, an available model.</summary>
    public static Scheme Ok() => Uniform(Attr(PaletteRole.Ok));

    /// <summary>Yellow: a permissive permission mode.</summary>
    public static Scheme Alert() => Uniform(Attr(PaletteRole.Alert));

    /// <summary>Amber: the model is reconnecting (the <see cref="PaletteRole.Alert"/> role).</summary>
    public static Scheme Warn() => Alert();

    /// <summary>Red: the model is unavailable.</summary>
    public static Scheme Error() => Uniform(Attr(PaletteRole.Error));

    /// <summary>A scheme painting every visual role with <paramref name="attr"/>, selection reversed explicitly.</summary>
    public static Scheme Uniform(Attribute attr) => new()
    {
        Normal = attr,
        Focus = attr,
        HotNormal = attr,
        HotFocus = attr,
        Active = attr,
        HotActive = attr,
        Highlight = new Attribute(in SelectionFg, in SelectionBg),
        Editable = attr,
        ReadOnly = attr,
        Disabled = attr,
    };

    private static string? ReadColorTerm()
    {
        try
        {
            return Environment.GetEnvironmentVariable(ColorTermVariable);
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }
}
