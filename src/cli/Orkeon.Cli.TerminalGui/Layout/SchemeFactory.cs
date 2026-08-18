using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Builds an opinionated <see cref="Scheme"/> with explicit fg/bg attributes.
/// Workaround for Terminal.Gui 2.1.0 default schemes that paint gray-on-gray
/// in many terminals, making text invisible until selected.
/// </summary>
public static class SchemeFactory
{
    /// <summary>Standard pane scheme: white text on black, focused border in cyan.</summary>
    public static Scheme Pane() => Build(
        fg: ColorName16.White,
        bg: ColorName16.Black,
        focusFg: ColorName16.BrightCyan,
        focusBg: ColorName16.Black,
        hotFg: ColorName16.BrightYellow);

    /// <summary>Editable input scheme: bright fg + dark bg, blue bg when focused.</summary>
    public static Scheme Editable() => Build(
        fg: ColorName16.White,
        bg: ColorName16.Black,
        focusFg: ColorName16.White,
        focusBg: ColorName16.Blue,
        hotFg: ColorName16.BrightYellow);

    // ── Fidelity palette (observed on the reference captures, PLAN §1.3) ──
    //
    // RGB attributes: Terminal.Gui downsamples to the driver's capability, so a
    // 16-color terminal gets the nearest ANSI color instead of nothing. The base
    // panes keep the ColorName16 schemes above (their contrast is already tuned);
    // only the fidelity accents use the sampled palette.

    private static readonly Color Bg = new(0x0d, 0x0d, 0x0d);
    private static readonly Color DimFg = new(0x8a, 0x8a, 0x8a);
    private static readonly Color AccentFg = new(0xe8, 0x65, 0x4a);   // ✱ gerund, ❯ prompt
    private static readonly Color PostureFg = new(0xd3, 0x3f, 0xb0);  // ▶▶ bypass permissions
    private static readonly Color OkFg = new(0x3f, 0xb9, 0x50);       // /rc, notification ●
    private static readonly Color ChipAgentBg = new(0xd4, 0xa7, 0x2c);   // @agent target
    private static readonly Color ChipSessionBg = new(0x58, 0xa6, 0xff); // session/branch

    /// <summary>Secondary text: placeholders, tips, parenthesised metrics.</summary>
    public static Scheme Dim() => Uniform(DimFg, Bg);

    /// <summary>Warm accent: the status-line asterisk + gerund, the prompt marker.</summary>
    public static Scheme Accent() => Uniform(AccentFg, Bg);

    /// <summary>Permission posture in the hint bar.</summary>
    public static Scheme Posture() => Uniform(PostureFg, Bg);

    /// <summary>Green accents: /rc, completion notices.</summary>
    public static Scheme Ok() => Uniform(OkFg, Bg);

    /// <summary>Context chip + its rule: agent-target (yellow) or session (blue).</summary>
    public static Scheme Chip(bool agentTarget)
        => Uniform(Bg, agentTarget ? ChipAgentBg : ChipSessionBg);

    /// <summary>Rule line matching the chip color, drawn as foreground cells.</summary>
    public static Scheme Rule(bool agentTarget)
        => Uniform(agentTarget ? ChipAgentBg : ChipSessionBg, Bg);

    private static Scheme Uniform(Color fg, Color bg)
    {
        var attr = new Attribute(in fg, in bg);
        return new Scheme
        {
            Normal = attr,
            Focus = attr,
            HotNormal = attr,
            HotFocus = attr,
            Active = attr,
            HotActive = attr,
            Highlight = new Attribute(in bg, in fg),
            Editable = attr,
            ReadOnly = attr,
            Disabled = attr,
        };
    }

    private static Scheme Build(ColorName16 fg, ColorName16 bg, ColorName16 focusFg, ColorName16 focusBg, ColorName16 hotFg)
    {
        var fgC = new Color(fg);
        var bgC = new Color(bg);
        var focusFgC = new Color(focusFg);
        var focusBgC = new Color(focusBg);
        var hotFgC = new Color(hotFg);
        return new Scheme
        {
            Normal    = new Attribute(in fgC,      in bgC),
            Focus     = new Attribute(in focusFgC, in focusBgC),
            HotNormal = new Attribute(in hotFgC,   in bgC),
            HotFocus  = new Attribute(in hotFgC,   in focusBgC),
            Active    = new Attribute(in fgC,      in bgC),
            HotActive = new Attribute(in hotFgC,   in bgC),
            Highlight = new Attribute(in bgC,      in fgC),
            Editable  = new Attribute(in fgC,      in bgC),
            ReadOnly  = new Attribute(in fgC,      in bgC),
            Disabled  = new Attribute(in fgC,      in bgC),
        };
    }
}
