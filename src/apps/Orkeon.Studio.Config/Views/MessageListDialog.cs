using System.Collections.ObjectModel;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
// Aliased under a distinct name: the bare name `Application` binds to the enclosing
// `Orkeon.Application` namespace here, and the alias marks the Terminal.Gui call sites.
using TerminalApp = Terminal.Gui.App.Application;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// The modal used for every list of lines the editor has to show: validation findings,
/// preset guidance, a diagnostic report, an error. The caller chooses the buttons, and
/// gets back which one was pressed.
/// </summary>
internal sealed class MessageListDialog : Window
{
    private readonly ListView _lines;
    private readonly List<Button> _buttons = [];

    /// <summary>Index of the pressed button, or -1 when the dialog was closed another way.</summary>
    public int ChoiceIndex { get; private set; } = -1;

    /// <summary>Builds the modal.</summary>
    /// <param name="title">Window title.</param>
    /// <param name="lines">The lines to show, scrollable.</param>
    /// <param name="buttons">Button labels, left to right; the last one is the default.</param>
    public MessageListDialog(string title, IReadOnlyList<string> lines, IReadOnlyList<string> buttons)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(buttons);

        Title = title;
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(90);
        Height = Dim.Percent(80);

        _lines = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
        };
        _lines.SetSource(new ObservableCollection<string>(lines.Count > 0 ? lines : ["(nothing to report)"]));

        Add(_lines);
        AddButtons(buttons);
    }

    /// <summary>Runs the modal and returns the pressed button index.</summary>
    public int Show()
    {
        TerminalApp.Run(this);
        return ChoiceIndex;
    }

    /// <summary>Shows lines with a single dismiss button.</summary>
    public static void ShowInfo(string title, IReadOnlyList<string> lines)
    {
        using var dialog = new MessageListDialog(title, lines, ["Close"]);
        dialog.Show();
    }

    /// <summary>Shows a single message with a single dismiss button.</summary>
    public static void ShowInfo(string title, string message) => ShowInfo(title, [message]);

    /// <summary>Shows lines with a confirm and a cancel button; true when the user confirmed.</summary>
    public static bool Confirm(string title, IReadOnlyList<string> lines, string confirmLabel, string cancelLabel)
    {
        using var dialog = new MessageListDialog(title, lines, [confirmLabel, cancelLabel]);
        return dialog.Show() == 0;
    }

    private void AddButtons(IReadOnlyList<string> labels)
    {
        View? previous = null;

        for (var i = 0; i < labels.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                X = previous is null ? 1 : Pos.Right(previous) + 2,
                Y = Pos.AnchorEnd(1),
                Text = labels[i],
                IsDefault = i == labels.Count - 1,
            };

            button.Accepting += (_, _) =>
            {
                ChoiceIndex = index;
                TerminalApp.RequestStop(this);
            };

            _buttons.Add(button);
            previous = button;
        }

        Add([.. _buttons]);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lines.Dispose();
            foreach (var button in _buttons)
                button.Dispose();
        }

        base.Dispose(disposing);
    }
}
