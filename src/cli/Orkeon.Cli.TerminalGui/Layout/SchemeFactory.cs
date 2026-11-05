using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// The semantic colour roles of the layout. Main text carries no colour (<see cref="Normal"/>),
/// secondary information is <see cref="Dim"/>, and a line of the layout holds at most one vivid role. Only
/// the roles of rendered content (a prompt's bar, a diff) paint a background: see ADR-017.
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

    /// <summary>The grey bar under what the operator submitted. Carries its own background and foreground.</summary>
    UserBar,

    /// <summary>The background of an added diff line. Carries its own background and foreground.</summary>
    DiffAdded,

    /// <summary>The background of a removed diff line. Carries its own background and foreground.</summary>
    DiffRemoved,

    /// <summary>The words changed on an added line, stronger than <see cref="DiffAdded"/>.</summary>
    AddedWord,

    /// <summary>The words changed on a removed line, stronger than <see cref="DiffRemoved"/>.</summary>
    RemovedWord,

    /// <summary>Inline code in rendered text.</summary>
    Code,

    /// <summary>A file named in rendered text.</summary>
    FileRef,

    /// <summary>The rules that frame a panel or a table.</summary>
    PanelRule,
}

/// <summary>
/// Builds the <see cref="Scheme"/>s and attributes of the layout from the semantic
/// <see cref="PaletteRole"/>s. The layout keeps the terminal's own background
/// (<see cref="Color.None"/>) and paints no fixed background over the user's theme. The roles with a
/// background mark content (<see cref="HasBackground"/>) and set their foreground with it, so they read
/// the same on a dark and on a light terminal.
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
        PaletteRole.UserBar or PaletteRole.DiffAdded or PaletteRole.DiffRemoved
            => trueColor ? new Color(0xe8, 0xe8, 0xe8) : new Color(ColorName16.White),
        PaletteRole.AddedWord or PaletteRole.RemovedWord
            => trueColor ? new Color(0xff, 0xff, 0xff) : new Color(ColorName16.Black),
        PaletteRole.Code => trueColor ? new Color(0x8f, 0xb4, 0xe8) : new Color(ColorName16.Cyan),
        PaletteRole.FileRef => trueColor ? new Color(0x5c, 0xa8, 0xf0) : new Color(ColorName16.BrightBlue),
        PaletteRole.PanelRule => trueColor ? new Color(0x5f, 0x5f, 0x5f) : new Color(ColorName16.DarkGray),
        _ => Default,
    };

    /// <summary>The background of <paramref name="role"/>: the terminal's own, except for the roles of <see cref="HasBackground"/>.</summary>
    public static Color Background(PaletteRole role, bool trueColor) => role switch
    {
        PaletteRole.UserBar => trueColor ? new Color(0x3a, 0x3a, 0x3a) : new Color(ColorName16.DarkGray),
        PaletteRole.DiffAdded => trueColor ? new Color(0x12, 0x3d, 0x22) : new Color(ColorName16.Green),
        PaletteRole.DiffRemoved => trueColor ? new Color(0x4d, 0x1a, 0x1e) : new Color(ColorName16.Red),
        PaletteRole.AddedWord => trueColor ? new Color(0x1f, 0x74, 0x3c) : new Color(ColorName16.BrightGreen),
        PaletteRole.RemovedWord => trueColor ? new Color(0x93, 0x2f, 0x35) : new Color(ColorName16.BrightRed),
        _ => Default,
    };

    /// <summary>Whether <paramref name="role"/> paints its own background.</summary>
    public static bool HasBackground(PaletteRole role)
        => role is PaletteRole.UserBar or PaletteRole.DiffAdded or PaletteRole.DiffRemoved
            or PaletteRole.AddedWord or PaletteRole.RemovedWord;

    /// <summary>
    /// The attribute of a char in <paramref name="role"/> on a row painted in <paramref name="row"/>: the
    /// background of the role when it has one, else the row's; the foreground of the role, the row's for
    /// plain text.
    /// </summary>
    public static Attribute Cell(PaletteRole? row, PaletteRole role, bool trueColor, bool bold, bool italic)
    {
        var fg = role == PaletteRole.Normal && row is { } plainOn ? Foreground(plainOn, trueColor) : Foreground(role, trueColor);
        var bg = !HasBackground(role) && row is { } paintedOn ? Background(paintedOn, trueColor) : Background(role, trueColor);
        var style = (bold ? TextStyle.Bold : TextStyle.None) | (italic ? TextStyle.Italic : TextStyle.None);
        return new Attribute(in fg, in bg, in style);
    }

    /// <summary>As <see cref="Cell(PaletteRole?, PaletteRole, bool, bool, bool)"/>, at this terminal's depth.</summary>
    internal static Attribute Cell(PaletteRole? row, PaletteRole role, bool bold, bool italic)
        => Cell(row, role, TrueColorTerminal, bold, italic);

    /// <summary>The attribute of <paramref name="role"/> at this terminal's depth, on its background (the terminal's for most roles).</summary>
    public static Attribute Attr(PaletteRole role, bool bold = false, bool italic = false)
        => Attr(role, TrueColorTerminal, bold, italic);

    /// <summary>The attribute of <paramref name="role"/> at an explicit depth, on its background (the terminal's for most roles).</summary>
    public static Attribute Attr(PaletteRole role, bool trueColor, bool bold, bool italic)
    {
        var fg = Foreground(role, trueColor);
        var bg = Background(role, trueColor);
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
