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
