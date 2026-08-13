using Terminal.Gui.App;
using TerminalApp = Terminal.Gui.App.Application;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Run.Views;

/// <summary>
/// Modal read-only text panel: the settings resolution chain, the mount-override rules, a
/// validation report. Scrollable, because several of those are paragraphs rather than lines.
/// </summary>
internal sealed class InfoDialog : Window
{
    internal InfoDialog(string title, string text)
    {
        Title = title;
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(90);
        Height = Dim.Percent(70);

        var body = new TextView
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            Text = text,
        };

        var close = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "Close", IsDefault = true };
        close.Accepting += (_, _) => TerminalApp.RequestStop(this);

        Add(body, close);
    }

    /// <summary>Runs the panel modally.</summary>
    internal static void Show(string title, string text)
    {
        using var dialog = new InfoDialog(title, text);
        TerminalApp.Run(dialog, errorHandler: null);
    }

    /// <summary>Runs the panel modally over a set of lines.</summary>
    internal static void Show(string title, IEnumerable<string> lines) =>
        Show(title, string.Join(Environment.NewLine, lines));
}
