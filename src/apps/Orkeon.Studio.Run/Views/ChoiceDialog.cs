using System.Collections.ObjectModel;
using Terminal.Gui.App;
using TerminalApp = Terminal.Gui.App.Application;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Run.Views;

/// <summary>
/// Modal "pick one of these" list. Used wherever the launcher has a closed set of answers:
/// the <c>*.ork.ts</c> scripts of a directory, the two shapes of an ambiguous directory, and
/// the recent-launch list.
/// </summary>
internal sealed class ChoiceDialog : Window
{
    private readonly ListView _list;

    internal ChoiceDialog(string title, string prompt, IReadOnlyList<string> choices, string acceptLabel)
    {
        ArgumentNullException.ThrowIfNull(choices);

        Title = title;
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(90);
        Height = Dim.Percent(70);

        var promptLabel = new Label { X = 1, Y = 0, Width = Dim.Fill(1), Height = 2, Text = prompt };

        _list = new ListView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
        };
        _list.SetSource(new ObservableCollection<string>(choices));

        var accept = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = acceptLabel, IsDefault = true };
        var cancel = new Button { X = Pos.Right(accept) + 2, Y = Pos.AnchorEnd(1), Text = "Cancel" };

        accept.Accepting += (_, _) =>
        {
            SelectedIndex = choices.Count == 0 ? -1 : _list.SelectedItem ?? -1;
            TerminalApp.RequestStop(this);
        };
        cancel.Accepting += (_, _) =>
        {
            SelectedIndex = -1;
            TerminalApp.RequestStop(this);
        };

        Add(promptLabel, _list, accept, cancel);
    }

    /// <summary>Index of the picked choice, or -1 when the dialog was cancelled.</summary>
    internal int SelectedIndex { get; private set; } = -1;

    /// <summary>Runs the dialog modally and returns the picked index, or -1.</summary>
    internal static int Show(string title, string prompt, IReadOnlyList<string> choices, string acceptLabel = "Choose")
    {
        using var dialog = new ChoiceDialog(title, prompt, choices, acceptLabel);
        TerminalApp.Run(dialog, errorHandler: null);
        return dialog.SelectedIndex;
    }
    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Terminal.Gui's base View.Dispose also disposes the Add()-ed subviews; their
            // IsDisposed guard makes these explicit calls idempotent no-ops, and satisfies the
            // "owned IDisposable field" rule the repo builds with.
            _list.Dispose();
        }

        base.Dispose(disposing);
    }

}
