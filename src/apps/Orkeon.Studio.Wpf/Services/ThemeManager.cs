using System.Windows;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>
/// Swaps the token dictionary that carries the whole theme. The dictionary is found by its
/// Source (any entry under /Themes/Tokens.) rather than by index, so a reordering of
/// App.xaml's merge list cannot silently corrupt the switch; index 0 stays the fallback slot.
/// </summary>
public static class ThemeManager
{
    /// <summary>Whether the dark token dictionary is currently applied.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Applies the requested theme by swapping the token dictionary in place.</summary>
    public static void Apply(bool dark)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var tokens = new ResourceDictionary
        {
            Source = new Uri($"/Themes/Tokens.{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative),
        };

        for (var i = 0; i < dictionaries.Count; i++)
        {
            if (dictionaries[i].Source is { OriginalString: var source }
                && source.Contains("/Themes/Tokens.", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries[i] = tokens;
                IsDark = dark;
                return;
            }
        }

        if (dictionaries.Count > 0)
        {
            dictionaries[0] = tokens;
        }
        else
        {
            dictionaries.Add(tokens);
        }

        IsDark = dark;
    }
}
