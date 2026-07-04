using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Modal "are you sure you want to quit while a command is running?" dialog.
/// Mirrors the modal pattern used by <see cref="FindDialog"/>: instantiate, call
/// <see cref="Show"/>, then read <see cref="Confirmed"/>.
/// </summary>
public sealed class QuitConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public QuitConfirmDialog(string message)
    {
        Title = "Quit?";
        Width = 70;
        Height = 9;
        X = Pos.Center();
        Y = Pos.Center();

        var label = new Label
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = 3,
            Text = message,
        };

        var yes = new Button
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Text = "Yes — terminate now",
            IsDefault = false,
        };
        var no = new Button
        {
            X = Pos.Right(yes) + 2,
            Y = Pos.AnchorEnd(2),
            Text = "No — keep running",
            IsDefault = true,
        };

        yes.Accepting += (_, _) => { Confirmed = true; Application.RequestStop(this); };
        no.Accepting += (_, _) => { Confirmed = false; Application.RequestStop(this); };

        Add(label, yes, no);
    }

    /// <summary>
    /// Runs the dialog modally; returns once the user clicks Yes or No.
    /// Read <see cref="Confirmed"/> to learn the choice.
    /// </summary>
    public bool Show()
    {
        Application.Run(this);
        return Confirmed;
    }
}
