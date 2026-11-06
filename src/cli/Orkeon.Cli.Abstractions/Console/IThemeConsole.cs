namespace Orkeon.Cli.Abstractions.Console;

/// <summary>A palette of the split-pane console.</summary>
public enum ConsoleTheme
{
    /// <summary>Colours chosen for a dark terminal.</summary>
    Dark,

    /// <summary>Colours chosen for a light terminal.</summary>
    Light,

    /// <summary>The terminal's own colours: emphasis stays bold and italic, selection stays visible.</summary>
    None,
}

/// <summary>A console whose palette can change while it runs. A console without this capability has no palette.</summary>
public interface IThemeConsole
{
    /// <summary>False for a decorator over a console without palettes: it reads as a console without this capability.</summary>
    bool HasPalette { get; }

    /// <summary>The palette in force.</summary>
    ConsoleTheme Theme { get; }

    /// <summary>Paints everything in <paramref name="theme"/> from now on, what is already on screen included.</summary>
    void SetTheme(ConsoleTheme theme);
}
